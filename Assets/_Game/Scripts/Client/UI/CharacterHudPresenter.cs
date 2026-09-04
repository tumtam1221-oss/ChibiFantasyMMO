using ChibiFantasy.Core;
using ChibiFantasy.Network;

namespace ChibiFantasy.Client.UI
{
    /// <summary>What a heads-up display shows, as values rather than objects.</summary>
    /// <remarks>
    /// Every number here was decided by the server and replicated. Nothing is computed: a
    /// bar fraction is arithmetic on two replicated numbers, and the moment the client works
    /// out a rule of its own -- how much experience the next level needs, say -- it has
    /// started keeping a second copy of the game.
    /// </remarks>
    public readonly struct HudViewData
    {
        private HudViewData(bool bound, CharacterId character, int health, int maxHealth,
            int level, long experience, bool alive)
        {
            IsBound = bound;
            Character = character;
            Health = health;
            MaxHealth = maxHealth;
            Level = level;
            Experience = experience;
            IsAlive = alive;
        }

        /// <summary>Whether a character is being shown at all.</summary>
        /// <remarks>False before entering the world and after a despawn. A screen shows
        /// nothing rather than zeroes, because a health bar reading 0/0 looks like death.</remarks>
        public bool IsBound { get; }

        public CharacterId Character { get; }

        public int Health { get; }

        public int MaxHealth { get; }

        public int Level { get; }

        /// <summary>Progress within the current level, in Phase 05 terms.</summary>
        public long Experience { get; }

        public bool IsAlive { get; }

        /// <summary>Health as a fraction, for a bar. Zero when there is nothing to show.</summary>
        public float HealthFraction => MaxHealth <= 0
            ? 0f
            : (float)Health / MaxHealth;

        /// <summary>Health as a player reads it.</summary>
        public string HealthLabel => IsBound ? Health + " / " + MaxHealth : string.Empty;

        public string LevelLabel => IsBound ? "Lv " + Level : string.Empty;

        /// <summary>
        /// Experience as a bare number.
        /// </summary>
        /// <remarks>
        /// Deliberately not a fraction. Turning it into one needs the cost of the next
        /// level, which is the authored progression curve -- and a client that read that
        /// curve to draw a bar would be one content patch away from showing a different
        /// number than the server. Reported as a limitation rather than guessed at.
        /// </remarks>
        public string ExperienceLabel => IsBound ? "EXP " + Experience : string.Empty;

        public static HudViewData Unbound => default;

        public static HudViewData From(CharacterNetworkEntity entity)
        {
            if (entity == null) return Unbound;

            return new HudViewData(true, entity.Character, entity.Health, entity.MaxHealth,
                entity.Level, entity.Experience, entity.IsAlive);
        }
    }

    /// <summary>
    /// Reads the local player's replicated state for the heads-up display.
    /// </summary>
    /// <remarks>
    /// <b>The local character only.</b> Every character in view has a network object, and
    /// all of them replicate health and level -- that is public information, and a nameplate
    /// over another player will want it. What a HUD shows is <i>this</i> player, so the
    /// binding refuses anything the client does not own.
    ///
    /// <b>Polled from a state read, not from an event.</b> The replicated values are
    /// SyncVars, which do not raise anything this layer can subscribe to; a screen calls
    /// <see cref="Read"/> when it repaints. <see cref="HasChanged"/> exists so it can repaint
    /// only when something actually moved rather than every frame.
    /// </remarks>
    public sealed class CharacterHudPresenter
    {
        private CharacterNetworkEntity _entity;
        private HudViewData _last;

        /// <summary>Whether a local character is bound.</summary>
        public bool IsBound => _entity != null;

        /// <summary>The last values read, for a screen that repaints from cache.</summary>
        public HudViewData Current => _last;

        /// <summary>
        /// Binds the character this client owns.
        /// </summary>
        /// <remarks>Refuses anything else. A HUD showing somebody else's health would be a
        /// privacy question at best and a confusing bug at worst.</remarks>
        public bool Bind(CharacterNetworkEntity entity)
        {
            if (entity == null || !entity.IsOwner)
            {
                Unbind();

                return false;
            }

            _entity = entity;
            _last = HudViewData.From(entity);

            return true;
        }

        /// <summary>Releases the character, for a despawn or a disconnect.</summary>
        /// <remarks>The view data goes back to unbound rather than keeping the last values,
        /// so a disconnected client shows nothing instead of a frozen health bar.</remarks>
        public void Unbind()
        {
            _entity = null;
            _last = HudViewData.Unbound;
        }

        /// <summary>The current values, read from the replicated object.</summary>
        /// <remarks>A destroyed object reads as unbound rather than throwing: a character can
        /// despawn between one frame and the next, and a HUD is not the place to discover
        /// it.</remarks>
        public HudViewData Read()
        {
            if (_entity == null) return _last = HudViewData.Unbound;

            return _last = HudViewData.From(_entity);
        }

        /// <summary>
        /// Whether anything a screen draws has changed since the last read.
        /// </summary>
        /// <remarks>So a HUD repaints on change rather than every frame. Compared field by
        /// field because the replicated values are the whole of what is drawn.</remarks>
        public bool HasChanged()
        {
            HudViewData previous = _last;
            HudViewData current = Read();

            return previous.IsBound != current.IsBound
                || previous.Health != current.Health
                || previous.MaxHealth != current.MaxHealth
                || previous.Level != current.Level
                || previous.Experience != current.Experience
                || previous.Character != current.Character;
        }
    }
}
