using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>
    /// The sprites the pre-world screens wear.
    /// </summary>
    /// <remarks>
    /// <b>One skin, every screen.</b> Login, server select, channel select and character
    /// select are all built by <see cref="UiFactory"/>, so dressing the factory dresses all
    /// four at once. The alternative -- each screen loading its own art -- is four places to
    /// forget, and four ways for the same button to look different.
    ///
    /// <b>Optional by design.</b> Every field may be null, and the factory falls back to the
    /// flat colours it has always drawn. That is what keeps several thousand existing tests
    /// working: they build screens in a bare test scene where no skin asset is loaded, and
    /// they assert behaviour rather than appearance.
    ///
    /// <b>It holds art and nothing else.</b> No text, no server names, no layout decisions
    /// and no logic. Labels are TMP at runtime, because text baked into an image cannot be
    /// localised and cannot show what a server is actually called.
    /// </remarks>
    [CreateAssetMenu(menuName = "ChibiFantasy/UI/Pre-World Skin", fileName = "PreWorldUiSkin")]
    public sealed class PreWorldUiSkin : ScriptableObject
    {
        /// <summary>Where the runtime looks for the one skin. A Resources path.</summary>
        public const string ResourcePath = "PreWorldUiSkin";

        [Header("Panels")]
        [SerializeField] private Sprite _panelLarge;
        [SerializeField] private Sprite _panelMedium;
        [SerializeField] private Sprite _panelSmall;
        [SerializeField] private Sprite _panelDark;

        [Header("Buttons: normal, hover, pressed")]
        [SerializeField] private Sprite _primaryNormal;
        [SerializeField] private Sprite _primaryHover;
        [SerializeField] private Sprite _primaryPressed;
        [SerializeField] private Sprite _secondaryNormal;
        [SerializeField] private Sprite _secondaryHover;
        [SerializeField] private Sprite _secondaryPressed;

        [Header("Inputs")]
        [SerializeField] private Sprite _inputNormal;
        [SerializeField] private Sprite _inputFocus;

        [Header("Decoration")]
        [SerializeField] private Sprite _logo;
        [SerializeField] private Sprite _divider;

        [Header("Login screen: its own authored art")]
        [SerializeField] private Sprite _loginBackground;
        [SerializeField] private Sprite _loginPanel;
        [SerializeField] private Sprite _loginLogo;
        [SerializeField] private Sprite _loginFieldId;
        [SerializeField] private Sprite _loginFieldPassword;
        [SerializeField] private Sprite _loginButtonNormal;
        [SerializeField] private Sprite _loginButtonHover;
        [SerializeField] private Sprite _loginButtonDisabled;
        [SerializeField] private Sprite _checkboxOff;
        [SerializeField] private Sprite _checkboxOn;

        [Header("Server select: its own authored art")]
        [SerializeField] private Sprite _serverBackground;
        [SerializeField] private Sprite _serverPanel;
        [SerializeField] private Sprite _serverTitle;
        [SerializeField] private Sprite _serverCompass;
        [SerializeField] private Sprite _serverRowNormal;
        [SerializeField] private Sprite _serverRowSelected;
        [SerializeField] private Sprite _serverStatusOnline;
        [SerializeField] private Sprite _serverStatusMaintenance;
        [SerializeField] private Sprite _serverButtonBack;
        [SerializeField] private Sprite _serverButtonEnter;

        [Header("Channel select: its own authored art")]
        [SerializeField] private Sprite _channelBackground;
        [SerializeField] private Sprite _channelPanel;
        [SerializeField] private Sprite _channelTitle;
        [SerializeField] private Sprite _channelRowNormal;
        [SerializeField] private Sprite _channelRowSelected;
        [SerializeField] private Sprite _channelStatusOnline;
        [SerializeField] private Sprite _channelStatusBusy;
        [SerializeField] private Sprite _channelStatusMaintenance;
        [SerializeField] private Sprite _channelButtonBack;
        [SerializeField] private Sprite _channelButtonEnter;

        [Header("Character select: its own authored art")]
        [SerializeField] private Sprite _characterBackground;
        [SerializeField] private Sprite _characterListPanel;
        [SerializeField] private Sprite _characterInfoPanel;
        [SerializeField] private Sprite _characterSlotNormal;
        [SerializeField] private Sprite _characterSlotSelected;
        [SerializeField] private Sprite _characterSlotCreate;
        [SerializeField] private Sprite _characterCompass;
        [SerializeField] private Sprite _characterPortrait;
        [SerializeField] private Sprite _characterButtonBack;
        [SerializeField] private Sprite _characterButtonEnter;

        [Header("Status icons")]
        [SerializeField] private Sprite _iconServer;
        [SerializeField] private Sprite _iconChannel;
        [SerializeField] private Sprite _iconUser;
        [SerializeField] private Sprite _iconOnline;
        [SerializeField] private Sprite _iconBusy;
        [SerializeField] private Sprite _iconFull;
        [SerializeField] private Sprite _iconLock;
        [SerializeField] private Sprite _iconMaintenance;

        public Sprite PanelLarge => _panelLarge;
        public Sprite PanelMedium => _panelMedium;
        public Sprite PanelSmall => _panelSmall;
        public Sprite PanelDark => _panelDark;
        public Sprite PrimaryNormal => _primaryNormal;
        public Sprite PrimaryHover => _primaryHover;
        public Sprite PrimaryPressed => _primaryPressed;
        public Sprite SecondaryNormal => _secondaryNormal;
        public Sprite SecondaryHover => _secondaryHover;
        public Sprite SecondaryPressed => _secondaryPressed;
        public Sprite InputNormal => _inputNormal;
        public Sprite InputFocus => _inputFocus;
        public Sprite Logo => _logo;
        public Sprite Divider => _divider;
        public Sprite IconServer => _iconServer;
        public Sprite IconChannel => _iconChannel;
        public Sprite IconUser => _iconUser;
        public Sprite IconOnline => _iconOnline;
        public Sprite IconBusy => _iconBusy;
        public Sprite IconFull => _iconFull;
        public Sprite IconLock => _iconLock;
        public Sprite IconMaintenance => _iconMaintenance;

        /// <summary>The painted scene behind the login form. Null means a flat colour.</summary>
        public Sprite LoginBackground => _loginBackground;

        public Sprite LoginPanel => _loginPanel;
        public Sprite LoginLogo => _loginLogo;

        /// <summary>Field art with the person icon already drawn into it.</summary>
        public Sprite LoginFieldId => _loginFieldId;

        /// <summary>Field art with the lock icon already drawn into it.</summary>
        public Sprite LoginFieldPassword => _loginFieldPassword;

        public Sprite LoginButtonNormal => _loginButtonNormal;
        public Sprite LoginButtonHover => _loginButtonHover;
        public Sprite LoginButtonDisabled => _loginButtonDisabled;
        public Sprite CheckboxOff => _checkboxOff;
        public Sprite CheckboxOn => _checkboxOn;

        public Sprite ServerBackground => _serverBackground;
        public Sprite ServerPanel => _serverPanel;
        public Sprite ServerTitle => _serverTitle;
        public Sprite ServerCompass => _serverCompass;
        public Sprite ServerRowNormal => _serverRowNormal;
        public Sprite ServerRowSelected => _serverRowSelected;
        public Sprite ServerStatusOnline => _serverStatusOnline;
        public Sprite ServerStatusMaintenance => _serverStatusMaintenance;
        public Sprite ServerButtonBack => _serverButtonBack;
        public Sprite ServerButtonEnter => _serverButtonEnter;

        public Sprite ChannelBackground => _channelBackground;
        public Sprite ChannelPanel => _channelPanel;
        public Sprite ChannelTitle => _channelTitle;
        public Sprite ChannelRowNormal => _channelRowNormal;
        public Sprite ChannelRowSelected => _channelRowSelected;
        public Sprite ChannelStatusOnline => _channelStatusOnline;
        public Sprite ChannelStatusBusy => _channelStatusBusy;
        public Sprite ChannelStatusMaintenance => _channelStatusMaintenance;
        public Sprite ChannelButtonBack => _channelButtonBack;
        public Sprite ChannelButtonEnter => _channelButtonEnter;

        public Sprite CharacterBackground => _characterBackground;
        public Sprite CharacterListPanel => _characterListPanel;
        public Sprite CharacterInfoPanel => _characterInfoPanel;
        public Sprite CharacterSlotNormal => _characterSlotNormal;
        public Sprite CharacterSlotSelected => _characterSlotSelected;
        public Sprite CharacterSlotCreate => _characterSlotCreate;
        public Sprite CharacterCompass => _characterCompass;

        /// <summary>
        /// The hero drawn beside the list.
        /// </summary>
        /// <remarks>One supplied illustration, not a likeness of the character chosen:
        /// nothing in this project renders a character's own appearance into a 2D image, and
        /// there is no per-character art to load. It stands in for every character, which is
        /// a placeholder and is reported as one.</remarks>
        public Sprite CharacterPortrait => _characterPortrait;

        public Sprite CharacterButtonBack => _characterButtonBack;
        public Sprite CharacterButtonEnter => _characterButtonEnter;

        /// <summary>Whether this skin carries the authored character-select art.</summary>
        public bool HasCharacterArt => _characterListPanel != null
            && _characterSlotNormal != null && _characterSlotSelected != null;

        /// <summary>Whether this skin carries the authored channel-select art.</summary>
        public bool HasChannelArt => _channelPanel != null && _channelRowNormal != null
            && _channelRowSelected != null;

        /// <summary>Whether this skin carries the authored server-select art.</summary>
        public bool HasServerArt => _serverPanel != null && _serverRowNormal != null
            && _serverRowSelected != null;

        /// <summary>Whether this skin carries the authored login art.</summary>
        public bool HasLoginArt => _loginPanel != null && _loginFieldId != null
            && _loginButtonNormal != null;

        private static PreWorldUiSkin _loaded;
        private static bool _looked;

        /// <summary>
        /// The skin, or null when this build has none.
        /// </summary>
        /// <remarks>Looked for once. A missing skin is a normal state -- a test scene, a
        /// server build -- and asking Resources for it every time a widget is built would
        /// be a file-system probe per button.</remarks>
        public static PreWorldUiSkin Active
        {
            get
            {
                if (_looked) return _loaded;

                _looked = true;
                _loaded = Resources.Load<PreWorldUiSkin>(ResourcePath);

                return _loaded;
            }
        }

        /// <summary>Forgets the cached skin. For tests that swap one in.</summary>
        public static void Reset(PreWorldUiSkin skin = null)
        {
            _loaded = skin;
            _looked = skin != null;
        }

        /// <summary>
        /// Dresses a button in normal/hover/pressed art, if this skin has any.
        /// </summary>
        /// <remarks>Sprite swap rather than colour tint, because the three states are three
        /// authored images. A button with no art keeps Unity's colour tint, which is what
        /// every existing test sees.</remarks>
        public void Dress(Button button, bool primary = true)
        {
            if (button == null) return;

            Sprite normal = primary ? _primaryNormal : _secondaryNormal;
            Sprite hover = primary ? _primaryHover : _secondaryHover;
            Sprite pressed = primary ? _primaryPressed : _secondaryPressed;

            if (normal == null) return;

            var image = button.GetComponent<Image>();

            if (image != null)
            {
                image.sprite = normal;
                image.type = Image.Type.Sliced;

                // The art carries its own colour; tinting it again would mute it.
                image.color = Color.white;
            }

            button.transition = Selectable.Transition.SpriteSwap;

            SpriteState state = button.spriteState;
            state.highlightedSprite = hover;
            state.pressedSprite = pressed;
            state.selectedSprite = hover;
            button.spriteState = state;
        }

        /// <summary>Dresses a panel image, if this skin has panel art.</summary>
        public void Dress(Image panel, Sprite sprite)
        {
            if (panel == null || sprite == null) return;

            panel.sprite = sprite;
            panel.type = Image.Type.Sliced;
            panel.color = Color.white;
        }
    }
}
