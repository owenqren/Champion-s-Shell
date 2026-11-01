// Mod, ModHooks and ILocalSettings come from the Modding API, patched into the 1.5.78 copy's Assembly-CSharp.dll.
// Without the Modding API patch these types don't exist, which is why the build needs the copy (property HollowKnightCopy).
using Modding;
// EasyCharmState comes from SFCore (Mods/SFCore/SFCore.dll in the game copy). SFCore must load before this mod's
// Initialize() runs; the Modding API orders that by the mod's dependencies, and the DLL must be present in Managed/Mods/SFCore/.
using SFCore;

// Groups all of the mod's classes; matches RootNamespace in ChampionsShell.csproj, which also decides the prefix of
// embedded resource names (see IconResource in ChampionsShellCharm.cs).
namespace ChampionsShell
{
    // Per-save data. The Modding API stores it next to each save file (ILocalSettings) as JSON, so each of the game's
    // four save slots has its own copy. Add new fields here (not to PlayerData) when later steps need to save things,
    // e.g. the upgrade tier from the Nailsmith step.
    public class LocalSettings
    {
        // Owned / equipped / new flags for Champion's Shell (SFCore's EasyCharmState). Nullable because JSON from a save
        // made before the charm existed has no such field; callers must handle null (see OnLoadLocal).
        public EasyCharmState? Charm;

        // Charm upgrade tier (ChampionsShellCharm.Tier); 0, the base charm, for saves made before upgrades existed.
        public int UpgradeTier;
    }

    // Entry point. The Modding API finds every class that inherits Mod in DLLs under Managed/Mods/ and creates it
    // (there must be exactly one per DLL, with a parameterless constructor).
    // ILocalSettings<LocalSettings> makes the Modding API call OnLoadLocal/OnSaveLocal when a save loads or saves.
    // Partial so the gitignored TestCheats.cs can add to this class on a developer's machine.
    public partial class ChampionsShell : Mod, ILocalSettings<LocalSettings>
    {
        // The charm (ChampionsShellCharm.cs); null until Initialize() runs, so every use before then must null-check.
        private ChampionsShellCharm? charm;

        // Latest per-save data. The Modding API calls OnLoadLocal on scene changes even before Initialize(), so it's kept
        // here and applied once the charm exists. Starts blank, which is also what a brand-new game uses.
        private LocalSettings settings = new LocalSettings();

        // Finds and tracks every enemy for the health bars (EnemyTracker.cs); created in Initialize(), hence `null!`
        // (the compiler can't see that Initialize() always runs before anything reads this).
        private EnemyTracker tracker = null!;

        // Passes the display name to Modding.Mod; it shows in the game's mod list and prefixes this mod's log lines
        // in ModLog.txt (in the copy's save folder, unity.Team Cherry.Hollow Knight Modded).
        public ChampionsShell() : base("Champion's Shell") { }

        // Called by the Modding API for the mod menu and log; returns <Version> from ChampionsShell.csproj (built into the DLL).
        // This is the "Champion's Shell : 0.1.0.0" text in the top-left corner of the main menu.
        public override string GetVersion() => typeof(ChampionsShell).Assembly.GetName().Version.ToString();

        // Called once by the Modding API after the game starts (main thread, after SFCore has loaded). All hooks and
        // GameObjects are set up here; nothing runs before this, so hooks added here miss events from earlier loading.
        public override void Initialize()
        {
            // Creates the charm; EasyCharm's constructor registers its icon with SFCore's CharmHelper (which assigns the
            // charm id past vanilla's 40) and adds its ModHooks for the gotCharm_N / equippedCharm_N / charmCost_N lookups.
            ChampionsShellCharm newCharm = new ChampionsShellCharm();
            // Stores it for the hooks and the save callbacks below; assigned only after construction succeeded.
            charm = newCharm;
            // Applies any settings the Modding API handed over before the charm existed (OnLoadLocal runs early and
            // skips the charm when it's null); a blank state if the save has none.
            newCharm.RestoreCharmState(settings.Charm ?? new EasyCharmState());
            // Applies the saved upgrade tier the same way.
            newCharm.Tier = settings.UpgradeTier;
            // Creates the enemy tracker (just the empty list; nothing is hooked until Hook()).
            tracker = new EnemyTracker();
            // Starts watching enemies as they spawn (On.HealthManager.Start). Enemies that already exist when this runs
            // are not seen, which is fine because Initialize() happens at game start, before any gameplay scene.
            tracker.Hook();
            // Builds the on-screen health bars: one persistent GameObject with an overlay canvas that draws a bar for
            // each tracked enemy, only while the charm is equipped (it needs newCharm to read the equipped flag).
            EnemyHealthBars.Create(tracker, newCharm);
            // Builds the hidden popup shown when the charm is collected, using the charm's icon (EasyCharm.GetSprite).
            PickupPopup popup = PickupPopup.Create(newCharm.GetSprite());
            // Starts watching for the room that holds the charm, so saves without it find it in the world.
            new WorldPickup(newCharm, popup).Hook();
            // Clears the charm for a new game, so a previously loaded save's flags don't carry over (ModHooks.NewGameHook).
            ModHooks.NewGameHook += () => ResetCharm();
            // Starts the Finishing Blow ability, which needs the tracker for enemies' max HP (FinishingBlow.cs).
            new FinishingBlow(newCharm, tracker).Hook();
            // Starts the charm upgrade at the Nailsmith (NailsmithUpgrade.cs).
            new NailsmithUpgrade(newCharm).Hook();
            // Sets up local test cheats if TestCheats.cs exists; otherwise the compiler removes this call.
            SetUpTestCheats();
            // Writes to ModLog.txt in the copy's save folder (Modding.Loggable.Log); seeing this line proves the mod loaded
            // and shows which charm id SFCore assigned.
            Log($"Initialized, charm id {newCharm.Id}");
        }

        // Implemented only in the gitignored TestCheats.cs. With that file missing, this and its call compile to nothing.
        partial void SetUpTestCheats();

        // Clears the charm's flags and tier for a new game.
        private void ResetCharm()
        {
            // Nothing to reset before Initialize() has created the charm.
            if (charm == null) return;
            // Blank owned/equipped/new flags.
            charm.RestoreCharmState(new EasyCharmState());
            // Back to the base charm.
            charm.Tier = 0;
        }

        // Called by the Modding API when a save loads, and with blank settings on scene changes (including before
        // Initialize()); stores them and restores the charm's flags if the charm exists yet.
        public void OnLoadLocal(LocalSettings loaded)
        {
            // Keeps the settings so Initialize() and OnSaveLocal can use them.
            settings = loaded;
            // Restores owned/equipped/new flags, or blank ones for saves without them; skipped if the charm isn't created yet
            // (Initialize() applies `settings` itself in that case).
            charm?.RestoreCharmState(loaded.Charm ?? new EasyCharmState());
            // Restores the upgrade tier the same way, if the charm exists yet.
            if (charm != null) charm.Tier = loaded.UpgradeTier;
        }

        // Called by the Modding API when the game saves; returns the charm's current flags to store with the save.
        public LocalSettings OnSaveLocal()
        {
            // Copies the charm's current flags into the settings (EasyCharm.GetCharmState), if the charm exists. The flags
            // live inside EasyCharm at runtime, so they must be copied out here or equipping would not survive a reload.
            if (charm != null) settings.Charm = charm.GetCharmState();
            // Copies the upgrade tier too, if the charm exists.
            if (charm != null) settings.UpgradeTier = charm.Tier;
            // Hands the settings to the Modding API to write next to the save.
            return settings;
        }
    }
}
