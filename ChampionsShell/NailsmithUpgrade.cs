// Array.Exists, used to check whether the FSM has already been edited.
using System;
// PlayMakerFSM's FsmOwnerDefault and FsmState come from PlayMaker (PlayMaker.dll in the game copy).
using HutongGames.PlayMaker;
// CallMethodProper, the FSM action the Nailsmith uses to start a conversation (src/HutongGames.PlayMaker.Actions/).
using HutongGames.PlayMaker.Actions;
// ModHooks comes from the Modding API in the copy's Assembly-CSharp.dll.
using Modding;
// FsmUtil's extension methods (GetState, AddState, AddTransition, ChangeTransition, AddMethod, InsertMethod) come from SFCore.
using SFCore.Utils;
// GameObject and MeshRenderer come from Unity (UnityEngine.CoreModule).
using UnityEngine;

// Same namespace as the rest of the mod (RootNamespace in ChampionsShell.csproj).
namespace ChampionsShell
{
    // Lets the player upgrade the charm at the Nailsmith for geo and a relic. It edits his "Conversation Control" FSM and
    // reuses the game's own yes/no box, so the box also takes the geo (its "Dialogue Page Control" FSM has a Take Geo state).
    // After he leaves for good, his invisible object stays while an upgrade is affordable, so the anvil still works.
    public class NailsmithUpgrade
    {
        // GameObject name of the Nailsmith in Room_nailsmith (level16).
        private const string NailsmithObject = "Nailsmith";

        // His FSM that runs the conversation.
        private const string ConversationFsm = "Conversation Control";

        // His FSM that plays the hammer sound while he works; turned off at the empty anvil.
        private const string HammerSoundFsm = "Anvil Tink";

        // New states added to Conversation Control.
        private const string RouteState = "Charm Route";

        // Decides whether the charm offer comes before the nail offer.
        private const string OfferCheckState = "Charm Offer?";

        // Says the offer line.
        private const string OfferState = "Charm Offer";

        // Says the last line and ends the conversation.
        private const string FarewellState = "Charm Farewell";

        // Decides, in place of leaving for good, whether the object stays as the anvil.
        private const string AnvilCheckState = "Charm Anvil Check";

        // Waits for the player at the empty anvil, like the game's Idle state.
        private const string AnvilIdleState = "Charm Anvil Idle";

        // Events the states above send to move on.
        private const string ToOffer = "CHARM OFFER";

        // Carries on to the game's normal conversation.
        private const string ToVanilla = "CHARM VANILLA";

        // Skips the game's price lookup for the charm's price.
        private const string ToPrompt = "CHARM PROMPT";

        // Ends the conversation with the last line.
        private const string ToFarewell = "CHARM FAREWELL";

        // Stays as the anvil.
        private const string ToAnvil = "CHARM ANVIL";

        // Language keys answered by OnLanguageGet; the tier is added to the prompt key.
        private const string PromptKey = "CHARM_UPGRADE_PROMPT_";

        // The Nailsmith's offer.
        private const string OfferKey = "CHARM_UPGRADE_OFFER";

        // The Nailsmith's thanks.
        private const string ThanksKey = "CHARM_UPGRADE_THANKS";

        // The offer read at the empty anvil, with no Nailsmith to speak it.
        private const string AnvilOfferKey = "CHARM_UPGRADE_ANVIL_OFFER";

        // The line after an upgrade at the empty anvil.
        private const string AnvilDoneKey = "CHARM_UPGRADE_ANVIL_DONE";

        // The line after declining at the empty anvil.
        private const string AnvilDeclineKey = "CHARM_UPGRADE_ANVIL_DECLINE";

        // Sheet the game's Send Text state reads the yes/no prompt from.
        private const string PromptSheet = "Prompts";

        // Sheet for the spoken lines; OnLanguageGet answers by key, so any sheet name works.
        private const string SpeechSheet = "Nailsmith";

