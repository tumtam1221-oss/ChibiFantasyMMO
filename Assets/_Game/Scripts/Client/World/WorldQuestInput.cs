using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChibiFantasy.Client.World
{
    /// <summary>
    /// The one place that knows Ctrl+Q opens the quest journal.
    /// </summary>
    /// <remarks>
    /// <b>One component per gesture, which is how this client already works.</b>
    /// <c>WorldCombatInput</c> owns attacking, <c>WorldLootInput</c> owns picking up, and
    /// <c>WorldPointerInput</c> owns the click. A quest hotkey read from inside the journal
    /// panel, or from the composition root, would be the start of keyboard checks scattered
    /// across files that have nothing to do with input.
    ///
    /// <b>A real InputAction, built here rather than authored into an asset.</b> The only
    /// <c>.inputactions</c> asset in the project belongs to the prototype scene; adding a
    /// production map for one binding would mean a second input asset for the live client to
    /// keep in step with. The action is still an action -- rebindable, pollable, disabled
    /// with the component -- and there is exactly one of it.
    ///
    /// <b>The modifier is part of the binding, not an if-statement.</b> A one-modifier
    /// composite means Q alone never fires it, which matters because Q is the sort of key a
    /// later gate will want for a skill.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WorldQuestInput : MonoBehaviour
    {
        private InputAction _journal;

        /// <summary>Raised when the player asks for the journal.</summary>
        public event System.Action Toggled;

        /// <summary>How many times the gesture has fired. For tests.</summary>
        public int Presses { get; private set; }

        /// <summary>Whether the action is listening.</summary>
        public bool IsListening => _journal != null && _journal.enabled;

        /// <summary>
        /// The keys this actually resolved to, as the Input System reports them.
        /// </summary>
        /// <remarks>
        /// <b>Asked of the binding, not of the source.</b> A composite that is spelled wrong
        /// still compiles and still enables -- it simply resolves to nothing, and the
        /// gesture silently never fires. This is the only way to know the difference, and it
        /// is what a test should assert rather than that the event plumbing works.
        ///
        /// Strings rather than <c>InputControl</c>, so a caller does not need the Input
        /// System to ask. That matters here: the test assembly deliberately does not
        /// reference it, and widening that list to read a binding would have been the wrong
        /// way round. A keybinding screen will want the same answer in the same shape.
        /// </remarks>
        public IReadOnlyList<string> BoundControls
        {
            get
            {
                var paths = new List<string>();

                if (_journal == null) return paths;

                foreach (InputControl control in _journal.controls) paths.Add(control.path);

                return paths;
            }
        }

        /// <summary>Builds and enables the binding.</summary>
        /// <remarks>Public so a test can compose it without a frame, and so the composition
        /// root can decide when input starts mattering.</remarks>
        public void Compose()
        {
            if (_journal != null) return;

            _journal = new InputAction("QuestLog", InputActionType.Button);

            // Ctrl and Q together, either control key. Written as a composite so the
            // modifier is a binding rather than a check somebody can forget to make.
            _journal.AddCompositeBinding("OneModifier")
                .With("Modifier", "<Keyboard>/leftCtrl")
                .With("Binding", "<Keyboard>/q");

            _journal.AddCompositeBinding("OneModifier")
                .With("Modifier", "<Keyboard>/rightCtrl")
                .With("Binding", "<Keyboard>/q");

            _journal.performed += OnPerformed;

            _journal.Enable();
        }

        /// <summary>Fires the gesture as though it were pressed. For tests.</summary>
        /// <remarks>The panel is driven through the same event a real press raises, so a
        /// test exercises the path a player does rather than a shortcut past it.</remarks>
        public void Press()
        {
            Presses++;

            Toggled?.Invoke();
        }

        private void OnPerformed(InputAction.CallbackContext context)
        {
            Press();
        }

        private void Awake()
        {
            Compose();
        }

        private void OnDestroy()
        {
            if (_journal == null) return;

            _journal.performed -= OnPerformed;
            _journal.Disable();
            _journal.Dispose();
            _journal = null;
        }
    }
}
