using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Navbar
{
    // Regroupements d'onglets : « Aujourd'hui » (météo et agenda côte à côte) et « Minuteur » (minuteur et eau via un sélecteur)
    public partial class NotchWindow
    {
        private Grid? _todayGrid;
        private ColumnDefinition? _todayWeatherCol, _todayCalCol;
        private Border? _todayDivider;
        private StackPanel? _homeBar;

        private static bool InToday(HoverView view) => view is HoverView.Weather or HoverView.Calendar;

        private void InitToday()
        {
            // Météo à gauche, agenda à droite, séparés par un fin trait vertical ; les deux pages existantes sont déplacées telles quelles
            if (WeatherView.Parent is Panel parent && CalView.Parent == parent)
            {
                parent.Children.Remove(WeatherView);
                parent.Children.Remove(CalView);

                _todayWeatherCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                _todayCalCol = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                _todayGrid = new Grid { Visibility = Visibility.Collapsed, RenderTransform = new TranslateTransform() };
                _todayGrid.ColumnDefinitions.Add(_todayWeatherCol);
                _todayGrid.ColumnDefinitions.Add(_todayCalCol);

                WeatherView.Margin = new Thickness(0, 0, 11 * _scale, 0);
                CalView.Margin = new Thickness(11 * _scale, 0, 0, 0);
                Grid.SetColumn(WeatherView, 0);
                Grid.SetColumn(CalView, 1);
                _todayDivider = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Background = FgAlpha(0x1F), Margin = new Thickness(0, 4 * _scale, 0, 4 * _scale) };
                Grid.SetColumn(_todayDivider, 1);

                _todayGrid.Children.Add(WeatherView);
                _todayGrid.Children.Add(_todayDivider);
                _todayGrid.Children.Add(CalView);
                parent.Children.Add(_todayGrid);
            }

            // Minuteur / Eau : un sélecteur en tête de chaque page (une rangée de plus, le contenu descend d'une rangée)
            foreach (var (view, me) in new[] { (TimerView, HoverView.Timer), (WaterView, HoverView.Water) })
            {
                foreach (UIElement child in view.Children) Grid.SetRow(child, Grid.GetRow(child) + 1);
                view.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
                var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6 * _scale) };
                Grid.SetRow(bar, 0);
                view.Children.Add(bar);
                _timeBars.Add((me, bar));
            }
            TimerToggle.ToolTip = "Minuteur et eau";
        }

        private readonly List<(HoverView View, StackPanel Bar)> _timeBars = new();
        /// <summary>Affiche la page « Aujourd'hui » et ses deux moitiés selon les modules activés ; met à jour le sélecteur de l'accueil.</summary>
        private void ApplyToday()
        {
            if (_todayGrid != null)
            {
                bool weather = _settings.ShowWeather, calendar = _settings.ShowCalendar;
                WeatherView.Visibility = weather ? Visibility.Visible : Visibility.Collapsed;
                CalView.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
                _todayDivider!.Visibility = weather && calendar ? Visibility.Visible : Visibility.Collapsed;
                _todayWeatherCol!.Width = weather ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                _todayCalCol!.Width = calendar ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                WeatherView.Margin = new Thickness(0, 0, calendar ? 11 * _scale : 0, 0);
                CalView.Margin = new Thickness(weather ? 11 * _scale : 0, 0, 0, 0);
                _todayGrid.Visibility = InToday(_view) ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateTimeBars();
        }

        private void UpdateTimeBars()
        {
            var entries = new List<(string Label, HoverView View)>();
            if (_settings.ShowTimer) entries.Add(("Minuteur", HoverView.Timer));
            if (_settings.ShowWater) entries.Add(("Eau", HoverView.Water));
            foreach (var (me, bar) in _timeBars)
            {
                bar.Children.Clear();
                if (entries.Count < 2) continue;   // un seul module : pas de sélecteur
                foreach (var (label, target) in entries)
                {
                    bool active = target == me;
                    var chip = new Border
                    {
                        Child = new TextBlock { Text = label, Foreground = active ? Fg() : FgAlpha(0x99), FontSize = 11 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont },
                        Background = FgAlpha(active ? (byte)0x30 : (byte)0x12), CornerRadius = new CornerRadius(10 * _scale),
                        Padding = new Thickness(11 * _scale, 2 * _scale, 11 * _scale, 2 * _scale), Margin = new Thickness(0, 0, 5 * _scale, 0), Cursor = Cursors.Hand,
                    };
                    if (!active)
                    {
                        Press.Attach(chip, 0.94);
                        chip.MouseLeftButtonUp += (_, _) => GoTo(target);
                    }
                    bar.Children.Add(chip);
                }
            }
        }
    }
}