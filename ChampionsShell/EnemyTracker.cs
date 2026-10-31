// List and HashSet, used to hold the tracked enemies and the extra boss names.
using System.Collections.Generic;
// ReflectionHelper comes from the Modding API in the copy's Assembly-CSharp.dll; it reads private fields by name.
using Modding;
// Renderer, Collider2D come from Unity (UnityEngine.CoreModule, UnityEngine.Physics2DModule).
using UnityEngine;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // One enemy (or one part of a multi-part boss) that may get a health bar. Plain data; EnemyTracker fills it in and
    // EnemyHealthBars reads it.
    public class TrackedEnemy
    {
        // The game's health component on the enemy (HealthManager.hp is its current HP). Unity's null check also
        // covers destroyed objects, which is how EnemyTracker.Refresh notices an enemy was removed from the scene.
        public HealthManager Health = null!;

        // Highest HP seen: hp right after HealthManager.Start, raised if a phase refills it. The bar's fill is hp / MaxHp,
        // so a boss that heals past its starting HP shows a full bar instead of one wider than the background.
        public int MaxHp;

        // True for bosses: they get a bigger bar that docks at the top of the screen when they're out of view.
        public bool IsBoss;

        // True once the enemy has lost HP. Bosses only dock to the top of the screen after this, so a boss that hasn't
        // been fought yet never gets a bar while it's out of view. Never reset to false (a healed boss is still in a fight).
        public bool InFight;

        // Sprite renderer (tk2d sprites draw through a MeshRenderer); its bounds give the "top of the enemy" for the bar.
        // Null when the HealthManager is on an object with no renderer (the sprite is then on a child object).
        public Renderer? Renderer;

        // Fallback for enemies with no renderer on the same object: the hitbox bounds.
        public Collider2D? Collider;

        // Time.time when the Knight last had a clear line to this enemy; EnemyHealthBars uses it so bars don't flicker
        // at corners. Starts far in the past so an enemy that was never seen has no bar.
        public float LastSeenTime = -100f;
    }

    // Finds enemies as they spawn and keeps their HP up to date for EnemyHealthBars.
    public class EnemyTracker
    {
        // Names (GameObject.name) of bosses the automatic boss checks miss; filled in from playtesting. Names must match
        // exactly what Unity shows for the object (including any "(Clone)" suffix). Empty until Owen has tested bosses.
        private static readonly HashSet<string> ExtraBossNames = new HashSet<string>
        {
        };

        // Enemies currently tracked; EnemyHealthBars reads this every frame. Entries are added by the hook below and
        // removed by Refresh(), so the list never grows across scene changes.
        private readonly List<TrackedEnemy> enemies = new List<TrackedEnemy>();

        // Read-only view of the tracked enemies for EnemyHealthBars (so it can't add or remove entries itself).
        public IReadOnlyList<TrackedEnemy> Enemies => enemies;

        // Starts watching every enemy that spawns; called once from ChampionsShell.Initialize().
        public void Hook()
        {
            // On.HealthManager.Start (MMHOOK_Assembly-CSharp, the MonoMod hooks the Modding API ships) runs our method
            // around every enemy's HealthManager.Start. Hooked at Start, not Awake, because Start is where the game applies
            // Godhome HP scaling.
            On.HealthManager.Start += OnHealthManagerStart;
        }

        // Replacement for HealthManager.Start: runs the original, then records the enemy.
        // `orig` is the game's own Start and `self` is the HealthManager it runs on (the On.* hook signature).
        private void OnHealthManagerStart(On.HealthManager.orig_Start orig, HealthManager self)
        {
            // Runs the game's Start first; it applies Godhome HP scaling (hpScale), so hp afterwards is the real max.
            // Skipping this call would break the enemy completely, so it always runs and always runs first.
            orig(self);
            // Records the enemy with its max HP and where to find its top edge.
            enemies.Add(new TrackedEnemy
            {
                // The enemy's HealthManager; everything else is read from it each frame.
                Health = self,
                // Max HP from just after Start (HealthManager.hp).
                MaxHp = self.hp,
                // Whether it counts as a boss (see IsBoss below).
                IsBoss = IsBoss(self),
                // Renderer on the enemy object, if any (tk2dSprite's MeshRenderer). Looked up once here, not per frame.
                Renderer = self.GetComponent<Renderer>(),
                // Hitbox on the enemy object, used if there's no renderer. Also looked up once.
                Collider = self.GetComponent<Collider2D>(),
            });
        }

        // Decides whether a freshly started HealthManager belongs to a boss. The three checks go from most to least
        // reliable; the last one is the manual list for bosses the first two miss.
        private static bool IsBoss(HealthManager health)
        {
            // In a Godhome boss scene (BossSceneController.IsBossScene), the scene lists its bosses in BossSceneController.bosses.
            // IsBossScene is checked first because Instance is null outside those scenes.
            if (BossSceneController.IsBossScene && System.Array.IndexOf(BossSceneController.Instance.bosses, health) >= 0) return true;
            // In the main game, bosses that unlock a Hall of Gods statue have HealthManager's private unlockBossScene set.
            // It's private, so ReflectionHelper reads it by name; if a game update renames the field this check silently stops matching.
            if (ReflectionHelper.GetField<HealthManager, BossScene>(health, "unlockBossScene") != null) return true;
            // Otherwise only bosses named in ExtraBossNames count.
            return ExtraBossNames.Contains(health.gameObject.name);
        }

        // Updates HP bookkeeping and drops gone enemies; EnemyHealthBars calls this once per frame (at the start of its Update).
        public void Refresh()
        {
            // Walks backwards so entries can be removed while looping without skipping the next one.
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                // The enemy being checked.
                TrackedEnemy enemy = enemies[i];
                // Drops enemies whose object was destroyed (Unity's null check covers destroyed objects, e.g. on scene change)
                // or that died (HealthManager.GetIsDead). Checking destroyed first matters: GetIsDead would throw on a destroyed object.
                if (enemy.Health == null || enemy.Health.GetIsDead())
                {
                    // Removes the gone or dead enemy.
                    enemies.RemoveAt(i);
                    // Next enemy.
                    continue;
                }
                // Current HP (HealthManager.hp); some FSMs change it directly, so it's read rather than hooked
                // (a hook on TakeDamage would miss those changes).
                int hp = enemy.Health.hp;
                // Raises the max if a phase refilled HP above it.
                if (hp > enemy.MaxHp) enemy.MaxHp = hp;
                // Marks the fight as started the first time the enemy is below full HP.
                if (hp < enemy.MaxHp) enemy.InFight = true;
            }
        }

        // Whether an enemy can have a bar at all right now: HP left (hasSpecialDeath enemies can sit at 0 without
        // isDead) and its object active in the scene (the game deactivates enemies instead of destroying them in some
        // fights, and an inactive enemy has no meaningful position).
        public static bool IsAlive(TrackedEnemy enemy) => enemy.Health.hp > 0 && enemy.Health.gameObject.activeInHierarchy;
    }
}
