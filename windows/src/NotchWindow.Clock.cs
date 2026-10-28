using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Navbar
{
    // Styles d'affichage de l'heure : numérique, monospace, analogique, anneau des secondes, en toutes lettres
    public partial class NotchWindow
    {
        private ClockFace? _clockFace;

        private static readonly (string Label, string Value)[] ClockStyles =
            { ("Numérique", "digital"), ("Monospace", "mono"), ("Analogique", "analog"), ("Anneau", "ring"), ("En lettres", "words") };

        private void InitClockStyle()
        {
            _clockFace = new ClockFace(66 * _scale);
            ClockFaceHost.Child = _clockFace;
            ClockWords.FontSize = 26 * _scale;
            ClockWords.MaxWidth = 230 * _scale;
            ApplyClockStyle();
        }

        private void ApplyClockStyle()
        {
            string style = _settings.ClockStyle;
            bool face = style is "analog" or "ring";

            ClockDigital.Visibility = style is "digital" or "mono" ? Visibility.Visible : Visibility.Collapsed;
            ClockFaceHost.Visibility = face ? Visibility.Visible : Visibility.Collapsed;
            ClockWords.Visibility = style == "words" ? Visibility.Visible : Visibility.Collapsed;
            if (_clockFace != null) _clockFace.Mode = style;

            // Police à chasse fixe pour le style « Monospace » (heure du survol et en-tête)
            var font = style == "mono" ? new FontFamily("Cascadia Mono, Consolas") : new FontFamily("Segoe UI Variable Display, Segoe UI");
            ClockText.FontFamily = font;
            HeaderTime.FontFamily = style == "mono" ? font : new FontFamily("Segoe UI Variable, Segoe UI");
            PillClock.FontFamily = style == "mono" ? font : new FontFamily("Segoe UI Variable, Segoe UI");

            UpdateClock();
        }

        private ClockFace? _previewFace;
        private TextBlock? _previewText;

        /// <summary>Aperçu vivant du style choisi (l'horloge du survol n'est visible que sans musique en cours).</summary>
        private UIElement BuildClockPreview()
        {
            _previewFace = null;
            _previewText = null;
            string style = _settings.ClockStyle;

            if (style is "analog" or "ring")
            {
                _previewFace = new ClockFace(58 * _scale) { Mode = style, HorizontalAlignment = HorizontalAlignment.Left };
                var host = new Border { Child = _previewFace, HorizontalAlignment = HorizontalAlignment.Left };
                _previewFace.Update(DateTime.Now, Fg(), FgAlpha(0x55), EffectiveAccent(), FormatTime(DateTime.Now));
                return host;
            }

            _previewText = new TextBlock
            {
                Foreground = Fg(), FontWeight = FontWeights.SemiBold, FontSize = 24 * _scale, TextWrapping = TextWrapping.Wrap,
                FontFamily = style == "mono" ? new FontFamily("Cascadia Mono, Consolas") : new FontFamily("Segoe UI Variable Display, Segoe UI"),
                Text = style == "words" ? TimeInWords(DateTime.Now) : FormatTime(DateTime.Now),
            };
            return _previewText;
        }

        /// <summary>Met à jour la partie visuelle de l'horloge (visages dessinés, heure en lettres, en-tête).</summary>
        private void UpdateClockVisual(DateTime now, string time)
        {
            string style = _settings.ClockStyle;

            if (style is "analog" or "ring")
                _clockFace?.Update(now, Fg(), FgAlpha(0x55), EffectiveAccent(), time);
            else if (style == "words")
                ClockWords.Text = TimeInWords(now);

            if (_view == HoverView.Settings && _settingsPage == 2)
            {
                _previewFace?.Update(now, Fg(), FgAlpha(0x55), EffectiveAccent(), time);
                if (_previewText != null) _previewText.Text = style == "words" ? TimeInWords(now) : time;
            }

            // En-tête : l'heure en lettres remplace l'heure numérique (et la date, pour tenir dans la place)
            if (style == "words")
            {
                HeaderTime.Text = TimeInWords(now);
                HeaderDate.Visibility = Visibility.Collapsed;
            }
        }

        // ───────────── Heure en toutes lettres (arrondie à 5 minutes) ─────────────

        private static readonly string[] HourNames =
            { "Minuit", "Une heure", "Deux heures", "Trois heures", "Quatre heures", "Cinq heures", "Six heures",
              "Sept heures", "Huit heures", "Neuf heures", "Dix heures", "Onze heures", "Midi" };

        private static string HourLabel(int hour24)
        {
            hour24 %= 24;
            if (hour24 == 0) return "Minuit";
            if (hour24 == 12) return "Midi";
            return HourNames[hour24 % 12];
        }

        private static readonly string[] EnglishHours =
            { "midnight", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "noon" };

        private static string EnglishHour(int hour24)
        {
            hour24 %= 24;
            return hour24 == 0 ? "midnight" : hour24 == 12 ? "noon" : EnglishHours[hour24 % 12];
        }

        /// <summary>L'heure en anglais : « Ten o'clock », « Half past ten », « Quarter to eleven »…</summary>
        private static string TimeInWordsEnglish(int hour, int minutes)
        {
            string current = EnglishHour(hour), next = EnglishHour(hour + 1);
            string text = minutes switch
            {
                0 => current is "noon" or "midnight" ? current : current + " o'clock",
                15 => $"quarter past {current}",
                30 => $"half past {current}",
                45 => $"quarter to {next}",
                < 30 => $"{EnglishMinutes(minutes)} past {current}",
                _ => $"{EnglishMinutes(60 - minutes)} to {next}",
            };
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static string EnglishMinutes(int m) => m switch { 5 => "five", 10 => "ten", 20 => "twenty", 25 => "twenty-five", _ => m.ToString() };

        private static string TimeInWords(DateTime d)
        {
            int minutes = (int)Math.Round(d.Minute / 5.0) * 5;
            int hour = d.Hour;
            if (minutes == 60) { minutes = 0; hour++; }

            if (Loc.English) return TimeInWordsEnglish(hour, minutes);

            bool noon = hour % 24 is 0 or 12;
            string current = HourLabel(hour);
            string next = HourLabel(hour + 1);

            string text = minutes switch
            {
                0 => current,
                5 => $"{current} cinq",
                10 => $"{current} dix",
                15 => $"{current} et quart",
                20 => $"{current} vingt",
                25 => $"{current} vingt-cinq",
                30 => noon ? $"{current} et demi" : $"{current} et demie",
                35 => $"{next} moins vingt-cinq",
                40 => $"{next} moins vingt",
                45 => $"{next} moins le quart",
                50 => $"{next} moins dix",
                _ => $"{next} moins cinq",
            };
            return text;
        }
    }
}
