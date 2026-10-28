using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Navbar.Services;
using Color = System.Windows.Media.Color;

namespace Navbar
{
    // Ressources système : cartes (valeur + courbe récente) pour processeur, mémoire, carte graphique et disque ; réseau ; processus les plus gourmands
    public partial class NotchWindow
    {
        private sealed class SysTile
        {
            public required Border Root { get; init; }
            public required TextBlock Value { get; init; }
            public required TextBlock Label { get; init; }
            public required TextBlock Sub { get; init; }
            public required Canvas Spark { get; init; }
            public required Border Bar { get; init; }
            public required Border BarTrack { get; init; }
        }

        private readonly Dictionary<string, SysTile> _sysTiles = new();
        private readonly Dictionary<string, List<double>> _sysHistory = new();
        private bool _sysTilesBuilt;
        private const int SysHistoryLength = 45;

        private static readonly Color LoadMedium = Color.FromRgb(0xFB, 0x92, 0x3C);
        private static readonly Color LoadHigh = Color.FromRgb(0xFF, 0x46, 0x46);
        private static readonly Color LoadLow = Color.FromRgb(0x34, 0xD3, 0x99);

        private void InitSystemView()
        {
            SysIcon.Stroke = FgAlpha(0x99);
            SysToggle.Width = SysToggle.Height = 24 * _scale;
            SysToggle.Padding = new Thickness(4.5 * _scale);
            SysLine.FontSize = 11 * _scale;
            SysLine.Margin = new Thickness(0, 5 * _scale, 0, 6 * _scale);
            PillSys.FontSize = 13 * _scale;
            PillSys.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);

            Shared.SystemUpdated += OnSystemUpdated;
        }

