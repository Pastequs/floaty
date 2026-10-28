using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record HaEntity(string Id, string Name, string State)
    {
        public bool On => State is "on" or "open" or "playing" or "home" or "unlocked";

        // Réglages fins des lumières (renseignés quand l'appareil les propose)
        public int? Level { get; init; }       // luminosité 0-100
        public int? Kelvin { get; init; }      // température de couleur
        public int MinK { get; init; } = 2200;
        public int MaxK { get; init; } = 6500;
        public bool CanDim { get; init; }
        public bool CanTemp { get; init; }
        public string Room { get; init; } = "";
        public string Domain => Id.StartsWith("dirigera:") ? (Id.StartsWith("dirigera:scene:") ? "scene" : "dirigera") : Id.Split('.')[0];
    }

    /// <summary>
    /// Domotique via Home Assistant (API REST locale). L'adresse (ex. http://homeassistant.local:8123) est dans les réglages,
    /// le jeton d'accès longue durée est chiffré (DPAPI). Lumières, prises, scènes, volets… s'allument d'un clic.
    /// </summary>
    public sealed record CalendarInfo(string Id, string Name);
    public sealed record CalEvent(string Calendar, string Title, DateTime Start, DateTime End, bool AllDay, string Location, string Link = "");

    public static class HomeAssistant
    {
        private const string TokenKey = "ha.token";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
        public static readonly string[] Domains = { "light", "switch", "scene", "script", "input_boolean", "fan", "cover", "lock" };

        public static bool HasToken => SecretStore.Has(TokenKey);
        public static void SetToken(string token) => SecretStore.Set(TokenKey, token.Trim());

        private static HttpRequestMessage Request(HttpMethod method, string baseUrl, string path)
        {
            string token = SecretStore.Get(TokenKey);
            if (baseUrl.Length == 0) throw new InvalidOperationException("Adresse de Home Assistant manquante (Réglages › Maison).");
            if (token.Length == 0) throw new InvalidOperationException("Jeton d'accès manquant (Réglages › Maison).");
            var r = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return r;
        }

        private static async Task<string> SendAsync(HttpRequestMessage request)
        {
            try
            {
                using var response = await Http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return body;
                throw new InvalidOperationException((int)response.StatusCode == 401 ? "Jeton refusé par Home Assistant." : $"Erreur Home Assistant {(int)response.StatusCode}.");
            }
            catch (HttpRequestException) { throw new InvalidOperationException("Home Assistant est injoignable (adresse, réseau ?)."); }
            catch (TaskCanceledException) { throw new InvalidOperationException("Home Assistant ne répond pas."); }
        }

        private static HaEntity Parse(JsonElement e)
        {
            string id = e.GetProperty("entity_id").GetString() ?? "";
            var attrs = e.GetProperty("attributes");
            string name = attrs.TryGetProperty("friendly_name", out var n) ? n.GetString() ?? id : id;
            string state = e.GetProperty("state").GetString() ?? "";
            if (!id.StartsWith("light.")) return new HaEntity(id, name, state);

            var modes = attrs.TryGetProperty("supported_color_modes", out var sm) && sm.ValueKind == JsonValueKind.Array
                ? sm.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
            int? Num(string key) => attrs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
            int? brightness = Num("brightness");
            return new HaEntity(id, name, state)
            {
                CanDim = modes.Any(m => m != "onoff"),
                CanTemp = modes.Contains("color_temp"),
                Level = brightness == null ? null : (int)Math.Round(brightness.Value * 100.0 / 255),
                Kelvin = Num("color_temp_kelvin"),
                MinK = Num("min_color_temp_kelvin") ?? 2200,
                MaxK = Num("max_color_temp_kelvin") ?? 6500,
            };
        }

        /// <summary>Pièce (zone) de chaque entité, en une seule requête de modèle.</summary>
        private static async Task<Dictionary<string, string>> AreasAsync(string baseUrl, IEnumerable<string> ids)
        {
            var map = new Dictionary<string, string>();
            try
            {
                string list = string.Join(",", ids.Select(i => $"\"{i}\""));
                var r = Request(HttpMethod.Post, baseUrl, "/api/template");
                r.Content = new StringContent(JsonSerializer.Serialize(new { template = "{% for i in [" + list + "] %}{{ i }}={{ area_name(i) or \"\" }};{% endfor %}" }), Encoding.UTF8, "application/json");
                foreach (var pair in (await SendAsync(r)).Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
                }
            }
            catch { /* zones indisponibles : les appareils sont simplement regroupés dans « Autres » */ }
            return map;
        }

        public static async Task SetLightAsync(string baseUrl, HaEntity light, int? percent, int? kelvin)
        {
            var body = new Dictionary<string, object> { ["entity_id"] = light.Id };
            if (percent != null) body["brightness_pct"] = Math.Clamp(percent.Value, 1, 100);
            if (kelvin != null) body["color_temp_kelvin"] = kelvin.Value;
            var r = Request(HttpMethod.Post, baseUrl, "/api/services/light/turn_on");
            r.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            await SendAsync(r);
        }

        public static async Task TurnOffAsync(string baseUrl, HaEntity entity)
        {
            string service = entity.Domain is "scene" or "script" ? "" : entity.Domain == "cover" ? "/api/services/cover/close_cover" : entity.Domain == "lock" ? "/api/services/lock/lock" : $"/api/services/{entity.Domain}/turn_off";
            if (service.Length == 0) return;
            var r = Request(HttpMethod.Post, baseUrl, service);
            r.Content = new StringContent(JsonSerializer.Serialize(new { entity_id = entity.Id }), Encoding.UTF8, "application/json");
            await SendAsync(r);
        }

        /// <summary>Toutes les entités commandables (pour les choisir dans les réglages).</summary>
        public static async Task<List<HaEntity>> ListAsync(string baseUrl)
        {
            using var doc = JsonDocument.Parse(await SendAsync(Request(HttpMethod.Get, baseUrl, "/api/states")));
            return doc.RootElement.EnumerateArray().Select(Parse).Where(e => Domains.Contains(e.Domain)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>État actuel des entités choisies, dans l'ordre demandé.</summary>
        public static async Task<List<HaEntity>> StatesAsync(string baseUrl, IEnumerable<string> ids)
        {
            var wanted = ids.ToList();
            var all = await ListAsync(baseUrl);
            var areas = await AreasAsync(baseUrl, wanted);
            return wanted.Select(id => (all.FirstOrDefault(e => e.Id == id) ?? new HaEntity(id, id, "unavailable")) with { Room = areas.TryGetValue(id, out var a) ? a : "" }).ToList();
        }

        // ───────────── Calendriers (iCloud / Apple via l'intégration CalDAV de Home Assistant, Google, etc.) ─────────────

        public static async Task<List<CalendarInfo>> CalendarsAsync(string baseUrl)
        {
            using var doc = JsonDocument.Parse(await SendAsync(Request(HttpMethod.Get, baseUrl, "/api/calendars")));
            return doc.RootElement.EnumerateArray()
                .Select(c => new CalendarInfo(c.GetProperty("entity_id").GetString() ?? "", c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""))
                .Where(c => c.Id.Length > 0).ToList();
        }

        /// <summary>Événements des calendriers choisis entre deux dates, triés par début.</summary>
        public static async Task<List<CalEvent>> EventsAsync(string baseUrl, IEnumerable<CalendarInfo> calendars, DateTime from, DateTime to)
        {
            string range = $"?start={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}&end={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
            var results = await Task.WhenAll(calendars.Select(async cal =>
            {
                var list = new List<CalEvent>();
                using var doc = JsonDocument.Parse(await SendAsync(Request(HttpMethod.Get, baseUrl, $"/api/calendars/{cal.Id}{range}")));
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var (start, allDay) = ParseWhen(e.GetProperty("start"));
                    var (end, _) = ParseWhen(e.GetProperty("end"));
                    string title = e.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    string place = e.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
                    string description = e.TryGetProperty("description", out var ds) && ds.ValueKind == JsonValueKind.String ? ds.GetString() ?? "" : "";
                    string link = VideoLink.Match(place + " " + description) is { Success: true } vm ? vm.Value.TrimEnd('.', ',', ')') : "";
                    list.Add(new CalEvent(cal.Name.Length > 0 ? cal.Name : cal.Id, title.Length > 0 ? title : "(sans titre)", start, end, allDay, place, link));
                }
                return list;
            }));
            return results.SelectMany(r => r).OrderBy(e => e.Start).ToList();
        }

        /// <summary>Lien de visioconférence (Zoom, Meet, Teams, Webex, FaceTime) dans le lieu ou la description.</summary>
        private static readonly System.Text.RegularExpressions.Regex VideoLink = new(
            @"https?://(?:[\w.-]*zoom\.us|meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|[\w.-]*webex\.com|facetime\.apple\.com|[\w.-]*whereby\.com|meet\.jit\.si)[^\s<>""]*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static (DateTime When, bool AllDay) ParseWhen(JsonElement element)
        {
            if (element.TryGetProperty("dateTime", out var dt) && DateTimeOffset.TryParse(dt.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var offset))
                return (offset.LocalDateTime, false);
            if (element.TryGetProperty("date", out var d) && DateTime.TryParse(d.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
                return (day, true);
            return (DateTime.Now, false);
        }

        public static async Task ToggleAsync(string baseUrl, HaEntity entity)
        {
            string service = entity.Domain switch
            {
                "scene" or "script" => $"/api/services/{entity.Domain}/turn_on",
                "lock" => entity.State == "locked" ? "/api/services/lock/unlock" : "/api/services/lock/lock",
                _ => $"/api/services/{entity.Domain}/toggle",
            };
            var r = Request(HttpMethod.Post, baseUrl, service);
            r.Content = new StringContent(JsonSerializer.Serialize(new { entity_id = entity.Id }), Encoding.UTF8, "application/json");
            await SendAsync(r);
        }
    }
}









