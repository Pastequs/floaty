using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Navbar
{
    // Historique du presse-papiers : les 10 dernières copies, remises en place d'un clic
    public partial class NotchWindow
    {
        private static readonly (string Label, int Value)[] ClipLimitChoices =
            { ("3", 3), ("5", 5), ("8", 8), ("10", 10) };

        private void InitClipboardView()
        {
            ClipTitle.FontSize = 14 * _scale;
            ClipCount.FontSize = 11 * _scale;
            ClipCount.Margin = new Thickness(8 * _scale, 0, 0, 0);
            ClipClearBtn.CornerRadius = new CornerRadius(10 * _scale);
            ClipClearBtn.Padding = new Thickness(11 * _scale, 3 * _scale, 11 * _scale, 3 * _scale);
            ClipClearText.FontSize = 11 * _scale;
            ClipScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);
            ClipSearchFrame.Margin = new Thickness(0, 6 * _scale, 0, 0);
            ClipSearchFrame.CornerRadius = new CornerRadius(12 * _scale);
            ClipSearch.FontSize = ClipSearchHint.FontSize = 12 * _scale;
            ClipSearch.Margin = ClipSearchHint.Margin = new Thickness(12 * _scale, 3 * _scale, 12 * _scale, 3 * _scale);

            Shared.Clipboard.Changed += OnClipChanged;
            RenderClipboard();
        }

        private void ClipToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Clipboard);

        private void OnClipChanged() => Dispatcher.BeginInvoke(RenderClipboard);

        private void ClipClear_Click(object sender, MouseButtonEventArgs e) => Shared.Clipboard.Clear();

        // ───────────── Affichage ─────────────

        private void RenderClipboard()
        {
            string filter = ClipSearch.Text.Trim();
            var entries = Shared.Clipboard.Entries.Where(e => filter.Length == 0 || Searchable(e).Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
            ClipSearchFrame.Visibility = Shared.Clipboard.Entries.Count > 3 || filter.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ClipCount.Text = filter.Length > 0 ? $"{entries.Count} trouvé(s)" : $"{Shared.Clipboard.Entries.Count(e => !e.Pinned)}/{Math.Clamp(_settings.ClipLimit, 1, 10)}" + (Shared.Clipboard.Entries.Any(e => e.Pinned) ? $" · {Shared.Clipboard.Entries.Count(e => e.Pinned)} épinglé(s)" : "");
            ClipClearBtn.Visibility = entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            ClipList.Children.Clear();
            if (entries.Count == 0)
            {
                ClipList.Children.Add(new TextBlock
                {
                    Text = filter.Length > 0 ? "Aucune copie ne correspond." : "Rien de copié pour l'instant : copie un texte, une image ou des fichiers.",
                    Foreground = FgAlpha(0x77), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 14 * _scale, 0, 0),
                });
                return;
            }

            for (int i = 0; i < entries.Count; i++) ClipList.Children.Add(BuildClipRow(entries[i], current: filter.Length == 0 && ReferenceEquals(entries[i], Shared.Clipboard.Entries.FirstOrDefault(e => !e.Pinned))));
        }

        private UIElement BuildClipRow(ClipEntry entry, bool current)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Vignette : image réelle pour une capture, pictogramme sinon
            UIElement icon = entry.Kind == ClipKind.Image && entry.Image != null
                ? new Border
                {
                    Width = 30 * _scale, Height = 24 * _scale, CornerRadius = new CornerRadius(5 * _scale), ClipToBounds = true,
                    Child = new System.Windows.Controls.Image { Source = entry.Image, Stretch = Stretch.UniformToFill },
                }
                : new TextBlock
                {
                    Text = entry.Kind == ClipKind.Files ? "" : "", FontFamily = IconFont, FontSize = 15 * _scale,
                    Foreground = current ? EffectiveAccent() : FgAlpha(0x99), Width = 30 * _scale,
                    TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4 * _scale, 0, 6 * _scale, 0) };
            text.Children.Add(new TextBlock
            {
                Text = Preview(entry), Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = (current ? "En cours · " : "") + Meta(entry), Foreground = current ? EffectiveAccent() : FgAlpha(0x77),
                FontSize = 10 * _scale, FontFamily = UiFont,
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var remove = new TextBlock
            {
                Text = "", FontFamily = IconFont, FontSize = 10 * _scale, Foreground = FgAlpha(0x77), Cursor = Cursors.Hand,
                Width = 22 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, ToolTip = "Retirer de l'historique",
            };
            remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; Shared.Clipboard.Remove(entry); };
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);

            var pin = new TextBlock
            {
                Text = "\uE718", FontFamily = IconFont, FontSize = 11 * _scale, Cursor = Cursors.Hand, Width = 22 * _scale,
                Foreground = entry.Pinned ? EffectiveAccent() : FgAlpha(0x55), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, ToolTip = entry.Pinned ? "Désépingler" : "Épingler (reste en tête, ne compte pas dans la limite)",
            };
            pin.MouseLeftButtonUp += (_, e) => { e.Handled = true; Shared.Clipboard.TogglePin(entry); };
            Grid.SetColumn(pin, 2);
            grid.Children.Add(pin);

            var card = new Border
            {
                Child = grid, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(10 * _scale),
                Padding = new Thickness(6 * _scale, 3 * _scale, 4 * _scale, 3 * _scale), Margin = new Thickness(0, 0, 0, 3 * _scale),
                Background = current ? FgAlpha(0x1F) : Brushes.Transparent,
                ToolTip = entry.Kind == ClipKind.Text ? Truncate(entry.Text, 600) : null,
            };
            card.MouseEnter += (_, _) => { if (!current) card.Background = FgAlpha(0x14); };
            card.MouseLeave += (_, _) => { if (!current) card.Background = Brushes.Transparent; };
            card.MouseLeftButtonUp += (_, _) => RestoreClip(entry);
            return card;
        }

        private static string Searchable(ClipEntry e) => e.Kind switch
        {
            ClipKind.Text => e.Text,
            ClipKind.Files => string.Join(" ", e.Files),
            _ => "image",
        };

        private void ClipSearch_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ClipSearch.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            ClipSearch.Focus();
        }

        private void ClipSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClipSearchHint.Visibility = ClipSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderClipboard();
        }

        private void ClipSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            ClipSearch.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void RestoreClip(ClipEntry entry)
        {
            bool ok = Shared.Clipboard.Restore(entry);
            Shared.ShowEverywhere(new OverlayInfo("", 1, ok ? "Remis dans le presse-papiers" : "Presse-papiers occupé, réessaie", "clip"));
        }

        // ───────────── Libellés ─────────────

        private static string Preview(ClipEntry e) => e.Kind switch
        {
            ClipKind.Image => $"Image {e.Image?.PixelWidth}×{e.Image?.PixelHeight}",
            ClipKind.Files => e.Files.Length == 1 ? System.IO.Path.GetFileName(e.Files[0].TrimEnd('\\')) : $"{e.Files.Length} fichiers",
            _ => Truncate(string.Join(" ", e.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())), 90),
        };

        private static string Meta(ClipEntry e)
        {
            string age = (DateTime.Now - e.At) switch
            {
                var d when d < TimeSpan.FromMinutes(1) => "à l'instant",
                var d when d < TimeSpan.FromHours(1) => $"il y a {(int)d.TotalMinutes} min",
                var d when d < TimeSpan.FromDays(1) => $"il y a {(int)d.TotalHours} h",
                _ => e.At.ToString("dd/MM HH:mm"),
            };
            return e.Kind switch
            {
                ClipKind.Text => $"{e.Text.Length} caractères · {age}",
                ClipKind.Files => $"fichiers · {age}",
                _ => $"image · {age}",
            };
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

        // ───────────── Réglages de la rubrique ─────────────

        private void RenderClipboardSettings()
        {
            SettingsList.Children.Add(Row("Copies", ChipsFor(ClipLimitChoices, _settings.ClipLimit, v =>
            {
                _settings.ClipLimit = v;
                Save();
                Shared.ApplyServices();
                RenderClipboard();
                RenderSettings();
            })));

            var privacy = new WrapPanel();
            privacy.Children.Add(Toggle("Ignorer les mots de passe", _settings.ClipSkipSensitive, v =>
            {
                _settings.ClipSkipSensitive = v;
                Save();
                Shared.ApplyServices();
                RenderSettings();
            }));
            privacy.Children.Add(Toggle("Garder après redémarrage", _settings.ClipPersist, v =>
            {
                _settings.ClipPersist = v;
                if (!v) Storage.Save("clipboard", new List<SavedClip>());   // on n'écrit plus rien et on efface ce qui restait
                Save();
                Shared.ApplyServices();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Confidentialité", privacy));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Vider l'historique", false, () => Shared.Clipboard.Clear()));
            SettingsList.Children.Add(Row("Actions", actions));

            SettingsList.Children.Add(Row("Note", new TextBlock
            {
                Text = "L'historique reste en mémoire par défaut. Images jamais enregistrées sur disque. Un clic sur une copie la remet dans le presse-papiers.",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
    }
}

