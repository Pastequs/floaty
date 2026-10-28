using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace Navbar
{
    // Étagère de fichiers et liste de tâches
    public partial class NotchWindow
    {
        private static readonly FontFamily UiFont = new("Segoe UI Variable, Segoe UI");
        private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        private List<string> _shelf => Shared.Shelf;
        private List<TaskItem> _tasks => Shared.Tasks;

        // ───────────── Étagère ─────────────

        private void InitShelf()
        {
            ShelfHint.FontSize = 13 * _scale;
            ShelfCaption.FontSize = 10 * _scale;
            // Les textes restent à l'intérieur de la zone en pointillés : marges, retour à la ligne ou points de suspension
            ShelfHint.Margin = new Thickness(16 * _scale, 0, 16 * _scale, 0);
            ShelfHint.TextWrapping = TextWrapping.Wrap;
            ShelfHint.TextAlignment = TextAlignment.Center;
            ShelfCaption.Margin = new Thickness(14 * _scale, 0, 14 * _scale, 7 * _scale);
            ShelfCaption.TextTrimming = TextTrimming.CharacterEllipsis;

            // Captures d'écran : l'image arrive directement dans l'étagère et le presse-papiers
            ShelfShots.Margin = new Thickness(0, 6 * _scale, 8 * _scale, 0);
            foreach (var (label, mode) in new[] { ("Écran", ShotMode.Screen), ("Fenêtre", ShotMode.Window), ("Zone", ShotMode.Region) })
            {
                var m = mode;
                ShelfShots.Children.Add(Chip("\uD83D\uDCF7 " + label, false, () => _ = Shared.TakeScreenshotAsync(m)));
            }
            RenderShelf();
        }

        private void RenderShelf()
        {
            ShelfItems.Children.Clear();
            RenderShelfFolders();
            ShelfHint.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShelfClear.Visibility = _shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShelfCaption.Text = _shelf.Count == 0 ? "" : "Glisse-les où tu veux · clic : ouvrir · clic droit : retirer";
            foreach (var path in _shelf) ShelfItems.Children.Add(BuildShelfItem(path));
        }

        private UIElement BuildShelfItem(string path)
        {
            var icon = new System.Windows.Controls.Image
            {
                Width = 32 * _scale, Height = 32 * _scale, Source = IconFor(path),
                Margin = new Thickness(0, 0, 0, 4 * _scale),
            };
            var name = new TextBlock
            {
                Text = Path.GetFileName(path.TrimEnd('\\')), Foreground = Fg(),
                FontSize = 11 * _scale, FontFamily = UiFont, Width = 68 * _scale,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(icon);
            panel.Children.Add(name);

            var item = new Border
            {
                Child = panel, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Padding = new Thickness(6 * _scale), Margin = new Thickness(0, 0, 6 * _scale, 0),
                CornerRadius = new CornerRadius(8 * _scale), ToolTip = path + "\nClic : ouvrir · Clic droit : retirer · Glisser : déposer ailleurs",
            };

            Point start = default;
            bool dragged = false;
            item.MouseEnter += (_, _) => item.Background = FgAlpha(0x22);
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(null); dragged = false; };
            item.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || dragged) return;
                var p = e.GetPosition(null);
                if (Math.Abs(p.X - start.X) < 6 && Math.Abs(p.Y - start.Y) < 6) return;
                dragged = true;
                DragDrop.DoDragDrop(item, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
            };
            item.MouseLeftButtonUp += (_, _) => { if (!dragged) OpenPath(path); };
            item.MouseRightButtonUp += (_, _) =>
            {
                _shelf.Remove(path);
                Storage.Save("shelf", _shelf);
                RenderShelf();
                Shared.NotifyDataChanged(this);
            };
            return item;
        }

        private static ImageSource? IconFor(string path)
        {
            try
            {
                // SHGetFileInfo gère aussi les dossiers, contrairement à ExtractAssociatedIcon
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (info.hIcon == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
        }

        private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

        private static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* fichier déplacé ou sans application associée */ }
        }

        private void Island_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            if (!_settings.ShelfAutoOpen) return;   // on peut déposer sur la pastille fermée sans l'ouvrir
            _retractTimer.Stop();
            _hovered = true;
            _view = HoverView.Shelf;
            ShelfHint.Text = "Relâche pour ajouter";
            ShelfHint.Visibility = Visibility.Visible;
            ApplyView();
            UpdateState();
        }

        private void Island_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Island_DragLeave(object sender, DragEventArgs e)
        {
            ShelfHint.Text = "Dépose des fichiers ici";
            ShelfHint.Visibility = _shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShelfClear.Visibility = _shelf.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _hovered = false;
            _retractTimer.Start();
        }

        private void Island_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                    if (!_shelf.Contains(f)) _shelf.Add(f);
                Storage.Save("shelf", _shelf);
                RenderShelf();
                Shared.NotifyDataChanged(this);
                ShelfScroll.ScrollToRightEnd();
            }
            ShelfHint.Text = "Dépose des fichiers ici";
            e.Handled = true;
        }

        // ───────────── Tâches ─────────────

        // « Appeler le garage @14:30 » ou « @14h » → rappel à cette heure (demain si déjà passée) ; « @30m » → dans 30 min
        private static readonly Regex DueClock = new(@"\s*@(\d{1,2})[:h](\d{2})?\s*$", RegexOptions.Compiled);
        private static readonly Regex DueRelative = new(@"\s*@(\d{1,3})m\s*$", RegexOptions.Compiled);

        private static TaskItem ParseTask(string text)
        {
            var rel = DueRelative.Match(text);
            if (rel.Success)
                return new TaskItem { Text = text.Substring(0, rel.Index).Trim(), DueAt = DateTime.Now.AddMinutes(int.Parse(rel.Groups[1].Value)) };

            var clk = DueClock.Match(text);
            if (clk.Success)
            {
                int h = int.Parse(clk.Groups[1].Value);
                int m = clk.Groups[2].Success ? int.Parse(clk.Groups[2].Value) : 0;
                if (h < 24 && m < 60)
                {
                    var due = DateTime.Today.AddHours(h).AddMinutes(m);
                    if (due <= DateTime.Now) due = due.AddDays(1);
                    return new TaskItem { Text = text.Substring(0, clk.Index).Trim(), DueAt = due };
                }
            }
            return new TaskItem { Text = text };
        }

        private void SaveTasks()
        {
            Storage.Save("tasks", _tasks);
            RenderTasks();
            Shared.NotifyDataChanged(this);
        }

        // Le champ de saisie a besoin du clavier : on retire temporairement l'option « sans focus »
        private void SetNoActivate(bool on)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            ex = on ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        private void AddBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (AddBox.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            AddBox.Focus();
        }

        private void AddBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => _typing = true;

        private void AddBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _typing = false;
            if (ReferenceEquals(sender, AddBox) && AddBox.Text.Length == 0 && AddBoxFrame.IsVisible) AddBoxFrame.Visibility = Visibility.Collapsed;
            SetNoActivate(true);
            if (!_hovered) _retractTimer.Start();
        }

        private void AddBox_TextChanged(object sender, TextChangedEventArgs e) =>
            AddBoxHint.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    }
}


