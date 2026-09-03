using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// A short list of actions at the pointer.
    /// </summary>
    /// <remarks>
    /// <b>It sends commands and nothing else.</b> Which entries appear comes from
    /// <see cref="ItemContextActions"/>; picking one raises an event the controller turns
    /// into a service call. The menu holds no gameplay reference and decides no gameplay
    /// rule.
    ///
    /// <b>Buttons are reused.</b> The pool grows to the largest menu ever shown and is
    /// re-labelled after that, so opening a menu does not instantiate.
    /// </remarks>
    public sealed class ItemContextMenu : MonoBehaviour
    {
        [SerializeField] private RectTransform buttonParent;
        [SerializeField] private float entryHeight = 24f;
        [SerializeField] private float width = 140f;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<ItemContextAction> _actions = new List<ItemContextAction>();

        /// <summary>Raised with the action the player picked.</summary>
        public event System.Action<ItemContextAction> Picked;

        public bool IsOpen { get; private set; }

        /// <summary>Which panel the open menu refers to.</summary>
        public ItemSelectionSource Source { get; private set; }

        public int SlotIndex { get; private set; }

        /// <summary>The actions currently on offer.</summary>
        public IReadOnlyList<ItemContextAction> Actions => _actions;

        /// <summary>Creates the container when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (buttonParent == null) buttonParent = GetComponent<RectTransform>();
            if (buttonParent == null) buttonParent = gameObject.AddComponent<RectTransform>();

            buttonParent.pivot = new Vector2(0f, 1f);

            var background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.10f, 0.11f, 0.14f, 0.98f);
            }

            var layout = GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(2, 2, 2, 2);

            Close();
        }

        /// <summary>
        /// Opens with a set of actions at a screen position.
        /// </summary>
        /// <param name="source">Which panel the slot belongs to.</param>
        /// <param name="slotIndex">Slot the actions apply to.</param>
        /// <param name="actions">What to offer. An empty list closes the menu instead.</param>
        /// <param name="screenPosition">Where the pointer was.</param>
        public bool Open(ItemSelectionSource source, int slotIndex,
            IReadOnlyList<ItemContextAction> actions, Vector2 screenPosition)
        {
            if (actions == null || actions.Count == 0)
            {
                Close();
                return false;
            }

            Source = source;
            SlotIndex = slotIndex;

            _actions.Clear();
            for (int i = 0; i < actions.Count; i++) _actions.Add(actions[i]);

            EnsurePool(_actions.Count);

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool used = i < _actions.Count;
                _buttons[i].gameObject.SetActive(used);
                if (used) _labels[i].text = _actions[i].ToString();
            }

            if (buttonParent != null)
            {
                buttonParent.sizeDelta = new Vector2(width, _actions.Count * entryHeight + 4f);
                buttonParent.position = new Vector3(screenPosition.x, screenPosition.y,
                    buttonParent.position.z);
            }

            IsOpen = true;
            gameObject.SetActive(true);
            return true;
        }

        /// <summary>Closes without raising <see cref="Picked"/>.</summary>
        public void Close()
        {
            IsOpen = false;
            Source = ItemSelectionSource.None;
            SlotIndex = -1;
            _actions.Clear();

            gameObject.SetActive(false);
        }

        /// <summary>Picks by position in the offered list. What a button click calls.</summary>
        public void Pick(int index)
        {
            if (!IsOpen || index < 0 || index >= _actions.Count)
            {
                Close();
                return;
            }

            ItemContextAction action = _actions[index];
            Close();

            var handler = Picked;
            if (handler != null) handler(action);
        }

        private void EnsurePool(int count)
        {
            for (int i = _buttons.Count; i < count; i++)
            {
                int index = i;

                var go = new GameObject("Action" + i, typeof(RectTransform));
                go.transform.SetParent(buttonParent, false);

                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(width - 4f, entryHeight);

                var image = go.AddComponent<Image>();
                image.color = new Color(0.20f, 0.22f, 0.28f, 1f);

                var button = go.AddComponent<Button>();
                button.onClick.AddListener(() => Pick(index));

                var textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(go.transform, false);
                var textRect = (RectTransform)textGo.transform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(6f, 0f);
                textRect.offsetMax = Vector2.zero;

                var text = textGo.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 12;
                text.alignment = TextAnchor.MiddleLeft;
                text.color = Color.white;
                text.raycastTarget = false;

                _buttons.Add(button);
                _labels.Add(text);
            }
        }
    }
}
