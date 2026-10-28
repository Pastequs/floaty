using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Navbar.Services;

namespace Navbar
{
    // Dossiers favoris de l'étagère : un clic ouvre le dossier, un fichier glissé dessus y est copié
    public partial class NotchWindow
    {
        private void RenderShelfFolders()
        {
            ShelfFolders.Children.Clear();
            ShelfFolders.Margin = new Thickness(0, 0, 8 * _scale, 6 * _scale);
            foreach (var folder in _settings.ShelfFolders.Take(4))
            {
                string path = folder;
                string name = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;
                var chip = (Border)Chip("\uD83D\uDCC1 " + name, false, () =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                });
                chip.ToolTip = path + "\nClic : ouvrir · dépose un fichier pour l'y copier";
                chip.AllowDrop = true;
                chip.DragEnter += (_, e) => { chip.Opacity = 0.7; e.Effects = DragDropEffects.Copy; e.Handled = true; };
                chip.DragLeave += (_, _) => chip.Opacity = 1;
                chip.DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
                chip.Drop += (_, e) =>
                {
                    chip.Opacity = 1;
                    if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
                    int copied = files.Count(f => CopyInto(f, path));
                    Shared.ShowEverywhere(new OverlayInfo("\uE8B7", 1, copied > 0 ? $"{copied} copié(s) dans {name}" : "Copie impossible", "shelf"));
                    e.Handled = true;
                };
                ShelfFolders.Children.Add(chip);
            }
        }

        /// <summary>Copie un fichier ou un dossier dans la destination, sans écraser : « nom (2).ext » si le nom existe déjà.</summary>
        private static bool CopyInto(string source, string destinationFolder)
        {
            try
            {
                string name = Path.GetFileName(source.TrimEnd('\\'));
                string target = Path.Combine(destinationFolder, name);
                for (int i = 2; File.Exists(target) || Directory.Exists(target); i++)
                    target = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(name) + $" ({i})" + Path.GetExtension(name));

                if (Directory.Exists(source)) CopyDirectory(source, target);
                else File.Copy(source, target);
                return true;
            }
            catch { return false; }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private void RenderShelfFolderSettings()
        {
            var chips = new WrapPanel();
            foreach (var folder in _settings.ShelfFolders.ToList())
            {
                string captured = folder;
                chips.Children.Add(Chip(Path.GetFileName(captured.TrimEnd('\\')) + "  ✕", false, () =>
                {
                    _settings.ShelfFolders.Remove(captured);
                    Save();
                    RenderShelfFolders();
                    RenderSettings();
                }));
            }
            chips.Children.Add(Chip("+ Ajouter un dossier", false, () =>
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Dossier favori de l'étagère", UseDescriptionForTitle = true };
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || _settings.ShelfFolders.Contains(dialog.SelectedPath)) return;
                _settings.ShelfFolders.Add(dialog.SelectedPath);
                Save();
                RenderShelfFolders();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Dossiers favoris", chips));
            SettingsList.Children.Add(Row("", new TextBlock
            {
                Text = "Quatre dossiers au plus s'affichent en bas de l'étagère. Un clic l'ouvre ; un fichier glissé dessus y est copié (jamais écrasé).",
                Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
            }));
        }
    }
}
