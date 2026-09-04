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
    public sealed class ChannelSelectScreen : SessionScreenBase
    {
        protected override string Title => "Choose a channel";

        protected override string EmptyMessage => "No available channels";

        public event System.Action Selected;

        protected override void Fetch()
        {
            Session.FetchChannels();
        }

        protected override void BuildRows()
        {
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
    }
}
