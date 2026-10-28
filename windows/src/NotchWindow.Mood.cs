using System;
using System.Windows;

namespace Navbar
{
    // Humeur de la mascotte : danse sur la musique, s'inquiète quand le processeur s'emballe, s'endort après un Focus
    public partial class NotchWindow
    {
        private void InitMood()
        {
            Action<string> finished = label =>
            {
                if (label != "Focus")
                {
                    Dispatcher.BeginInvoke(() => React(Watermelon.Reaction.Cheer));
                    return;
                }
                Dispatcher.BeginInvoke(() => { MascotBox.Rest(12); MascotPill.Rest(12); });
            };
            Shared.Timer.Finished += finished;
            Action<string, string> notified = (_, _) => Dispatcher.BeginInvoke(() => React(Watermelon.Reaction.Startle));
            Shared.Notifications.Received += notified;
            Closed += (_, _) => Shared.Notifications.Received -= notified;

            // Chronomètre de réunion : rafraîchit la pastille chaque seconde tant que le micro ou la caméra sont utilisés
            var meetingTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            meetingTimer.Tick += (_, _) => { if (Shared.InMeeting) RefreshPill(); else meetingTimer.Stop(); };
            Action onPrivacy = () => Dispatcher.BeginInvoke(() => { if (Shared.InMeeting) meetingTimer.Start(); else { meetingTimer.Stop(); RefreshPill(); } });
            Shared.PrivacyChanged += onPrivacy;
            Closed += (_, _) => { meetingTimer.Stop(); Shared.PrivacyChanged -= onPrivacy; };
            Closed += (_, _) => Shared.Timer.Finished -= finished;
        }

        private void React(Watermelon.Reaction reaction)
        {
            if (!_settings.MascotReactions) return;
            MascotBox.React(reaction);
            MascotPill.React(reaction);
        }

        /// <summary>À rappeler quand la lecture ou la charge du processeur change.</summary>
        private void UpdateMascotMood()
        {
            bool busy = Shared.LastSystem is { } s && s.Cpu >= 90 && DateTime.Now - Shared.LastSystemAt < TimeSpan.FromSeconds(10);
            bool dancing = _mediaState.HasMedia && _mediaState.Playing;
            var mood = busy ? Watermelon.Mood.Worried : dancing ? Watermelon.Mood.Dance : Watermelon.Mood.Normal;
            MascotBox.SetMood(mood);
            MascotPill.SetMood(mood);
        }
    }
}


