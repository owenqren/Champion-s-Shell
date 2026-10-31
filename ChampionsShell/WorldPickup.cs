// Action, used to tell WorldPickup when the Knight touches the charm.
using System;
// MonoBehaviour, GameObject, SpriteRenderer, Vector2/3, Mathf, Time, Object come from Unity (UnityEngine.CoreModule).
using UnityEngine;
// Scene, used to read a room's name.
using UnityEngine.SceneManagement;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // Places Champion's Shell in the world for saves that don't own it yet, and gives it to the player on touch.
    // Ownership is the charm's gotCharm_<id> flag (saved through ChampionsShell.OnSaveLocal), so a collected charm
    // never reappears.
    public class WorldPickup
    {
        // Scene (room) the charm sits in, matching Unity's scene name exactly.
        private const string TargetScene = "Town";

        // World position of the charm in TargetScene, on the path from the Dirtmouth bench to the well. Owen chose the spot
        // in game; the Knight stands at y 8.4 there, so 9 floats just above it. z 0 is the plane the Knight moves on.
        private static readonly Vector3 TargetPosition = new Vector3(179f, 9f, 0f);

        // The charm to give (EasyCharm.GiveCharm from SFCore).
        private readonly ChampionsShellCharm charm;

        // The popup shown on collection.
        private readonly PickupPopup popup;

        // Stores what the pickup needs; called from ChampionsShell.Initialize().
        public WorldPickup(ChampionsShellCharm charm, PickupPopup popup)
        {
            // Keeps the charm.
            this.charm = charm;
            // Keeps the popup.
            this.popup = popup;
        }

        // Starts watching room changes; called once from ChampionsShell.Initialize().
        public void Hook()
        {
            // Unity fires this when a new scene becomes active. It must never throw: an exception here also stops the
            // game's own scene setup (see the freeze note on ChampionsShell.OnLoadLocal). Written out in full because the
            // game has its own global class named SceneManager, which would hide Unity's.
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        // Runs on every room change; spawns the charm if this is its room and the save doesn't own it.
        private void OnSceneChanged(Scene from, Scene to)
        {
            // Only the pickup's room matters.
            if (to.name != TargetScene) return;
            // PlayerData.instance can be null on the way to the main menu.
            if (PlayerData.instance == null) return;
            // Already owned (PlayerData.GetBool reaches EasyCharm's gotCharm_<id> answer): nothing to place.
            if (PlayerData.instance.GetBool($"gotCharm_{charm.Id}")) return;
            // New object in the active scene, so Unity removes it when the player leaves the room.
            GameObject pickup = new GameObject("Champion's Shell Pickup");
            // Puts it at the chosen spot.
            pickup.transform.position = TargetPosition;
            // Adds the sprite renderer that draws the charm.
            SpriteRenderer sprite = pickup.AddComponent<SpriteRenderer>();
            // Uses the charm's icon (EasyCharm.GetSprite, built by ChampionsShellCharm.GetSpriteInternal).
            sprite.sprite = charm.GetSprite();
            // Adds the behaviour that bobs it and watches for the Knight.
            CharmPickup behaviour = pickup.AddComponent<CharmPickup>();
            // Tells it what to do on touch.
            behaviour.Collected = Collect;
        }

        // Gives the charm to the player; runs once when the Knight touches it.
        private void Collect()
        {
            // Unlocks the inventory's Charms tab, which the game hides until PlayerData.hasCharm (first charm found) is true.
            PlayerData.instance.SetBool("hasCharm", true);
            // Sets gotCharm_<id> and marks it new so the inventory shows the "new" dot (EasyCharm.GiveCharm).
            charm.GiveCharm(true);
            // Shows the icon and name on screen (PickupPopup.Show).
            popup.Show();
        }
    }

    // The floating charm in the world: bobs up and down and watches for the Knight. WorldPickup adds it to the pickup object.
    public class CharmPickup : MonoBehaviour
    {
        // Distance in world units at which the Knight collects the charm.
        private const float PickupRadius = 1.5f;

        // How far the charm bobs above and below its spot, in world units.
        private const float BobHeight = 0.15f;

        // Speed of the bobbing, in radians per second.
        private const float BobSpeed = 2f;

        // Called on touch; set by WorldPickup right after AddComponent.
        public Action? Collected;

        // Resting position, set in Start so bobbing doesn't drift.
        private Vector3 restPosition;

        // Runs once before the first Update (Unity MonoBehaviour.Start), after WorldPickup has placed the object.
        private void Start()
        {
            // Remembers the spot to bob around.
            restPosition = transform.position;
        }

        // Runs every frame (Unity MonoBehaviour.Update): bobs the charm and checks for the Knight.
        private void Update()
        {
            // Moves the charm along a sine wave around its spot.
            transform.position = restPosition + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobHeight);
            // The Knight (HeroController.instance), which can be null during scene loads.
            HeroController hero = HeroController.instance;
            // No Knight yet: nothing can touch the charm.
            if (hero == null) return;
            // During a room transition the Knight's position is still from the old room, which could collect by accident.
            if (hero.cState.transitioning) return;
            // Too far away, comparing x and y only because z doesn't matter in a 2D game.
            if (Vector2.Distance(hero.transform.position, restPosition) > PickupRadius) return;
            // Gives the charm (WorldPickup.Collect).
            Collected?.Invoke();
            // Removes the charm from the world.
            Destroy(gameObject);
        }
    }
}
