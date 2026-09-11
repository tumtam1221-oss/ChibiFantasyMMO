using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Builds the placeholder production UI the client screens are made of.
    /// </summary>
    /// <remarks>
    /// <b>Built in code, on purpose.</b> A screen assembled here is one file a reviewer can
    /// read and a test can instantiate without loading a scene; the same screen dragged
    /// together in the editor is forty kilobytes of YAML that nobody can review and that
    /// merges badly. The scenes hold a canvas and a component, and the layout lives where it
    /// can be reasoned about.
    ///
    /// <b>Anchored, never hand-placed.</b> Every element below anchors to a corner or
    /// stretches, and the canvas scales from a 1920x1080 reference -- so 1600x900 and
    /// 1280x720 are the same layout at a different size rather than three layouts.
    ///
    /// <b>Placeholder, and honest about it.</b> Flat panels, one accent colour, readable
    /// type. This is the shape of a fantasy MMO screen without pretending to be its art, and
    /// no third-party UI asset is involved.
    /// </remarks>
    public static class UiFactory
    {
        /// <summary>The design resolution every screen is authored against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        public static readonly Color Backdrop = new Color(0.09f, 0.10f, 0.14f, 1f);
        public static readonly Color Panel = new Color(0.16f, 0.18f, 0.24f, 0.96f);
        public static readonly Color Slot = new Color(0.22f, 0.24f, 0.31f, 1f);
        public static readonly Color Accent = new Color(0.36f, 0.62f, 0.93f, 1f);
        public static readonly Color Danger = new Color(0.86f, 0.36f, 0.36f, 1f);
        public static readonly Color Ink = new Color(0.92f, 0.94f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f, 1f);

        /// <summary>
        /// A full-screen canvas that scales rather than stretches.
        /// </summary>
        /// <remarks><c>ScaleWithScreenSize</c> and a match of one half: width and height
        /// both matter for a desktop window that can be any shape, and matching only width
        /// makes a short window cut the bottom off.</remarks>
        public static Canvas CreateCanvas(string name, GameObject parent = null)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));

            if (parent != null) host.transform.SetParent(parent.transform, false);

            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>A rectangle stretched to fill its parent, inset by a margin.</summary>
        public static RectTransform CreateStretched(string name, Transform parent,
            float margin = 0f)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();

            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);

            return rect;
        }

        /// <summary>A fixed-size rectangle anchored to a point in its parent.</summary>
        /// <remarks>The anchor is where it sticks; the size is in reference pixels and the
        /// canvas scales it. That is the difference between a layout and a screenshot.</remarks>
        public static RectTransform CreateAnchored(string name, Transform parent,
            Vector2 anchor, Vector2 size, Vector2 offset = default)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();

            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;

            return rect;
        }

        /// <summary>A flat coloured panel.</summary>
        public static Image CreatePanel(string name, Transform parent, Color colour)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image))
                .GetComponent<Image>();

            image.rectTransform.SetParent(parent, false);
            image.color = colour;

            return image;
        }

        /// <summary>
        /// Sizes and places a rect so the drawn part of its sprite lands on an exact box.
        /// </summary>
        /// <remarks>
        /// <b>Why this is needed at all.</b> The art in this project's UI packs is drawn at
        /// different sizes and different offsets inside identically sized canvases -- two
        /// login fields whose bars differ by 138 pixels, two checkbox states differing by
        /// half, two buttons with different margins. Given equal rects they render at
        /// visibly unequal sizes. Inflating the rect by the inverse of the drawn extent puts
        /// the artwork where the layout asked for it, whatever the canvas around it does.
        ///
        /// <b>Sprites are drawn unsliced when this is used.</b> The mapping from texture to
        /// rect has to be linear for the arithmetic to hold, so callers set
        /// <see cref="Image.Type.Simple"/>.
        /// </remarks>
        /// <param name="opaque">Drawn extent as fractions of the texture: left, bottom,
        /// right, top.</param>
        /// <param name="centre">Where the drawn box should be centred, in the parent.</param>
        public static void FitSprite(RectTransform rect, Vector4 opaque, float width,
            float height, Vector2 centre)
        {
            if (rect == null) return;

            float spanX = opaque.z - opaque.x;
            float spanY = opaque.w - opaque.y;

            if (spanX <= 0f || spanY <= 0f) return;

            float w = width / spanX;
            float h = height / spanY;

            rect.sizeDelta = new Vector2(w, h);

            rect.anchoredPosition = new Vector2(
                centre.x + (w * (0.5f - ((opaque.x + opaque.z) * 0.5f))),
                centre.y + (h * (0.5f - ((opaque.y + opaque.w) * 0.5f))));

            ConfineHitArea(rect, opaque);
        }

        /// <summary>The child a fitted rect hands its pointer target to.</summary>
        private const string HitAreaName = "Hit Area";

        /// <summary>
        /// Moves a fitted rect's pointer target onto the drawn part of its art.
        /// </summary>
        /// <remarks>
        /// <b>Fitting inflates the rect, and a rect is what UGUI hit-tests.</b> A login
        /// field whose bar fills 44% of its image is given a rect more than twice the height
        /// of the bar, and the invisible remainder still swallows clicks -- enough for the
        /// two fields to overlap, so that clicking the login field put the caret in the
        /// password one. Rows on the selection screens overlap each other the same way.
        ///
        /// <b>The graphic stops being the target and a child takes over.</b> The child is
        /// anchored to the drawn fractions, so it tracks the artwork through every resize
        /// and every change of sprite. It carries a fully transparent image because UGUI
        /// hit-tests graphics, not rects, and it is a child rather than a sibling so the
        /// click still reaches whatever handler the fitted object carries.
        /// </remarks>
        private static void ConfineHitArea(RectTransform rect, Vector4 opaque)
        {
            Transform found = rect.Find(HitAreaName);

            if (found == null)
            {
                var graphic = rect.GetComponent<Graphic>();

                // Nothing to move: a rect with no graphic of its own, or one already told
                // not to take clicks, was never a pointer target to begin with.
                if (graphic == null || !graphic.raycastTarget) return;

                graphic.raycastTarget = false;

                var host = new GameObject(HitAreaName, typeof(RectTransform), typeof(Image));
                host.layer = rect.gameObject.layer;
                host.transform.SetParent(rect, false);
                host.transform.SetAsFirstSibling();

                Image fill = host.GetComponent<Image>();
                fill.color = new Color(0f, 0f, 0f, 0f);
                fill.raycastTarget = true;

                found = host.transform;
            }

            var area = (RectTransform)found;

            area.anchorMin = new Vector2(opaque.x, opaque.y);
            area.anchorMax = new Vector2(opaque.z, opaque.w);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
        }

        /// <summary>A line of text.</summary>
        public static TextMeshProUGUI CreateLabel(string name, Transform parent, string text,
            float size = 24f, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var label = new GameObject(name, typeof(RectTransform),
                typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();

            label.rectTransform.SetParent(parent, false);
            label.text = text;
            label.fontSize = size;
            label.color = Ink;
            label.alignment = alignment;
            label.raycastTarget = false;

            return label;
        }

        /// <summary>A button with a label on it.</summary>
        public static Button CreateButton(string name, Transform parent, string text,
            out TextMeshProUGUI label)
        {
            Image background = CreatePanel(name, parent, Accent);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            // Art if this build has any, Unity's colour tint if not. A screen must look
            // right in a player's client and still build in a bare test scene.
            PreWorldUiSkin.Active?.Dress(button);

            label = CreateLabel("Label", background.transform, text, 22f,
                TextAlignmentOptions.Center);

            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            return button;
        }

        /// <summary>
        /// A single-line text field.
        /// </summary>
        /// <remarks>The password variant sets <c>contentType</c> to Password, which is what
        /// masks the characters -- a visual mask only, and the value still never leaves this
        /// machine except inside the login request.</remarks>
        public static TMP_InputField CreateField(string name, Transform parent,
            string placeholder, bool password = false)
        {
            Image background = CreatePanel(name, parent, Slot);

            PreWorldUiSkin skin = PreWorldUiSkin.Active;

            if (skin != null) skin.Dress(background, skin.InputNormal);

            var field = background.gameObject.AddComponent<TMP_InputField>();

            RectTransform viewport = CreateStretched("Text Area", background.transform, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = CreateLabel("Text", viewport, string.Empty, 22f);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI hint = CreateLabel("Placeholder", viewport, placeholder, 22f);
            hint.color = Muted;
            RectTransform hintRect = hint.rectTransform;
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;

            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = background;
            field.lineType = TMP_InputField.LineType.SingleLine;

            if (password)
            {
                field.contentType = TMP_InputField.ContentType.Password;
                field.asteriskChar = '*';
            }

            // TMP builds its caret object in OnEnable, and only when it already has a text
            // component to build it beside. AddComponent above ran OnEnable there and then,
            // before any of these lines -- so a field composed in code ends up with no caret
            // at all: it takes focus, it accepts typing, and it never shows a cursor.
            // Cycling the component runs OnEnable a second time, now that it has what it
            // needs. Only play mode builds one, which is also the only place it is seen.
            if (Application.isPlaying)
            {
                field.enabled = false;
                field.enabled = true;
            }

            return field;
        }

        /// <summary>
        /// A horizontal bar with a fill, for health and the like.
        /// </summary>
        /// <remarks>A filled <see cref="Image"/> rather than a <see cref="Slider"/>: a
        /// slider is an input control, and a health bar the player can drag is a bug waiting
        /// to be found.</remarks>
        public static Image CreateBar(string name, Transform parent, Color fill,
            out Image fillImage)
        {
            Image track = CreatePanel(name, parent, Slot);

            RectTransform fillRect = CreateStretched("Fill", track.transform, 2f);

            fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.color = fill;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;

            return track;
        }

        /// <summary>A vertical list inside a scroll view.</summary>
        /// <remarks>Returns the content rectangle, because that is what a caller fills. The
        /// layout group and fitter make rows size themselves rather than being placed.</remarks>
        public static RectTransform CreateScrollList(string name, Transform parent,
            out ScrollRect scroll)
        {
            Image frame = CreatePanel(name, parent, Panel);

            scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            RectTransform viewport = CreateStretched("Viewport", frame.transform, 6f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();

            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            return content;
        }

        /// <summary>A row in a list: a button carrying a title and a detail line.</summary>
        public static Button CreateRow(Transform parent, string title, string detail,
            out TextMeshProUGUI titleLabel, out TextMeshProUGUI detailLabel)
        {
            Button button = CreateButton("Row", parent, string.Empty, out TextMeshProUGUI _);

            var background = button.GetComponent<Image>();
            background.color = Slot;

            var rect = button.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 64f);

            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 64f;

            // The button factory's own centred label is not what a row wants.
            DestroyWidget(button.transform.GetChild(0).gameObject);

            titleLabel = CreateLabel("Title", button.transform, title, 22f);
            titleLabel.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            titleLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            titleLabel.rectTransform.offsetMax = new Vector2(-12f, -4f);

            detailLabel = CreateLabel("Detail", button.transform, detail, 16f);
            detailLabel.color = Muted;
            detailLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            detailLabel.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            detailLabel.rectTransform.offsetMin = new Vector2(12f, 4f);
            detailLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

            return button;
        }

        /// <summary>
        /// Destroys a widget this factory made, in whichever mode we are running.
        /// </summary>
        /// <remarks>
        /// <c>Destroy</c> is deferred to the end of the frame and there is no end of the
        /// frame outside play mode, so the editor refuses it outright and logs an error. The
        /// screens here are built and rebuilt by code rather than by a scene, which means
        /// they get driven in both modes -- a test rebuilding a list, an editor tool
        /// previewing one -- and the destroy has to work in both. Immediate is safe here
        /// because nothing is destroyed while it is being iterated over: the lists are
        /// cleared after the loop, never during it.
        /// </remarks>
        public static void DestroyWidget(GameObject widget)
        {
            if (widget == null) return;

            // Detached first, because Destroy is deferred to the end of the frame: a row
            // rebuilt mid-frame would otherwise hold the old icons and the new ones at the
            // same time, and anything reading the hierarchy -- a layout group, a test --
            // would see both. Immediate destruction has no such gap, but doing this in both
            // modes keeps them behaving identically.
            widget.transform.SetParent(null, false);

            if (Application.isPlaying) Object.Destroy(widget);
            else Object.DestroyImmediate(widget);
        }
    }
}
