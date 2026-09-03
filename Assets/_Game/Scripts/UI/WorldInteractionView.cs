using System.Collections.Generic;
using System.Text;
using ChibiFantasy.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.UI
{
    /// <summary>
    /// The banner naming the map the player is on.
    /// </summary>
    /// <remarks>A snapshot and a label. Prototype presentation: the classification is shown
    /// because it is useful while building, not because a shipped game would.</remarks>
    public sealed class MapNameView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text label;

        [SerializeField] private Color townColor = new Color(0.60f, 0.80f, 0.95f, 1f);
        [SerializeField] private Color fieldColor = new Color(0.75f, 0.90f, 0.70f, 1f);
        [SerializeField] private Color dangerColor = new Color(0.95f, 0.65f, 0.55f, 1f);

        public bool IsVisible { get; private set; }

        public MapViewData Data { get; private set; }

        /// <summary>The label as last formatted. Exposed so the rule is testable.</summary>
        public string Label { get; private set; }

        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;
            if (label != null) return;

            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            label = go.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }

        public void Show(MapViewData map)
        {
            Data = map;

            if (!map.IsValid)
            {
                Hide();
                return;
            }

            Label = FormatLabel(map, Text);
            IsVisible = true;

            if (label != null)
            {
                label.text = Label;
                label.color = map.IsBossArea || map.PkAllowed ? dangerColor
                    : map.IsTown ? townColor : fieldColor;
            }

            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            Data = MapViewData.None;
            Label = string.Empty;
            IsVisible = false;

            if (label != null) label.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>The map's name, with the flags worth warning about.</summary>
        public static string FormatLabel(MapViewData map, ILocalizedTextSource text)
        {
            if (!map.IsValid) return string.Empty;

            string name = map.NameKey.IsValid
                ? LocalizedText.Resolve(text, map.NameKey)
                : map.Map.ToString();

            var builder = new StringBuilder(name);

            builder.Append(" [").Append(map.Category).Append(']');
            if (map.PkAllowed) builder.Append(" PK");

            return builder.ToString();
        }
    }

    /// <summary>
    /// The prompt shown when a player is standing at a portal.
    /// </summary>
    /// <remarks>
    /// <b>It asks; it does not travel.</b> Pressing raises an event and the controller
    /// submits it to <c>TravelService</c>. The button being interactable reflects the
    /// snapshot's advisory range and enabled flags, and the service re-checks both.
    /// </remarks>
    public sealed class PortalInteractionView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text label;
        [SerializeField] private Button useButton;

        /// <summary>Raised with the portal the player asked to use.</summary>
        public event System.Action<ChibiFantasy.Core.DefinitionId> Requested;

        public bool IsVisible { get; private set; }

        public PortalViewData Data { get; private set; }

        public string Label { get; private set; }

        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.09f, 0.10f, 0.13f, 0.92f);
            }

            if (label == null) label = WorldUiBuilder.CreateLabel(transform, 13);

            if (useButton == null)
            {
                useButton = WorldUiBuilder.CreateButton(transform, "Enter", new Vector2(8f, 8f));
                useButton.onClick.AddListener(Request);
            }

            Hide();
        }

        public void Show(PortalViewData portal)
        {
            Data = portal;

            if (!portal.IsValid)
            {
                Hide();
                return;
            }

            Label = FormatLabel(portal, Text);
            IsVisible = true;

            if (label != null) label.text = Label;
            if (useButton != null) useButton.interactable = portal.CanOffer;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            Data = PortalViewData.None;
            Label = string.Empty;
            IsVisible = false;

            if (label != null) label.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Raises <see cref="Requested"/>. Changes nothing by itself.</summary>
        public void Request()
        {
            if (!IsVisible) return;

            var handler = Requested;
            if (handler != null) handler(Data.Portal);
        }

        /// <summary>Where the portal goes, and why it may not be usable.</summary>
        public static string FormatLabel(PortalViewData portal, ILocalizedTextSource text)
        {
            if (!portal.IsValid) return string.Empty;

            string destination = portal.DestinationNameKey.IsValid
                ? LocalizedText.Resolve(text, portal.DestinationNameKey)
                : portal.DestinationMap.ToString();

            var builder = new StringBuilder("To ").Append(destination);

            builder.Append(" [").Append(portal.DestinationCategory).Append(']');

            if (!portal.Enabled) builder.Append("\nClosed");
            else if (!portal.IsInRange) builder.Append("\nToo far");

            if (portal.LevelRequirement > 0)
            {
                builder.Append("\nLevel ").Append(portal.LevelRequirement);
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// The prompt shown next to an NPC, with one button per role it offers.
    /// </summary>
    /// <remarks>
    /// <b>It offers; it opens nothing.</b> Picking a role raises an event; the controller
    /// asks <c>NpcInteractionService</c> whether it is allowed and opens the matching
    /// screen. The panel draws a button per role without knowing what any role means.
    /// </remarks>
    public sealed class NpcInteractionView : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Text nameLabel;
        [SerializeField] private RectTransform buttonParent;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<NpcRole> _roles = new List<NpcRole>();

        /// <summary>Raised with the NPC and the role the player picked.</summary>
        public event System.Action<ChibiFantasy.Core.DefinitionId, NpcRole> RolePicked;

        public bool IsVisible { get; private set; }

        public NpcViewData Data { get; private set; }

        public string Title { get; private set; }

        public IReadOnlyList<NpcRole> OfferedRoles => _roles;

        public ILocalizedTextSource Text { get; set; }

        /// <summary>Creates the child graphics when a prefab was not authored.</summary>
        public void EnsureVisuals()
        {
            if (root == null) root = gameObject;

            var background = GetComponent<Image>();
            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(0.09f, 0.10f, 0.13f, 0.94f);
            }

            if (nameLabel == null) nameLabel = WorldUiBuilder.CreateLabel(transform, 14);

            if (buttonParent == null)
            {
                var go = new GameObject("Roles", typeof(RectTransform));
                go.transform.SetParent(transform, false);

                buttonParent = (RectTransform)go.transform;
                buttonParent.anchorMin = Vector2.zero;
                buttonParent.anchorMax = new Vector2(1f, 0f);
                buttonParent.pivot = new Vector2(0.5f, 0f);
                buttonParent.sizeDelta = new Vector2(0f, 120f);

                var layout = go.AddComponent<VerticalLayoutGroup>();
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 2f;
            }

            Hide();
        }

        /// <summary>Draws an NPC and the roles it offers.</summary>
        public void Show(NpcViewData npc)
        {
            EnsureVisuals();

            Data = npc;

            if (!npc.IsValid)
            {
                Hide();
                return;
            }

            Title = FormatTitle(npc, Text);
            IsVisible = true;

            _roles.Clear();
            for (int i = 0; i < npc.Roles.Count; i++)
            {
                // Generic is what every NPC can do; a button for it would be noise next to
                // the roles that actually open something.
                if (npc.Roles[i] == NpcRole.Generic && npc.Roles.Count > 1) continue;
                _roles.Add(npc.Roles[i]);
            }

            EnsurePool(_roles.Count);

            for (int i = 0; i < _buttons.Count; i++)
            {
                bool used = i < _roles.Count;
                _buttons[i].gameObject.SetActive(used);

                if (!used) continue;

                _labels[i].text = _roles[i].ToString();
                _buttons[i].interactable = npc.CanOffer;
            }

            if (nameLabel != null) nameLabel.text = Title;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            Data = NpcViewData.None;
            Title = string.Empty;
            IsVisible = false;
            _roles.Clear();

            for (int i = 0; i < _buttons.Count; i++) _buttons[i].gameObject.SetActive(false);
            if (nameLabel != null) nameLabel.text = string.Empty;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Picks by position. What a role button calls.</summary>
        public void Pick(int index)
        {
            if (!IsVisible || index < 0 || index >= _roles.Count) return;

            var handler = RolePicked;
            if (handler != null) handler(Data.Npc, _roles[index]);
        }

        public static string FormatTitle(NpcViewData npc, ILocalizedTextSource text)
        {
            if (!npc.IsValid) return string.Empty;

            string name = npc.NameKey.IsValid
                ? LocalizedText.Resolve(text, npc.NameKey)
                : npc.Npc.ToString();

            if (!npc.Enabled) return name + " (unavailable)";
            return npc.IsInRange ? name : name + " (too far)";
        }

        private void EnsurePool(int count)
        {
            for (int i = _buttons.Count; i < count; i++)
            {
                int index = i;

                Button button = WorldUiBuilder.CreateButton(buttonParent, string.Empty,
                    Vector2.zero, stretch: true);

                button.onClick.AddListener(() => Pick(index));

                _buttons.Add(button);
                _labels.Add(button.GetComponentInChildren<Text>());
            }
        }
    }

    /// <summary>Shared construction for the prototype world panels.</summary>
    /// <remarks>These panels are debug-grade on purpose: the phase needed a way to see and
    /// drive travel and interaction, not finished art. Sharing the builders keeps that
    /// scaffolding in one place rather than copied three times.</remarks>
    internal static class WorldUiBuilder
    {
        public static Text CreateLabel(Transform parent, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(8f, -8f);
            rect.sizeDelta = new Vector2(-16f, 60f);

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button CreateButton(Transform parent, string label, Vector2 position,
            bool stretch = false)
        {
            var go = new GameObject(string.IsNullOrEmpty(label) ? "Button" : label,
                typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;

            if (!stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = position;
            }

            rect.sizeDelta = new Vector2(stretch ? 0f : 110f, 26f);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.24f, 0.28f, 0.34f, 1f);

            var button = go.AddComponent<Button>();

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;

            return button;
        }
    }
}
