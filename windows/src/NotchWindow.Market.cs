using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Navbar.Services;

namespace Navbar
{
    // Bourse et crypto : liste de suivi choisie par l'utilisateur, cours Yahoo Finance
    public partial class NotchWindow
    {
        private static readonly Brush Up = Frozen(0x34, 0xD3, 0x99), Down = Frozen(0xF8, 0x71, 0x71);
        private CancellationTokenSource? _marketSearchCts;
        private bool _marketSearching;

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void InitMarket()
        {
            MarketToggle.Width = MarketToggle.Height = 24 * _scale;
            MarketToggle.Padding = new Thickness(4.5 * _scale);
            MarketIcon.Stroke = FgAlpha(0x99);
            MarketTitle.FontSize = 14 * _scale;
            MarketNote.FontSize = 11 * _scale;
            MarketNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            MarketAddBtn.CornerRadius = new CornerRadius(12 * _scale);
            MarketAddBtn.Padding = new Thickness(11 * _scale, 4 * _scale, 11 * _scale, 4 * _scale);
            MarketAddBtn.Background = FgAlpha(0x1F);
            MarketAddText.FontSize = 12 * _scale;
            MarketScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);
            MarketSearchFrame.Margin = new Thickness(0, 6 * _scale, 0, 0);
            MarketSearchFrame.CornerRadius = new CornerRadius(12 * _scale);
            MarketSearchBox.FontSize = MarketSearchHint.FontSize = 13 * _scale;
            MarketSearchBox.Margin = MarketSearchHint.Margin = new Thickness(12 * _scale, 4 * _scale, 12 * _scale, 4 * _scale);
            PillMarket.FontSize = 13 * _scale;
            PillMarket.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);

