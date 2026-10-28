using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Navbar
{
    /// <summary>
    /// Langue de l'interface (français ou anglais). Les textes du code sont en français ; en anglais, <see cref="T"/> les remplace
    /// d'après le dictionnaire Resources/en.json : phrase entière d'abord, sinon expressions connues (mots entiers, les plus longues d'abord).
    /// </summary>
    internal static class Loc
    {
        public static string Lang { get; private set; } = "fr";
        public static bool English => Lang == "en";

        private static Dictionary<string, string>? _en;
        private static Regex? _phrases;
        private static readonly Dictionary<string, string> Cache = new();

        /// <summary>Applique le réglage « auto », « fr » ou « en » : langue du texte et culture des dates et nombres.</summary>
        public static void Init(string setting)
        {
            Lang = setting is "fr" or "en" ? setting : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
            var culture = CultureInfo.GetCultureInfo(English ? "en-GB" : "fr-FR");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Cache.Clear();
            if (English && _en == null) Load();
        }

        private static void Load()
        {
            _en = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("en.json");
                if (stream == null) return;
                using var doc = JsonDocument.Parse(stream);
                foreach (var p in doc.RootElement.EnumerateObject()) _en[p.Name] = p.Value.GetString() ?? "";
            }
            catch { /* dictionnaire illisible : l'interface reste en français */ }

            // Les expressions (au moins 3 lettres) sont remplaçables à l'intérieur d'un texte ; un mot n'est remplacé que s'il est entier
            var parts = _en.Keys.Where(k => k.Count(char.IsLetter) >= 3).OrderByDescending(k => k.Length).Select(k =>
            {
                string before = char.IsLetterOrDigit(k[0]) ? @"(?<![\p{L}\p{N}])" : "";
                string after = char.IsLetterOrDigit(k[^1]) ? @"(?![\p{L}\p{N}])" : "";
                return before + Regex.Escape(k) + after;
            });
            _phrases = new Regex(string.Join("|", parts), RegexOptions.CultureInvariant);
        }

        private static readonly (Regex Pattern, string Replacement)[] Rules =
        {
            (new Regex(@"il y a (\d+) (min|h|j)\b", RegexOptions.CultureInvariant), "$1 $2 ago"),
            (new Regex(@"il y a moins d'une minute", RegexOptions.CultureInvariant), "less than a minute ago"),
            (new Regex(@"\b(\d+) j (\d+) h\b", RegexOptions.CultureInvariant), "$1 d $2 h"),
            // unités de taille et de débit : Ko, Mo, Go, To (et o/s) deviennent KB, MB, GB, TB (B/s)
            (new Regex(@"(?<=\d) Ko\b", RegexOptions.CultureInvariant), " KB"),
            (new Regex(@"(?<=\d) Mo\b", RegexOptions.CultureInvariant), " MB"),
            (new Regex(@"(?<=\d) Go\b", RegexOptions.CultureInvariant), " GB"),
            (new Regex(@"(?<=\d) To\b", RegexOptions.CultureInvariant), " TB"),
            (new Regex(@"(?<=\d) o/s\b", RegexOptions.CultureInvariant), " B/s"),
        };

        /// <summary>Texte français → texte de la langue choisie (inchangé en français ou sans correspondance).</summary>
        public static string T(string text)
        {
            if (!English || string.IsNullOrEmpty(text) || _en == null || _phrases == null || !text.Any(char.IsLetter)) return text;   // rien à traduire dans une heure ou un nombre
            lock (Cache)
            {
                if (Cache.TryGetValue(text, out var cached)) return cached;
                string result;
                if (_en.TryGetValue(text, out var exact)) result = exact;
                else
                {
                    result = _phrases.Replace(text, m => _en[m.Value]);
                    foreach (var (pattern, replacement) in Rules) result = pattern.Replace(result, replacement);
                }
                if (Cache.Count > 4000) Cache.Clear();
                Cache[text] = result;
                return result;
            }
        }

        /// <summary>Un texte qui ressemble encore à du français (pour repérer les oublis du dictionnaire pendant les tests).</summary>
        public static bool LooksFrench(string text) =>
            Regex.IsMatch(text, @"[àâçéèêëîïôùûüœ]|\b(le|la|les|des|une?|du|de|et|ou|pour|dans|sur|avec|pas|ton|ta|tes|ce|cette|est|sont|vers|aucun|aucune|tout|toute)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
