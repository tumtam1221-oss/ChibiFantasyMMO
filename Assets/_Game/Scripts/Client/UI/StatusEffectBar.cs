using System.Collections.Generic;
using ChibiFantasy.Data;
using ChibiFantasy.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The buff and debuff rows under the vitals.
    /// </summary>
    /// <remarks>
    /// <b>Two rows, because the two mean opposite things.</b> A player glancing at their bar
    /// needs to know at once whether something good or something bad is happening to them,
    /// and a single mixed row makes that a reading exercise. Buffs sit above debuffs, both
    /// under the health bar, which is where a player already looks.
    ///
    /// <b>Rebuilt on change, counted down every frame.</b> Widgets are created when a
    /// snapshot replaces the previous one -- not per frame, and not per second. The timers
    /// tick locally between snapshots and only the one label that changed is rewritten,
    /// because rebuilding a row of strings sixty times a second is how a HUD ends up at the
    /// top of an allocation profile.
    ///
    /// <b>It shows what it was sent and nothing more.</b> Nothing here decides that an
    /// effect exists, has expired or should be hidden. A countdown reaching zero leaves the
    /// icon exactly where it is until the server sends a snapshot without it.
    /// </remarks>
    public sealed class StatusEffectBar : MonoBehaviour
    {
        /// <summary>One icon, its timer and its stack count.</summary>
        private sealed class Entry
        {
            public GameObject Root;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Timer;
            public TextMeshProUGUI Stacks;
            public string LastTimer = string.Empty;
        }

        private readonly List<Entry> _buffWidgets = new List<Entry>();
        private readonly List<Entry> _debuffWidgets = new List<Entry>();

        private StatusEffectPresenter _presenter;
        private RectTransform _buffRow;
        private RectTransform _debuffRow;
        private bool _built;

        /// <summary>What is being drawn, for a test to read.</summary>
        public StatusEffectPresenter Presenter => _presenter;

        /// <summary>The row beneficial effects are drawn in.</summary>
        public RectTransform BuffRow => _buffRow;

        /// <summary>The row harmful effects are drawn in.</summary>
        public RectTransform DebuffRow => _debuffRow;

        public int BuffCount => _buffWidgets.Count;

        public int DebuffCount => _debuffWidgets.Count;

        /// <summary>How many times the widgets have been rebuilt. One per snapshot, not per frame.</summary>
        public int RebuildCount { get; private set; }

        /// <summary>
        /// Builds the rows under an anchor the HUD supplies.
        /// </summary>
        /// <remarks>Attached to the anchor rather than owning a canvas of its own, so the
        /// bar moves with the vitals panel and there is one canvas per screen.</remarks>
        public void Compose(RectTransform anchor)
        {
            EnsureBuilt(anchor);
        }

        /// <summary>Binds the character this client owns and starts listening for snapshots.</summary>
        public bool Bind(CharacterNetworkEntity entity,
            IDefinitionRegistry<StatusEffectDefinition> effects)
        {
            Unbind();

            _presenter = new StatusEffectPresenter(effects);
            _presenter.Changed += Rebuild;

            bool bound = _presenter.Bind(entity);

            Rebuild();

            return bound;
        }

        public void Unbind()
        {
            if (_presenter != null)
            {
                _presenter.Changed -= Rebuild;
                _presenter.Unbind();
            }

            _presenter = null;

            Clear(_buffWidgets);
            Clear(_debuffWidgets);
        }

        /// <summary>
        /// Advances the countdowns and rewrites only the labels that moved.
        /// </summary>
        /// <remarks>Public so a test can step it deterministically. A frame is not a
        /// requirement for a timer to be correct.</remarks>
        public void Tick(float deltaSeconds)
        {
            if (_presenter == null) return;

            _presenter.Advance(deltaSeconds);

            Retime(_presenter.Buffs, _buffWidgets);
            Retime(_presenter.Debuffs, _debuffWidgets);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        // ---- drawing -----------------------------------------------------------------------

        /// <summary>Destroys both rows and builds them again from the current snapshot.</summary>
        /// <remarks>Wholesale, because the snapshot is wholesale. Reconciling widgets against
        /// a replaced list would be the client maintaining status state by applying
        /// differences, which is the thing the snapshot design exists to avoid.</remarks>
        private void Rebuild()
        {
            EnsureBuilt(null);

            Clear(_buffWidgets);
            Clear(_debuffWidgets);

            RebuildCount++;

            if (_presenter == null) return;

            Fill(_presenter.Buffs, _buffRow, _buffWidgets, UiFactory.Accent);
            Fill(_presenter.Debuffs, _debuffRow, _debuffWidgets, UiFactory.Danger);
        }

        private void Fill(IReadOnlyList<StatusEffectViewData> source, RectTransform row,
            List<Entry> into, Color tint)
        {
            if (row == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                into.Add(Create(source[i], row, tint));
            }
        }

        /// <summary>
        /// One icon.
        /// </summary>
        /// <remarks>
        /// <b>A placeholder square, deliberately.</b> No status art exists in this project,
        /// so a tinted square carrying the effect's name is what an honest build shows. It
        /// reads correctly, it is obviously not final, and it does not require importing
        /// somebody else's icons.
        /// </remarks>
        private static Entry Create(in StatusEffectViewData view, RectTransform row,
            Color tint)
        {
            Image square = UiFactory.CreatePanel(view.Effect.Value ?? "Status", row, tint);

            RectTransform rect = square.rectTransform;
            rect.sizeDelta = new Vector2(64f, 64f);

            var element = square.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 64f;
            element.preferredHeight = 64f;

            var entry = new Entry { Root = square.gameObject };

            entry.Name = UiFactory.CreateLabel("Name", rect, view.DisplayName, 12f,
                TextAlignmentOptions.Center);
            entry.Name.rectTransform.anchorMin = new Vector2(0f, 0.45f);
            entry.Name.rectTransform.anchorMax = new Vector2(1f, 1f);
            entry.Name.rectTransform.offsetMin = Vector2.zero;
            entry.Name.rectTransform.offsetMax = Vector2.zero;

            entry.Timer = UiFactory.CreateLabel("Timer", rect, view.RemainingLabel, 12f,
                TextAlignmentOptions.Center);
            entry.Timer.rectTransform.anchorMin = new Vector2(0f, 0f);
            entry.Timer.rectTransform.anchorMax = new Vector2(1f, 0.45f);
            entry.Timer.rectTransform.offsetMin = Vector2.zero;
            entry.Timer.rectTransform.offsetMax = Vector2.zero;

            entry.LastTimer = view.RemainingLabel;

            // Only when there is more than one. "x1" on every icon is noise.
            if (view.ShowStacks)
            {
                entry.Stacks = UiFactory.CreateLabel("Stacks", rect, "x" + view.Stacks, 14f,
                    TextAlignmentOptions.BottomRight);
                entry.Stacks.rectTransform.anchorMin = Vector2.zero;
                entry.Stacks.rectTransform.anchorMax = Vector2.one;
                entry.Stacks.rectTransform.offsetMin = new Vector2(0f, 2f);
                entry.Stacks.rectTransform.offsetMax = new Vector2(-4f, 0f);
            }

            return entry;
        }

        /// <summary>Rewrites a timer only when its text actually changed.</summary>
        private static void Retime(IReadOnlyList<StatusEffectViewData> source,
            List<Entry> widgets)
        {
            int count = source.Count < widgets.Count ? source.Count : widgets.Count;

            for (int i = 0; i < count; i++)
            {
                string label = source[i].RemainingLabel;

                if (label == widgets[i].LastTimer) continue;

                widgets[i].LastTimer = label;

                if (widgets[i].Timer != null) widgets[i].Timer.text = label;
            }
        }

        private static void Clear(List<Entry> widgets)
        {
            for (int i = 0; i < widgets.Count; i++)
            {
                UiFactory.DestroyWidget(widgets[i].Root);
            }

            widgets.Clear();
        }

        private void EnsureBuilt(RectTransform anchor)
        {
            if (_built) return;

            RectTransform parent = anchor != null
                ? anchor
                : GetComponent<RectTransform>();

            if (parent == null) return;

            _built = true;

            _buffRow = Row("Buffs", parent, 1f);
            _debuffRow = Row("Debuffs", parent, 0f);
        }

        /// <summary>One horizontal strip of icons.</summary>
        private static RectTransform Row(string name, RectTransform parent, float top)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;

            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, top);
            rect.anchorMax = new Vector2(1f, top);
            rect.pivot = new Vector2(0f, top);
            rect.sizeDelta = new Vector2(0f, 64f);
            rect.anchoredPosition = new Vector2(0f, top > 0.5f ? 72f : 0f);

            var layout = host.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.LowerLeft;

            return rect;
        }
    }
}
