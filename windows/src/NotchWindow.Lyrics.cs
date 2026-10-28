using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Navbar.Services;

namespace Navbar
{
    // Volet de paroles : la pastille s'élargit, la ligne en cours est mise en avant, un clic sur une ligne s'y rend
    public partial class NotchWindow
    {
        private const double LyricsExtra = 230;   // largeur ajoutée à la pastille quand les paroles sont ouvertes

        private bool _lyricsOpen;
        private readonly List<TextBlock> _lyricBlocks = new();
        private int _activeLyric = -1;

        private bool LyricsActive => _lyricsOpen && _view == HoverView.Auto && _mediaState.HasMedia;
        private double OpenWidthNow => OpenW + (LyricsActive ? LyricsExtra : 0);

        private void InitLyrics()
        {
            LyricsPane.Width = (LyricsExtra - 30) * _scale;
            LyricsPane.Margin = new Thickness(0, 2 * _scale, 0, 0);
            LyricsBtn.FontSize = 16 * _scale;
            LyricsBtn.Width = 30 * _scale;
            LyricsBtn.Padding = new Thickness(0, 7 * _scale, 0, 7 * _scale);
        }

        private void Lyrics_Click(object sender, MouseButtonEventArgs e)
        {
            _lyricsOpen = !_lyricsOpen;
            LyricsBtn.Foreground = _lyricsOpen ? EffectiveAccent() : FgAlpha(0x99);
            ApplyView();
            if (_hovered) UpdateState();   // la largeur de la pastille change
        }

        /// <summary>Reconstruit la liste des lignes quand les paroles d'une piste arrivent (ou disparaissent).</summary>
        private void BuildLyricsPane()
        {
            LyricsList.Children.Clear();
            _lyricBlocks.Clear();
            _activeLyric = -1;

            if (_lines.Count == 0)
            {
                LyricsList.Children.Add(new TextBlock
                {
                    Text = "Pas de paroles pour ce titre", Foreground = FgAlpha(0x66), FontSize = 12 * _scale,
                    FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8 * _scale, 0, 0),
                });
                return;
            }

            foreach (var line in _lines)
            {
                var block = new TextBlock
                {
                    Text = line.Text.Length > 0 ? line.Text : "♪", FontSize = 13 * _scale, FontFamily = UiFont,
                    Foreground = FgAlpha(0x77), TextWrapping = TextWrapping.Wrap, Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 0, 6 * _scale),
                };
                double time = line.Time;
                block.MouseLeftButtonUp += (_, _) => _media.Seek(time);   // « clique une ligne pour y aller »
                LyricsList.Children.Add(block);
                _lyricBlocks.Add(block);
            }
        }

        private void HighlightLyric(double position)
        {
            if (_lyricBlocks.Count == 0) return;

            int active = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Time <= position + 0.3) active = i; else break;
            }
            if (active == _activeLyric) return;
            _activeLyric = active;

            for (int i = 0; i < _lyricBlocks.Count; i++)
            {
                bool on = i == active;
                _lyricBlocks[i].Foreground = on ? EffectiveAccent() : FgAlpha(on ? (byte)0xFF : (byte)0x77);
                _lyricBlocks[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }

            if (active >= 0 && LyricsPane.Visibility == Visibility.Visible)
            {
                var block = _lyricBlocks[active];
                block.UpdateLayout();
                double y = block.TransformToAncestor(LyricsList).Transform(new Point(0, 0)).Y;
                LyricsPane.ScrollToVerticalOffset(Math.Max(0, y - LyricsPane.ViewportHeight / 2 + block.ActualHeight / 2));
            }
        }
    }
}
