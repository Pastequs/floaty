using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Navbar
{
    /// <summary>
    /// Traduit l'interface déjà construite : pour chaque fenêtre, les textes (TextBlock, boutons, infobulles) sont passés dans <see cref="Loc.T"/>.
    /// Un balayage a lieu peu après chaque mise en page, seulement en anglais ; ce qu'on a déjà traduit n'est pas retraité.
    /// </summary>
    internal static class UiTranslator
    {
        private static readonly ConditionalWeakTable<object, string> Produced = new();
        private static readonly HashSet<Window> Dirty = new();
        private static readonly HashSet<Window> Attached = new();

#if DEBUG
        /// <summary>Textes restés en français pendant un test (variable NAVBAR_TEST_FRLOG = fichier de sortie).</summary>
        private static readonly HashSet<string> Missing = new();
#endif

        public static void Start()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) => Attach((Window)s)));
            // Avant chaque image : le texte français n'est jamais affiché, même une fraction de seconde
            CompositionTarget.Rendering += (_, _) => Sweep();
        }

        private static void Attach(Window window)
        {
            if (!Attached.Add(window)) return;
            window.LayoutUpdated += (_, _) => Dirty.Add(window);
            window.Closed += (_, _) => { Attached.Remove(window); Dirty.Remove(window); };
            Dirty.Add(window);
        }

        private static void Sweep()
        {
            if (!Loc.English || Dirty.Count == 0) return;
            var windows = new List<Window>(Dirty);
            Dirty.Clear();
            foreach (var window in windows) Walk(window);
#if DEBUG
            DumpMissing();
#endif
        }

        internal static void Walk(DependencyObject node)
        {
            switch (node)
            {
                case TextBlock tb:
                    // Un texte posé par la propriété Text n'a pas d'éléments Run : on le traite directement ; sinon on traduit chaque Run
                    var inlines = tb.Inlines;
                    if (inlines.Count == 0) { if (tb.Text.Length > 0) tb.Text = Translate(tb, tb.Text); }
                    else if (inlines.Count == 1 && inlines.FirstInline is Run only)
                    {
                        // Un seul Run (cas du texte posé par Text) : on repasse par Text pour que la propriété reste cohérente
                        string current = only.Text;
                        if (current.Length > 0 && Translate(tb, current) is var translated && translated != current) tb.Text = translated;
                    }
                    else WalkInlines(inlines);
                    break;
                case ContentControl cc when cc.Content is string s: cc.Content = Translate(cc, s); break;
                case HeaderedContentControl hc when hc.Header is string h: hc.Header = Translate(hc, h); break;
            }
            if (node is FrameworkElement fe && fe.ToolTip is string tip) fe.ToolTip = Translate(fe, tip);
            if (node is FrameworkElement { ToolTip: ToolTip { Content: string tc } tt }) tt.Content = Translate(tt, tc);

            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
            }
        }

        private static void WalkInlines(InlineCollection inlines)
        {
            foreach (var inline in new List<Inline>(inlines))   // copie : changer le texte d'un Run modifie la collection
            {
                if (inline is Run run && run.Text.Length > 0) run.Text = Translate(run, run.Text);
                else if (inline is Span span) WalkInlines(span.Inlines);
            }
        }

        private static string Translate(object owner, string text)
        {
            if (text.Length == 0) return text;
            if (Produced.TryGetValue(owner, out var last) && last == text) return text;   // déjà notre traduction
            string result = Loc.T(text);
#if DEBUG
            if ((result == text && text.Count(char.IsLetter) >= 3) || Loc.LooksFrench(result)) Missing.Add(result == text ? text : text + "  =>  " + result);   // inchangé ou traduit à moitié : on relit la liste pour y repérer les oublis
#endif
            Produced.Remove(owner);
            Produced.Add(owner, result);
            return result;
        }

#if DEBUG
        /// <summary>Écrit les textes non traduits dans le fichier demandé (vérification pendant les tests).</summary>
        public static void DumpMissing()
        {
            if (Environment.GetEnvironmentVariable("NAVBAR_TEST_FRLOG") is { Length: > 0 } path)
                System.IO.File.WriteAllLines(path, Missing);
        }
#endif
    }
}
