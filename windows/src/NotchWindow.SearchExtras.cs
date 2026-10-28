using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Navbar.Services;

namespace Navbar
{
    // Recherche universelle : notes, tâches, presse-papiers et commandes (« minuteur 10 », « rappelle-moi à 15h … »)
    public partial class NotchWindow
    {
        private static readonly Regex CmdTimer = new(@"^(?:minuteur|timer|chrono)\s+(\d{1,3})\s*(?:m|min|minutes?)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CmdRemindAt = new(@"^(?:rappelle[- ]moi|rappel)\s+(?:à\s+|a\s+)?(\d{1,2})\s*[:h]\s*(\d{2})?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CmdRemindIn = new(@"^(?:rappelle[- ]moi|rappel)\s+dans\s+(\d{1,3})\s*(?:m|min|minutes?)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CmdNote = new(@"^note\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CmdTask = new(@"^(?:t[aâ]che|todo)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string Snippet(string text, string query)
        {
            string flat = string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            int i = flat.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) return flat.Length > 70 ? flat.Substring(0, 70) + "…" : flat;
            int start = Math.Max(0, i - 20);
            string s = flat.Substring(start, Math.Min(flat.Length - start, 80));
            return (start > 0 ? "…" : "") + s + (start + s.Length < flat.Length ? "…" : "");
        }

        /// <summary>Commandes reconnues dans la saisie, puis notes, tâches et copies qui contiennent le texte.</summary>
        private List<SearchHit> ExtraHits(string query)
        {
            var hits = new List<SearchHit>();

            // Commandes : la première ligne du résultat, validée par Entrée
            Match m;
            if ((m = CmdTimer.Match(query)).Success)
                hits.Add(new SearchHit($"Lancer un minuteur de {m.Groups[1].Value} min", "Commande · Entrée pour démarrer", "cmd:timer:" + m.Groups[1].Value, SearchKind.File));
            else if ((m = CmdRemindIn.Match(query)).Success)
            {
                string text = m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : "Rappel";
                hits.Add(new SearchHit($"Te rappeler « {text} » dans {m.Groups[1].Value} min", "Commande · crée une tâche avec rappel", $"cmd:remind-in:{m.Groups[1].Value}|{text}", SearchKind.File));
            }
            else if ((m = CmdRemindAt.Match(query)).Success)
            {
                int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                if (h < 24 && min < 60)
                {
                    string text = m.Groups[3].Value.Trim().Length > 0 ? m.Groups[3].Value.Trim() : "Rappel";
                    hits.Add(new SearchHit($"Te rappeler « {text} » à {h:00}:{min:00}", "Commande · crée une tâche avec rappel", $"cmd:remind-at:{h}:{min}|{text}", SearchKind.File));
                }
            }
            else if ((m = CmdNote.Match(query)).Success)
                hits.Add(new SearchHit("Nouvelle note : " + m.Groups[1].Value.Trim(), "Commande · Entrée pour l'enregistrer", "cmd:note:" + m.Groups[1].Value.Trim(), SearchKind.File));
            else if ((m = CmdTask.Match(query)).Success)
                hits.Add(new SearchHit("Nouvelle tâche : " + m.Groups[1].Value.Trim(), "Commande · Entrée pour l'ajouter (@14h = rappel)", "cmd:task:" + m.Groups[1].Value.Trim(), SearchKind.File));

            if (query.Length < 2) return hits;

            foreach (var n in Shared.Notes.Where(n => n.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(n => n.Pinned).ThenByDescending(n => n.At).Take(3))
            {
                string first = n.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "(vide)";
                hits.Add(new SearchHit(first, "Note · " + Snippet(n.Text, query), "note:" + n.Id, SearchKind.File));
            }
            foreach (var t in Shared.Tasks.Where(t => t.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(3))
                hits.Add(new SearchHit(t.Text, "Tâche" + (t.Done ? " · faite" : "") + (t.DueAt != null ? " · " + t.DueAt.Value.ToString("dd/MM HH:mm") : ""), "task:" + Shared.Tasks.IndexOf(t), SearchKind.File));
            foreach (var c in Shared.Clipboard.Entries.Where(c => c.Kind == ClipKind.Text && c.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(2))
                hits.Add(new SearchHit(Snippet(c.Text, query), "Presse-papiers · clic pour le remettre", "clip:" + c.Key, SearchKind.File));
            return hits;
        }

        /// <summary>Exécute une cible « cmd: », « note: », « task: » ou « clip: » ; faux si ce n'en est pas une.</summary>
        private bool LaunchExtra(SearchHit hit)
        {
            string target = hit.Target;
            if (target.StartsWith("note:", StringComparison.Ordinal))
            {
                var note = Shared.Notes.FirstOrDefault(n => n.Id == target.Substring(5));
                if (note != null) { SearchBox.Clear(); GoTo(HoverView.Notes); OpenNote(note); }
                return true;
            }
            if (target.StartsWith("task:", StringComparison.Ordinal)) { SearchBox.Clear(); GoTo(HoverView.Tasks); return true; }
            if (target.StartsWith("clip:", StringComparison.Ordinal))
            {
                var entry = Shared.Clipboard.Entries.FirstOrDefault(c => c.Key == target.Substring(5));
                if (entry != null) RestoreClip(entry);
                return true;
            }
            if (!target.StartsWith("cmd:", StringComparison.Ordinal)) return false;

            string[] parts = target.Substring(4).Split(':', 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            string confirm;
            switch (parts[0])
            {
                case "timer":
                    _timer.StartMinutes(int.Parse(arg));
                    confirm = $"Minuteur de {arg} min lancé";
                    break;
                case "remind-in":
                {
                    var bits = arg.Split('|', 2);
                    _tasks.Add(new TaskItem { Text = bits[1], DueAt = DateTime.Now.AddMinutes(int.Parse(bits[0])) });
                    SaveTasks();
                    confirm = $"Rappel dans {bits[0]} min";
                    break;
                }
                case "remind-at":
                {
                    var bits = arg.Split('|', 2);
                    var clock = bits[0].Split(':');
                    var due = DateTime.Today.AddHours(int.Parse(clock[0])).AddMinutes(int.Parse(clock[1]));
                    if (due <= DateTime.Now) due = due.AddDays(1);
                    _tasks.Add(new TaskItem { Text = bits[1], DueAt = due });
                    SaveTasks();
                    confirm = $"Rappel à {due:HH:mm}";
                    break;
                }
                case "note":
                    Shared.Notes.Insert(0, new NoteItem { Text = arg });
                    SaveNotes();
                    confirm = "Note enregistrée";
                    break;
                case "task":
                    _tasks.Add(ParseTask(arg));
                    SaveTasks();
                    confirm = "Tâche ajoutée";
                    break;
                default: return true;
            }
            SearchBox.Clear();
            Shared.ShowEverywhere(new OverlayInfo("\uE73E", 1, confirm, "cmd"));
            return true;
        }
    }
}
