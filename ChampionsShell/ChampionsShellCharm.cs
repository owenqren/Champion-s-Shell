// Stream, used to read the embedded icon PNG out of ChampionsShell.dll.
using System.IO;
// Assembly, used to find resources embedded in this DLL.
using System.Reflection;
// EasyCharm comes from SFCore (Mods/SFCore/SFCore.dll in the game copy).
using SFCore;
// Sprite, Texture2D, Rect, Vector2 come from Unity (UnityEngine.CoreModule; LoadImage from ImageConversionModule).
using UnityEngine;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // The Champion's Shell charm. EasyCharm (SFCore) registers it with CharmHelper and answers the game's
    // gotCharm_N / equippedCharm_N / newCharm_N / charmCost_N / CHARM_NAME_N / CHARM_DESC_N lookups through ModHooks.
    public class ChampionsShellCharm : EasyCharm
    {
        // Name of the icon inside the DLL; must match LogicalName on the EmbeddedResource in ChampionsShell.csproj.
        private const string IconResource = "ChampionsShell.Resources.ChampionsShell.png";

        // Icon pixels per Unity world unit; raise it to draw the icon smaller in the charm menu, lower it to draw bigger.
        private const float IconPixelsPerUnit = 100f;

        // Name shown in the inventory; the game asks for it through the CHARM_NAME_<id> language key (EasyCharm hooks it).
        protected override string GetName() => "Champion's Shell";

        // Description shown in the inventory, asked for through CHARM_DESC_<id>; the upgrade's ability line is added later.
        protected override string GetDescription() =>
            "Shell worn by a champion of a kingdom long fallen.\n\nReveals the remaining strength of foes.";

        // Notch cost, asked for through charmCost_<id>; Owen's decision: always 1 notch.
        protected override int GetCharmCost() => 1;

        // Builds the icon sprite; EasyCharm calls this once (via GetSprite) when the charm is created in Initialize().
        protected override Sprite GetSpriteInternal()
        {
            // Opens the embedded PNG from this DLL (depends on IconResource matching the csproj LogicalName).
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResource);
            // Buffer the size of the PNG file.
            byte[] bytes = new byte[stream.Length];
            // Copies the whole PNG into the buffer.
            stream.Read(bytes, 0, bytes.Length);
            // Placeholder texture; LoadImage below resizes it to the PNG's real size.
            Texture2D texture = new Texture2D(2, 2);
            // Decodes the PNG into the texture (Unity ImageConversionModule).
            texture.LoadImage(bytes);
            // Wraps the whole texture in a sprite centred on its middle, at IconPixelsPerUnit.
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), IconPixelsPerUnit);
        }
    }
}
