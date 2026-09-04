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
    public sealed class ServerSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a server";

        protected override string EmptyMessage => "No available servers";

        /// <summary>Raised when the flow service accepted a server.</summary>
        public event System.Action Selected;

        protected override void Fetch()
        {
            Session.FetchServers();
        }

        protected override void BuildRows()
        {
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
    }
}
