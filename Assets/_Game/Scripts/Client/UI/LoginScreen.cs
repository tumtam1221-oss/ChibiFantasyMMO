using System.Collections.Generic;
using ChibiFantasy.Contracts;
using ChibiFantasy.Core;
using ChibiFantasy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChibiFantasy.Client.UI
{
    /// <summary>The login screen: an account, a password and one button.</summary>
    /// <remarks>
    /// <b>The password is masked, never logged and never held longer than the request.</b>
    /// It goes straight into the existing login call and the field is cleared afterwards,
    /// whether the attempt succeeded or not -- a failed attempt leaving the password on
    /// screen is how it ends up in a screenshot.
    ///
    /// A failure keeps the player here and says why in words. It never advances, and it
    /// never reports success it did not get.
    /// </remarks>
    public sealed class LoginScreen : SessionScreenBase
    {
        private TMP_InputField _account;
        private TMP_InputField _password;
        private Button _submit;
        private TextMeshProUGUI _submitLabel;

        protected override string Title => "Chibi Fantasy";

        /// <summary>
        /// Where the typed credentials go.
        /// </summary>
        /// <remarks>
        /// A delegate rather than a reference to the API, because <c>IAccountApi</c>
        /// deliberately carries no credential -- how a secret is collected and transmitted is
        /// the transport's business, and this screen names no transport. The composition
        /// wires this to whatever is actually sending the request.
        /// </remarks>
        public System.Action<string, string> Credentials { get; set; }

        /// <summary>
        /// What this build reports about itself.
        /// </summary>
        /// <remarks>Supplied rather than invented here: version compatibility is checked by
        /// the authority, and a screen that made a version up would be claiming something
        /// about the build it is running in.</remarks>
        public VersionSet Versions { get; set; }

        /// <summary>Raised when the server accepted a sign-in.</summary>
        public event System.Action SignedIn;

        /// <summary>What the player typed, for a test to drive without a keyboard.</summary>
        public void Fill(string account, string password)
        {
            EnsureBuilt();

            if (_account != null) _account.text = account;
            if (_password != null) _password.text = password;
        }

        /// <summary>
        /// Sends the login the player typed.
        /// </summary>
        /// <remarks>Refuses to run twice at once: a second press while a request is in
        /// flight would open a second session and the server would refuse it, which reads to
        /// a player as the game rejecting a password that worked.</remarks>
        public void Submit()
        {
            EnsureBuilt();

            if (Session == null || IsBusy) return;

            string account = _account == null ? string.Empty : _account.text;
            string password = _password == null ? string.Empty : _password.text;

            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            {
                SetStatus("Enter your login and password");

                return;
            }

            IsBusy = true;
            SetSubmitEnabled(false);
            SetStatus("Connecting...");

            Credentials?.Invoke(account, password);

            LoginResult result = Session.SubmitLogin(new LoginRequest(RequestId.New(),
                Versions));

            // Cleared whatever happened. It has been sent; keeping it achieves nothing and
            // risks everything.
            if (_password != null) _password.text = string.Empty;

            IsBusy = false;
            SetSubmitEnabled(true);

            if (result.IsAccepted)
            {
                SetStatus(string.Empty);
                SignedIn?.Invoke();

                return;
            }

            SetStatus(Explain(result.Reason));
        }

        protected override void Fetch()
        {
        }

        protected override void BuildRows()
        {
        }

        protected override string EmptyMessage => string.Empty;

        protected override void BuildExtra(RectTransform root)
        {
            // The list frame is not wanted here; a login screen is a form.
            Content.parent.parent.gameObject.SetActive(false);

            RectTransform form = UiFactory.CreateAnchored("Form", root,
                new Vector2(0.5f, 0.5f), new Vector2(520f, 280f));

            UiFactory.CreatePanel("Frame", form, UiFactory.Panel).rectTransform
                .SetAsFirstSibling();

            var frame = (RectTransform)form.GetChild(0);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = Vector2.zero;

            _account = UiFactory.CreateField("Account", form, "Login");
            Place(_account.GetComponent<RectTransform>(), 200f);

            _password = UiFactory.CreateField("Password", form, "Password", password: true);
            Place(_password.GetComponent<RectTransform>(), 130f);

            _submit = UiFactory.CreateButton("Submit", form, "Sign in", out _submitLabel);
            Place(_submit.GetComponent<RectTransform>(), 50f);

            _submit.onClick.AddListener(Submit);
        }

        private static void Place(RectTransform rect, float fromBottom)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(-48f, 54f);
            rect.anchoredPosition = new Vector2(0f, fromBottom);
        }

        private void SetSubmitEnabled(bool enabled)
        {
            if (_submit != null) _submit.interactable = enabled;

            if (_submitLabel != null)
            {
                _submitLabel.text = enabled ? "Sign in" : "Signing in...";
            }
        }
    }
}
