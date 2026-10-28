using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Navbar.Services
{
    public sealed class ChatTurn
    {
        public string Role { get; set; } = "user";
        public string Text { get; set; } = "";
    }

    public sealed class ChatConversation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Provider { get; set; } = "claude";
        public string Model { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public bool Pinned { get; set; }
        public List<ChatTurn> Turns { get; set; } = new();
    }

    /// <summary>
    /// Conversations du chat IA, conservées entre deux lancements. Le fichier (%AppData%\Navbar\chats.bin) est chiffré
    /// avec ton compte Windows (DPAPI) : son contenu n'est lisible ni par un autre compte ni en copiant le fichier ailleurs.
    /// </summary>
    public static class ChatStore
    {
        private const int MaxConversations = 60;
        private static string FilePath => Path.Combine(Storage.DataDir, "chats.bin");

        private static List<ChatConversation>? _all;
        public static bool Persist { get; set; } = true;

        /// <summary>Conversation en cours (partagée par toutes les fenêtres).</summary>
        public static ChatConversation? Current { get; set; }

        public static IReadOnlyList<ChatConversation> All
        {
            get { Load(); return _all!.OrderByDescending(c => c.Pinned).ThenByDescending(c => c.UpdatedAt).ToList(); }
        }

        private static void Load()
        {
            if (_all != null) return;
            _all = new List<ChatConversation>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                _all = JsonSerializer.Deserialize<List<ChatConversation>>(Encoding.UTF8.GetString(json)) ?? new();
            }
            catch { _all = new List<ChatConversation>(); }   // fichier d'un autre compte ou corrompu : on repart d'une liste vide
        }

        public static ChatConversation StartNew(string provider, string model)
        {
            Load();
            var conversation = new ChatConversation { Provider = provider, Model = model };
            Current = conversation;
            return conversation;
        }

        /// <summary>Enregistre la conversation courante (créée dans la liste dès son premier message).</summary>
        public static void Commit()
        {
            Load();
            if (Current == null || Current.Turns.Count == 0) return;

            Current.UpdatedAt = DateTime.Now;
            if (Current.Title.Length == 0)
            {
                string first = Current.Turns.First(t => t.Role == "user").Text.Replace('\n', ' ').Trim();
                Current.Title = first.Length > 48 ? first.Substring(0, 47) + "…" : first;
            }
            if (!_all!.Contains(Current)) _all.Add(Current);
            if (_all.Count > MaxConversations)
                foreach (var old in _all.OrderBy(c => c.UpdatedAt).Take(_all.Count - MaxConversations).ToList()) _all.Remove(old);
            Save();
        }

        public static ChatConversation? MostRecent { get { Load(); return _all!.OrderByDescending(c => c.UpdatedAt).FirstOrDefault(); } }

        /// <summary>Enregistre un changement (titre, épingle) fait sur une conversation existante.</summary>
        public static void Touch() { Load(); Save(); }

        public static void Delete(ChatConversation conversation)
        {
            Load();
            _all!.Remove(conversation);
            if (ReferenceEquals(Current, conversation)) Current = null;
            Save();
        }

        public static void DeleteAll()
        {
            Load();
            _all!.Clear();
            Current = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        private static void Save()
        {
            if (!Persist) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_all)), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, bytes);
            }
            catch { /* disque indisponible : les conversations restent en mémoire */ }
        }
    }
}



