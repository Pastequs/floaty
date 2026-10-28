using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record WeatherNow(double Temp, double Feels, int Code, double Wind, int Humidity, bool IsDay);
    public sealed record WeatherDay(DateTime Date, int Code, double Max, double Min);
    public sealed record WeatherHour(DateTime Time, double Temp, int Code, int RainPercent, bool IsDay);
    public sealed record WeatherData(string City, WeatherNow Now, IReadOnlyList<WeatherDay> Days, DateTime At,
                                     IReadOnlyList<WeatherHour>? Hours = null, double? Uv = null, int? RainInMin = null, bool RainingNow = false);

    /// <summary>
    /// Météo via Open-Meteo (gratuit, sans clé). La position vient de la ville choisie, sinon d'une localisation
    /// approximative par adresse IP (service ipwho.is) : aucune donnée personnelle n'est envoyée en dehors de cette requête.
    /// </summary>
    public sealed class WeatherService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

        public async Task<WeatherData?> GetAsync(string city, bool fahrenheit)
        {
            try
            {
                var (lat, lon, name) = await LocateAsync(city);
                string unit = fahrenheit ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "";
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
                    "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,relative_humidity_2m,is_day" +
                    "&hourly=temperature_2m,weather_code,precipitation_probability,is_day&forecast_hours=14&minutely_15=precipitation&forecast_minutely_15=8" +
                    "&daily=weather_code,temperature_2m_max,temperature_2m_min,uv_index_max&timezone=auto&forecast_days=7{2}", lat, lon, unit);

                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var root = doc.RootElement;
                var c = root.GetProperty("current");

                var now = new WeatherNow(
                    c.GetProperty("temperature_2m").GetDouble(), c.GetProperty("apparent_temperature").GetDouble(),
                    c.GetProperty("weather_code").GetInt32(), c.GetProperty("wind_speed_10m").GetDouble(),
                    c.GetProperty("relative_humidity_2m").GetInt32(), c.GetProperty("is_day").GetInt32() == 1);

                var d = root.GetProperty("daily");
                var days = new List<WeatherDay>();
                int count = d.GetProperty("time").GetArrayLength();
                for (int i = 0; i < count; i++)
                {
                    days.Add(new WeatherDay(
                        DateTime.Parse(d.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture),
                        d.GetProperty("weather_code")[i].GetInt32(),
                        d.GetProperty("temperature_2m_max")[i].GetDouble(),
                        d.GetProperty("temperature_2m_min")[i].GetDouble()));
                }
                // Heure par heure : les 14 prochaines heures
                var hours = new List<WeatherHour>();
                if (root.TryGetProperty("hourly", out var h))
                {
                    int n = h.GetProperty("time").GetArrayLength();
                    for (int i = 0; i < n; i++)
                    {
                        var time = DateTime.Parse(h.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture);
                        if (time < DateTime.Now.AddMinutes(-59)) continue;
                        hours.Add(new WeatherHour(time, h.GetProperty("temperature_2m")[i].GetDouble(), h.GetProperty("weather_code")[i].GetInt32(),
                            h.GetProperty("precipitation_probability")[i].ValueKind == JsonValueKind.Number ? h.GetProperty("precipitation_probability")[i].GetInt32() : 0,
                            h.GetProperty("is_day")[i].GetInt32() == 1));
                    }
                }

                // Pluie dans les deux prochaines heures (relevés de 15 minutes)
                int? rainIn = null; bool raining = false;
                if (root.TryGetProperty("minutely_15", out var q))
                {
                    int n = q.GetProperty("time").GetArrayLength();
                    for (int i = 0; i < n; i++)
                    {
                        var time = DateTime.Parse(q.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture);
                        if (q.GetProperty("precipitation")[i].ValueKind != JsonValueKind.Number || q.GetProperty("precipitation")[i].GetDouble() < 0.1) continue;
                        int minutes = (int)Math.Max(0, (time - DateTime.Now).TotalMinutes);
                        if (minutes <= 15) raining = true;
                        rainIn = minutes;
                        break;
                    }
                }

                double? uv = d.TryGetProperty("uv_index_max", out var uvArr) && uvArr.GetArrayLength() > 0 && uvArr[0].ValueKind == JsonValueKind.Number ? uvArr[0].GetDouble() : null;
                return new WeatherData(name, now, days, DateTime.Now, hours, uv, rainIn, raining);
            }
            catch { return null; }
        }

        private static async Task<(double Lat, double Lon, string Name)> LocateAsync(string city)
        {
            if (!string.IsNullOrWhiteSpace(city))
            {
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=fr&name=" + Uri.EscapeDataString(city.Trim());
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var hit = doc.RootElement.GetProperty("results")[0];
                return (hit.GetProperty("latitude").GetDouble(), hit.GetProperty("longitude").GetDouble(), hit.GetProperty("name").GetString() ?? city);
            }

            // Plusieurs services de localisation par IP : certains sont bloqués par les DNS filtrants, on prend le premier qui répond
            foreach (var url in new[] { "https://ifconfig.co/json", "https://ipwho.is/", "https://ipapi.co/json/", "https://ipinfo.io/json" })
            {
                try
                {
                    using var ip = JsonDocument.Parse(await Http.GetStringAsync(url));
                    var r = ip.RootElement;
                    string name = r.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "Ici" : "Ici";

                    if (r.TryGetProperty("latitude", out var la) && r.TryGetProperty("longitude", out var lo))
                        return (la.GetDouble(), lo.GetDouble(), name);
                    if (r.TryGetProperty("loc", out var loc) && loc.GetString() is { } pair && pair.Split(',') is { Length: 2 } xy)
                        return (double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture), name);
                }
                catch { /* service injoignable : suivant */ }
            }
            throw new InvalidOperationException("Position introuvable");
        }

        // ───────────── Codes météo WMO ─────────────

        public static (string Label, string Kind) Describe(int code, bool isDay) => code switch
        {
            0 => (isDay ? "Dégagé" : "Nuit claire", isDay ? "clear" : "night"),
            1 => ("Plutôt dégagé", "partly"),
            2 => ("Partiellement nuageux", "partly"),
            3 => ("Couvert", "cloudy"),
            45 or 48 => ("Brouillard", "fog"),
            >= 51 and <= 57 => ("Bruine", "rain"),
            >= 61 and <= 67 => ("Pluie", "rain"),
            >= 71 and <= 77 => ("Neige", "snow"),
            >= 80 and <= 82 => ("Averses", "rain"),
            85 or 86 => ("Averses de neige", "snow"),
            >= 95 => ("Orage", "storm"),
            _ => ("Variable", "cloudy"),
        };
    }
}



