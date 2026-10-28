using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Navbar.Services
{
    /// <summary>Icônes haute définition (jusqu'à 256 px) lues dans les ressources d'un exécutable : nettes même à grande taille, contrairement aux icônes 32 px des fenêtres.</summary>
    public static class IconExtractor
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[]? ids, uint count, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        /// <summary>Icône de la taille demandée (la ressource la plus proche est choisie par Windows) ; null si le fichier n'en a pas.</summary>
        public static ImageSource? FromFile(string file, int index = 0, int size = 128)
        {
            try
            {
                var handles = new IntPtr[1];
                if (PrivateExtractIcons(file, index, size, size, handles, null, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                DestroyIcon(handles[0]);
                return source;
            }
            catch { return null; }
        }
    }
}
