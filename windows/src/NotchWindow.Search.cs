using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Navbar
{
    // Recherche système (applications, fichiers, web) et lancement des applications récentes
    public partial class NotchWindow
    {
        private SearchService _search => Shared.Search;
        private DispatcherTimer? _searchDebounce;
        private List<SearchHit> _recents = new();
        private DateTime _recentsAt = DateTime.MinValue;

        // Résultats affichés, dans l'ordre, avec leur ligne pour la sélection au clavier
        private readonly List<(SearchHit Hit, Border Row)> _searchRows = new();
        private int _searchSelected = -1;

        private static readonly (string Label, string Value)[] EngineChoices =
            { ("Google", "google"), ("Bing", "bing"), ("DuckDuckGo", "duckduckgo") };

        private static readonly (string Label, int Value)[] RecentChoices =
            { ("4", 4), ("6", 6), ("8", 8), ("10", 10) };

        private void InitSearch()
        {
            // La barre de recherche est en haut, sur la ligne de l'heure ; les résultats s'affichent dans l'onglet Recherche
            SearchBoxFrame.CornerRadius = new CornerRadius(10 * _scale);
            SearchBoxFrame.Padding = new Thickness(9 * _scale, 1 * _scale, 9 * _scale, 1 * _scale);
            SearchBoxFrame.Margin = new Thickness(0, -4 * _scale, 0, 0);
            SearchBoxFrame.Width = 190 * _scale;
            SearchBoxFrame.Height = 21 * _scale;
            SearchGlyph.FontSize = 11 * _scale;
            SearchHint.FontSize = SearchBox.FontSize = 12 * _scale;
            SearchHint.Margin = new Thickness(20 * _scale, 0, 0, 0);
            SearchBox.Margin = new Thickness(18 * _scale, 0, 0, 0);
            SearchHint.TextTrimming = TextTrimming.CharacterEllipsis;

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _searchDebounce.Tick += async (_, _) => { _searchDebounce!.Stop(); await RunSearchAsync(); };
            Closed += (_, _) => _searchDebounce?.Stop();
        }

        private void SearchToggle_Click(object sender, MouseButtonEventArgs e)
        {
            SetView(HoverView.Search);
            Dispatcher.BeginInvoke(() => { if (_view == HoverView.Search) FocusSearchBox(); }, DispatcherPriority.Input);
        }

        private void FocusSearchBox()
        {
            SetNoActivate(false);
            Activate();
            SearchBox.Focus();
        }

        /// <summary>À l'ouverture : champ vide et applications récentes.</summary>
        private void OnSearchOpened()
        {
            if (SearchBox.Text.Trim().Length > 0) _ = RunSearchAsync();   // on a commencé à taper depuis la barre du haut
            else _ = ShowRecentsAsync();
        }

        // ───────────── Saisie ─────────────

        private void SearchBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (SearchBox.IsKeyboardFocusWithin) return;
            FocusSearchBox();
            if (_view != HoverView.Search) GoTo(HoverView.Search);   // les résultats prennent la place du module affiché
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (SearchBox.Text.Length > 0 && _view != HoverView.Search) GoTo(HoverView.Search);
            _searchDebounce?.Stop();
            _searchDebounce?.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: MoveSelection(1); e.Handled = true; break;
                case Key.Up: MoveSelection(-1); e.Handled = true; break;
                case Key.Enter: LaunchSelected(); e.Handled = true; break;
                case Key.Escape:
                    if (SearchBox.Text.Length > 0) SearchBox.Clear(); else Keyboard.ClearFocus();
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_searchRows.Count == 0) return;
            _searchSelected = Math.Clamp(_searchSelected + delta, 0, _searchRows.Count - 1);
            for (int i = 0; i < _searchRows.Count; i++)
                _searchRows[i].Row.Background = i == _searchSelected ? FgAlpha(0x2A) : Brushes.Transparent;
            _searchRows[_searchSelected].Row.BringIntoView();
        }

        private void LaunchSelected()
        {
            if (_searchRows.Count == 0) return;
            int index = _searchSelected >= 0 ? _searchSelected : 0;
            Launch(_searchRows[index].Hit);
        }

        private void Launch(SearchHit hit)
        {
            if (LaunchExtra(hit)) { Keyboard.ClearFocus(); return; }
            if (hit.Target.StartsWith("calc:", StringComparison.Ordinal))
            {
                try { Clipboard.SetText(hit.Target.Substring(5)); } catch { }
                Shared.ShowEverywhere(new OverlayInfo("\uE8EF", 1, "Résultat copié : " + hit.Target.Substring(5), "clip"));
            }
            else if (hit.Target.StartsWith("web:", StringComparison.Ordinal))
                SearchService.OpenWeb(hit.Target.Substring(4), _settings.SearchEngine);
            else
                SearchService.Launch(hit);

            Keyboard.ClearFocus();
        }

        // ───────────── Recherche ─────────────

        private async Task RunSearchAsync()
        {
            string query = SearchBox.Text.Trim();
            if (query.Length == 0) { await ShowRecentsAsync(); return; }

            bool apps = _settings.SearchApps, files = _settings.SearchFiles;
            var result = await Task.Run(() =>
            {
                var list = new List<SearchHit>();
                if (apps) list.AddRange(_search.SearchApps(query, 5));
                if (files) list.AddRange(_search.SearchFiles(query, 6));
                return list;
            });

            if (SearchBox.Text.Trim() != query) return;   // l'utilisateur a continué à taper

            // Commandes, notes, tâches, copies : avant les applications et les fichiers
            result.InsertRange(0, ExtraHits(query));

            // Dernière ligne : poursuivre la recherche sur le web
            result.Add(new SearchHit($"Rechercher « {query} » sur le web", EngineLabel(), "web:" + query, SearchKind.File));

            // Calculatrice : « 12*(3+4) » donne le résultat en première ligne ; un clic le copie
            if (TryCalculate(query, out string calc))
                result.Insert(0, new SearchHit($"= {calc}", query, "calc:" + calc, SearchKind.File));
            RenderResults(result);
        }

        private string EngineLabel() => EngineChoices.FirstOrDefault(e => e.Value == _settings.SearchEngine).Label ?? "Web";

        private void RenderResults(List<SearchHit> hits)
        {
            SearchList.Children.Clear();
            _searchRows.Clear();
            _searchSelected = 0;

            foreach (var hit in hits.Take(10))
            {
                var row = BuildResultRow(hit);
                _searchRows.Add((hit, row));
                SearchList.Children.Add(row);
            }
            if (_searchRows.Count > 0) _searchRows[0].Row.Background = FgAlpha(0x2A);
        }

        private Border BuildResultRow(SearchHit hit)
        {
            bool web = hit.Target.StartsWith("web:", StringComparison.Ordinal);
            string? glyphText = web ? "\uE774"
                : hit.Target.StartsWith("calc:", StringComparison.Ordinal) ? "\uE8EF"
                : hit.Target.StartsWith("note:", StringComparison.Ordinal) ? "\uE70B"
                : hit.Target.StartsWith("task:", StringComparison.Ordinal) ? "\uE73E"
                : hit.Target.StartsWith("clip:", StringComparison.Ordinal) ? "\uE8C8"
                : hit.Target.StartsWith("cmd:", StringComparison.Ordinal) ? "\uE768" : null;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            UIElement icon = glyphText != null ? new TextBlock { Text = glyphText, FontFamily = IconFont, FontSize = 18 * _scale, Foreground = Fg(), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
                : hit.Kind == SearchKind.UwpApp
                    ? new TextBlock { Text = "", FontFamily = IconFont, FontSize = 18 * _scale, Foreground = Fg(), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center }
                    : new System.Windows.Controls.Image { Source = IconFor(hit.Target), Width = 22 * _scale, Height = 22 * _scale };
            if (icon is FrameworkElement fe) fe.Width = 30 * _scale;
            grid.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = hit.Title, Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = hit.Subtitle, Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var border = new Border
            {
                Child = grid, Cursor = Cursors.Hand, Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(10 * _scale), Padding = new Thickness(6 * _scale, 3 * _scale, 6 * _scale, 3 * _scale),
                ToolTip = web ? null : hit.Target,
            };
            border.MouseEnter += (_, _) =>
            {
                int index = _searchRows.FindIndex(r => ReferenceEquals(r.Row, border));
                if (index >= 0) { _searchSelected = index; MoveSelection(0); }
            };
            border.MouseLeftButtonUp += (_, _) => Launch(hit);
            return border;
        }

        /// <summary>Évalue une expression simple (+ - * / % et parenthèses) ; faux si ce n'est pas un calcul.</summary>
        private static bool TryCalculate(string query, out string result)
        {
            result = "";
            string expr = query.Replace(" ", "").Replace(",", ".").Replace("×", "*").Replace("÷", "/");
            if (expr.Length < 3 || !expr.Any(char.IsDigit)) return false;
            if (!expr.All(c => char.IsDigit(c) || "+-*/%().".Contains(c))) return false;
            if (!expr.Skip(1).Any(c => "+-*/%".Contains(c))) return false;   // un nombre seul n'est pas un calcul

            try
            {
                double value = Convert.ToDouble(new System.Data.DataTable().Compute(expr, null), CultureInfo.InvariantCulture);
                if (double.IsNaN(value) || double.IsInfinity(value)) return false;
                result = value.ToString("0.##########", CultureInfo.CurrentCulture);
                return true;
            }
            catch { return false; }
        }

        // ───────────── Applications récentes ─────────────

        private async Task ShowRecentsAsync()
        {
            if (DateTime.Now - _recentsAt > TimeSpan.FromSeconds(20))
            {
                int count = _settings.RecentCount;
                _recents = await Task.Run(() => _search.Recents(count));
                _recentsAt = DateTime.Now;
            }
            if (SearchBox.Text.Trim().Length > 0) return;   // une recherche est en cours
            RenderRecents();
        }

        private void RenderRecents()
        {
            SearchList.Children.Clear();
            _searchRows.Clear();
            _searchSelected = -1;

            SearchList.Children.Add(new TextBlock
            {
                Text = "Récents", Foreground = FgAlpha(0x88), FontSize = 11 * _scale, FontFamily = UiFont,
                Margin = new Thickness(2 * _scale, 0, 0, 5 * _scale),
            });

            if (_recents.Count == 0)
            {
                SearchList.Children.Add(new TextBlock
                {
                    Text = "Aucune application récente trouvée", Foreground = FgAlpha(0x66), FontSize = 12 * _scale, FontFamily = UiFont,
                });
                return;
            }

            // Tuiles : icône + nom, un clic lance l'application
            var wrap = new WrapPanel();
            foreach (var hit in _recents)
            {
                var tile = new StackPanel { Width = 78 * _scale, Margin = new Thickness(0, 0, 4 * _scale, 6 * _scale) };
                UIElement icon = hit.Kind == SearchKind.UwpApp
                    ? new TextBlock { Text = "", FontFamily = IconFont, FontSize = 22 * _scale, Foreground = Fg(), TextAlignment = TextAlignment.Center, Height = 28 * _scale }
                    : new System.Windows.Controls.Image { Source = IconFor(hit.Target), Width = 28 * _scale, Height = 28 * _scale };
                tile.Children.Add(icon);
                tile.Children.Add(new TextBlock
                {
                    Text = hit.Title, Foreground = Fg(), FontSize = 11 * _scale, FontFamily = UiFont, TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3 * _scale, 0, 0),
                });

                var card = new Border
                {
                    Child = tile, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = hit.Subtitle,
                    CornerRadius = new CornerRadius(10 * _scale), Padding = new Thickness(2 * _scale),
                };
                card.MouseEnter += (_, _) => card.Background = FgAlpha(0x1F);
                card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
                var captured = hit;
                card.MouseLeftButtonUp += (_, _) => Launch(captured);
                wrap.Children.Add(card);
            }
            SearchList.Children.Add(wrap);
        }

        // ───────────── Réglages de la rubrique ─────────────

        private void RenderSearchSettings()
        {
            var sources = new WrapPanel();
            sources.Children.Add(Toggle("Applications", _settings.SearchApps, v => { _settings.SearchApps = v; Save(); RenderSettings(); }));
            sources.Children.Add(Toggle("Fichiers", _settings.SearchFiles, v => { _settings.SearchFiles = v; Save(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Sources", sources));

            SettingsList.Children.Add(Row("Moteur web", ChipsFor(EngineChoices, _settings.SearchEngine, v =>
            {
                _settings.SearchEngine = v;
                Save();
                RenderSettings();
            })));

            SettingsList.Children.Add(Row("Récents", ChipsFor(RecentChoices, _settings.RecentCount, v =>
            {
                _settings.RecentCount = v;
                _recentsAt = DateTime.MinValue;
                Save();
                RenderSettings();
            })));
        }
    }
}









