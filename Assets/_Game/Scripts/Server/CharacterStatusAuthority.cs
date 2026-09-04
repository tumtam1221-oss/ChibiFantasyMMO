using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Runs every character's status clock and tells each owner what is on them.
    /// </summary>
    /// <remarks>
    /// <b>It owns no status.</b> The list lives on <see cref="LivingCharacter.Status"/>,
    /// which is Phase 12's runtime; the rules live in <c>StatusEffectService</c>. What is
    /// here is the two things neither of those can do on a server: advance the clock, and
    /// decide when a client is worth telling. A second status container would have been the
    /// easy mistake and would have started disagreeing with the first the moment one of them
    /// was ticked.
    ///
    /// <b>Expiry is the server's clock and nobody else's.</b> <see cref="Tick"/> takes
    /// elapsed seconds from the world loop, exactly as the movement and combat authorities
    /// do. A client counts a number down to draw it; when it reaches zero on screen, nothing
    /// has happened yet -- the effect ends when this says it ends, and the removal is
    /// replicated like any other change.
    ///
    /// <b>Nothing is sent on an unchanged frame.</b> The runtime's own revision already
    /// advances on exactly the changes worth knowing about, so this compares one integer per
    /// character per tick and sends nothing the rest of the time. That is what keeps a
    /// countdown from becoming a packet per second per player.
    ///
    /// <b>Owner-scoped, always.</b> Every publish is addressed to the connection that owns
    /// the character. There is no path here that sends one player's effects to another.
    /// </remarks>
    public sealed class CharacterStatusAuthority : ICharacterStatusSource
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly IDefinitionRegistry<StatusEffectDefinition> _effects;
        private readonly CharacterReplicationService _replication;

        /// <summary>
        /// What each character was last sent, per effect.
        /// </summary>
        /// <remarks>
        /// <b>Not the runtime's revision, deliberately.</b> That revision advances on every
        /// tick that has any effect at all -- including a tick where an indefinite passive
        /// did not move -- so publishing on it would send a packet per tick per player and
        /// turn a countdown into network traffic. What is compared instead is what a bar
        /// would actually draw differently.
        ///
        /// Keyed by character id rather than by connection, so a reconnect on a new
        /// connection is a fresh character with no stale entry to confuse it.
        /// </remarks>
        private readonly Dictionary<string, List<StatusEffectSnapshot>> _published =
            new Dictionary<string, List<StatusEffectSnapshot>>();

        private readonly List<StatusEffectSnapshot> _scratch =
            new List<StatusEffectSnapshot>();

        public CharacterStatusAuthority(WorldCharacterRegistry characters,
            IDefinitionRegistry<StatusEffectDefinition> effects,
            CharacterReplicationService replication = null)
        {
            _characters = characters;
            _effects = effects;
            _replication = replication;
        }

        /// <summary>How many effects have expired since this authority was built.</summary>
        public int Expired { get; private set; }

        /// <summary>How many snapshots have actually been sent.</summary>
        /// <remarks>For diagnostics and for the test that proves an unchanged frame sends
        /// nothing.</remarks>
        public int Published { get; private set; }

        /// <summary>
        /// Advances every character's status clock and publishes what changed.
        /// </summary>
        /// <remarks>Time arrives as an argument, matching every other authority in this
        /// assembly. Nothing here reads a clock, which is what makes an expiry reproducible
        /// in a test rather than a matter of how long the test took to run.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (_characters == null) return;

            IReadOnlyList<LivingCharacter> all = _characters.All();

            for (int i = 0; i < all.Count; i++)
            {
                LivingCharacter character = all[i];

                if (character?.Status == null) continue;

                if (deltaSeconds > 0f) Expired += character.Status.Tick(deltaSeconds);
            }

            PublishChanged();
        }

        /// <summary>
        /// Sends a fresh snapshot to every owner whose status has moved.
        /// </summary>
        /// <remarks>Public so a caller that changed a status outside the tick -- a skill
        /// landing a debuff, a fruit granting a passive -- can push the result immediately
        /// rather than leaving the player looking at a stale bar until the next frame.</remarks>
        public int PublishChanged()
        {
            if (_characters == null) return 0;

            IReadOnlyList<LivingCharacter> all = _characters.All();

            var sent = 0;

            for (int i = 0; i < all.Count; i++)
            {
                if (PublishIfChanged(all[i])) sent++;
            }

            return sent;
        }

        /// <summary>Sends one character's status, but only if it differs from the last sent.</summary>
        public bool PublishIfChanged(LivingCharacter character)
        {
            if (character?.Status == null) return false;

            StatusSnapshot snapshot = Build(character);

            return Differs(character.Character.Value, snapshot) && Send(character, snapshot);
        }

        /// <summary>
        /// Whether a snapshot is worth a packet.
        /// </summary>
        /// <remarks>
        /// <b>A shrinking timer is not news.</b> The client is already counting the number
        /// down for display and arrives at the same answer without being told, so an effect
        /// that only lost time since the last send changes nothing a player can see that
        /// they were not going to see anyway.
        ///
        /// <b>A growing one is.</b> Time going up is a refresh -- somebody re-applied
        /// something -- and a bar whose timer silently failed to jump back up would be
        /// telling the player their buff is about to end when it is not.
        ///
        /// Everything else -- an effect appearing, disappearing, changing stacks or changing
        /// category -- is a difference in what is drawn, and is sent.
        /// </remarks>
        private bool Differs(string character, in StatusSnapshot snapshot)
        {
            if (!_published.TryGetValue(character, out List<StatusEffectSnapshot> last))
            {
                return true;
            }

            int count = snapshot.Effects?.Length ?? 0;

            if (count != last.Count) return true;

            for (var i = 0; i < count; i++)
            {
                StatusEffectSnapshot now = snapshot.Effects[i];
                StatusEffectSnapshot then = last[i];

                if (now.EffectId != then.EffectId) return true;
                if (now.Stacks != then.Stacks) return true;
                if (now.Category != then.Category) return true;
                if (now.IsIndefinite != then.IsIndefinite) return true;

                // Refreshed. A tolerance, because a duration re-applied to the same value
                // through floating point arithmetic is not a refresh.
                if (now.RemainingSeconds > then.RemainingSeconds + 0.001f) return true;
            }

            return false;
        }

        private void Remember(string character, in StatusSnapshot snapshot)
        {
            if (!_published.TryGetValue(character, out List<StatusEffectSnapshot> last))
            {
                last = new List<StatusEffectSnapshot>();
                _published[character] = last;
            }

            last.Clear();

            if (snapshot.Effects == null) return;

            last.AddRange(snapshot.Effects);
        }

        /// <summary>Sends one character's status regardless of whether it changed.</summary>
        /// <remarks>What a rebind uses. A client that just received a new character object
        /// has nothing, so "unchanged since last time" is the wrong question to ask.</remarks>
        public bool Publish(LivingCharacter character)
        {
            if (character?.Status == null) return false;

            return Send(character, Build(character));
        }

        private bool Send(LivingCharacter character, in StatusSnapshot snapshot)
        {
            if (_replication == null) return false;

            if (!_replication.TryGet(character.Character, out FishNet.Object.NetworkObject obj)
                || obj == null)
            {
                return false;
            }

            var entity = obj.GetComponent<CharacterNetworkEntity>();

            if (entity == null) return false;

            entity.ServerPublishStatus(snapshot);

            Remember(character.Character.Value, snapshot);

            Published++;

            return true;
        }

        /// <summary>Forgets what a character was last sent, for a despawn.</summary>
        /// <remarks>So a reconnect is told its status rather than skipped as unchanged --
        /// the new client has an empty bar and the revision may well be the same number.</remarks>
        public bool Forget(CharacterId character)
        {
            return character.IsValid && _published.Remove(character.Value);
        }

        // ---- ICharacterStatusSource ------------------------------------------------------

        /// <summary>
        /// Builds the first snapshot for a connection, at the moment it can receive one.
        /// </summary>
        /// <remarks>Called from the entity's spawn callback rather than at spawn time. See
        /// that method for why: a target message before the recipient observes the object is
        /// discarded with a warning and no error.</remarks>
        public bool TryBuildStatusSnapshot(int clientId, out StatusSnapshot snapshot)
        {
            snapshot = default;

            if (_characters == null) return false;

            if (!_characters.TryGet(clientId, out LivingCharacter character)) return false;

            snapshot = Build(character);

            Remember(character.Character.Value, snapshot);

            return true;
        }

        /// <summary>
        /// Projects the authoritative list onto what a client may see.
        /// </summary>
        /// <remarks>
        /// <b>The source never travels.</b> <c>ActiveStatusEffect.Source</c> is what granted
        /// an effect and exists so the server can take back exactly what it gave. Which
        /// hidden mechanism buffed a player is server business, no icon needs it, and it is
        /// dropped here rather than trimmed later.
        ///
        /// <b>The category is resolved, not guessed.</b> Read off the authored definition on
        /// the server, so a client whose content is missing an effect still knows which row
        /// to draw it in. An effect this server cannot resolve is still sent -- a player
        /// carrying something is entitled to see that they are carrying it, and a silently
        /// dropped debuff is worse than an unnamed one.
        /// </remarks>
        public StatusSnapshot Build(LivingCharacter character)
        {
            return character == null
                ? default
                : Build(character.Character, character.Status);
        }

        /// <summary>The same projection, from the two things it actually needs.</summary>
        /// <remarks>Split out so the projection can be exercised on a status list directly,
        /// without standing a whole world up around it. The overload above is what the
        /// server calls.</remarks>
        public StatusSnapshot Build(CharacterId character, StatusEffectRuntimeState status)
        {
            var snapshot = new StatusSnapshot
            {
                CharacterId = character.Value ?? string.Empty,
                Revision = status == null ? 0 : status.Revision.Value,
                Effects = System.Array.Empty<StatusEffectSnapshot>(),
            };

            if (status == null) return snapshot;

            _scratch.Clear();

            IReadOnlyList<ActiveStatusEffect> active = status.Active;

            for (int i = 0; i < active.Count; i++)
            {
                ActiveStatusEffect effect = active[i];

                _scratch.Add(new StatusEffectSnapshot
                {
                    EffectId = effect.Effect.Value ?? string.Empty,
                    Stacks = effect.Stacks,
                    RemainingSeconds = effect.RemainingSeconds,
                    Category = (int)CategoryOf(effect.Effect),
                });
            }

            snapshot.Effects = _scratch.ToArray();

            return snapshot;
        }

        private StatusEffectCategory CategoryOf(DefinitionId effect)
        {
            if (_effects == null) return StatusEffectCategory.None;

            return _effects.TryGet(effect, out StatusEffectDefinition definition)
                && definition != null
                ? definition.Category
                : StatusEffectCategory.None;
        }
    }
}
