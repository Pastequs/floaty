using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record LyricLine(double Time, string Text);

    /// <summary>Paroles synchronisées via LRCLIB (https://lrclib.net).</summary>
    public sealed class LyricsService
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly Regex Stamp = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Navbar/0.1");
            return c;
        }

        public async Task<IReadOnlyList<LyricLine>> FetchAsync(string title, string artist, double duration)
        {
            try
            {
                string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title)
                           + "&artist_name=" + Uri.EscapeDataString(artist)
                           + (duration > 0 ? "&duration=" + (int)Math.Round(duration) : "");
                using var resp = await Http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    var lines = ParseSynced(await resp.Content.ReadAsStringAsync());
                    if (lines.Count > 0) return lines;
                }

                // Repli : recherche libre (titres de vidéos approximatifs)
                string q = Uri.EscapeDataString((artist + " " + title).Trim());
                using var search = await Http.GetAsync("https://lrclib.net/api/search?q=" + q);
                if (!search.IsSuccessStatusCode) return Array.Empty<LyricLine>();

                using var doc = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var lines = ParseSyncedElement(item);
                    if (lines.Count > 0) return lines;
                }
            }
            catch { /* hors ligne ou service indisponible : pas de paroles */ }
            return Array.Empty<LyricLine>();
        }

        private static List<LyricLine> ParseSynced(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return ParseSyncedElement(doc.RootElement);
        }

        private static List<LyricLine> ParseSyncedElement(JsonElement el)
        {
            var result = new List<LyricLine>();
            if (el.ValueKind != JsonValueKind.Object ||
                !el.TryGetProperty("syncedLyrics", out var s) || s.ValueKind != JsonValueKind.String)
                return result;

            foreach (var raw in s.GetString()!.Split('\n'))
            {
                var m = Stamp.Match(raw);
                if (!m.Success) continue;
                double t = int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                result.Add(new LyricLine(t, raw.Substring(m.Index + m.Length).Trim()));
            }
            return result;
        }
    }
}
