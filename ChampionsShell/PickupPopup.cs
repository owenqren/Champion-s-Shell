// MonoBehaviour, GameObject, Canvas, CanvasGroup, RectTransform, Sprite, Vector2, Color, Mathf, Time, Resources, Font,
// TextAnchor come from Unity (CoreModule, UIModule, TextRenderingModule).
using UnityEngine;
// Image, Text and CanvasScaler come from Unity UI (UnityEngine.UI.dll in the copy).
using UnityEngine.UI;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // Short on-screen message shown when the charm is collected: its icon and name, then a fade out. Lives on its own
    // GameObject that survives scene loads, so it finishes even if the player changes rooms; created in
    // ChampionsShell.Initialize() and shown by WorldPickup.
    public class PickupPopup : MonoBehaviour
    {
        // Seconds the popup stays on screen, including the fade.
        private const float ShowTime = 3f;

        // Seconds of ShowTime spent fading out at the end.
        private const float FadeTime = 0.6f;

        // Height of the popup above the bottom of the screen, as a fraction of screen height.
        private const float HeightOnScreen = 0.28f;

        // Icon size in canvas units (the canvas is scaled to behave like 1080p, see Create).
        private const float IconSize = 96f;

        // Text size in canvas units.
        private const int FontSize = 40;

        // Text shown next to the icon; matches ChampionsShellCharm.GetName().
        private const string Message = "Champion's Shell";

        // Fades the icon and text together (Unity CanvasGroup.alpha); 0 means the popup is invisible.
        private CanvasGroup group = null!;

        // Seconds left on screen; 0 or less means the popup isn't showing.
        private float timeLeft;

        // Builds the hidden popup; called once from ChampionsShell.Initialize() with the charm's sprite.
        public static PickupPopup Create(Sprite icon)
        {
            // New empty object for the popup.
            GameObject root = new GameObject("ChampionsShell Pickup Popup");
            // Keeps it alive across scene loads (Unity Object.DontDestroyOnLoad).
            DontDestroyOnLoad(root);
            // Adds the canvas that draws straight onto the screen above the game.
            Canvas canvas = root.AddComponent<Canvas>();
            // Screen-space overlay: nothing in the scene can cover it.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Draws above the health bars (EnemyHealthBars uses sorting order 100).
            canvas.sortingOrder = 101;
            // Adds the scaler so the sizes above look the same at any resolution.
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            // Scales the canvas with the screen.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Sizes above are in units of a 1080p screen.
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Follows the screen's height, so wide screens don't change the popup's size.
            scaler.matchWidthOrHeight = 1f;
            // Adds the behaviour so Update() runs every frame.
            PickupPopup popup = root.AddComponent<PickupPopup>();
            // Adds the group that fades everything on the canvas.
            popup.group = root.AddComponent<CanvasGroup>();
            // Starts invisible.
            popup.group.alpha = 0f;
            // The popup must never catch clicks.
            popup.group.blocksRaycasts = false;
            // Icon object on the canvas.
            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            // Puts it on the canvas.
            iconObject.transform.SetParent(root.transform, false);
            // The icon image.
            Image image = iconObject.GetComponent<Image>();
            // Shows the charm's sprite (EasyCharm.GetSprite, passed in by the caller).
            image.sprite = icon;
            // Keeps the sprite's shape if the art is replaced with a non-square image.
            image.preserveAspect = true;
            // Places the icon left of centre, HeightOnScreen up the screen.
            Place(iconObject.GetComponent<RectTransform>(), new Vector2(-170f, 0f), new Vector2(IconSize, IconSize));
            // Text object on the canvas.
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            // Puts it on the canvas.
            textObject.transform.SetParent(root.transform, false);
            // The text component.
            Text text = textObject.GetComponent<Text>();
            // Unity's built-in font; a placeholder until the game's own font is used.
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            // The charm's name.
            text.text = Message;
            // Text size.
            text.fontSize = FontSize;
            // Pale shell white, matching the health bars (EnemyHealthBars.FillColor).
            text.color = new Color(0.93f, 0.9f, 0.83f, 1f);
            // Starts the text at the left edge of its box, right beside the icon.
            text.alignment = TextAnchor.MiddleLeft;
            // Places the text box right of the icon.
            Place(textObject.GetComponent<RectTransform>(), new Vector2(70f, 0f), new Vector2(380f, IconSize));
            // Hands back the popup for WorldPickup.
            return popup;
        }

        // Anchors a rectangle at the popup's spot on screen, offset by `offset`, with the given size.
        private static void Place(RectTransform rect, Vector2 offset, Vector2 size)
        {
            // Anchor point: horizontal centre, HeightOnScreen up the screen.
            rect.anchorMin = new Vector2(0.5f, HeightOnScreen);
            // Same point, so the rectangle keeps a fixed size.
            rect.anchorMax = new Vector2(0.5f, HeightOnScreen);
            // Position relative to the anchor.
            rect.anchoredPosition = offset;
            // Fixed size in canvas units.
            rect.sizeDelta = size;
        }

        // Shows the popup from the start; called by WorldPickup when the charm is collected.
        public void Show() => timeLeft = ShowTime;

        // Runs every frame (Unity MonoBehaviour.Update): counts down and fades the popup out.
        private void Update()
        {
            // Nothing to do while hidden.
            if (timeLeft <= 0f) return;
            // Counts down in real time, so slow-motion hits and pauses don't hold the popup on screen.
            timeLeft -= Time.unscaledDeltaTime;
            // Fully visible until the last FadeTime seconds, then fades to 0.
            group.alpha = Mathf.Clamp01(timeLeft / FadeTime);
        }
    }
}
