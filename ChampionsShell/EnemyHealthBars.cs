// List, Dictionary, HashSet, used for the bar pool and the per-enemy bar lookup.
using System.Collections.Generic;
// MonoBehaviour, GameObject, Canvas, RectTransform, Camera, Vector2/3, Color, Mathf, Time, Screen come from Unity.
using UnityEngine;
// Image comes from Unity UI (UnityEngine.UI.dll in the copy; the csproj must reference it).
using UnityEngine.UI;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // Draws a health bar above every enemy while Champion's Shell is equipped, plus a docked boss bar at the top of the
    // screen when a boss being fought is out of view. Lives on its own GameObject that survives scene loads; created in
    // ChampionsShell.Initialize(). All positions are screen pixels because the canvas is a screen-space overlay.
    public class EnemyHealthBars : MonoBehaviour
    {
        // Normal enemy bar size in pixels at 1080p; scaled to the real screen height (see `scale` in Update).
        private static readonly Vector2 EnemyBarSize = new Vector2(90f, 8f);

        // Boss bar size above the boss, in pixels at 1080p.
        private static readonly Vector2 BossBarSize = new Vector2(220f, 14f);

        // Docked boss bar size at the top of the screen, in pixels at 1080p.
        private static readonly Vector2 DockedBarSize = new Vector2(640f, 14f);

        // Gap between the top of an enemy and its bar, in pixels at 1080p. Raise it if bars overlap sprites.
        private const float GapAbove = 16f;

        // Distance of the docked bar from the top edge of the screen, in pixels at 1080p. Keeps the bar below the HUD's
        // masks and soul vessel at top left and clear of the screen edge.
        private const float DockedFromTop = 70f;

        // Seconds a bar takes to mostly reach a new position, so moves between above-boss and docked never snap.
        // Smaller is snappier; the slide uses an exponential ease (see Place), so this is a time constant, not a duration.
        private const float SlideTime = 0.07f;

        // Border around the fill, in pixels at 1080p.
        private const float Border = 2f;

        // Background colour: dark, slightly see-through (alpha 0.8) so the empty part of the bar reads as "missing HP".
        private static readonly Color BackColor = new Color(0.05f, 0.05f, 0.07f, 0.8f);

        // Fill colour: pale shell white.
        private static readonly Color FillColor = new Color(0.93f, 0.9f, 0.83f, 1f);

        // Source of the tracked enemies (EnemyTracker.Enemies); set by Create().
        private EnemyTracker tracker = null!;

        // The charm, used to check equippedCharm_<id> (EasyCharm.Id from SFCore); set by Create().
        private ChampionsShellCharm charm = null!;

        // The overlay canvas every bar is drawn on; new bars are parented to it.
        private Canvas canvas = null!;

        // Bars currently in use, one per enemy on screen. An enemy absent from here has no bar.
        private readonly Dictionary<TrackedEnemy, BarWidget> enemyBars = new Dictionary<TrackedEnemy, BarWidget>();

        // Hidden bars ready for reuse, so enemies spawning and dying don't create and destroy UI objects constantly.
        private readonly Stack<BarWidget> spareBars = new Stack<BarWidget>();

        // The single docked boss bar at the top of the screen; created in Create(). It is not part of enemyBars or spareBars.
        private BarWidget dockedBar = null!;

        // Enemies that got a bar this frame, so bars of the rest can be put away. Cleared at the start of every Update.
        private readonly HashSet<TrackedEnemy> shownThisFrame = new HashSet<TrackedEnemy>();

        // Removal list reused every frame (a dictionary can't be changed while looping over it); reused to avoid
        // allocating a new list 60 times a second.
        private readonly List<TrackedEnemy> toRemove = new List<TrackedEnemy>();

        // One bar on screen: an outer background image with a fill image inside it.
        private class BarWidget
        {
            // Background rectangle; its position is the bar's centre (Image pivot is the middle by default).
            public RectTransform Back = null!;
            // Fill rectangle (child of Back); its right edge (anchorMax.x) is the HP fraction.
            public RectTransform Fill = null!;
            // Whether the bar was hidden last frame, so a newly shown bar starts at a chosen point instead of sliding in from wherever it was.
            public bool WasHidden = true;
        }

        // Builds the bars' GameObject and canvas; called once from ChampionsShell.Initialize().
        public static EnemyHealthBars Create(EnemyTracker tracker, ChampionsShellCharm charm)
        {
            // New empty object for the bar system.
            GameObject root = new GameObject("ChampionsShell Health Bars");
            // Keeps it alive across scene loads (Unity Object.DontDestroyOnLoad); otherwise the bars would vanish on the
            // first scene change and Update() would stop.
            DontDestroyOnLoad(root);
            // Adds the canvas that draws straight onto the screen above the game.
            Canvas canvas = root.AddComponent<Canvas>();
            // Screen-space overlay: positions are screen pixels and nothing in the scene can cover it (no camera needed).
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Draws above most of the game's own UI canvases (higher sortingOrder draws later). If the bars hide behind
            // something such as a fade or the pause menu, raise this.
            canvas.sortingOrder = 100;
            // Adds this behaviour so Update() runs every frame.
            EnemyHealthBars bars = root.AddComponent<EnemyHealthBars>();
            // Stores the enemy source.
            bars.tracker = tracker;
            // Stores the charm.
            bars.charm = charm;
            // Stores the canvas for new bars.
            bars.canvas = canvas;
            // Builds the docked boss bar up front (hidden until a boss needs it).
            bars.dockedBar = bars.NewWidget();
            // Hands back the component (the object lives on its own after this).
            return bars;
        }

        // Runs every frame (Unity MonoBehaviour.Update): refreshes enemies, then shows, hides and places the bars.
        private void Update()
        {
            // Updates HP and drops dead or destroyed enemies (EnemyTracker.Refresh). Runs even when the charm is off
            // so the tracker's list doesn't fill with stale enemies.
            tracker.Refresh();
            // Starts this frame's list of enemies with bars from scratch.
            shownThisFrame.Clear();
            // Only draws while the charm is equipped (PlayerData.equippedCharm_<id>, answered by EasyCharm), the game isn't
            // paused (GameManager.IsGamePaused), and the main camera exists (GameCameras.mainCamera). The null checks matter
            // on the main menu and during scene loads, where these singletons don't exist yet.
            bool active = CharmEquipped() && GameManager.instance != null && !GameManager.instance.IsGamePaused()
                && GameCameras.instance != null && GameCameras.instance.mainCamera != null;
            // Size multiplier so bars look the same at any resolution (sizes above are for 1080p).
            float scale = Screen.height / 1080f;
            // Sum of current HP over bosses being fought, for the docked bar (multi-part bosses share one bar).
            int bossHp = 0;
            // Sum of max HP over bosses being fought, for the docked bar.
            int bossMax = 0;
            // True when a boss being fought is out of view, so the docked bar shows.
            bool dock = false;
            // Last on-screen bar position of a boss that just went out of view, so the docked bar can slide up from it.
            Vector2? dockFrom = null;
            // Places the bars; when not active nothing is placed, so the clean-up below hides every bar.
            if (active)
            {
                // The game's main camera (GameCameras.mainCamera), used to turn world positions into screen positions;
                // `active` already checked it isn't null.
                Camera cam = GameCameras.instance!.mainCamera!;
                // Goes through every tracked enemy.
                foreach (TrackedEnemy enemy in tracker.Enemies)
                {
                    // Skips enemies with no HP left or whose object is inactive.
                    if (!EnemyTracker.IsAlive(enemy)) continue;
                    // Bar size for this enemy: bigger for bosses.
                    Vector2 size = (enemy.IsBoss ? BossBarSize : EnemyBarSize) * scale;
                    // Screen point for the bar's centre above the enemy, or null if the enemy is out of view.
                    Vector2? above = AboveOnScreen(enemy, cam, size.y, scale);
                    // Bosses being fought add to the docked bar's totals, whether or not they're on screen.
                    if (enemy.IsBoss && enemy.InFight) { bossHp += enemy.Health.hp; bossMax += enemy.MaxHp; }
                    // Out of view: no bar above it.
                    if (above == null)
                    {
                        // A boss being fought out of view turns on the docked bar.
                        if (enemy.IsBoss && enemy.InFight)
                        {
                            // Shows the docked bar this frame.
                            dock = true;
                            // If the boss's own bar was showing, the docked bar starts from there and slides up
                            // (Back.position is in screen pixels on an overlay canvas).
                            if (enemyBars.TryGetValue(enemy, out BarWidget old) && !old.WasHidden) dockFrom = old.Back.position;
                        }
                        // Nothing to draw above this enemy; its old bar (if any) is put away by the clean-up below.
                        continue;
                    }
                    // Gets this enemy's bar, or a spare one.
                    BarWidget bar = BarFor(enemy);
                    // A boss coming back into view starts its bar from the docked bar's spot and slides down;
                    // everything else starts exactly where it will be drawn.
                    Vector2 from = enemy.IsBoss && !dockedBar.WasHidden ? (Vector2)dockedBar.Back.position : above.Value;
                    // Draws the bar above the enemy with its HP fraction.
                    Place(bar, above.Value, size, Fraction(enemy.Health.hp, enemy.MaxHp), from);
                    // Remembers it was shown so it isn't put away below.
                    shownThisFrame.Add(enemy);
                }
            }
            // Puts away bars whose enemy got no bar this frame (died, went off screen, charm unequipped, game paused).
            toRemove.Clear();
            // Finds them.
            foreach (KeyValuePair<TrackedEnemy, BarWidget> pair in enemyBars) if (!shownThisFrame.Contains(pair.Key)) toRemove.Add(pair.Key);
            // Hides each one and returns it to the spares.
            foreach (TrackedEnemy enemy in toRemove) { Hide(enemyBars[enemy]); spareBars.Push(enemyBars[enemy]); enemyBars.Remove(enemy); }
            // Docked bar at the top centre (clear of the masks and soul at top left) when a fought boss is out of view.
            if (dock)
            {
                // Top centre of the screen, DockedFromTop below the edge. Screen y is 0 at the bottom, hence Height - offset.
                Vector2 docked = new Vector2(Screen.width / 2f, Screen.height - DockedFromTop * scale);
                // Draws the combined boss HP, sliding up from the boss's last bar position if there was one.
                Place(dockedBar, docked, DockedBarSize * scale, Fraction(bossHp, bossMax), dockFrom ?? docked);
            }
            // Otherwise hides the docked bar.
            else Hide(dockedBar);
        }

        // Whether Champion's Shell is equipped (PlayerData.GetBool reaches EasyCharm's GetPlayerBoolHook answer, which
        // reads the flags saved through ChampionsShell.OnSaveLocal). PlayerData.instance is null on the main menu.
        private bool CharmEquipped() => PlayerData.instance != null && PlayerData.instance.GetBool($"equippedCharm_{charm.Id}");

        // HP fraction clamped to 0..1 for the fill width; 0 when max is 0 so a bad MaxHp never divides by zero.
        private static float Fraction(int hp, int max) => max <= 0 ? 0f : Mathf.Clamp01((float)hp / max);

        // Screen point for a bar centred above an enemy, or null if the enemy's top is out of view or there's no room
        // for the bar below the top edge.
        private static Vector2? AboveOnScreen(TrackedEnemy enemy, Camera cam, float barHeight, float scale)
        {
            // Uses the renderer's bounds, or the hitbox's if there's no renderer, or the object's position as a last resort
            // (a zero-size box, so the bar sits at the object's origin).
            Bounds bounds = enemy.Renderer != null ? enemy.Renderer.bounds
                : enemy.Collider != null ? enemy.Collider.bounds
                : new Bounds(enemy.Health.transform.position, Vector3.zero);
            // Projects the top centre of those bounds to screen pixels (z is distance in front of the camera).
            Vector3 top = cam.WorldToScreenPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            // Bar centre: the gap plus half the bar height above the top (the bar is positioned by its centre).
            Vector2 centre = new Vector2(top.x, top.y + GapAbove * scale + barHeight / 2f);
            // Out of view if behind the camera, past the left/right edges, below the bottom, or too high for the bar.
            if (top.z < 0 || top.x < 0 || top.x > Screen.width || top.y < 0 || centre.y + barHeight / 2f > Screen.height) return null;
            // The bar's centre on screen.
            return centre;
        }

        // Returns the bar already used for an enemy, or takes a spare (or new) one for it.
        private BarWidget BarFor(TrackedEnemy enemy)
        {
            // Reuses this enemy's bar if it has one.
            if (enemyBars.TryGetValue(enemy, out BarWidget bar)) return bar;
            // Takes a spare bar, or builds one if there are none.
            bar = spareBars.Count > 0 ? spareBars.Pop() : NewWidget();
            // Links it to the enemy so next frame finds the same bar.
            enemyBars[enemy] = bar;
            // The bar to draw.
            return bar;
        }

        // Moves, sizes and fills one bar, sliding it toward its target; a bar that was hidden starts at `from`.
        private static void Place(BarWidget widget, Vector2 target, Vector2 size, float fraction, Vector2 from)
        {
            // Shows the bar if it was hidden.
            widget.Back.gameObject.SetActive(true);
            // Fraction of the way to move this frame; unscaled time so slow-motion hits don't slow the slide.
            // 1 - e^(-dt / SlideTime) is frame-rate independent: the same distance is covered per second at any FPS.
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / SlideTime);
            // Starting point: `from` if the bar just appeared, otherwise where it is now.
            Vector2 start = widget.WasHidden ? from : (Vector2)widget.Back.position;
            // Moves part of the way to the target (screen pixels on an overlay canvas).
            widget.Back.position = Vector2.Lerp(start, target, t);
            // Sets the size; a just-shown bar jumps to it, otherwise it eases so bars resize smoothly
            // (for example a boss bar growing into the docked bar).
            widget.Back.sizeDelta = widget.WasHidden ? size : Vector2.Lerp(widget.Back.sizeDelta, size, t);
            // Fill's right edge at the HP fraction of the background's width (the fill is anchored to the background,
            // so changing anchorMax.x resizes it without touching offsets).
            widget.Fill.anchorMax = new Vector2(fraction, 1f);
            // Remembers the bar is showing.
            widget.WasHidden = false;
        }

        // Hides one bar and marks it so it starts fresh next time it appears.
        private static void Hide(BarWidget widget)
        {
            // Hides the bar's object (the fill child goes with it).
            widget.Back.gameObject.SetActive(false);
            // Marks it hidden, so Place() snaps it to its start point instead of sliding from a stale position.
            widget.WasHidden = true;
        }

        // Builds one bar: a background image with a fill image inset by Border.
        private BarWidget NewWidget()
        {
            // Background object under the canvas; RectTransform is listed explicitly because UI objects need it from creation.
            GameObject back = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            // Puts it on the overlay canvas (false keeps the local layout instead of preserving a world position).
            back.transform.SetParent(canvas.transform, false);
            // Background colour (an Image with no sprite draws a solid rectangle).
            back.GetComponent<Image>().color = BackColor;
            // Fill object inside the background.
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            // Puts the fill inside the background so it moves with it.
            fill.transform.SetParent(back.transform, false);
            // Fill colour.
            fill.GetComponent<Image>().color = FillColor;
            // Fill's rectangle.
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            // Anchored to the background's left edge; Place() moves anchorMax.x to set the HP width.
            fillRect.anchorMin = new Vector2(0f, 0f);
            // Starts full.
            fillRect.anchorMax = new Vector2(1f, 1f);
            // Inset from the left and bottom edges by the border (scaled from 1080p; fixed at build time, so a later
            // resolution change leaves the border at the old size).
            fillRect.offsetMin = new Vector2(Border, Border) * (Screen.height / 1080f);
            // Inset from the right and top edges by the border (negative because offsetMax is measured from the anchor inward).
            fillRect.offsetMax = new Vector2(-Border, -Border) * (Screen.height / 1080f);
            // Starts hidden until Place() shows it.
            back.SetActive(false);
            // The finished bar.
            return new BarWidget { Back = back.GetComponent<RectTransform>(), Fill = fillRect };
        }
    }
}
