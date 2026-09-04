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
    /// <b>No creation.</b> Character creation exists as a domain service but has no
    /// production screen, so the button is absent rather than present and broken. Reported
    /// as a limitation.
    /// </remarks>
    public sealed class CharacterSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a character";

        protected override string EmptyMessage => "No characters on this account";

        /// <summary>Raised when the server authorised world entry.</summary>
        public event System.Action<EnterWorldResult> WorldAuthorised;

        protected override void Fetch()
        {
            Session.FetchCharacters();
        }

        protected override void BuildRows()
        {
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
    }
}
