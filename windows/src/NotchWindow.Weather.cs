using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Météo : conditions actuelles et prévisions sur cinq jours (Open-Meteo)
    public partial class NotchWindow
    {
        private void InitWeather()
        {
            WeatherToggle.Width = WeatherToggle.Height = 24 * _scale;
            WeatherToggle.Padding = new Thickness(4.5 * _scale);
            WeatherIcon.Stroke = FgAlpha(0x99);
            WeatherNote.FontSize = 10 * _scale;
            WeatherExtra.FontSize = 11 * _scale;
            WeatherExtra.Margin = new Thickness(0, 6 * _scale, 0, 4 * _scale);
            WeatherModeBtn.CornerRadius = new CornerRadius(10 * _scale);
            WeatherModeBtn.Padding = new Thickness(9 * _scale, 1 * _scale, 9 * _scale, 1 * _scale);
            WeatherModeText.FontSize = 11 * _scale;
            Press.Attach(WeatherModeBtn, 0.94);
            PillWeatherBox.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);
            PillWeather.FontSize = 13 * _scale;
            PillWeather.Margin = new Thickness(4 * _scale, 0, 0, 0);

#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_WXHOURLY") == "1") _settings.WeatherHourly = true;   // vérification visuelle seulement (jamais enregistré)
#endif
            Shared.WeatherUpdated += OnWeatherUpdated;
            RenderWeather();
        }

        private void WeatherToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Weather);

        private void WeatherMode_Click(object sender, MouseButtonEventArgs e)
        {
            _settings.WeatherHourly = !_settings.WeatherHourly;
            Save();
            RenderWeather();
        }

        private static string UvLabel(double uv) => uv < 3 ? "faible" : uv < 6 ? "modéré" : uv < 8 ? "élevé" : uv < 11 ? "très élevé" : "extrême";

        /// <summary>Les prochaines heures, une colonne toutes les deux heures.</summary>
        private void FillHourly(System.Collections.Generic.IReadOnlyList<WeatherHour> hours)
        {
            for (int i = 0; i < hours.Count && WeatherDays.Children.Count < 5; i += 2)
            {
                var h = hours[i];
                var (_, kind) = WeatherService.Describe(h.Code, h.IsDay);
                var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                column.Children.Add(new TextBlock { Text = i == 0 ? "Maint." : h.Time.ToString("HH") + "h", Foreground = FgAlpha(i == 0 ? (byte)0xDD : (byte)0x88), FontSize = 11 * _scale, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center });
                column.Children.Add(new WeatherGlyph(kind, 24 * _scale) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3 * _scale, 0, 3 * _scale) });
                column.Children.Add(new TextBlock { Text = Degrees(h.Temp), Foreground = Fg(), FontSize = 12 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, HorizontalAlignment = HorizontalAlignment.Center });
                column.Children.Add(new TextBlock
                {
                    Text = h.RainPercent >= 15 ? $"{h.RainPercent} %" : " ", Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)), FontSize = 11 * _scale, FontFamily = UiFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                WeatherDays.Children.Add(ForecastCard(column, i == 0));
            }
        }

        /// <summary>Une prévision dans sa carte arrondie ; la première (aujourd'hui / maintenant) est mise en avant.</summary>
        private Border ForecastCard(UIElement content, bool first) => new()
        {
            Child = content, CornerRadius = new CornerRadius(12 * _scale), Padding = new Thickness(2 * _scale, 7 * _scale, 2 * _scale, 7 * _scale),
            Margin = new Thickness(2 * _scale, 0, 2 * _scale, 0), Background = FgAlpha(first ? (byte)0x24 : (byte)0x12),
        };

        private void OnWeatherUpdated() => Dispatcher.BeginInvoke(() => { RenderWeather(); RefreshPill(); });

        private string Degrees(double value) => $"{Math.Round(value)}°";

        private void RenderWeather()
        {
            var data = Shared.Weather;
            WeatherNow.Children.Clear();
            WeatherDays.Children.Clear();

            if (data == null)
            {
                WeatherNow.Children.Add(new TextBlock
                {
                    Text = _settings.ShowWeather ? "Chargement de la météo…" : "Météo désactivée", Foreground = FgAlpha(0x88),
                    FontSize = 13 * _scale, FontFamily = UiFont,
                });
                WeatherNote.Text = "";
                return;
            }

            var now = data.Now;
            var (label, kind) = WeatherService.Describe(now.Code, now.IsDay);
            string windUnit = _settings.WeatherFahrenheit ? "mph" : "km/h";

            // Conditions actuelles : pictogramme et grande température à gauche, état et détails alignés à droite
            var hero = new Grid { Margin = new Thickness(2 * _scale, 0, 2 * _scale, 4 * _scale) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new WeatherGlyph(kind, 38 * _scale));
            left.Children.Add(new TextBlock
            {
                Text = Degrees(now.Temp), Foreground = Fg(), FontWeight = FontWeights.SemiBold, FontSize = 34 * _scale,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10 * _scale, 0, 0, 0),
            });
            hero.Children.Add(left);
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 1);
            right.Children.Add(new TextBlock { Text = label, Foreground = Fg(), FontSize = 12 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, TextAlignment = TextAlignment.Right });
            right.Children.Add(new TextBlock { Text = $"Ressenti {Degrees(now.Feels)}", Foreground = FgAlpha(0x99), FontSize = 11 * _scale, FontFamily = UiFont, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 2 * _scale, 0, 0) });
            right.Children.Add(new TextBlock { Text = $"{Math.Round(now.Wind)} {windUnit} · {now.Humidity} %", Foreground = FgAlpha(0x88), FontSize = 11 * _scale, FontFamily = UiFont, TextAlignment = TextAlignment.Right });
            hero.Children.Add(right);
            WeatherNow.Children.Add(hero);
            // Cinq prochains jours, ou les prochaines heures
            bool hourly = _settings.WeatherHourly && data.Hours is { Count: > 0 };
            WeatherModeText.Text = hourly ? "Jours" : "Heures";
            if (hourly) FillHourly(data.Hours!);
            for (int i = 0; !hourly && i < Math.Min(5, data.Days.Count); i++)
            {
                var day = data.Days[i];
                var (_, dayKind) = WeatherService.Describe(day.Code, true);

                var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                column.Children.Add(new TextBlock
                {
                    Text = i == 0 ? "Auj." : day.Date.ToString("ddd", CultureInfo.CurrentCulture).TrimEnd('.'),
                    Foreground = FgAlpha(i == 0 ? (byte)0xDD : (byte)0x88), FontSize = 11 * _scale, FontFamily = UiFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                var glyph = new WeatherGlyph(dayKind, 24 * _scale) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3 * _scale, 0, 3 * _scale) };
                column.Children.Add(glyph);
                column.Children.Add(new TextBlock
                {
                    Text = Degrees(day.Max), Foreground = Fg(), FontSize = 12 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                column.Children.Add(new TextBlock
                {
                    Text = Degrees(day.Min), Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                WeatherDays.Children.Add(ForecastCard(column, i == 0));
            }
            WeatherDays.Margin = new Thickness(0);

            // UV du jour et pluie à venir
            WeatherExtra.Inlines.Clear();
            if (data.Uv is { } uv)
                WeatherExtra.Inlines.Add(new System.Windows.Documents.Run($"UV {Math.Round(uv)} · {UvLabel(uv)}") { Foreground = new SolidColorBrush(uv < 3 ? Color.FromRgb(0x34, 0xD3, 0x99) : uv < 6 ? Color.FromRgb(0xFA, 0xCC, 0x15) : uv < 8 ? Color.FromRgb(0xFB, 0x92, 0x3C) : Color.FromRgb(0xF8, 0x71, 0x71)) });
            string rain = data.RainingNow ? "il pleut" : data.RainInMin is { } rm ? $"pluie dans {rm} min" : "pas de pluie dans les 2 h";
            WeatherExtra.Inlines.Add(new System.Windows.Documents.Run((WeatherExtra.Inlines.Count > 0 ? "   ·   " : "") + rain) { Foreground = new SolidColorBrush(data.RainingNow || data.RainInMin != null ? Color.FromRgb(0x60, 0xA5, 0xFA) : FgAlpha(0x77).Color) });

            var age = DateTime.Now - data.At;
            string ago = age < TimeSpan.FromMinutes(1) ? "à l'instant" : $"il y a {(int)age.TotalMinutes} min";
            WeatherNote.Text = $"{data.City} · mis à jour {ago}";
        }

        // ───────────── Réglages de la rubrique ─────────────

        private void RenderWeatherSettings()
        {
            var units = new WrapPanel();
            units.Children.Add(Toggle("Alerte pluie", _settings.WeatherRainAlert, v => { _settings.WeatherRainAlert = v; Save(); RenderSettings(); }));
            units.Children.Add(Toggle("Fahrenheit", _settings.WeatherFahrenheit, v =>
            {
                _settings.WeatherFahrenheit = v;
                Save();
                _ = Shared.RefreshWeatherAsync(force: true);
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Options", units));

            // Ville : saisie libre ; vide = localisation approximative par adresse IP
            var cityBox = new TextBox
            {
                Text = _settings.WeatherCity, FontSize = 13 * _scale, FontFamily = UiFont, Foreground = Fg(), CaretBrush = Fg(),
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12 * _scale, 4 * _scale, 12 * _scale, 4 * _scale),
            };
            var hint = new TextBlock
            {
                Text = "Auto (d'après ton IP)", Foreground = FgAlpha(0x66), FontSize = 13 * _scale, FontFamily = UiFont,
                IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12 * _scale, 0, 0, 0),
                Visibility = _settings.WeatherCity.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
            };
            cityBox.PreviewMouseLeftButtonDown += (_, _) =>
            {
                if (cityBox.IsKeyboardFocusWithin) return;
                SetNoActivate(false);
                Activate();
                cityBox.Focus();
            };
            cityBox.GotKeyboardFocus += (_, _) => _typing = true;
            cityBox.LostKeyboardFocus += (_, _) => { _typing = false; SetNoActivate(true); CommitCity(cityBox.Text); };
            cityBox.TextChanged += (_, _) => hint.Visibility = cityBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            cityBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitCity(cityBox.Text); Keyboard.ClearFocus(); e.Handled = true; } };

            var frame = new Border
            {
                Background = FgAlpha(0x1F), CornerRadius = new CornerRadius(12 * _scale), Width = 220 * _scale,
                HorizontalAlignment = HorizontalAlignment.Left, Child = new Grid { Children = { hint, cityBox } },
            };
            SettingsList.Children.Add(Row("Ville", frame));

            SettingsList.Children.Add(Row("Source", new TextBlock
            {
                Text = "Open-Meteo, sans compte ni clé. Sans ville, la position est estimée à partir de ton adresse IP (service ipwho.is).",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }

        private void CommitCity(string city)
        {
            city = city.Trim();
            if (city == _settings.WeatherCity) return;
            _settings.WeatherCity = city;
            Save();
            _ = Shared.RefreshWeatherAsync(force: true);
        }
    }
}








