using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Network;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// One status effect, as a bar of icons needs it.
    /// </summary>
    /// <remarks>
    /// <b>Presentation, resolved once.</b> The name, the icon and whether this belongs in
    /// the buff row or the debuff row are worked out when a snapshot arrives, not every
    /// frame while it is drawn. Only <see cref="RemainingSeconds"/> moves between snapshots,
    /// and it moves locally.
    /// </remarks>
    public readonly struct StatusEffectViewData
    {
        public StatusEffectViewData(DefinitionId effect, LocalizationKey nameKey,
            string displayName, AssetRef icon, StatusEffectCategory category, int stacks,
            float remainingSeconds, bool isIndefinite)
        {
            Effect = effect;
            NameKey = nameKey;
            DisplayName = displayName;
            Icon = icon;
            Category = category;
            Stacks = stacks < 1 ? 1 : stacks;
            RemainingSeconds = remainingSeconds;
            IsIndefinite = isIndefinite;
        }

        public DefinitionId Effect { get; }

        /// <summary>The authored key, for a screen with a localisation source.</summary>
        public LocalizationKey NameKey { get; }

        /// <summary>What to show when nothing translates the key.</summary>
        public string DisplayName { get; }

        public AssetRef Icon { get; }

        public StatusEffectCategory Category { get; }

        public int Stacks { get; }

        /// <summary>Seconds left, or zero or less for an effect that does not expire.</summary>
        public float RemainingSeconds { get; }

        public bool HasIcon => Icon.IsValid;

        /// <summary>
        /// Whether this effect expires at all.
        /// </summary>
        /// <remarks>
        /// Recorded when the snapshot arrived rather than derived from the number that is
        /// left. Deriving it would mean a perfectly ordinary debuff becomes "permanent" the
        /// instant its countdown reaches zero -- and so loses its timer at exactly the
        /// moment a player is watching it.
        /// </remarks>
        public bool IsIndefinite { get; }

        /// <summary>Stacks are shown only when there is more than one.</summary>
        /// <remarks>"x1" on every icon is noise on a bar a player reads at a glance.</remarks>
        public bool ShowStacks => Stacks > 1;

        /// <summary>
        /// Which row this belongs in.
        /// </summary>
        /// <remarks>
        /// Buffs and heals-over-time are good; debuffs, damage-over-time and control are
        /// not. An effect whose category this build does not recognise is drawn as a debuff,
        /// because being warned about something harmless is a smaller failure than not being
        /// warned about a poison.
        /// </remarks>
        public bool IsBeneficial => Category == StatusEffectCategory.Buff
            || Category == StatusEffectCategory.HealOverTime;

        /// <summary>The countdown, as a bar shows it.</summary>
        /// <remarks>Whole seconds under a minute, then minutes. Nothing below zero: the
        /// server has not removed it yet, so it still reads as about to end rather than as
        /// gone.</remarks>
        public string RemainingLabel
        {
            get
            {
                if (IsIndefinite) return string.Empty;

                int seconds = RemainingSeconds < 0f ? 0 : (int)RemainingSeconds;

                return seconds < 60
                    ? seconds + "s"
                    : (seconds / 60) + "m" + (seconds % 60).ToString("00");
            }
        }

        public override string ToString()
        {
            return DisplayName + (ShowStacks ? " x" + Stacks : string.Empty);
        }
    }

    /// <summary>
    /// Turns the server's status snapshot into something a bar can draw.
    /// </summary>
    /// <remarks>
    /// <b>It holds no status of its own.</b> What is on a character is the snapshot the
    /// server last sent, replaced wholesale each time. There is no add, no remove and no
    /// merge here, which is what makes it impossible for this to disagree with the server.
    ///
    /// <b>The countdown is drawn, not decided.</b> <see cref="Advance"/> reduces the numbers
    /// on screen so a player sees a timer move between packets. A number reaching zero
    /// removes nothing: the entry stays until the server says the effect is gone. That is
    /// the whole distinction between a countdown and an expiry, and getting it backwards
    /// would let a client with a fast clock walk out of a silence.
    ///
    /// <b>Definitions are looked up locally.</b> The wire carries an id; the name and the
    /// icon are authored content this client already has. An effect this build cannot
    /// resolve is still shown, named from its id, because a player carrying something
    /// unnamed is better informed than a player shown nothing.
    /// </remarks>
    public sealed class StatusEffectPresenter
    {
        private readonly List<StatusEffectViewData> _buffs = new List<StatusEffectViewData>();
        private readonly List<StatusEffectViewData> _debuffs = new List<StatusEffectViewData>();

        private readonly IDefinitionRegistry<StatusEffectDefinition> _effects;

        private CharacterNetworkEntity _entity;

        public StatusEffectPresenter(IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            _effects = effects;
        }

        /// <summary>The beneficial effects, in the order the server sent them.</summary>
        public IReadOnlyList<StatusEffectViewData> Buffs => _buffs;

        /// <summary>The harmful ones.</summary>
        public IReadOnlyList<StatusEffectViewData> Debuffs => _debuffs;

        /// <summary>Whose status this is.</summary>
        public CharacterId Character { get; private set; }

        /// <summary>The revision of the last snapshot taken.</summary>
        public int Revision { get; private set; }

        /// <summary>Whether a snapshot has arrived at all.</summary>
        /// <remarks>Distinct from "no effects": a bar showing nothing because the character
        /// is unbuffed and a bar showing nothing because nothing has arrived are different
        /// states, and only one of them is worth saying out loud.</remarks>
        public bool HasSnapshot { get; private set; }

        public int Count => _buffs.Count + _debuffs.Count;

        /// <summary>Raised when a snapshot replaced what was there.</summary>
        public event System.Action Changed;

        /// <summary>
        /// Binds the character this client owns.
        /// </summary>
        /// <remarks>Refuses anything else. Somebody else's buffs are not sent to this
        /// client, so a presenter bound to a remote character would draw an empty bar and
        /// imply they have none -- which is worse than not drawing one.</remarks>
        public bool Bind(CharacterNetworkEntity entity)
        {
            Unbind();

            if (entity == null || !entity.IsOwner) return false;

            _entity = entity;
            _entity.StatusChanged += OnStatusChanged;

            Character = entity.Character;

            // A snapshot that arrived before this bar existed is still the current one.
            if (entity.Status.Count > 0 || !string.IsNullOrEmpty(entity.Status.CharacterId))
            {
                OnStatusChanged(entity.Status);
            }

            return true;
        }

        public void Unbind()
        {
            if (_entity != null) _entity.StatusChanged -= OnStatusChanged;

            _entity = null;

            _buffs.Clear();
            _debuffs.Clear();

            Character = default;
            Revision = 0;
            HasSnapshot = false;
        }

        /// <summary>
        /// Moves the countdowns on, for display.
        /// </summary>
        /// <remarks>
        /// <b>Nothing is removed here, ever.</b> An entry that reaches zero stays exactly
        /// where it is until the server sends a snapshot without it. A client that dropped
        /// an effect when its own timer ran out would be a client deciding when a silence
        /// ends, and it would decide it early on a machine whose clock runs fast.
        /// </remarks>
        public void Advance(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            Countdown(_buffs, deltaSeconds);
            Countdown(_debuffs, deltaSeconds);
        }

        private static void Countdown(List<StatusEffectViewData> entries, float deltaSeconds)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                StatusEffectViewData entry = entries[i];

                if (entry.IsIndefinite) continue;

                float remaining = entry.RemainingSeconds - deltaSeconds;

                entries[i] = new StatusEffectViewData(entry.Effect, entry.NameKey,
                    entry.DisplayName, entry.Icon, entry.Category, entry.Stacks,
                    remaining < 0f ? 0f : remaining, false);
            }
        }

        /// <summary>Rebuilds both rows from a snapshot, discarding what was there.</summary>
        private void OnStatusChanged(StatusSnapshot snapshot)
        {
            _buffs.Clear();
            _debuffs.Clear();

            Revision = snapshot.Revision;
            HasSnapshot = true;

            if (!string.IsNullOrEmpty(snapshot.CharacterId))
            {
                Character = new CharacterId(snapshot.CharacterId);
            }

            if (snapshot.Effects != null)
            {
                for (int i = 0; i < snapshot.Effects.Length; i++)
                {
                    StatusEffectViewData view = Project(snapshot.Effects[i]);

                    if (view.IsBeneficial) _buffs.Add(view);
                    else _debuffs.Add(view);
                }
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// One replicated entry, joined to the content this client already has.
        /// </summary>
        /// <remarks>The category the server sent wins over the local definition's. They
        /// normally agree; when they do not, the server is the one that decided.</remarks>
        private StatusEffectViewData Project(in StatusEffectSnapshot entry)
        {
            var effect = new DefinitionId(entry.EffectId);
            var category = (StatusEffectCategory)entry.Category;

            StatusEffectDefinition definition = null;

            _effects?.TryGet(effect, out definition);

            if (definition == null)
            {
                // Content this build does not have. Named from the id rather than hidden.
                return new StatusEffectViewData(effect, default, FallbackName(entry.EffectId),
                    default, category, entry.Stacks, entry.RemainingSeconds,
                    entry.IsIndefinite);
            }

            return new StatusEffectViewData(effect, definition.NameKey,
                NameOf(definition, entry.EffectId), definition.Icon,
                category == StatusEffectCategory.None ? definition.Category : category,
                entry.Stacks, entry.RemainingSeconds, entry.IsIndefinite);
        }

        /// <summary>The authored name if there is one, otherwise something readable.</summary>
        /// <remarks>The localisation key is carried on the view data for a screen with a
        /// text source; this is what is drawn when there is none, which is every build until
        /// localisation data exists.</remarks>
        private static string NameOf(StatusEffectDefinition definition, string id)
        {
            string key = definition.NameKey.Key;

            return string.IsNullOrEmpty(key) ? FallbackName(id) : key;
        }

        /// <summary>
        /// A readable name derived from an effect id.
        /// </summary>
        /// <remarks>"status.silence" becomes "Silence". A raw content id on a player's
        /// screen looks like a bug even when it is not, and the last segment of an id is
        /// almost always the word a player would use for it anyway.</remarks>
        public static string FallbackName(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return "?";

            int dot = effectId.LastIndexOf('.');
            string tail = dot >= 0 && dot < effectId.Length - 1
                ? effectId.Substring(dot + 1)
                : effectId;

            if (tail.Length == 0) return "?";

            return char.ToUpperInvariant(tail[0]) + tail.Substring(1);
        }
    }
}
