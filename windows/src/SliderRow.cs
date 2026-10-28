using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Navbar
{
    /// <summary>Ligne « icône – curseur – valeur » utilisée pour le volume et la luminosité.</summary>
    public sealed class SliderRow
    {
        public bool IsDragging => _dragging;
        public Grid Root { get; }
        public TextBlock Glyph { get; }

        /// <summary>Déclenché pendant le glissement (valeur 0-1).</summary>
        public event Action<double>? Changing;
        /// <summary>Déclenché au relâchement.</summary>
        public event Action<double>? Committed;
        public event Action? GlyphClicked;

        private readonly Border _track, _fill;
        private readonly TextBlock _label;
        private readonly Grid _hit;
        private double _value;
        private bool _dragging, _enabled = true;

        public SliderRow(double scale, string glyph, Brush accent, Brush fg, Brush track)
        {
            var font = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            var ui = new FontFamily("Segoe UI Variable, Segoe UI");

            Root = new Grid { Margin = new Thickness(0, 0, 0, 8 * scale) };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Glyph = new TextBlock
            {
                Text = glyph, Foreground = fg, FontFamily = font, FontSize = 18 * scale,
                Width = 30 * scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
            };
            Glyph.MouseLeftButtonUp += (_, _) => GlyphClicked?.Invoke();

            _fill = new Border
            {
                Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0,
                Height = 6 * scale, CornerRadius = new CornerRadius(3 * scale),
            };
            _track = new Border
            {
                Background = track, Child = _fill,
                Height = 6 * scale, CornerRadius = new CornerRadius(3 * scale),
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
            _track.SizeChanged += (_, _) => UpdateFill();

            _hit = new Grid
            {
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Height = 24 * scale,
                Margin = new Thickness(8 * scale, 0, 12 * scale, 0), Children = { _track },
            };
            _hit.MouseLeftButtonDown += OnDown;
            _hit.MouseMove += OnMove;
            _hit.MouseLeftButtonUp += OnUp;
            Grid.SetColumn(_hit, 1);

            _label = new TextBlock
            {
                Foreground = fg, FontFamily = ui, FontSize = 13 * scale, Width = 66 * scale, TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_label, 2);

            Root.Children.Add(Glyph);
            Root.Children.Add(_hit);
            Root.Children.Add(_label);
        }

        public void SetAccent(Brush accent) => _fill.Background = accent;

        /// <summary>Met à jour la position (ignoré pendant que l'utilisateur glisse).</summary>
        public void SetValue(double value01, string? label = null)
        {
            if (_dragging) return;
            _value = Math.Clamp(value01, 0, 1);
            _label.Text = label ?? $"{Math.Round(_value * 100)}%";
            UpdateFill();
        }

        public void SetEnabled(bool enabled, string? message = null)
        {
            _enabled = enabled;
            Root.Opacity = enabled ? 1 : 0.45;
            if (!enabled && message != null) _label.Text = message;
        }

        private void UpdateFill() => _fill.Width = _track.ActualWidth * _value;

        private double RatioAt(MouseEventArgs e) =>
            Math.Clamp(e.GetPosition(_track).X / Math.Max(1, _track.ActualWidth), 0, 1);

        private void Apply(MouseEventArgs e)
        {
            _value = RatioAt(e);
            _label.Text = $"{Math.Round(_value * 100)}%";
            UpdateFill();
            Changing?.Invoke(_value);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!_enabled) return;
            _dragging = true;
            _hit.CaptureMouse();
            Apply(e);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragging) Apply(e);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _hit.ReleaseMouseCapture();
            Committed?.Invoke(_value);
        }
    }
}
