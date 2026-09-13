using System.Collections.Generic;
using ChibiFantasy.Core;
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

        private TextMeshProUGUI _bagLabel;
        private TextMeshProUGUI _hints;
        private ILocalizedTextSource _text;

        /// <summary>Where this screen's words are translated. Optional.</summary>
        /// <remarks>Assigning relabels immediately: the HUD is built when the world loads
        /// and its two fixed captions are never touched again, so a language chosen
        /// afterwards would otherwise leave the bag button and the control hints in the
        /// language the player just left.</remarks>
        public ILocalizedTextSource Text
        {
            get => _text;
            set
            {
                _text = value;

                if (_bagLabel != null) _bagLabel.text = UiText.Of(_text, UiStrings.HudInventory);
                if (_hints != null) _hints.text = UiText.Of(_text, UiStrings.HudHints);
                if (_defeatLabel != null)
                {
                    _defeatLabel.text = UiText.Of(_text, UiStrings.HudDefeated);
                }

                if (_reviveLabel != null)
                {
                    _reviveLabel.text = UiText.Of(_text, UiStrings.HudReturnToTown);
                }
            }
        }

        private RectTransform _panel;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _health;
        private TextMeshProUGUI _mana;
        private TextMeshProUGUI _level;
        private TextMeshProUGUI _experience;
        private TextMeshProUGUI _attackSpeed;
        private Image _healthFill;

        /// <summary>What the player is currently pointing at, drawn only while there is one.</summary>
        /// <remarks>Its own small panel rather than a second column in the vitals: a target
        /// comes and goes, and the player's own numbers must not move around the screen when
        /// it does.</remarks>
        private RectTransform _targetPanel;
        private RectTransform _defeatPanel;
        private TextMeshProUGUI _defeatLabel;
        private TextMeshProUGUI _reviveLabel;

        private TextMeshProUGUI _targetName;
        private TextMeshProUGUI _targetHealth;
        private Image _targetHealthFill;
        private RectTransform _statusAnchor;
        private StatusEffectBar _statusBar;
        private IDefinitionRegistry<StatusEffectDefinition> _effects;
        private bool _built;

        /// <summary>Raised when the player asks for their bag.</summary>
        public event System.Action InventoryRequested;

        /// <summary>Raised when a fallen player asks to get up in town.</summary>
        /// <remarks>An event rather than a call into the network, so this screen stays a
        /// screen: it knows a button was pressed and nothing about who might act on it.</remarks>
        public event System.Action ReviveRequested;

        /// <summary>Whether the fallen notice is currently on screen. For tests.</summary>
        public bool IsShowingDefeat => _defeatPanel != null && _defeatPanel.gameObject.activeSelf;

        /// <summary>The values currently on screen, for a test to read.</summary>
        public HudViewData Current => _presenter.Current;

        public bool IsBound => _presenter.IsBound;

        /// <summary>Where the buff and debuff rows live.</summary>
        /// <remarks>Reserved by 18.5 and filled in by 18.7. The bar under it draws the
        /// owner-scoped snapshot the server sends; nothing here decides what is on it.</remarks>
        public RectTransform StatusEffectAnchor => _statusAnchor;

        /// <summary>The buff and debuff rows, for a test to read.</summary>
        public StatusEffectBar StatusEffects => _statusBar;

        /// <summary>
        /// Supplies the authored status effects the bar resolves names and icons from.
        /// </summary>
        /// <remarks>Content, not state. Given once by whoever composes the world screens,
        /// for the same reason the inventory panel is given an item registry: the wire
        /// carries ids and the client already has the definitions.</remarks>
        public void UseStatusEffects(IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            _effects = effects;
        }

        /// <summary>Binds the character this client owns.</summary>
        public bool Bind(CharacterNetworkEntity entity)
        {
            EnsureBuilt();

            bool bound = _presenter.Bind(entity);

            // The status bar binds through the same call, so the vitals and the buffs can
            // never end up pointed at different characters.
            if (_statusBar != null) _statusBar.Bind(entity, _effects);

            Repaint();

            return bound;
        }

        public void Unbind()
        {
            _presenter.Unbind();

            if (_statusBar != null) _statusBar.Unbind();

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
            if (_mana != null) _mana.text = data.ManaLabel;
            if (_level != null) _level.text = data.LevelLabel;
            if (_experience != null) _experience.text = data.ExperienceLabel;
            if (_attackSpeed != null) _attackSpeed.text = data.AttackSpeedLabel;
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

            // Beside the health figure. Empty until the server computes a mana ceiling,
            // which is the same rule 18.5 used when there was never going to be one.
            _mana = UiFactory.CreateLabel("Mana", _panel, string.Empty, 16f,
                TextAlignmentOptions.Right);
            _mana.color = UiFactory.Accent;
            Row(_mana.rectTransform, -40f, 22f);

            _level = UiFactory.CreateLabel("Level", _panel, string.Empty, 18f);
            Row(_level.rectTransform, -70f, 24f);

            _experience = UiFactory.CreateLabel("Experience", _panel, string.Empty, 16f,
                TextAlignmentOptions.Right);
            _experience.color = UiFactory.Muted;
            Row(_experience.rectTransform, -70f, 24f);

            // Between the level and the experience: the derived figure a player tuning AGI
            // or trying a weapon wants to see move. Inspectable now; designed later.
            _attackSpeed = UiFactory.CreateLabel("AttackSpeed", _panel, string.Empty, 14f,
                TextAlignmentOptions.Center);
            _attackSpeed.color = UiFactory.Muted;
            Row(_attackSpeed.rectTransform, -70f, 24f);

            // Under the vitals, which is where a player already looks.
            _statusAnchor = UiFactory.CreateAnchored("StatusEffects", _panel,
                new Vector2(0f, 0f), new Vector2(340f, 28f), new Vector2(10f, 6f));

            _statusBar = _statusAnchor.gameObject.AddComponent<StatusEffectBar>();
            _statusBar.Compose(_statusAnchor);

            BuildTargetPanel(root);

            Button bag = UiFactory.CreateButton("Inventory", root,
                UiText.Of(Text, UiStrings.HudInventory),
                out _bagLabel);

            RectTransform bagRect = bag.GetComponent<RectTransform>();
            bagRect.anchorMin = new Vector2(1f, 0f);
            bagRect.anchorMax = new Vector2(1f, 0f);
            bagRect.pivot = new Vector2(1f, 0f);
            bagRect.sizeDelta = new Vector2(180f, 48f);
            bagRect.anchoredPosition = new Vector2(-24f, 24f);

            bag.onClick.AddListener(() => InventoryRequested?.Invoke());

            BuildHints(root);
            BuildDefeatPanel(root);

            _panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// What the mouse does, said once, quietly.
        /// </summary>
        /// <remarks>
        /// <b>Only the controls that exist.</b> This game is played with the mouse: the left
        /// button walks, selects, attacks and picks up; the right button held turns the view;
        /// the wheel zooms. WASD, Space and F are development aids that are switched off in
        /// normal play, so telling a player about them would be telling them something
        /// untrue.
        ///
        /// Deliberately three short lines in a corner rather than a tutorial. This gate is
        /// about the controls working, not about teaching them.
        /// </remarks>
        private void BuildHints(RectTransform root)
        {
            TextMeshProUGUI hints = UiFactory.CreateLabel("Hints", root,
                UiText.Of(Text, UiStrings.HudHints), 15f);

            _hints = hints;

            hints.color = UiFactory.Muted;

            RectTransform rect = hints.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(360f, 70f);
            rect.anchoredPosition = new Vector2(24f, 24f);
        }

        /// <summary>
        /// What a player sees when they have lost a fight.
        /// </summary>
        /// <remarks>
        /// <b>It exists because the alternative was nothing at all.</b> A character reduced
        /// to zero health simply stopped working: attacks were refused, monsters ignored
        /// them, and because health is persisted, signing out and back in returned them to
        /// the same zero. Nothing on screen said what had happened or offered a way out.
        ///
        /// <b>It says one thing and offers one action.</b> Not a death screen with options
        /// this game does not have -- no resurrection items, no penalties, no timer. Those
        /// are decisions for a later gate; being stuck forever is not.
        /// </remarks>
        private void BuildDefeatPanel(RectTransform root)
        {
            _defeatPanel = UiFactory.CreateAnchored("Defeated", root, new Vector2(0.5f, 0.5f),
                new Vector2(360f, 132f), Vector2.zero);

            UiFactory.CreatePanel("Frame", _defeatPanel, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)_defeatPanel.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _defeatLabel = UiFactory.CreateLabel("DefeatedText", _defeatPanel,
                UiText.Of(Text, UiStrings.HudDefeated), 22f, TextAlignmentOptions.Center);

            Row(_defeatLabel.rectTransform, -22f, 32f);

            Button revive = UiFactory.CreateButton("ReturnToTown", _defeatPanel,
                UiText.Of(Text, UiStrings.HudReturnToTown), out _reviveLabel);

            RectTransform rect = revive.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(220f, 48f);
            rect.anchoredPosition = new Vector2(0f, 22f);

            revive.onClick.AddListener(() => ReviveRequested?.Invoke());

            _defeatPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows or hides the fallen notice.
        /// </summary>
        /// <remarks><b>Driven by replicated health, never by this screen.</b> The caller
        /// passes what the server said; nothing here decides that anybody is down, and
        /// hiding the notice does not revive anybody.</remarks>
        public void ShowDefeated(bool defeated)
        {
            if (_defeatPanel == null) return;

            if (_defeatPanel.gameObject.activeSelf == defeated) return;

            _defeatPanel.gameObject.SetActive(defeated);
        }

        /// <summary>The target readout: a name, a bar and a number.</summary>
        private void BuildTargetPanel(RectTransform root)
        {
            _targetPanel = UiFactory.CreateAnchored("Target", root, new Vector2(0.5f, 1f),
                new Vector2(320f, 72f), new Vector2(0f, -24f));

            UiFactory.CreatePanel("Frame", _targetPanel, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)_targetPanel.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _targetName = UiFactory.CreateLabel("TargetName", _targetPanel, string.Empty,
                18f, TextAlignmentOptions.Center);

            Row(_targetName.rectTransform, -6f, 24f);

            UiFactory.CreateBar("TargetHealthBar", _targetPanel,
                new Color(0.78f, 0.28f, 0.30f), out _targetHealthFill);

            var bar = (RectTransform)_targetPanel.GetChild(_targetPanel.childCount - 1);
            Row(bar, -34f, 20f);

            _targetHealth = UiFactory.CreateLabel("TargetHealth", _targetPanel, string.Empty,
                15f, TextAlignmentOptions.Center);

            Row(_targetHealth.rectTransform, -34f, 20f);

            _targetPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draws the monster the player has selected, or nothing.
        /// </summary>
        /// <remarks>Called with whatever the input has selected. The numbers are the ones the
        /// server replicated onto that monster; this screen computes none of them and cannot
        /// make a monster look healthier than the server says it is.</remarks>
        public void ShowTarget(string displayName, int health, int maxHealth)
        {
            if (_targetPanel == null) return;

            bool has = !string.IsNullOrEmpty(displayName);

            if (_targetPanel.gameObject.activeSelf != has)
            {
                _targetPanel.gameObject.SetActive(has);
            }

            if (!has) return;

            if (_targetName != null && _targetName.text != displayName)
            {
                _targetName.text = displayName;
            }

            string label = health + " / " + maxHealth;

            if (_targetHealth != null && _targetHealth.text != label)
            {
                _targetHealth.text = label;
            }

            if (_targetHealthFill != null)
            {
                _targetHealthFill.fillAmount = maxHealth <= 0
                    ? 0f
                    : Mathf.Clamp01(health / (float)maxHealth);
            }
        }

        /// <summary>Stops drawing a target.</summary>
        public void ClearTarget()
        {
            ShowTarget(null, 0, 0);
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
