using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Navbar.Services;
using Brushes = System.Windows.Media.Brushes;

namespace Navbar
{
    // Notes rapides : épinglage, recherche, listes à cocher et éditeur plein cadre
    public partial class NotchWindow
    {
        private NoteItem? _editingNote;
        private static readonly Regex CheckLine = new(@"^(\s*[-*]\s*)\[( |x|X)\]\s*(.*)$", RegexOptions.Compiled);

        private void InitNotes()
        {
            NotesToggle.Width = NotesToggle.Height = 24 * _scale;
            NotesToggle.Padding = new Thickness(4.5 * _scale);
            NotesIcon.Stroke = FgAlpha(0x99);
            NotesTitle.FontSize = 14 * _scale;
            NotesCount.FontSize = 11 * _scale;
            NotesCount.Margin = new Thickness(8 * _scale, 0, 0, 0);

            foreach (var (button, text) in new[] { (NoteNewBtn, NoteNewText), (NoteDoneBtn, NoteDoneText), (NoteDeleteBtn, NoteDeleteText), (NotePinBtn, NotePinText), (NoteListBtn, NoteListText) })
            {
                button.CornerRadius = new CornerRadius(12 * _scale);
                button.Padding = new Thickness(11 * _scale, 4 * _scale, 11 * _scale, 4 * _scale);
                button.Margin = new Thickness(5 * _scale, 0, 0, 0);
                text.FontSize = 12 * _scale;
                Press.Attach(button, 0.94);
            }
            NoteNewBtn.Background = NoteDeleteBtn.Background = NotePinBtn.Background = NoteListBtn.Background = FgAlpha(0x1F);

            NotesSearchFrame.Margin = new Thickness(0, 6 * _scale, 0, 0);
            NotesSearchFrame.CornerRadius = new CornerRadius(12 * _scale);
            NotesSearch.FontSize = NotesSearchHint.FontSize = 12 * _scale;
            NotesSearch.Margin = NotesSearchHint.Margin = new Thickness(12 * _scale, 3 * _scale, 12 * _scale, 3 * _scale);

            NotesScroll.Margin = new Thickness(0, 6 * _scale, 0, 0);
            NoteEditorFrame.Margin = new Thickness(0, 8 * _scale, 0, 0);
            NoteEditorFrame.CornerRadius = new CornerRadius(14 * _scale);
            NoteEditor.FontSize = 13 * _scale;
            NoteEditor.Margin = new Thickness(12 * _scale, 8 * _scale, 12 * _scale, 8 * _scale);
            PillNote.FontSize = 13 * _scale;
            PillNote.MaxWidth = 150 * _scale;
            PillNote.Margin = new Thickness(7 * _scale, 0, 7 * _scale, 0);

            RenderNotes();
        }

        private void NotesToggle_Click(object sender, MouseButtonEventArgs e) => SetView(HoverView.Notes);

        // ───────────── Recherche ─────────────

        private void NotesSearch_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (NotesSearch.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            NotesSearch.Focus();
        }

        private void NotesSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            NotesSearchHint.Visibility = NotesSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderNotes();
        }

        private void NotesSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            NotesSearch.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        // ───────────── Listes à cocher ─────────────

        private static (int Done, int Total) CheckProgress(string text)
        {
            int done = 0, total = 0;
            foreach (var line in text.Split('\n'))
            {
                var m = CheckLine.Match(line.TrimEnd('\r'));
                if (!m.Success) continue;
                total++;
                if (m.Groups[2].Value != " ") done++;
            }
            return (done, total);
        }

