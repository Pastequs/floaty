using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record ChatMessage(string Role, string Text);

    public sealed record ChatProvider(string Key, string Label, string DefaultModel, bool NeedsKey, bool Anthropic);

    /// <summary>
    /// Discussion avec une IA en flux continu. Les clés viennent du SecretStore (chiffré DPAPI) et ne partent que vers
    /// le fournisseur choisi. L'historique reste en mémoire : rien n'est écrit sur le disque.
    /// </summary>
    public sealed class ChatService
    {
        public static readonly ChatProvider[] Providers =
        {
            new("claude", "Claude", "claude-sonnet-5-5", true, true),
            new("openai", "OpenAI", "gpt-4o-mini", true, false),
            new("openrouter", "OpenRouter", "openai/gpt-4o-mini", true, false),
            new("custom", "Autre (compatible OpenAI)", "", false, false),   // Ollama, LM Studio, etc.
        };

        public static ChatProvider Find(string key) => Array.Find(Providers, p => p.Key == key) ?? Providers[0];
        public static string SecretName(string providerKey) => "chat." + providerKey;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

        /// <summary>Liste des modèles proposés par le fournisseur (identifiant et nom), récupérée par son API.</summary>
        public async Task<List<(string Id, string Name)>> ListModelsAsync(ChatProvider provider, string customUrl)
        {
            string key = SecretStore.Get(SecretName(provider.Key));
            if (provider.NeedsKey && key.Length == 0) throw new InvalidOperationException("Ajoute d'abord ta clé d'API de ce fournisseur.");

            string url = provider.Key switch
            {
                "claude" => "https://api.anthropic.com/v1/models?limit=100",
                "openai" => "https://api.openai.com/v1/models",
                "openrouter" => "https://openrouter.ai/api/v1/models",
                _ => customUrl.TrimEnd('/') + "/models",
            };
            if (provider.Key == "custom" && customUrl.Length == 0) throw new InvalidOperationException("Adresse du serveur manquante.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (provider.Anthropic) { request.Headers.Add("x-api-key", key); request.Headers.Add("anthropic-version", "2023-06-01"); }
            else if (key.Length > 0) request.Headers.Add("Authorization", "Bearer " + key);

            using var response = await Http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Explain((int)response.StatusCode, body));

            using var doc = JsonDocument.Parse(body);
            var list = new List<(string, string)>();
            foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                string id = m.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                if (id.Length == 0) continue;
                string name = m.TryGetProperty("display_name", out var d) && d.GetString() is { Length: > 0 } dn ? dn
                            : m.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nn ? nn : id;
                list.Add((id, name));
            }
            // OpenAI mêle embeddings, images, audio… : on garde ce qui sert à discuter
            if (provider.Key == "openai")
                list = list.Where(m => !new[] { "embedding", "whisper", "tts", "dall-e", "moderation", "davinci", "babbage", "image", "audio", "realtime", "transcribe" }.Any(x => m.Item1.Contains(x))).ToList();
            return list.OrderBy(m => m.Item1, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Envoie la conversation ; chaque morceau de réponse est remis à onDelta. Lève une exception au message lisible en cas d'échec.</summary>
        public async Task StreamAsync(ChatProvider provider, string model, string customUrl, IReadOnlyList<ChatMessage> history, Action<string> onDelta, CancellationToken ct)
        {
            string key = SecretStore.Get(SecretName(provider.Key));
            if (provider.NeedsKey && key.Length == 0) throw new InvalidOperationException("Aucune clé d'API : ajoute-la dans Réglages › Chat IA.");
            if (model.Length == 0) throw new InvalidOperationException("Aucun modèle choisi : indique-le dans Réglages › Chat IA.");

            string url = provider.Key switch
            {
                "claude" => "https://api.anthropic.com/v1/messages",
                "openai" => "https://api.openai.com/v1/chat/completions",
                "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
                _ => customUrl.TrimEnd('/') + "/chat/completions",
            };
            if (provider.Key == "custom" && customUrl.Length == 0) throw new InvalidOperationException("Adresse du serveur manquante (Réglages › Chat IA).");

            var messages = new List<object>();
            foreach (var m in history) messages.Add(new { role = m.Role, content = m.Text });

            object body = provider.Anthropic
                ? new { model, max_tokens = 1024, stream = true, messages }
                : new { model, stream = true, messages };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            if (provider.Anthropic)
            {
                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (key.Length > 0) request.Headers.Add("Authorization", "Bearer " + key);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(Explain((int)response.StatusCode, error));
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data:")) continue;
                string data = line.Substring(5).Trim();
                if (data.Length == 0 || data == "[DONE]") continue;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;
                    if (provider.Anthropic)
                    {
                        if (root.TryGetProperty("type", out var t) && t.GetString() == "content_block_delta"
                            && root.GetProperty("delta").TryGetProperty("text", out var text))
                            onDelta(text.GetString() ?? "");
                    }
                    else if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                             && choices[0].TryGetProperty("delta", out var delta)
                             && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        onDelta(content.GetString() ?? "");
                }
                catch (JsonException) { /* ligne de contrôle non JSON : ignorée */ }
            }
        }

        private static string Explain(int status, string body)
        {
            string detail = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e))
                    detail = e.ValueKind == JsonValueKind.String ? e.GetString() ?? ""
                           : e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            }
            catch { /* corps non JSON */ }

            string head = status switch
            {
                401 or 403 => "Clé refusée par le fournisseur",
                404 => "Modèle ou adresse introuvable",
                429 => "Trop de requêtes ou crédit épuisé",
                _ => $"Erreur {status}",
            };
            return detail.Length > 0 ? head + " : " + detail : head;
        }
    }
}


