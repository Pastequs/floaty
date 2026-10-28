using System;
using System.Windows.Threading;

namespace Navbar.Services
{
    public enum TimerStatus { Idle, Running, Paused }

    /// <summary>Minuteur simple et mode Pomodoro (25 min de focus puis 5 min de pause).</summary>
    public sealed class TimerService
    {
        public event Action? Changed;          // démarrage, pause, arrêt : l'affichage doit se réorganiser
        public event Action? Ticked;           // chaque 250 ms pendant que le minuteur tourne
        public event Action<string>? Finished; // libellé de la phase terminée

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime _end;
        private bool _pomodoro;
        private bool _onBreak;

        public int FocusMinutes { get; set; } = 25;
        public int BreakMinutes { get; set; } = 5;

        public TimerStatus Status { get; private set; } = TimerStatus.Idle;
        public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(25);
        public TimeSpan Total { get; private set; } = TimeSpan.FromMinutes(25);
        public string Label { get; private set; } = "Minuteur";

        public TimerService()
        {
            _timer.Tick += (_, _) => Tick();
        }

        public void StartMinutes(int minutes) => Begin(TimeSpan.FromMinutes(minutes), "Minuteur", false, false);
        public void StartPomodoro() => Begin(TimeSpan.FromMinutes(FocusMinutes), "Focus", true, false);

        public void TogglePause()
        {
            if (Status == TimerStatus.Running)
            {
                Remaining = _end - DateTime.UtcNow;
                if (Remaining < TimeSpan.Zero) Remaining = TimeSpan.Zero;
                Status = TimerStatus.Paused;
                _timer.Stop();
            }
            else if (Status == TimerStatus.Paused)
            {
                _end = DateTime.UtcNow + Remaining;
                Status = TimerStatus.Running;
                _timer.Start();
            }
            else
            {
                // Idle : relance la dernière durée choisie
                Begin(Total, Label, _pomodoro, _onBreak);
                return;
            }
            Changed?.Invoke();
        }

        /// <summary>Arrête complètement (y compris un Pomodoro) et revient au minuteur par défaut.</summary>
        public void Stop()
        {
            _timer.Stop();
            Status = TimerStatus.Idle;
            _pomodoro = false;
            _onBreak = false;
            Total = TimeSpan.FromMinutes(FocusMinutes);
            Remaining = Total;
            Label = "Minuteur";
            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Stop();
            Status = TimerStatus.Idle;
            Remaining = Total;
            Changed?.Invoke();
        }

        private void Begin(TimeSpan total, string label, bool pomodoro, bool onBreak)
        {
            Total = total;
            Remaining = total;
            Label = label;
            _pomodoro = pomodoro;
            _onBreak = onBreak;
            _end = DateTime.UtcNow + total;
            Status = TimerStatus.Running;
            _timer.Start();
            Changed?.Invoke();
        }

        private void Tick()
        {
            Remaining = _end - DateTime.UtcNow;
            if (Remaining > TimeSpan.Zero) { Ticked?.Invoke(); return; }

            string finished = Label;
            Finished?.Invoke(finished);

            if (_pomodoro && !_onBreak)
                Begin(TimeSpan.FromMinutes(BreakMinutes), "Pause", true, true);
            else
            {
                if (_pomodoro) { Total = TimeSpan.FromMinutes(FocusMinutes); Label = "Focus"; _onBreak = false; }
                Reset();
            }
        }
    }
}
