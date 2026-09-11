using System.Collections.Generic;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// Every word the game's own screens say, and the English it says them in.
    /// </summary>
    /// <remarks>
    /// <b>Why the English lives here and not in a file.</b> These strings were literals
    /// inside the views that draw them, and that is what made them trustworthy: they could
    /// not go missing, could not be half-shipped, and could not disagree with the code that
    /// used them. Moving them to a content file would trade all of that for the ability to
    /// retype "Accept" without a compile. So English stays compiled in, and translations are
    /// content -- which is the direction risk should run, because an absent translation
    /// leaves a readable screen and an absent English string leaves a blank one.
    ///
    /// <b>What is deliberately not here.</b> The names of items, monsters, NPCs, maps and
    /// quests. Those belong to the content the game ships, are authored as
    /// <c>LocalizationKey</c>s on the definitions, and are looked up through
    /// <see cref="UiText.ContentText"/>. A monster that a patch renames must not require a
    /// code change, and this file must not become a second place a name can be written down.
    ///
    /// <b>The keys are the contract with the translators.</b> A test asserts every key here
    /// has a Thai entry, so a screen added without its translation fails the build rather
    /// than quietly shipping in English.
    ///
    /// <b>Placeholders are numbered, never concatenated.</b> See <see cref="UiText.Format"/>.
    /// </remarks>
    public static class UiStrings
    {
        // ---- shared -----------------------------------------------------------------------

        public const string CommonBack = "ui.common.back";
        public const string CommonEnter = "ui.common.enter";
        public const string CommonClose = "ui.common.close";
        public const string CommonAccept = "ui.common.accept";
        public const string CommonNone = "ui.common.none";
        public const string CommonHeaderStatus = "ui.common.header.status";
        public const string CommonHeaderPlayers = "ui.common.header.players";
        public const string CommonHeaderPing = "ui.common.header.ping";
        public const string CommonPopulation = "ui.common.population";
        public const string CommonLoading = "ui.common.loading";
        public const string CommonNothingToShow = "ui.common.nothing_to_show";
        public const string CommonLanguage = "ui.common.language";

        // ---- quest journal ----------------------------------------------------------------

        public const string QuestTitle = "ui.quest.title";
        public const string QuestTitleAvailable = "ui.quest.title.available";
        public const string QuestTitleActive = "ui.quest.title.active";
        public const string QuestTitleCompleted = "ui.quest.title.completed";
        public const string QuestTabAvailable = "ui.quest.tab.available";
        public const string QuestTabActive = "ui.quest.tab.active";
        public const string QuestTabCompleted = "ui.quest.tab.completed";
        public const string QuestButtonReady = "ui.quest.button.ready";
        public const string QuestButtonInProgress = "ui.quest.button.in_progress";
        public const string QuestButtonDone = "ui.quest.button.done";
        public const string QuestEmpty = "ui.quest.empty";
        public const string QuestSelect = "ui.quest.select";
        public const string QuestSectionGiver = "ui.quest.section.giver";
        public const string QuestSectionDescription = "ui.quest.section.description";
        public const string QuestSectionObjective = "ui.quest.section.objective";
        public const string QuestSectionRewards = "ui.quest.section.rewards";
        public const string QuestRequiresLevel = "ui.quest.requires_level";
        public const string QuestRewardExperience = "ui.quest.reward.experience";
        public const string QuestRewardCurrency = "ui.quest.reward.currency";
        public const string QuestRewardStack = "ui.quest.reward.stack";
        public const string QuestObjectiveProgress = "ui.quest.objective.progress";
        public const string QuestObjectivePlain = "ui.quest.objective.plain";
        public const string QuestVerbDefeat = "ui.quest.verb.defeat";
        public const string QuestVerbCollect = "ui.quest.verb.collect";
        public const string QuestVerbTalk = "ui.quest.verb.talk";
        public const string QuestVerbTravel = "ui.quest.verb.travel";
        public const string QuestVerbReachLevel = "ui.quest.verb.reach_level";
        public const string QuestVerbDeliver = "ui.quest.verb.deliver";
        public const string QuestVerbOther = "ui.quest.verb.other";
        public const string QuestHintTurnInNamed = "ui.quest.hint.turn_in.named";
        public const string QuestHintTurnInUnknown = "ui.quest.hint.turn_in.unknown";
        public const string QuestHintAcceptNamed = "ui.quest.hint.accept.named";
        public const string QuestHintAcceptUnknown = "ui.quest.hint.accept.unknown";
        public const string QuestFinishedNote = "ui.quest.finished_note";
        public const string QuestFinishedRepeatableNote = "ui.quest.finished_note.repeatable";
        public const string QuestRepeatable = "ui.quest.repeatable";
        public const string QuestDaily = "ui.quest.daily";
        public const string QuestOneTime = "ui.quest.one_time";
        public const string QuestRowRepeatable = "ui.quest.row.repeatable";
        public const string QuestRowDaily = "ui.quest.row.daily";
        public const string QuestTrackerComplete = "ui.quest.tracker.complete";

        // ---- NPC conversation --------------------------------------------------------------

        public const string NpcButtonTurnIn = "ui.npc.button.turn_in";
        public const string NpcQuestOffered = "ui.npc.quest.offered";
        public const string NpcQuestFinished = "ui.npc.quest.finished";

        // What each kind of NPC says when you walk up to them. Keyed by role rather than by
        // NPC, exactly as NpcDialogueView.KeyFor builds it -- so these strings and that
        // method must keep agreeing, which a test checks by calling it.
        public const string NpcServiceGeneric = "npc.service.generic";
        public const string NpcServiceQuest = "npc.service.quest";
        public const string NpcServiceShop = "npc.service.shop";
        public const string NpcServiceStorage = "npc.service.storage";
        public const string NpcServiceJobChange = "npc.service.jobchange";
        public const string NpcServiceWarp = "npc.service.warp";
        public const string NpcServiceEnhancement = "npc.service.enhancement";
        public const string NpcRefusedTooFar = "ui.npc.refused.too_far";
        public const string NpcRefusedWrongMap = "ui.npc.refused.wrong_map";
        public const string NpcRefusedDisabled = "ui.npc.refused.disabled";
        public const string NpcRefusedOther = "ui.npc.refused.other";

        // ---- the world around the player ---------------------------------------------------

        public const string WorldPortalTo = "ui.world.portal.to";
        public const string WorldPortalClosed = "ui.world.portal.closed";
        public const string WorldPortalTooFar = "ui.world.portal.too_far";
        public const string WorldPortalLevel = "ui.world.portal.level";
        public const string WorldNpcUnavailable = "ui.world.npc.unavailable";
        public const string WorldNpcTooFar = "ui.world.npc.too_far";
        public const string WorldMonsterLabel = "ui.world.monster.label";

        // ---- heads-up display ---------------------------------------------------------------

        public const string HudInventory = "ui.hud.inventory";
        public const string HudHints = "ui.hud.hints";

        /// <summary>Shown across the middle of the screen when the player has fallen.</summary>
        public const string HudDefeated = "ui.hud.defeated";

        /// <summary>The one thing there is to do about it.</summary>
        public const string HudReturnToTown = "ui.hud.return_to_town";

        // ---- sign in --------------------------------------------------------------------------

        public const string LoginTitle = "ui.login.title";
        public const string LoginPromptCredentials = "ui.login.prompt.credentials";
        public const string LoginStatusConnecting = "ui.login.status.connecting";
        public const string LoginFieldAccount = "ui.login.field.account";
        public const string LoginFieldAccountShort = "ui.login.field.account_short";
        public const string LoginFieldPassword = "ui.login.field.password";
        public const string LoginButtonSubmit = "ui.login.button.submit";
        public const string LoginButtonSubmitting = "ui.login.button.submitting";
        public const string LoginRemember = "ui.login.remember";
        public const string LoginFooter = "ui.login.footer";
        public const string LoginTagline = "ui.login.tagline";

        // ---- choosing a server ----------------------------------------------------------------

        public const string ServerTitle = "ui.server.title";
        public const string ServerEmpty = "ui.server.empty";
        public const string ServerSubtitle = "ui.server.subtitle";
        public const string ServerHeaderName = "ui.server.header.name";
        public const string ServerStateOnline = "ui.server.state.online";

        // ---- choosing a channel ---------------------------------------------------------------

        public const string ChannelTitle = "ui.channel.title";
        public const string ChannelEmpty = "ui.channel.empty";
        public const string ChannelSubtitle = "ui.channel.subtitle";
        public const string ChannelHeaderName = "ui.channel.header.name";
        public const string ChannelStateOpen = "ui.channel.state.open";

        // ---- choosing a character -------------------------------------------------------------

        public const string CharacterTitle = "ui.character.title";
        public const string CharacterEmpty = "ui.character.empty";
        public const string CharacterHeading = "ui.character.heading";
        public const string CharacterSubtitle = "ui.character.subtitle";
        public const string CharacterLevel = "ui.character.level";
        public const string CharacterSlotDetail = "ui.character.slot_detail";
        public const string CharacterInfoLevel = "ui.character.info.level";
        public const string CharacterInfoClass = "ui.character.info.class";
        public const string CharacterInfoLocation = "ui.character.info.location";
        public const string CharacterButtonEnterWorld = "ui.character.button.enter_world";
        public const string CharacterCreate = "ui.character.create";
        public const string CharacterCreateUnavailable = "ui.character.create.unavailable";
        public const string CharacterEnteringWorld = "ui.character.entering_world";

        // ---- what went wrong ------------------------------------------------------------------

        public const string RejectSessionExpired = "ui.reject.session_expired";
        public const string RejectSessionRevoked = "ui.reject.session_revoked";
        public const string RejectSessionInvalid = "ui.reject.session_invalid";
        public const string RejectServerFull = "ui.reject.server_full";
        public const string RejectServerMaintenance = "ui.reject.server_maintenance";
        public const string RejectServerUnavailable = "ui.reject.server_unavailable";
        public const string RejectChannelFull = "ui.reject.channel_full";
        public const string RejectChannelMaintenance = "ui.reject.channel_maintenance";
        public const string RejectChannelUnavailable = "ui.reject.channel_unavailable";
        public const string RejectCharacterUnavailable = "ui.reject.character_unavailable";
        public const string RejectCharacterNotPlayable = "ui.reject.character_not_playable";
        public const string RejectCharacterInWorld = "ui.reject.character_in_world";
        public const string RejectVersionMismatch = "ui.reject.version_mismatch";
        public const string RejectBadCredentials = "ui.reject.bad_credentials";
        public const string RejectAccountBanned = "ui.reject.account_banned";
        public const string RejectAccountSuspended = "ui.reject.account_suspended";
        public const string RejectAccountDisabled = "ui.reject.account_disabled";
        public const string RejectMaintenance = "ui.reject.maintenance";
        public const string RejectUnreachable = "ui.reject.unreachable";
        public const string RejectUnknown = "ui.reject.unknown";

        // ---- the bag and what is done with it ---------------------------------------------------

        public const string InventoryTitle = "ui.inventory.title";
        public const string InventoryWaiting = "ui.inventory.waiting";
        public const string InventoryEmptySlot = "ui.inventory.empty_slot";
        public const string InventoryWeight = "ui.inventory.weight";
        public const string InventoryCapacity = "ui.inventory.capacity";
        public const string InventoryActionEquip = "ui.inventory.action.equip";
        public const string InventoryActionUnequip = "ui.inventory.action.unequip";
        public const string InventoryActionUse = "ui.inventory.action.use";
        public const string InventoryActionSplit = "ui.inventory.action.split";
        public const string InventoryActionDrop = "ui.inventory.action.drop";
        public const string InventoryActionCancel = "ui.inventory.action.cancel";
        public const string SplitTitle = "ui.split.title";
        public const string SplitConfirm = "ui.split.confirm";
        public const string SplitCancel = "ui.split.cancel";
        public const string SplitAmount = "ui.split.amount";

        /// <summary>The English wording the game ships with, by key.</summary>
        /// <remarks>Exposed so a test can walk it and so the localization service can seed
        /// English from it before a content file adds anything on top.</remarks>
        public static IReadOnlyDictionary<string, string> English => Table;

        /// <summary>Every key the screens use. In no particular order.</summary>
        public static IEnumerable<string> Keys => Table.Keys;

        /// <summary>How many words the game says. For a coverage report.</summary>
        public static int Count => Table.Count;

        /// <summary>The shipped English for a key, or null when it is not one of ours.</summary>
        public static string EnglishFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            string text;

            return Table.TryGetValue(key, out text) ? text : null;
        }

        private static readonly Dictionary<string, string> Table =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                // shared
                { CommonBack, "Back" },
                { CommonEnter, "Enter" },
                { CommonClose, "Close" },
                { CommonAccept, "Accept" },
                { CommonNone, "-" },
                { CommonHeaderStatus, "Status" },
                { CommonHeaderPlayers, "Players" },
                { CommonHeaderPing, "Ping" },
                { CommonPopulation, "{0}  ~  {1} online" },
                { CommonLoading, "Loading..." },
                { CommonNothingToShow, "Nothing to show" },
                { CommonLanguage, "Language" },

                // quest journal
                { QuestTitle, "QUESTS" },
                { QuestTitleAvailable, "QUESTS - AVAILABLE" },
                { QuestTitleActive, "QUESTS - ACTIVE" },
                { QuestTitleCompleted, "QUESTS - COMPLETED" },
                { QuestTabAvailable, "Available" },
                { QuestTabActive, "Active" },
                { QuestTabCompleted, "Completed" },
                { QuestButtonReady, "Ready" },
                { QuestButtonInProgress, "In progress" },
                { QuestButtonDone, "Done" },
                { QuestEmpty, "Nothing here." },
                { QuestSelect, "Select a quest." },
                { QuestSectionGiver, "Quest Giver" },
                { QuestSectionDescription, "Description" },
                { QuestSectionObjective, "Objective" },
                { QuestSectionRewards, "Rewards" },
                { QuestRequiresLevel, "Requires level {0}" },
                { QuestRewardExperience, "EXP {0}" },
                { QuestRewardCurrency, "Gold {0}" },
                { QuestRewardStack, "{0} x {1}" },
                { QuestObjectiveProgress, "{0} {1} {2} / {3}" },
                { QuestObjectivePlain, "{0} {1}" },
                { QuestVerbDefeat, "Defeat" },
                { QuestVerbCollect, "Collect" },
                { QuestVerbTalk, "Speak with" },
                { QuestVerbTravel, "Travel to" },
                { QuestVerbReachLevel, "Reach level" },
                { QuestVerbDeliver, "Deliver" },
                { QuestVerbOther, "Objective" },
                { QuestHintTurnInNamed, "Return to {0} to claim your reward." },
                { QuestHintTurnInUnknown, "Return to this quest's giver to claim your reward." },
                { QuestHintAcceptNamed, "Speak with {0} to accept this quest." },
                { QuestHintAcceptUnknown, "Find this quest's giver to accept it." },
                { QuestFinishedNote, "Finished. Thank you for your help." },
                { QuestFinishedRepeatableNote,
                    "Finished. Come back and take it again whenever you like." },
                { QuestRepeatable, "Repeatable -- can be taken again" },
                { QuestDaily, "Daily -- resets at midnight" },
                { QuestOneTime, "One time only" },
                { QuestRowRepeatable, "{0}  [repeat]" },
                { QuestRowDaily, "{0}  [daily]" },
                { QuestTrackerComplete, " (complete)" },

                // NPC conversation
                { NpcButtonTurnIn, "Turn In" },
                { NpcQuestOffered, "-- NEW QUEST --" },
                { NpcQuestFinished, "-- QUEST COMPLETE --" },
                { NpcServiceGeneric, "Good day to you." },
                { NpcServiceQuest, "Welcome to Harbor Town.\n"
                    + "This is a safe place for new adventurers." },
                { NpcServiceShop, "General goods, fairly priced.\n[Shop service]" },
                { NpcServiceStorage, "Your belongings are safe with me.\n[Storage service]" },
                { NpcServiceJobChange, "I can guide you along your chosen path.\n"
                    + "Your first job advancement begins at Level 15." },
                { NpcServiceWarp, "I can take you elsewhere.\n[Travel service]" },
                { NpcServiceEnhancement, "Bring me your steel and I will make it sharper.\n"
                    + "[Blacksmith service]" },
                { NpcRefusedTooFar, "You are too far away." },
                { NpcRefusedWrongMap, "They are not here." },
                { NpcRefusedDisabled, "They have nothing to say right now." },
                { NpcRefusedOther, "They cannot help you with that." },

                // the world
                { WorldPortalTo, "To {0}" },
                { WorldPortalClosed, "Closed" },
                { WorldPortalTooFar, "Too far" },
                { WorldPortalLevel, "Level {0}" },
                { WorldNpcUnavailable, "{0} (unavailable)" },
                { WorldNpcTooFar, "{0} (too far)" },
                { WorldMonsterLabel, "Lv{0} {1}  {2}/{3}" },

                // heads-up display
                { HudInventory, "Inventory" },
                { HudHints, "Left Click: Move / Select / Attack / Pick up\n"
                    + "Right Drag: Camera\nMouse Wheel: Zoom" },
                { HudDefeated, "You have fallen." },
                { HudReturnToTown, "Return to town" },

                // sign in
                { LoginTitle, "Chibi Fantasy" },
                { LoginPromptCredentials, "Enter your login and password" },
                { LoginStatusConnecting, "Connecting..." },
                { LoginFieldAccount, "Login ID" },
                { LoginFieldAccountShort, "Login" },
                { LoginFieldPassword, "Password" },
                { LoginButtonSubmit, "Sign in" },
                { LoginButtonSubmitting, "Signing in..." },
                { LoginRemember, "Remember me" },
                { LoginFooter, "Welcome to Chibi Fantasy" },
                { LoginTagline, "SMALL HEROES    BIG ADVENTURES" },

                // choosing a server
                { ServerTitle, "Choose a server" },
                { ServerEmpty, "No available servers" },
                { ServerSubtitle, "Choose a server to begin your adventure" },
                { ServerHeaderName, "Server Name" },
                { ServerStateOnline, "Online" },

                // choosing a channel
                { ChannelTitle, "Choose a channel" },
                { ChannelEmpty, "No available channels" },
                { ChannelSubtitle, "Choose a channel to enter the world" },
                { ChannelHeaderName, "Channel Name" },
                { ChannelStateOpen, "Open" },

                // choosing a character
                { CharacterTitle, "Choose a character" },
                { CharacterEmpty, "No characters on this account" },
                { CharacterHeading, "Select Character" },
                { CharacterSubtitle, "Choose your hero to enter the world" },
                { CharacterLevel, "Level {0}" },
                { CharacterSlotDetail, "Lv. {0}   {1}" },
                { CharacterInfoLevel, "Level" },
                { CharacterInfoClass, "Class" },
                { CharacterInfoLocation, "Location" },
                { CharacterButtonEnterWorld, "Enter World" },
                { CharacterCreate, "Create Character" },
                { CharacterCreateUnavailable, "Character creation is not available yet" },
                { CharacterEnteringWorld, "Entering world..." },

                // what went wrong
                { RejectSessionExpired, "Your session expired -- sign in again" },
                { RejectSessionRevoked, "Your session was ended" },
                { RejectSessionInvalid, "Your session is no longer valid" },
                { RejectServerFull, "That server is full" },
                { RejectServerMaintenance, "That server is under maintenance" },
                { RejectServerUnavailable, "That server is unavailable" },
                { RejectChannelFull, "That channel is full" },
                { RejectChannelMaintenance, "That channel is under maintenance" },
                { RejectChannelUnavailable, "That channel is unavailable" },
                { RejectCharacterUnavailable, "That character is unavailable" },
                { RejectCharacterNotPlayable, "That character cannot be played right now" },
                { RejectCharacterInWorld, "That character is already in the world" },
                { RejectVersionMismatch, "Your client needs updating" },
                { RejectBadCredentials, "Incorrect login or password" },
                { RejectAccountBanned, "This account is banned" },
                { RejectAccountSuspended, "This account is suspended" },
                { RejectAccountDisabled, "This account is disabled" },
                { RejectMaintenance, "The service is under maintenance" },
                { RejectUnreachable, "Could not reach the server -- try again" },
                { RejectUnknown, "Something went wrong -- try again" },

                // the bag
                { InventoryTitle, "Inventory" },
                { InventoryWaiting, "Waiting for server state" },
                { InventoryEmptySlot, "Empty" },
                { InventoryWeight, "Weight {0} / {1}" },
                { InventoryCapacity, "Slots {0} / {1}" },
                { InventoryActionEquip, "Equip" },
                { InventoryActionUnequip, "Unequip" },
                { InventoryActionUse, "Use" },
                { InventoryActionSplit, "Split" },
                { InventoryActionDrop, "Drop" },
                { InventoryActionCancel, "Cancel" },
                { SplitTitle, "Split Stack" },
                { SplitConfirm, "Split" },
                { SplitCancel, "Cancel" },
                { SplitAmount, "Amount" }
            };
    }
}
