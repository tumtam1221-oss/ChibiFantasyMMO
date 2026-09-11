using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The characters on this account, and the way into the world.
    /// </summary>
    /// <remarks>
    /// <b>Scoped by the server, not by this screen.</b> The list comes from an endpoint that
    /// filters by the authenticated account in SQL; there is no filtering here to get wrong,
    /// and nothing this screen could ask that would return somebody else's characters.
    ///
    /// <b>Two steps, like the two screens before it.</b> Clicking a slot highlights it and
    /// fills the panel on the right; Enter World is what asks the authority. Both the slot
    /// and the button end in <see cref="Pick"/>, the only place a character is chosen, so
    /// the painted screen and the plain list cannot diverge.
    ///
    /// <b>No creation.</b> Character creation exists as a domain service but has no
    /// production screen. The empty slots are drawn because the art has them and a player
    /// should see how many they have left, and saying so is all they do. Reported as a
    /// limitation rather than wired to something that would fail.
    /// </remarks>
    public sealed class CharacterSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a character";

        protected override string EmptyMessage => "No characters on this account";

        /// <summary>
        /// This screen paints its own slots, so the plain rows are always empty.
        /// </summary>
        /// <remarks>Only character slots count. An account with none of its own still has
        /// empty slots drawn, and "no characters on this account" is exactly what should be
        /// written under them.</remarks>
        protected override bool HasContent => _painted.Count > 0 || base.HasContent;

        /// <summary>Raised when the server authorised world entry.</summary>
        public event System.Action<EnterWorldResult> WorldAuthorised;

        /// <summary>Raised when the player asked to leave this screen.</summary>
        public event System.Action WentBack;

        /// <summary>The slot a player has clicked but not yet entered the world with.</summary>
        public CharacterId Highlighted { get; private set; }

        /// <summary>Whether there is a highlighted slot Enter World would act on.</summary>
        public bool HasHighlight => Highlighted.IsValid;

        private readonly List<SlotWidgets> _painted = new List<SlotWidgets>();

        private RectTransform _list;
        private Button _enter;

        private TextMeshProUGUI _infoName;
        private TextMeshProUGUI _infoLevel;
        private TextMeshProUGUI _infoClass;
        private TextMeshProUGUI _infoLocation;

        private struct SlotWidgets
        {
            public CharacterId Character;
            public Image Background;
        }

        protected override void Fetch()
        {
            Session.FetchCharacters();
        }

        // ---- the plain list, for a build with no art and for every existing test -------------

        protected override void BuildRows()
        {
            if (_list != null)
            {
                PaintSlots();

                return;
            }

            IReadOnlyList<CharacterRowViewData> characters = Session.Characters;

            for (int i = 0; i < characters.Count; i++)
            {
                CharacterRowViewData row = characters[i];

                AddRow(row.Name, Describe(row), row.IsSelectable, () => Pick(row.Character));
            }
        }

        private static string Describe(in CharacterRowViewData row)
        {
            return "Level " + row.Level;
        }

        // ---- the painted screen -----------------------------------------------------------------

        protected override void BuildExtra(RectTransform root)
        {
            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin == null || !skin.HasCharacterArt) return;

            Content.parent.parent.gameObject.SetActive(false);

            Transform backdrop = root.Find("Backdrop");

            if (backdrop != null) backdrop.gameObject.SetActive(false);

            Transform title = root.Find("Title");

            if (title != null) title.gameObject.SetActive(false);

            if (skin.CharacterBackground != null)
            {
                RectTransform scene = UiFactory.CreateStretched("Scene", root);
                scene.SetAsFirstSibling();

                var painted = scene.gameObject.AddComponent<Image>();
                painted.sprite = skin.CharacterBackground;
                painted.raycastTarget = false;
            }

            BuildHeading(root, skin);
            BuildPortrait(root, skin);

            RectTransform panel = UiFactory.CreateAnchored("Panel", root,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = skin.CharacterListPanel;
            panelImage.type = Image.Type.Simple;

            UiFactory.FitSprite(panel, ListPanelOpaque, ListPanelWidth, ListPanelHeight,
                new Vector2(ListPanelX, ListPanelY));

            _list = panel;

            BuildInfoPanel(root, skin);
            BuildButtons(root, skin);
            PaintSlots();
        }

        /// <summary>The compass, the title and the line under it.</summary>
        private static void BuildHeading(RectTransform root, PreWorldUiSkin skin)
        {
            if (skin.CharacterCompass != null)
            {
                RectTransform compass = UiFactory.CreateAnchored("Compass", root,
                    new Vector2(0.5f, 0.5f), Vector2.one);

                var art = compass.gameObject.AddComponent<Image>();
                art.sprite = skin.CharacterCompass;
                art.raycastTarget = false;

                UiFactory.FitSprite(compass, CompassOpaque, CompassSize, CompassSize,
                    new Vector2(CompassX, HeadingY));
            }

            Legible(root, "Heading", "Select Character", 62f,
                new Vector2(TextX, HeadingY), Color.white, 78f, FontStyles.Bold);

            Legible(root, "Subtitle", "Choose your hero to enter the world", 26f,
                new Vector2(TextX + 4f, SubtitleY), new Color(0.86f, 0.92f, 1f, 1f), 34f,
                FontStyles.Normal);
        }

        /// <summary>
        /// A line of heading text that survives the painting behind it.
        /// </summary>
        /// <remarks>The background art is a sunlit scene, and white text laid straight onto
        /// its bright leaves is barely readable. A dark copy of the same line, offset by
        /// three units and drawn first, is what makes it legible over any of it -- cheaper
        /// than a material instance per label and it cannot be undone by a shader keyword.
        /// </remarks>
        private static TextMeshProUGUI Legible(RectTransform root, string name, string text,
            float size, Vector2 at, Color colour, float height, FontStyles style)
        {
            // The same weight on both, or the wider one shows past the other and the line
            // reads as its own last word twice.
            Line(root, name + " Shadow", text, size, at + new Vector2(3f, -3f),
                new Color(0.02f, 0.05f, 0.12f, 0.85f), height, style);

            return Line(root, name, text, size, at, colour, height, style);
        }

        private static TextMeshProUGUI Line(RectTransform root, string name, string text,
            float size, Vector2 at, Color colour, float height, FontStyles style)
        {
            TextMeshProUGUI label = UiFactory.CreateLabel(name, root, text, size,
                TextAlignmentOptions.Left);

            label.color = colour;
            label.fontStyle = style;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(900f, height);
            rect.anchoredPosition = at;

            return label;
        }

        /// <summary>The hero standing between the list and the details.</summary>
        /// <remarks>The same illustration whatever is chosen. Nothing here renders a
        /// character's own appearance, so this is a placeholder and is reported as one
        /// rather than dressed up as a likeness.</remarks>
        private static void BuildPortrait(RectTransform root, PreWorldUiSkin skin)
        {
            if (skin.CharacterPortrait == null) return;

            RectTransform hero = UiFactory.CreateAnchored("Portrait", root,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var art = hero.gameObject.AddComponent<Image>();
            art.sprite = skin.CharacterPortrait;
            art.type = Image.Type.Simple;
            art.raycastTarget = false;

            UiFactory.FitSprite(hero, PortraitOpaque,
                PortraitHeight * PortraitAspect, PortraitHeight,
                new Vector2(PortraitX, PortraitY));
        }

        /// <summary>
        /// The details of the highlighted character.
        /// </summary>
        /// <remarks>The three rules drawn into the panel art divide it into a name and three
        /// fields, so the labels are placed on those bands rather than on a grid this screen
        /// invented. Every value comes from the row the authority sent.</remarks>
        private void BuildInfoPanel(RectTransform root, PreWorldUiSkin skin)
        {
            if (skin.CharacterInfoPanel == null) return;

            RectTransform panel = UiFactory.CreateAnchored("Info", root,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var art = panel.gameObject.AddComponent<Image>();
            art.sprite = skin.CharacterInfoPanel;
            art.type = Image.Type.Simple;

            UiFactory.FitSprite(panel, InfoPanelOpaque, InfoWidth, InfoHeight,
                new Vector2(InfoX, InfoY));

            _infoName = UiFactory.CreateLabel("Name", panel, string.Empty, 30f,
                TextAlignmentOptions.Center);

            _infoName.fontStyle = FontStyles.Bold;
            _infoName.color = Color.white;

            RectTransform name = _infoName.rectTransform;
            name.anchorMin = new Vector2(0.5f, 0.5f);
            name.anchorMax = new Vector2(0.5f, 0.5f);
            name.pivot = new Vector2(0.5f, 0.5f);
            name.sizeDelta = new Vector2(InfoWidth - 60f, 40f);
            name.anchoredPosition = new Vector2(0f, InfoNameY);

            _infoLevel = InfoRow(panel, "Level", InfoRow1Y);
            _infoClass = InfoRow(panel, "Class", InfoRow2Y);
            _infoLocation = InfoRow(panel, "Location", InfoRow3Y);
        }

        /// <summary>One labelled field, keyed left and valued right.</summary>
        private static TextMeshProUGUI InfoRow(RectTransform panel, string key, float y)
        {
            TextMeshProUGUI label = UiFactory.CreateLabel("Key " + key, panel, key, 22f,
                TextAlignmentOptions.Left);

            label.color = new Color(0.72f, 0.82f, 0.95f, 1f);

            RectTransform left = label.rectTransform;
            left.anchorMin = new Vector2(0.5f, 0.5f);
            left.anchorMax = new Vector2(0.5f, 0.5f);
            left.pivot = new Vector2(0f, 0.5f);
            left.sizeDelta = new Vector2(200f, 30f);
            left.anchoredPosition = new Vector2(InfoKeyX, y);

            TextMeshProUGUI value = UiFactory.CreateLabel("Value " + key, panel, "-", 22f,
                TextAlignmentOptions.Right);

            value.color = Color.white;

            RectTransform right = value.rectTransform;
            right.anchorMin = new Vector2(0.5f, 0.5f);
            right.anchorMax = new Vector2(0.5f, 0.5f);
            right.pivot = new Vector2(1f, 0.5f);
            right.sizeDelta = new Vector2(260f, 30f);
            right.anchoredPosition = new Vector2(InfoValueX, y);

            return value;
        }

        private void BuildButtons(RectTransform root, PreWorldUiSkin skin)
        {
            Button back = PaintedButton(root, "Back", "Back", skin.CharacterButtonBack,
                BackOpaque, BackWidth, BackHeight, new Vector2(BackX, ButtonY),
                BackLabelOffset);

            back.onClick.AddListener(GoBack);

            _enter = PaintedButton(root, "Enter", "Enter World", skin.CharacterButtonEnter,
                EnterOpaque, EnterWidth, EnterHeight, new Vector2(EnterX, ButtonY),
                EnterLabelOffset);

            _enter.onClick.AddListener(Confirm);
            _enter.interactable = false;
        }

        /// <summary>
        /// A button wearing one of the pack's images.
        /// </summary>
        /// <remarks>The label is offset rather than centred because both images already carry
        /// a mark of their own -- an arrow on one, a star on the other -- and text through the
        /// middle would sit on top of it.</remarks>
        private static Button PaintedButton(RectTransform parent, string name, string text,
            Sprite art, Vector4 opaque, float width, float height, Vector2 centre,
            float labelOffset)
        {
            RectTransform host = UiFactory.CreateAnchored(name, parent,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = art;
            image.type = Image.Type.Simple;

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            UiFactory.FitSprite(host, opaque, width, height, centre);

            TextMeshProUGUI label = UiFactory.CreateLabel("Label", host, text, 24f,
                TextAlignmentOptions.Center);

            label.fontStyle = FontStyles.Bold;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width * 0.6f, 34f);
            rect.anchoredPosition = new Vector2(labelOffset, 0f);

            return button;
        }

        // ---- the slots ----------------------------------------------------------------------------

        private void PaintSlots()
        {
            for (int i = 0; i < _painted.Count; i++)
            {
                if (_painted[i].Background != null) Destroy(_painted[i].Background.gameObject);
            }

            _painted.Clear();

            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            // Built before bound: EnsureBuilt runs inside Bind before the session is set.
            if (skin == null || _list == null || Session == null) return;

            IReadOnlyList<CharacterRowViewData> characters = Session.Characters;

            var drawn = 0;

            for (var i = 0; i < characters.Count && drawn < SlotsShown; i++, drawn++)
            {
                PaintCharacter(characters[i], drawn, skin);
            }

            // The rest of the account's slots, as far as the frame has room for. They are
            // drawn because a player should see how many characters they may still make,
            // and they say so and nothing else -- creation has no screen to open.
            int empty = Mathf.Min(SlotsShown, SlotsOn(Session)) - drawn;

            for (var i = 0; i < empty; i++) PaintCreateSlot(drawn + i, skin);

            if (!HasHighlight) HighlightFirst(characters);

            RefreshHighlight();
        }

        /// <summary>How many slots the account has, clamped to something drawable.</summary>
        /// <remarks>An unset slot limit reads as int.MaxValue, which is a fine answer for a
        /// rule and a useless one for a frame with room for four.</remarks>
        private static int SlotsOn(SessionUiController session)
        {
            int slots = session.Flow.MaxCharacterSlots;

            return slots < SlotsShown ? slots : SlotsShown;
        }

        private void HighlightFirst(IReadOnlyList<CharacterRowViewData> characters)
        {
            for (var i = 0; i < characters.Count; i++)
            {
                if (!characters[i].IsSelectable) continue;

                Highlighted = characters[i].Character;

                return;
            }
        }

        private void PaintCharacter(CharacterRowViewData row, int index, PreWorldUiSkin skin)
        {
            RectTransform host = Slot("Slot " + row.Name, index, skin.CharacterSlotNormal,
                SlotNormalOpaque);

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = host.GetComponent<Image>();
            button.interactable = row.IsSelectable;

            CharacterId character = row.Character;
            button.onClick.AddListener(() => Highlight(character));

            PaintFace(host, skin);

            TextMeshProUGUI name = SlotLabel(host, row.Name, 26f, SlotNameY);
            name.fontStyle = FontStyles.Bold;
            name.color = Color.white;

            TextMeshProUGUI detail = SlotLabel(host,
                "Lv. " + row.Level + "   " + Readable(row.Class), 21f, SlotDetailY);

            detail.color = new Color(0.76f, 0.85f, 0.96f, 1f);

            _painted.Add(new SlotWidgets
            {
                Character = row.Character,
                Background = host.GetComponent<Image>(),
            });
        }

        private void PaintCreateSlot(int index, PreWorldUiSkin skin)
        {
            Sprite art = skin.CharacterSlotCreate != null
                ? skin.CharacterSlotCreate
                : skin.CharacterSlotNormal;

            RectTransform host = Slot("Create " + index, index, art, SlotCreateOpaque);

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = host.GetComponent<Image>();
            button.onClick.AddListener(SayCreationIsUnavailable);

            TextMeshProUGUI label = SlotLabel(host, "Create Character", 24f, 0f);
            label.color = new Color(0.84f, 0.90f, 0.98f, 1f);
        }

        /// <summary>The one honest thing an unwired button can do.</summary>
        private void SayCreationIsUnavailable()
        {
            SetStatus("Character creation is not available yet");
        }

        private RectTransform Slot(string name, int index, Sprite art, Vector4 opaque)
        {
            RectTransform host = UiFactory.CreateAnchored(name, _list,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = host.gameObject.AddComponent<Image>();
            image.sprite = art;
            image.type = Image.Type.Simple;

            UiFactory.FitSprite(host, opaque, SlotWidth, SlotHeight,
                new Vector2(0f, FirstSlotY - (index * SlotStep)));

            return host;
        }

        /// <summary>
        /// The hero's head, in the round well the slot art draws.
        /// </summary>
        /// <remarks>
        /// <b>A crop, not a second image.</b> There is no headshot in this project and no
        /// per-character art at all, so the same illustration the screen already shows is
        /// scaled up and clipped to its head. The mask is the square that fits inside the
        /// well's ring, so nothing spills over the art around it.
        /// </remarks>
        private static void PaintFace(RectTransform host, PreWorldUiSkin skin)
        {
            if (skin.CharacterPortrait == null) return;

            RectTransform well = UiFactory.CreateAnchored("Face", host,
                new Vector2(0.5f, 0.5f), new Vector2(FaceSize, FaceSize));

            well.anchoredPosition = new Vector2(FaceX, 0f);
            well.gameObject.AddComponent<RectMask2D>();

            RectTransform face = UiFactory.CreateAnchored("Art", well,
                new Vector2(0.5f, 0.5f), Vector2.one);

            var image = face.gameObject.AddComponent<Image>();
            image.sprite = skin.CharacterPortrait;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            float width = FaceSize / HeadSpan;
            float height = width * PortraitTall;

            face.sizeDelta = new Vector2(width, height);
            face.anchoredPosition = new Vector2((0.5f - HeadU) * width,
                (0.5f - HeadV) * height);
        }

        private static TextMeshProUGUI SlotLabel(RectTransform host, string text, float size,
            float y)
        {
            TextMeshProUGUI label = UiFactory.CreateLabel("Label", host, text, size,
                TextAlignmentOptions.Left);

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(SlotWidth * 0.6f, 32f);
            rect.anchoredPosition = new Vector2(SlotTextX, y);

            return label;
        }

        /// <summary>
        /// A definition id as something to read.
        /// </summary>
        /// <remarks>Presentation only: the last segment of the id, underscores opened out and
        /// each word capitalised. It invents nothing -- an id with no name behind it shows a
        /// dash rather than a word this screen made up.</remarks>
        private static string Readable(DefinitionId id)
        {
            string value = id.Value;

            if (string.IsNullOrEmpty(value)) return "-";

            int dot = value.LastIndexOf('.');

            if (dot >= 0 && dot < value.Length - 1) value = value.Substring(dot + 1);

            string[] parts = value.Split('_');

            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;

                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }

            return string.Join(" ", parts);
        }

        // ---- choosing -----------------------------------------------------------------------------

        /// <summary>Marks a slot without committing to it.</summary>
        public void Highlight(CharacterId character)
        {
            if (IsBusy) return;

            Highlighted = character;

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
                    SlotWidgets widgets = _painted[i];

                    if (widgets.Background == null) continue;

                    bool on = Highlighted.IsValid && widgets.Character == Highlighted;

                    widgets.Background.sprite = on
                        ? skin.CharacterSlotSelected
                        : skin.CharacterSlotNormal;

                    UiFactory.FitSprite(widgets.Background.rectTransform,
                        on ? SlotSelectedOpaque : SlotNormalOpaque, SlotWidth, SlotHeight,
                        new Vector2(0f, FirstSlotY - (i * SlotStep)));
                }
            }

            if (_enter != null) _enter.interactable = HasHighlight;

            RefreshInfo();
        }

        /// <summary>Fills the panel on the right from the highlighted row.</summary>
        private void RefreshInfo()
        {
            if (_infoName == null || Session == null) return;

            IReadOnlyList<CharacterRowViewData> characters = Session.Characters;

            for (var i = 0; i < characters.Count; i++)
            {
                CharacterRowViewData row = characters[i];

                if (!Highlighted.IsValid || row.Character != Highlighted) continue;

                _infoName.text = row.Name;
                _infoLevel.text = row.Level.ToString();
                _infoClass.text = Readable(row.Class);
                _infoLocation.text = Readable(row.Map);

                return;
            }

            _infoName.text = string.Empty;
            _infoLevel.text = "-";
            _infoClass.text = "-";
            _infoLocation.text = "-";
        }

        /// <summary>Enters the world with the highlighted character.</summary>
        public void Confirm()
        {
            if (!HasHighlight) return;

            Pick(Highlighted);
        }

        /// <summary>
        /// Steps back from the character list.
        /// </summary>
        /// <remarks>The session has already chosen a server and a channel, and this project's
        /// flow has no "unchoose". Rather than invent one, this hands the session back
        /// entirely -- the same real sign-out the two screens before it perform -- and the
        /// driver returns the client to the login screen. Reported as a limitation rather
        /// than faked as a one-step-back.</remarks>
        public void GoBack()
        {
            if (Session == null || IsBusy) return;

            IsBusy = true;

            bool released = Session.SignOut();

            IsBusy = false;

            if (released) WentBack?.Invoke();
        }

        /// <summary>
        /// Chooses a character and asks to enter the world.
        /// </summary>
        /// <remarks>Two steps because the server treats them as two: selecting is a session
        /// transition that can be refused on its own, and entering revalidates the server,
        /// the channel and the client version. A screen that jumped straight to the world
        /// scene would be skipping the half that admits the player.</remarks>
        private void Pick(CharacterId character)
        {
            if (IsBusy) return;

            IsBusy = true;

            SessionResult selected = Session.SubmitSelectCharacter(character,
                RequestId.New());

            if (!selected.IsAccepted)
            {
                IsBusy = false;
                SetStatus(Explain(selected.Reason));

                return;
            }

            SetStatus("Entering world...");

            EnterWorldResult entry = Session.SubmitEnterWorld(RequestId.New());

            IsBusy = false;

            if (entry.IsAccepted)
            {
                WorldAuthorised?.Invoke(entry);

                return;
            }

            SetStatus(Explain(entry.Reason));
        }

        // ---- the numbers this screen is drawn from --------------------------------------------------
        //
        // Every extent below was measured off the imported textures by scanning for alpha, and
        // is written as the fraction of the image its artwork actually covers. The pack draws
        // each piece at a different size inside its canvas -- the list frame fills 86% of one
        // image, the info frame 44% of another -- so equal rects would render at unequal
        // sizes. UiFactory.FitSprite compensates; these are what it compensates with.

        private static readonly Vector4 ListPanelOpaque =
            new Vector4(78f / 1086f, 66f / 1448f, 1009f / 1086f, 1447f / 1448f);

        private static readonly Vector4 InfoPanelOpaque =
            new Vector4(48f / 1086f, 385f / 1448f, 1038f / 1086f, 1029f / 1448f);

        private static readonly Vector4 SlotNormalOpaque =
            new Vector4(66f / 2172f, 134f / 724f, 2109f / 2172f, 607f / 724f);

        private static readonly Vector4 SlotSelectedOpaque =
            new Vector4(79f / 2172f, 145f / 724f, 2093f / 2172f, 588f / 724f);

        private static readonly Vector4 SlotCreateOpaque =
            new Vector4(44f / 2172f, 121f / 724f, 2123f / 2172f, 617f / 724f);

        private static readonly Vector4 CompassOpaque =
            new Vector4(62f / 1254f, 68f / 1254f, 1190f / 1254f, 1216f / 1254f);

        private static readonly Vector4 PortraitOpaque =
            new Vector4(119f / 1086f, 36f / 1448f, 1073f / 1086f, 1428f / 1448f);

        private static readonly Vector4 BackOpaque =
            new Vector4(249f / 1774f, 249f / 887f, 1526f / 1774f, 643f / 887f);

        private static readonly Vector4 EnterOpaque =
            new Vector4(108f / 1983f, 216f / 793f, 1875f / 1983f, 607f / 793f);

        // ---- heading ---------------------------------------------------------------------------------

        private const float HeadingY = 372f;
        private const float SubtitleY = 306f;
        private const float CompassX = -800f;
        private const float CompassSize = 118f;
        private const float TextX = -706f;

        // ---- the list frame and its slots --------------------------------------------------------------

        private const float ListPanelWidth = 600f;
        private const float ListPanelHeight = 660f;
        private const float ListPanelX = -640f;
        private const float ListPanelY = -60f;

        /// <summary>How many slots the frame has room for.</summary>
        /// <remarks>Four, because four is what fits. An account allowed more than four
        /// characters would have the rest unreachable, which is reported rather than papered
        /// over with a scroll view this screen has no art for.</remarks>
        private const int SlotsShown = 4;

        private const float SlotWidth = 520f;
        private const float SlotHeight = 120f;
        private const float SlotStep = 138f;
        private const float FirstSlotY = 220f;

        /// <summary>Where text begins, clear of the round well drawn into the slot.</summary>
        private const float SlotTextX = -118f;

        private const float SlotNameY = 22f;
        private const float SlotDetailY = -22f;

        // ---- the head in the slot's well ------------------------------------------------------------

        /// <summary>The square that fits inside the well's ring.</summary>
        private const float FaceSize = 74f;

        private const float FaceX = -199f;

        /// <summary>Where the head sits in the portrait, as fractions of the image.</summary>
        private const float HeadU = 0.437f;

        private const float HeadV = 0.779f;

        /// <summary>How much of the image's width the head crop takes.</summary>
        private const float HeadSpan = 0.571f;

        /// <summary>The portrait image's own height over its width.</summary>
        private const float PortraitTall = 1448f / 1086f;

        // ---- the hero ---------------------------------------------------------------------------------

        private const float PortraitHeight = 690f;

        /// <summary>Its drawn width over its drawn height, so it is never squashed.</summary>
        private const float PortraitAspect = 954f / 1392f;

        private const float PortraitX = 250f;
        private const float PortraitY = -35f;

        // ---- the details panel --------------------------------------------------------------------------

        private const float InfoWidth = 500f;
        private const float InfoHeight = 325f;
        private const float InfoX = 640f;
        private const float InfoY = -40f;

        /// <summary>The bands the three rules drawn into the panel art divide it into.</summary>
        private const float InfoNameY = 108f;

        private const float InfoRow1Y = 11f;
        private const float InfoRow2Y = -70f;
        private const float InfoRow3Y = -130f;

        private const float InfoKeyX = -190f;
        private const float InfoValueX = 190f;

        // ---- the two buttons ------------------------------------------------------------------------------

        private const float ButtonY = -452f;

        private const float BackWidth = 340f;
        private const float BackHeight = 105f;
        private const float BackX = -680f;

        /// <summary>Right of the arrow the art already draws.</summary>
        private const float BackLabelOffset = 61f;

        private const float EnterWidth = 470f;
        private const float EnterHeight = 104f;
        private const float EnterX = 620f;

        /// <summary>Left of the star the art already draws.</summary>
        private const float EnterLabelOffset = -66f;
    }
}
