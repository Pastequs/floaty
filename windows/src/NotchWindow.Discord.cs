using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Navbar.Services;

namespace Navbar
{
    // Discord : appels vocaux (membres, qui parle, micro / casque / volumes / quitter, serveurs et salons vocaux) via l'IPC local
    public partial class NotchWindow
    {
        private bool _callShown;
        private TextBlock? _discordStatus;   // ligne « Statut » des réglages, mise à jour en direct

        // Pictogrammes vectoriels (jeu Lucide, grille 24 × 24, trait de 2)
        private const string IcoMic = "M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v3";
        private const string IcoMicOff = "M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v3 M2 2l20 20";
        private const string IcoHeadphones = "M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3";
        private const string IcoHeadphonesOff = "M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3 M2 2l20 20";
        private const string IcoLeave = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9";
        private const string IcoSliders = "M21 4h-7 M10 4H3 M21 12h-9 M8 12H3 M21 20h-5 M12 20H3 M14 2v4 M8 10v4 M16 18v4";
        private const string IcoList = "M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01";
        private const string IcoSpeaker = "M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z M16 9a5 5 0 0 1 0 6 M19.364 18.364a9 9 0 0 0 0-12.728";
        private const string IcoBack = "M12 19l-7-7 7-7 M19 12H5";
        private const string IcoServer = "M5 3h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M5 13h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z M7 7h.01 M7 17h.01";

