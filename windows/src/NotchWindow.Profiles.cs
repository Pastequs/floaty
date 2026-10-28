using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Navbar.Services;

namespace Navbar
{
    // Profils de réglages (« bureau », « jeu »…) et export / import vers un fichier
    public partial class NotchWindow
    {
        /// <summary>Réglages personnels qu'un profil ne touche pas (identifiants, listes de suivi, alertes).</summary>


        private static string ExportPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Navbar-reglages.json");

        private static Dictionary<string, string> LoadProfiles() => Storage.Load("profiles", new Dictionary<string, string>());

        private void RenderProfileSettings()
        {
            var profiles = LoadProfiles();

            var chips = new WrapPanel();
            foreach (var name in profiles.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
            {
                string captured = name;
                chips.Children.Add(Chip(name, false, () => ApplyProfile(profiles[captured])));
            }
            SettingsList.Children.Add(Row("Profils", chips.Children.Count > 0 ? chips : Hint("Aucun profil. Règle l'appli comme tu veux, puis donne un nom ci-dessous.")));

            SettingsList.Children.Add(Row("Enregistrer", SettingsInput("", "Nom du profil (Entrée)", false, value =>
            {
                value = value.Trim();
                if (value.Length == 0) return;
                var all = LoadProfiles();
                all[value] = SnapshotJson();
                Storage.Save("profiles", all);
                RenderSettings();
            })));

            var actions = new WrapPanel();
            if (profiles.Count > 0)
                actions.Children.Add(Chip("Supprimer un profil…", false, () => RenderProfileDeletion()));
            actions.Children.Add(Chip("Exporter", false, () =>
            {
                try { File.WriteAllText(ExportPath, SnapshotJson()); if (_profileNote != null) _profileNote.Text = "Exporté dans " + ExportPath; }
                catch (Exception ex) { if (_profileNote != null) _profileNote.Text = "Échec : " + ex.Message; }
            }));
            actions.Children.Add(Chip("Importer", false, () =>
            {
                if (!File.Exists(ExportPath)) { if (_profileNote != null) _profileNote.Text = "Aucun fichier à importer : " + ExportPath; return; }
                try { ApplyProfile(File.ReadAllText(ExportPath)); }
                catch (Exception ex) { if (_profileNote != null) _profileNote.Text = "Fichier illisible : " + ex.Message; }
            }));
            SettingsList.Children.Add(Row("Fichier", actions));

            // Bascule automatique : un profil par contexte (réunion = micro/caméra utilisés, jeu = application plein écran)
            var auto = new WrapPanel();
            auto.Children.Add(Toggle("Changer selon le contexte", _settings.ContextAuto, v =>
            {
                _settings.ContextAuto = v;
                Save();
                Shared.ApplyServices();
                RenderSettings();
            }));
            SettingsList.Children.Add(Row("Automatique", auto));

            if (_settings.ContextAuto)
            {
                foreach (var (key, label) in new[] { ("reunion", "En réunion"), ("jeu", "En jeu / plein écran"), ("normal", "Sinon") })
                {
                    var choices = new WrapPanel();
                    string ctx = key;
                    choices.Children.Add(Chip("Aucun", !_settings.ContextProfiles.ContainsKey(ctx), () => { _settings.ContextProfiles.Remove(ctx); Save(); RenderSettings(); }));
                    foreach (var name in profiles.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
                    {
                        string captured = name;
                        choices.Children.Add(Chip(name, _settings.ContextProfiles.TryGetValue(ctx, out var chosen) && chosen == captured, () => { _settings.ContextProfiles[ctx] = captured; Save(); RenderSettings(); }));
                    }
                    SettingsList.Children.Add(Row(label, choices));
                }
                SettingsList.Children.Add(Row("", Hint("Le profil s'applique après deux relevés identiques (environ 12 s). « Sinon » : si aucun profil n'est choisi, les réglages d'avant le changement sont rétablis.")));
            }

            _profileNote = new TextBlock { Foreground = FgAlpha(0x77), FontSize = 11 * _scale, FontFamily = UiFont, TextWrapping = TextWrapping.Wrap };
            _profileNote.Text = "Un profil mémorise l'apparence, les modules et le contenu de la pastille. L'export va dans Documents\\Navbar-reglages.json (sans clés ni secrets).";
            SettingsList.Children.Add(Row("", _profileNote));
        }

        private TextBlock? _profileNote;

        private void RenderProfileDeletion()
        {
            var all = LoadProfiles();
            SettingsList.Children.Clear();
            var chips = new WrapPanel();
            foreach (var name in all.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
            {
                string captured = name;
                chips.Children.Add(Chip(name + "  ✕", false, () => { all.Remove(captured); Storage.Save("profiles", all); RenderSettings(); }));
            }
            SettingsList.Children.Add(Row("Supprimer", chips));
            SettingsList.Children.Add(Row("", Chip("Annuler", false, () => RenderSettings())));
        }

        private static string SnapshotJson() => Shared.SnapshotJson();

        /// <summary>Applique un profil puis recrée la pastille (en rouvrant les réglages ici).</summary>
        private void ApplyProfile(string json)
        {
            Shared.ApplyProfileJson(json, rebuild: false);
            Shared.NotifySettingsChanged(this);
            RebuildRequested?.Invoke(true);
        }
    }
}