        private void SysToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.System);

        /// <summary>Demande les mesures tant que le module est affiché (et libère l'échantillonneur sinon).</summary>
        private void UpdateSystemSampling() => Shared.WantSystemStats(this, _view == HoverView.System);

        private void OnSystemUpdated(SysSnapshot snapshot)
        {
            if (_view == HoverView.System) RenderSystem(snapshot);
            UpdateMascotMood();
            RefreshPill();
        }

        // ───────────── Cartes ─────────────

        private SysTile BuildTile(string key, string label)
        {
            var labelText = new TextBlock { Text = label, Foreground = FgAlpha(0x88), FontSize = 10 * _scale, FontFamily = UiFont, FontWeight = FontWeights.SemiBold };
            var value = new TextBlock { Foreground = Fg(), FontWeight = FontWeights.SemiBold, FontSize = 26 * _scale, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Margin = new Thickness(0, 1 * _scale, 0, 0) };
            var sub = new TextBlock { Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis };
            var spark = new Canvas { Height = (_settings.SysTopProcesses ? 30 : 84) * _scale, Margin = new Thickness(0, 5 * _scale, 0, 5 * _scale), ClipToBounds = true };
            var bar = new Border { HorizontalAlignment = HorizontalAlignment.Left, Height = 3 * _scale, CornerRadius = new CornerRadius(1.5 * _scale), Width = 0 };
            var track = new Border { Child = bar, Height = 3 * _scale, CornerRadius = new CornerRadius(1.5 * _scale), Background = FgAlpha(0x22) };
            track.SizeChanged += (_, _) => { if (_sysTiles.TryGetValue(key, out var t)) t.Bar.Width = track.ActualWidth * (double)(t.Bar.Tag ?? 0.0); };

            var stack = new StackPanel();
            stack.Children.Add(labelText);
            stack.Children.Add(value);
            stack.Children.Add(sub);
            stack.Children.Add(spark);
            stack.Children.Add(track);

            var card = new Border
            {
                Child = stack, Background = FgAlpha(0x12), CornerRadius = new CornerRadius(12 * _scale),
                Padding = new Thickness(10 * _scale, 7 * _scale, 10 * _scale, 8 * _scale), Margin = new Thickness(0, 0, 6 * _scale, 0),
            };
            var tile = new SysTile { Root = card, Value = value, Label = labelText, Sub = sub, Spark = spark, Bar = bar, BarTrack = track };
            _sysTiles[key] = tile;
            _sysHistory.TryAdd(key, new List<double>());
            return tile;
        }

        /// <summary>Les cartes dépendent du matériel : la carte graphique ou le disque peuvent ne pas exposer de mesure.</summary>
        private void BuildTilesFor(SysSnapshot s)
        {
            SysTiles.Children.Clear();
            _sysTiles.Clear();

            SysTiles.Children.Add(BuildTile("cpu", "PROCESSEUR").Root);
            SysTiles.Children.Add(BuildTile("ram", "MÉMOIRE").Root);
            if (s.Gpu != null) SysTiles.Children.Add(BuildTile("gpu", "GRAPHIQUE").Root);
            if (s.Disk != null) SysTiles.Children.Add(BuildTile("disk", "DISQUE").Root);
            SysTiles.Columns = SysTiles.Children.Count;
            _sysTilesBuilt = true;
        }

        /// <summary>Vert au repos, orange puis rouge quand la charge monte.</summary>
        private static Color LoadColor(double percent) =>
            percent >= 85 ? LoadHigh : percent >= 60 ? LoadMedium : LoadLow;

        private void SetTile(string key, double percent, string sub)
        {
            if (!_sysTiles.TryGetValue(key, out var tile)) return;
            var color = LoadColor(percent);
            var brush = new SolidColorBrush(color);

            tile.Value.Text = $"{Math.Round(percent)} %";
            tile.Value.Foreground = brush;
            tile.Sub.Text = sub;

            tile.Bar.Background = brush;
            tile.Bar.Tag = Math.Clamp(percent / 100.0, 0, 1);
            tile.Bar.Width = tile.BarTrack.ActualWidth * Math.Clamp(percent / 100.0, 0, 1);

            var history = _sysHistory[key];
            history.Add(percent);
            if (history.Count > SysHistoryLength) history.RemoveAt(0);
            DrawSpark(tile.Spark, history, color);
        }

        /// <summary>Courbe des dernières mesures, avec un dégradé léger dessous (échelle fixe 0 – 100 %).</summary>
        private void DrawSpark(Canvas canvas, List<double> history, Color color)
        {
            canvas.Children.Clear();
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 78 * _scale, h = canvas.Height;
            if (history.Count < 2) return;

            var line = new PointCollection();
            for (int i = 0; i < history.Count; i++)
                line.Add(new Point(i * w / (SysHistoryLength - 1) + w * (1 - (history.Count - 1) / (double)(SysHistoryLength - 1)), h - 1 - history[i] / 100.0 * (h - 2)));

            var area = new PointCollection(line) { new Point(line[^1].X, h), new Point(line[0].X, h) };
            canvas.Children.Add(new Polygon
            {
                Points = area,
                Fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90),
            });
            canvas.Children.Add(new Polyline { Points = line, Stroke = new SolidColorBrush(color), StrokeThickness = 1.4 * _scale, StrokeLineJoin = PenLineJoin.Round });
        }

        private void RenderSystem(SysSnapshot s)
        {
            if (!_sysTilesBuilt) BuildTilesFor(s);

            SetTile("cpu", s.Cpu, s.TempC is { } t ? $"{Math.Round(t)}°C" : "charge globale");
            SetTile("ram", s.RamPercent, $"{Gigabytes(s.RamUsed)} / {Gigabytes(s.RamTotal)} Go");
            if (s.Gpu is { } gpu) SetTile("gpu", gpu, "moteur 3D");
            if (s.Disk is { } disk) SetTile("disk", disk, "activité");

            // Réseau, latence et durée d'activité sur une ligne colorée
            var up = s.Uptime;
            string uptime = up.TotalDays >= 1 ? $"{(int)up.TotalDays} j {up.Hours} h" : $"{(int)up.TotalHours} h {up.Minutes:00}";
            SysLine.Inlines.Clear();
            SysLine.Inlines.Add(new System.Windows.Documents.Run("↓ " + Rate(s.NetDown)) { Foreground = new SolidColorBrush(LoadLow), FontWeight = FontWeights.SemiBold });
            SysLine.Inlines.Add(new System.Windows.Documents.Run("   ↑ " + Rate(s.NetUp)) { Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)), FontWeight = FontWeights.SemiBold });
            if (s.PingMs is { } ms)
                SysLine.Inlines.Add(new System.Windows.Documents.Run($"   ●  {ms} ms") { Foreground = new SolidColorBrush(ms < 60 ? LoadLow : ms < 150 ? LoadMedium : LoadHigh) });
            SysLine.Inlines.Add(new System.Windows.Documents.Run($"   ·   allumé depuis {uptime}") { Foreground = FgAlpha(0x77) });

            SysTop.Visibility = _settings.SysTopProcesses ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.SysTopProcesses)
            {
                FillTop(SysTopCpu, "PROCESSEUR", s.TopCpu, p => $"{Math.Round(p.Cpu)} %", p => p.Cpu);
                FillTop(SysTopMem, "MÉMOIRE", s.TopMemory, p => Memory(p.MemoryBytes), p => p.MemoryBytes);
            }
        }

        /// <summary>Liste des plus gourmands : chaque ligne a une barre de fond proportionnelle au premier.</summary>
        private void FillTop(StackPanel panel, string title, IReadOnlyList<ProcInfo> items, Func<ProcInfo, string> text, Func<ProcInfo, double> measure)
        {
            panel.Children.Clear();
            panel.Margin = new Thickness(0, 0, 8 * _scale, 0);
            panel.Children.Add(new TextBlock
            {
                Text = title, Foreground = FgAlpha(0x66), FontSize = 10 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont, Margin = new Thickness(0, 0, 0, 3 * _scale),
            });

            double max = items.Count > 0 ? Math.Max(1e-9, items.Max(measure)) : 1;
            var accent = EffectiveAccentColor();
            foreach (var p in items)
            {
                double ratio = Math.Clamp(measure(p) / max, 0, 1);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2 * _scale), Height = 17 * _scale };
                var fill = new Border { Background = new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)), CornerRadius = new CornerRadius(5 * _scale), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                row.SizeChanged += (_, _) => fill.Width = row.ActualWidth * ratio;
                row.Children.Add(fill);

                var content = new Grid { Margin = new Thickness(6 * _scale, 0, 6 * _scale, 0) };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.Children.Add(new TextBlock { Text = p.Name, Foreground = Fg(), FontSize = 11 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var v = new TextBlock { Text = text(p), Foreground = FgAlpha(0xBB), FontSize = 11 * _scale, FontFamily = UiFont, Margin = new Thickness(6 * _scale, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(v, 1);
                content.Children.Add(v);
                row.Children.Add(content);
                panel.Children.Add(row);
            }
        }
        // ───────────── Formats ─────────────

        private static string Gigabytes(long bytes) => (bytes / 1073741824.0).ToString("0.#", CultureInfo.CurrentCulture);

        private static string Memory(long bytes) =>
            bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.0} Go" : $"{bytes / 1048576.0:0} Mo";

        private static string Rate(double bytesPerSecond) =>
            bytesPerSecond >= 1048576 ? $"{bytesPerSecond / 1048576.0:0.0} Mo/s"
            : bytesPerSecond >= 1024 ? $"{bytesPerSecond / 1024.0:0} Ko/s"
            : $"{bytesPerSecond:0} o/s";

        // ───────────── Réglages de la rubrique ─────────────

        private void RenderSystemSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("Processus gourmands", _settings.SysTopProcesses, v =>
            {
                _settings.SysTopProcesses = v;
                Save();
                UpdateSystemSampling();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Options", options));
        }
    }
}



