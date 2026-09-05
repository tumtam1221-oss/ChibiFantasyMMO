using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Gameplay;
using ChibiFantasy.Network;

namespace ChibiFantasy.Server
{
    /// <summary>
    /// Turns a client's "pick that up" into the loot registry's own answer.
    /// </summary>
    /// <remarks>
    /// <b>A door, not a second loot system.</b> Every decision below already existed in
    /// <see cref="MonsterLootRegistry"/> and <see cref="LootPickupService"/>: whether the
    /// pile is still there, whether it was already taken, whether the taker is eligible,
    /// whether the bag has room, and whether the character was saved afterwards. This adds
    /// the two things a networked player needs and a local test never did -- an identity to
    /// name, and a distance they must walk.
    ///
    /// <b>Reach is checked here because only here is it known.</b> The pile knows where it
    /// is and the character knows where they are; the pickup service is given neither, and
    /// giving it a position would push a movement concern into an item rule. A player who
    /// can loot from anywhere on the map does not have to play the game.
    ///
    /// <b>Sequence numbers are the replay defence, exactly as inventory does it.</b> A
    /// repeated or delayed request is refused before it reaches the registry, so a duplicate
    /// packet cannot take a second copy of a thing there is only one of.
    /// </remarks>
    public sealed class CharacterLootAuthority : ICharacterLootRequestSink
    {
        private readonly WorldCharacterRegistry _characters;
        private readonly MonsterLootRegistry _loot;
        private readonly CharacterReplicationService _replication;
        private readonly float _reachMetres;

        private readonly Dictionary<string, long> _sequences = new Dictionary<string, long>();

        public CharacterLootAuthority(WorldCharacterRegistry characters,
            MonsterLootRegistry loot, CharacterReplicationService replication = null,
            float reachMetres = 4f)
        {
            _characters = characters;
            _loot = loot;
            _replication = replication;
            _reachMetres = reachMetres <= 0f ? 4f : reachMetres;
        }

        /// <summary>How many requests were handled, accepted or not. For diagnostics.</summary>
        public int Handled { get; private set; }

        /// <summary>What the last request did.</summary>
        public LootPickupOutcome LastResult { get; private set; }

        public void Submit(int connectionId, string lootId, int index, long sequence)
        {
            Handled++;

            LastResult = Apply(connectionId, lootId, index, sequence);
        }

        /// <summary>Resolves the request against the world, and says what happened.</summary>
        public LootPickupOutcome Apply(int connectionId, string lootId, int index,
            long sequence)
        {
            if (_characters == null || _loot == null)
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.MissingContext);
            }

            if (!_characters.TryGet(connectionId, out LivingCharacter taker))
            {
                // The identity is the connection's. Nothing in the request says who is
                // asking, so nobody can ask on somebody else's behalf.
                return LootPickupOutcome.Rejected(LootPickupRejection.NotEligible);
            }

            var loot = new InstanceId(lootId ?? string.Empty);

            if (!loot.IsValid)
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.AlreadyTaken);
            }

            string key = taker.Character.Value;

            if (_sequences.TryGetValue(key, out long last) && sequence <= last)
            {
                // A replayed or out-of-order request. Refused before it can take anything.
                return LootPickupOutcome.Rejected(LootPickupRejection.AlreadyTaken, loot);
            }

            if (!_loot.TryGet(loot, out LootObjectState pile))
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.AlreadyTaken, loot);
            }

            if (!IsWithinReach(taker, pile))
            {
                return LootPickupOutcome.Rejected(LootPickupRejection.NotEligible, loot);
            }

            LootPickupOutcome outcome = _loot.Pickup(loot, index, taker.Character);

            // Only an accepted request consumes the sequence, so a player refused for being
            // too far away can walk closer and ask again.
            if (outcome.IsAccepted)
            {
                _sequences[key] = sequence;

                Publish(taker);
            }

            return outcome;
        }

        /// <summary>Whether the character is close enough to reach the pile.</summary>
        private bool IsWithinReach(LivingCharacter taker, LootObjectState pile)
        {
            CombatPosition from = taker.Combatant.Position;
            CombatPosition to = pile.Position;

            return from.SqrDistanceTo(to) <= _reachMetres * _reachMetres;
        }

        public bool TryBuildLootSnapshot(int connectionId, out LootSnapshot snapshot)
        {
            snapshot = default;

            if (_characters == null || _loot == null) return false;

            if (!_characters.TryGet(connectionId, out LivingCharacter character)) return false;

            snapshot = SnapshotFor(character);

            return true;
        }

        /// <summary>
        /// What this character can see on the ground.
        /// </summary>
        /// <remarks>Scoped to their own map, because a pile on another map is one they could
        /// not take even if they knew about it -- and telling them is telling them where a
        /// boss died somewhere they are not.</remarks>
        public LootSnapshot SnapshotFor(LivingCharacter character)
        {
            var entries = new List<LootEntrySnapshot>();

            IReadOnlyList<LootObjectState> piles = _loot.All();

            for (var i = 0; i < piles.Count; i++)
            {
                LootObjectState pile = piles[i];

                if (pile == null || pile.IsEmpty) continue;

                if (!_loot.TryGetMap(pile.LootId, out DefinitionId map)) continue;

                if (character.Location != null && map != character.Location.CurrentMap)
                {
                    continue;
                }

                IReadOnlyList<LootResult> contents = pile.Contents;

                for (var c = 0; c < contents.Count; c++)
                {
                    if (contents[c].Quantity <= 0) continue;

                    entries.Add(new LootEntrySnapshot
                    {
                        LootId = pile.LootId.Value,
                        Index = c,
                        ItemId = contents[c].Item.Value,
                        Quantity = contents[c].Quantity,
                        X = pile.Position.X,
                        Y = pile.Position.Y,
                        Z = pile.Position.Z,
                    });
                }
            }

            return new LootSnapshot
            {
                CharacterId = character.Character.Value,
                Entries = entries.ToArray(),
            };
        }

        /// <summary>Tells one player what the ground looks like now.</summary>
        public bool Publish(LivingCharacter character)
        {
            if (character == null || _replication == null) return false;

            if (!_replication.TryGet(character.Character,
                out FishNet.Object.NetworkObject networkObject))
            {
                return false;
            }

            var entity = networkObject == null
                ? null
                : networkObject.GetComponent<CharacterNetworkEntity>();

            if (entity == null) return false;

            entity.ServerPublishLoot(SnapshotFor(character));

            return true;
        }

        /// <summary>Tells everybody in the world, after something changed the ground.</summary>
        public int PublishAll()
        {
            if (_characters == null) return 0;

            IReadOnlyList<LivingCharacter> all = _characters.All();

            var published = 0;

            for (var i = 0; i < all.Count; i++)
            {
                if (Publish(all[i])) published++;
            }

            return published;
        }

        /// <summary>Forgets a character's replay history when they leave.</summary>
        public bool Forget(CharacterId character)
        {
            return character.IsValid && _sequences.Remove(character.Value);
        }
    }
}
