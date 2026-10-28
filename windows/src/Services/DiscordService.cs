using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Navbar.Services
{
    public sealed record GuildInfo(string Id, string Name, string IconUrl);
    public sealed record ChannelInfo(string Id, string Name, int Type);

    public sealed record VoiceMember(string Id, string Name, bool Muted, bool Deafened, string AvatarUrl, bool LocalMuted, int Volume);

    /// <summary>
    /// Vocal Discord via l'IPC local (tube nommé discord-ipc-N) : membres du salon, qui parle, micro / casque / quitter.
    /// Il faut une application créée sur discord.com/developers (identifiant + secret) dont tu es testeur : Discord réserve
    /// le RPC aux applications autorisées. Jeton et secret sont chiffrés (DPAPI) ; la première connexion demande une
    /// autorisation dans la fenêtre Discord.
    /// </summary>
    public sealed class DiscordService
    {
        private const string TokenKey = "discord.token", SecretKey = "discord.secret";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private NamedPipeClientStream? _pipe;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
        private readonly Dictionary<string, VoiceMember> _members = new();
        private readonly HashSet<string> _speaking = new();
        private string _channelId = "";
        private TaskCompletionSource<bool> _ready = new();

        public bool Connected { get; private set; }
        public string Status { get; private set; } = "Discord n'est pas connecté";
        public string ChannelName { get; private set; } = "";
        public bool InChannel => _channelId.Length > 0;   // vrai aussi pour un appel privé, dont le salon n'a pas de nom
        public bool Muted { get; private set; }
        public bool Deafened { get; private set; }
        public double InputVolume { get; private set; } = 100;    // micro, 0 à 100
        public double OutputVolume { get; private set; } = 100;   // haut-parleurs, 0 à 200
        public string MyId { get; private set; } = "";
        public IReadOnlyCollection<VoiceMember> Members => _members.Values.ToList();
        public bool IsSpeaking(string id) => _speaking.Contains(id);
        public event Action? Changed;

        private void Raise(string? status = null)
        {
            if (status != null) Status = status;
            Changed?.Invoke();
        }

        public static bool HasCredentials(string clientId) => clientId.Length > 0 && SecretStore.Has(SecretKey);
        public static void SetSecret(string secret) => SecretStore.Set(SecretKey, secret);
        public static bool HasSecret => SecretStore.Has(SecretKey);
        public static void Forget() => SecretStore.Set(TokenKey, "");

        /// <summary>Tente de se connecter si ce n'est pas déjà fait ; sans effet quand Discord n'est pas lancé.</summary>
        public async Task EnsureConnectedAsync(string clientId)
        {
            if (Connected) return;
            if (clientId.Length == 0) { Raise("Renseigne l'identifiant de l'application (Application ID)."); return; }
            if (!HasSecret) { Raise("Renseigne le secret client (Client Secret)."); return; }
            try { Raise("Connexion à Discord…"); await ConnectAsync(clientId); }
            catch (Exception ex)
            {
                Disconnect();
                Raise(ex switch
                {
                    TimeoutException => "Discord ne répond pas. Si une fenêtre d'autorisation était affichée, accepte-la plus vite ou réessaie.",
                    IOException => "Discord est introuvable (lance-le, puis réessaie).",
                    _ => "Échec : " + ex.Message,
                });
            }
        }

        private async Task ConnectAsync(string clientId)
        {
            NamedPipeClientStream? pipe = null;
            for (int i = 0; i < 10 && pipe == null; i++)
            {
                var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await candidate.ConnectAsync(400); pipe = candidate; }
                catch { candidate.Dispose(); }
            }
            if (pipe == null) throw new IOException("Discord introuvable");
            _pipe = pipe;

            // Discord ignore les commandes envoyées avant son message READY : on attend la fin de la poignée de main
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await WriteFrameAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = clientId });
            _ = Task.Run(ReadLoopAsync);
            if (await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(8))) != _ready.Task) throw new TimeoutException();

            string token = SecretStore.Get(TokenKey);
            if (token.Length == 0)
            {
                Raise("Autorise l'accès dans la fenêtre Discord…");
                token = await AuthorizeAsync(clientId);
            }

            var auth = await CommandAsync("AUTHENTICATE", new JsonObject { ["access_token"] = token });
            if (auth == null)
            {
                // Jeton expiré : on repart de zéro à la prochaine tentative
                SecretStore.Set(TokenKey, "");
                throw new InvalidOperationException("Jeton refusé");
            }
            Connected = true;
            await CommandAsync("SUBSCRIBE", null, "VOICE_CHANNEL_SELECT");
            Raise("Connecté");
            await RefreshChannelAsync();
        }

        private async Task<string> AuthorizeAsync(string clientId)
        {
            var scopes = new JsonArray("rpc", "rpc.voice.read", "rpc.voice.write");
            var granted = await CommandAsync("AUTHORIZE", new JsonObject { ["client_id"] = clientId, ["scopes"] = scopes }, null, TimeSpan.FromMinutes(2));
            string code = granted?["code"]?.GetValue<string>() ?? throw new InvalidOperationException("Autorisation refusée ou identifiant d'application invalide (vérifie l'Application ID)");

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["client_secret"] = SecretStore.Get(SecretKey),
                ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = "http://localhost",
            });
            using var response = await Http.PostAsync("https://discord.com/api/oauth2/token", form);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            string token = json?["access_token"]?.GetValue<string>()
                ?? throw new InvalidOperationException("échange du jeton refusé (" + (json?["error_description"] ?? json?["error"] ?? (int)response.StatusCode) + ") : vérifie le secret et la redirection http://localhost");
            SecretStore.Set(TokenKey, token);
            return token;
        }

        public void Disconnect()
        {
            Connected = false;
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            foreach (var p in _pending.Values) p.TrySetResult(null);
            _pending.Clear();
            _members.Clear();
            _speaking.Clear();
            _channelId = "";
            ChannelName = "";
        }

        // ───────────── Commandes ─────────────

        public async Task SetMuteAsync(bool mute) { await CommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["mute"] = mute }); Muted = mute; Raise(); }
        public async Task SetDeafenAsync(bool deaf) { await CommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["deaf"] = deaf }); Deafened = deaf; Raise(); }
        public async Task SetInputVolumeAsync(double v) { await CommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["input"] = new JsonObject { ["volume"] = v } }); InputVolume = v; }
        public async Task SetOutputVolumeAsync(double v) { await CommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["output"] = new JsonObject { ["volume"] = v } }); OutputVolume = v; }

        /// <summary>Coupe ou rétablit un membre pour toi seul (il n'est pas coupé pour les autres).</summary>
        public async Task SetUserMuteAsync(string id, bool mute)
        {
            await CommandAsync("SET_USER_VOICE_SETTINGS", new JsonObject { ["user_id"] = id, ["mute"] = mute });
            if (_members.TryGetValue(id, out var m)) { _members[id] = m with { LocalMuted = mute }; Raise(); }
        }

        /// <summary>Volume d'un membre pour toi seul, de 0 à 200 %.</summary>
        public async Task SetUserVolumeAsync(string id, int volume)
        {
            volume = Math.Clamp(volume, 0, 200);
            await CommandAsync("SET_USER_VOICE_SETTINGS", new JsonObject { ["user_id"] = id, ["volume"] = volume });
            if (_members.TryGetValue(id, out var m)) { _members[id] = m with { Volume = volume }; Raise(); }
        }

        // ───────────── Serveurs et salons ─────────────

        public async Task<List<GuildInfo>> GetGuildsAsync()
        {
            var data = await CommandAsync("GET_GUILDS", null);
            var list = new List<GuildInfo>();
            foreach (var g in data?["guilds"]?.AsArray() ?? new JsonArray())
                list.Add(new GuildInfo(g?["id"]?.GetValue<string>() ?? "", g?["name"]?.GetValue<string>() ?? "?", g?["icon_url"]?.GetValue<string>() ?? ""));
            return list;
        }

        public async Task<List<ChannelInfo>> GetChannelsAsync(string guildId)
        {
            var data = await CommandAsync("GET_CHANNELS", new JsonObject { ["guild_id"] = guildId });
            var list = new List<ChannelInfo>();
            foreach (var c in data?["channels"]?.AsArray() ?? new JsonArray())
                list.Add(new ChannelInfo(c?["id"]?.GetValue<string>() ?? "", c?["name"]?.GetValue<string>() ?? "?", (int)Num(c?["type"], 0)));
            return list;
        }

        public Task JoinVoiceAsync(string channelId) =>
            CommandAsync("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = channelId, ["force"] = true });

        public async Task LeaveAsync() { await CommandAsync("SELECT_VOICE_CHANNEL", new JsonObject { ["channel_id"] = null }); await RefreshChannelAsync(); }

        private async Task RefreshChannelAsync()
        {
            // Les abonnements du salon précédent tombent avec lui : on les refait pour le nouveau
            var channel = await CommandAsync("GET_SELECTED_VOICE_CHANNEL", null);
            _members.Clear();
            _speaking.Clear();
            _channelId = channel?["id"]?.GetValue<string>() ?? "";
            ChannelName = channel?["name"]?.GetValue<string>() ?? "";

            if (_channelId.Length > 0)
            {
                foreach (var vs in channel!["voice_states"]?.AsArray() ?? new JsonArray()) AddMember(vs);
                foreach (var evt in new[] { "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP" })
                    await CommandAsync("SUBSCRIBE", new JsonObject { ["channel_id"] = _channelId }, evt);
            }
            var settings = await CommandAsync("GET_VOICE_SETTINGS", null);
            Muted = settings?["mute"]?.GetValue<bool>() ?? false;
            Deafened = settings?["deaf"]?.GetValue<bool>() ?? false;
            InputVolume = Num(settings?["input"]?["volume"], 100);
            OutputVolume = Num(settings?["output"]?["volume"], 100);
            Raise(_channelId.Length > 0 ? "En vocal" : "Pas dans un salon vocal");
        }

        private static double Num(JsonNode? n, double fallback)
        {
            try { return n?.GetValue<double>() ?? fallback; } catch { return fallback; }
        }

        private void AddMember(JsonNode? vs)
        {
            var user = vs?["user"];
            string id = user?["id"]?.GetValue<string>() ?? "";
            if (id.Length == 0) return;
            string name = vs?["nick"]?.GetValue<string>() ?? user?["global_name"]?.GetValue<string>() ?? user?["username"]?.GetValue<string>() ?? id;
            var state = vs?["voice_state"];
            string avatar = user?["avatar"]?.GetValue<string>() ?? "";
            _members[id] = new VoiceMember(id, name, state?["mute"]?.GetValue<bool>() == true || state?["self_mute"]?.GetValue<bool>() == true,
                                           state?["deaf"]?.GetValue<bool>() == true || state?["self_deaf"]?.GetValue<bool>() == true,
                                           avatar.Length > 0 ? $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png?size=64" : "",
                                           vs?["mute"]?.GetValue<bool>() == true, (int)Num(vs?["volume"], 100));
        }

        // ───────────── Protocole IPC : 4 octets d'opcode, 4 octets de taille, JSON ─────────────

        private async Task WriteFrameAsync(int opcode, JsonNode payload)
        {
            var pipe = _pipe ?? throw new IOException("Pas de connexion");
            byte[] data = Encoding.UTF8.GetBytes(payload.ToJsonString());
            var frame = new byte[8 + data.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(data.Length).CopyTo(frame, 4);
            data.CopyTo(frame, 8);
            await _writeLock.WaitAsync();
            try { await pipe.WriteAsync(frame); await pipe.FlushAsync(); }
            finally { _writeLock.Release(); }
        }

        private async Task<JsonNode?> CommandAsync(string cmd, JsonObject? args, string? evt = null, TimeSpan? timeout = null)
        {
            string nonce = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[nonce] = tcs;

            var body = new JsonObject { ["cmd"] = cmd, ["nonce"] = nonce, ["args"] = args ?? new JsonObject() };
            if (evt != null) body["evt"] = evt;
            await WriteFrameAsync(1, body);

            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));
            lock (_pending) _pending.Remove(nonce);
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } log)   // diagnostic : noms de commandes seulement
                File.AppendAllText(log, $"rpc {cmd} {(done == tcs.Task ? "answered" : "TIMEOUT")}\n");
