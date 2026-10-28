using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;

namespace Navbar
{
    // Réseau : débit en direct, connexion active, latence, détection de coupure
    public partial class NotchWindow
    {
        private void InitNetwork()
        {
            NetToggle.Width = NetToggle.Height = 24 * _scale;
            NetToggle.Foreground = FgAlpha(0x99);
            NetTitle.FontSize = 14 * _scale;
            NetNote.FontSize = 11 * _scale;
            NetNote.Margin = new Thickness(8 * _scale, 0, 0, 0);
            NetBody.Margin = new Thickness(0, 8 * _scale, 0, 0);

            Shared.NetUpdated += OnNetUpdated;
            Closed += (_, _) => { Shared.NetUpdated -= OnNetUpdated; Shared.WantNetwork(this, false); };
            RenderNetwork();
        }

        private void NetToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Network);

        private void OnNetUpdated() => Dispatcher.BeginInvoke(() => { if (_view == HoverView.Network) RenderNetwork(); });

        /// <summary>Le détail n'est mesuré que pendant que le module est affiché.</summary>
        private void UpdateNetworkSampling() => Shared.WantNetwork(this, _view == HoverView.Network);

        private Border NetTile(string label, string value, Brush? tone = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont });
            stack.Children.Add(new TextBlock { Text = value, Foreground = tone ?? Fg(), FontSize = 15 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont });
            return new Border
            {
                Child = stack, Background = FgAlpha(0x14), CornerRadius = new CornerRadius(12 * _scale),
                Padding = new Thickness(11 * _scale, 6 * _scale, 11 * _scale, 6 * _scale), Margin = new Thickness(0, 0, 6 * _scale, 6 * _scale), MinWidth = 108 * _scale,
            };
        }

        private void RenderNetwork()
        {
            NetBody.Children.Clear();
            var n = Shared.LastNet;
            if (n == null) { NetBody.Children.Add(Hint("Mesure en cours…")); return; }

            NetNote.Text = n.Online ? (n.Kind.Length > 0 ? n.Kind + (n.Ssid.Length > 0 ? " · " + n.Ssid : "") : "") : "hors ligne";

            var root = new StackPanel();
            var tiles = new WrapPanel();
            tiles.Children.Add(NetTile("↓ Réception", NetworkService.Rate(n.DownBps)));
            tiles.Children.Add(NetTile("↑ Envoi", NetworkService.Rate(n.UpBps)));
            tiles.Children.Add(NetTile("État", n.Online ? "En ligne" : "Hors ligne", n.Online ? Up : Down));
            root.Children.Add(tiles);

            // Latence vers quelques cibles, avec un point de couleur
            var pings = new WrapPanel();
            foreach (var p in n.Pings)
            {
                Brush dot = p.Ms == null ? Down : p.Ms < 60 ? Up : p.Ms < 150 ? new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15)) : Down;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12 * _scale, 0) };
                row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7 * _scale, Height = 7 * _scale, Fill = dot, Margin = new Thickness(0, 0, 5 * _scale, 0), VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(new TextBlock { Text = $"{p.Label} {(p.Ms == null ? "—" : p.Ms + " ms")}", Foreground = FgAlpha(0xAA), FontSize = 11 * _scale, FontFamily = UiFont });
                pings.Children.Add(row);
            }
            root.Children.Add(pings);

            // Courbe du débit descendant récent
            var history = Shared.NetHistory;
            if (history.Count > 3)
            {
                var spark = Sparkline(history, EffectiveAccent());
                spark.Margin = new Thickness(0, 8 * _scale, 0, 0);
                spark.HorizontalAlignment = HorizontalAlignment.Left;
                root.Children.Add(spark);
            }
            NetBody.Children.Add(root);
        }

        // ───────────── Réglages ─────────────

        private void RenderNetworkSettings()
        {
            var options = new WrapPanel();
            options.Children.Add(Toggle("Alerte de coupure", _settings.AlertNetwork, v => { _settings.AlertNetwork = v; Save(); Shared.ApplyServices(); RenderSettings(); }));
            SettingsList.Children.Add(Row("Options", options));
            SettingsList.Children.Add(Row("Mesures", new TextBlock
            {
                Text = "Débit : cartes réseau actives. Latence : Cloudflare (1.1.1.1), Google (8.8.8.8) et ta box. Une coupure est signalée après deux relevés sans réponse. " +
                       "Les mesures détaillées n'ont lieu que lorsque l'onglet est affiché ; sinon, un simple test toutes les 6 secondes.",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
    }
}
