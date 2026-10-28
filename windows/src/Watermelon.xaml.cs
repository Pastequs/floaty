using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Navbar
{
    /// <summary>
    /// La mascotte pastèque : respire, cligne, regarde autour, saute, dort et salue au retour.
    /// Ses petites activités changent avec l'heure de la journée (bâillements le matin, repas à midi, lune la nuit…).
    /// </summary>
    public partial class Watermelon : UserControl
    {
        private enum Period { Morning, Midday, Afternoon, Evening, Night }

        private static readonly Random Rng = new();

        private bool _asleep;
        private Period _period = Period.Afternoon;
        private double _eyeOpen = 1;
        private bool _busy;   // une activité à la fois
        private readonly DispatcherTimer _life = new();

        public Watermelon()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                StartBlink();
                StartBreathing();
                ScheduleNext();
            };
            Unloaded += (_, _) => _life.Stop();

            // Invisible (module masqué, pastille sans mascotte) : plus aucune animation, donc aucun travail de rendu
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue)
                {
                    if (!_asleep) StartBlink();
                    StartBreathing();
                    ScheduleNext();
                }
                else
                {
                    _life.Stop();
                    HopScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    HopScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                }
            };
            _life.Tick += (_, _) => { _life.Stop(); DoSomething(); ScheduleNext(); };
        }

        public bool IsAsleep => _asleep;

        // ───────────── Heure de la journée ─────────────

        /// <summary>Règle accessoires, regard et activités selon l'heure (0-23).</summary>
        public void SetTimeOfDay(int hour)
        {
            var period = hour switch
            {
                >= 5 and < 11 => Period.Morning,
                >= 11 and < 14 => Period.Midday,
                >= 14 and < 18 => Period.Afternoon,
                >= 18 and < 22 => Period.Evening,
                _ => Period.Night,
            };
            if (period == _period && SunBox.Tag != null) return;
            _period = period;
            SunBox.Tag = "set";

            // Plus de soleil ni de lune au-dessus de la mascotte : seuls les yeux et les activités changent avec l'heure

            // La nuit, les yeux restent un peu mi-clos
            _eyeOpen = period == Period.Night ? 0.62 : period == Period.Evening ? 0.85 : 1;
            if (!_asleep)
            {
                EyeLScale.ScaleY = EyeRScale.ScaleY = _eyeOpen;
                StartBlink();
            }

            if (period == Period.Night) Twinkle();
        }

        private void Twinkle()
        {
            foreach (var star in new[] { Star1, Star2 })
            {
                var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.8 + Rng.NextDouble()), RepeatBehavior = RepeatBehavior.Forever };
                a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.Zero));
                a.KeyFrames.Add(new LinearDoubleKeyFrame(0.25, TimeSpan.FromSeconds(0.9)));
                a.KeyFrames.Add(new LinearDoubleKeyFrame(1, a.Duration.TimeSpan));
                star.BeginAnimation(OpacityProperty, a);
            }
        }

        // ───────────── Sommeil et salut ─────────────

        public void SetAsleep(bool asleep)
        {
            if (asleep == _asleep) return;
            _asleep = asleep;
            SleepZ.Visibility = asleep ? Visibility.Visible : Visibility.Collapsed;

            if (asleep)
            {
                EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                EyeLScale.ScaleY = EyeRScale.ScaleY = 0.12;
                EyeLShift.X = EyeRShift.X = 0;
                StartBreathing();
                StartSleepZ();
            }
            else
            {
                EyeLScale.ScaleY = EyeRScale.ScaleY = _eyeOpen;
                SleepZ.BeginAnimation(OpacityProperty, null);
                SleepZ.BeginAnimation(Canvas.TopProperty, null);
                StartBlink();
                StartBreathing();
                Wave();
            }
        }

        // ───────────── Petites actions, selon l'heure ─────────────

        // ───────────── Humeur : réactions aux événements ─────────────

        public enum Mood { Normal, Dance, Worried }
        private Mood _mood = Mood.Normal;

        /// <summary>Danse sur la musique, ou s'inquiète quand le processeur s'emballe ; revient à la normale ensuite.</summary>
        public void SetMood(Mood mood)
        {
            if (mood == _mood) return;
            _mood = mood;
            // Inquiète : une goutte de sueur reste sur le front tant que le processeur est saturé
            Sweat.BeginAnimation(OpacityProperty, null);
            if (mood == Mood.Worried)
            {
                var drip = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                drip.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
                drip.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(350)));
                drip.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1500)));
                drip.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1900)));
                Sweat.BeginAnimation(OpacityProperty, drip);
            }
            else Sweat.Opacity = 0;
            if (!_asleep && _life.IsEnabled) ScheduleNext();   // rythme plus vif tout de suite
        }

        /// <summary>S'endort quelques instants (fin d'une session de concentration), puis se réveille toute seule.</summary>
        public void Rest(double seconds)
        {
            Yawn();
            var wake = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            wake.Tick += (_, _) =>
            {
                wake.Stop();
                SetAsleep(true);
                var up = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                up.Tick += (_, _) => { up.Stop(); SetAsleep(false); };
                up.Start();
            };
            wake.Start();
        }
        private void ScheduleNext()
        {
            _life.Interval = TimeSpan.FromSeconds(_asleep ? 9 : _mood == Mood.Dance ? 1.2 + Rng.NextDouble() * 1.2 : _mood == Mood.Worried ? 1.5 + Rng.NextDouble() : 3.5 + Rng.NextDouble() * 5);
            _life.Start();
        }

        private void DoSomething()
        {
            if (_asleep || _busy) return;

            int roll = Rng.Next(6);
            if (_mood == Mood.Dance) { if (roll < 3) Sway(); else if (roll < 5) Wiggle(); else Hop(); return; }
            if (_mood == Mood.Worried) { if (roll < 3) Look(-2.6, 2.6); else Wiggle(); return; }
            switch (_period)
            {
                case Period.Morning:   // se réveille : bâille, s'étire
                    if (roll < 2) Yawn(); else if (roll == 2) Stretch(); else if (roll == 3) Look(-2.6); else if (roll == 4) Look(2.6); else Hop();
                    break;
                case Period.Midday:    // heure du repas : mâchonne, saute de joie
                    if (roll < 2) Munch(); else if (roll == 2) Hop(); else if (roll == 3) Wiggle(); else Look(2.6, -2.6);
                    break;
                case Period.Afternoon: // en forme : regarde partout, gigote
                    if (roll == 0) Look(-2.6); else if (roll == 1) Look(2.6); else if (roll == 2) Look(-2.6, 2.6);
                    else if (roll == 3) Wiggle(); else if (roll == 4) Hop(); else Look(0, 0, up: true);
                    break;
                case Period.Evening:   // décontractée : se balance doucement, bâille parfois
                    if (roll < 2) Sway(); else if (roll == 2) Yawn(); else if (roll == 3) Look(-2.6, 2.6); else Look(2.6);
                    break;
                default:               // nuit : somnolente, dodeline et bâille
                    if (roll < 3) Yawn(); else if (roll == 3) Nod(); else Look(2.2);
                    break;
            }
        }

        private void Busy(double seconds)
        {
            _busy = true;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            t.Tick += (_, _) => { t.Stop(); _busy = false; };
            t.Start();
        }

        /// <summary>Les yeux glissent vers un côté (puis éventuellement vers l'autre) et reviennent.</summary>
        private void Look(double first, double? second = null, bool up = false)
        {
            var x = new DoubleAnimationUsingKeyFrames();
            x.KeyFrames.Add(new EasingDoubleKeyFrame(first, TimeSpan.FromMilliseconds(180)));
            x.KeyFrames.Add(new EasingDoubleKeyFrame(first, TimeSpan.FromMilliseconds(750)));
            double t = 750;
            if (second != null)
            {
                x.KeyFrames.Add(new EasingDoubleKeyFrame(second.Value, TimeSpan.FromMilliseconds(t + 200)));
                x.KeyFrames.Add(new EasingDoubleKeyFrame(second.Value, TimeSpan.FromMilliseconds(t + 750)));
                t += 750;
            }
            x.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(t + 220)));
            EyeLShift.BeginAnimation(TranslateTransform.XProperty, x);
            EyeRShift.BeginAnimation(TranslateTransform.XProperty, x);

            if (up)
            {
                var y = new DoubleAnimationUsingKeyFrames();
                y.KeyFrames.Add(new EasingDoubleKeyFrame(-2.2, TimeSpan.FromMilliseconds(180)));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(-2.2, TimeSpan.FromMilliseconds(800)));
                y.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1000)));
                EyeLShift.BeginAnimation(TranslateTransform.YProperty, y);
                EyeRShift.BeginAnimation(TranslateTransform.YProperty, y);
            }
        }

        /// <summary>Réactions ponctuelles aux événements de l'appli (ignorées si elle dort).</summary>
        public void React(Reaction reaction)
        {
            if (_asleep) return;
            switch (reaction)
            {
                case Reaction.Cheer:   // minuteur terminé : saute de joie, yeux étoilés et joues rosies, puis se trémousse
                    Hop();
                    Show(SparkL, 1.6); Show(SparkR, 1.6); Show(BlushL, 1.6); Show(BlushR, 1.6);
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
                    t.Tick += (_, _) => { t.Stop(); Wiggle(); };
                    t.Start();
                    break;
                case Reaction.Startle: // notification : sursaute, grands yeux et bouche en « o »
                    Hop();
                    Surprised(1.0);
                    break;
                case Reaction.Sip:     // verre d'eau : hoche la tête, joues rosies, content
                    Nod();
                    Show(BlushL, 1.8); Show(BlushR, 1.8);
                    break;
            }
            Busy(1.2);
        }

        /// <summary>Fait apparaître un élément un instant, puis le fond doucement.</summary>
        private static void Show(UIElement element, double seconds)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(160)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(seconds)));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(seconds + 0.35)));
            element.BeginAnimation(OpacityProperty, a);
        }

        /// <summary>Yeux écarquillés et petite bouche ronde, puis retour au visage habituel.</summary>
        private void Surprised(double seconds)
        {
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            EyeLScale.ScaleY = EyeRScale.ScaleY = 1.35;
            Smile.Opacity = 0;
            OpenMouth.Opacity = 1;
            MouthScale.ScaleY = 0.7;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                Smile.Opacity = 1;
                OpenMouth.Opacity = 0;
                MouthScale.ScaleY = 0;
                if (_asleep) return;
                EyeLScale.ScaleY = EyeRScale.ScaleY = _eyeOpen;
                StartBlink();
            };
            t.Start();
        }

        public enum Reaction { Cheer, Startle, Sip }

        private void Hop()
        {
            var hop = new DoubleAnimationUsingKeyFrames();
            hop.KeyFrames.Add(new EasingDoubleKeyFrame(-5, TimeSpan.FromMilliseconds(170)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(340)) { EasingFunction = new BounceEase { Bounces = 1, Bounciness = 3 } });
            HopShift.BeginAnimation(TranslateTransform.YProperty, hop);
        }

        private void Wiggle()
        {
            var tilt = new DoubleAnimationUsingKeyFrames();
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-8, TimeSpan.FromMilliseconds(120)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(8, TimeSpan.FromMilliseconds(320)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-5, TimeSpan.FromMilliseconds(500)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(680)));
            Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);
        }

        /// <summary>Balancement lent, comme quelqu'un de détendu en fin de journée.</summary>
        private void Sway()
        {
            Busy(2.6);
            var tilt = new DoubleAnimationUsingKeyFrames();
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(5, TimeSpan.FromMilliseconds(700)) { EasingFunction = new SineEase() });
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-5, TimeSpan.FromMilliseconds(1700)) { EasingFunction = new SineEase() });
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(2400)) { EasingFunction = new SineEase() });
            Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);
        }

        /// <summary>Bâillement : yeux plissés, bouche grande ouverte puis qui se referme.</summary>
        private void Yawn()
        {
            Busy(2.4);
            Smile.Visibility = Visibility.Collapsed;
            OpenMouth.Opacity = 1;

            var mouth = new DoubleAnimationUsingKeyFrames();
            mouth.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.Zero));
            mouth.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, TimeSpan.FromMilliseconds(650)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            mouth.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, TimeSpan.FromMilliseconds(1500)));
            mouth.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(2100)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } });
            mouth.Completed += (_, _) => { OpenMouth.Opacity = 0; Smile.Visibility = Visibility.Visible; };
            MouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, mouth);

            // Les yeux se ferment pendant le bâillement
            var eyes = new DoubleAnimationUsingKeyFrames();
            eyes.KeyFrames.Add(new LinearDoubleKeyFrame(_eyeOpen, TimeSpan.Zero));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, TimeSpan.FromMilliseconds(500)));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, TimeSpan.FromMilliseconds(1600)));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(_eyeOpen, TimeSpan.FromMilliseconds(2100)));
            eyes.Completed += (_, _) => StartBlink();
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, eyes);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, eyes);
        }

        /// <summary>Étirement vertical du matin.</summary>
        private void Stretch()
        {
            Busy(1.8);
            var up = new DoubleAnimationUsingKeyFrames();
            up.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, TimeSpan.FromMilliseconds(600)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            up.KeyFrames.Add(new EasingDoubleKeyFrame(1.22, TimeSpan.FromMilliseconds(1000)));
            up.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1500)) { EasingFunction = new BounceEase { Bounces = 1, Bounciness = 4 } });
            var narrow = new DoubleAnimationUsingKeyFrames();
            narrow.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, TimeSpan.FromMilliseconds(600)));
            narrow.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, TimeSpan.FromMilliseconds(1000)));
            narrow.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1500)));
            StretchScale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
            StretchScale.BeginAnimation(ScaleTransform.ScaleXProperty, narrow);
        }

        /// <summary>Mâchonne : la bouche s'ouvre et se ferme plusieurs fois (heure du repas).</summary>
        private void Munch()
        {
            Busy(2.2);
            Smile.Visibility = Visibility.Collapsed;
            OpenMouth.Opacity = 1;

            var chew = new DoubleAnimationUsingKeyFrames();
            double t = 0;
            for (int i = 0; i < 5; i++)
            {
                chew.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, TimeSpan.FromMilliseconds(t += 160)));
                chew.KeyFrames.Add(new EasingDoubleKeyFrame(0.05, TimeSpan.FromMilliseconds(t += 160)));
            }
            chew.Completed += (_, _) => { OpenMouth.Opacity = 0; Smile.Visibility = Visibility.Visible; };
            MouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, chew);

            // Petit rebond de satisfaction en même temps
            Hop();
        }

        /// <summary>La tête tombe de sommeil puis se redresse d'un coup (la nuit).</summary>
        private void Nod()
        {
            Busy(2.2);
            var tilt = new DoubleAnimationUsingKeyFrames();
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(12, TimeSpan.FromMilliseconds(900)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn } });
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(12, TimeSpan.FromMilliseconds(1200)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-3, TimeSpan.FromMilliseconds(1350)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1700)));
            Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);

            var eyes = new DoubleAnimationUsingKeyFrames();
            eyes.KeyFrames.Add(new LinearDoubleKeyFrame(_eyeOpen, TimeSpan.Zero));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, TimeSpan.FromMilliseconds(800)));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, TimeSpan.FromMilliseconds(1200)));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1350)));
            eyes.KeyFrames.Add(new EasingDoubleKeyFrame(_eyeOpen, TimeSpan.FromMilliseconds(1800)));
            eyes.Completed += (_, _) => StartBlink();
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, eyes);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, eyes);
        }

        /// <summary>Petit saut avec inclinaison et « hi! » : le salut quand on revient.</summary>
        public void Wave()
        {
            var tilt = new DoubleAnimationUsingKeyFrames();
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-14, TimeSpan.FromMilliseconds(150)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(14, TimeSpan.FromMilliseconds(400)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-10, TimeSpan.FromMilliseconds(650)));
            tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0, TimeSpan.FromMilliseconds(900)));
            Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);

            Hop();

            var hi = new DoubleAnimationUsingKeyFrames();
            hi.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(150)));
            hi.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromMilliseconds(1500)));
            hi.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(1900)));
            Hello.BeginAnimation(OpacityProperty, hi);
        }

        // ───────────── Animations continues ─────────────

        private void StartBlink()
        {
            // Un clignement toutes les 4 s (plus lent le soir et la nuit)
            double period = _period == Period.Night ? 5.5 : _period == Period.Evening ? 4.6 : 4;
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(period), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(_eyeOpen, TimeSpan.FromSeconds(period - 0.3)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, TimeSpan.FromSeconds(period - 0.18)));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(_eyeOpen, TimeSpan.FromSeconds(period - 0.06)));
            Timeline.SetDesiredFrameRate(blink, 30);
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        }

        /// <summary>Respiration : léger étirement vertical, plus lent et plus ample pendant le sommeil.</summary>
        private void StartBreathing()
        {
            double period = _asleep ? 3.2 : _period == Period.Night ? 2.9 : 2.2, amount = _asleep ? 0.07 : 0.045;
            var breathY = new DoubleAnimation(1, 1 + amount, TimeSpan.FromSeconds(period))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            var breathX = new DoubleAnimation(1, 1 - amount / 2, TimeSpan.FromSeconds(period))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(breathY, 15);
            Timeline.SetDesiredFrameRate(breathX, 15);
            HopScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathY);
            HopScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathX);
        }

        /// <summary>Les « z » montent en s'estompant, en boucle.</summary>
        private void StartSleepZ()
        {
            var rise = new DoubleAnimation(-4, -16, TimeSpan.FromSeconds(2.6)) { RepeatBehavior = RepeatBehavior.Forever };
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.6), RepeatBehavior = RepeatBehavior.Forever };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, TimeSpan.FromSeconds(0.7)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromSeconds(2.6)));
            SleepZ.BeginAnimation(Canvas.TopProperty, rise);
            SleepZ.BeginAnimation(OpacityProperty, fade);
        }
    }
}


