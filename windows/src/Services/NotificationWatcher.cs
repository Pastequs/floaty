using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Navbar.Services
{
    /// <summary>
    /// Aperçu des notifications Windows (messagerie, calendrier…) : lit le centre de notifications via UserNotificationListener.
    /// Désactivé par défaut (il lit le contenu des notifications) ; rien n'est enregistré ni envoyé ailleurs.
    /// </summary>
    public sealed class NotificationWatcher
    {
        public event Action<string, string>? Received;   // application, texte

        private readonly HashSet<uint> _seen = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
        private UserNotificationListener? _listener;
        private bool _first = true, _busy;

        public string Status { get; private set; } = "Désactivé";
        public bool HideContent { get; set; }

        public NotificationWatcher() => _timer.Tick += async (_, _) => await PollAsync();

        public async Task StartAsync()
        {
            if (_timer.IsEnabled) return;
            try
            {
                _listener = UserNotificationListener.Current;
                var access = await _listener.RequestAccessAsync();
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    Status = "Accès refusé : autorise-le dans Paramètres Windows › Système › Notifications (accès aux notifications).";
                    return;
                }
                _first = true;
                _seen.Clear();
                _timer.Start();
                Status = "Actif";
            }
            catch (Exception ex) { Status = "Indisponible : " + ex.Message; }
        }

        public void Stop()
        {
            _timer.Stop();
            Status = "Désactivé";
        }

        private async Task PollAsync()
        {
            if (_busy || _listener == null) return;
            _busy = true;
            try
            {
                var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                var fresh = new List<(string App, string Text)>();
                foreach (var n in list)
                {
                    if (!_seen.Add(n.Id) || _first) continue;
                    string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
                    string text = "";
                    if (!HideContent)
                    {
                        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                        text = string.Join(" · ", binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Take(2) ?? Enumerable.Empty<string>());
                    }
                    fresh.Add((app, text));
                }
                _first = false;
                if (_seen.Count > 500) _seen.Clear();   // évite une liste sans fin ; les notifications actives sont revues sans être ré-annoncées
                foreach (var (app, text) in fresh) Received?.Invoke(app, text);
            }
            catch { /* liste momentanément indisponible */ }
            finally { _busy = false; }
        }
    }
}
