using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>The server list, as the account authority reported it.</summary>
    /// <remarks>
    /// <b>Two ways to look, one way to decide.</b> With authored art the screen draws a
    /// framed table -- name, status, players -- and a player highlights a row and presses
    /// Enter. Without art it draws the plain list it always did. Both end in
    /// <see cref="Pick"/>, which is the only place a server is chosen, so the behaviour
    /// tests exercise is the behaviour a player gets.
    ///
    /// <b>Only what the runtime knows is shown.</b> Names, status and population come from
    /// <c>ServerRowViewData</c>, which the authority filled in. There is no ping anywhere in
    /// this project, so the ping column shows a dash rather than a number somebody made up.
    /// </remarks>
    public sealed class ServerSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a server";

        protected override string EmptyMessage => "No available servers";

        /// <summary>This screen paints its own table, so the plain rows are always empty.</summary>
        protected override bool HasContent => _painted.Count > 0 || base.HasContent;

        /// <summary>Raised when the flow service accepted a server.</summary>
        public event System.Action Selected;

        /// <summary>Raised when the player asked to go back to the login screen.</summary>
        public event System.Action SignedOut;

        /// <summary>The row a player has highlighted but not yet confirmed.</summary>
        public ServerId Highlighted { get; private set; }

        /// <summary>Whether there is a highlighted row Enter would act on.</summary>
        public bool HasHighlight => Highlighted.IsValid;

        private readonly List<RowWidgets> _painted = new List<RowWidgets>();

        private RectTransform _list;
        private Button _enter;
        private Button _back;
        private TextMeshProUGUI _enterLabel;

        private struct RowWidgets
        {
            public ServerId Server;
            public Image Background;
            public bool Selectable;
        }

        protected override void Fetch()
        {
            Session.FetchServers();
        }

        // ---- the plain list, for a build with no art and for every existing test ------------

        protected override void BuildRows()
        {
            if (_list != null)
            {
                PaintRows();

                return;
            }

            IReadOnlyList<ServerRowViewData> servers = Session.Servers;

            for (int i = 0; i < servers.Count; i++)
            {
                ServerRowViewData row = servers[i];

                AddRow(row.NameKey.Key, Describe(row), row.IsSelectable,
                    () => Pick(row.Server));
            }
        }

        /// <summary>
        /// The detail line under a server's name.
        /// </summary>
        /// <remarks>Only values the view data actually carries. A population it does not
        /// know is left out rather than shown as zero, because zero players and unknown
        /// players are different things and one of them is a lie.</remarks>
        private static string Describe(in ServerRowViewData row)
        {
            string state = row.IsSelectable ? "Online" : row.Status.ToString();

            return row.PopulationKnown
                ? state + "  ~  " + row.Population + " online"
                : state;
        }

        // ---- the painted table -------------------------------------------------------------

        protected override void BuildExtra(RectTransform root)
        {
            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin == null || !skin.HasServerArt) return;

            // The plain list and the plain chrome are replaced, not drawn underneath.
            Content.parent.parent.gameObject.SetActive(false);

            Transform backdrop = root.Find("Backdrop");

            if (backdrop != null) backdrop.gameObject.SetActive(false);

            Transform title = root.Find("Title");

            if (title != null) title.gameObject.SetActive(false);

            if (skin.ServerBackground != null)
            {
                RectTransform scene = UiFactory.CreateStretched("Scene", root);
                scene.SetAsFirstSibling();

                var painted = scene.gameObject.AddComponent<Image>();
                painted.sprite = skin.ServerBackground;
                painted.raycastTarget = false;
            }

            BuildHeading(root, skin);

            RectTransform panel = UiFactory.CreateAnchored("Panel", root,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = skin.ServerPanel;
            panelImage.type = Image.Type.Simple;

            UiFactory.FitSprite(panel, PanelOpaque, PanelWidth, PanelHeight,
                new Vector2(0f, PanelY));

            _list = panel;

            BuildColumnHeaders(panel);
            BuildButtons(root, skin);
            PaintRows();
        }

        /// <summary>The compass, the title and the line under it.</summary>
        private void BuildHeading(RectTransform root, PreWorldUiSkin skin)
        {
            if (skin.ServerCompass != null)
            {
                RectTransform compass = UiFactory.CreateAnchored("Compass", root,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var mark = compass.gameObject.AddComponent<Image>();
                mark.sprite = skin.ServerCompass;
                mark.raycastTarget = false;

                UiFactory.FitSprite(compass, CompassOpaque, 118f, 124f,
                    new Vector2(-300f, 424f));
            }

            if (skin.ServerTitle != null)
            {
                RectTransform banner = UiFactory.CreateAnchored("TitleArt", root,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var art = banner.gameObject.AddComponent<Image>();
                art.sprite = skin.ServerTitle;
                art.raycastTarget = false;

                UiFactory.FitSprite(banner, TitleOpaque, 430f, 71f, new Vector2(50f, 442f));
            }

            TextMeshProUGUI subtitle = UiFactory.CreateLabel("Subtitle", root,
                "Choose a server to begin your adventure", 24f, TextAlignmentOptions.Left);

            subtitle.color = new Color(0.78f, 0.86f, 0.97f, 1f);

            RectTransform rect = subtitle.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 32f);
            rect.anchoredPosition = new Vector2(-165f, 388f);
        }

        /// <summary>The column headings above the rows.</summary>
        /// <remarks>Ping has no source anywhere in this project, so its column exists to keep
        /// the table's shape and every cell under it is a dash. Inventing a latency would be
        /// telling a player something about a server nobody measured.</remarks>
        private void BuildColumnHeaders(RectTransform panel)
        {
            Header(panel, "Server Name", NameX, TextAlignmentOptions.Left);
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
            rect.sizeDelta = new Vector2(260f, 30f);
            rect.anchoredPosition = new Vector2(x, HeaderY);
        }

        /// <summary>Back to the login screen, and into the world.</summary>
        private void BuildButtons(RectTransform root, PreWorldUiSkin skin)
        {
            _back = PaintedButton(root, "Back", "Back", skin.ServerButtonBack, BackOpaque,
                320f, 88f, new Vector2(-700f, -424f), out TextMeshProUGUI _);

            _back.onClick.AddListener(GoBack);

            _enter = PaintedButton(root, "Enter", "Enter", skin.ServerButtonEnter, EnterOpaque,
                330f, 82f, new Vector2(700f, -424f), out _enterLabel);

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

        /// <summary>
        /// Rebuilds the table from whatever the authority last returned.
        /// </summary>
        /// <remarks>Torn down and rebuilt rather than diffed: the list is at most a handful
        /// of rows and arrives once, so a diff would be more code than it saves.</remarks>
        private void PaintRows()
        {
            for (int i = 0; i < _painted.Count; i++)
            {
                if (_painted[i].Background != null) Destroy(_painted[i].Background.gameObject);
            }

            _painted.Clear();

            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            // The screen is built before it is bound -- EnsureBuilt runs first inside Bind --
            // so on the first pass there is no session to read a list from. The rows are
            // painted again by Rebuild the moment there is.
            if (skin == null || _list == null || Session == null) return;

            IReadOnlyList<ServerRowViewData> servers = Session.Servers;

            for (var i = 0; i < servers.Count; i++)
            {
                PaintRow(servers[i], i, skin);
            }

            RefreshHighlight();
        }

        private void PaintRow(ServerRowViewData row, int index, PreWorldUiSkin skin)
        {
            RectTransform host = UiFactory.CreateAnchored("Row " + row.NameKey.Key, _list,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = skin.ServerRowNormal;
            image.type = Image.Type.Simple;

            UiFactory.FitSprite(host, RowOpaque, RowWidth, RowHeight,
                new Vector2(0f, FirstRowY - (index * RowStep)));

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = row.IsSelectable;

            ServerId server = row.Server;
            button.onClick.AddListener(() => Highlight(server));

            Cell(host, row.NameKey.Key, NameX, TextAlignmentOptions.Left, 24f, Color.white);

            // The status dot, at whatever size its own image is drawn -- the two states are
            // not the same size inside their canvases.
            Sprite dot = row.IsSelectable ? skin.ServerStatusOnline : skin.ServerStatusMaintenance;

            if (dot != null)
            {
                RectTransform pip = UiFactory.CreateAnchored("Status", host,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var pipImage = pip.gameObject.AddComponent<Image>();
                pipImage.sprite = dot;
                pipImage.raycastTarget = false;

                UiFactory.FitSprite(pip,
                    row.IsSelectable ? OnlineOpaque : MaintenanceOpaque, 22f, 22f,
                    new Vector2(StatusX - 4f, 0f));
            }

            TextMeshProUGUI state = Cell(host, row.IsSelectable ? "Online" : row.Status.ToString(),
                StatusX + 24f, TextAlignmentOptions.Left, 22f, Color.white);

            state.color = row.IsSelectable
                ? new Color(0.80f, 0.92f, 0.82f, 1f)
                : new Color(0.95f, 0.72f, 0.70f, 1f);

            // Population only when the authority actually knows it, and a capacity only when
            // one was reported.
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

            _painted.Add(new RowWidgets
            {
                Server = row.Server,
                Background = image,
                Selectable = row.IsSelectable,
            });
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
            rect.sizeDelta = new Vector2(260f, 30f);
            rect.anchoredPosition = new Vector2(x, 0f);

            return label;
        }

        // ---- choosing ------------------------------------------------------------------------

        /// <summary>
        /// Marks a row without committing to it.
        /// </summary>
        /// <remarks>The design is two steps -- highlight, then Enter -- so a player can see
        /// what they picked before the client leaves the screen. Nothing is asked of the
        /// authority here; that is <see cref="Pick"/>, and it happens when Enter is pressed.</remarks>
        public void Highlight(ServerId server)
        {
            if (IsBusy) return;

            Highlighted = server;

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

                    bool on = Highlighted.IsValid && widgets.Server == Highlighted;

                    widgets.Background.sprite = on
                        ? skin.ServerRowSelected
                        : skin.ServerRowNormal;

                    UiFactory.FitSprite(widgets.Background.rectTransform,
                        on ? RowSelectedOpaque : RowOpaque, RowWidth, RowHeight,
                        new Vector2(0f, FirstRowY - (i * RowStep)));
                }
            }

            if (_enter != null) _enter.interactable = HasHighlight;
        }

        /// <summary>Asks the authority for the highlighted server.</summary>
        public void Confirm()
        {
            if (!HasHighlight) return;

            Pick(Highlighted);
        }

        /// <summary>Hands the session back and returns to the login screen.</summary>
        public void GoBack()
        {
            if (Session == null || IsBusy) return;

            IsBusy = true;

            bool released = Session.SignOut();

            IsBusy = false;

            if (released) SignedOut?.Invoke();
        }

        private void Pick(ServerId server)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult result = Session.SubmitSelectServer(server, RequestId.New());

            IsBusy = false;

            if (result.IsAccepted)
            {
                Selected?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }

        // ---- the numbers this screen is drawn from --------------------------------------------
        //
        // Every extent below was measured off the imported textures by scanning for alpha, for
        // the reason UiFactory.FitSprite exists: the art is drawn at different sizes and
        // offsets inside identically sized canvases.

        private static readonly Vector4 PanelOpaque =
            new Vector4(15f / 1448f, 181f / 1086f, 1433f / 1448f, 935f / 1086f);

        private static readonly Vector4 TitleOpaque =
            new Vector4(104f / 2048f, 200f / 683f, 1948f / 2048f, 506f / 683f);

        private static readonly Vector4 CompassOpaque =
            new Vector4(128f / 1254f, 114f / 1254f, 1125f / 1254f, 1159f / 1254f);

        private static readonly Vector4 RowOpaque =
            new Vector4(54f / 2048f, 216f / 683f, 1994f / 2048f, 474f / 683f);

        private static readonly Vector4 RowSelectedOpaque =
            new Vector4(54f / 2048f, 208f / 683f, 1994f / 2048f, 481f / 683f);

        private static readonly Vector4 OnlineOpaque =
            new Vector4(274f / 1254f, 293f / 1254f, 973f / 1254f, 981f / 1254f);

        private static readonly Vector4 MaintenanceOpaque =
            new Vector4(177f / 1254f, 195f / 1254f, 1074f / 1254f, 1083f / 1254f);

        private static readonly Vector4 BackOpaque =
            new Vector4(184f / 1774f, 228f / 887f, 1591f / 1774f, 616f / 887f);

        private static readonly Vector4 EnterOpaque =
            new Vector4(140f / 1774f, 241f / 887f, 1636f / 1774f, 613f / 887f);

        /// <summary>The frame, at the design's own proportion of 1920x1080.</summary>
        private const float PanelWidth = 1120f;

        private const float PanelHeight = 596f;
        private const float PanelY = 20f;

        /// <summary>A row, drawn at the art's own 7.5:1.</summary>
        private const float RowWidth = 1010f;

        private const float RowHeight = 134f;

        /// <summary>Centre-to-centre between rows.</summary>
        private const float RowStep = 118f;

        private const float FirstRowY = 74f;
        private const float HeaderY = 196f;

        // Column centres, as the reference places them across the table.
        private const float NameX = -455f;
        private const float StatusX = -110f;
        private const float PlayersX = 140f;
        private const float PingX = 385f;
    }
}
