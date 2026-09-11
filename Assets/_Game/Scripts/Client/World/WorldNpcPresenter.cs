using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Makes the townspeople standing in a scene into people a player can talk to.
    /// </summary>
    /// <remarks>
    /// <b>The models were already there; this is everything else.</b> Harbor Town ships five
    /// NPC models placed by hand. Nothing moves them, nothing replaces them and nothing
    /// spawns a second copy: each is found by the marker on it, given a name above its head
    /// and a click that reaches the server.
    ///
    /// <b>Names come from content, not from the hierarchy.</b> A marker names an NPC id, the
    /// id resolves to an <see cref="NPCDefinition"/>, and the definition's localization key
    /// is what gets drawn. Renaming a GameObject therefore changes nothing, and an NPC the
    /// catalogue does not ship shows no plate at all rather than a lie.
    ///
    /// <b>It asks; it never answers.</b> A click sends one request and draws whatever comes
    /// back. The role is chosen from what the definition says it offers -- which is a guess
    /// the server is free to refuse, and does, through exactly the same check it would apply
    /// to a client that made the role up.
    /// </remarks>
    public sealed class WorldNpcPresenter : MonoBehaviour
    {
        /// <summary>How high above an NPC's feet its name floats.</summary>
        public const float PlateHeight = 2.0f;

        /// <summary>How high the quest mark floats, above the name.</summary>
        public const float MarkHeight = 2.55f;

        private readonly List<WorldNpcMarker> _markers = new List<WorldNpcMarker>();
        private readonly Dictionary<WorldNpcMarker, CharacterNameplate> _plates =
            new Dictionary<WorldNpcMarker, CharacterNameplate>();

        private readonly Dictionary<WorldNpcMarker, CharacterNameplate> _marks =
            new Dictionary<WorldNpcMarker, CharacterNameplate>();

        private QuestJournal _journal;

        private IDefinitionRegistry<NPCDefinition> _npcs;
        private ILocalizedTextSource _text;
        private CharacterNetworkEntity _player;
        private long _sequence;

        /// <summary>How many NPCs were adopted from the scene.</summary>
        public int Count => _markers.Count;

        /// <summary>The last answer the server gave. For tests and for a HUD.</summary>
        public NpcInteractionSnapshot LastAnswer { get; private set; }

        /// <summary>Raised when the server allows an interaction and a screen should open.</summary>
        public event System.Action<NpcInteractionSnapshot> Authorised;

        /// <summary>Raised when the server refuses one, so a player can be told why.</summary>
        public event System.Action<NpcInteractionSnapshot> Refused;

        /// <summary>Points this at the world's content and the player's own network object.</summary>
        /// <remarks>The entity may be null while the world is still assembling; a click
        /// before then does nothing rather than throwing, and the player clicks again.</remarks>
        public void Bind(IDefinitionRegistry<NPCDefinition> npcs,
            ILocalizedTextSource text = null)
        {
            _npcs = npcs;
            _text = text;

            RefreshPlates();
        }

        /// <summary>
        /// Points this at the player's quest log, so the marks can follow it.
        /// </summary>
        /// <remarks>Subscribed rather than polled: a mark changes when a quest is taken or
        /// finished, which is a handful of times an hour, and a town of quest givers
        /// re-deciding every frame would be a town that costs a frame.</remarks>
        public void UseJournal(QuestJournal journal)
        {
            if (_journal != null) _journal.Changed -= RefreshMarks;

            _journal = journal;

            if (_journal != null) _journal.Changed += RefreshMarks;

            RefreshMarks();
        }

        /// <summary>Points this at the player, once they exist.</summary>
        public void UsePlayer(CharacterNetworkEntity player)
        {
            if (_player != null) _player.NpcInteractionAnswered -= OnAnswered;

            _player = player;

            if (_player != null) _player.NpcInteractionAnswered += OnAnswered;
        }

        /// <summary>
        /// Adopts every NPC marker in a scene that has just loaded.
        /// </summary>
        /// <remarks>
        /// <b>Found, not created.</b> The five models are authored into Harbor Town; this
        /// walks the scene for the markers on them. Calling it twice does not double
        /// anything -- a marker already adopted is skipped -- because an additively loaded
        /// world scene can be bound more than once.
        /// </remarks>
        public int Adopt(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            var found = 0;

            GameObject[] roots = scene.GetRootGameObjects();

            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;

                WorldNpcMarker[] markers =
                    roots[i].GetComponentsInChildren<WorldNpcMarker>(true);

                for (var m = 0; m < markers.Length; m++)
                {
                    if (Adopt(markers[m])) found++;
                }
            }

            RefreshPlates();
            RefreshMarks();

            return found;
        }

        /// <summary>Takes one marker under management. False if it was already.</summary>
        public bool Adopt(WorldNpcMarker marker)
        {
            if (marker == null || _markers.Contains(marker)) return false;

            marker.EnsureClickable();

            marker.Interaction = OnArrived;

            _markers.Add(marker);

            return true;
        }

        /// <summary>Forgets everything, for a scene that is going away.</summary>
        public void Clear()
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                if (_markers[i] != null) _markers[i].Interaction = null;
            }

            _markers.Clear();
            _plates.Clear();
            _marks.Clear();
        }

        /// <summary>
        /// Writes each NPC's name above its head.
        /// </summary>
        /// <remarks>An NPC whose id resolves to nothing is left blank rather than labelled
        /// with its id: a player seeing "npc.blacksmith" over somebody's head learns only
        /// that the game is broken, and the nameplate hides itself when empty.</remarks>
        public void RefreshPlates()
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                WorldNpcMarker marker = _markers[i];

                if (marker == null) continue;

                if (!_plates.TryGetValue(marker, out CharacterNameplate plate)
                    || plate == null)
                {
                    plate = CharacterNameplate.Create(marker.transform, PlateHeight);

                    _plates[marker] = plate;
                }

                plate.Refresh(LabelFor(marker));
            }
        }

        /// <summary>What is drawn above one NPC.</summary>
        /// <remarks>Name and role on two lines. The role is derived from the capabilities
        /// content authored, so an NPC that stops selling stops saying it does without
        /// anybody editing a second place.</remarks>
        public string LabelFor(WorldNpcMarker marker)
        {
            NPCDefinition npc = DefinitionFor(marker);

            if (npc == null) return string.Empty;

            string name = null;

            if (_text != null && npc.NameKey.IsValid)
            {
                _text.TryGet(npc.NameKey, out name);
            }

            // No translation shipped yet is the ordinary case for this project, so the
            // fallback is the normal path rather than an error one.
            if (string.IsNullOrEmpty(name)) name = FallbackName(npc);

            string role = RoleTag(npc);

            return string.IsNullOrEmpty(role) ? name : name + "\n" + role;
        }

        /// <summary>
        /// The short tag under an NPC's name.
        /// </summary>
        /// <remarks>Derived from <see cref="NPCDefinition.HasRole"/> rather than from the
        /// category, so it says what the NPC can actually do rather than how somebody
        /// classified it. The first match wins: an NPC with two services is labelled by the
        /// one a player came for most often, and the panel lists the rest.</remarks>
        public static string RoleTag(NPCDefinition npc)
        {
            if (npc == null) return string.Empty;

            if (npc.HasRole(NpcRole.Shop)) return "<Shop>";
            if (npc.HasRole(NpcRole.Enhancement)) return "<Blacksmith>";
            if (npc.HasRole(NpcRole.Storage)) return "<Storage>";
            if (npc.HasRole(NpcRole.JobChange)) return "<Job>";
            if (npc.HasRole(NpcRole.Quest)) return "<Guide>";
            if (npc.HasRole(NpcRole.Warp)) return "<Warp>";

            return string.Empty;
        }

        /// <summary>
        /// Which role a click asks for.
        /// </summary>
        /// <remarks>
        /// <b>A guess, and deliberately one the server checks.</b> This picks the service
        /// the NPC advertises so a single click does the obvious thing rather than opening a
        /// menu of one. The request is still validated exactly as though the client had made
        /// the role up, because from the server's side it did.
        ///
        /// Same order as <see cref="RoleTag"/>, so what a player reads above an NPC's head
        /// is what clicking it asks for.
        /// </remarks>
        public static NpcRole PrimaryRole(NPCDefinition npc)
        {
            if (npc == null) return NpcRole.Generic;

            if (npc.HasRole(NpcRole.Shop)) return NpcRole.Shop;
            if (npc.HasRole(NpcRole.Enhancement)) return NpcRole.Enhancement;
            if (npc.HasRole(NpcRole.Storage)) return NpcRole.Storage;
            if (npc.HasRole(NpcRole.JobChange)) return NpcRole.JobChange;
            if (npc.HasRole(NpcRole.Quest)) return NpcRole.Quest;

            return NpcRole.Generic;
        }

        /// <summary>The definition a marker names, or null.</summary>
        public NPCDefinition DefinitionFor(WorldNpcMarker marker)
        {
            if (marker == null || !marker.IsIdentified || _npcs == null) return null;

            return _npcs.TryGet(new DefinitionId(marker.NpcId), out NPCDefinition npc)
                ? npc
                : null;
        }

        /// <summary>
        /// Asks the server whether this player may use this NPC.
        /// </summary>
        /// <remarks>Public so a test can drive it without a pointer, and so a keyboard or a
        /// controller can one day reach the same door the mouse does.</remarks>
        public bool Request(WorldNpcMarker marker)
        {
            if (marker == null || !marker.IsIdentified || _player == null) return false;

            NPCDefinition npc = DefinitionFor(marker);

            // An NPC this client has no content for is one it cannot even name a role on.
            // Asking anyway would spend a round trip to be told what is already known here.
            if (npc == null) return false;

            _sequence++;

            _player.RequestNpcInteraction(marker.NpcId, (int)PrimaryRole(npc), _sequence);

            return true;
        }

        private void OnArrived(WorldNpcMarker marker)
        {
            Request(marker);
        }

        private void OnAnswered(NpcInteractionSnapshot snapshot)
        {
            // A reply to a request older than the one outstanding is a reply to a click the
            // player has already moved on from. Drawing it would reopen a panel they closed.
            if (snapshot.Sequence < _sequence) return;

            LastAnswer = snapshot;

            if (snapshot.Accepted) Authorised?.Invoke(snapshot);
            else Refused?.Invoke(snapshot);
        }

        /// <summary>
        /// A readable name for an NPC with no translation available.
        /// </summary>
        /// <remarks>Built from the id rather than the GameObject, so it is still content
        /// deciding what somebody is called. "npc.general_store_merchant" reads as "General
        /// Store Merchant", which is worse than a translation and far better than a key.</remarks>
        public static string FallbackName(NPCDefinition npc)
        {
            if (npc == null || !npc.Id.IsValid) return string.Empty;

            string id = npc.Id.Value ?? string.Empty;

            int dot = id.LastIndexOf('.');

            if (dot >= 0 && dot + 1 < id.Length) id = id.Substring(dot + 1);

            string[] words = id.Split('_');
            var built = new System.Text.StringBuilder();

            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;

                if (built.Length > 0) built.Append(' ');

                built.Append(char.ToUpperInvariant(words[i][0]));

                if (words[i].Length > 1) built.Append(words[i].Substring(1));
            }

            return built.ToString();
        }

        // ---- the mark above their head ---------------------------------------------------

        /// <summary>
        /// Redraws every quest mark from the player's log.
        /// </summary>
        /// <remarks>The mark is a second floating label rather than a sprite, so it reuses
        /// the nameplate that already billboards correctly and costs the town no new
        /// material, atlas or canvas.</remarks>
        public void RefreshMarks()
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                WorldNpcMarker marker = _markers[i];

                if (marker == null) continue;

                if (!_marks.TryGetValue(marker, out CharacterNameplate mark) || mark == null)
                {
                    mark = CharacterNameplate.Create(marker.transform, MarkHeight);
                    mark.name = "Quest Mark";

                    _marks[marker] = mark;
                }

                mark.Refresh(MarkFor(marker));
            }
        }

        /// <summary>What one NPC's mark reads. Empty hides it.</summary>
        public string MarkFor(WorldNpcMarker marker)
        {
            if (_journal == null) return string.Empty;

            return Glyph(_journal.MarkerFor(DefinitionFor(marker)));
        }

        /// <summary>The character a marker state is drawn as.</summary>
        /// <remarks>Static and tiny so a test can assert the vocabulary without a scene, and
        /// so there is one place that decides what "!" means.</remarks>
        public static string Glyph(QuestMarker marker)
        {
            switch (marker)
            {
                case QuestMarker.Available: return "!";
                case QuestMarker.ReadyToTurnIn: return "?";
                default: return string.Empty;
            }
        }

        private void OnDestroy()
        {
            if (_player != null) _player.NpcInteractionAnswered -= OnAnswered;

            if (_journal != null) _journal.Changed -= RefreshMarks;
        }
    }
}
