using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Navbar.Services
{
    public sealed record SearchHit(string Title, string Subtitle, string Target, SearchKind Kind);

    public enum SearchKind { App, File, UwpApp }

    /// <summary>Recherche système : applications du menu Démarrer, fichiers (index Windows Search) et applications récentes.</summary>
    public sealed class SearchService
    {
        private List<SearchHit>? _apps;

        // ───────────── Applications installées ─────────────

        private List<SearchHit> Apps()
        {
            if (_apps != null) return _apps;

            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            };

            var found = new List<SearchHit>();
            foreach (var root in roots.Where(Directory.Exists))
            {
                try
                {
                    foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileNameWithoutExtension(lnk);
                        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("désinstall", StringComparison.OrdinalIgnoreCase)) continue;
                        found.Add(new SearchHit(name, "Application", lnk, SearchKind.App));
                    }
                }
                catch { /* dossier inaccessible */ }
            }

            _apps = found.GroupBy(a => a.Title, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            return _apps;
        }

        public List<SearchHit> SearchApps(string query, int max)
        {
            return Apps()
                .Where(a => a.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(a => a.Title.Length)
                .Take(max)
                .ToList();
        }

        // ───────────── Fichiers : index Windows Search ─────────────

        /// <summary>Interroge l'index de Windows (le même que la recherche de l'Explorateur) via ADO, sans paquet externe.</summary>
        public List<SearchHit> SearchFiles(string query, int max)
        {
            var hits = new List<SearchHit>();
            dynamic? connection = null, recordset = null;
            try
            {
                var type = Type.GetTypeFromProgID("ADODB.Connection");
                if (type == null) return hits;

                connection = Activator.CreateInstance(type)!;
                connection.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");

                string safe = query.Replace("'", "''").Replace("%", "").Replace("[", "");
                string sql = $"SELECT TOP {max} System.ItemName, System.ItemPathDisplay FROM SystemIndex " +
                             $"WHERE System.ItemName LIKE '%{safe}%' AND System.FileExtension <> '.lnk'";
                recordset = connection.Execute(sql);

                while (!recordset.EOF)
                {
                    string name = Convert.ToString(recordset.Fields[0].Value) ?? "";
                    string path = Convert.ToString(recordset.Fields[1].Value) ?? "";
                    if (path.Length > 0) hits.Add(new SearchHit(name, Path.GetDirectoryName(path) ?? path, path, SearchKind.File));
                    recordset.MoveNext();
                }
            }
            catch { /* index désactivé ou indisponible : pas de fichiers */ }
            finally
            {
                try { recordset?.Close(); } catch { }
                try { connection?.Close(); } catch { }
            }
            return hits;
        }

        // ───────────── Applications récentes (UserAssist) ─────────────

        private static readonly Dictionary<string, Environment.SpecialFolder> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SpecialFolder.System,
            ["{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"] = Environment.SpecialFolder.SystemX86,
            ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.SpecialFolder.ProgramFiles,
            ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.SpecialFolder.ProgramFilesX86,
            ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.SpecialFolder.Windows,
            ["{A77F5D77-2E2B-44C3-A6A2-ABA601054A51}"] = Environment.SpecialFolder.Programs,
            ["{0139D44E-6AFE-49F2-8690-3DAFCAE6FFB8}"] = Environment.SpecialFolder.CommonPrograms,
            ["{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}"] = Environment.SpecialFolder.ApplicationData,
            ["{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"] = Environment.SpecialFolder.LocalApplicationData,
            ["{5E6C858F-0E22-4760-9AFE-EA3317B67173}"] = Environment.SpecialFolder.UserProfile,
        };

        private static readonly string[] Ignored =
        {
            "navbar", "explorer", "conhost", "rundll32", "dllhost", "svchost", "searchhost", "startmenuexperiencehost",
            "shellexperiencehost", "msedgewebview2", "uninstall", "unins0", "setup", "update", "installer", "crashhandler",
        };

        /// <summary>Applications lancées récemment, la plus récente d'abord (journal « UserAssist » de Windows).</summary>
        public List<SearchHit> Recents(int count)
        {
            var seen = new Dictionary<string, (DateTime Last, SearchHit Hit)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
                if (root == null) return new List<SearchHit>();

                foreach (var guid in root.GetSubKeyNames())
                {
                    using var counts = root.OpenSubKey(guid + @"\Count");
                    if (counts == null) continue;

                    foreach (var encoded in counts.GetValueNames())
                    {
                        if (counts.GetValue(encoded) is not byte[] data || data.Length < 68) continue;

                        long filetime = BitConverter.ToInt64(data, 60);
                        if (filetime <= 0) continue;
                        var last = DateTime.FromFileTimeUtc(filetime).ToLocalTime();
                        if (last < DateTime.Now.AddDays(-45)) continue;

                        string name = Rot13(encoded);
                        var hit = ToHit(name);
                        if (hit == null) continue;

                        string key = hit.Target;
                        if (!seen.TryGetValue(key, out var existing) || last > existing.Last) seen[key] = (last, hit);
                    }
                }
            }
            catch { /* registre inaccessible : pas de récents */ }

            return seen.Values.OrderByDescending(v => v.Last).Select(v => v.Hit).Take(count).ToList();
        }

        private static string Rot13(string text) => string.Create(text.Length, text, (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                span[i] = c is >= 'a' and <= 'z' ? (char)((c - 'a' + 13) % 26 + 'a')
                        : c is >= 'A' and <= 'Z' ? (char)((c - 'A' + 13) % 26 + 'A')
                        : c;
            }
        });

        private static SearchHit? ToHit(string entry)
        {
            string lower = entry.ToLowerInvariant();
            if (Ignored.Any(lower.Contains)) return null;

            // Application du Store : identifiant « Famille_hash!App »
            if (!entry.Contains('\\') && entry.Contains('!'))
            {
                string package = entry.Split('!')[0].Split('_')[0];
                string title = package.Contains('.') ? package.Substring(package.LastIndexOf('.') + 1) : package;
                return new SearchHit(title, "Application du Store", entry, SearchKind.UwpApp);
            }

            // Dossier connu : « {GUID}\chemin\relatif.exe »
            string path = entry;
            if (entry.StartsWith('{'))
            {
                int end = entry.IndexOf('}');
                if (end < 0) return null;
                if (!KnownFolders.TryGetValue(entry.Substring(0, end + 1), out var folder)) return null;
                path = Path.Combine(Environment.GetFolderPath(folder), entry.Substring(end + 1).TrimStart('\\'));
            }

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(path)) return null;

            string description = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription)) description = info.FileDescription!;
                }
            }
            catch { }

            return new SearchHit(description, Path.GetDirectoryName(path) ?? "", path, SearchKind.App);
        }

        // ───────────── Lancement ─────────────

        public static void Launch(SearchHit hit)
        {
            try
            {
                if (hit.Kind == SearchKind.UwpApp)
                    Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{hit.Target}") { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(hit.Target) { UseShellExecute = true });
            }
            catch { /* application déplacée ou supprimée depuis */ }
        }

        public static void OpenWeb(string query, string engine)
        {
            string q = Uri.EscapeDataString(query);
            string url = engine switch
            {
                "bing" => $"https://www.bing.com/search?q={q}",
                "duckduckgo" => $"https://duckduckgo.com/?q={q}",
                _ => $"https://www.google.com/search?q={q}",
            };
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
