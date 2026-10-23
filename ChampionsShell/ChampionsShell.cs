// Mod, ModHooks and ILocalSettings come from the Modding API, patched into the 1.5.78 copy's Assembly-CSharp.dll.
using Modding;
// EasyCharmState comes from SFCore (Mods/SFCore/SFCore.dll in the game copy).
using SFCore;

// Groups all of the mod's classes; matches RootNamespace in ChampionsShell.csproj.
namespace ChampionsShell
{
    // Per-save data. The Modding API stores it next to each save file (ILocalSettings) as JSON.
    public class LocalSettings
    {
        // Owned / equipped / new flags for Champion's Shell (SFCore's EasyCharmState); null in saves made before the charm existed.
        public EasyCharmState? Charm;
    }

    // Entry point. The Modding API finds every class that inherits Mod in DLLs under Managed/Mods/ and creates it.
    // ILocalSettings<LocalSettings> makes the Modding API call OnLoadLocal/OnSaveLocal when a save loads or saves.
    public class ChampionsShell : Mod, ILocalSettings<LocalSettings>
    {
        // The charm (ChampionsShellCharm.cs); null until Initialize() runs.
        private ChampionsShellCharm? charm;

        // Latest per-save data. The Modding API calls OnLoadLocal on scene changes even before Initialize(), so it's kept here.
        private LocalSettings settings = new LocalSettings();

        // Finds and tracks every enemy for the health bars (EnemyTracker.cs); created in Initialize().
        private EnemyTracker tracker = null!;

        // Passes the display name to Modding.Mod; it shows in the game's mod list and prefixes this mod's log lines.
        public ChampionsShell() : base("Champion's Shell") { }

        // Called by the Modding API for the mod menu and log; returns <Version> from ChampionsShell.csproj (built into the DLL).
        public override string GetVersion() => typeof(ChampionsShell).Assembly.GetName().Version.ToString();

        // Called once by the Modding API after the game starts (main thread, after SFCore has loaded).
        public override void Initialize()
        {
            // Creates the charm; EasyCharm's constructor registers its icon with SFCore's CharmHelper and adds its ModHooks.
            ChampionsShellCharm newCharm = new ChampionsShellCharm();
            // Stores it for the hooks and the save callbacks below.
            charm = newCharm;
            // Applies any settings the Modding API handed over before the charm existed.
            newCharm.RestoreCharmState(settings.Charm ?? new EasyCharmState());
            // Creates the enemy tracker.
            tracker = new EnemyTracker();
            // Starts watching enemies as they spawn (On.HealthManager.Start).
            tracker.Hook();
            // Builds the on-screen health bars, shown above enemies while the charm is equipped.
            EnemyHealthBars.Create(tracker, newCharm);
            // TEMPORARY until the world pickup exists: gives the charm when a save is loaded (ModHooks.AfterSavegameLoadHook).
            ModHooks.AfterSavegameLoadHook += _ => GiveCharmForTesting();
            // TEMPORARY until the world pickup exists: resets the charm for a new game, then gives it (ModHooks.NewGameHook).
            ModHooks.NewGameHook += () => { charm?.RestoreCharmState(new EasyCharmState()); GiveCharmForTesting(); };
            // Writes to ModLog.txt in the copy's save folder (Modding.Loggable.Log), showing the mod loaded.
            Log($"Initialized, charm id {newCharm.Id}");
        }

        // TEMPORARY: gives Champion's Shell if the current save doesn't have it yet. Remove with the world-pickup step.
        private void GiveCharmForTesting()
        {
            // Nothing to give before Initialize() has created the charm.
            if (charm == null) return;
            // Unlocks the inventory's Charms tab, which the game hides until PlayerData.hasCharm (first charm found) is true.
            PlayerData.instance.SetBool("hasCharm", true);
            // Already owned (PlayerData.GetBool reaches EasyCharm's gotCharm_<id> answer): nothing to do.
            if (PlayerData.instance.GetBool($"gotCharm_{charm.Id}")) return;
            // Sets gotCharm_<id> and marks it new so the inventory shows the "new" dot (EasyCharm.GiveCharm).
            charm.GiveCharm(true);
            // Notes it in ModLog.txt.
            Log("Gave Champion's Shell (temporary test giver)");
        }

        // Called by the Modding API when a save loads, and with blank settings on scene changes (including before
        // Initialize()); stores them and restores the charm's flags if the charm exists yet.
        public void OnLoadLocal(LocalSettings loaded)
        {
            // Keeps the settings so Initialize() and OnSaveLocal can use them.
            settings = loaded;
            // Restores owned/equipped/new flags, or blank ones for saves without them; skipped if the charm isn't created yet.
            charm?.RestoreCharmState(loaded.Charm ?? new EasyCharmState());
        }

        // Called by the Modding API when the game saves; returns the charm's current flags to store with the save.
        public LocalSettings OnSaveLocal()
        {
            // Copies the charm's current flags into the settings (EasyCharm.GetCharmState), if the charm exists.
            if (charm != null) settings.Charm = charm.GetCharmState();
            // Hands the settings to the Modding API to write next to the save.
            return settings;
        }
    }
}
