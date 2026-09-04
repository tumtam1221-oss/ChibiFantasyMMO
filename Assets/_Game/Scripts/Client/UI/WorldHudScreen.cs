using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The heads-up display: this player's health, level and experience.
    /// </summary>
    /// <remarks>
    /// <b>It repaints on change, not on a schedule.</b> The replicated values are SyncVars
    /// with nothing to subscribe to, so the presenter is asked once a frame whether anything
    /// moved -- comparing six numbers -- and the labels are only rewritten when it says yes.
    /// Rebuilding strings every frame is the classic way a HUD becomes the top of an
    /// allocation profile.
    ///
    /// <b>Unbound is a state it draws.</b> Before entering the world and after a despawn the
    /// bars are hidden rather than shown at zero, because a health bar reading 0/0 looks like
    /// a dead character rather than an absent one.
    /// </remarks>
    public sealed class WorldHudScreen : MonoBehaviour
    {
        private readonly CharacterHudPresenter _presenter = new CharacterHudPresenter();

        private RectTransform _panel;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _health;
        private TextMeshProUGUI _level;
        private TextMeshProUGUI _experience;
        private Image _healthFill;
        private RectTransform _statusAnchor;
        private bool _built;

        /// <summary>Raised when the player asks for their bag.</summary>
        public event System.Action InventoryRequested;

        /// <summary>The values currently on screen, for a test to read.</summary>
        public HudViewData Current => _presenter.Current;

        public bool IsBound => _presenter.IsBound;

        /// <summary>
        /// Where buff and debuff icons will go.
        /// </summary>
        /// <remarks>
        /// An anchor and nothing else. Status effects are not replicated -- there is no
        /// network representation of them anywhere in this project -- so there is nothing to
        /// draw, and drawing a made-up row of icons would be a lie about what the server
        /// knows. The space is reserved so the gate that replicates them has somewhere to
        /// put them, and the absence is reported rather than hidden.
        /// </remarks>
        public RectTransform StatusEffectAnchor => _statusAnchor;

        /// <summary>Binds the character this client owns.</summary>
        public bool Bind(CharacterNetworkEntity entity)
        {
            EnsureBuilt();

            bool bound = _presenter.Bind(entity);

            Repaint();

            return bound;
        }

        public void Unbind()
        {
            _presenter.Unbind();

            Repaint();
        }

        private void Update()
        {
            if (_presenter.HasChanged()) Repaint();
        }

        private void Repaint()
        {
            HudViewData data = _presenter.Current;

            if (_panel != null) _panel.gameObject.SetActive(data.IsBound);

            if (!data.IsBound) return;

            if (_name != null) _name.text = data.Character.Value ?? string.Empty;
            if (_health != null) _health.text = data.HealthLabel;
            if (_level != null) _level.text = data.LevelLabel;
            if (_experience != null) _experience.text = data.ExperienceLabel;
            if (_healthFill != null) _healthFill.fillAmount = data.HealthFraction;
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Builds this screen's widgets, once.</summary>
        /// <remarks>Unity only sends <c>Awake</c> while the player loop is running, so a
        /// screen added and bound in the same breath would otherwise have no widgets at all.
        /// Building on first use makes both orders identical, and the flag makes a second
        /// call after <c>Awake</c> harmless rather than a second canvas.</remarks>
        private void EnsureBuilt()
        {
            if (_built) return;

            _built = true;

            Build();
        }

        private void Build()
        {
            Canvas canvas = UiFactory.CreateCanvas("HUD Canvas", gameObject);

            RectTransform root = UiFactory.CreateStretched("Root", canvas.transform);

            _panel = UiFactory.CreateAnchored("Vitals", root, new Vector2(0f, 1f),
                new Vector2(360f, 132f), new Vector2(24f, -24f));

            UiFactory.CreatePanel("Frame", _panel, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)_panel.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _name = UiFactory.CreateLabel("Name", _panel, string.Empty, 22f);
            Row(_name.rectTransform, -8f, 26f);

            UiFactory.CreateBar("HealthBar", _panel, new Color(0.78f, 0.28f, 0.30f),
                out _healthFill);

            var bar = (RectTransform)_panel.GetChild(_panel.childCount - 1);
            Row(bar, -40f, 22f);

            _health = UiFactory.CreateLabel("Health", _panel, string.Empty, 16f,
                TextAlignmentOptions.Center);
            Row(_health.rectTransform, -40f, 22f);

            _level = UiFactory.CreateLabel("Level", _panel, string.Empty, 18f);
            Row(_level.rectTransform, -70f, 24f);

            _experience = UiFactory.CreateLabel("Experience", _panel, string.Empty, 16f,
                TextAlignmentOptions.Right);
            _experience.color = UiFactory.Muted;
            Row(_experience.rectTransform, -70f, 24f);

            // Reserved, empty, and documented above.
            _statusAnchor = UiFactory.CreateAnchored("StatusEffects", _panel,
                new Vector2(0f, 0f), new Vector2(340f, 28f), new Vector2(10f, 6f));

            Button bag = UiFactory.CreateButton("Inventory", root, "Inventory",
                out TextMeshProUGUI _);

            RectTransform bagRect = bag.GetComponent<RectTransform>();
            bagRect.anchorMin = new Vector2(1f, 0f);
            bagRect.anchorMax = new Vector2(1f, 0f);
            bagRect.pivot = new Vector2(1f, 0f);
            bagRect.sizeDelta = new Vector2(180f, 48f);
            bagRect.anchoredPosition = new Vector2(-24f, 24f);

            bag.onClick.AddListener(() => InventoryRequested?.Invoke());

            _panel.gameObject.SetActive(false);
        }

        private static void Row(RectTransform rect, float fromTop, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-20f, height);
            rect.anchoredPosition = new Vector2(0f, fromTop);
        }
    }
}