            Shared.MarketUpdated += OnMarketUpdated;
            Closed += (_, _) => Shared.MarketUpdated -= OnMarketUpdated;
            RenderMarket();
        }

        private void MarketToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Market);

        private void OnMarketUpdated() => Dispatcher.BeginInvoke(() => { if (!_marketSearching) RenderMarket(); RefreshPill(); });

        // ───────────── Liste de suivi ─────────────

        private void RenderMarket()
        {
            MarketList.Children.Clear();
            var list = _settings.Watchlist;
            MarketNote.Text = list.Count == 0 ? "" : "cours indicatifs";

            if (list.Count == 0)
            {
                MarketList.Children.Add(Hint("Rien à suivre. « + Ajouter » pour chercher une action ou une crypto."));
                return;
            }

            foreach (var symbol in list)
            {
                Shared.Quotes.TryGetValue(symbol, out var quote);
                MarketList.Children.Add(MarketRow(symbol, quote));
            }
        }

        private TextBlock Hint(string text) => new()
        {
            Text = text, Foreground = FgAlpha(0x77), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
        };

        private UIElement MarketRow(string symbol, Quote? q)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = symbol, Foreground = Fg(), FontSize = 13 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont });
            names.Children.Add(new TextBlock
            {
                Text = q?.Name ?? "…", Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            grid.Children.Add(names);

            if (q != null)
            {
                Brush tone = q.Change >= 0 ? Up : Down;
                var spark = Sparkline(q.Series, tone);
                Grid.SetColumn(spark, 1);
                grid.Children.Add(spark);

                var numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10 * _scale, 0, 0, 0), MinWidth = 78 * _scale };
                numbers.Children.Add(new TextBlock
                {
                    Text = MarketService.FormatPrice(q.Price, q.Currency), Foreground = Fg(), FontSize = 13 * _scale,
                    FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Right,
                });
                numbers.Children.Add(new TextBlock
                {
                    Text = (q.ChangePercent >= 0 ? "+" : "") + q.ChangePercent.ToString("0.00", CultureInfo.CurrentCulture) + " %",
                    Foreground = tone, FontSize = 11 * _scale, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Right,
                });
                Grid.SetColumn(numbers, 2);
                grid.Children.Add(numbers);
            }

            var remove = new TextBlock
            {
                Text = "", FontFamily = IconFont, FontSize = 10 * _scale, Foreground = FgAlpha(0x55), Cursor = Cursors.Hand,
                Margin = new Thickness(8 * _scale, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ne plus suivre",
            };
            remove.MouseEnter += (_, _) => remove.Foreground = Down;
            remove.MouseLeave += (_, _) => remove.Foreground = FgAlpha(0x55);
            remove.MouseLeftButtonUp += (_, _) => RemoveSymbol(symbol);
            Grid.SetColumn(remove, 3);
            grid.Children.Add(remove);

            return new Border
            {
                Child = grid, Background = FgAlpha(0x14), CornerRadius = new CornerRadius(10 * _scale),
                Padding = new Thickness(10 * _scale, 4 * _scale, 10 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
            };
        }

        /// <summary>Petite courbe de la journée, dessinée en vectoriel.</summary>
        private FrameworkElement Sparkline(IReadOnlyList<double> series, Brush tone)
        {
            double w = 64 * _scale, h = 20 * _scale;
            var canvas = new Canvas { Width = w, Height = h, VerticalAlignment = VerticalAlignment.Center };
            if (series.Count < 2) return canvas;

            double min = series.Min(), max = series.Max(), span = Math.Max(1e-9, max - min);
            var points = new PointCollection();
            for (int i = 0; i < series.Count; i++)
                points.Add(new Point(i * w / (series.Count - 1), h - (series[i] - min) / span * (h - 2) - 1));
            canvas.Children.Add(new Polyline
            {
                Points = points, Stroke = tone, StrokeThickness = 1.5 * _scale, StrokeLineJoin = PenLineJoin.Round,
            });
            return canvas;
        }

        private void RemoveSymbol(string symbol)
        {
            _settings.Watchlist.Remove(symbol);
            Save();
            _ = Shared.RefreshMarketAsync();
            Shared.NotifyMarketChanged();
            RenderMarket();
            RefreshPill();
        }

        // ───────────── Ajout d'un titre ─────────────

        private void MarketAdd_Click(object sender, MouseButtonEventArgs e)
        {
            if (MarketSearchFrame.Visibility == Visibility.Visible) { CloseMarketSearch(); return; }

            MarketSearchFrame.Visibility = Visibility.Visible;
            MarketAddText.Text = "Fermer";
            _marketSearching = true;
            MarketList.Children.Clear();
            MarketList.Children.Add(Hint("Tape le nom d'une entreprise ou d'une crypto, puis choisis dans la liste."));

            SetNoActivate(false);
            Activate();
            Dispatcher.BeginInvoke(() => MarketSearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CloseMarketSearch()
        {
            _marketSearchCts?.Cancel();
            MarketSearchBox.Text = "";
            MarketSearchFrame.Visibility = Visibility.Collapsed;
            MarketAddText.Text = "+ Ajouter";
            _marketSearching = false;
            Keyboard.ClearFocus();
            RenderMarket();
        }

        private void MarketSearchBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (MarketSearchBox.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            MarketSearchBox.Focus();
        }

        private void MarketSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseMarketSearch(); e.Handled = true; }
        }

        private async void MarketSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarketSearchHint.Visibility = MarketSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string query = MarketSearchBox.Text.Trim();
            _marketSearchCts?.Cancel();
            if (query.Length < 2) return;

            var cts = _marketSearchCts = new CancellationTokenSource();
            try
            {
                await System.Threading.Tasks.Task.Delay(350, cts.Token);   // on attend la fin de la frappe
                var hits = await Shared.Market.SearchAsync(query);
                if (cts.IsCancellationRequested) return;
                RenderHits(hits);
            }
            catch (OperationCanceledException) { }
        }

        private void RenderHits(List<SymbolHit> hits)
        {
            MarketList.Children.Clear();
            if (hits.Count == 0) { MarketList.Children.Add(Hint("Aucun résultat.")); return; }

            foreach (var hit in hits)
            {
                bool tracked = _settings.Watchlist.Contains(hit.Symbol);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = hit.Symbol, Foreground = Fg(), FontSize = 13 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont,
                });
                text.Children.Add(new TextBlock
                {
                    Text = $"{hit.Name} · {MarketService.TypeLabel(hit.Type)}" + (hit.Exchange.Length > 0 ? " · " + hit.Exchange : ""),
                    Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(text);
                var mark = new TextBlock
                {
                    Text = tracked ? "" : "", FontFamily = IconFont, FontSize = 12 * _scale,
                    Foreground = tracked ? Up : FgAlpha(0x99), VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(mark, 1);
                grid.Children.Add(mark);

                var card = new Border
                {
                    Child = grid, Cursor = Cursors.Hand, Background = FgAlpha(0x14), CornerRadius = new CornerRadius(10 * _scale),
                    Padding = new Thickness(10 * _scale, 4 * _scale, 10 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                };
                card.MouseEnter += (_, _) => card.Background = FgAlpha(0x22);
                card.MouseLeave += (_, _) => card.Background = FgAlpha(0x14);
                var captured = hit;
                card.MouseLeftButtonUp += (_, _) => AddSymbol(captured.Symbol);
                MarketList.Children.Add(card);
            }
        }

        private void AddSymbol(string symbol)
        {
            if (!_settings.Watchlist.Contains(symbol)) _settings.Watchlist.Add(symbol);
            Save();
            _ = Shared.RefreshMarketAsync(force: true);
            CloseMarketSearch();
            RefreshPill();
        }

        // ───────────── Pastille fermée ─────────────

        /// <summary>« BTC 52 310 € +1,2 % » pour le premier titre suivi.</summary>
        private bool FillMarketPill()
        {
            if (!_settings.PillMarket || _settings.Watchlist.Count == 0) return false;
            if (!Shared.Quotes.TryGetValue(_settings.Watchlist[0], out var q)) return false;

            string label = q.Symbol.Split('-')[0];
            PillMarket.Text = $"{label} {MarketService.FormatPrice(q.Price, q.Currency)} {(q.ChangePercent >= 0 ? "▲" : "▼")}{Math.Abs(q.ChangePercent):0.0} %";
            PillMarket.Foreground = q.Change >= 0 ? Up : Down;
            return true;
        }

        // ───────────── Alertes de prix ─────────────

        private string _alertSymbol = "";
        private bool _alertAbove = true;

        private void RenderPriceAlertSettings()
        {
            // Alertes existantes : un clic sur l'une d'elles la supprime
            var current = new WrapPanel();
            foreach (var alert in _settings.PriceAlerts.ToList())
            {
                var captured = alert;
                string label = $"{alert.Symbol.Split('-')[0]} {(alert.Above ? ">" : "<")} {alert.Price.ToString("0.####", CultureInfo.CurrentCulture)}" + (alert.Fired ? " ✓" : "") + "  ✕";
                current.Children.Add(Chip(label, false, () => { _settings.PriceAlerts.Remove(captured); Save(); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Alertes", current.Children.Count > 0 ? current : Hint("Aucune alerte. Ajoute un seuil ci-dessous.")));

            if (_settings.Watchlist.Count == 0) return;
            if (!_settings.Watchlist.Contains(_alertSymbol)) _alertSymbol = _settings.Watchlist[0];

            var symbols = new WrapPanel();
            foreach (var s in _settings.Watchlist)
            {
                var captured = s;
                symbols.Children.Add(Chip(s.Split('-')[0], _alertSymbol == s, () => { _alertSymbol = captured; RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Titre", symbols));

            var direction = new WrapPanel();
            direction.Children.Add(Chip("Au-dessus de", _alertAbove, () => { _alertAbove = true; RenderSettings(); }));
            direction.Children.Add(Chip("Sous", !_alertAbove, () => { _alertAbove = false; RenderSettings(); }));
            SettingsList.Children.Add(Row("Quand", direction));

            Shared.Quotes.TryGetValue(_alertSymbol, out var quote);
            var price = SettingsInput("", quote != null ? $"Seuil, ex. {quote.Price:0.##}" : "Seuil", false, value =>
            {
                string text = value.Trim().Replace(',', '.');
                if (text.Length == 0) return;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double threshold) || threshold <= 0) return;
                _settings.PriceAlerts.Add(new PriceAlert { Symbol = _alertSymbol, Above = _alertAbove, Price = threshold });
                Save();
                _ = Shared.RefreshMarketAsync(force: true);
                RenderSettings();
            });
            SettingsList.Children.Add(Row("Seuil", price));
        }

        // ───────────── Réglages ─────────────

        private void RenderMarketSettings()
        {

            SettingsList.Children.Add(Row("Suivi", new TextBlock
            {
                Text = _settings.Watchlist.Count == 0 ? "Aucun titre." : string.Join(" · ", _settings.Watchlist)
                     + "\nAjoute ou retire des titres depuis l'onglet Bourse.",
                Foreground = FgAlpha(0x99), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));

            RenderPriceAlertSettings();

            SettingsList.Children.Add(Row("Source", new TextBlock
            {
                Text = "Yahoo Finance, sans compte ni clé. Cours parfois différés, à titre indicatif : ce n'est pas un conseil financier.",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
    }
}


