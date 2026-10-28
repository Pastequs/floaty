using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Navbar
{
    // Tâches : anneau de progression, boutons Ajouter / Modifier, liste en pastilles, rappels @14:30 ou @30m
    public partial class NotchWindow
    {
        private static readonly Color Done = Color.FromRgb(0x2F, 0xD0, 0x6B);

        private RingGauge? _taskRing;
        private bool _editingTasks;
        private TaskItem? _renaming;

        private void InitTasks()
        {
            _taskRing = new RingGauge(40 * _scale, 3.4 * _scale);
            TaskRingHost.Child = _taskRing;
            TaskRingText.FontSize = 12 * _scale;
            TaskTitle.FontSize = 14 * _scale;
            TaskSub.FontSize = 11 * _scale;
            ((FrameworkElement)TaskTitle.Parent).Margin = new Thickness(10 * _scale, 0, 0, 0);

            foreach (var (button, glyph, text) in new[] { (TaskAddBtn, TaskAddGlyph, TaskAddText), (TaskEditBtn, TaskEditGlyph, TaskEditText) })
            {
                button.CornerRadius = new CornerRadius(12 * _scale);
                button.Padding = new Thickness(10 * _scale, 4 * _scale, 11 * _scale, 4 * _scale);
                button.Margin = new Thickness(5 * _scale, 0, 0, 0);
                glyph.FontSize = 11 * _scale;
                text.FontSize = 12 * _scale;
                text.Margin = new Thickness(6 * _scale, 0, 0, 0);
            }

            AddBoxFrame.CornerRadius = new CornerRadius(14 * _scale);
            AddBoxFrame.Margin = new Thickness(0, 8 * _scale, 0, 0);
            AddBox.FontSize = AddBoxHint.FontSize = 13 * _scale;
            AddBox.Margin = new Thickness(12 * _scale, 5 * _scale, 12 * _scale, 5 * _scale);
            AddBoxHint.Margin = new Thickness(12 * _scale, 0, 0, 0);
            TaskScroll.Margin = new Thickness(0, 8 * _scale, 0, 0);

            RenderTasks();
        }

        private void TaskAdd_Click(object sender, MouseButtonEventArgs e) => OpenAddRow(null);

        private void TaskEdit_Click(object sender, MouseButtonEventArgs e)
        {
            _editingTasks = !_editingTasks;
            if (!_editingTasks) CloseAddRow();
            RenderTasks();
        }

        // ───────────── Saisie ─────────────

        /// <summary>Affiche le champ de saisie : nouvelle tâche, ou renommage d'une tâche existante.</summary>
        private void OpenAddRow(TaskItem? rename)
        {
            _renaming = rename;
            AddBoxFrame.Visibility = Visibility.Visible;
            AddBoxHint.Text = rename == null ? "Nouvelle tâche…  (@14:30 = rappel)" : "Renommer la tâche…";
            AddBox.Text = rename == null ? "" : rename.Text + (rename.DueAt is { } due ? $" @{due:HH:mm}" : "");

            // Le champ a besoin du clavier : on retire le mode « sans focus » le temps de la saisie
            SetNoActivate(false);
            Activate();
            Dispatcher.BeginInvoke(() => { AddBox.Focus(); AddBox.CaretIndex = AddBox.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CloseAddRow()
        {
            AddBoxFrame.Visibility = Visibility.Collapsed;
            AddBox.Clear();
            _renaming = null;
            Keyboard.ClearFocus();
        }

        private void AddBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string text = AddBox.Text.Trim();
                if (text.Length == 0) return;

                var parsed = ParseTask(text);
                if (_renaming != null)
                {
                    _renaming.Text = parsed.Text;
                    if (parsed.DueAt != null) { _renaming.DueAt = parsed.DueAt; _renaming.Notified = false; }
                    CloseAddRow();
                }
                else
                {
                    _tasks.Add(parsed);
                    AddBox.Clear();   // on reste prêt pour la tâche suivante
                }
                SaveTasks();
                TaskScroll.ScrollToTop();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseAddRow();
                e.Handled = true;
            }
        }

        // ───────────── Liste ─────────────

        private void RenderTasks()
        {
            RefreshPill();

            int total = _tasks.Count, done = _tasks.Count(t => t.Done);
            _taskRing?.SetValue(total == 0 ? 0 : done / (double)total, Done, FgAlpha(0x22));
            TaskRingText.Text = total == 0 ? "0" : $"{done}/{total}";
            TaskSub.Text = total == 0 ? "Rien pour l'instant"
                         : done == total ? "Tout est fait ✓"
                         : $"{total - done} restante{(total - done > 1 ? "s" : "")}";

            // « Modifier » devient « Terminé » (couleur d'accent) pendant l'édition
            TaskEditBtn.Background = _editingTasks ? AccentFill() : FgAlpha(0x1F);
            TaskAddBtn.Background = FgAlpha(0x1F);
            var onEdit = _editingTasks ? (IsLight(EffectiveAccentColor()) ? new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)) : Brushes.White) : Fg();
            TaskEditText.Foreground = TaskEditGlyph.Foreground = onEdit;
            TaskAddText.Foreground = TaskAddGlyph.Foreground = Fg();
            TaskEditText.Text = _editingTasks ? "Terminé" : "Modifier";
            TaskEditGlyph.Text = _editingTasks ? "" : "";

            TaskList.Children.Clear();
            if (total == 0)
            {
                TaskList.Children.Add(new TextBlock
                {
                    Text = "Ajoute une tâche avec le bouton ci-dessus. Écris « @14:30 » ou « @30m » pour un rappel.",
                    Foreground = FgAlpha(0x77), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                });
                return;
            }

            // À faire d'abord (rappels les plus proches en tête), puis les tâches terminées
            var ordered = _tasks.Where(t => !(_settings.HideDoneTasks && t.Done))
                                .OrderBy(t => t.Done).ThenBy(t => t.DueAt ?? DateTime.MaxValue);
            foreach (var task in ordered) TaskList.Children.Add(BuildTaskCard(task));
        }

        private UIElement BuildTaskCard(TaskItem task)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Cercle à cocher : plein et vert une fois la tâche faite
            var check = new Border
            {
                Width = 18 * _scale, Height = 18 * _scale, CornerRadius = new CornerRadius(8 * _scale), Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9 * _scale, 0),
                Background = task.Done ? new SolidColorBrush(Done) : Brushes.Transparent,
                BorderBrush = task.Done ? new SolidColorBrush(Done) : FgAlpha(0x88), BorderThickness = new Thickness(1.6 * _scale),
                Child = task.Done ? new TextBlock
                {
                    Text = "", FontFamily = IconFont, FontSize = 10 * _scale, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                } : null,
            };
            check.MouseLeftButtonUp += (_, _) => { task.Done = !task.Done; SaveTasks(); };

            var text = new TextBlock
            {
                Text = task.Text, FontSize = 13 * _scale, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = task.Done ? FgAlpha(0x77) : Fg(),
                TextDecorations = task.Done ? TextDecorations.Strikethrough : null,
            };
            Grid.SetColumn(text, 1);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (_editingTasks)
            {
                right.Children.Add(IconButton("", "Renommer", () => OpenAddRow(task)));
                right.Children.Add(IconButton("", "Supprimer", () => { _tasks.Remove(task); SaveTasks(); }));
            }
            else if (task.DueAt is { } due && !task.Done)
            {
                bool late = due < DateTime.Now;
                right.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8 * _scale), Padding = new Thickness(7 * _scale, 1 * _scale, 7 * _scale, 1 * _scale),
                    Background = late ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x50, 0x50)) : FgAlpha(0x1F),
                    Child = new TextBlock
                    {
                        Text = $"{(late ? "En retard · " : "")}{due:HH:mm}", FontSize = 11 * _scale, FontFamily = UiFont, Foreground = Fg(),
                    },
                });
            }
            Grid.SetColumn(right, 2);

            row.Children.Add(check);
            row.Children.Add(text);
            row.Children.Add(right);

            return new Border
            {
                Child = row, CornerRadius = new CornerRadius(10 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                Padding = new Thickness(9 * _scale, 5 * _scale, 8 * _scale, 5 * _scale),
                Background = task.Done ? new SolidColorBrush(Color.FromArgb(0x52, 40, 120, 60)) : FgAlpha(0x14),
            };
        }

        private UIElement IconButton(string glyph, string tip, Action click)
        {
            var b = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 12 * _scale, Foreground = Fg(), Cursor = Cursors.Hand,
                Width = 26 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, ToolTip = tip,
            };
            b.MouseLeftButtonUp += (_, _) => click();
            return b;
        }
    }
}
