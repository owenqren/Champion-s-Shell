// Mathf, used to keep the tier inside the bonus table (UnityEngine.CoreModule).
using UnityEngine;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // The charm's second ability: the nail does bonus damage to enemies low on health, once the charm has been upgraded.
    public class FinishingBlow
    {
        // Enemy HP fraction at or below which the bonus applies, by upgrade tier (ChampionsShellCharm.Tier), measured
        // against TrackedEnemy.MaxHp for that enemy part. Tier 0 never reaches this check.
        private static readonly float[] LowHealthByTier = { 0f, 0.25f, 0.32f, 0.4f };

        // Damage multiplier by upgrade tier (ChampionsShellCharm.Tier): the index is the tier, and tier 0 has no bonus.
        private static readonly float[] DamageByTier = { 1f, 1.2f, 1.28f, 1.35f };

        // The charm, read for its tier and its equipped flag.
        private readonly ChampionsShellCharm charm;

        // Enemy tracker, read for each enemy's max HP (EnemyTracker.Find).
        private readonly EnemyTracker tracker;

        // Stores what the ability needs; called from ChampionsShell.Initialize().
        public FinishingBlow(ChampionsShellCharm charm, EnemyTracker tracker)
        {
            // Keeps the charm.
            this.charm = charm;
            // Keeps the tracker.
            this.tracker = tracker;
        }

        // Starts watching hits; called once from ChampionsShell.Initialize().
        public void Hook()
        {
            // On.HealthManager.TakeDamage (MMHOOK_Assembly-CSharp) runs our method around every hit that lands. Blocked
            // hits never reach it (HealthManager.Hit sends those to Invincible instead).
            On.HealthManager.TakeDamage += OnTakeDamage;
        }

        // Replacement for HealthManager.TakeDamage: raises the hit's multiplier if the bonus applies, then runs the original.
        private void OnTakeDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hit)
        {
            // Adds the bonus if this hit qualifies. HitInstance is a struct, so this only changes the copy passed on below.
            // TakeDamage deals DamageDealt * Multiplier (src/HealthManager.cs), so this stacks with other damage charms.
            if (Applies(self, hit)) hit.Multiplier *= DamageByTier[Tier()];
            // Always runs the game's own method; skipping it would lose the hit entirely.
            orig(self, hit);
        }

        // Whether the bonus applies to this hit.
        private bool Applies(HealthManager enemy, HitInstance hit)
        {
            // Tier 0 is the base charm, which has no bonus.
            if (Tier() == 0) return false;
            // Only the nail counts (nail arts use the same type); not spells, nail beams or anything else.
            if (hit.AttackType != AttackTypes.Nail) return false;
            // Needs the charm equipped (PlayerData.GetBool reaches EasyCharm's equippedCharm_<id> answer).
            if (!PlayerData.instance.GetBool($"equippedCharm_{charm.Id}")) return false;
            // Finds the enemy's recorded max HP (EnemyTracker.Find).
            TrackedEnemy? tracked = tracker.Find(enemy);
            // Enemies the tracker doesn't know have no max HP to compare with.
            if (tracked == null || tracked.MaxHp <= 0) return false;
            // Low enough on health, using the HP from before this hit.
            return (float)enemy.hp / tracked.MaxHp <= LowHealthByTier[Tier()];
        }

        // The charm's tier, kept inside DamageByTier in case a save holds a value out of range.
        private int Tier() => Mathf.Clamp(charm.Tier, 0, DamageByTier.Length - 1);
    }
}
