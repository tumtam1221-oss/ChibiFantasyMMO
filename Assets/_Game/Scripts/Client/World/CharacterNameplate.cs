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

        public static CharacterNameplate Create(Transform parent, float height)
        {
            var host = new GameObject("Nameplate");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(0f, height, 0f);

            var plate = host.AddComponent<CharacterNameplate>();

            plate._label = host.AddComponent<TextMeshPro>();
            plate._label.alignment = TextAlignmentOptions.Center;
            plate._label.fontSize = 2.4f;
            plate._label.color = new Color(0.90f, 0.92f, 0.96f);
            plate._label.text = string.Empty;
            plate._label.rectTransform.sizeDelta = new Vector2(4f, 0.6f);

            return plate;
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
