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

        // True for bosses: they get no bar above them, only the docked bar at the top of the screen during a fight.
        public bool IsBoss;

        // True once the enemy has lost HP. The docked boss bar only shows after this, so a boss that hasn't been fought
        // yet is never revealed. Never reset to false (a healed boss is still in a fight).
        public bool InFight;

        // Sprite renderer (tk2d sprites draw through a MeshRenderer); its bounds give the "top of the enemy" for the bar.
        // Null when the HealthManager is on an object with no renderer (the sprite is then on a child object).
        public Renderer? Renderer;

        // Fallback for enemies with no renderer on the same object: the hitbox bounds.
        public Collider2D? Collider;

        // True for a boss fought in rounds, one part at a time (False Knight's body, then head). The top bar shows only
        // the part in play, instead of adding the parts together.
        public bool IsStaged;

        // HP at the last Refresh, used to notice changes.
        public int LastHp;

        // True for the staged part the top bar shows: the one being hit, or the one exposed when the other part's round ended.
        public bool InPlay;

        // True once the game has reported this enemy's death (HealthManager.OnDeath). False Knight's fight ends this way
        // with HP left and HealthManager.isDead never set, so GetIsDead alone would leave his bar on screen.
        public bool Finished;

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

        // Start of the object name of False Knight's body (GameObject.name "False Knight New" in the log).
        private const string StagedBossPrefix = "False Knight";

        // GameObject.name of False Knight's head. It's generic, so it only counts while a False Knight is tracked.
        private const string StagedHeadName = "Head";

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
            TrackedEnemy tracked = new TrackedEnemy
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
                // Starts the change detection at the current HP.
                LastHp = self.hp,
            };
            // Adds it to the list EnemyHealthBars reads.
            enemies.Add(tracked);
            // Notes its death when the game reports it (HealthManager.OnDeath, also fired by the FSM action SendHealthManagerDeathEvent).
            self.OnDeath += () => Finish(tracked);
            // Re-marks False Knight's parts, since his body and head can start in either order.
            MarkStagedParts();
        }

        // Marks False Knight's body and head as one staged boss, once his body is tracked.
        private void MarkStagedParts()
        {
            // True if a tracked enemy is False Knight's body.
            bool bodyTracked = false;
            // Checks every tracked enemy; destroyed ones are skipped because Refresh hasn't removed them yet.
            foreach (TrackedEnemy enemy in enemies) if (enemy.Health != null && enemy.Health.gameObject.name.StartsWith(StagedBossPrefix)) bodyTracked = true;
            // Nothing to mark without the body.
            if (!bodyTracked) return;
            // Goes through the enemies again to mark the parts.
            foreach (TrackedEnemy enemy in enemies)
            {
                // Skips destroyed objects.
                if (enemy.Health == null) continue;
                // The enemy's scene object name.
                string name = enemy.Health.gameObject.name;
                // Only the body and head are parts of this fight.
                if (!name.StartsWith(StagedBossPrefix) && name != StagedHeadName) continue;
                // The body opens the fight; only set the first time a part is marked, so later spawns don't change the round.
                if (!enemy.IsStaged) enemy.InPlay = name.StartsWith(StagedBossPrefix);
                // Uses the top bar, never a small bar above it.
                enemy.IsBoss = true;
                // Shows one part at a time.
                enemy.IsStaged = true;
            }
        }

        // Marks an enemy finished; for a staged boss that's every part, because the fight is over for all of them.
        private void Finish(TrackedEnemy finished)
        {
            // Marks the enemy itself; Refresh removes it.
            finished.Finished = true;
            // Non-staged enemies have no other parts.
            if (!finished.IsStaged) return;
            // Marks the other parts too.
            foreach (TrackedEnemy enemy in enemies) if (enemy.IsStaged) enemy.Finished = true;
        }

        // Puts one staged part in play and the others out of it.
        private void SetInPlay(TrackedEnemy current)
        {
            // Goes through the tracked enemies; only staged parts are touched.
            foreach (TrackedEnemy enemy in enemies) if (enemy.IsStaged) enemy.InPlay = enemy == current;
        }

        // Ends a staged part's round: the FalseyControl and Head FSMs refill its HP (SetHP), which exposes the other part.
        private void EndRound(TrackedEnemy refilled)
        {
            // The new round starts from this refilled amount, so it shows as a full bar. Rounds needn't refill to the same HP.
            refilled.MaxHp = refilled.Health.hp;
            // Hands the top bar to the other part, which can have full HP and no InFight yet.
            foreach (TrackedEnemy enemy in enemies) if (enemy.IsStaged) { enemy.InPlay = enemy != refilled; enemy.InFight = true; }
        }

        // Finds the tracked entry for a HealthManager, or null if it isn't tracked. Used by FinishingBlow for MaxHp; a
        // plain loop is fine because it only runs when a hit lands.
        public TrackedEnemy? Find(HealthManager health)
        {
            // Checks each tracked enemy.
            foreach (TrackedEnemy enemy in enemies) if (enemy.Health == health) return enemy;
            // Not tracked.
            return null;
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
                // or that died (HealthManager.GetIsDead, or Finished from HealthManager.OnDeath). Checking destroyed first matters:
                // GetIsDead would throw on a destroyed object.
                if (enemy.Health == null || enemy.Finished || enemy.Health.GetIsDead())
                {
                    // Removes the gone or dead enemy.
                    enemies.RemoveAt(i);
                    // Next enemy.
                    continue;
                }
                // Current HP (HealthManager.hp); some FSMs change it directly, so it's read rather than hooked
                // (a hook on TakeDamage would miss those changes).
                int hp = enemy.Health.hp;
                // For a staged part, a refill ends its round and a hit puts it in play.
                if (enemy.IsStaged && hp != enemy.LastHp) { if (hp > enemy.LastHp) EndRound(enemy); else SetInPlay(enemy); }
                // Remembers this HP for the next frame's comparison.
                enemy.LastHp = hp;
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