        // Relic spent for each upgrade, as a PlayerData field: the index is the current tier. trinket1 Wanderer's Journal,
        // trinket2 Hallownest Seal, trinket3 King's Idol.
        private static readonly string[] RelicByTier = { "trinket1", "trinket2", "trinket3" };

        // Relic names for the prompt, in the same order.
        private static readonly string[] RelicNameByTier = { "Wanderer's Journal", "Hallownest Seal", "King's Idol" };

        // Geo cost of each upgrade, in the same order.
        private static readonly int[] GeoByTier = { 300, 800, 2000 };

        // The charm, read for its ownership and tier and upgraded here.
        private readonly ChampionsShellCharm charm;

        // True while the object is standing in as the anvil, after the Nailsmith has left.
        private bool atAnvil;

        // True while the yes/no box on screen is the charm's rather than the nail's, so the shared states know where to go.
        private bool buying;

        // Stores what the upgrade needs; called from ChampionsShell.Initialize().
        public NailsmithUpgrade(ChampionsShellCharm charm)
        {
            // Keeps the charm.
            this.charm = charm;
        }

        // Starts watching FSMs and text lookups; called once from ChampionsShell.Initialize().
        public void Hook()
        {
            // On.PlayMakerFSM.OnEnable (MMHOOK_PlayMaker) runs around every FSM when its object becomes active.
            On.PlayMakerFSM.OnEnable += OnFsmEnable;
            // The game asks for all text through this hook (Language.Get), which is how the new lines appear.
            ModHooks.LanguageGetHook += OnLanguageGet;
        }

        // Replacement for PlayMakerFSM.OnEnable: edits the Nailsmith's conversation, then runs the original.
        private void OnFsmEnable(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM self)
        {
            // Only his conversation FSM, and only once per object (OnEnable can run again on the same object).
            if (self.gameObject.name == NailsmithObject && self.FsmName == ConversationFsm && !Array.Exists(self.FsmStates, s => s.Name == OfferState)) Edit(self);
            // Always runs the game's own method; every FSM in the game passes through here.
            orig(self);
        }

