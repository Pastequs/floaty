using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace Navbar
{
    // Thèmes sombre, clair, système et « verre liquide », indépendants de la couleur d'accent
    public partial class NotchWindow
    {
        private bool _light;
        private bool _glass;
        private Color _fg = Colors.White, _bg = Colors.Black;

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }

        private bool ResolveLight() => _settings.Theme switch
        {
            "light" or "paper" or "sakura" => true,
            "system" => SystemUsesLightTheme(),
            _ => false,   // sombre et verre : texte clair
        };

        private void InitTheme()
        {
            _glass = _settings.Theme == "glass";
            _light = ResolveLight();
            _fg = _light ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White;
            _bg = BackgroundColor();

            Resources["BgBrush"] = new SolidColorBrush(_bg);
            Resources["FgBrush"] = new SolidColorBrush(_fg);
            Resources["DimBrush"] = FgAlpha(0x99);
            Resources["FaintBrush"] = FgAlpha(0x66);
            Resources["TrackBrush"] = FgAlpha(0x33);
            Resources["ChipBrush"] = FgAlpha(_glass ? (byte)0x2A : (byte)0x22);

            ApplyGlass();
        }

        /// <summary>Ambiances : un fond teinté et un accent assorti, en plus de sombre / clair / verre.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, (Color Bg, string Accent)> Ambiances = new()
        {
            ["night"] = (Color.FromRgb(0x0C, 0x12, 0x2C), "#7AA2FF"),
            ["paper"] = (Color.FromRgb(0xF3, 0xEA, 0xD7), "#C2410C"),
            ["fruit"] = (Color.FromRgb(0x2A, 0x0E, 0x16), "#FF4D6D"),
            ["neon"] = (Color.FromRgb(0x0B, 0x06, 0x1E), "#B026FF"),
            ["ocean"] = (Color.FromRgb(0x05, 0x1A, 0x26), "#2DD4BF"),
            ["forest"] = (Color.FromRgb(0x08, 0x1C, 0x12), "#4ADE80"),
            ["sakura"] = (Color.FromRgb(0xFB, 0xE9, 0xEF), "#DB2777"),
            ["terminal"] = (Color.FromRgb(0x00, 0x00, 0x00), "#22FF66"),
        };
        private static readonly System.Collections.Generic.Dictionary<string, string> AmbianceAccents = Ambiances.ToDictionary(a => a.Key, a => a.Value.Accent);

        private Color BackgroundColor() =>
            Ambiances.TryGetValue(_settings.Theme, out var ambiance) ? ambiance.Bg :
            _glass ? GlassColor(_settings.GlassOpacity)
                   : ShadeColor(_light, _light ? _settings.ShadeLight : _settings.ShadeDark);

        /// <summary>Noir pur → gris foncé (0 à 1) ou blanc pur → gris clair, pour nuancer légèrement le fond.</summary>
        private static Color ShadeColor(bool light, double shade)
        {
            shade = Math.Clamp(shade, 0, 1);
            byte Lerp(int from, int to) => (byte)Math.Round(from + (to - from) * shade);
            return light ? Color.FromRgb(Lerp(255, 216), Lerp(255, 216), Lerp(255, 220))
                         : Color.FromRgb(Lerp(0, 46), Lerp(0, 46), Lerp(0, 50));
        }

        /// <summary>Teinte du verre : bleu nuit translucide ; <paramref name="opacity"/> règle la densité (0 = très clair).</summary>
        private static Color GlassColor(double opacity)
        {
            opacity = Math.Clamp(opacity, 0, 1);
            return Color.FromArgb((byte)Math.Round(0x38 + opacity * 0xA0), 22, 26, 40);
        }

        /// <summary>Applique la nuance ou la transparence choisie tout de suite (le curseur la modifie en direct).</summary>
        private void ApplyShade()
        {
            _bg = BackgroundColor();
            Resources["BgBrush"] = new SolidColorBrush(_bg);
        }

        // ───────────── Verre liquide ─────────────

        /// <summary>
        /// Effet « liquid glass » : fond translucide, reflet lumineux sur le haut, liseré qui s'éclaircit vers les bords.
        /// (Windows ne permet pas de flouter ce qui se trouve derrière une fenêtre transparente : le verre reste net.)
        /// </summary>
        private void ApplyGlass()
        {
            if (!_glass)
            {
                Island.BorderThickness = new Thickness(0);
                Island.BorderBrush = null;
                GlassSheen.Visibility = Visibility.Collapsed;
                GlassSheen.Background = null;
                return;
            }

            Island.BorderThickness = new Thickness(1.2 * _scale);
            Island.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xB0, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0x45, 255, 255, 255), 0.35),
                    new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 1),
                },
            };

            GlassSheen.Visibility = Visibility.Visible;
            GlassSheen.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x58, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0x16, 255, 255, 255), 0.30),
                    new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.62),
                    new GradientStop(Color.FromArgb(0x1C, 255, 255, 255), 1),
                },
            };
        }

        private SolidColorBrush FgAlpha(byte alpha) => new(Color.FromArgb(alpha, _fg.R, _fg.G, _fg.B));
        private SolidColorBrush Fg() => new(_fg);

        // ───────────── Accent ─────────────

        /// <summary>Accent « dégradé » : cyan → bleu.</summary>
        private const string GradientAccent = "gradient";

        /// <summary>Couleur d'accent ; le blanc sur thème clair devient du noir pour rester visible.</summary>
        private Brush EffectiveAccent()
        {
            if (_settings.AccentFromArt && _artAccent is { } art) return new SolidColorBrush(art);
            if (UsesDynAccent && _dynAccent is { } dyn) return new SolidColorBrush(dyn);
            if (_settings.Accent == GradientAccent)
                return new LinearGradientBrush(Color.FromRgb(0x66, 0xDB, 0xFF), Color.FromRgb(0x2D, 0x6B, 0xFF), 0);

            var accent = (Color)ColorConverter.ConvertFromString(_settings.Accent);
            if (_light && accent == Colors.White) accent = _fg;
            return new SolidColorBrush(accent);
        }

        /// <summary>Remplissage des barres : identique à l'accent (dégradé compris).</summary>
        private Brush AccentFill() => EffectiveAccent();

        /// <summary>Couleur unie équivalente à l'accent (texte, icônes, calcul du contraste).</summary>
        private Color EffectiveAccentColor()
        {
            if (_settings.AccentFromArt && _artAccent is { } art) return art;
            if (UsesDynAccent && _dynAccent is { } dyn) return dyn;
            if (_settings.Accent == GradientAccent) return Color.FromRgb(0x19, 0xB8, 0xFF);

            var accent = (Color)ColorConverter.ConvertFromString(_settings.Accent);
            return _light && accent == Colors.White ? _fg : accent;
        }

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || _settings.Theme != "system") return;
            Dispatcher.BeginInvoke(() =>
            {
                if (ResolveLight() != _light) RebuildRequested?.Invoke(false);
            });
        }
    }
}

