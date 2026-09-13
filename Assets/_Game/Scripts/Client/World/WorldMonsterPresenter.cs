using System.Collections.Generic;
using ChibiFantasy.Core;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Puts the approved model on an authoritative monster, and animates it.
    /// </summary>
    /// <remarks>
    /// <b>It presents; it decides nothing.</b> Position, health and existence all come from
    /// <see cref="MonsterNetworkEntity"/>, which the server writes. Nothing here moves a
    /// monster, damages one, or decides one has died -- it reads those and picks an
    /// animation. A monster the server has not sent does not appear, and one the server has
    /// taken away goes with it.
    ///
    /// <b>Art comes from the catalogue, keyed by definition id.</b> No file compares a
    /// GameObject's name to decide what a monster is. A monster with no authored model draws
    /// nothing here and is left to the development placeholder, which is the honest split:
    /// "finished" and "not started" should not look the same.
    ///
    /// <b>Walking is inferred, not replicated.</b> The server sends positions, not a state
    /// machine, so movement is the only thing this can read a walk from: it watches the
    /// position it was given and decides the monster is moving when it changes. That keeps
    /// the presentation honest about what the world actually says, and means an AI added in
    /// a later gate needs no new wire.
    ///
    /// <b>The model is a child of the monster it stands for.</b> That is what makes a
    /// shipped client able to click one. Targeting resolves a collider with
    /// <c>GetComponentInParent&lt;MonsterNetworkEntity&gt;</c>, so hanging the model under
    /// the network object puts a real monster under the cursor through the path that already
    /// existed -- without touching <c>WorldPointerInput</c> or <c>WorldCombatInput</c>, and
    /// without relying on the development marker, which a player's build does not compile.
    ///
    /// <b>Names come from content and health from the server.</b> Neither is written here.
    /// The plate reads the authored name key through the language service, exactly as an
    /// NPC's does, and falls back to a readable form of the definition id rather than to a
    /// hard-coded string -- so a monster added tomorrow is labelled without this file
    /// changing.
    /// </remarks>
    public sealed class WorldMonsterPresenter : MonoBehaviour
    {
        /// <summary>Metres per second above which a monster is presented as moving.</summary>
        /// <remarks>Above snapshot jitter and below any real walk. A monster standing still
        /// that flickered into its hop animation would read as broken.</remarks>
        private const float MoveThreshold = 0.08f;

        /// <summary>How long a walk keeps playing after the last movement was seen.</summary>
        /// <remarks>Positions arrive in snapshots, not every frame. Without a little
        /// persistence the hop would stutter between Move and Idle at the snapshot rate --
        /// the same reasoning the player's own locomotion smoothing already uses.</remarks>
        private const float MoveMemorySeconds = 0.25f;

        /// <summary>How quickly the model closes the gap to the position the server sent.</summary>
        /// <remarks>
        /// Monster positions replicate at the synchroniser's rate, not the frame rate, so the
        /// authoritative position arrives as a series of jumps. Snapping the model to each
        /// one makes a strolling monster stutter across the ground; easing toward it makes
        /// the same stream of positions read as a walk.
        ///
        /// This smooths <i>presentation only</i>. Nothing here is ever read back as a
        /// position: the follow speed, the facing and the animation all come from the
        /// authoritative values, so a smoothed model cannot disagree with the server about
        /// where its monster is.
        /// </remarks>
        private const float FollowSharpness = 14f;

        /// <summary>Beyond this the model is moved rather than eased.</summary>
        /// <remarks>A monster that respawned, was teleported home by its leash, or appeared
        /// after a lost snapshot should not be seen gliding across the map to get there.
        /// </remarks>
        private const float TeleportDistance = 3f;

        /// <summary>How far below the name the health bar hangs.</summary>
        /// <remarks>Public because the authored nameplate height has to clear the model by
        /// at least this much, or the bar is drawn inside the monster -- and that is a thing
        /// a test can check against the catalogue rather than a thing somebody has to
        /// remember.</remarks>
        public const float BarDrop = 0.16f;

        /// <summary>How wide and tall the bar is drawn, in metres.</summary>
        private const float BarWidth = 0.34f;

        private const float BarHeight = 0.045f;

        /// <summary>
        /// How large a monster's name is drawn, in world units.
        /// </summary>
        /// <remarks>A third of the size a player's nameplate uses. A monster's name says
        /// what the thing under the cursor is; a player's has to be legible across a plaza.
        /// Drawn in world units, so it shrinks with distance like the monster does and is
        /// not pinned to one screen resolution.</remarks>
        private const float NameFontSize = 0.8f;

        /// <summary>How far one hop carries a monster with nothing authored, in metres.</summary>
        private const float DefaultHopMetres = 0.45f;

        /// <summary>Bounds on how far the hop may be sped up or slowed down.</summary>
        /// <remarks>Outside these it stops reading as the same creature: too slow and it
        /// hangs in the air, too fast and the squash never resolves.</remarks>
        private const float SlowestHop = 0.65f;

        private const float FastestHop = 1.9f;

        private sealed class Shown
        {
            public GameObject Visual;
            public Animator Animator;
            public CharacterNameplate Plate;
            public Transform BarFill;
            public int BarHealth = -1;
            public MonsterNetworkEntity Monster;
            public Vector3 LastPosition;
            public float LastChangeTime;
            public float MovingUntil;
            public float ObservedSpeed;
            public float HopMetres;
            public int LastHealth;
            public int LastSwings;
            public bool WasAlive;

            /// <summary>
            /// The health the picture is showing, which trails the server's by the swing's
            /// moment of contact. See <see cref="MonsterHitQueue"/>.
            /// </summary>
            public MonsterHitQueue Hits;

            /// <summary>Every renderer on the model, for the flash. Found once.</summary>
            public Renderer[] Renderers;
        }

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();

        private NetworkManager _networkManager;
        private MonsterVisualCatalogue _catalogue;
        private IDefinitionRegistry<MonsterDefinition> _definitions;
        private ILocalizedTextSource _text;

        /// <summary>
        /// What the local player currently has selected, or null.
        /// </summary>
        /// <remarks>
        /// <b>Read, never written, and purely local.</b> Which monster a player is looking at
        /// is a fact about their client, not about the world: two players standing together
        /// have different targets and the server has no opinion. Supplied as a function so
        /// this holds no reference to the input class and cannot start telling it anything.
        /// </remarks>
        private System.Func<MonsterNetworkEntity> _selected;

        private Material _barBacking;
        private Material _barFill;

        /// <summary>How many monsters are currently being drawn. For tests.</summary>
        public int Count => _shown.Count;

        /// <summary>Whether this can draw anything at all.</summary>
        public bool IsComposed => _networkManager != null && _catalogue != null;

        /// <summary>Points this at the client, the approved art, and what monsters are called.</summary>
        /// <param name="definitions">
        /// Authored monsters, for the name key. Required rather than optional, and null is
        /// allowed: the argument has to be written out. An optional one is how the world
        /// server came to build its monsters without a map registry and bury every one of
        /// them in a hillside, in silence -- the same shape must not be repeated here.
        /// </param>
        /// <param name="text">The player's language. Same rule.</param>
        /// <param name="selected">
        /// What the local player has targeted. Only that monster wears a name and a health
        /// bar: six labelled slimes in one clearing hide the clearing.
        /// </param>
        public void Compose(NetworkManager networkManager, MonsterVisualCatalogue catalogue,
            IDefinitionRegistry<MonsterDefinition> definitions, ILocalizedTextSource text,
            System.Func<MonsterNetworkEntity> selected)
        {
            _networkManager = networkManager;
            _catalogue = catalogue;
            _definitions = definitions;
            _text = text;
            _selected = selected;
        }

        /// <summary>Whether this monster is the one the local player has selected.</summary>
        public bool IsSelected(MonsterNetworkEntity monster)
        {
            if (monster == null || _selected == null) return false;

            MonsterNetworkEntity target = _selected();

            return target != null && ReferenceEquals(target, monster);
        }

        /// <summary>Whether this presenter is the one drawing a given monster.</summary>
        /// <remarks>Asked by the development placeholder so the two never draw the same
        /// monster twice -- a capsule standing inside a slime.</remarks>
        public bool Draws(DefinitionId monster)
        {
            return _catalogue != null && _catalogue.Knows(monster);
        }

        private void Update()
        {
            // Objects is checked as well as Started because the editor can reload the
            // domain mid-session: FishNet's tables are torn down while this component's
            // Update is still being called, and the enumeration below would throw.
            if (!IsComposed || _networkManager.ClientManager == null
                || !_networkManager.ClientManager.Started
                || _networkManager.ClientManager.Objects == null
                || _networkManager.ClientManager.Objects.Spawned == null)
            {
                return;
            }

            Sweep();
        }

        private void Sweep()
        {
            foreach (KeyValuePair<int, NetworkObject> pair in
                _networkManager.ClientManager.Objects.Spawned)
            {
                if (pair.Value == null) continue;

                MonsterNetworkEntity monster;

                if (!pair.Value.TryGetComponent(out monster)) continue;

                DefinitionId definition = monster.Definition;

                if (!Draws(definition)) continue;

                Shown shown;

                if (!_shown.TryGetValue(pair.Key, out shown) || shown.Visual == null)
                {
                    shown = Build(monster, definition);

                    if (shown == null) continue;

                    _shown[pair.Key] = shown;
                }

                Follow(shown, monster);
            }

            Forget();
        }

        /// <summary>Moves a model to where the server says its monster is, and animates it.</summary>
        private void Follow(Shown shown, MonsterNetworkEntity monster)
        {
            var position = new Vector3(monster.X, monster.Y, monster.Z);

            Vector3 step = position - shown.LastPosition;
            step.y = 0f;

            // Measured against when the position last changed, not against the frame. A
            // snapshot arriving every tenth of a second would otherwise read as a monster
            // that is motionless for five frames and then travelling at ten times its speed
            // for one.
            if (step.sqrMagnitude > 0f)
            {
                float since = Time.time - shown.LastChangeTime;
                float speed = since > 0f ? step.magnitude / since : 0f;

                shown.LastPosition = position;
                shown.LastChangeTime = Time.time;

                if (speed > MoveThreshold)
                {
                    shown.MovingUntil = Time.time + MoveMemorySeconds;
                    shown.ObservedSpeed = speed;
                }
            }

            Transform visual = shown.Visual.transform;

            if ((visual.position - position).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                visual.position = position;
            }
            else
            {
                visual.position = Vector3.Lerp(visual.position, position,
                    1f - Mathf.Exp(-FollowSharpness * Time.deltaTime));
            }

            // Faced the way the server says, not the way it was last seen to travel. A
            // monster about to leap is standing still, so travel says nothing about what it
            // is aiming at -- and the leap is the one moment facing has to be right. The
            // server also holds the angle steady once it commits to a swing, so a player
            // running round behind it during the wind-up stays behind it.
            visual.rotation = Quaternion.Slerp(visual.rotation,
                Quaternion.Euler(0f, monster.Facing, 0f),
                1f - Mathf.Exp(-12f * Time.deltaTime));

            // Health drops are queued rather than drawn, and drawn when the blade arrives.
            //
            // The server applies a basic attack's damage the instant it accepts the swing,
            // and tells every client about the swing and the new health in the same tick.
            // Drawn at once, the slime flinched, its number appeared and its bar fell while
            // the sword was still being drawn back -- a third of a second before anything
            // touched it. So each drop waits for the clip's measured moment of contact, and
            // everything that reads the health for drawing reads the held-back copy. Rises
            // are shown at once: a respawn or a heal has no swing to wait for.
            // Held for as long as the swing that caused it takes to make contact -- which
            // depends on how fast that swing is playing, so the feedback is asked rather
            // than a constant read. With no swing to explain the blow it falls back to
            // the clip's own contact at normal speed.
            CombatFeedback feedback = CombatFeedback.Current;

            float hold = feedback != null
                ? feedback.ImpactDelayAt(Time.time)
                : CombatFeedback.BasicAttackImpactSeconds;

            shown.Hits.Observe(monster.Health, Time.time, hold);

            while (shown.Hits.TryDraw(Time.time, out DrawnHit hit))
            {
                DrawHit(shown, monster, hit);
            }

            bool alive = !shown.Hits.ShownDead;

            if (shown.Animator != null)
            {
                bool moving = alive && Time.time < shown.MovingUntil;

                shown.Animator.SetBool("Move", moving);
                shown.Animator.SetBool("Dead", !alive);

                // One bounce per hop's worth of ground. The hop is played faster when the
                // monster is covering ground faster, which is what stops a creature
                // authored to stroll at half a metre a second from sliding through the same
                // one-second hop it uses to chase.
                //
                // Presentation only: the speed is read from positions the server already
                // sent, and nothing here changes where anything is.
                if (moving && shown.HopMetres > 0f)
                {
                    float cycles = shown.ObservedSpeed / shown.HopMetres;

                    shown.Animator.SetFloat("MoveSpeed",
                        Mathf.Clamp(cycles, SlowestHop, FastestHop));
                }

                // A rise in the swing count is the only thing that means "struck at". The
                // server decided it, landed it and applied the damage before this was told;
                // the animation is a report of that, never a cause of it.
                if (alive && monster.Swings > shown.LastSwings)
                {
                    shown.Animator.SetTrigger("Attack");

                    if (feedback != null) feedback.Play(CombatSound.MonsterAttack);
                }
            }

            if (shown.BarFill != null && shown.Hits.ShownHealth != shown.BarHealth)
            {
                shown.BarHealth = shown.Hits.ShownHealth;

                int max = monster.MaxHealth > 0 ? monster.MaxHealth : 1;
                float fraction = Mathf.Clamp01(shown.Hits.ShownHealth / (float)max);

                shown.BarFill.localScale = new Vector3(fraction, 1f, 1f);
            }

            // A name and a health bar belong to the monster a player has chosen to look at,
            // and to no other. Selection is local, so this asks the local input and writes
            // nothing back -- a monster does not know it is being looked at, and nothing
            // about the world changes because it is.
            if (shown.Plate != null)
            {
                shown.Plate.SetShown(alive && IsSelected(monster));
            }

            shown.LastHealth = monster.Health;
            shown.LastSwings = monster.Swings;

            // The model stays after death so the death animation can finish and be seen.
            // Despawn belongs to the world: when the server takes the monster away, Forget
            // removes this.
            shown.WasAlive = alive;
        }

        /// <summary>
        /// The whole of one blow being seen: the number, the spark, the flash, the flinch,
        /// and -- if it was the last one -- the fall.
        /// </summary>
        /// <remarks>
        /// <b>Death beats Hit, and nothing beats Death.</b> A blow that kills sets the dead
        /// flag and clears the Hit trigger rather than pulling it, so a corpse never
        /// flinches. The queue reports a blow taken after death with a zero amount, and a
        /// zero amount draws nothing. The animator's own graph has no way out of Death, so
        /// this is belt and braces -- the belt is the graph, and this is the braces.
        /// </remarks>
        private static void DrawHit(Shown shown, MonsterNetworkEntity monster, DrawnHit hit)
        {
            if (hit.Amount <= 0 && !hit.Kills) return;

            CombatFeedback feedback = CombatFeedback.Current;
            Vector3 where = shown.Visual != null
                ? shown.Visual.transform.position
                : new Vector3(monster.X, monster.Y, monster.Z);
            float top = HeightOf(shown);

            if (feedback != null && hit.Amount > 0)
            {
                // The number starts just over the body and to one side, so it climbs past
                // the health bar rather than through it. The spark is in the body, where the
                // blade went.
                feedback.ShowDamage(where + new Vector3(0f, top + 0.05f, 0f), hit.Amount,
                    DamageNumberKind.Dealt);
                feedback.ShowImpact(where + new Vector3(0f, top * 0.55f, 0f), ImpactKind.Physical);

                if (shown.Renderers == null && shown.Visual != null)
                {
                    shown.Renderers = shown.Visual.GetComponentsInChildren<Renderer>(true);
                }

                feedback.Flash(shown.Renderers, new Color(1.9f, 1.9f, 2.1f),
                    CombatFeedback.FlashSeconds);
                feedback.Play(hit.Kills ? CombatSound.MonsterDeath : CombatSound.PlayerHitMonster);
            }

            if (shown.Animator == null) return;

            if (hit.Kills)
            {
                shown.Animator.ResetTrigger("Hit");
                shown.Animator.SetBool("Move", false);
                shown.Animator.SetBool("Dead", true);
            }
            else
            {
                shown.Animator.SetTrigger("Hit");
            }
        }

        /// <summary>How tall this monster is drawn, from the model's own bounds.</summary>
        private static float HeightOf(Shown shown)
        {
            if (shown.Visual == null) return 0.3f;

            var renderer = shown.Visual.GetComponentInChildren<Renderer>(true);

            if (renderer == null) return 0.3f;

            float top = renderer.bounds.max.y - shown.Visual.transform.position.y;

            // the skinned bounds are padded for the bubbles; the body is the lower part
            return Mathf.Clamp(top * 0.55f, 0.12f, 0.6f);
        }

        private Shown Build(MonsterNetworkEntity monster, DefinitionId definition)
        {
            GameObject prefab = _catalogue.PrefabFor(definition);

            if (prefab == null) return null;

            // Parented to the monster's own network object, not to this presenter. That is
            // what puts a real MonsterNetworkEntity above the collider a click lands on, so
            // a shipped client can target one through the path that already exists.
            GameObject visual = Instantiate(prefab, monster.transform);

            visual.name = "Monster " + definition.Value;
            visual.transform.position = new Vector3(monster.X, monster.Y, monster.Z);
            visual.transform.rotation = Quaternion.Euler(0f, monster.Facing, 0f);

            // How big this creature reads next to a player, which is an authored judgement and
            // not a property of the mesh. Applied before the collider is fitted below, so the
            // thing a click lands on is measured from the model at the size it is drawn.
            //
            // The model's pivot is between its feet, so a uniform scale here keeps it standing
            // on the ground: there is no offset to chase. Everything hanging off it -- the
            // nameplate, the bubbles, the hop -- is authored in the model's own space and comes
            // along at the same scale without being told.
            float scale = _catalogue.VisualScaleFor(definition);

            visual.transform.localScale = new Vector3(scale, scale, scale);

            // Something for a click to hit. The model is a skinned mesh with no collider of
            // its own, and targeting raycasts the world.
            AddClickTarget(visual, monster);

            var shown = new Shown
            {
                Visual = visual,
                Animator = visual.GetComponentInChildren<Animator>(true),
                Monster = monster,
                LastPosition = visual.transform.position,
                LastChangeTime = Time.time,
                HopMetres = Hop(definition),
                LastHealth = monster.Health,
                LastSwings = monster.Swings,
                WasAlive = monster.IsAlive,
                Hits = new MonsterHitQueue(monster.Health),
            };

            BuildPlate(shown, definition);

            return shown;
        }

        /// <summary>
        /// Puts the monster's name and a health bar over its head.
        /// </summary>
        /// <remarks>
        /// <b>Reuses the nameplate players and NPCs already wear</b> rather than adding a
        /// second one: it already turns toward the camera in yaw only, and already refuses
        /// to rewrite a string that has not changed. The bar hangs beneath it as a child, so
        /// it inherits that billboarding for free.
        ///
        /// The bar is two quads and a runtime material -- not an authored asset, and nothing
        /// shared. Runtime code in this project does not write to shared assets.
        /// </remarks>
        /// <summary>How far one hop is meant to carry this monster.</summary>
        private float Hop(DefinitionId definition)
        {
            float authored = _catalogue == null ? 0f : _catalogue.HopMetresFor(definition);

            return authored > 0f ? authored : DefaultHopMetres;
        }

        private void BuildPlate(Shown shown, DefinitionId definition)
        {
            float height = _catalogue.NameplateHeightFor(definition);

            shown.Plate = CharacterNameplate.Create(shown.Visual.transform, height,
                NameFontSize);
            shown.Plate.Refresh(NameOf(definition));

            // Hidden until this monster is the one the player has picked.
            shown.Plate.SetShown(false);

            EnsureBarMaterials();

            Transform backing = Quad("Health", shown.Plate.transform, _barBacking,
                new Vector3(0f, -BarDrop, 0f), new Vector3(BarWidth, BarHeight, 1f));

            // The fill is pivoted at the bar's left edge, so scaling it empties the bar from
            // the right rather than shrinking it toward its own middle.
            var pivot = new GameObject("Fill").transform;

            pivot.SetParent(backing, false);
            pivot.localPosition = new Vector3(-0.5f, 0f, -0.01f);
            pivot.localScale = Vector3.one;

            Quad("Bar", pivot, _barFill, new Vector3(0.5f, 0f, 0f), Vector3.one);

            shown.BarFill = pivot;
        }

        private static Transform Quad(string name, Transform parent, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

            quad.name = name;

            // A health bar a click could land on would steal the click from the monster
            // underneath it.
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = localScale;
            quad.GetComponent<Renderer>().sharedMaterial = material;

            return quad.transform;
        }

        private void EnsureBarMaterials()
        {
            if (_barBacking != null && _barFill != null) return;

            // The pipeline's own unlit shader, so a health bar is not shaded by the sun and
            // does not need a material asset of its own.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

            _barBacking = new Material(unlit) { color = new Color(0.06f, 0.06f, 0.08f, 1f) };
            _barFill = new Material(unlit) { color = new Color(0.78f, 0.22f, 0.24f, 1f) };
        }

        /// <summary>
        /// What to write above a monster.
        /// </summary>
        /// <remarks>
        /// The authored name key through the player's language, exactly as an NPC's plate
        /// resolves it. Monster names are deliberately shipped in one language -- a name is
        /// what players call a thing to each other -- so this is a content lookup rather
        /// than a translation.
        ///
        /// With no content or no translation it falls back to a readable form of the id
        /// rather than to a hard-coded string, so nothing here changes when a monster is
        /// added.
        /// </remarks>
        public string NameOf(DefinitionId definition)
        {
            MonsterDefinition authored = null;

            if (_definitions != null && definition.IsValid)
            {
                _definitions.TryGet(definition, out authored);
            }

            if (authored != null && _text != null && authored.NameKey.IsValid
                && _text.TryGet(authored.NameKey, out string name)
                && !string.IsNullOrEmpty(name))
            {
                return name;
            }

            return UiText.Readable(definition);
        }

        /// <summary>
        /// Gives the model a collider and a way back to the monster it stands for.
        /// </summary>
        /// <remarks>
        /// <b>Sized from the model rather than guessed.</b> The bounds of the renderers are
        /// what a player sees, so that is what a click should hit.
        ///
        /// <b>It reuses the development marker deliberately.</b> Targeting reads
        /// <c>DevelopmentMonsterMarker</c>, and the two files that do the reading --
        /// <c>WorldPointerInput</c> and <c>WorldCombatInput</c> -- are locked. Attaching the
        /// marker the existing code already looks for makes a real monster clickable without
        /// touching either. That marker is compiled out of a production build, so a shipped
        /// client cannot click a monster yet: a production targeting path belongs to the
        /// runtime gate, and this is recorded rather than quietly worked around.
        /// </remarks>
        private static void AddClickTarget(GameObject visual, MonsterNetworkEntity monster)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true);

            var box = visual.AddComponent<BoxCollider>();

            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;

                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                // A renderer reports its bounds in world units and a box collider is sized in
                // its own local ones, so the two only agree while the model is drawn at the
                // size it was modelled. Divide by the scale it is actually drawn at, or a
                // monster shown at three quarters gets a click target three quarters the size
                // of itself -- which reads as "this thing is hard to click" long before anyone
                // thinks to suspect the collider.
                Vector3 lossy = visual.transform.lossyScale;

                box.center = visual.transform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(
                    bounds.size.x / Mathf.Max(1e-4f, Mathf.Abs(lossy.x)),
                    bounds.size.y / Mathf.Max(1e-4f, Mathf.Abs(lossy.y)),
                    bounds.size.z / Mathf.Max(1e-4f, Mathf.Abs(lossy.z)));
            }
            else
            {
                box.center = new Vector3(0f, 0.15f, 0f);
                box.size = new Vector3(0.35f, 0.3f, 0.35f);
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var marker = visual.AddComponent<DevelopmentMonsterMarker>();

            marker.Monster = monster;
#endif
        }

        /// <summary>Drops models whose monster the server has taken away.</summary>
        private void Forget()
        {
            _gone.Clear();

            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (_networkManager.ClientManager.Objects.Spawned.ContainsKey(pair.Key)
                    && pair.Value.Visual != null)
                {
                    continue;
                }

                _gone.Add(pair.Key);
            }

            for (var i = 0; i < _gone.Count; i++)
            {
                Shown shown = _shown[_gone[i]];

                if (shown.Visual != null) Destroy(shown.Visual);

                _shown.Remove(_gone[i]);
            }
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (pair.Value.Visual != null) Destroy(pair.Value.Visual);
            }

            _shown.Clear();

            // Runtime materials, made here and owned here. Left behind they leak a pair per
            // presenter per play session, which is how five font assets once accumulated in
            // a single afternoon.
            if (_barBacking != null) Destroy(_barBacking);
            if (_barFill != null) Destroy(_barFill);
        }
    }
}
