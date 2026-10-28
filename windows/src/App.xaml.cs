using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows;
using Navbar.Services;
using Forms = System.Windows.Forms;

namespace Navbar
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private Forms.NotifyIcon? _tray;
        private Forms.ToolStripItem? _quit;
        private readonly List<NotchWindow> _windows = new();
        private Settings _settings = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _mutex = new Mutex(true, "Navbar.SingleInstance", out bool created);
            if (!created)
            {
                Shutdown();
                return;
            }

            _settings = Storage.Load("settings", new Settings());
            MigrateScreens();
            if (_settings.Accent == "nusa") _settings.Accent = "gradient";   // ancien nom de l'accent dégradé
            Loc.Init(_settings.Language);
            UiTranslator.Start();
#if DEBUG
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_LOCCHECK") is { Length: > 0 } locCheck) { LocSelfTest.Run(locCheck); Shutdown(); return; }   // vérification de la traduction, sans ouvrir l'interface
#endif
            Shared.Init(_settings);
            Shared.SettingsChanged += OnSettingsChanged;
            Exit += (_, _) => Shared.OnExit();
            Shared.RebuildAllRequested += () => Dispatcher.BeginInvoke(() => RebuildAll(null));   // changement de profil automatique

            RebuildAll(null);
            Shared.ApplyServices();

            // Tous les réglages sont dans l'encoche (onglet engrenage) : l'icône ne sert qu'à quitter
            var menu = new Forms.ContextMenuStrip();
            _quit = menu.Items.Add(Loc.T("Quitter"), null, (_, _) => Shutdown());
            _tray = new Forms.NotifyIcon
            {
                Icon = (Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null) ?? SystemIcons.Application,
                Text = "Navbar",
                Visible = true,
                ContextMenuStrip = menu,
            };
        }

        /// <summary>L'ancien réglage « un écran » devient une liste d'écrans.</summary>
        private void MigrateScreens()
        {
            if (_settings.Screens.Count == 0 && !string.IsNullOrEmpty(_settings.ScreenName))
                _settings.Screens.Add(_settings.ScreenName);
        }

        /// <summary>Noms des écrans à équiper : ceux du réglage qui existent encore, sans doublon (l'écran principal par défaut).</summary>
        private List<string> TargetScreens()
        {
            var names = _settings.Screens
                .Select(n => ScreenService.Resolve(n).DeviceName)
                .Distinct()
                .ToList();
            if (names.Count == 0) names.Add(ScreenService.Resolve("").DeviceName);
            return names;
        }

        /// <summary>Recrée toutes les fenêtres (une par écran) ; <paramref name="reopenOn"/> rouvre les réglages sur cet écran.</summary>
        private void RebuildAll(string? reopenOn)
        {
            if (_quit != null) _quit.Text = Loc.T("Quitter");   // la langue a pu changer
            foreach (var w in _windows.ToList()) CloseWindow(w);

            foreach (var screen in TargetScreens()) CreateWindow(screen, reopenOn);
        }

        private NotchWindow CreateWindow(string screen, string? reopenOn)
        {
            var window = new NotchWindow(_settings, screen);
            window.RebuildRequested += reopen => RebuildAll(reopen ? screen : null);
            _windows.Add(window);
            window.Show();
            if (reopenOn == screen) window.ReopenSettings();
            return window;
        }

        private void CloseWindow(NotchWindow window)
        {
            _windows.Remove(window);
            window.Close();
        }

        /// <summary>Un réglage a changé dans une fenêtre : on recrée les autres pour qu'elles le reflètent.</summary>
        private void OnSettingsChanged(object? sender)
        {
            // Le jeu d'écrans a pu changer : on resynchronise les fenêtres
            var wanted = TargetScreens();
            var current = _windows.Select(w => w.ScreenName).ToList();
            if (!wanted.OrderBy(x => x).SequenceEqual(current.OrderBy(x => x)))
            {
                RebuildAll(null);
                return;
            }

            foreach (var window in _windows.Where(w => !ReferenceEquals(w, sender)).ToList())
            {
                string screen = window.ScreenName;
                CloseWindow(window);
                CreateWindow(screen, null);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}


