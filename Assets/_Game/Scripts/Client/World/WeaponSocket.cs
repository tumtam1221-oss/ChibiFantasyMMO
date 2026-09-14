using ChibiFantasy.Core;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// Holds a weapon model in a character's hand — the reusable attachment foundation for
    /// every class's weapon.
    /// </summary>
    /// <remarks>
    /// <b>Presentation only.</b> It renders a model where the hand is; it never decides ATK,
    /// range, damage, hit, cooldown or anything a server owns. The equipped item and what it
    /// does are the server's; this is the picture of it.
    ///
    /// <b>One socket, resolved from the Humanoid rig.</b> The hand is found through
    /// <c>Animator.GetBoneTransform(HumanBodyBones.RightHand)</c>, so it works for both
    /// production rigs without a hard-coded bone name and survives a re-export that renames
    /// bones. A single child transform is created under the hand once; the weapon is a child
    /// of that, so a per-weapon grip offset never touches the rig.
    ///
    /// <b>Equip creates once; it never instantiates per frame.</b> Equipping the same weapon
    /// again is a no-op, so a replicated equipment update that repeats does not stack a
    /// second sword. Equipping a different weapon swaps the model. Unequip removes it. A
    /// missing bone or a missing prefab fails safely — no weapon, no exception.
    ///
    /// <b>Rebindable.</b> When the character model is rebuilt (a gender swap, a respawn) the
    /// presenter calls <see cref="Bind"/> again with the new animator and re-applies the
    /// weapon, so the sword follows the new hand rather than a destroyed one.
    /// </remarks>
    public sealed class WeaponSocket : MonoBehaviour
    {
        private Transform _hand;
        private Transform _socket;
        private GameObject _weapon;
        private DefinitionId _equipped;

        /// <summary>The item currently shown, or invalid when the hand is empty.</summary>
        public DefinitionId Equipped => _equipped;

        /// <summary>The live weapon object, or null. For tests and grip inspection.</summary>
        public GameObject Weapon => _weapon;

        /// <summary>Whether a hand socket has been resolved from a rig.</summary>
        public bool HasSocket => _socket != null;

        /// <summary>
        /// Points the socket at a rig's right hand, creating the child socket once.
        /// </summary>
        /// <remarks>Called whenever the model is (re)built. A weapon already shown is
        /// re-attached to the new hand so a rebuild does not drop it.</remarks>
        public void Bind(Animator animator)
        {
            Transform hand = animator == null
                ? null
                : animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (hand == _hand && _socket != null) return;

            _hand = hand;

            // The old socket belonged to a model that is being replaced; drop it and the
            // weapon under it, then rebuild against the new hand.
            if (_socket != null)
            {
                Remove(_socket.gameObject);
                _socket = null;
                _weapon = null;
            }

            if (_hand == null) return;

            _socket = new GameObject("MainHandSocket").transform;
            _socket.SetParent(_hand, false);
            _socket.localPosition = Vector3.zero;
            _socket.localRotation = Quaternion.identity;
            _socket.localScale = Vector3.one;

            // Re-show whatever was equipped before the rebuild, in the new hand.
            if (_equipped.IsValid && _pendingPrefab != null)
            {
                Spawn(_pendingPrefab, _pendingPosition, _pendingEuler, _pendingScale);
            }
        }

        // What to re-attach after a rebind. Held rather than re-resolved so the socket does
        // not need the catalogue.
        private GameObject _pendingPrefab;
        private Vector3 _pendingPosition;
        private Vector3 _pendingEuler;
        private float _pendingScale = 1f;

        /// <summary>
        /// Shows one weapon in the hand, swapping or clearing as needed.
        /// </summary>
        /// <param name="item">The equipped item's id. Invalid clears the hand.</param>
        /// <param name="prefab">The presentation prefab. Null clears the hand.</param>
        /// <param name="localPosition">Grip offset relative to the socket.</param>
        /// <param name="localEuler">Grip rotation relative to the socket.</param>
        /// <param name="scale">Uniform scale; zero or less is treated as 1.</param>
        public void Equip(DefinitionId item, GameObject prefab, Vector3 localPosition,
            Vector3 localEuler, float scale)
        {
            if (!item.IsValid || prefab == null)
            {
                Unequip();

                return;
            }

            // Same weapon already shown: nothing to do. This is what makes a repeated
            // replicated equipment update cost nothing and never stack a second model.
            if (item == _equipped && _weapon != null) return;

            if (_weapon != null)
            {
                Remove(_weapon);
                _weapon = null;
            }

            _equipped = item;
            _pendingPrefab = prefab;
            _pendingPosition = localPosition;
            _pendingEuler = localEuler;
            _pendingScale = scale <= 0f ? 1f : scale;

            if (_socket != null)
            {
                Spawn(_pendingPrefab, _pendingPosition, _pendingEuler, _pendingScale);
            }
        }

        /// <summary>Clears the hand.</summary>
        public void Unequip()
        {
            _equipped = default;
            _pendingPrefab = null;

            if (_weapon != null)
            {
                Remove(_weapon);
                _weapon = null;
            }
        }

        // Destroy that also works when a test drives the socket outside play mode.
        private static void Remove(GameObject go)
        {
            if (go == null) return;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void Spawn(GameObject prefab, Vector3 pos, Vector3 euler, float scale)
        {
            _weapon = Instantiate(prefab, _socket);
            _weapon.transform.localPosition = pos;
            _weapon.transform.localRotation = Quaternion.Euler(euler);
            _weapon.transform.localScale = new Vector3(scale, scale, scale);
        }

        private void OnDestroy()
        {
            // Only while playing: during an edit-mode teardown the whole hierarchy is going
            // anyway, and DestroyImmediate is not allowed from OnDestroy.
            if (Application.isPlaying && _socket != null) Destroy(_socket.gameObject);
        }
    }
}
