using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Navbar
{
    // Agenda : prochains événements des calendriers choisis (iCloud / Apple via Home Assistant)
    public partial class NotchWindow
    {
        private void InitCalendar()
        {
            CalToggle.Width = CalToggle.Height = 24 * _scale;
            CalToggle.Foreground = FgAlpha(0x99);
            CalTitle.FontSize = 14 * _scale;
            CalNote.FontSize = 11 * _scale;
            CalNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            CalScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);
            CalModeBtn.CornerRadius = new CornerRadius(12 * _scale);
            CalModeBtn.Padding = new Thickness(10 * _scale, 3 * _scale, 10 * _scale, 3 * _scale);
            CalModeText.FontSize = 12 * _scale;
            Press.Attach(CalModeBtn, 0.94);
            PillCalendar.FontSize = 13 * _scale;
            PillCalendar.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);

            Shared.CalendarUpdated += OnCalendarUpdated;
            Closed += (_, _) => Shared.CalendarUpdated -= OnCalendarUpdated;

            // Le compte à rebours de la pastille avance à la minute
            var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            tick.Tick += (_, _) => { if (_settings.PillCalendar) RefreshPill(); };
            tick.Start();
            Closed += (_, _) => tick.Stop();
            RenderCalendar();
        }

        private void CalToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Calendar);

        private void CalMode_Click(object sender, MouseButtonEventArgs e)
        {
            _settings.CalendarWeekView = !_settings.CalendarWeekView;
            Save();
            RenderCalendar();
        }

        private void OnCalendarUpdated() => Dispatcher.BeginInvoke(() => { RenderCalendar(); RefreshPill(); });

        private static string DayLabel(DateTime day)
        {
            if (day.Date == DateTime.Today) return "Aujourd'hui";
            if (day.Date == DateTime.Today.AddDays(1)) return "Demain";
            string text = day.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private void RenderCalendar()
        {
            CalList.Children.Clear();
            CalNote.Text = "";

            if (!_settings.ShowCalendar) { CalList.Children.Add(Hint("Active l'agenda dans Réglages › Général.")); return; }
            if (_settings.HaUrl.Length == 0 || !HomeAssistant.HasToken) { CalList.Children.Add(Hint("Configure d'abord Home Assistant dans Réglages › Maison (adresse et jeton).")); return; }
            if (_settings.CalendarEntities.Count == 0) { CalList.Children.Add(Hint("Choisis tes calendriers dans Réglages › Agenda.")); return; }
            if (Shared.CalError.Length > 0 && Shared.CalEvents.Count == 0) { CalList.Children.Add(new TextBlock { Text = Shared.CalError, Foreground = Down, FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap }); return; }
            if (Shared.CalLoadedAt == DateTime.MinValue) { CalList.Children.Add(Hint("Chargement de l'agenda…")); return; }

            var upcoming = Shared.CalEvents.Where(e => e.End > DateTime.Now || e.AllDay && e.End.Date > DateTime.Today).ToList();
            CalModeText.Text = _settings.CalendarWeekView ? "Liste" : "Semaine";
            if (upcoming.Count == 0) { CalList.Children.Add(Hint("Rien de prévu dans les 7 prochains jours.")); return; }
            CalNote.Text = $"{upcoming.Count} à venir";
            if (_settings.CalendarWeekView) { RenderCalendarWeek(upcoming); return; }

            DateTime? currentDay = null;
            foreach (var ev in upcoming)
            {
                var day = ev.Start.Date < DateTime.Today ? DateTime.Today : ev.Start.Date;
                if (currentDay != day)
                {
                    currentDay = day;
                    CalList.Children.Add(new TextBlock
                    {
                        Text = DayLabel(day), Foreground = day == DateTime.Today ? EffectiveAccent() : FgAlpha(0x88), FontSize = 11 * _scale, FontWeight = FontWeights.SemiBold,
                        FontFamily = UiFont, Margin = new Thickness(2 * _scale, currentDay == upcoming[0].Start.Date ? 0 : 6 * _scale, 0, 3 * _scale),
                    });
                }

                bool now = !ev.AllDay && ev.Start <= DateTime.Now && ev.End > DateTime.Now;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58 * _scale) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (ev.Link.Length > 0)
                {
                    string link = ev.Link;
                    var join = new Border
                    {
                        Child = new TextBlock { Text = "Rejoindre", FontSize = 11 * _scale, FontFamily = UiFont, Foreground = IsLight(EffectiveAccentColor()) ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : Brushes.White },
                        Background = AccentFill(), CornerRadius = new CornerRadius(10 * _scale), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(8 * _scale, 2 * _scale, 8 * _scale, 2 * _scale), Margin = new Thickness(6 * _scale, 0, 0, 0), ToolTip = link,
                    };
                    Press.Attach(join, 0.93);
                    join.MouseLeftButtonUp += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true }); } catch { } };
                    Grid.SetColumn(join, 2);
                    row.Children.Add(join);
                }
                row.Children.Add(new TextBlock
                {
                    Text = ev.AllDay ? "Journée" : ev.Start.ToString("HH:mm"), Foreground = now ? EffectiveAccent() : FgAlpha(0x99),
                    FontSize = 12 * _scale, FontFamily = UiFont, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = ev.Title, Foreground = Fg(), FontSize = 13 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
                string sub = (ev.AllDay ? "" : $"jusqu'à {ev.End:HH:mm} · ") + ev.Calendar + (ev.Location.Length > 0 ? " · " + ev.Location : "");
                text.Children.Add(new TextBlock { Text = sub, Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);

                CalList.Children.Add(new Border
                {
                    Child = row, Background = now ? FgAlpha(0x22) : FgAlpha(0x14), CornerRadius = new CornerRadius(10 * _scale),
                    Padding = new Thickness(10 * _scale, 4 * _scale, 10 * _scale, 4 * _scale), Margin = new Thickness(0, 0, 0, 3 * _scale),
                });
            }
        }

        /// <summary>Vue semaine : une colonne par jour avec ses premiers événements.</summary>
        private void RenderCalendarWeek(System.Collections.Generic.List<CalEvent> events)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            for (int d = 0; d < 7; d++)
            {
                var day = DateTime.Today.AddDays(d);
                var column = new StackPanel { Margin = new Thickness(0, 0, 4 * _scale, 0) };
                var head = new Border
                {
                    Background = d == 0 ? AccentFill() : FgAlpha(0x1F), CornerRadius = new CornerRadius(8 * _scale), Padding = new Thickness(0, 2 * _scale, 0, 2 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                    Child = new TextBlock
                    {
                        Text = day.ToString("ddd d", CultureInfo.CurrentCulture).TrimEnd('.'), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 * _scale, FontFamily = UiFont, FontWeight = FontWeights.SemiBold,
                        Foreground = d == 0 ? new SolidColorBrush(IsLight(EffectiveAccentColor()) ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White) : Fg(),
                    },
                };
                column.Children.Add(head);

                var ofDay = events.Where(e => e.Start.Date <= day && (e.AllDay ? e.End.Date > day : e.Start.Date == day)).ToList();
                foreach (var ev in ofDay.Take(4))
                {
                    var cell = new Border { Background = FgAlpha(0x14), CornerRadius = new CornerRadius(6 * _scale), Padding = new Thickness(4 * _scale, 2 * _scale, 4 * _scale, 2 * _scale), Margin = new Thickness(0, 0, 0, 2 * _scale), ToolTip = $"{(ev.AllDay ? "Journée" : ev.Start.ToString("HH:mm"))} · {ev.Title}" };
                    var cellText = new StackPanel();
                    cellText.Children.Add(new TextBlock { Text = ev.AllDay ? "journée" : ev.Start.ToString("HH:mm"), FontSize = 10 * _scale, FontFamily = UiFont, Foreground = FgAlpha(0x88) });
                    cellText.Children.Add(new TextBlock { Text = ev.Title, FontSize = 10 * _scale, FontFamily = UiFont, Foreground = Fg(), TextTrimming = TextTrimming.CharacterEllipsis });
                    cell.Child = cellText;
                    column.Children.Add(cell);
                }
                if (ofDay.Count > 4) column.Children.Add(new TextBlock { Text = $"+{ofDay.Count - 4}", FontSize = 10 * _scale, FontFamily = UiFont, Foreground = FgAlpha(0x77), HorizontalAlignment = HorizontalAlignment.Center });
                grid.Children.Add(column);
            }
            CalList.Children.Add(grid);
        }

        /// <summary>« 14:30 Réunion · dans 25 min » pour le prochain rendez-vous des 12 prochaines heures.</summary>
        private bool FillCalendarPill()
        {
            if (!_settings.PillCalendar || Shared.NextEvent is not { } ev || ev.Start > DateTime.Now.AddHours(12)) return false;

            string title = ev.Title.Length > 20 ? ev.Title.Substring(0, 19) + "…" : ev.Title;
            var delta = ev.Start - DateTime.Now;
            string when = delta <= TimeSpan.Zero ? "en cours" : delta.TotalMinutes < 60 ? $"dans {Math.Ceiling(delta.TotalMinutes)} min" : ev.Start.ToString("HH:mm");
            PillCalendar.Text = $"{title} · {when}";
            PillCalendar.Foreground = delta > TimeSpan.Zero && delta.TotalMinutes <= 10 ? Down : Fg();   // rouge quand c'est imminent
            return true;
        }

        // ───────────── Réglages ─────────────

        private System.Collections.Generic.List<CalendarInfo>? _calChoices;
        private string _calSettingsNote = "";

        private void RenderCalendarSettings()
        {
            if (_settings.HaUrl.Length == 0 || !HomeAssistant.HasToken)
                SettingsList.Children.Add(Row("Prérequis", Hint("L'agenda passe par Home Assistant : renseigne d'abord l'adresse et le jeton dans Réglages › Maison.")));

            var actions = new WrapPanel();
            actions.Children.Add(Chip("Charger mes calendriers", false, async () =>
            {
                _calSettingsNote = "Chargement…";
                try { _calChoices = await HomeAssistant.CalendarsAsync(_settings.HaUrl); _calSettingsNote = $"{_calChoices.Count} calendrier(s) trouvé(s)"; }
                catch (Exception ex) { _calSettingsNote = ex.Message; }
                RenderSettings();
            }));
            actions.Children.Add(Chip("Actualiser", false, () => _ = Shared.RefreshCalendarAsync(force: true)));
            SettingsList.Children.Add(Row("Connexion", actions));

            SettingsList.Children.Add(Row("Rappel", ChipsFor(new[] { ("Jamais", 0), ("5 min", 5), ("10 min", 10), ("15 min", 15), ("30 min", 30) }, _settings.CalendarReminderMin, v =>
            {
                _settings.CalendarReminderMin = v;
                Save();
                RenderSettings();
            })));
            if (_calSettingsNote.Length > 0) SettingsList.Children.Add(Row("", new TextBlock { Text = _calSettingsNote, Foreground = FgAlpha(0x99), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap }));

            if (_calChoices != null)
            {
                var chips = new WrapPanel();
                foreach (var c in _calChoices)
                {
                    var captured = c;
                    bool on = _settings.CalendarEntities.Contains(c.Id);
                    chips.Children.Add(Chip(c.Name.Length > 0 ? c.Name : Shared.CalendarName(c.Id), on, () =>
                    {
                        if (on) _settings.CalendarEntities.Remove(captured.Id); else _settings.CalendarEntities.Add(captured.Id);
                        Save();
                        _ = Shared.RefreshCalendarAsync(force: true);
                        RenderSettings();
                    }));
                }
                SettingsList.Children.Add(Row("Afficher", chips));
            }
            else if (_settings.CalendarEntities.Count > 0)
                SettingsList.Children.Add(Row("Affichés", Hint(string.Join(" · ", _settings.CalendarEntities.Select(Shared.CalendarName)))));

            SettingsList.Children.Add(Row("Guide Apple", GuideBlock(new[]
            {
                "Prérequis : Home Assistant configuré dans Réglages › Maison (adresse et jeton), et l'identification en deux facteurs activée sur ton compte Apple.",
                "1. Crée un mot de passe d'application : appleid.apple.com › Connexion et sécurité › Mots de passe d'application › « + » › nomme-le « Home Assistant » › copie le code (xxxx-xxxx-xxxx-xxxx). Ton vrai mot de passe Apple ne fonctionne pas ici.",
                "2. Dans Home Assistant : Paramètres › Appareils et services › « Ajouter une intégration » › cherche « CalDAV ».",
                "3. Renseigne : URL  https://caldav.icloud.com  · nom d'utilisateur = ton identifiant Apple (e-mail) · mot de passe = le code de l'étape 1. Valide : chaque calendrier iCloud devient une entité « calendar.… ».",
                "4. Ici : « Charger mes calendriers » puis coche ceux à afficher.",
                "5. Active l'onglet : Réglages › Général › Modules › « Agenda ». Option « Prochain rendez-vous » dans Réglages › Pastille pour l'avoir dans la pastille fermée.",
                "Dépannage : calendrier absent de la liste = vérifie dans Home Assistant (Outils de développement › États, filtre « calendar. »). Un événement qui manque : seuls les 7 prochains jours sont lus ; « Actualiser » force la mise à jour (sinon toutes les 5 minutes).",
            }, ("Ouvrir appleid.apple.com", "https://appleid.apple.com/account/manage"), ("Ouvrir Home Assistant", _settings.HaUrl))));
        }
    }
}







