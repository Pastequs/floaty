using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Navbar
{
    // Petite roue crantée en haut à gauche : ouvre directement la page de réglages du module affiché
    public partial class NotchWindow
    {
        private const string IcoGear =
            "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z " +
            "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z";

        private void InitGear()
        {
            GearBtn.Margin = new Thickness(10 * _scale, 0, 0, 0);
            Press.Attach(GearBtn, 0.85);
            foreach (var tab in TabBar.Children.OfType<FrameworkElement>())
            {
                Press.Attach(tab, 0.86);   // retour dès l'appui sur chaque onglet
                // largeur et marges : voir LayoutTabs
            }
            if (_settings.TabOrder.Count > 0) ApplyTabOrder();
            var icon = Icon(IcoGear, 14, FgAlpha(0x99));
            GearBtn.Child = icon;
            GearBtn.MouseEnter += (_, _) => GearBtn.Child = Icon(IcoGear, 14, Fg());
            GearBtn.MouseLeave += (_, _) => GearBtn.Child = Icon(IcoGear, 14, FgAlpha(0x99));
        }

        /// <summary>Page de réglages (index de PageNames) du module affiché ; -1 s'il n'en a pas.</summary>
        private int SettingsPageFor(HoverView view) => view switch
        {
            HoverView.Auto => InCall ? 17 : _mediaState.HasMedia ? 4 : 2,
            HoverView.Timer => 5,
            HoverView.Water => 6,
            HoverView.Tasks => 7,
            HoverView.Shelf => 8,
            HoverView.Controls => 9,
            HoverView.Devices => 10,
            HoverView.Search => 11,
            HoverView.Clipboard => 12,
            HoverView.System => 13,
            HoverView.Weather => 14,
            HoverView.Market => 15,
            HoverView.Chat => 16,
            HoverView.Discord => 17,
            HoverView.Smart => 18,
            HoverView.Network => 19,
            HoverView.Calendar => 20,
            HoverView.Windows => 21,
            _ => -1,   // Réglages eux-mêmes et Notes (rien à régler)
        };

        private void UpdateGear() =>
            GearBtn.Visibility = SettingsPageFor(_view) >= 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Gear_Click(object sender, MouseButtonEventArgs e)
        {
            int page = SettingsPageFor(_view);
            if (page >= 0) OpenSettingsPage(page);
        }
    }
}







