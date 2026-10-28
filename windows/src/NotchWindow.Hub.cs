using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Navbar
{
    // Un seul onglet « Connexions » pour le son, les appareils (Bluetooth, Wi-Fi, batteries) et le réseau : un sélecteur en tête de chaque page passe de l'une à l'autre
    public partial class NotchWindow
    {
        private readonly List<(HoverView View, StackPanel Bar)> _hubBars = new();

        private static bool InHub(HoverView view) => view is HoverView.Controls or HoverView.Devices or HoverView.Network;

        /// <summary>Les pages du groupe comptent pour l'onglet « Connexions » dans la navigation à la molette.</summary>
        private static HoverView TabHome(HoverView view) => InHub(view) ? HoverView.Controls : InToday(view) ? HoverView.Weather : view == HoverView.Water ? HoverView.Timer : view;

        private void InitHub()
        {
            ControlsToggle.ToolTip = "Connexions : Wi-Fi, Bluetooth, mode avion, thème, son, réseau";
            foreach (var (view, me) in new[] { (ControlsView, HoverView.Controls), (DevicesView, HoverView.Devices), (NetView, HoverView.Network) })
            {
                // Une rangée de plus en tête ; le contenu existant descend d'une rangée
                if (view.RowDefinitions.Count == 0) view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                foreach (UIElement child in view.Children) Grid.SetRow(child, Grid.GetRow(child) + 1);
                view.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });

                var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6 * _scale) };
                Grid.SetRow(bar, 0);
                view.Children.Add(bar);
                _hubBars.Add((me, bar));
            }
            UpdateHubBars();
        }

        /// <summary>Reconstruit les sélecteurs : seuls les modules activés apparaissent, la page affichée est mise en avant.</summary>
        private void UpdateHubBars()
        {
            var entries = new List<(string Label, HoverView View)> { ("Rapide", HoverView.Controls) };
            if (_settings.ShowDevices) entries.Add(("Appareils", HoverView.Devices));
            // Le débit et la latence sont déjà dans l'onglet Ressources : pas de page Réseau ici

            foreach (var (me, bar) in _hubBars)
            {
                bar.Children.Clear();
                if (entries.Count < 2) continue;   // un seul module : pas de sélecteur
                foreach (var (label, target) in entries)
                {
                    bool active = target == me;
                    var chip = new Border
                    {
                        Child = new TextBlock { Text = label, Foreground = active ? Fg() : FgAlpha(0x99), FontSize = 11 * _scale, FontWeight = FontWeights.SemiBold, FontFamily = UiFont },
                        Background = FgAlpha(active ? (byte)0x30 : (byte)0x12), CornerRadius = new CornerRadius(10 * _scale),
                        Padding = new Thickness(11 * _scale, 2 * _scale, 11 * _scale, 2 * _scale), Margin = new Thickness(0, 0, 5 * _scale, 0), Cursor = Cursors.Hand,
                    };
                    if (!active)
                    {
                        Press.Attach(chip, 0.94);
                        chip.MouseLeftButtonUp += (_, _) => GoTo(target);
                    }
                    bar.Children.Add(chip);
                }
            }
        }
    }
}
