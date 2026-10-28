#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Navbar
{
    /// <summary>
    /// Vérification de la traduction sans ouvrir l'interface : variable NAVBAR_TEST_LOCCHECK = dossier contenant candidates.txt (textes du code, un par ligne)
    /// et recevant loccheck.txt. Teste le moteur sur des exemples dynamiques, la couverture du dictionnaire et le traducteur d'arbre visuel.
    /// </summary>
    internal static class LocSelfTest
    {
        public static void Run(string folder)
        {
            var report = new StringBuilder();
            Loc.Init("en");
            report.AppendLine("langue: " + Loc.Lang + " ; culture: " + System.Globalization.CultureInfo.CurrentCulture.Name);

            report.AppendLine("--- exemples dynamiques ---");
            foreach (var s in new[]
            {
                "Rechercher ou commander…", " % · Aujourd'hui", "12 ouverte(s) · 20 épinglée(s)", "Ressenti 10°", "pas de pluie dans les 2 h",
                " · mis à jour il y a 5 min", "allumé depuis 13 j 19 h", "Paris · mis à jour à l'instant", "Aucune fenêtre ouverte.", "Tout est fait ✓",
                "Nouvelle tâche…  (@14:30 = rappel)", "Hub IKEA : erreur 500", "Chargement de la météo…", "Résume ce texte en quelques points clairs :",
            }) report.AppendLine($"{s}  =>  {Loc.T(s)}");

            report.AppendLine("--- couverture du dictionnaire ---");
            var file = Path.Combine(folder, "candidates.txt");
            var candidates = File.Exists(file) ? File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Trim().Length > 0).Distinct().ToList() : new List<string>();
            var untranslated = candidates.Where(c => Loc.T(c) == c && Loc.LooksFrench(c)).ToList();
            report.AppendLine($"textes testés: {candidates.Count} ; encore en français: {untranslated.Count}");
            foreach (var u in untranslated) report.AppendLine("- " + u);

            report.AppendLine("--- arbre d'interface ---");
            var panel = new StackPanel();
            var tb1 = new TextBlock { Text = "Réglages" };
            var tb2 = new TextBlock();
            tb2.Inlines.Add(new Run("UV 2 · faible"));
            tb2.Inlines.Add(new Run("   ·   pas de pluie dans les 2 h"));
            var btn = new Button { Content = "Annuler", ToolTip = "Annuler le dernier ajout" };
            var tb3 = new TextBlock { Text = "Texte inconnu du dictionnaire" };
            var tb4 = new TextBlock { ToolTip = new ToolTip { Content = "Fermer la fenêtre" } };
            foreach (UIElement e in new UIElement[] { tb1, tb2, btn, tb3, tb4 }) panel.Children.Add(e);
            report.AppendLine($"debug: inlines tb1={tb1.Inlines.Count} ; visuels du panneau={System.Windows.Media.VisualTreeHelper.GetChildrenCount(panel)} ; T(Réglages)={Loc.T("Réglages")}");
            UiTranslator.Walk(panel);
            report.AppendLine($"TextBlock simple : {tb1.Text}");
            report.AppendLine($"TextBlock à runs : {string.Join(" || ", tb2.Inlines.OfType<Run>().Select(r => r.Text))}");
            report.AppendLine($"Bouton           : {btn.Content} | infobulle : {btn.ToolTip}");
            report.AppendLine($"Texte inconnu    : {tb3.Text}");
            report.AppendLine($"Infobulle objet  : {((ToolTip)tb4.ToolTip).Content}");
            UiTranslator.Walk(panel);
            report.AppendLine($"2e passage       : {tb1.Text} | {btn.Content} | {btn.ToolTip}");

            Loc.Init("fr");
            report.AppendLine($"retour en français : {Loc.T("Réglages")} ; culture {System.Globalization.CultureInfo.CurrentCulture.Name}");
            File.WriteAllText(Path.Combine(folder, "loccheck.txt"), report.ToString(), Encoding.UTF8);
        }
    }
}
#endif
