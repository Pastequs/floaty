using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Navbar
{
    // Rendu Markdown léger des réponses du chat : titres, listes, citations, gras, italique, code en ligne, blocs de code copiables, liens
    public partial class NotchWindow
    {
        private static readonly Regex MdInline = new(@"(`[^`\n]+`)|(\*\*[^*\n]+\*\*)|(__[^_\n]+__)|(\[[^\]\n]+\]\(https?://[^)\s]+\))|(\*[^*\s][^*\n]*\*)|((?<![\w])_[^_\s][^_\n]*_(?![\w]))", RegexOptions.Compiled);
        private static readonly Regex MdLink = new(@"^\[([^\]]+)\]\((https?://[^)\s]+)\)$", RegexOptions.Compiled);
        private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Courier New");

        /// <summary>Convertit un texte Markdown en éléments WPF ; tolère un bloc de code non fermé (réponse en cours d'écriture).</summary>
        private UIElement RenderMarkdown(string markdown)
        {
            var panel = new StackPanel();
            var lines = markdown.Replace("\r", "").Split('\n');
            var paragraph = new List<string>();

            void FlushParagraph()
            {
                if (paragraph.Count == 0) return;
                panel.Children.Add(InlineText(string.Join("\n", paragraph), 12.5, FontWeights.Normal, new Thickness(0, 0, 0, 4 * _scale)));
                paragraph.Clear();
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Bloc de code : de « ``` » à « ``` » (ou jusqu'à la fin si pas encore fermé)
                if (line.TrimStart().StartsWith("```"))
                {
                    FlushParagraph();
                    string language = line.TrimStart().Substring(3).Trim();
                    var code = new List<string>();
                    for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                    panel.Children.Add(CodeBlock(string.Join("\n", code), language));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(); continue; }

                var heading = Regex.Match(line, @"^(#{1,4})\s+(.*)$");
                if (heading.Success)
                {
                    FlushParagraph();
                    int level = heading.Groups[1].Length;
                    panel.Children.Add(InlineText(heading.Groups[2].Value, level == 1 ? 15 : level == 2 ? 14 : 13, FontWeights.SemiBold, new Thickness(0, 4 * _scale, 0, 3 * _scale)));
                    continue;
                }

                var item = Regex.Match(line, @"^(\s*)([-*•]|\d+[.)])\s+(.*)$");
                if (item.Success)
                {
                    FlushParagraph();
                    int indent = item.Groups[1].Length / 2;
                    string marker = char.IsDigit(item.Groups[2].Value[0]) ? item.Groups[2].Value : "•";
                    var row = new Grid { Margin = new Thickness((8 + indent * 12) * _scale, 0, 0, 2 * _scale) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16 * _scale) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = marker, Foreground = FgAlpha(0x99), FontSize = 13 * _scale, FontFamily = UiFont });
                    var text = InlineText(item.Groups[3].Value, 12.5, FontWeights.Normal, new Thickness(0));
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);
                    panel.Children.Add(row);
                    continue;
                }

                if (line.StartsWith(">"))
                {
                    FlushParagraph();
                    var quote = new Border { BorderBrush = FgAlpha(0x55), BorderThickness = new Thickness(2 * _scale, 0, 0, 0), Padding = new Thickness(8 * _scale, 0, 0, 0), Margin = new Thickness(0, 0, 0, 4 * _scale) };
                    quote.Child = InlineText(line.TrimStart('>', ' '), 12.5, FontWeights.Normal, new Thickness(0));
                    ((TextBlock)quote.Child).Foreground = FgAlpha(0xBB);
                    panel.Children.Add(quote);
                    continue;
                }

                if (Regex.IsMatch(line, @"^\s*(---+|\*\*\*+)\s*$")) { FlushParagraph(); panel.Children.Add(new Border { Height = 1, Background = FgAlpha(0x33), Margin = new Thickness(0, 4 * _scale, 0, 6 * _scale) }); continue; }

                paragraph.Add(line);
            }
            FlushParagraph();
            return panel;
        }

        private TextBlock InlineText(string text, double size, FontWeight weight, Thickness margin)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size * _scale, FontFamily = UiFont, FontWeight = weight, Foreground = Fg(), Margin = margin };
            int last = 0;
            foreach (Match m in MdInline.Matches(text))
            {
                if (m.Index > last) block.Inlines.Add(new Run(text.Substring(last, m.Index - last)));
                string token = m.Value;
                if (token.StartsWith("`")) block.Inlines.Add(new Run(token.Trim('`')) { FontFamily = CodeFont, FontSize = size * 0.93 * _scale, Background = FgAlpha(0x22) });
                else if (token.StartsWith("**") || token.StartsWith("__")) block.Inlines.Add(new Run(token.Substring(2, token.Length - 4)) { FontWeight = FontWeights.Bold });
                else if (token.StartsWith("["))
                {
                    var link = MdLink.Match(token);
                    var hyperlink = new Hyperlink(new Run(link.Groups[1].Value)) { Foreground = EffectiveAccent(), NavigateUri = new Uri(link.Groups[2].Value) };
                    hyperlink.RequestNavigate += (_, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { } e.Handled = true; };
                    block.Inlines.Add(hyperlink);
                }
                else block.Inlines.Add(new Run(token.Substring(1, token.Length - 2)) { FontStyle = FontStyles.Italic });
                last = m.Index + m.Length;
            }
            if (last < text.Length) block.Inlines.Add(new Run(text.Substring(last)));
            return block;
        }

        /// <summary>Bloc de code : police à chasse fixe, langue en haut à gauche, bouton « Copier ».</summary>
        private UIElement CodeBlock(string code, string language)
        {
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = language.Length > 0 ? language : "code", Foreground = FgAlpha(0x77), FontSize = 10 * _scale, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center });
            var copy = new TextBlock { Text = "Copier", Foreground = FgAlpha(0xAA), FontSize = 10 * _scale, FontFamily = UiFont, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, Background = System.Windows.Media.Brushes.Transparent };
            copy.MouseEnter += (_, _) => copy.Foreground = Fg();
            copy.MouseLeave += (_, _) => copy.Foreground = FgAlpha(0xAA);
            copy.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                try { Clipboard.SetText(code); copy.Text = "Copié ✓"; } catch { copy.Text = "Échec"; }
                var reset = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                reset.Tick += (_, _) => { reset.Stop(); copy.Text = "Copier"; };
                reset.Start();
            };
            head.Children.Add(copy);

            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(new TextBlock
            {
                Text = code, FontFamily = CodeFont, FontSize = 12 * _scale, Foreground = Fg(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3 * _scale, 0, 0),
            });
            return new Border
            {
                Child = stack, Background = FgAlpha(0x18), CornerRadius = new CornerRadius(8 * _scale),
                Padding = new Thickness(9 * _scale, 5 * _scale, 9 * _scale, 6 * _scale), Margin = new Thickness(0, 2 * _scale, 0, 5 * _scale),
            };
        }
    }
}
