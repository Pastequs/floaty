using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using Navbar.Services;

namespace Navbar
{
    // Contenu de la pastille fermée : l'utilisateur choisit ce qui s'y affiche
    public partial class NotchWindow
    {
        private (double W, double H, double R, bool Content) _restApplied;
        private bool _ready;

        /// <summary>Calcule taille et contenu de la pastille fermée selon les réglages et l'état courant.</summary>
        private (double W, double H, double R, bool Content) RestMetrics()
        {
            // Sur un bord latéral la pastille fermée devient une languette verticale discrète ; elle s'ouvre normalement au survol
            if (SideEdge)
            {
                if (_settings.MinimalNotch) return (8, 56, 4, false);
                if (!_settings.ShowMascot) return (14, 96, 7, false);
                foreach (UIElement child in PillContent.Children) child.Visibility = ReferenceEquals(child, MascotPill) ? Visibility.Visible : Visibility.Collapsed;
                return (40, 56, 16, true);   // la mascotte seule, dans une languette verticale
            }
            if (_settings.MinimalNotch) return (96, 9, 5, false);

            bool music = _settings.PillMusic && _mediaState.HasMedia && _mediaState.Playing;
            bool timer = _settings.PillTimer && _timer.Status != TimerStatus.Idle;
            int tasksLeft = _tasks.Count(t => !t.Done);
            bool tasks = _settings.PillTasks && tasksLeft > 0;

            // L'heure s'efface au profit de la mascotte quand le PC est inactif
            bool mascotOn = _settings.ShowMascot, clockOn = _settings.PillClock;
            if (mascotOn && clockOn && _settings.PillSwap)
            {
                bool resting = _idleSeconds > 45;
                mascotOn = resting;
                clockOn = !resting;
            }
            bool battery = _settings.PillBattery && FillBattery(PillBattery);

            MascotPill.Visibility = Show(mascotOn);
            PillClock.Visibility = Show(clockOn);
            PillBattery.Visibility = Show(battery);
            bool privacy = UpdatePrivacyDots();
            bool weather = _settings.PillWeather && Shared.Weather != null;
            PillWeatherBox.Visibility = Show(weather);
            if (weather)
            {
                var now = Shared.Weather!.Now;
                var (_, kind) = WeatherService.Describe(now.Code, now.IsDay);
                if (!ReferenceEquals(PillWeatherIcon.Tag, kind))
                {
                    PillWeatherIcon.Child = new WeatherGlyph(kind, 18 * _scale);
                    PillWeatherIcon.Tag = kind;
                }
                PillWeather.Text = Degrees(now.Temp);
            }

            bool meeting = Shared.InMeeting;
            PillMeeting.Visibility = Show(meeting);
            if (meeting)
            {
                var span = DateTime.Now - Shared.MeetingSince!.Value;
                PillMeeting.Text = "● " + (span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss"));
            }

            bool noteOn = FillNotePill();
            PillNote.Visibility = Show(noteOn);

            bool calendar = FillCalendarPill();
            PillCalendar.Visibility = Show(calendar);

            bool market = FillMarketPill();
            PillMarket.Visibility = Show(market);

            bool sys = _settings.PillSystem && Shared.LastSystem != null;
            PillSys.Visibility = Show(sys);
            if (sys) PillSys.Text = $"CPU {Math.Round(Shared.LastSystem!.Cpu)} %  ·  RAM {Math.Round(Shared.LastSystem.RamPercent)} %";
            PillMusic.Visibility = Show(music);
            SetPillEq(music);
            PillTimer.Visibility = Show(timer);
            PillWater.Visibility = Show(_settings.PillWater);
            PillTasks.Visibility = Show(tasks);

            PillMusicClip.Visibility = Show(_settings.PillMusicTitle);
            if (music && _settings.PillMusicTitle) SetPillTitle(_mediaState.Title);
            if (_settings.PillWater)
                PillWater.Text = $"\U0001F4A7 {Math.Round(TodayMl * 100.0 / Math.Max(1, _settings.WaterGoalMl))} %";
            if (tasks) PillTasks.Text = $"✓ {tasksLeft}";

            bool any = mascotOn || clockOn || music || timer || _settings.PillWater || tasks || battery || sys || privacy || weather || market || meeting || calendar || noteOn;
            if (!any) return (130, 14, 8, false);

            PillContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = PillContent.DesiredSize.Width / (_scale * _wf) + 26;
            // La pastille fermée prend la hauteur de la barre des tâches quand celle-ci est plus haute (icônes grandes) : elles restent alignées
            double h = Math.Max(30, Math.Round(DockBarHeightUnits()));
            return (Math.Max(w, 118), h, h / 2, true);
        }

        private string _pillTitle = "";
        private bool _pillTitleScrolling;

        /// <summary>Titre du média : s'il dépasse la largeur disponible, il défile en continu vers la gauche et revient par la droite (si l'option est active), sinon il est tronqué.</summary>
        private void SetPillTitle(string title)
        {
            double max = 150 * _scale;
            bool marquee = _settings.PillMarquee;
            if (title == _pillTitle && marquee == _pillTitleScrolling) return;
            _pillTitle = title;

            PillMusicText.Text = PillMusicText2.Text = title;
            PillMusicText.Width = double.NaN;               // mesure la largeur naturelle, sans l'effet d'un réglage précédent
            PillMusicText.TextTrimming = TextTrimming.None;
            PillMusicText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double natural = PillMusicText.DesiredSize.Width;
            PillMusicShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            PillMusicShift.X = 0;
            PillMusicText2.Visibility = Visibility.Collapsed;
            // Le Canvas laisse le texte à sa taille naturelle (une cellule plus étroite le rognerait, même décalé)
            PillMusicCanvas.Height = PillMusicText.DesiredSize.Height;
            PillMusicCanvas.Width = Math.Min(natural, max);

            if (natural <= max)
            {
                PillMusicClip.Width = double.NaN;
                _pillTitleScrolling = marquee;
                return;
            }

            PillMusicClip.Width = max;
            if (!marquee)
            {
                PillMusicText.TextTrimming = TextTrimming.CharacterEllipsis;
                PillMusicText.Width = max;
                _pillTitleScrolling = false;
                return;
            }
            _pillTitleScrolling = true;

            // Deux copies du titre séparées par un espace : en glissant d'une copie vers la gauche, la suivante arrive par la droite, sans coupure
            double gap = 36 * _scale, loop = natural + gap, speed = 28 * _scale;   // pixels par seconde
            System.Windows.Controls.Canvas.SetLeft(PillMusicText2, loop);
            PillMusicText2.Visibility = Visibility.Visible;

            var a = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
            a.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.Zero));
            a.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(1.5)));   // courte pause pour lire le début
            a.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(-loop, TimeSpan.FromSeconds(1.5 + loop / speed)));
            System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(a, 30);
            PillMusicShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, a);
        }

        private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        private void SetPillContentVisible(bool visible) =>
            PillContent.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(180)));

        /// <summary>Met à jour la pastille fermée quand l'heure, la musique, le minuteur… changent.</summary>
        private void RefreshPill()
        {
            if (!_ready || _hovered || _typing || _retractTimer.IsEnabled || _overlayActive) return;

            var m = RestMetrics();
            bool resized = Math.Abs(m.W - _restApplied.W) > 1 || m.H != _restApplied.H;
            if (resized) Animate(m.W, m.H, m.R, false);
            if (resized || m.Content != _restApplied.Content) SetPillContentVisible(m.Content);
            _restApplied = m;
        }
    }
}