        // Adds the charm's states to the conversation.
        private void Edit(PlayMakerFSM fsm)
        {
            // A new room visit starts out of anvil mode; the Idle state decides again.
            atAnvil = false;
            // No yes/no box is open.
            buying = false;

            // All the new states are created before any is linked, because FsmUtil.AddTransition stores the target state
            // when it's called, and a link to a state that doesn't exist yet goes nowhere and hangs the talk.
            // Choice at the start of the talk: at the anvil, skip his greetings.
            FsmState route = fsm.AddState(RouteState);
            // Choice after the greeting: the charm offer if it's affordable, otherwise the normal nail offer.
            FsmState offerCheck = fsm.AddState(OfferCheckState);
            // Says the offer and sets up the game's yes/no box.
            FsmState offer = fsm.AddState(OfferState);
            // Waits for the last line, then ends the talk through the game's own state.
            FsmState farewell = fsm.AddState(FarewellState);
            // The empty anvil: the game's Idle state sends LEFT when PlayerData.nailsmithCliff is set, and normally deactivates him.
            FsmState anvilCheck = fsm.AddState(AnvilCheckState);
            // Listens for the player talking, as the game's Idle does (npc_control sends CONVO START).
            FsmState anvilIdle = fsm.AddState(AnvilIdleState);

            // Sends the talk to the offer or to the game's Met? state (depends on atAnvil, set in the anvil check below).
            route.AddMethod(() => fsm.SendEvent(atAnvil ? ToOffer : ToVanilla));
            // Anvil route.
            route.AddTransition(ToOffer, OfferState);
            // Normal route.
            route.AddTransition(ToVanilla, "Met?");
            // Puts the choice between the box opening and the greeting (the game's Box Up state).
            fsm.ChangeTransition("Box Up", "FINISHED", RouteState);

            // Sends the talk on (depends on CanUpgrade).
            offerCheck.AddMethod(() => fsm.SendEvent(CanUpgrade() ? ToOffer : ToVanilla));
            // Affordable.
            offerCheck.AddTransition(ToOffer, OfferState);
            // Not affordable, so the game carries on as normal.
            offerCheck.AddTransition(ToVanilla, "Offer Type");
            // All three greetings normally lead to Offer Type; they now go through the check first.
            foreach (string greeting in new[] { "Greet", "Regreet", "Meet 3" }) fsm.ChangeTransition(greeting, "CONVO_FINISH", OfferCheckState);

            // Depends on the FSM variables Send Text reads ("Prompt Convo", "Upgrade Cost") and on the dialogue box.
            offer.AddMethod(() => StartOffer(fsm));
            // When the line ends, the game's own states close the box and open the yes/no box.
            offer.AddTransition("CONVO_FINISH", "Dial Box Down");

            // In the game's Price Check, jumps past the nail prices to the yes/no text while the charm is being bought.
            fsm.InsertMethod("Price Check", 0, () => { if (buying) fsm.SendEvent(ToPrompt); });
            // Target of that jump.
            fsm.AddTransition("Price Check", ToPrompt, "Send Text");

            // After Yes, the game's Accept state would start the nail cutscene; the charm's upgrade runs instead.
            fsm.InsertMethod("Accept", 0, () => { if (buying) { Upgrade(); Farewell(fsm, atAnvil ? AnvilDoneKey : ThanksKey); } });
            // Target for the end of the purchase.
            fsm.AddTransition("Accept", ToFarewell, FarewellState);

            // After No, the game's Enough Geo? state would say a nail line; the charm carries on instead.
            fsm.InsertMethod("Enough Geo?", 0, () => { if (buying) Declined(fsm); });
            // Declining at the Nailsmith continues to the nail offer.
            fsm.AddTransition("Enough Geo?", ToVanilla, "Offer Type");
            // Declining at the anvil ends the talk.
            fsm.AddTransition("Enough Geo?", ToFarewell, FarewellState);

            // Ends the talk once the last line finishes.
            farewell.AddTransition("CONVO_FINISH", "Talk Finish 2");

            // Stays as the anvil if an upgrade is affordable, otherwise leaves as normal (depends on CanUpgrade).
            anvilCheck.AddMethod(() => CheckAnvil(fsm));
            // Stays.
            anvilCheck.AddTransition(ToAnvil, AnvilIdleState);
            // Leaves.
            anvilCheck.AddTransition(ToVanilla, "Leave");
            // Puts the check before the game's Leave state.
            fsm.ChangeTransition("Idle", "LEFT", AnvilCheckState);
            // Starts the talk from the empty anvil.
            anvilIdle.AddTransition("CONVO START", "Hero Look");
        }

        // Whether this save can buy the next upgrade right now.
        private bool CanUpgrade()
        {
            // Fully upgraded: nothing to offer.
            if (charm.Tier >= RelicByTier.Length) return false;
            // Needs the charm (PlayerData.GetBool reaches EasyCharm's gotCharm_<id> answer).
            if (!PlayerData.instance.GetBool($"gotCharm_{charm.Id}")) return false;
            // Needs the relic and the geo (PlayerData.trinket1-3 and geo).
            return PlayerData.instance.GetInt(RelicByTier[charm.Tier]) > 0 && PlayerData.instance.GetInt("geo") >= GeoByTier[charm.Tier];
        }

        // Says the offer line and sets the price for the yes/no box.
        private void StartOffer(PlayMakerFSM fsm)
        {
            // The shared states below now belong to the charm.
            buying = true;
            // Text for the yes/no box; Send Text reads this variable, and OnLanguageGet answers the key.
            fsm.FsmVariables.GetFsmString("Prompt Convo").Value = PromptKey + charm.Tier;
            // Price for the yes/no box; Send Text copies it to the box's Toll Cost, and the box takes that geo on Yes.
            fsm.FsmVariables.GetFsmInt("Upgrade Cost").Value = GeoByTier[charm.Tier];
            // Starts the line; the DialogueBox sends CONVO_FINISH to the FSM when it ends (DialogueManager's page control).
            Say(fsm, atAnvil ? AnvilOfferKey : OfferKey);
        }