        /// <summary>Coche ou décoche la n-ième ligne de liste de la note.</summary>
        private void ToggleCheck(NoteItem note, int checkIndex)
        {
            var lines = note.Text.Split('\n');
            int seen = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].TrimEnd('\r');
                var m = CheckLine.Match(raw);
                if (!m.Success) continue;
                if (seen++ != checkIndex) continue;
                lines[i] = m.Groups[1].Value + (m.Groups[2].Value == " " ? "[x] " : "[ ] ") + m.Groups[3].Value + (lines[i].EndsWith('\r') ? "\r" : "");
                break;
            }
            note.Text = string.Join("\n", lines);
            note.At = DateTime.Now;
            SaveNotes();
        }

        private void NoteList_Click(object sender, MouseButtonEventArgs e)
        {
            if (_editingNote == null) return;
            string text = NoteEditor.Text;
            int caret = NoteEditor.CaretIndex;
            int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
            NoteEditor.Text = text.Insert(lineStart, "- [ ] ");
            NoteEditor.CaretIndex = lineStart + 6;
            NoteEditor.Focus();
        }

        private void NotePin_Click(object sender, MouseButtonEventArgs e)
        {
            if (_editingNote == null) return;
            _editingNote.Pinned = !_editingNote.Pinned;
            NotePinText.Text = _editingNote.Pinned ? "Désépingler" : "Épingler";
            Storage.Save("notes", Shared.Notes);
            Shared.NotifyDataChanged(this);
            RefreshPill();
        }

        // ───────────── Liste ─────────────

        private void RenderNotes()
        {
            var all = Shared.Notes;
            string filter = NotesSearch.Text.Trim();
            var notes = all.Where(n => filter.Length == 0 || n.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                           .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.At).ToList();
            NotesCount.Text = all.Count == 0 ? "" : filter.Length > 0 ? $"{notes.Count} trouvée(s)" : $"{all.Count}";

            bool editing = _editingNote != null;
            NotesScroll.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            NotesSearchFrame.Visibility = !editing && (all.Count > 3 || filter.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
            NoteEditorFrame.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            NoteNewBtn.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
            NoteDoneBtn.Visibility = NoteDeleteBtn.Visibility = NotePinBtn.Visibility = NoteListBtn.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            NoteDoneBtn.Background = AccentFill();
            NoteDoneText.Foreground = IsLight(EffectiveAccentColor()) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x18)) : Brushes.White;
            if (editing) { NotePinText.Text = _editingNote!.Pinned ? "Désépingler" : "Épingler"; return; }

            NotesList.Children.Clear();
            if (notes.Count == 0)
            {
                NotesList.Children.Add(new TextBlock
                {
                    Text = filter.Length > 0 ? "Aucune note ne correspond." : "Aucune note. « + Nouvelle » pour en écrire une : elle reste sur ton PC.",
                    Foreground = FgAlpha(0x77), FontSize = 12 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap,
                });
                return;
            }

            foreach (var note in notes) NotesList.Children.Add(BuildNoteCard(note));
        }

        private UIElement BuildNoteCard(NoteItem note)
        {
            string[] lines = note.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var (done, total) = CheckProgress(note.Text);
            string title = lines.Length > 0 ? CheckLine.Replace(lines[0], "$3") : "(vide)";

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = title, Foreground = Fg(), FontSize = 13 * _scale, FontWeight = FontWeights.SemiBold,
                FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
            });

            if (total > 0)
            {
                // Les premières lignes de la liste, cochables d'un clic sans ouvrir la note
                int index = 0, shown = 0;
                foreach (var line in note.Text.Split('\n'))
                {
                    var m = CheckLine.Match(line.TrimEnd('\r'));
                    if (!m.Success) continue;
                    int checkIndex = index++;
                    if (shown++ >= 3) continue;
                    bool ticked = m.Groups[2].Value != " ";
                    var box = new TextBlock { Text = ticked ? "" : "", FontFamily = IconFont, FontSize = 12 * _scale, Foreground = ticked ? EffectiveAccent() : FgAlpha(0x99), Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
                    var label = new TextBlock
                    {
                        Text = m.Groups[3].Value, FontSize = 12 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6 * _scale, 0, 0, 0),
                        Foreground = ticked ? FgAlpha(0x66) : Fg(), TextDecorations = ticked ? TextDecorations.Strikethrough : null,
                    };
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1 * _scale, 0, 0) };
                    row.Children.Add(box);
                    row.Children.Add(label);
                    var captured = note;
                    box.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleCheck(captured, checkIndex); };
                    text.Children.Add(row);
                }
                text.Children.Add(new TextBlock { Text = $"{done}/{total} · " + note.At.ToString("dd/MM HH:mm"), Foreground = FgAlpha(0x66), FontSize = 10 * _scale, FontFamily = UiFont, Margin = new Thickness(0, 2 * _scale, 0, 0) });
            }
            else
                text.Children.Add(new TextBlock
                {
                    Text = (lines.Length > 1 ? lines[1] + " · " : "") + note.At.ToString("dd/MM HH:mm"),
                    Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextTrimming = TextTrimming.CharacterEllipsis,
                });
            grid.Children.Add(text);

            var pin = new TextBlock
            {
                Text = "", FontFamily = IconFont, FontSize = 11 * _scale, Cursor = Cursors.Hand, Width = 22 * _scale, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Foreground = note.Pinned ? EffectiveAccent() : FgAlpha(0x44), Background = Brushes.Transparent, ToolTip = note.Pinned ? "Désépingler" : "Épingler en tête",
            };
            var pinned = note;
            pin.MouseLeftButtonUp += (_, e) => { e.Handled = true; pinned.Pinned = !pinned.Pinned; SaveNotes(); RefreshPill(); };
            Grid.SetColumn(pin, 1);
            grid.Children.Add(pin);

            var card = new Border
            {
                Child = grid, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(10 * _scale), Margin = new Thickness(0, 0, 0, 4 * _scale),
                Padding = new Thickness(10 * _scale, 5 * _scale, 6 * _scale, 5 * _scale), Background = note.Pinned ? FgAlpha(0x1E) : FgAlpha(0x14),
            };
            card.MouseEnter += (_, _) => card.Background = FgAlpha(0x2A);
            card.MouseLeave += (_, _) => card.Background = note.Pinned ? FgAlpha(0x1E) : FgAlpha(0x14);
            var open = note;
            card.MouseLeftButtonUp += (_, _) => OpenNote(open);
            return card;
        }

        /// <summary>Première ligne de la note épinglée la plus récente, pour la pastille fermée.</summary>
        private bool FillNotePill()
        {
            if (!_settings.PillNote) return false;
            var note = Shared.Notes.Where(n => n.Pinned).OrderByDescending(n => n.At).FirstOrDefault();
            if (note == null) return false;
            string first = note.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            PillNote.Text = "📌 " + CheckLine.Replace(first, "$3");
            return true;
        }

        // ───────────── Édition ─────────────

        private void NoteNew_Click(object sender, MouseButtonEventArgs e)
        {
            var note = new NoteItem();
            Shared.Notes.Insert(0, note);
            OpenNote(note);
        }

        private void OpenNote(NoteItem note)
        {
            _editingNote = note;
            NoteEditor.Text = note.Text;
            RenderNotes();

            SetNoActivate(false);   // le clavier est nécessaire pour écrire
            Activate();
            Dispatcher.BeginInvoke(() => { NoteEditor.Focus(); NoteEditor.CaretIndex = NoteEditor.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void NoteEditor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (NoteEditor.IsKeyboardFocusWithin) return;
            SetNoActivate(false);
            Activate();
            NoteEditor.Focus();
        }

        private void NoteEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseNote(); e.Handled = true; return; }

            // Entrée sur une ligne de liste : la ligne suivante démarre avec sa propre case ; sur une case vide, la liste se termine
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                string text = NoteEditor.Text;
                int caret = NoteEditor.CaretIndex;
                int start = caret == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                int end = text.IndexOf('\n', caret);
                string line = text.Substring(start, (end < 0 ? text.Length : end) - start).TrimEnd('\r');
                var m = CheckLine.Match(line);
                if (m.Success)
                {
                    e.Handled = true;
                    if (m.Groups[3].Value.Length == 0)
                    {
                        NoteEditor.Text = text.Remove(start, line.Length);   // case vide : on sort de la liste
                        NoteEditor.CaretIndex = start;
                    }
                    else
                    {
                        string insert = "\n" + m.Groups[1].Value + "[ ] ";
                        NoteEditor.Text = text.Insert(caret, insert);
                        NoteEditor.CaretIndex = caret + insert.Length;
                    }
                }
            }
        }

        private void NoteDone_Click(object sender, MouseButtonEventArgs e) => CloseNote();

        private void NoteDelete_Click(object sender, MouseButtonEventArgs e)
        {
            if (_editingNote == null) return;
            Shared.Notes.Remove(_editingNote);
            _editingNote = null;
            SaveNotes();
        }

        /// <summary>Enregistre la note en cours ; une note laissée vide disparaît.</summary>
        private void CloseNote()
        {
            if (_editingNote == null) return;

            string text = NoteEditor.Text;
            if (string.IsNullOrWhiteSpace(text)) Shared.Notes.Remove(_editingNote);
            else if (text != _editingNote.Text) { _editingNote.Text = text; _editingNote.At = DateTime.Now; }

            _editingNote = null;
            Keyboard.ClearFocus();
            SaveNotes();
        }

        private void SaveNotes()
        {
            Storage.Save("notes", Shared.Notes);
            RenderNotes();
            RefreshPill();
            Shared.NotifyDataChanged(this);
        }
    }
}

