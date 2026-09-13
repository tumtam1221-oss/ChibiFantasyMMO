namespace ChibiFantasy.Client.World
{
    /// <summary>One blow the picture is about to draw.</summary>
    public readonly struct DrawnHit
    {
        public DrawnHit(int amount, int healthAfter, bool kills)
        {
            Amount = amount;
            HealthAfter = healthAfter;
            Kills = kills;
        }

        /// <summary>Health the blow took, as the server reported it.</summary>
        public int Amount { get; }

        /// <summary>Health left afterwards, never below zero.</summary>
        public int HealthAfter { get; }

        /// <summary>Whether this is the blow that brought the target down.</summary>
        public bool Kills { get; }
    }

    /// <summary>
    /// Holds a monster's authoritative health changes back until the moment the picture
    /// should show them.
    /// </summary>
    /// <remarks>
    /// <b>Why anything is held back at all.</b> The server applies a basic attack's damage
    /// the instant it accepts the swing, and every client hears about the swing and the new
    /// health in the same tick. Drawn immediately, the target flinches, its number appears
    /// and its bar drops while the attacker's blade is still being drawn back -- a third of
    /// a second before anything touches it. This queue is the difference between that and a
    /// blow that lands when it looks like it lands.
    ///
    /// <b>What it never does.</b> It never invents a number: every amount is the difference
    /// between two values the server wrote, and every one is drawn exactly once, in order.
    /// It never loses one: when the queue is full the newest blow is folded into the one
    /// before it, so the total shown is always the total taken. It never delays a rise: a respawn or a heal has no swing to wait for and is
    /// shown at once, and anything still queued about the old health is discarded with it.
    /// And once it has shown the monster dead, nothing it is later handed can make it
    /// flinch or stand up -- Death outranks Hit, permanently, on the presentation side as
    /// well as in the animator's graph.
    ///
    /// <b>Plain C#, on purpose.</b> Time arrives as an argument and nothing here touches the
    /// engine, so every rule above is asserted in a test that spawns nothing.
    /// </remarks>
    public sealed class MonsterHitQueue
    {
        /// <summary>How many blows may wait at once before new ones are folded together.</summary>
        public const int Capacity = 4;

        private struct Pending
        {
            public float DueTime;
            public int Amount;
            public int HealthAfter;
        }

        private readonly Pending[] _pending = new Pending[Capacity];
        private int _count;
        private int _lastSeen;

        public MonsterHitQueue(int initialHealth)
        {
            _lastSeen = initialHealth;
            ShownHealth = initialHealth < 0 ? 0 : initialHealth;
            ShownDead = initialHealth <= 0;
        }

        /// <summary>The health the picture is currently showing. Never negative.</summary>
        public int ShownHealth { get; private set; }

        /// <summary>Whether the picture is currently showing the monster dead.</summary>
        public bool ShownDead { get; private set; }

        /// <summary>Blows waiting to be drawn.</summary>
        public int PendingCount => _count;

        /// <summary>
        /// Notices the health the server is reporting this frame and files any change.
        /// </summary>
        /// <param name="serverHealth">The replicated value, as is.</param>
        /// <param name="now">The presentation clock.</param>
        /// <param name="delay">How long a drop waits before it is drawn.</param>
        /// <returns>Whether a rise was applied immediately.</returns>
        public bool Observe(int serverHealth, float now, float delay)
        {
            if (serverHealth == _lastSeen) return false;

            int before = _lastSeen;

            _lastSeen = serverHealth;

            if (serverHealth > before)
            {
                _count = 0;
                ShownHealth = serverHealth;
                ShownDead = serverHealth <= 0;

                return true;
            }

            if (_count >= Capacity)
            {
                // Full. The newest blow joins the one behind it rather than being lost or
                // pushing an older one out early: one combined number for two blows is
                // honest about the total, and it only ever happens under a pile-on.
                _pending[_count - 1].Amount += before - serverHealth;
                _pending[_count - 1].HealthAfter = serverHealth < 0 ? 0 : serverHealth;

                return false;
            }

            _pending[_count++] = new Pending
            {
                DueTime = now + (delay > 0f ? delay : 0f),
                Amount = before - serverHealth,
                HealthAfter = serverHealth < 0 ? 0 : serverHealth,
            };

            return false;
        }

        /// <summary>
        /// Takes the next blow whose moment has come, if any, applying it to the shown
        /// health.
        /// </summary>
        /// <returns>False when nothing is due. A blow taken after the monster was already
        /// shown dead is applied to the health but reported with a zero amount, so the
        /// caller draws nothing for it.</returns>
        public bool TryDraw(float now, out DrawnHit hit)
        {
            if (_count == 0 || now < _pending[0].DueTime)
            {
                hit = default;

                return false;
            }

            hit = Take();

            return true;
        }

        private DrawnHit Take()
        {
            Pending next = _pending[0];

            Shift();

            bool wasDead = ShownDead;

            ShownHealth = next.HealthAfter;
            ShownDead = next.HealthAfter <= 0;

            // a corpse takes no more blows worth drawing
            int amount = wasDead ? 0 : next.Amount;

            return new DrawnHit(amount, next.HealthAfter, !wasDead && ShownDead);
        }

        private void Shift()
        {
            if (_count == 0) return;

            for (var i = 0; i < _count - 1; i++) _pending[i] = _pending[i + 1];

            _count--;
        }
    }
}
