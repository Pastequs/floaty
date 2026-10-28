using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Navbar
{
    // Ordre des onglets personnalisable (flèches dans Réglages › Général)
    public partial class NotchWindow
    {
        private static readonly Dictionary<HoverView, string> TabLabels = new()
        {
            [HoverView.Auto] = "Accueil", [HoverView.Search] = "Recherche", [HoverView.Weather] = "Météo", [HoverView.Market] = "Bourse",
            [HoverView.Calendar] = "Agenda", [HoverView.Network] = "Réseau", [HoverView.Smart] = "Maison", [HoverView.Discord] = "Discord",
            [HoverView.Chat] = "Chat IA", [HoverView.Windows] = "Fenêtres", [HoverView.Notes] = "Notes", [HoverView.System] = "Ressources", [HoverView.Clipboard] = "Presse-papiers",
            [HoverView.Controls] = "Connexions", [HoverView.Devices] = "Appareils", [HoverView.Water] = "Eau", [HoverView.Tasks] = "Tâches",
            [HoverView.Shelf] = "Étagère", [HoverView.Timer] = "Minuteur", [HoverView.Settings] = "Réglages",
        };

        /// <summary>Replace les onglets de la barre dans l'ordre choisi.</summary>
        private void ApplyTabOrder()
        {
            var ordered = TabMap();
            TabBar.Children.Clear();
            foreach (var (tab, _) in ordered) TabBar.Children.Add(tab);
            LayoutTabs();
        }

        private void MoveTab(HoverView view, int direction)
        {
            var names = TabMap().Select(t => t.View.ToString()).ToList();
            int i = names.IndexOf(view.ToString()), j = i + direction;
            if (i < 0 || j < 0 || j >= names.Count) return;
            (names[i], names[j]) = (names[j], names[i]);
            _settings.TabOrder = names;
            Save();
            ApplyTabOrder();
            Shared.NotifySettingsChanged(this);   // les autres écrans se recréent avec le nouvel ordre
            RenderSettings();
        }

        private void RenderTabOrderSettings()
        {
            var panel = new StackPanel();
            var map = TabMap();
            for (int i = 0; i < map.Length; i++)
            {
                var view = map[i].View;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 3 * _scale) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. {TabLabels[view]}", Foreground = map[i].Tab.Visibility == Visibility.Visible ? Fg() : FgAlpha(0x55),
                    FontSize = 12 * _scale, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center,
                });
                var arrows = new StackPanel { Orientation = Orientation.Horizontal };
                if (i > 0) arrows.Children.Add(Chip("▲", false, () => MoveTab(view, -1)));
                if (i < map.Length - 1) arrows.Children.Add(Chip("▼", false, () => MoveTab(view, 1)));
                Grid.SetColumn(arrows, 1);
                row.Children.Add(arrows);
                panel.Children.Add(row);
            }
            panel.Children.Add(Chip("Ordre par défaut", false, () =>
            {
                _settings.TabOrder = new List<string>();
                Save();
                ApplyTabOrder();
                Shared.NotifySettingsChanged(this);
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Ordre", panel));
        }
    }
}

