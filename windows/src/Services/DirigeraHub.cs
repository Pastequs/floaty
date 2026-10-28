using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Navbar.Services
{
    /// <summary>
    /// Hub IKEA DIRIGERA en direct (API locale HTTPS, port 8443), sans Home Assistant.
    /// Association par OAuth : on demande un code, tu appuies sur le bouton d'action du hub, puis on échange le code contre un jeton
    /// (chiffré DPAPI). Le certificat du hub est auto-signé : il n'est accepté que pour l'adresse configurée.
    /// Les appareils sont exposés comme des « HaEntity » d'identifiant « dirigera:… » pour partager l'affichage avec Home Assistant.
    /// </summary>
    public static class DirigeraHub
    {
        private const string TokenKey = "dirigera.token";
        public const string Prefix = "dirigera:";

        public static bool HasToken => SecretStore.Has(TokenKey);
        public static void ClearToken() => SecretStore.Set(TokenKey, "");
        public static bool IsHubId(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);

        private static HttpClient Client(string ip) => new(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) => string.Equals(request.RequestUri?.Host, ip, StringComparison.OrdinalIgnoreCase),
        }) { Timeout = TimeSpan.FromSeconds(10) };

        private static string Base(string ip) => $"https://{ip}:8443/v1";

        // ───────────── Association ─────────────

        public sealed record PairingTicket(string Code, string Verifier);

        private static string RandomVerifier()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var bytes = RandomNumberGenerator.GetBytes(128);
            return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
        }

        private static string Challenge(string verifier) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Étape 1 : demande un code. Il faut ensuite appuyer sur le bouton d'action du hub (moins d'une minute).</summary>
        public static async Task<PairingTicket> BeginPairingAsync(string ip)
        {
            string verifier = RandomVerifier();
            string url = $"{Base(ip)}/oauth/authorize?audience=homesmart.local&response_type=code&code_challenge={Challenge(verifier)}&code_challenge_method=S256";
            try
            {
                using var http = Client(ip);
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                return new PairingTicket(doc.RootElement.GetProperty("code").GetString() ?? throw new InvalidOperationException("Code absent"), verifier);
            }
            catch (HttpRequestException) { throw new InvalidOperationException("Hub injoignable : vérifie l'adresse IP et que le PC est sur le même réseau."); }
            catch (TaskCanceledException) { throw new InvalidOperationException("Le hub ne répond pas."); }
        }

        /// <summary>Étape 2 : tant que le bouton n'a pas été pressé, le hub refuse ; on réessaie jusqu'à ce qu'il accepte.</summary>
        public static async Task<bool> TryFinishPairingAsync(string ip, PairingTicket ticket)
        {
            try
            {
                using var http = Client(ip);
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = ticket.Code, ["name"] = "Navbar-" + Environment.MachineName,
                    ["grant_type"] = "authorization_code", ["code_verifier"] = ticket.Verifier,
                });
                using var response = await http.PostAsync($"{Base(ip)}/oauth/token", form);
                if (!response.IsSuccessStatusCode) return false;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                string? token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(token)) return false;
                SecretStore.Set(TokenKey, token);
                return true;
            }
            catch { return false; }
        }

        // ───────────── Appareils ─────────────

        private static HttpRequestMessage Request(HttpMethod method, string ip, string path)
        {
            string token = SecretStore.Get(TokenKey);
            if (ip.Length == 0) throw new InvalidOperationException("Adresse du hub IKEA manquante (Réglages › Maison).");
            if (token.Length == 0) throw new InvalidOperationException("Hub IKEA non associé (Réglages › Maison).");
            var r = new HttpRequestMessage(method, Base(ip) + path);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return r;
        }

        private static async Task<string> SendAsync(string ip, HttpRequestMessage request)
        {
            try
            {
                using var http = Client(ip);
                using var response = await http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return body;
                throw new InvalidOperationException((int)response.StatusCode == 401 ? "Hub IKEA : accès refusé, associe-le de nouveau." : $"Hub IKEA : erreur {(int)response.StatusCode}.");
            }
            catch (HttpRequestException) { throw new InvalidOperationException("Hub IKEA injoignable (adresse, réseau ?)."); }
            catch (TaskCanceledException) { throw new InvalidOperationException("Hub IKEA : pas de réponse."); }
        }

        /// <summary>Lumières, prises, stores et scènes du hub.</summary>
        public static async Task<List<HaEntity>> ListAsync(string ip)
        {
            var list = new List<HaEntity>();

            using (var doc = JsonDocument.Parse(await SendAsync(ip, Request(HttpMethod.Get, ip, "/devices"))))
            {
                foreach (var d in doc.RootElement.EnumerateArray())
                {
                    string type = d.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    if (type is not ("light" or "outlet" or "blinds")) continue;

                    var attrs = d.GetProperty("attributes");
                    string name = attrs.TryGetProperty("customName", out var n) && n.GetString() is { Length: > 0 } custom ? custom : type;
                    bool reachable = !d.TryGetProperty("isReachable", out var r) || r.GetBoolean();
                    bool on = type == "blinds"
                        ? attrs.TryGetProperty("blindsCurrentLevel", out var lvl) && lvl.ValueKind == JsonValueKind.Number && lvl.GetDouble() > 0
                        : attrs.TryGetProperty("isOn", out var isOn) && isOn.ValueKind == JsonValueKind.True;
                    string room = d.TryGetProperty("room", out var rm) && rm.ValueKind == JsonValueKind.Object && rm.TryGetProperty("name", out var rn) ? rn.GetString() ?? "" : "";
                    int? Int(string key) => attrs.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : null;
                    bool light = type == "light";
                    int? kelvinMin = Int("colorTemperatureMin"), kelvinMax = Int("colorTemperatureMax");
                    list.Add(new HaEntity(Prefix + type + ":" + d.GetProperty("id").GetString(), name, !reachable ? "unavailable" : on ? "on" : "off")
                    {
                        Room = room,
                        CanDim = light && attrs.TryGetProperty("lightLevel", out _),
                        Level = light ? Int("lightLevel") : null,
                        CanTemp = light && attrs.TryGetProperty("colorTemperature", out _),
                        Kelvin = light ? Int("colorTemperature") : null,
                        MinK = kelvinMin != null && kelvinMax != null ? Math.Min(kelvinMin.Value, kelvinMax.Value) : 2200,
                        MaxK = kelvinMin != null && kelvinMax != null ? Math.Max(kelvinMin.Value, kelvinMax.Value) : 4000,
                    });
                }
            }

            using (var doc = JsonDocument.Parse(await SendAsync(ip, Request(HttpMethod.Get, ip, "/scenes"))))
            {
                foreach (var s in doc.RootElement.EnumerateArray())
                {
                    string name = s.TryGetProperty("info", out var info) && info.TryGetProperty("name", out var n) ? n.GetString() ?? "Scène" : "Scène";
                    list.Add(new HaEntity(Prefix + "scene:" + s.GetProperty("id").GetString(), name, "off"));
                }
            }
            return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static async Task SetLightAsync(string ip, HaEntity light, int? percent, int? kelvin)
        {
            string id = light.Id.Substring(Prefix.Length).Split(':', 2)[1];
            var attributes = new Dictionary<string, int>();
            if (percent != null) attributes["lightLevel"] = Math.Clamp(percent.Value, 1, 100);
            if (kelvin != null) attributes["colorTemperature"] = kelvin.Value;
            var request = Request(new HttpMethod("PATCH"), ip, $"/devices/{id}");
            request.Content = new StringContent(JsonSerializer.Serialize(new[] { new { attributes } }), Encoding.UTF8, "application/json");
            await SendAsync(ip, request);
        }

        /// <summary>Éteint (ou ferme) sans jamais rallumer : sert au « tout éteindre ».</summary>
        public static async Task TurnOffAsync(string ip, HaEntity entity)
        {
            if (entity.Domain == "scene" || !entity.On) return;
            await ToggleAsync(ip, entity);
        }

        /// <summary>Allume/éteint (ou ouvre/ferme un store, ou déclenche une scène).</summary>
        public static async Task ToggleAsync(string ip, HaEntity entity)
        {
            string[] parts = entity.Id.Substring(Prefix.Length).Split(':', 2);
            string kind = parts[0], id = parts.Length > 1 ? parts[1] : "";

            HttpRequestMessage request;
            if (kind == "scene")
                request = Request(HttpMethod.Post, ip, $"/scenes/{id}/trigger");
            else
            {
                request = Request(new HttpMethod("PATCH"), ip, $"/devices/{id}");
                object attributes = kind == "blinds"
                    ? new { blindsTargetLevel = entity.On ? 0 : 100 }
                    : new { isOn = !entity.On };
                request.Content = new StringContent(JsonSerializer.Serialize(new[] { new { attributes } }), Encoding.UTF8, "application/json");
            }
            await SendAsync(ip, request);
        }
    }
}


