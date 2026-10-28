using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Navbar.Services
{
    public sealed class TaskItem
    {
        public string Text { get; set; } = "";
        public bool Done { get; set; }
        public DateTime? DueAt { get; set; }
        public bool Notified { get; set; }
    }

    public sealed class NoteItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get; set; } = "";
        public DateTime At { get; set; } = DateTime.Now;
        public bool Pinned { get; set; }           // reste en tête de liste (et peut s'afficher dans la pastille)
    }

    public sealed class Routine
    {
        public string Name { get; set; } = "";
        public List<string> Ids { get; set; } = new();   // appareils concernés (Home Assistant ou IKEA)
        public bool TurnOn { get; set; }                 // faux : tout éteindre ; vrai : tout allumer
    }

    public sealed class PriceAlert
    {
        public string Symbol { get; set; } = "";
        public bool Above { get; set; } = true;     // vrai : prévenir au-dessus du seuil ; faux : en dessous
        public double Price { get; set; }
        public bool Fired { get; set; }             // déjà déclenchée : elle ne sonne qu'une fois
    }

    public sealed class WaterEntry
    {
        public DateTime At { get; set; }
        public int Ml { get; set; }
    }

    public sealed class Settings
    {
        public int WaterGoalMl { get; set; } = 2000;
        public int BottleMl { get; set; } = 500;
        public int WaterFromHour { get; set; } = 9;    // les rappels ne sonnent qu'entre ces heures
        public int WaterToHour { get; set; } = 21;
        public bool ShowWater { get; set; } = true;
        public bool ShowDevices { get; set; } = true;
        public bool ShowSearch { get; set; } = true;
        public bool ShowClipboard { get; set; } = true;
        public bool ShowSystem { get; set; } = true;
        public bool ShowWeather { get; set; } = true;
        public bool WeatherHourly { get; set; } = false;       // prévision heure par heure au lieu des cinq jours
        public bool WeatherRainAlert { get; set; } = true;     // aperçu quand la pluie arrive dans l'heure
        public bool PillWeather { get; set; } = false;
        public string WeatherCity { get; set; } = "";          // vide = localisation approximative par IP
        public bool WeatherFahrenheit { get; set; } = false;
        public bool ShowNotes { get; set; } = true;
        public bool ShowMarket { get; set; } = true;
        public bool PillMarket { get; set; } = false;          // premier titre de la liste dans la pastille fermée
        public List<string> Watchlist { get; set; } = new() { "BTC-EUR", "ETH-EUR", "AAPL", "MSFT" };
        public bool AccentFromWallpaper { get; set; } = false;   // l'accent suit la couleur dominante du fond d'écran
        public bool AccentFromApp { get; set; } = false;         // l'accent suit l'icône de l'application active
        public bool AccentFromArt { get; set; } = false;      // l'accent suit la pochette de l'album en lecture
        public bool ShowHome { get; set; } = false;           // domotique Home Assistant (désactivée tant qu'elle n'est pas configurée)
        public string HaUrl { get; set; } = "";
        public List<string> HaEntities { get; set; } = new();
        public bool ShowNotifications { get; set; } = false;  // aperçu des notifications Windows (lit leur contenu : désactivé par défaut)
        public bool NotificationsHideContent { get; set; } = false;
        public bool MeetingMode { get; set; } = true;         // micro ou caméra utilisés : mode discret + chronomètre dans la pastille
        public bool ShowNetwork { get; set; } = true;
        public bool AlertNetwork { get; set; } = true;        // aperçu quand la connexion tombe ou revient
        public List<string> ShelfFolders { get; set; } = new();   // dossiers favoris de l'étagère (ouverts ou recevant un dépôt)
        public bool ShowCalendar { get; set; } = false;       // agenda via Home Assistant (désactivé tant qu'il n'est pas configuré)
        public List<string> CalendarEntities { get; set; } = new();
        public int CalendarReminderMin { get; set; } = 10;    // prévenir X minutes avant un rendez-vous (0 = jamais)
        public bool CalendarWeekView { get; set; } = false;   // vue semaine au lieu de la liste
        public bool PillCalendar { get; set; } = false;       // prochain rendez-vous dans la pastille fermée
        public string DirigeraIp { get; set; } = "";           // adresse locale du hub IKEA DIRIGERA
        public bool PillNote { get; set; } = false;           // note épinglée dans la pastille fermée
        public bool HomeGroupRooms { get; set; } = false;      // regrouper les appareils par pièce (désactivé : simple grille de tuiles)
        public bool HomeAllOffButton { get; set; } = false;    // bouton « Tout éteindre » en tête
        public List<Routine> Routines { get; set; } = new();
        public List<string> TabOrder { get; set; } = new();    // ordre personnalisé des onglets (noms de modules) ; vide = ordre par défaut
        public bool ContextAuto { get; set; } = false;         // changer de profil selon le contexte (réunion, jeu, sinon)
        public Dictionary<string, string> ContextProfiles { get; set; } = new();   // contexte (reunion / jeu / normal) -> nom du profil
        public bool ShowWindows { get; set; } = true;          // onglet « Fenêtres » (fenêtres ouvertes)
        public bool OpenOnWindows { get; set; } = false;       // la pastille s'ouvre sur l'onglet Fenêtres plutôt que sur l'accueil
        public bool ShowPinned { get; set; } = true;           // applications épinglées à la barre des tâches Windows, ouvertes ou non
        public string DockPlacement { get; set; } = "right";   // right : tout à droite de la pastille ; around : épinglées à gauche, autres fenêtres à droite
        public int DockIconSize { get; set; } = 17;             // taille des icônes de la barre autour de la pastille (pixels)
        public bool WindowPreview { get; set; } = true;        // aperçu des fenêtres au survol d'une icône de la barre
        public List<string> ExtraPinned { get; set; } = new();     // applications épinglées à NOTRE barre (chemins d'exécutables), sans toucher à celle de Windows
        public List<string> DockOrder { get; set; } = new();       // ordre choisi des éléments de la barre (clés TaskbarItem.OrderKey), modifié par glisser-déposer
        public List<string> HiddenPinned { get; set; } = new();   // épingles de la barre Windows masquées dans NOTRE barre (nom du raccourci)
        public string PillEdge { get; set; } = "top";           // bord de l'écran de la pastille : top, bottom, left, right
        public bool DockStrip { get; set; } = false;           // rangée d'icônes permanente à côté de la pastille fermée
        public int DockMax { get; set; } = 10;
        public string WindowsLayout { get; set; } = "vertical";   // vertical : liste en lignes / colonne d'icônes ; horizontal : tuiles / rangée d'icônes
        public string DockSide { get; set; } = "right";            // colonne d'icônes verticale : bord droit ou gauche de l'écran
        public bool HideTaskbar { get; set; } = false;             // barre des tâches Windows en masquage automatique
        public bool RestoreTaskbarOnExit { get; set; } = true;     // la rétablir quand l'application se ferme
        public bool? TaskbarOriginalAutoHide { get; set; }         // état d'avant, pour le rétablir même après un arrêt brutal
        public bool WindowsThisScreen { get; set; } = false;   // l'onglet ne liste que les fenêtres de cet écran
        public bool QuietManual { get; set; } = false;        // ne pas déranger : plus de rappels (eau, tâches, alertes de prix)
        public bool QuietWithFocus { get; set; } = true;      // automatique pendant un Pomodoro « Focus »
        public List<PriceAlert> PriceAlerts { get; set; } = new();
        public bool ShowDiscord { get; set; } = true;          // l'onglet n'apparaît que lorsque l'application Discord est ouverte
        public string DiscordClientId { get; set; } = "";
        public bool ShowChat { get; set; } = true;
        public string ChatProvider { get; set; } = "claude";
        public Dictionary<string, string> ChatModels { get; set; } = new();   // modèle choisi par fournisseur (vide = modèle par défaut)
        public bool ChatSave { get; set; } = true;             // conserver les conversations (chiffrées) entre deux lancements
        public string ChatCustomUrl { get; set; } = "";        // ex. http://localhost:11434/v1 (Ollama)
        public bool PrivacyDots { get; set; } = true;          // pastilles micro / caméra / capture d'écran
        public bool HotkeyEnabled { get; set; } = true;
        public string HotkeyChoice { get; set; } = "auto";     // auto = première combinaison libre parmi Ctrl+Alt+Espace, Ctrl+Alt+N…
        public bool PillSystem { get; set; } = false;        // « CPU 23 % · RAM 61 % » dans la pastille fermée
        public bool SysTopProcesses { get; set; } = true;   // liste des processus les plus gourmands
        public int ClipLimit { get; set; } = 10;             // 1 à 10 copies conservées
        public bool ClipPersist { get; set; } = false;       // garder l'historique (texte et fichiers) après un redémarrage
        public bool ClipSkipSensitive { get; set; } = true;  // ignorer ce que les gestionnaires de mots de passe signalent
        public bool SearchApps { get; set; } = true;
        public bool SearchFiles { get; set; } = true;
        public string SearchEngine { get; set; } = "google";
        public int RecentCount { get; set; } = 8;
        public string ScreenName { get; set; } = "";        // ancien réglage (un seul écran), migré vers Screens
        public List<string> Screens { get; set; } = new();  // écrans où la pastille apparaît (vide = écran principal)
        public bool PillSwap { get; set; } = false;         // la mascotte remplace l'heure quand le PC est inactif
        public double GlassOpacity { get; set; } = 0.6;   // densité du thème « verre » : 0 très transparent, 1 presque opaque
        public double ShadeDark { get; set; } = 0;          // nuance du noir : 0 = noir pur, 1 = gris foncé
        public double ShadeLight { get; set; } = 0.25;         // nuance du blanc : 0 = blanc, 1 = gris clair
        public string ClockStyle { get; set; } = "digital"; // digital, mono, analog, ring, words
        public bool HideWindowsMedia { get; set; } = true;  // les touches média sont traitées par l'encoche
        public bool PillBattery { get; set; } = false;
        public bool HeaderBattery { get; set; } = true;
        public string TimeFormat { get; set; } = "24";      // 24, 24s (avec secondes), 12, 12s
        public string DateStyle { get; set; } = "short";    // long, short, numeric, none
        public bool LyricsLine { get; set; } = true;        // ligne de paroles sous le titre
        public bool LyricsOpenDefault { get; set; } = false;
        public int PomoFocusMin { get; set; } = 25;
        public int PomoBreakMin { get; set; } = 5;
        public int PresetA { get; set; } = 5;
        public int PresetB { get; set; } = 15;
        public bool TimerSound { get; set; } = true;
        public bool TaskSound { get; set; } = true;
        public bool HideDoneTasks { get; set; } = false;
        public bool ShelfAutoOpen { get; set; } = true;
        public int VolumeStepPct { get; set; } = 2;
        public int WaterMinutes { get; set; } = 60; // 0 = désactivé
        public int SizePercent { get; set; } = 100;     // échelle générale (polices comprises)
        public int WidthPercent { get; set; } = 100;    // largeur de la pastille seule
        public int HeightPercent { get; set; } = 100;   // hauteur de la pastille seule
        public int RetractMs { get; set; } = 600;       // délai avant fermeture quand le curseur est parti
        public string Theme { get; set; } = "dark";     // dark, light ou system
        public string Accent { get; set; } = "#FFFFFF";
        public string Language { get; set; } = "auto";          // langue de l'interface : « auto » (celle de Windows), « fr » ou « en »
        public bool MascotReactions { get; set; } = true;  // la pastèque réagit aux événements (minuteur, notification, eau)
        public bool ShowMascot { get; set; } = true;      // la pastèque dans la pastille fermée
        public bool PillClock { get; set; } = false;       // contenus optionnels de la pastille fermée
        public bool PillMusic { get; set; } = false;
        public bool PillMusicTitle { get; set; } = true;   // titre à côté des barres ; sinon seulement les barres animées
        public string PreferredMedia { get; set; } = "";     // source choisie en premier quand plusieurs lisent (vide = automatique)
        public bool PillMarquee { get; set; } = true;      // le titre du média défile s'il est trop long pour la pastille
        public bool PillTimer { get; set; } = true;
        public bool PillWater { get; set; } = false;
        public bool PillTasks { get; set; } = false;
        public bool MinimalNotch { get; set; } = false;    // pastille réduite à un trait
        public bool HideWindowsOsd { get; set; } = true;   // les touches volume sont traitées par l'encoche
        public bool ShowMedia { get; set; } = true;
        public List<string> DisabledMedia { get; set; } = new();   // sources ignorées : vlc, wmp, apple, spotify, chrome, edge, firefox, youtube, other
        public bool ShowOverlays { get; set; } = true;
        public bool ShowTimer { get; set; } = true;
        public bool ShowShelf { get; set; } = true;
        public bool ShowTasks { get; set; } = true;
    }

    /// <summary>Persistance JSON locale dans %AppData%\Navbar.</summary>
    public static class Storage
    {
        /// <summary>Dossier des données de l'utilisateur ; en DEBUG, NAVBAR_TEST_DATADIR le remplace (les tests ne touchent jamais aux vraies données).</summary>
        internal static string DataDir =>
#if DEBUG
            Environment.GetEnvironmentVariable("NAVBAR_TEST_DATADIR") is { Length: > 0 } testDir ? testDir :
#endif
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Navbar");

        private static string Dir => DataDir;

        public static T Load<T>(string name, T fallback)
        {
            try
            {
                string path = Path.Combine(Dir, name + ".json");
                if (!File.Exists(path)) return fallback;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? fallback;
            }
            catch { return fallback; }
        }

        public static void Save<T>(string name, T value)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, name + ".json"), JsonSerializer.Serialize(value));
            }
            catch { /* disque en lecture seule : on ignore, l'état reste en mémoire */ }
        }
    }
}



























