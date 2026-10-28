using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Chat IA : conversations conservées (chiffrées), changement de conversation sans perte, choix du modèle parmi ceux du fournisseur
    public partial class NotchWindow
    {
        private static readonly ChatService Chat = new();
        private static readonly Dictionary<string, List<(string Id, string Name)>> ModelCache = new();
        private CancellationTokenSource? _chatCts;
        private bool _chatBusy;
        private string _chatMode = "chat";   // chat, history, models
        private string _chatModelStatus = "";

        private string ChatModelNow
        {
            get
            {
                var p = ChatService.Find(_settings.ChatProvider);
                return _settings.ChatModels.TryGetValue(p.Key, out var m) && m.Length > 0 ? m : p.DefaultModel;
            }
        }

        private void InitChat()
        {
            ChatToggle.Width = ChatToggle.Height = 24 * _scale;
            ChatToggle.Padding = new Thickness(4.5 * _scale);
            ChatIcon.Stroke = FgAlpha(0x99);
            ChatTitle.FontSize = 14 * _scale;
            ChatNote.FontSize = 11 * _scale;
            ChatNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            ChatNote.TextTrimming = TextTrimming.CharacterEllipsis;
            ChatNote.MaxWidth = 130 * _scale;
            foreach (var (button, text) in new[] { (ChatModelBtn, ChatModelText), (ChatHistBtn, ChatHistText), (ChatNewBtn, ChatNewText) })
            {
                button.CornerRadius = new CornerRadius(12 * _scale);
                button.Padding = new Thickness(10 * _scale, 4 * _scale, 10 * _scale, 4 * _scale);
                button.Margin = new Thickness(5 * _scale, 0, 0, 0);
                button.Background = FgAlpha(0x1F);
                text.FontSize = 12 * _scale;
                Press.Attach(button, 0.94);
            }
            ChatScroll.Margin = new Thickness(0, 6 * _scale, 0, 6 * _scale);
            ChatInputFrame.CornerRadius = new CornerRadius(12 * _scale);
            ChatInput.FontSize = ChatHint.FontSize = 13 * _scale;
            ChatInput.Margin = ChatHint.Margin = new Thickness(12 * _scale, 4 * _scale, 12 * _scale, 4 * _scale);
            Closed += (_, _) => _chatCts?.Cancel();

            // Contexte rapide : le presse-papiers devient une question en un clic
            foreach (var (label, prompt) in new[]
            {
                ("📋 Coller", ""),
                ("Résumer", Loc.T("Résume ce texte en quelques points clairs :")),
                ("Traduire", Loc.T("Traduis ce texte en français :")),
                ("Expliquer", Loc.T("Explique ce texte simplement :")),
            })
            {
                var p = prompt;
                var chip = SmartChipLike(label, () => UseClipboardAsContext(p));
                ((FrameworkElement)chip).Margin = new Thickness(0, 0, 5 * _scale, 4 * _scale);
                ChatQuick.Children.Add(chip);
            }

#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_MD") == "1")
            {
                // Conversation d'exemple (jamais enregistrée) pour vérifier le rendu Markdown
                ChatStore.Current = new ChatConversation { Turns =
                {
                    new ChatTurn { Role = "user", Text = "Comment lister les fichiers ?" },
                    new ChatTurn { Role = "assistant", Text = "## Lister\nUtilise **ls** avec l'option `-la` :\n```bash\nls -la ~/Documents\n```\n- premier point\n- *second* point avec [un lien](https://example.com)\n1. étape un\n2. étape deux" },
                } };
            }
#endif
            ChatStore.Persist = _settings.ChatSave;
            // On reprend la dernière discussion là où elle s'était arrêtée
            if (ChatStore.Current == null && _settings.ChatSave) ChatStore.Current = ChatStore.MostRecent;
            RenderChat();
        }

        private void ChatToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Chat);

        private void SetChatMode(string mode)
        {
            _chatMode = mode;
            ChatInput.Text = "";
            RenderChat();
        }

        private void ChatHist_Click(object sender, MouseButtonEventArgs e) => SetChatMode(_chatMode == "history" ? "chat" : "history");
        private void ChatModel_Click(object sender, MouseButtonEventArgs e) => SetChatMode(_chatMode == "models" ? "chat" : "models");

        private void ChatNew_Click(object sender, MouseButtonEventArgs e)
        {
            _chatCts?.Cancel();
            _chatBusy = false;
            ChatStore.Current = null;   // la conversation reste dans l'historique
            SetChatMode("chat");
        }

        // ───────────── Affichage ─────────────

        private void RenderChat()
        {
            var provider = ChatService.Find(_settings.ChatProvider);
            ChatNote.Text = $"{provider.Label} · {ChatModelNow}";
            ChatNote.ToolTip = ChatNote.Text;
            ChatHistBtn.Background = _chatMode == "history" ? AccentFill() : FgAlpha(0x1F);
            ChatModelBtn.Background = _chatMode == "models" ? AccentFill() : FgAlpha(0x1F);
            var onAccent = new SolidColorBrush(IsLight(EffectiveAccentColor()) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);
            ChatHistText.Foreground = _chatMode == "history" ? onAccent : Fg();
            ChatModelText.Foreground = _chatMode == "models" ? onAccent : Fg();
            ChatHint.Text = _chatMode == "models" ? "Filtrer, ou saisir un identifiant puis Entrée…"
                          : _chatMode == "history" ? (_renamingConv != null ? "Nouveau titre puis Entrée…" : "Rechercher dans les conversations…")
                          : "Pose ta question…";
            ChatQuick.Visibility = _chatMode == "chat" ? Visibility.Visible : Visibility.Collapsed;
            ChatList.Children.Clear();

            switch (_chatMode)
            {
                case "history": RenderChatHistory(); return;
                case "models": RenderChatModels(provider); return;
            }

            var turns = ChatStore.Current?.Turns;
            if (turns == null || turns.Count == 0)
            {
                bool ready = !provider.NeedsKey || SecretStore.Has(ChatService.SecretName(provider.Key));
                ChatList.Children.Add(Hint(ready ? "Pose une question. Tes conversations sont conservées (chiffrées) : « Historique » pour les retrouver."
                                                  : (Loc.English ? $"Add your {provider.Label} API key in Settings › AI chat to get started." : $"Ajoute ta clé d'API {provider.Label} dans Réglages › Chat IA pour commencer.")));
                return;
            }
            foreach (var t in turns) ChatList.Children.Add(Bubble(t.Role == "user", t.Text, false).Border);
            ChatScroll.ScrollToEnd();
        }

        private ChatConversation? _renamingConv;

        private void RenderChatHistory()
        {
            string filter = _renamingConv == null ? ChatInput.Text.Trim() : "";
            var all = ChatStore.All.Where(c => filter.Length == 0 || c.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                               || c.Turns.Any(t => t.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (all.Count == 0) { ChatList.Children.Add(Hint(filter.Length > 0 ? "Aucune conversation ne correspond." : "Aucune conversation enregistrée.")); return; }

            foreach (var conv in all)
            {
                var c = conv;
                bool current = ReferenceEquals(ChatStore.Current, c);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = c.Title, Foreground = Fg(), FontSize = 13 * _scale, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock
                {
                    Text = $"{c.UpdatedAt:dd/MM HH:mm} · {c.Turns.Count} messages · {c.Model}", Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(text);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                void ActionButton(string glyph, string tip, Brush rest, Action click, int column)
                {
                    var b = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 11 * _scale, Foreground = rest, Cursor = Cursors.Hand, Width = 22 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent, ToolTip = tip };
                    b.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
                    Grid.SetColumn(b, column);
                    grid.Children.Add(b);
                }
                ActionButton("\uE718", c.Pinned ? "Désépingler" : "Épingler en tête", c.Pinned ? EffectiveAccent() : FgAlpha(0x55), () => { c.Pinned = !c.Pinned; ChatStore.Touch(); RenderChat(); }, 1);
                ActionButton("\uE70F", "Renommer", FgAlpha(0x55), () =>
                {
                    _renamingConv = c;
                    ChatInput.Text = c.Title;
                    ChatInput.Focus();
                    ChatInput.SelectAll();
                    RenderChat();
                }, 2);
                var remove = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 10 * _scale, Foreground = FgAlpha(0x55), Cursor = Cursors.Hand, Width = 24 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent, ToolTip = "Supprimer cette conversation" };
                remove.MouseEnter += (_, _) => remove.Foreground = Down;
                remove.MouseLeave += (_, _) => remove.Foreground = FgAlpha(0x55);
                remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; ChatStore.Delete(c); RenderChat(); };
                Grid.SetColumn(remove, 3);
                grid.Children.Add(remove);

                var card = new Border
                {
                    Child = grid, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(10 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                    Padding = new Thickness(10 * _scale, 4 * _scale, 4 * _scale, 4 * _scale), Background = current ? FgAlpha(0x26) : FgAlpha(0x14),
                };
                card.MouseEnter += (_, _) => card.Background = FgAlpha(0x2A);
                card.MouseLeave += (_, _) => card.Background = current ? FgAlpha(0x26) : FgAlpha(0x14);
                Press.Attach(card, 0.985);
                card.MouseLeftButtonUp += (_, _) => OpenConversation(c);
                ChatList.Children.Add(card);
            }
        }

        /// <summary>Reprend une conversation : fournisseur et modèle utilisés à l'époque redeviennent ceux du chat.</summary>
        private void OpenConversation(ChatConversation c)
        {
            _chatCts?.Cancel();
            _chatBusy = false;
            ChatStore.Current = c;
            if (ChatService.Providers.Any(p => p.Key == c.Provider))
            {
                _settings.ChatProvider = c.Provider;
                if (c.Model.Length > 0) _settings.ChatModels[c.Provider] = c.Model;
                Save();
            }
            SetChatMode("chat");
        }

        /// <summary>Insère le texte copié (entre balises de code) dans le champ ; avec une consigne, envoie directement.</summary>
        private void UseClipboardAsContext(string instruction)
        {
            string clip;
            try { clip = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { clip = ""; }
            if (string.IsNullOrWhiteSpace(clip))
            {
                Shared.ShowEverywhere(new OverlayInfo("\uE8C8", 1, "Presse-papiers vide : copie d'abord un texte", "clip"));
                return;
            }
            if (clip.Length > 8000) clip = clip.Substring(0, 8000) + "\n[…]";
            string block = "```\n" + clip.Trim() + "\n```";
            ChatInput.Text = instruction.Length > 0 ? instruction + "\n" + block : (ChatInput.Text.Length > 0 ? ChatInput.Text + "\n" : "") + block;
            if (instruction.Length > 0) _ = SendChatAsync();
        }

        // ───────────── Choix du modèle ─────────────

        private void RenderChatModels(ChatProvider provider)
        {
            ChatList.Children.Add(Hint($"Modèles de {provider.Label} · actuel : {ChatModelNow}"));
            if (!ModelCache.TryGetValue(provider.Key, out var models))
            {
                var load = SmartChipLike("Charger la liste des modèles", () => _ = LoadModelsAsync(provider, () => RenderChat()));
                ChatList.Children.Add(load);
                if (_chatModelStatus.Length > 0) ChatList.Children.Add(new TextBlock { Text = _chatModelStatus, Foreground = Down, FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4 * _scale, 0, 0) });
                ChatList.Children.Add(Hint("Tu peux aussi saisir directement un identifiant de modèle ci-dessous puis Entrée."));
                return;
            }

            string filter = ChatInput.Text.Trim();
            var matches = models.Where(m => filter.Length == 0 || m.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
            if (matches.Count == 0) ChatList.Children.Add(Hint("Aucun modèle ne correspond : Entrée pour utiliser « " + filter + " » tel quel."));
            foreach (var m in matches)
            {
                var model = m;
                bool current = model.Id == ChatModelNow;
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = model.Name, Foreground = Fg(), FontSize = 12 * _scale, FontFamily = UiFont, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
                if (model.Name != model.Id) text.Children.Add(new TextBlock { Text = model.Id, Foreground = FgAlpha(0x66), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
                var card = new Border
                {
                    Child = text, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(8 * _scale), Margin = new Thickness(0, 0, 0, 3 * _scale),
                    Padding = new Thickness(10 * _scale, 3 * _scale, 10 * _scale, 3 * _scale), Background = current ? AccentFill() : FgAlpha(0x14),
                };
                if (current) { foreach (var t in text.Children.OfType<TextBlock>()) t.Foreground = new SolidColorBrush(IsLight(EffectiveAccentColor()) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White); }
                else { card.MouseEnter += (_, _) => card.Background = FgAlpha(0x2A); card.MouseLeave += (_, _) => card.Background = FgAlpha(0x14); }
                Press.Attach(card, 0.985);
                card.MouseLeftButtonUp += (_, _) => ChooseModel(provider, model.Id);
                ChatList.Children.Add(card);
            }
            if (models.Count > 40 && filter.Length == 0) ChatList.Children.Add(Hint($"{models.Count} modèles : tape pour filtrer."));
        }

        private UIElement SmartChipLike(string text, Action click)
        {
            var chip = new Border
            {
                Child = new TextBlock { Text = text, Foreground = Fg(), FontSize = 12 * _scale, FontFamily = UiFont },
                Background = FgAlpha(0x1F), CornerRadius = new CornerRadius(12 * _scale), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(11 * _scale, 4 * _scale, 11 * _scale, 4 * _scale), Margin = new Thickness(0, 6 * _scale, 0, 0),
            };
            Press.Attach(chip, 0.94);
            chip.MouseLeftButtonUp += (_, _) => click();
            return chip;
        }

        private async System.Threading.Tasks.Task LoadModelsAsync(ChatProvider provider, Action done)
        {
            _chatModelStatus = "";
            try { ModelCache[provider.Key] = await Chat.ListModelsAsync(provider, _settings.ChatCustomUrl); }
            catch (Exception ex) { _chatModelStatus = ex.Message; }
            done();
        }

        private void ChooseModel(ChatProvider provider, string id)
        {
            _settings.ChatModels[provider.Key] = id.Trim();
            Save();
            if (ChatStore.Current != null) { ChatStore.Current.Model = id.Trim(); ChatStore.Current.Provider = provider.Key; ChatStore.Commit(); }
            SetChatMode("chat");
        }

        // ───────────── Bulles et saisie ─────────────

        /// <summary>Bulle de message : texte simple pour toi et pour les erreurs, Markdown pour les réponses ; Set remplace le contenu (réponse en flux).</summary>
        private (Border Border, Action<string, bool> Set) Bubble(bool user, string text, bool error)
        {
            string latest = text;
            var border = new Border
            {
                CornerRadius = new CornerRadius(12 * _scale), Margin = new Thickness(user ? 40 * _scale : 0, 0, user ? 0 : 40 * _scale, 5 * _scale),
                Padding = new Thickness(10 * _scale, 5 * _scale, 10 * _scale, 5 * _scale),
                Background = user ? FgAlpha(0x26) : FgAlpha(0x12), HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                ToolTip = user ? null : "Clic droit : copier toute la réponse",
            };
            void Set(string t, bool err)
            {
                latest = t;
                border.Child = user || err
                    ? new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 13 * _scale, FontFamily = UiFont, Foreground = err ? new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)) : Fg() }
                    : RenderMarkdown(t);
            }
            Set(text, error);
            if (!user) border.MouseRightButtonUp += (_, _) => { try { Clipboard.SetText(latest); Shared.ShowEverywhere(new OverlayInfo("\uE8C8", 1, "Réponse copiée", "clip")); } catch { } };
            return (border, Set);
        }
        private void ChatInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ChatInput.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            ChatInput.Focus();
        }

        private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            ChatHint.Visibility = ChatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_chatMode == "models" || _chatMode == "history" && _renamingConv == null) RenderChat();   // le champ filtre la liste
        }

        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_renamingConv != null) { _renamingConv = null; ChatInput.Text = ""; RenderChat(); }
                else if (_chatMode != "chat") SetChatMode("chat");
                else Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_chatMode == "history")
                {
                    if (_renamingConv != null && ChatInput.Text.Trim().Length > 0)
                    {
                        _renamingConv.Title = ChatInput.Text.Trim();
                        ChatStore.Touch();
                    }
                    _renamingConv = null;
                    ChatInput.Text = "";
                    RenderChat();
                    return;
                }
                if (_chatMode == "models")
                {
                    string typed = ChatInput.Text.Trim();
                    if (typed.Length == 0) return;
                    var provider = ChatService.Find(_settings.ChatProvider);
                    var exact = ModelCache.TryGetValue(provider.Key, out var list) ? list.Where(m => m.Id.Contains(typed, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList() : new();
                    ChooseModel(provider, exact.Count == 1 ? exact[0].Id : typed);
                    return;
                }
                _ = SendChatAsync();
            }
        }

        // ───────────── Envoi ─────────────

        private async System.Threading.Tasks.Task SendChatAsync()
        {
            string text = ChatInput.Text.Trim();
            if (text.Length == 0 || _chatBusy) return;
            if (_chatMode != "chat") SetChatMode("chat");

            var provider = ChatService.Find(_settings.ChatProvider);
            var conversation = ChatStore.Current ?? ChatStore.StartNew(provider.Key, ChatModelNow);
            conversation.Provider = provider.Key;
            conversation.Model = ChatModelNow;   // on peut changer de modèle en cours de route

            ChatInput.Text = "";
            _chatBusy = true;
            if (conversation.Turns.Count == 0) ChatList.Children.Clear();
            conversation.Turns.Add(new ChatTurn { Role = "user", Text = text });
            ChatList.Children.Add(Bubble(true, text, false).Border);

            var reply = Bubble(false, "…", false);
            ChatList.Children.Add(reply.Border);
            ChatScroll.ScrollToEnd();

            var cts = _chatCts = new CancellationTokenSource();
            var answer = new System.Text.StringBuilder();
            try
            {
                var history = conversation.Turns.Select(t => new ChatMessage(t.Role, t.Text)).ToArray();
                var lastRender = DateTime.MinValue;
                await Chat.StreamAsync(provider, ChatModelNow, _settings.ChatCustomUrl, history, delta =>
                {
                    answer.Append(delta);
                    if (DateTime.UtcNow - lastRender < TimeSpan.FromMilliseconds(90)) return;   // le Markdown est reconstruit : pas à chaque morceau
                    lastRender = DateTime.UtcNow;
                    Dispatcher.BeginInvoke(() => { reply.Set(answer.ToString(), false); ChatScroll.ScrollToEnd(); });
                }, cts.Token);

                if (answer.Length == 0) reply.Set("(réponse vide)", false);
                else { reply.Set(answer.ToString(), false); conversation.Turns.Add(new ChatTurn { Role = "assistant", Text = answer.ToString() }); }
                ChatScroll.ScrollToEnd();
                ChatStore.Commit();
            }
            catch (OperationCanceledException)
            {
                // Interrompue (autre conversation ouverte) : ce qui est déjà arrivé est gardé
                if (answer.Length > 0) { conversation.Turns.Add(new ChatTurn { Role = "assistant", Text = answer.ToString() }); ChatStore.Commit(); }
            }
            catch (Exception ex)
            {
                // La question reste dans l'historique seulement si une réponse a pu être donnée : sinon on la retire pour pouvoir réessayer
                if (conversation.Turns.Count > 0 && conversation.Turns[^1].Role == "user") conversation.Turns.RemoveAt(conversation.Turns.Count - 1);
                if (_chatMode == "chat" && ReferenceEquals(ChatStore.Current, conversation))
                {
                    reply.Set(ex.Message, true);
                }
                ChatStore.Commit();
            }
            finally { _chatBusy = false; }
        }

        // ───────────── Réglages ─────────────

        private string _modelFilter = "";

        private void RenderChatSettings()
        {
            var providers = new WrapPanel();
            foreach (var p in ChatService.Providers)
            {
                var captured = p;
                providers.Children.Add(Chip(p.Label, _settings.ChatProvider == p.Key, () =>
                {
                    _settings.ChatProvider = captured.Key;
                    Save();
                    RenderChat();
                    RenderSettings();
                }));
            }
            SettingsList.Children.Add(Row("Fournisseur", providers));

            var current = ChatService.Find(_settings.ChatProvider);
            string secretName = ChatService.SecretName(current.Key);

            string keyState = SecretStore.Has(secretName) ? "Clé enregistrée (chiffrée)" : (current.NeedsKey ? "Colle ta clé d'API" : "Clé facultative");
            SettingsList.Children.Add(Row("Clé d'API", SettingsInput("", keyState, true, value =>
            {
                if (value.Length == 0) return;
                SecretStore.Set(secretName, value.Trim());
                RenderChat();
                RenderSettings();
            })));

            if (current.Key == "custom")
            {
                SettingsList.Children.Add(Row("Serveur", SettingsInput(_settings.ChatCustomUrl, "http://localhost:11434/v1", false, value =>
                {
                    _settings.ChatCustomUrl = value.Trim();
                    Save();
                })));
            }

            // Modèle : saisie libre, ou liste récupérée auprès du fournisseur
            SettingsList.Children.Add(Row("Modèle", SettingsInput(ChatModelNow, current.DefaultModel.Length > 0 ? current.DefaultModel : "nom du modèle", false, value =>
            {
                _settings.ChatModels[current.Key] = value.Trim();
                Save();
                RenderChat();
            })));

            var load = new WrapPanel();
            load.Children.Add(Chip(ModelCache.ContainsKey(current.Key) ? "Actualiser la liste" : "Charger la liste des modèles", false, async () =>
            {
                Keyboard.ClearFocus();
                await LoadModelsAsync(current, () => RenderSettings());
            }));
            SettingsList.Children.Add(Row("Liste", load));
            if (_chatModelStatus.Length > 0) SettingsList.Children.Add(Row("", new TextBlock { Text = _chatModelStatus, Foreground = Down, FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap }));

            if (ModelCache.TryGetValue(current.Key, out var models))
            {
                SettingsList.Children.Add(Row("Filtrer", SettingsInput(_modelFilter, $"{models.Count} modèles : tape pour filtrer", false, value =>
                {
                    value = value.Trim();
                    if (value == _modelFilter) return;
                    _modelFilter = value;
                    RenderSettings();
                })));
                var chips = new WrapPanel();
                foreach (var m in models.Where(m => _modelFilter.Length == 0 || m.Id.Contains(_modelFilter, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(_modelFilter, StringComparison.OrdinalIgnoreCase)).Take(30))
                {
                    var id = m.Id;
                    chips.Children.Add(Chip(m.Name.Length > 28 ? m.Name.Substring(0, 27) + "…" : m.Name, id == ChatModelNow, () =>
                    {
                        _settings.ChatModels[current.Key] = id;
                        Save();
                        RenderChat();
                        RenderSettings();
                    }));
                }
                SettingsList.Children.Add(Row("Choisir", chips));
            }

            var keep = new WrapPanel();
            keep.Children.Add(Toggle("Conserver les conversations", _settings.ChatSave, v =>
            {
                _settings.ChatSave = v;
                ChatStore.Persist = v;
                Save();
                if (v) ChatStore.Commit();
                else ChatStore.DeleteAll();   // on n'écrit plus rien et on efface ce qui restait
                RenderChat();
                RenderSettings();
            }));
            keep.Children.Add(Chip("Effacer toutes les conversations", false, () => { ChatStore.DeleteAll(); RenderChat(); }));
            if (SecretStore.Has(secretName)) keep.Children.Add(Chip("Effacer la clé", false, () => { SecretStore.Set(secretName, ""); RenderChat(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Données", keep));

            SettingsList.Children.Add(Row("Sécurité", new TextBlock
            {
                Text = "La clé et les conversations sont chiffrées avec ton compte Windows (DPAPI). La clé n'est envoyée qu'au fournisseur choisi. " +
                       "Désactiver « Conserver » efface l'historique et le garde en mémoire seulement.",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
        /// <summary>Champ de saisie des réglages (masqué pour une clé) ; la valeur est validée en quittant le champ ou avec Entrée.</summary>
        private UIElement SettingsInput(string initial, string hint, bool secret, Action<string> commit)
        {
            var hintBlock = new TextBlock
            {
                Text = hint, Foreground = FgAlpha(0x66), FontSize = 13 * _scale, FontFamily = UiFont, IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12 * _scale, 0, 0, 0),
            };
            Control box;
            Func<string> read;
            Action<Action> onChange;
            if (secret)
            {
                var pb = new PasswordBox { Password = initial };
                box = pb; read = () => pb.Password; onChange = a => pb.PasswordChanged += (_, _) => a();
            }
            else
            {
                var tb = new TextBox { Text = initial };
                box = tb; read = () => tb.Text; onChange = a => tb.TextChanged += (_, _) => a();
            }
            box.FontSize = 13 * _scale; box.FontFamily = UiFont; box.Foreground = Fg();
            box.Background = System.Windows.Media.Brushes.Transparent; box.BorderThickness = new Thickness(0); box.VerticalAlignment = VerticalAlignment.Center;
            box.Margin = new Thickness(12 * _scale, 4 * _scale, 12 * _scale, 4 * _scale);
            if (box is TextBox t) t.CaretBrush = Fg(); else if (box is PasswordBox p) p.CaretBrush = Fg();
            hintBlock.Visibility = initial.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            onChange(() => hintBlock.Visibility = read().Length == 0 ? Visibility.Visible : Visibility.Collapsed);

            box.PreviewMouseLeftButtonDown += (_, _) =>
            {
                if (box.IsKeyboardFocusWithin) return;
                SetNoActivate(false);
                Activate();
                box.Focus();
            };
            box.GotKeyboardFocus += (_, _) => _typing = true;
            box.LostKeyboardFocus += (_, _) => { _typing = false; SetNoActivate(true); commit(read()); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; } };

            return new Border
            {
                Background = FgAlpha(0x1F), CornerRadius = new CornerRadius(12 * _scale), Width = 250 * _scale,
                HorizontalAlignment = HorizontalAlignment.Left, Child = new Grid { Children = { hintBlock, box } },
            };
        }
    }
}










