// Mod and ModHooks come from the Modding API, patched into the 1.5.78 copy's Assembly-CSharp.dll.
using Modding;

// Groups all of the mod's classes; matches RootNamespace in ChampionsShell.csproj.
namespace ChampionsShell
{
    // Entry point. The Modding API finds every class that inherits Mod in DLLs under Managed/Mods/ and creates it.
    public class ChampionsShell : Mod
    {
        // Passes the display name to Modding.Mod; it shows in the game's mod list and prefixes this mod's log lines.
        public ChampionsShell() : base("Champion's Shell") { }

        // Called by the Modding API for the mod menu and log; returns <Version> from ChampionsShell.csproj (built into the DLL).
        public override string GetVersion() => typeof(ChampionsShell).Assembly.GetName().Version.ToString();

        // Called once by the Modding API after the game starts; hooks for the charm, health bar and Nailsmith go here.
        public override void Initialize()
        {
            // Writes to ModLog.txt in the game's save folder (Modding.Mod.Log), showing the mod loaded.
            Log("Initialized");
        }
    }
}
