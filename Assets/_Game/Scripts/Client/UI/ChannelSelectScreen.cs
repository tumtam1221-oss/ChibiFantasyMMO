using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>The channels of the chosen server.</summary>
    /// <remarks>
    /// <b>The same two steps as the server screen.</b> A click highlights a channel and Enter
    /// confirms it, so a player sees what they picked before the client leaves the screen.
    /// Both paths end in <see cref="Pick"/>, the only place a channel is chosen.
    ///
    /// <b>Only what the runtime knows.</b> Names, status and population come from
    /// <c>ChannelRowViewData</c>. Ping is not measured anywhere in this project, so that
    /// column shows a dash rather than a number nobody took.
    /// </remarks>
    public sealed class ChannelSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a channel";

        protected override string EmptyMessage => "No available channels";

        /// <summary>This screen paints its own table, so the plain rows are always empty.</summary>
        protected override bool HasContent => _painted.Count > 0 || base.HasContent;

        public event System.Action Selected;

        /// <summary>Raised when the player asked to go back to the server list.</summary>
        public event System.Action WentBack;

        /// <summary>The row a player has highlighted but not yet confirmed.</summary>
        public ChannelId Highlighted { get; private set; }

        /// <summary>Whether there is a highlighted row Enter would act on.</summary>
        public bool HasHighlight => Highlighted.IsValid;

        private readonly List<RowWidgets> _painted = new List<RowWidgets>();

        private RectTransform _list;
        private Button _enter;

        private struct RowWidgets
        {
            public ChannelId Channel;
            public Image Background;
        }

        protected override void Fetch()
        {
            Session.FetchChannels();
        }

        // ---- the plain list, for a build with no art and for every existing test -------------

        protected override void BuildRows()
        {
            if (_list != null)
            {
                PaintRows();

                return;
            }

            IReadOnlyList<ChannelRowViewData> channels = Session.Channels;

            for (int i = 0; i < channels.Count; i++)
            {
                ChannelRowViewData row = channels[i];

                AddRow(row.NameKey.Key, Describe(row), row.IsSelectable,
                    () => Pick(row.Channel));
            }
        }

        /// <summary>PK is shown because the view data carries it. It is never set here.</summary>
        private static string Describe(in ChannelRowViewData row)
        {
            string state = row.IsSelectable ? "Open" : row.Status.ToString();

            if (row.PkEnabled) state += "  ~  PK";

            return row.PopulationKnown ? state + "  ~  " + row.Population + " online" : state;
        }

        // ---- the painted table ----------------------------------------------------------------

        protected override void BuildExtra(RectTransform root)
        {
            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin == null || !skin.HasChannelArt) return;

            Content.parent.parent.gameObject.SetActive(false);

            Transform backdrop = root.Find("Backdrop");

            if (backdrop != null) backdrop.gameObject.SetActive(false);

            Transform title = root.Find("Title");

            if (title != null) title.gameObject.SetActive(false);

            if (skin.ChannelBackground != null)
            {
                RectTransform scene = UiFactory.CreateStretched("Scene", root);
                scene.SetAsFirstSibling();

                var painted = scene.gameObject.AddComponent<Image>();
                painted.sprite = skin.ChannelBackground;
                painted.raycastTarget = false;
            }

            BuildHeading(root, skin);

            RectTransform panel = UiFactory.CreateAnchored("Panel", root,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = skin.ChannelPanel;
            panelImage.type = Image.Type.Simple;

            UiFactory.FitSprite(panel, PanelOpaque, PanelWidth, PanelHeight,
                new Vector2(0f, PanelY));

            _list = panel;

            BuildColumnHeaders(panel);
            BuildButtons(root, skin);
            PaintRows();
        }

        /// <summary>The title, which carries its own compass, and the line under it.</summary>
        private void BuildHeading(RectTransform root, PreWorldUiSkin skin)
        {
            if (skin.ChannelTitle != null)
            {
                RectTransform banner = UiFactory.CreateAnchored("TitleArt", root,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var art = banner.gameObject.AddComponent<Image>();
                art.sprite = skin.ChannelTitle;
                art.raycastTarget = false;

                UiFactory.FitSprite(banner, TitleOpaque, 540f, 166f, new Vector2(0f, 434f));
            }

            TextMeshProUGUI subtitle = UiFactory.CreateLabel("Subtitle", root,
                "Choose a channel to enter the world", 24f, TextAlignmentOptions.Left);

            subtitle.color = new Color(0.78f, 0.86f, 0.97f, 1f);

            RectTransform rect = subtitle.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 32f);
            rect.anchoredPosition = new Vector2(-80f, 374f);
        }

        /// <summary>The column headings above the rows.</summary>
        /// <remarks>Ping has no source in this project. The column keeps the table's shape
        /// and every cell under it is a dash, because a latency nobody measured is not a
        /// thing to tell a player.</remarks>
        private void BuildColumnHeaders(RectTransform panel)
        {
            Header(panel, "Channel Name", NameX, TextAlignmentOptions.Left);
            Header(panel, "Status", StatusX, TextAlignmentOptions.Left);
            Header(panel, "Players", PlayersX, TextAlignmentOptions.Center);
            Header(panel, "Ping", PingX, TextAlignmentOptions.Center);
        }

        private static void Header(RectTransform panel, string text, float x,
            TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = UiFactory.CreateLabel("Header " + text, panel, text, 22f,
                alignment);

            label.color = new Color(0.72f, 0.80f, 0.92f, 1f);
            label.fontStyle = FontStyles.Bold;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(alignment == TextAlignmentOptions.Left ? 0f : 0.5f, 0.5f);
            rect.sizeDelta = new Vector2(280f, 30f);
            rect.anchoredPosition = new Vector2(x, HeaderY);
        }

        private void BuildButtons(RectTransform root, PreWorldUiSkin skin)
        {
            Button back = PaintedButton(root, "Back", "Back", skin.ChannelButtonBack,
                BackOpaque, 320f, 88f, new Vector2(-700f, -424f), out TextMeshProUGUI _);

            back.onClick.AddListener(GoBack);

            _enter = PaintedButton(root, "Enter", "Enter", skin.ChannelButtonEnter, EnterOpaque,
                330f, 85f, new Vector2(700f, -424f), out TextMeshProUGUI _);

            _enter.onClick.AddListener(Confirm);
            _enter.interactable = false;
        }

        private static Button PaintedButton(RectTransform parent, string name, string text,
            Sprite art, Vector4 opaque, float width, float height, Vector2 centre,
            out TextMeshProUGUI label)
        {
            RectTransform host = UiFactory.CreateAnchored(name, parent,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = art;
            image.type = Image.Type.Simple;

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            UiFactory.FitSprite(host, opaque, width, height, centre);

            label = UiFactory.CreateLabel("Label", host, text, 24f,
                TextAlignmentOptions.Center);

            label.fontStyle = FontStyles.Bold;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0f, (host.rect.height - height) * 0.5f);
            rect.offsetMax = new Vector2(0f, -(host.rect.height - height) * 0.5f);

            return button;
        }

        private void PaintRows()
        {
            for (int i = 0; i < _painted.Count; i++)
            {
                if (_painted[i].Background != null) Destroy(_painted[i].Background.gameObject);
            }

            _painted.Clear();

            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            // Built before bound: EnsureBuilt runs inside Bind before the session is set.
            if (skin == null || _list == null || Session == null) return;

            IReadOnlyList<ChannelRowViewData> channels = Session.Channels;

            for (var i = 0; i < channels.Count; i++) PaintRow(channels[i], i, skin);

            RefreshHighlight();
        }

        private void PaintRow(ChannelRowViewData row, int index, PreWorldUiSkin skin)
        {
            RectTransform host = UiFactory.CreateAnchored("Row " + row.NameKey.Key, _list,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = skin.ChannelRowNormal;
            image.type = Image.Type.Simple;

            UiFactory.FitSprite(host, RowOpaque, RowWidth, RowHeight,
                new Vector2(0f, FirstRowY - (index * RowStep)));

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = row.IsSelectable;

            ChannelId channel = row.Channel;
            button.onClick.AddListener(() => Highlight(channel));

            Cell(host, row.NameKey.Key, NameX, TextAlignmentOptions.Left, 24f, Color.white);

            Sprite dot = DotFor(row.Status, skin);
            Vector4 dotOpaque = DotOpaqueFor(row.Status);

            if (dot != null)
            {
                RectTransform pip = UiFactory.CreateAnchored("Status", host,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var pipImage = pip.gameObject.AddComponent<Image>();
                pipImage.sprite = dot;
                pipImage.raycastTarget = false;

                UiFactory.FitSprite(pip, dotOpaque, 22f, 22f, new Vector2(StatusX - 4f, 0f));
            }

            TextMeshProUGUI state = Cell(host, row.Status.ToString(), StatusX + 24f,
                TextAlignmentOptions.Left, 22f, Color.white);

            state.color = ColourFor(row.Status);

            string players = row.PopulationKnown
                ? (row.Capacity > 0
                    ? row.Population + " / " + row.Capacity
                    : row.Population.ToString())
                : "-";

            Cell(host, players, PlayersX, TextAlignmentOptions.Center, 22f,
                new Color(0.88f, 0.92f, 0.98f, 1f));

            // No ping is measured anywhere in this project.
            Cell(host, "-", PingX, TextAlignmentOptions.Center, 22f,
                new Color(0.60f, 0.66f, 0.76f, 1f));

            _painted.Add(new RowWidgets { Channel = row.Channel, Background = image });
        }

        private static Sprite DotFor(ChannelStatus status, PreWorldUiSkin skin)
        {
            switch (status)
            {
                case ChannelStatus.Online: return skin.ChannelStatusOnline;
                case ChannelStatus.Busy: return skin.ChannelStatusBusy;
                default: return skin.ChannelStatusMaintenance;
            }
        }

        private static Vector4 DotOpaqueFor(ChannelStatus status)
        {
            switch (status)
            {
                case ChannelStatus.Online: return OnlineOpaque;
                case ChannelStatus.Busy: return BusyOpaque;
                default: return MaintenanceOpaque;
            }
        }

        private static Color ColourFor(ChannelStatus status)
        {
            switch (status)
            {
                case ChannelStatus.Online: return new Color(0.80f, 0.92f, 0.82f, 1f);
                case ChannelStatus.Busy: return new Color(0.97f, 0.82f, 0.66f, 1f);
                default: return new Color(0.78f, 0.80f, 0.86f, 1f);
            }
        }

        private static TextMeshProUGUI Cell(RectTransform row, string text, float x,
            TextAlignmentOptions alignment, float size, Color colour)
        {
            TextMeshProUGUI label = UiFactory.CreateLabel("Cell", row, text, size, alignment);

            label.color = colour;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(alignment == TextAlignmentOptions.Left ? 0f : 0.5f, 0.5f);
            rect.sizeDelta = new Vector2(280f, 30f);
            rect.anchoredPosition = new Vector2(x, 0f);

            return label;
        }

        // ---- choosing ---------------------------------------------------------------------------

        /// <summary>Marks a row without committing to it.</summary>
        public void Highlight(ChannelId channel)
        {
            if (IsBusy) return;

            Highlighted = channel;

            SetStatus(string.Empty);
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin != null)
            {
                for (var i = 0; i < _painted.Count; i++)
                {
                    RowWidgets widgets = _painted[i];

                    if (widgets.Background == null) continue;

                    bool on = Highlighted.IsValid && widgets.Channel == Highlighted;

                    widgets.Background.sprite = on
                        ? skin.ChannelRowSelected
                        : skin.ChannelRowNormal;

                    UiFactory.FitSprite(widgets.Background.rectTransform,
                        on ? RowSelectedOpaque : RowOpaque, RowWidth, RowHeight,
                        new Vector2(0f, FirstRowY - (i * RowStep)));
                }
            }

            if (_enter != null) _enter.interactable = HasHighlight;
        }

        /// <summary>Asks the authority for the highlighted channel.</summary>
        public void Confirm()
        {
            if (!HasHighlight) return;

            Pick(Highlighted);
        }

        /// <summary>
        /// Steps back to the server list.
        /// </summary>
        /// <remarks>The session has already chosen a server, and this project's flow has no
        /// "unchoose". Rather than invent one, this hands the session back entirely -- the
        /// same real sign-out the server screen's Back performs -- and the driver returns the
        /// client to the login screen. Reported as a limitation rather than faked as a
        /// one-step-back.</remarks>
        public void GoBack()
        {
            if (Session == null || IsBusy) return;

            IsBusy = true;

            bool released = Session.SignOut();

            IsBusy = false;

            if (released) WentBack?.Invoke();
        }

        private void Pick(ChannelId channel)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult result = Session.SubmitSelectChannel(channel, RequestId.New());

            IsBusy = false;

            if (result.IsAccepted)
            {
                Selected?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }

        // ---- the numbers this screen is drawn from ------------------------------------------------
        //
        // Measured off the imported textures by scanning for alpha. The normal and selected row
        // art are not the same size inside their canvases -- 1944x304 against 1976x268 -- so
        // both are fitted rather than given the same rect.

        private static readonly Vector4 PanelOpaque =
            new Vector4(20f / 1448f, 211f / 1086f, 1429f / 1448f, 920f / 1086f);

        private static readonly Vector4 TitleOpaque =
            new Vector4(81f / 2048f, 57f / 683f, 1976f / 2048f, 638f / 683f);

        private static readonly Vector4 RowOpaque =
            new Vector4(52f / 2048f, 193f / 683f, 1997f / 2048f, 498f / 683f);

        private static readonly Vector4 RowSelectedOpaque =
            new Vector4(36f / 2048f, 219f / 683f, 2013f / 2048f, 487f / 683f);

        private static readonly Vector4 OnlineOpaque =
            new Vector4(296f / 1254f, 304f / 1254f, 958f / 1254f, 957f / 1254f);

        private static readonly Vector4 BusyOpaque =
            new Vector4(326f / 1254f, 341f / 1254f, 926f / 1254f, 934f / 1254f);

        private static readonly Vector4 MaintenanceOpaque =
            new Vector4(365f / 1254f, 368f / 1254f, 896f / 1254f, 888f / 1254f);

        private static readonly Vector4 BackOpaque =
            new Vector4(123f / 1774f, 233f / 887f, 1651f / 1774f, 655f / 887f);

        private static readonly Vector4 EnterOpaque =
            new Vector4(97f / 1774f, 243f / 887f, 1678f / 1774f, 649f / 887f);

        private const float PanelWidth = 1080f;
        private const float PanelHeight = 660f;
        private const float PanelY = 0f;

        /// <summary>
        /// A row, sized to sit in the frame the way the server screen's rows do.
        /// </summary>
        /// <remarks>The channel row art is a plain rounded bar -- no icon, no detail -- so
        /// widening it past its own 6.4:1 only stretches two rounded caps, which reads
        /// correctly. Left at its natural proportion it was narrow inside a wide frame and
        /// looked stubby beside the server screen, which uses art that is already slimmer.
        /// The margin either side is now 50, matching that screen.</remarks>
        private const float RowWidth = 980f;

        private const float RowHeight = 128f;

        /// <summary>Centre-to-centre between rows. Four fit inside the frame.</summary>
        private const float RowStep = 124f;

        private const float FirstRowY = 122f;
        private const float HeaderY = 240f;

        private const float NameX = -441f;
        private const float StatusX = -107f;
        private const float PlayersX = 136f;
        private const float PingX = 374f;
    }
}