        private FrameworkElement Icon(string data, double size, Brush stroke)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data), Stroke = stroke, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            };
            return new Viewbox { Width = size * _scale, Height = size * _scale, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
        }

        private void InitDiscord()
        {
            DiscordToggle.Width = DiscordToggle.Height = 24 * _scale;
            DiscordToggle.Padding = new Thickness(4.5 * _scale);
            DiscordIcon.Fill = FgAlpha(0x99);   // vrai logo Discord (silhouette pleine)
            DiscordTitle.FontSize = 14 * _scale;
            DiscordNote.FontSize = 11 * _scale;
            DiscordNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            DiscordScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);

            Shared.DiscordChanged += OnDiscordChanged;
            Closed += (_, _) => Shared.DiscordChanged -= OnDiscordChanged;
            RenderDiscord();
        }

        private void DiscordToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Discord);

        private void OnDiscordChanged()
        {
            DiscordToggle.Visibility = Shared.DiscordVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_discordStatus != null) _discordStatus.Text = Shared.Discord.Status;
            if (_view == HoverView.Discord && !Shared.DiscordVisible) { GoTo(HoverView.Auto); return; }
            RenderDiscord();

            // Entrée ou sortie d'appel : la vue automatique bascule entre l'appel et média / horloge
            if (_view == HoverView.Auto && InCall != _callShown)
            {
                _callShown = InCall;
                ApplyView();
                if (_hovered) { _smoothResize = true; UpdateState(); _smoothResize = false; }
            }
            _callShown = InCall;
            ResizeForDiscord();
        }

        /// <summary>La hauteur suit le contenu (nombre de membres, mode) tant que la vue Discord est affichée.</summary>
        private void ResizeForDiscord()
        {
            if (!_hovered || !(_view == HoverView.Discord || _view == HoverView.Auto && InCall)) return;
            _smoothResize = true;
            UpdateState();
            _smoothResize = false;
        }

        /// <summary>Bouton rond d'action : icône vectorielle, pleine de couleur quand il est actif (rouge) ou sélectionné (accent).</summary>
        private Border DiscordButton(string icon, string tip, Brush? background, Func<System.Threading.Tasks.Task> action)
        {
            bool filled = background != null;
            var b = new Border
            {
                Child = Icon(icon, 15, filled ? Brushes.White : Fg()),
                Width = 34 * _scale, Height = 28 * _scale, CornerRadius = new CornerRadius(14 * _scale), Cursor = Cursors.Hand, ToolTip = tip,
                Margin = new Thickness(5 * _scale, 0, 0, 0), Background = background ?? FgAlpha(0x1F),
            };
            Press.Attach(b, 0.9);
            var rest = b.Background;
            b.MouseEnter += (_, _) => { if (!filled) b.Background = FgAlpha(0x33); };
            b.MouseLeave += (_, _) => b.Background = rest;
            b.MouseLeftButtonUp += async (_, _) => { try { await action(); } catch { /* Discord a refusé la commande */ } };
            return b;
        }

        private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));

        private void RenderDiscord()
        {
            var d = Shared.Discord;
            DiscordNote.Text = d.Connected && d.InChannel ? (d.ChannelName.Length > 0 ? d.ChannelName : "appel") : "";
            DiscordButtons.Children.Clear();
            DiscordList.Children.Clear();
            if (!d.Connected)
            {
                // Sans le mode avancé (RPC), on lit simplement la fenêtre Discord ouverte sur le PC
                string title = Shared.DiscordTitle;
                string place = title.Length > 0 ? title.Replace(" - Discord", "").Trim() : "";
                bool mentions = place.StartsWith("(") || place.StartsWith("•");
                DiscordNote.Text = Shared.DiscordRunning ? "ouvert" : "";
                DiscordList.Children.Add(new TextBlock
                {
                    Text = place.Length > 0 ? place : "Discord tourne en arrière-plan (zone de notification).",
                    Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                });
                if (mentions)
                    DiscordList.Children.Add(new TextBlock
                    {
                        Text = "Tu as des messages ou mentions non lus.", Foreground = Up, FontSize = 12 * _scale, FontFamily = UiFont,
                        Margin = new Thickness(0, 4 * _scale, 0, 0),
                    });
                var open = new Border
                {
                    Child = new TextBlock { Text = "Ouvrir Discord", Foreground = Fg(), FontSize = 12 * _scale, FontFamily = UiFont },
                    Background = FgAlpha(0x1F), CornerRadius = new CornerRadius(12 * _scale), Cursor = Cursors.Hand,
                    Padding = new Thickness(12 * _scale, 4 * _scale, 12 * _scale, 4 * _scale),
                    Margin = new Thickness(0, 10 * _scale, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                };
                open.MouseLeftButtonUp += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("discord://") { UseShellExecute = true }); } catch { }
                };
                DiscordList.Children.Add(open);
                DiscordList.Children.Add(new TextBlock
                {
                    Text = "Pour voir l'appel et le contrôler : suis la procédure dans Réglages › Discord (3 minutes, une seule fois).",
                    Foreground = FgAlpha(0x66), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8 * _scale, 0, 0),
                });
                return;
            }

            var inCall = d.InChannel;
            DiscordButtons.Children.Add(DiscordButton(d.Muted ? IcoMicOff : IcoMic, d.Muted ? "Réactiver le micro" : "Couper le micro", d.Muted ? Danger : null, () => d.SetMuteAsync(!d.Muted)));
            DiscordButtons.Children.Add(DiscordButton(d.Deafened ? IcoHeadphonesOff : IcoHeadphones, d.Deafened ? "Réactiver le son" : "Couper le son (casque)", d.Deafened ? Danger : null, () => d.SetDeafenAsync(!d.Deafened)));
            DiscordButtons.Children.Add(DiscordButton(IcoSliders, "Volumes du micro et des haut-parleurs", _discordMode == "volumes" ? AccentFill() : null,
                () => { SetDiscordMode(_discordMode == "volumes" ? "call" : "volumes"); return System.Threading.Tasks.Task.CompletedTask; }));
            DiscordButtons.Children.Add(DiscordButton(IcoServer, "Serveurs et salons vocaux", _discordMode is "servers" or "channels" ? AccentFill() : null,
                () => { SetDiscordMode(_discordMode is "servers" or "channels" ? "call" : "servers"); return System.Threading.Tasks.Task.CompletedTask; }));
            if (inCall) DiscordButtons.Children.Add(DiscordButton(IcoLeave, "Quitter le salon vocal", Danger, d.LeaveAsync));

            if (_discordMode is "servers" or "channels") { RenderDiscordServers(); return; }

            if (_discordMode == "volumes")
            {
                var accent = AccentFill();
                var mic = new SliderRow(_scale, "\uE720", accent, Fg(), FgAlpha(0x33));
                mic.SetValue(d.InputVolume / 100.0);
                mic.Committed += v => _ = d.SetInputVolumeAsync(Math.Round(v * 100));
                var speaker = new SliderRow(_scale, "\uE767", accent, Fg(), FgAlpha(0x33));
                speaker.SetValue(d.OutputVolume / 200.0, $"{Math.Round(d.OutputVolume)}%");
                speaker.Committed += v => _ = d.SetOutputVolumeAsync(Math.Round(v * 200));
                DiscordList.Children.Add(mic.Root);
                DiscordList.Children.Add(speaker.Root);
                DiscordList.Children.Add(Hint("Micro : 0 à 100 %. Haut-parleurs : 0 à 200 % (le curseur affiche la part de 200)."));
                return;
            }

            var members = d.Members.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            DiscordNote.Text = inCall ? $"{(d.ChannelName.Length > 0 ? d.ChannelName : "appel")} · {members.Count}" : "";
            if (members.Count == 0)
            {
                DiscordList.Children.Add(Hint(d.Status));
                DiscordList.Children.Add(Hint("Rejoins un salon vocal depuis le bouton serveurs, ou directement dans Discord."));
                return;
            }

            foreach (var m in members)
            {
                bool speaking = d.IsSpeaking(m.Id);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(Avatar(m, speaking));

                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new TextBlock
                {
                    Text = m.Name, Foreground = m.LocalMuted ? FgAlpha(0x66) : Fg(), FontSize = 13 * _scale, FontFamily = UiFont,
                    TextDecorations = m.LocalMuted ? TextDecorations.Strikethrough : null,
                });
                if (m.Volume != 100)
                    name.Children.Add(new TextBlock { Text = m.Volume + " %", Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont });
                row.Children.Add(name);

                if (m.Deafened || m.Muted || m.LocalMuted)
                {
                    var status = Icon(m.Deafened ? IcoHeadphonesOff : IcoMicOff, 13, m.LocalMuted ? Down : FgAlpha(0x88));
                    status.Margin = new Thickness(8 * _scale, 0, 0, 0);
                    status.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(status);
                }

                var card = new Border
                {
                    Child = row, Background = FgAlpha(0x14), CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand,
                    Padding = new Thickness(8 * _scale, 4 * _scale, 10 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                    ToolTip = "Clic : couper ce membre pour toi · Ctrl + molette : son volume",
                };
                card.MouseEnter += (_, _) => card.Background = FgAlpha(0x22);
                card.MouseLeave += (_, _) => card.Background = FgAlpha(0x14);
                var captured = m;
                card.MouseLeftButtonUp += async (_, _) => { try { await d.SetUserMuteAsync(captured.Id, !captured.LocalMuted); } catch { } };
                card.PreviewMouseWheel += async (_, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                    e.Handled = true;
                    try { await d.SetUserVolumeAsync(captured.Id, captured.Volume + (e.Delta > 0 ? 10 : -10)); } catch { }
                };
                DiscordList.Children.Add(card);
            }
        }

        private string _discordMode = "call";   // call, volumes, servers, channels
        private GuildInfo? _discordGuild;
        private List<GuildInfo>? _guilds;
        private List<ChannelInfo>? _channels;
        private static readonly Dictionary<string, ImageSource> AvatarCache = new();

        private void SetDiscordMode(string mode)
        {
            _discordMode = mode;
            RenderDiscord();
            ResizeForDiscord();
        }

        private ImageSource? CachedImage(string url)
        {
            if (url.Length == 0) return null;
            if (!AvatarCache.TryGetValue(url, out var img))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url);
                bmp.DecodePixelWidth = 64;
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                AvatarCache[url] = img = bmp;
            }
            return img;
        }

        /// <summary>Pastille ronde avec la photo du membre (ou son initiale) ; un anneau vert quand il parle.</summary>
        private FrameworkElement Avatar(VoiceMember m, bool speaking)
        {
            double size = 26 * _scale;
            var image = CachedImage(m.AvatarUrl);
            var circle = new Ellipse
            {
                Width = size, Height = size, Fill = image != null ? new ImageBrush(image) { Stretch = Stretch.UniformToFill } : FgAlpha(0x33),
                Stroke = speaking ? Up : Brushes.Transparent, StrokeThickness = 2 * _scale,
            };
            var holder = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, 9 * _scale, 0), VerticalAlignment = VerticalAlignment.Center };
            holder.Children.Add(circle);
            if (image == null)
                holder.Children.Add(new TextBlock
                {
                    Text = m.Name.Length > 0 ? m.Name.Substring(0, 1).ToUpperInvariant() : "?", Foreground = Fg(), FontSize = 12 * _scale, FontWeight = FontWeights.SemiBold,
                    FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
            return holder;
        }

        // ───────────── Serveurs et salons vocaux ─────────────

        private UIElement DiscordCard(string iconData, string text, string sub, Action click, ImageSource? image = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var holder = new Grid { Width = 26 * _scale, Height = 26 * _scale, Margin = new Thickness(0, 0, 9 * _scale, 0), VerticalAlignment = VerticalAlignment.Center };
            if (image != null) holder.Children.Add(new Ellipse { Fill = new ImageBrush(image) { Stretch = Stretch.UniformToFill } });
            else
            {
                holder.Children.Add(new Ellipse { Fill = FgAlpha(0x1F) });
                var ico = Icon(iconData, 13, Fg());
                ico.HorizontalAlignment = HorizontalAlignment.Center;
                ico.VerticalAlignment = VerticalAlignment.Center;
                holder.Children.Add(ico);
            }
            row.Children.Add(holder);

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = text, Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
            if (sub.Length > 0)
                names.Children.Add(new TextBlock { Text = sub, Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont });
            row.Children.Add(names);

            var card = new Border
            {
                Child = row, Background = FgAlpha(0x14), CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand,
                Padding = new Thickness(8 * _scale, 4 * _scale, 10 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
            };
            card.MouseEnter += (_, _) => card.Background = FgAlpha(0x22);
            card.MouseLeave += (_, _) => card.Background = FgAlpha(0x14);
            card.MouseLeftButtonUp += (_, _) => click();
            Press.Attach(card, 0.98);
            return card;
        }

        private async void RenderDiscordServers()
        {
            var d = Shared.Discord;
            if (_discordMode == "servers")
            {
                DiscordNote.Text = "serveurs";
                if (_guilds == null)
                {
                    DiscordList.Children.Add(Hint("Chargement des serveurs…"));
                    try { _guilds = await d.GetGuildsAsync(); } catch { _guilds = new(); }
                    if (_discordMode == "servers") RenderDiscord();
                    return;
                }
                if (_guilds.Count == 0) { DiscordList.Children.Add(Hint("Aucun serveur trouvé.")); return; }
                foreach (var g in _guilds)
                {
                    var guild = g;
                    DiscordList.Children.Add(DiscordCard(IcoServer, g.Name, "", () => { _discordGuild = guild; _channels = null; SetDiscordMode("channels"); }, CachedImage(g.IconUrl)));
                }
                return;
            }

            // Salons vocaux d'un serveur : un clic rejoint
            DiscordNote.Text = _discordGuild?.Name ?? "";
            DiscordList.Children.Add(DiscordCard(IcoBack, "Retour aux serveurs", "", () => SetDiscordMode("servers")));
            if (_discordGuild == null) return;
            if (_channels == null)
            {
                DiscordList.Children.Add(Hint("Chargement des salons…"));
                try { _channels = await d.GetChannelsAsync(_discordGuild.Id); } catch { _channels = new(); }
                if (_discordMode == "channels") RenderDiscord();
                return;
            }
            var voice = _channels.Where(c => c.Type == 2).ToList();
            if (voice.Count == 0) DiscordList.Children.Add(Hint("Aucun salon vocal sur ce serveur."));
            foreach (var c in voice)
            {
                var channel = c;
                DiscordList.Children.Add(DiscordCard(IcoSpeaker, c.Name, "Salon vocal · clic pour rejoindre", async () =>
                {
                    try { await d.JoinVoiceAsync(channel.Id); SetDiscordMode("call"); } catch { }
                }));
            }
        }

        // ───────────── Réglages ─────────────

        private void RenderDiscordSettings()
        {

            SettingsList.Children.Add(Row("Identifiant", SettingsInput(_settings.DiscordClientId, "Client ID de l'application", false, value =>
            {
                value = value.Trim();
                if (value == _settings.DiscordClientId) return;
                _settings.DiscordClientId = value;
                DiscordService.Forget();
                Save();
            })));

            SettingsList.Children.Add(Row("Secret", SettingsInput("", DiscordService.HasSecret ? "Secret enregistré (chiffré)" : "Client Secret", true, value =>
            {
                if (value.Length == 0) return;
                DiscordService.SetSecret(value.Trim());
                DiscordService.Forget();
                RenderSettings();
            })));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Se connecter", false, () =>
            {
                Keyboard.ClearFocus();   // valide d'abord le champ en cours de saisie
                Dispatcher.BeginInvoke(() =>
                {
                    Shared.Discord.Disconnect();
                    _ = Shared.ConnectDiscordAsync(force: true);
                }, System.Windows.Threading.DispatcherPriority.Input);
            }));
            if (DiscordService.HasSecret)
                actions.Children.Add(Chip("Effacer secret et jeton", false, () =>
                {
                    DiscordService.SetSecret(""); DiscordService.Forget(); Shared.Discord.Disconnect(); RenderSettings();
                }));
            SettingsList.Children.Add(Row("Compte", actions));

            _discordStatus = new TextBlock
            {
                Text = Shared.Discord.Status, Foreground = FgAlpha(0x99), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            };
            SettingsList.Children.Add(Row("Statut", _discordStatus));

            var guide = new StackPanel();
            string[] steps =
            {
                "1. Ouvre le portail développeur (bouton ci-dessous) et clique « New Application » : donne-lui le nom que tu veux.",
                "2. Onglet OAuth2 : dans Redirects, ajoute exactement  http://localhost  puis enregistre. Clique « Reset Secret » et copie le Client Secret.",
                "3. Rien à faire pour toi : le propriétaire de l'application est déjà autorisé (App Testers ne sert que pour inviter d'autres comptes). Utilise le même compte Discord que celui ouvert sur le PC.",
                "4. Onglet General Information : copie l'Application ID.",
                "5. Colle l'Application ID dans « Identifiant » et le secret dans « Secret » ci-dessus, puis clique « Se connecter ».",
                "6. Discord affiche une fenêtre d'autorisation : accepte. Ensuite, dès que tu entres en appel, la pastille affiche l'appel toute seule.",
                "Le secret et le jeton sont chiffrés avec ton compte Windows ; Discord doit être ouvert sur ce PC.",
            };
            foreach (var s in steps)
                guide.Children.Add(new TextBlock
                {
                    Text = s, Foreground = FgAlpha(0x99), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3 * _scale),
                });
            var portal = new WrapPanel();
            portal.Children.Add(Chip("Ouvrir le portail développeur", false, () =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.com/developers/applications") { UseShellExecute = true }); } catch { }
            }));
            guide.Children.Add(portal);
            SettingsList.Children.Add(Row("Procédure", guide));
        }
    }
}