        // Spends the relic and raises the tier. The yes/no box has already taken the geo.
        private void Upgrade()
        {
            // Removes one relic (PlayerData.DecrementInt); the tier still names the relic for this upgrade here.
            PlayerData.instance.DecrementInt(RelicByTier[charm.Tier]);
            // Raises the tier, which FinishingBlow and the charm's description read.
            charm.Tier++;
            // Saves now, as the game does after a nail upgrade; this also runs ChampionsShell.OnSaveLocal, which stores the tier.
            GameManager.instance.SaveGame();
        }

        // Moves on after No: to the nail offer at the Nailsmith, or to a last line at the anvil.
        private void Declined(PlayMakerFSM fsm)
        {
            // Ends the talk at the anvil, which has no nail offer.
            if (atAnvil) { Farewell(fsm, AnvilDeclineKey); return; }
            // The yes/no box is finished with.
            buying = false;
            // Carries on to the nail offer.
            fsm.SendEvent(ToVanilla);
        }

        // Says a last line and moves to the state that waits for it to end.
        private void Farewell(PlayMakerFSM fsm, string key)
        {
            // The yes/no box is finished with.
            buying = false;
            // Moves first, so the state is waiting for CONVO_FINISH.
            fsm.SendEvent(ToFarewell);
            // Starts the line.
            Say(fsm, key);
        }

        // Starts a spoken line in the game's dialogue box.
        private void Say(PlayMakerFSM fsm, string key)
        {
            // Finds the box as the game's own conversation action in "Not Enough Geo" does. It's read here, during the talk,
            // because the FSM's reference may not be set yet when the room loads.
            GameObject box = fsm.Fsm.GetOwnerDefaultTarget(fsm.GetFirstActionOfType<CallMethodProper>("Not Enough Geo").gameObject);
            // Shows the line; OnLanguageGet supplies the text.
            box.GetComponent<DialogueBox>().StartConversation(key, SpeechSheet);
        }

        // Runs each time the game's Idle state finds the Nailsmith has left.
        private void CheckAnvil(PlayMakerFSM fsm)
        {
            // Nothing affordable: he leaves as normal, which also removes the talk prompt.
            if (!CanUpgrade()) { atAnvil = false; fsm.SendEvent(ToVanilla); return; }
            // Stands in as the anvil.
            atAnvil = true;
            // Hides his sprite, since tk2d sprites draw through a MeshRenderer on the object.
            fsm.GetComponent<MeshRenderer>().enabled = false;
            // Silences the hammer sound, which is timed to his animation (FSM Anvil Tink, on the same object).
            PlayMakerFSM.FindFsmOnGameObject(fsm.gameObject, HammerSoundFsm).enabled = false;
            // Waits for the player.
            fsm.SendEvent(ToAnvil);
        }

        // Answers the game's text lookups for the new lines; every other key keeps its original text.
        private string OnLanguageGet(string key, string sheet, string orig)
        {
            // The yes/no prompt for each tier, naming the relic it takes (the box shows the geo price itself).
            if (sheet == PromptSheet && key.StartsWith(PromptKey)) return $"Give the {RelicNameByTier[int.Parse(key.Substring(PromptKey.Length))]} to strengthen Champion's Shell?";
            // The Nailsmith's offer.
            if (key == OfferKey) return "That shell you carry is the work of a master. Bring me the right relic and some Geo for my efforts, and I'll temper it further.";
            // The Nailsmith's thanks.
            if (key == ThanksKey) return "There. Fine work, if I say so myself. It should serve you well.";
            // The anvil's offer.
            if (key == AnvilOfferKey) return "The anvil is cold, but the forge's tools remain. The shell could still be tempered here.";
            // The anvil after an upgrade.
            if (key == AnvilDoneKey) return "The shell hums with new strength.";
            // The anvil after declining.
            if (key == AnvilDeclineKey) return "The anvil waits in silence.";
            // Every other key.
            return orig;
        }
    }
}
