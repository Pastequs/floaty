using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Maison : interrupteurs Home Assistant choisis dans les réglages
    public partial class NotchWindow
    {
        private List<HaEntity>? _haStates;
        private string _haError = "";
        private System.Windows.Threading.DispatcherTimer? _haTimer;
        private bool _haBusy;

        private void InitSmart()
        {
            SmartToggle.Width = SmartToggle.Height = 24 * _scale;
            SmartToggle.Foreground = FgAlpha(0x99);
            SmartTitle.FontSize = 14 * _scale;
            SmartNote.FontSize = 11 * _scale;
            SmartNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            SmartScroll.Margin = new Thickness(0, 8 * _scale, 0, 0);

            _haTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _haTimer.Tick += async (_, _) => { if (_view == HoverView.Smart && _hovered) await RefreshSmartAsync(); };
            _haTimer.Start();
            Closed += (_, _) => _haTimer?.Stop();
            RenderSmart();
        }

        private void SmartToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Smart);

        private async System.Threading.Tasks.Task RefreshSmartAsync()
        {
            if (_haBusy) return;
            _haBusy = true;
            try
            {
                var ids = _settings.HaEntities;
                var fromHa = ids.Where(i => !DirigeraHub.IsHubId(i)).ToList();
                var fromHub = ids.Where(DirigeraHub.IsHubId).ToList();
                var found = new System.Collections.Generic.Dictionary<string, HaEntity>();
                var errors = new System.Collections.Generic.List<string>();

                // Chaque source est interrogée à part : l'une peut tomber en panne sans masquer l'autre
                if (fromHa.Count > 0)
                    try { foreach (var e in await HomeAssistant.StatesAsync(_settings.HaUrl, fromHa)) found[e.Id] = e; }
                    catch (Exception ex) { errors.Add(ex.Message); }
                if (fromHub.Count > 0)
                    try { foreach (var e in await DirigeraHub.ListAsync(_settings.DirigeraIp)) found[e.Id] = e; }
                    catch (Exception ex) { errors.Add(ex.Message); }

                _haStates = ids.Select(id => found.TryGetValue(id, out var e) ? e : new HaEntity(id, id, "unavailable")).ToList();
                _haError = errors.Count > 0 && found.Count == 0 ? string.Join(" ", errors) : "";
            }
            catch (Exception ex) { _haError = ex.Message; }
            finally { _haBusy = false; }
            RenderSmart();
        }

        private string? _smartDetail;   // identifiant de la lumière dont on règle luminosité et température

        private async System.Threading.Tasks.Task ApplyToDevicesAsync(System.Collections.Generic.IEnumerable<HaEntity> devices, bool turnOn)
        {
            foreach (var e in devices)
            {
                try
                {
                    if (turnOn == e.On) continue;
                    if (DirigeraHub.IsHubId(e.Id)) { if (turnOn) await DirigeraHub.ToggleAsync(_settings.DirigeraIp, e); else await DirigeraHub.TurnOffAsync(_settings.DirigeraIp, e); }
                    else if (turnOn) await HomeAssistant.ToggleAsync(_settings.HaUrl, e);
                    else await HomeAssistant.TurnOffAsync(_settings.HaUrl, e);
                }
                catch (Exception ex) { _haError = ex.Message; }
            }
            await System.Threading.Tasks.Task.Delay(400);
            await RefreshSmartAsync();
        }

        private async System.Threading.Tasks.Task SetLightAsync(HaEntity light, int? percent, int? kelvin)
        {
            try
            {
                if (DirigeraHub.IsHubId(light.Id)) await DirigeraHub.SetLightAsync(_settings.DirigeraIp, light, percent, kelvin);
                else await HomeAssistant.SetLightAsync(_settings.HaUrl, light, percent, kelvin);
            }
            catch (Exception ex) { _haError = ex.Message; }
            await System.Threading.Tasks.Task.Delay(300);
            await RefreshSmartAsync();
        }

        private Border SmartChip(string text, Action click, bool filled = false)
        {
            var accent = EffectiveAccentColor();
            var chip = new Border
            {
                Child = new TextBlock { Text = text, FontSize = 11 * _scale, FontFamily = UiFont, Foreground = filled ? new SolidColorBrush(IsLight(accent) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White) : Fg() },
                Background = filled ? AccentFill() : FgAlpha(0x1F), CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand,
                Padding = new Thickness(9 * _scale, 2 * _scale, 9 * _scale, 2 * _scale), Margin = new Thickness(0, 0, 5 * _scale, 4 * _scale),
            };
            Press.Attach(chip, 0.94);
            chip.MouseLeftButtonUp += (_, _) => click();
            return chip;
        }

        private void RenderSmart()
        {
            SmartList.Children.Clear();
            SmartNote.Text = _haStates?.Count(s => s.On) is > 0 and var n ? $"{n} allumé(s)" : "";

            if (_haError.Length > 0 && (_haStates == null || _haStates.All(s => s.State == "unavailable"))) { SmartList.Children.Add(HaHint(_haError, true)); return; }
            bool haReady = _settings.HaUrl.Length > 0 && HomeAssistant.HasToken, hubReady = _settings.DirigeraIp.Length > 0 && DirigeraHub.HasToken;
            if (!haReady && !hubReady) { SmartList.Children.Add(HaHint("Connecte Home Assistant ou ton hub IKEA DIRIGERA dans Réglages › Maison.", false)); return; }
            if (_settings.HaEntities.Count == 0) { SmartList.Children.Add(HaHint("Choisis les appareils à afficher dans Réglages › Maison.", false)); return; }
            if (_haStates == null) { SmartList.Children.Add(HaHint("Chargement…", false)); _ = RefreshSmartAsync(); return; }

            if (_smartDetail != null && _haStates.FirstOrDefault(s => s.Id == _smartDetail) is { } detail) { RenderSmartDetail(detail); return; }
            _smartDetail = null;

            // Raccourcis : tout éteindre et routines personnelles
            var shortcuts = new WrapPanel();
            var lit = _haStates.Where(s => s.On && s.Domain != "scene").ToList();
            if (_settings.HomeAllOffButton && lit.Count > 0) shortcuts.Children.Add(SmartChip("Tout éteindre", () => _ = ApplyToDevicesAsync(lit, false), filled: true));
            foreach (var routine in _settings.Routines)
            {
                var r = routine;
                shortcuts.Children.Add(SmartChip("▶ " + r.Name, () =>
                    _ = ApplyToDevicesAsync(_haStates!.Where(s => r.Ids.Contains(s.Id)), r.TurnOn)));
            }
            if (shortcuts.Children.Count > 0) SmartList.Children.Add(shortcuts);

            // Regroupement par pièce quand au moins une est connue
            var groups = _haStates.GroupBy(s => s.Room.Length > 0 ? s.Room : "Autres").OrderBy(g => g.Key == "Autres" ? 1 : 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
            bool grouped = _settings.HomeGroupRooms && (groups.Count > 1 || groups[0].Key != "Autres");
            if (!grouped) groups = new() { _haStates.GroupBy(_ => "").First() };   // une seule grille, dans l'ordre choisi
            foreach (var group in groups)
            {
                if (grouped)
                {
                    var head = new Grid { Margin = new Thickness(0, 2 * _scale, 0, 4 * _scale) };
                    head.Children.Add(new TextBlock { Text = group.Key, Foreground = FgAlpha(0x88), FontSize = 11 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center });
                    var roomLit = group.Where(s => s.On && s.Domain != "scene").ToList();
                    if (roomLit.Count > 0)
                    {
                        var off = SmartChip("Éteindre la pièce", () => _ = ApplyToDevicesAsync(roomLit, false));
                        off.HorizontalAlignment = HorizontalAlignment.Right;
                        off.Margin = new Thickness(0);
                        head.Children.Add(off);
                    }
                    SmartList.Children.Add(head);
                }
                var tiles = new WrapPanel();
                foreach (var entity in group) tiles.Children.Add(SmartTile(entity));
                SmartList.Children.Add(tiles);
            }
        }

        private UIElement SmartTile(HaEntity entity)
        {
            var e = entity;
            bool unavailable = e.State is "unavailable" or "unknown";
            bool on = e.On;
            Brush bg = on ? AccentFill() : FgAlpha(0x1F);
            var accent = EffectiveAccentColor();
            Brush fg = on ? new SolidColorBrush(IsLight(accent) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White) : Fg();
            bool adjustable = e.CanDim || e.CanTemp;

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = e.Name, Foreground = fg, FontSize = 12 * _scale, FontFamily = UiFont, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            string state = unavailable ? "indisponible" : e.Domain is "scene" or "script" ? "activer" : on ? (e.Level != null ? $"allumé · {e.Level} %" : "allumé") : "éteint";
            stack.Children.Add(new TextBlock { Text = state, Foreground = fg, Opacity = 0.7, FontSize = 10 * _scale, FontFamily = UiFont });

            var tile = new Border
            {
                Child = stack, Background = bg, CornerRadius = new CornerRadius(12 * _scale), Cursor = Cursors.Hand,
                Width = 118 * _scale, Padding = new Thickness(10 * _scale, 6 * _scale, 10 * _scale, 6 * _scale), Margin = new Thickness(0, 0, 6 * _scale, 6 * _scale),
                Opacity = unavailable ? 0.5 : 1, ToolTip = adjustable ? "Clic : allumer / éteindre · clic droit ou appui long : luminosité et température" : null,
            };
            Press.Attach(tile, 0.95);

            // Appui long ou clic droit : réglages de la lumière ; clic simple : bascule
            bool longPress = false;
            var hold = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            hold.Tick += (_, _) => { hold.Stop(); if (adjustable) { longPress = true; _smartDetail = e.Id; RenderSmart(); } };
            tile.MouseLeftButtonDown += (_, _) => { longPress = false; if (adjustable) hold.Start(); };
            tile.MouseLeave += (_, _) => hold.Stop();
            tile.MouseRightButtonUp += (_, _) => { if (adjustable) { _smartDetail = e.Id; RenderSmart(); } };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                hold.Stop();
                if (longPress) return;
                try
                {
                    if (DirigeraHub.IsHubId(e.Id)) await DirigeraHub.ToggleAsync(_settings.DirigeraIp, e);
                    else await HomeAssistant.ToggleAsync(_settings.HaUrl, e);
                    await System.Threading.Tasks.Task.Delay(350);
                }
                catch (Exception ex) { _haError = ex.Message; }
                await RefreshSmartAsync();
            };
            return tile;
        }

        /// <summary>Réglage d'une lumière : interrupteur, luminosité et température de couleur.</summary>
        private void RenderSmartDetail(HaEntity light)
        {
            SmartNote.Text = light.Name;
            var top = new WrapPanel();
            top.Children.Add(SmartChip("← Retour", () => { _smartDetail = null; RenderSmart(); }));
            top.Children.Add(SmartChip(light.On ? "Éteindre" : "Allumer", () => _ = ApplyToDevicesAsync(new[] { light }, !light.On), filled: light.On));
            SmartList.Children.Add(top);

            var accent = AccentFill();
            if (light.CanDim)
            {
                var row = new SliderRow(_scale, "\uE706", accent, Fg(), FgAlpha(0x33));
                row.SetValue((light.Level ?? 100) / 100.0, $"{light.Level ?? 100}%");
                row.Committed += v => _ = SetLightAsync(light, Math.Max(1, (int)Math.Round(v * 100)), null);
                SmartList.Children.Add(row.Root);
            }
            if (light.CanTemp)
            {
                int span = Math.Max(1, light.MaxK - light.MinK);
                var row = new SliderRow(_scale, "\uE9CA", accent, Fg(), FgAlpha(0x33));
                int current = light.Kelvin ?? (light.MinK + light.MaxK) / 2;
                row.SetValue((current - light.MinK) / (double)span, $"{current} K");
                row.Committed += v => _ = SetLightAsync(light, null, light.MinK + (int)Math.Round(v * span));
                SmartList.Children.Add(row.Root);
                SmartList.Children.Add(HaHint("Chaud (gauche) → froid (droite).", false));
            }
        }
        private TextBlock HaHint(string text, bool error) => new()
        {
            Text = text, Foreground = error ? Down : FgAlpha(0x77), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Width = 330 * _scale,
        };

        // ───────────── Réglages ─────────────

        // ───────────── Routines : « Je pars », « Bonne nuit »… ─────────────

        private string _routineName = "";
        private readonly System.Collections.Generic.HashSet<string> _routineIds = new();
        private bool _routineOn;

        private void RenderRoutineSettings()
        {
            var existing = new WrapPanel();
            foreach (var routine in _settings.Routines.ToList())
            {
                var r = routine;
                existing.Children.Add(Chip($"{r.Name} ({(r.TurnOn ? "allume" : "éteint")} {r.Ids.Count})  ✕", false, () => { _settings.Routines.Remove(r); Save(); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Routines", existing.Children.Count > 0 ? existing : Hint("Aucune routine. Crée-en une ci-dessous : un bouton « ▶ nom » apparaît dans l'onglet Maison.")));

            if (_settings.HaEntities.Count == 0) return;
            SettingsList.Children.Add(Row("Nom", SettingsInput(_routineName, "ex. Je pars, Bonne nuit", false, value => _routineName = value.Trim())));

            var action = new WrapPanel();
            action.Children.Add(Chip("Éteindre", !_routineOn, () => { _routineOn = false; RenderSettings(); }));
            action.Children.Add(Chip("Allumer", _routineOn, () => { _routineOn = true; RenderSettings(); }));
            SettingsList.Children.Add(Row("Action", action));

            var devices = new WrapPanel();
            foreach (var id in _settings.HaEntities)
            {
                string captured = id;
                string label = _haStates?.FirstOrDefault(s => s.Id == id)?.Name ?? id;
                bool picked = _routineIds.Contains(id);
                devices.Children.Add(Chip(label, picked, () => { if (!_routineIds.Remove(captured)) _routineIds.Add(captured); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Appareils", devices));

            SettingsList.Children.Add(Row("", Chip("Créer la routine", false, () =>
            {
                Keyboard.ClearFocus();
                if (_routineName.Length == 0 || _routineIds.Count == 0) return;
                _settings.Routines.Add(new Routine { Name = _routineName, Ids = _routineIds.ToList(), TurnOn = _routineOn });
                _routineName = "";
                _routineIds.Clear();
                Save();
                RenderSettings();
            })));
        }

        private List<HaEntity>? _haChoices;
        private string _haSettingsNote = "";

        private List<HaEntity>? _ikChoices;
        private TextBlock? _ikStatus;
        private string _ikNote = "";
        private bool _ikPairing;

        private void SetIkStatus(string text) { _ikNote = text; if (_ikStatus != null) _ikStatus.Text = text; }

        /// <summary>Association au hub : code, appui sur le bouton d'action, échange contre un jeton.</summary>
        private async System.Threading.Tasks.Task PairHubAsync()
        {
            if (_ikPairing) return;
            Keyboard.ClearFocus();
            if (_settings.DirigeraIp.Length == 0) { SetIkStatus("Renseigne d'abord l'adresse IP du hub."); return; }

            _ikPairing = true;
            try
            {
                SetIkStatus("Demande d'association au hub…");
                var ticket = await DirigeraHub.BeginPairingAsync(_settings.DirigeraIp);
                SetIkStatus("Appuie maintenant sur le bouton d'action du hub (sous le boîtier) : j'attends 60 secondes…");
                for (int i = 0; i < 30; i++)
                {
                    await System.Threading.Tasks.Task.Delay(2000);
                    if (await DirigeraHub.TryFinishPairingAsync(_settings.DirigeraIp, ticket))
                    {
                        SetIkStatus("Hub associé ✓");
                        _haStates = null;
                        RenderSettings();
                        return;
                    }
                }
                SetIkStatus("Délai dépassé : relance « Associer » puis appuie sur le bouton du hub.");
            }
            catch (Exception ex) { SetIkStatus(ex.Message); }
            finally { _ikPairing = false; }
        }

        private void RenderHubSettings()
        {
            SettingsList.Children.Add(Row("Hub IKEA", SettingsInput(_settings.DirigeraIp, "Adresse IP du hub DIRIGERA, ex. 192.168.1.30", false, value =>
            {
                value = value.Trim();
                if (value == _settings.DirigeraIp) return;
                _settings.DirigeraIp = value;
                DirigeraHub.ClearToken();   // un autre hub demande une nouvelle association
                Save();
            })));

            var actions = new WrapPanel();
            actions.Children.Add(Chip(DirigeraHub.HasToken ? "Associer de nouveau" : "Associer le hub", false, () => _ = PairHubAsync()));
            if (DirigeraHub.HasToken)
            {
                actions.Children.Add(Chip("Charger mes appareils IKEA", false, async () =>
                {
                    SetIkStatus("Chargement…");
                    try { _ikChoices = await DirigeraHub.ListAsync(_settings.DirigeraIp); SetIkStatus($"{_ikChoices.Count} appareil(s) et scène(s) trouvés"); }
                    catch (Exception ex) { SetIkStatus(ex.Message); }
                    RenderSettings();
                }));
                actions.Children.Add(Chip("Dissocier", false, () => { DirigeraHub.ClearToken(); SetIkStatus("Hub dissocié."); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Association", actions));

            _ikStatus = new TextBlock { Text = _ikNote.Length > 0 ? _ikNote : DirigeraHub.HasToken ? "Hub associé ✓" : "Non associé", Foreground = FgAlpha(0x99), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap };
            SettingsList.Children.Add(Row("Statut", _ikStatus));

            if (_ikChoices != null)
            {
                var chips = new WrapPanel();
                foreach (var c in _ikChoices)
                {
                    var captured = c;
                    bool on = _settings.HaEntities.Contains(c.Id);
                    chips.Children.Add(Chip((c.Domain == "scene" ? "▶ " : "") + c.Name, on, () =>
                    {
                        if (on) _settings.HaEntities.Remove(captured.Id); else _settings.HaEntities.Add(captured.Id);
                        Save();
                        _haStates = null;
                        RenderSettings();
                    }));
                }
                SettingsList.Children.Add(Row("Afficher", chips));
            }

            SettingsList.Children.Add(Row("Guide IKEA", GuideBlock(new[]
            {
                "1. Trouve l'adresse IP du hub : dans l'appli IKEA Home smart › Paramètres › ton hub, ou dans la liste des appareils de ta box (nom « DIRIGERA »). Fixe-la (réservation DHCP) pour qu'elle ne change pas.",
                "2. Colle-la dans « Hub IKEA » (le PC doit être sur le même réseau que le hub) et valide avec Entrée.",
                "3. Clique « Associer le hub », puis, dans les 60 secondes, appuie sur le bouton d'action du hub (le petit bouton sous le boîtier). L'appli récupère un jeton, chiffré avec ton compte Windows.",
                "4. « Charger mes appareils IKEA » puis coche lumières, prises, stores ou scènes à afficher ; ils apparaissent dans l'onglet Maison, à côté de ceux de Home Assistant.",
                "Dépannage : « injoignable » = mauvaise adresse ou réseau différent ; « accès refusé » = associe-le de nouveau. Les capteurs et enceintes ne sont pas pris en charge pour l'instant.",
            })));
        }

        private void RenderSmartSettings()
        {
            var display = new WrapPanel();
            display.Children.Add(Toggle("Regrouper par pièce", _settings.HomeGroupRooms, v => { _settings.HomeGroupRooms = v; Save(); RenderSmart(); RenderSettings(); }));
            display.Children.Add(Toggle("Bouton « Tout éteindre »", _settings.HomeAllOffButton, v => { _settings.HomeAllOffButton = v; Save(); RenderSmart(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Affichage", display));

            RenderHubSettings();
            SettingsList.Children.Add(new Border { Height = 1, Background = FgAlpha(0x22), Margin = new Thickness(0, 4 * _scale, 0, 10 * _scale) });
            SettingsList.Children.Add(Row("Adresse", SettingsInput(_settings.HaUrl, "http://homeassistant.local:8123", false, value =>
            {
                value = value.Trim();
                if (value == _settings.HaUrl) return;
                _settings.HaUrl = value;
                Save();
            })));

            SettingsList.Children.Add(Row("Jeton", SettingsInput("", HomeAssistant.HasToken ? "Jeton enregistré (chiffré)" : "Jeton d'accès longue durée", true, value =>
            {
                if (value.Length == 0) return;
                HomeAssistant.SetToken(value);
                RenderSettings();
            })));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Charger mes appareils", false, async () =>
            {
                Keyboard.ClearFocus();
                _haSettingsNote = "Chargement…";
                try { _haChoices = await HomeAssistant.ListAsync(_settings.HaUrl); _haSettingsNote = $"{_haChoices.Count} appareils trouvés"; }
                catch (Exception ex) { _haSettingsNote = ex.Message; }
                RenderSettings();
            }));
            if (HomeAssistant.HasToken) actions.Children.Add(Chip("Effacer le jeton", false, () => { HomeAssistant.SetToken(""); RenderSettings(); }));
            SettingsList.Children.Add(Row("Connexion", actions));
            if (_haSettingsNote.Length > 0) SettingsList.Children.Add(Row("", new TextBlock { Text = _haSettingsNote, Foreground = FgAlpha(0x99), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap }));

            if (_haChoices != null)
            {
                var chips = new WrapPanel();
                foreach (var c in _haChoices)
                {
                    var captured = c;
                    bool on = _settings.HaEntities.Contains(c.Id);
                    chips.Children.Add(Chip(c.Name, on, () =>
                    {
                        if (on) _settings.HaEntities.Remove(captured.Id); else _settings.HaEntities.Add(captured.Id);
                        Save();
                        _haStates = null;
                        RenderSettings();
                    }));
                }
                SettingsList.Children.Add(Row("Afficher", chips));
            }
            else if (_settings.HaEntities.Count > 0)
                SettingsList.Children.Add(Row("Affichés", new TextBlock { Text = string.Join(" · ", _settings.HaEntities), Foreground = FgAlpha(0x88), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap }));

            RenderRoutineSettings();

            SettingsList.Children.Add(Row("Guide", GuideBlock(new[]
            {
                "1. Home Assistant doit tourner sur ton réseau. Son adresse ressemble à  http://homeassistant.local:8123  ou  http://192.168.1.20:8123  : vérifie qu'elle s'ouvre dans ton navigateur.",
                "2. Crée un jeton : dans Home Assistant, clique sur ton nom (en bas à gauche) › onglet « Sécurité » › tout en bas « Jetons d'accès longue durée » › « Créer un jeton » › nomme-le « Navbar » › copie-le tout de suite (il ne s'affiche qu'une fois).",
                "3. Ici : colle l'adresse dans « Adresse » (sans « / » final) et le jeton dans « Jeton » ; valide chaque champ avec Entrée.",
                "4. Clique « Charger mes appareils » puis coche ce que tu veux piloter : lumières, prises, scènes, scripts, volets, serrures.",
                "5. Active l'onglet : Réglages › Général › Modules › « Maison ». Un clic sur une tuile allume ou éteint ; l'état se rafraîchit toutes les 6 secondes.",
                "Dépannage : « injoignable » = adresse, port ou réseau (PC et Home Assistant sur le même réseau, ou adresse https d'accès distant). « Jeton refusé » = jeton recopié incomplet ou supprimé. Le jeton est chiffré avec ton compte Windows.",
            }, ("Ouvrir Home Assistant", _settings.HaUrl))));
        }

        /// <summary>Procédure pas à pas : étapes en texte, puis boutons qui ouvrent les liens utiles.</summary>
        private UIElement GuideBlock(string[] steps, params (string Label, string Url)[] links)
        {
            var guide = new StackPanel();
            foreach (var step in steps)
                guide.Children.Add(new TextBlock
                {
                    Text = step, Foreground = FgAlpha(0x99), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 3 * _scale),
                });
            var buttons = new WrapPanel();
            foreach (var (label, url) in links)
            {
                string target = url;
                buttons.Children.Add(Chip(label, false, () =>
                {
                    if (target.Length == 0) return;
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
                }));
            }
            if (buttons.Children.Count > 0) guide.Children.Add(buttons);
            return guide;
        }
    }
}










