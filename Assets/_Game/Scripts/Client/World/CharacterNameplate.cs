using ChibiFantasy.Data;
using ChibiFantasy.Network;
using TMPro;
using UnityEngine;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// The name above a character's head.
    /// </summary>
    /// <remarks>
    /// <b>Built in code and rewritten only on change.</b> A nameplate that rebuilt its string
    /// every frame would allocate once per character per frame, which is how a hundred
    /// players becomes a garbage collection problem. The text is compared before it is
    /// assigned.
    ///
    /// <b>The camera is found once.</b> <c>Camera.main</c> is a tagged search; calling it per
    /// frame per nameplate is the classic version of this mistake.
    /// </remarks>
    public sealed class CharacterNameplate : MonoBehaviour
    {
        private TextMeshPro _label;
        private Camera _camera;

        /// <summary>What it currently reads.</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>How many times the string was actually written.</summary>
        public int WriteCount { get; private set; }

        /// <summary>The size a player's or an NPC's name has always been drawn at.</summary>
        public const float DefaultFontSize = 2.4f;

        public static CharacterNameplate Create(Transform parent, float height)
        {
            return Create(parent, height, DefaultFontSize);
        }

        /// <summary>
        /// Builds a nameplate at a given size.
        /// </summary>
        /// <remarks>
        /// The size is a parameter because a monster's name is not a title. A player's
        /// nameplate identifies somebody across a plaza and is read at a distance; a
        /// monster's says what the thing you are already looking at is called, and at the
        /// player's size it dominated the creature it was labelling -- six of them in a camp
        /// buried the camp. Text is in world units, so this scales with distance like
        /// everything else in the scene rather than being pinned to one screen resolution.
        /// </remarks>
        public static CharacterNameplate Create(Transform parent, float height, float fontSize)
        {
            var host = new GameObject("Nameplate");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(0f, height, 0f);

            var plate = host.AddComponent<CharacterNameplate>();

            plate._label = host.AddComponent<TextMeshPro>();
            plate._label.alignment = TextAlignmentOptions.Center;
            plate._label.fontSize = fontSize;
            plate._label.color = new Color(0.90f, 0.92f, 0.96f);
            plate._label.text = string.Empty;
            plate._label.rectTransform.sizeDelta = new Vector2(4f * (fontSize / DefaultFontSize),
                0.6f * (fontSize / DefaultFontSize));

            return plate;
        }

        /// <summary>Shows or hides the plate without forgetting what it says.</summary>
        /// <remarks>Separate from <see cref="Refresh"/>, which hides an empty plate on its
        /// own: a monster's plate has a name at all times and is hidden because nobody has
        /// selected it, which is a different question.</remarks>
        public void SetShown(bool shown)
        {
            bool wanted = shown && Text.Length > 0;

            if (gameObject.activeSelf != wanted) gameObject.SetActive(wanted);
        }

        /// <summary>Sets the text, if it changed.</summary>
        public void Refresh(string text)
        {
            string wanted = text ?? string.Empty;

            if (wanted == Text) return;

            Text = wanted;
            WriteCount++;

            if (_label != null) _label.text = wanted;

            gameObject.SetActive(wanted.Length > 0);
        }

        /// <summary>Turns to face the viewer.</summary>
        /// <remarks>Yaw only. A nameplate that pitched with the camera would lie on its back
        /// when the player looked down.</remarks>
        public void FaceCamera()
        {
            if (_camera == null) _camera = Camera.main;

            if (_camera == null) return;

            Vector3 toCamera = _camera.transform.position - transform.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }

        private void LateUpdate()
        {
            FaceCamera();
        }
    }
}
