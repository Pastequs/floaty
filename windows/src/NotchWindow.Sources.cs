using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using MediaState = Navbar.Services.MediaState;

namespace Navbar
{
    // Lecteur multi-sessions : une pastille par source (Spotify, Chrome, VLC…), un clic pour basculer
    public partial class NotchWindow
    {
        private string _sourceSignature = "";

        private void RenderSourceChips(MediaState s)
        {
            var sources = _media.Sources;
            bool several = sources.Count > 1;
            SourceChips.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
            SourceText.Visibility = several ? Visibility.Collapsed : Visibility.Visible;
            if (!several) { _sourceSignature = ""; return; }

            string signature = string.Join("|", sources.Select(x => x.Id + (x.Active ? "*" : "") + (x.Playing ? "+" : "")));
            if (signature == _sourceSignature) return;   // inutile de tout reconstruire à chaque relevé
            _sourceSignature = signature;

            SourceChips.Children.Clear();
            var accent = EffectiveAccentColor();
            foreach (var src in sources)
            {
                var captured = src;
                var label = new TextBlock
                {
                    Text = (src.Playing ? "♪ " : "") + src.Label, FontSize = 10 * _scale, FontFamily = UiFont,
                    Foreground = src.Active ? new SolidColorBrush(IsLight(accent) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White) : Fg(),
                };
                var chip = new Border
                {
                    Child = label, CornerRadius = new CornerRadius(8 * _scale), Cursor = Cursors.Hand, ToolTip = "Écouter cette source",
                    Padding = new Thickness(7 * _scale, 1 * _scale, 7 * _scale, 1 * _scale), Margin = new Thickness(0, 0, 4 * _scale, 2 * _scale),
                    Background = src.Active ? AccentFill() : FgAlpha(0x22),
                };
                Press.Attach(chip, 0.94);
                chip.MouseLeftButtonUp += (_, _) => _media.SelectSource(captured.Id);
                SourceChips.Children.Add(chip);
            }
        }
    }
}