#endif
            if (done != tcs.Task) throw new TimeoutException();
            return await tcs.Task;
        }

        private async Task ReadLoopAsync()
        {
            var pipe = _pipe;
            var header = new byte[8];
            try
            {
                while (pipe != null && pipe.IsConnected)
                {
                    if (!await ReadExactAsync(pipe, header)) break;
                    int opcode = BitConverter.ToInt32(header, 0), length = BitConverter.ToInt32(header, 4);
                    var body = new byte[length];
                    if (length > 0 && !await ReadExactAsync(pipe, body)) break;
                    if (opcode == 2) break;   // fermeture demandée par Discord
                    if (opcode == 3) { await WriteFrameAsync(4, JsonNode.Parse(Encoding.UTF8.GetString(body))!); continue; }   // ping → pong

                    var msg = JsonNode.Parse(Encoding.UTF8.GetString(body));
                    string? nonce = msg?["nonce"]?.GetValue<string>();
                    string? evt = msg?["evt"]?.GetValue<string>();
#if DEBUG
                    if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOG") is { Length: > 0 } flog)
                        File.AppendAllText(flog, $"frame op={opcode} cmd={msg?["cmd"]} evt={evt} nonce={(nonce != null)}\n");
#endif

                    if (evt == "READY") _ready.TrySetResult(true);
                    if (evt == "ERROR" && nonce == null) { _ready.TrySetException(new InvalidOperationException(msg?["data"]?["message"]?.GetValue<string>() ?? "Discord a refusé la connexion")); continue; }   // ex. identifiant d'application invalide
                    if (evt == "ERROR") { Complete(nonce, null); continue; }
                    if (nonce != null) { Complete(nonce, msg?["data"] ?? new JsonObject()); continue; }
                    if (msg?["cmd"]?.GetValue<string>() == "DISPATCH") _ = Task.Run(() => OnEventAsync(evt, msg?["data"]));   // hors de la boucle de lecture : elle doit rester libre pour recevoir les réponses
                }
            }
            catch { /* connexion perdue */ }
            if (ReferenceEquals(_pipe, pipe))
            {
                Disconnect();
                Raise("Discord s'est fermé");
            }
        }

        private void Complete(string? nonce, JsonNode? data)
        {
            if (nonce == null) return;
            TaskCompletionSource<JsonNode?>? tcs;
            lock (_pending) _pending.TryGetValue(nonce, out tcs);
            tcs?.TrySetResult(data);
        }

        private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await s.ReadAsync(buffer.AsMemory(read));
                if (n == 0) return false;
                read += n;
            }
            return true;
        }

        private async Task OnEventAsync(string? evt, JsonNode? data)
        {
            switch (evt)
            {
                case "VOICE_CHANNEL_SELECT": await RefreshChannelAsync(); return;
                case "VOICE_STATE_CREATE": case "VOICE_STATE_UPDATE": AddMember(data); break;
                case "VOICE_STATE_DELETE": _members.Remove(data?["user"]?["id"]?.GetValue<string>() ?? ""); break;
                case "SPEAKING_START": _speaking.Add(data?["user_id"]?.GetValue<string>() ?? ""); break;
                case "SPEAKING_STOP": _speaking.Remove(data?["user_id"]?.GetValue<string>() ?? ""); break;
                default: return;
            }
            Raise();
        }
    }
}







