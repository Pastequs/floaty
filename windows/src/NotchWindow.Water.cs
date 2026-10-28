using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Suivi de la consommation d'eau : objectif, bouteille, historique par heure
    public partial class NotchWindow
    {
        private List<WaterEntry> _water => Shared.Water;

        private int TodayMl => Shared.TodayMl;

        private static string Liters(int ml) => (ml / 1000.0).ToString("0.##", CultureInfo.CurrentCulture);

        private void InitWater()
        {

            WaterTotal.FontSize = 40 * _scale;
            WaterSub.FontSize = 12 * _scale;
            WaterAddText.FontSize = 13 * _scale;
            WaterAddBtn.CornerRadius = new CornerRadius(10 * _scale);
            WaterAddBtn.Padding = new Thickness(11 * _scale, 5 * _scale, 11 * _scale, 5 * _scale);
            WaterUndoBtn.FontSize = 15 * _scale;
            WaterUndoBtn.Width = 30 * _scale;
            WaterUndoBtn.Padding = new Thickness(0, 5 * _scale, 0, 5 * _scale);

            WaterTrack.Height = WaterFill.Height = 12 * _scale;
            WaterTrack.CornerRadius = WaterFill.CornerRadius = new CornerRadius(6 * _scale);
            WaterTrack.Margin = new Thickness(0, 6 * _scale, 0, 0);
            WaterChart.Margin = new Thickness(0, 8 * _scale, 0, 0);
            WaterTrack.SizeChanged += (_, _) => WaterFill.Width = WaterTrack.ActualWidth * _waterRatio;

            RenderWater();
        }

        private double _waterRatio;

        private void RenderWater()
        {
            int total = TodayMl, goal = Math.Max(1, _settings.WaterGoalMl);
            bool reached = total >= goal;

            WaterTotal.Text = $"{Liters(total)} / {Liters(goal)} L";
            WaterSub.Text = reached ? $"{Math.Round(total * 100.0 / goal)} % · Objectif atteint"
                                    : $"{Math.Round(total * 100.0 / goal)} % · Aujourd'hui";
            WaterAddText.Text = $"+ {_settings.BottleMl} ml";

            _waterRatio = Math.Clamp(total / (double)goal, 0, 1);
            WaterFill.Background = AccentFill();
            WaterFill.Width = WaterTrack.ActualWidth * _waterRatio;

            RenderWaterChart();
            RefreshPill();
        }

        // Histogramme des prises d'eau, une barre par heure de la plage de rappel
        private void RenderWaterChart()
        {
            WaterChart.Children.Clear();
            WaterChart.ColumnDefinitions.Clear();

            int from = Math.Clamp(_settings.WaterFromHour, 0, 22), to = Math.Clamp(_settings.WaterToHour, from + 1, 23);
            var perHour = _water.Where(e => e.At.Date == DateTime.Today)
                                .GroupBy(e => e.At.Hour).ToDictionary(g => g.Key, g => g.Sum(e => e.Ml));
            // Les prises hors de la plage de rappel restent visibles
            if (perHour.Count > 0) { from = Math.Min(from, perHour.Keys.Min()); to = Math.Max(to, perHour.Keys.Max()); }
            double scaleMax = Math.Max(_settings.BottleMl, perHour.Values.DefaultIfEmpty(0).Max());
            double barMax = 78 * _scale;

            for (int h = from; h <= to; h++)
            {
                WaterChart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                perHour.TryGetValue(h, out int ml);

                var column = new Grid();
                column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var bar = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 9 * _scale, CornerRadius = new CornerRadius(3 * _scale),
                    Height = ml > 0 ? Math.Max(3 * _scale, barMax * ml / scaleMax) : 2 * _scale,
                    Background = ml > 0 ? EffectiveAccent() : FgAlpha(0x22),
                    ToolTip = ml > 0 ? $"{h} h · {ml} ml" : null,
                };
                column.Children.Add(bar);

                var label = new TextBlock
                {
                    Text = (h - from) % 3 == 0 ? h.ToString("00") : "", FontSize = 9 * _scale, Foreground = FgAlpha(0x77),
                    FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2 * _scale, 0, 0),
                };
                Grid.SetRow(label, 1);
                column.Children.Add(label);

                Grid.SetColumn(column, h - from);
                WaterChart.Children.Add(column);
            }
        }

        private void WaterAdd_Click(object sender, MouseButtonEventArgs e)
        {
            bool wasReached = TodayMl >= _settings.WaterGoalMl;
            _water.Add(new WaterEntry { At = DateTime.Now, Ml = _settings.BottleMl });
            Storage.Save("water", _water);
            RenderWater();
            Shared.NotifyDataChanged(this);
            React(Watermelon.Reaction.Sip);

            if (!wasReached && TodayMl >= _settings.WaterGoalMl)
            {
                System.Media.SystemSounds.Asterisk.Play();
                ShowOverlay(new OverlayInfo("\U0001F4A7", 1, "Objectif d'eau atteint !"));
            }
        }

        private void WaterUndo_Click(object sender, MouseButtonEventArgs e)
        {
            var last = _water.LastOrDefault(x => x.At.Date == DateTime.Today);
            if (last == null) return;
            _water.Remove(last);
            Storage.Save("water", _water);
            RenderWater();
            Shared.NotifyDataChanged(this);
        }

        private void WaterSettings_Click(object sender, MouseButtonEventArgs e) => OpenSettingsPage(PageWater);

        private void WaterToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Water);

    }
}
