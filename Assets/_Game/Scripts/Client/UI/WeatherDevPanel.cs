using ChibiFantasy.Client.World;
using ChibiFantasy.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// Four buttons that ask the server for a sky: rain on or off, snow on or off, and back
    /// to automatic.
    /// </summary>
    /// <remarks>
    /// <b>It asks; it does not set.</b> Every button sends a request and then waits to be
    /// told, exactly like any other client. The labels are driven by what the server last
    /// broadcast, so a press that the server refuses leaves the buttons where they were
    /// rather than showing a lie. That is also why there is no "apply locally" path: weather
    /// one player can see and another cannot is worse than no button at all.
    ///
    /// <b>It builds nothing on a release build.</b> The server refuses the message there, so
    /// a panel would be a row of buttons that quietly do nothing. <see cref="Awake"/> removes
    /// the component instead.
    ///
    /// <b>Its own canvas.</b> Hanging this off the player's HUD would put a development
    /// control inside the screen a player actually uses, and would make the HUD's layout
    /// depend on whether this build has the panel. A separate overlay canvas keeps the two
    /// from knowing about each other.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WeatherDevPanel : MonoBehaviour
    {
        [Tooltip("Draw the panel. Off hides it without removing the component.")]
        [SerializeField] private bool _show = true;

        private WorldClientBootstrap _world;
        private EnvironmentWeather _presenter;
        private Canvas _canvas;
        private TextMeshProUGUI _rainLabel;
        private TextMeshProUGUI _snowLabel;
        private TextMeshProUGUI _status;
        private WorldWeather _painted = (WorldWeather)(-1);
        private bool _built;

        private void Awake()
        {
            // A control whose message the server will not accept is not a control.
            if (!Debug.isDebugBuild)
            {
                Destroy(this);

                return;
            }
        }

        private void OnEnable()
        {
            if (!Debug.isDebugBuild || !_show) return;

            Build();
        }

        private void OnDisable()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);

            _canvas = null;
            _built = false;
        }

        private void Update()
        {
            if (!_built) return;

            if (_world == null) _world = FindFirstObjectByType<WorldClientBootstrap>(
                FindObjectsInactive.Exclude);

            if (_presenter == null) _presenter = FindFirstObjectByType<EnvironmentWeather>(
                FindObjectsInactive.Exclude);

            Repaint();
        }

        /// <summary>What the world says the sky is, or clear before it has said anything.</summary>
        private WorldWeather Current()
        {
            if (_world == null) return WorldWeather.Clear;

            return (WorldWeather)_world.LastWeather;
        }

        /// <summary>
        /// Keeps the labels honest.
        /// </summary>
        /// <remarks>Repainted only when the weather actually moved -- rewriting three strings
        /// every frame is how a debug panel ends up at the top of an allocation profile.
        /// The waiting line is the exception: it counts down, so it changes every frame it is
        /// on screen at all.</remarks>
        private void Repaint()
        {
            WorldWeather now = Current();
            float waiting = _presenter != null ? _presenter.SecondsUntilFalling : 0f;

            if (waiting > 0f)
            {
                _status.text = "clouding over -- " + waiting.ToString("F0") + "s";
                _status.color = UiFactory.Ink;
            }
            else if (now != _painted)
            {
                _status.text = now == WorldWeather.Clear ? "clear" : now.ToString().ToLower();
                _status.color = UiFactory.Muted;
            }

            if (now == _painted) return;

            _painted = now;

            _rainLabel.text = now == WorldWeather.Rain ? "Rain: ON" : "Rain: off";
            _snowLabel.text = now == WorldWeather.Snow ? "Snow: ON" : "Snow: off";
        }

        /// <summary>
        /// Presses a weather button.
        /// </summary>
        /// <remarks>Pressing the sky you are already under asks for automatic rather than for
        /// the same thing again, which is what makes one button an on and an off.</remarks>
        private void Toggle(WorldWeather weather)
        {
            if (_world == null) return;

            if (Current() == weather) _world.RequestAutomaticWeather();
            else _world.RequestWeather((int)weather);
        }

        private void Build()
        {
            if (_built) return;

            _canvas = UiFactory.CreateCanvas("Weather Dev Panel");

            // Above the HUD, so it is never the thing hidden behind an inventory window.
            _canvas.sortingOrder = 500;

            Image panel = UiFactory.CreatePanel("Panel", _canvas.transform, UiFactory.Panel);

            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(200f, 196f);
            rect.anchoredPosition = new Vector2(24f, -24f);

            TextMeshProUGUI title = UiFactory.CreateLabel("Title", panel.transform, "Weather",
                18f, TextAlignmentOptions.Center);
            Row(title.rectTransform, -10f, 22f);

            _status = UiFactory.CreateLabel("Status", panel.transform, "clear", 14f,
                TextAlignmentOptions.Center);
            _status.color = UiFactory.Muted;
            Row(_status.rectTransform, -32f, 18f);

            _rainLabel = AddButton(panel.transform, "Rain", "Rain: off", -56f,
                () => Toggle(WorldWeather.Rain));

            _snowLabel = AddButton(panel.transform, "Snow", "Snow: off", -100f,
                () => Toggle(WorldWeather.Snow));

            AddButton(panel.transform, "Automatic", "Automatic", -144f,
                () => { if (_world != null) _world.RequestAutomaticWeather(); });

            _built = true;
        }

        private static TextMeshProUGUI AddButton(Transform parent, string name, string text,
            float top, UnityEngine.Events.UnityAction action)
        {
            Button button = UiFactory.CreateButton(name, parent, text,
                out TextMeshProUGUI label);

            label.fontSize = 17f;
            Row(button.GetComponent<RectTransform>(), top, 38f);
            button.onClick.AddListener(action);

            return label;
        }

        /// <summary>One full-width row, measured down from the top of the panel.</summary>
        private static void Row(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-10f, 0f);
            rect.sizeDelta = new Vector2(-20f, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }
    }
}
