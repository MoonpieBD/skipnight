using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("SkipNight", "C-Rust", "1.2.2")]
    [Description("Advanced player skipnight system")]

    public class SkipNight : CovalencePlugin
    {
        #region Configuration
        private bool isVotingActive = false;
        private int yesVotes = 0;
        private int totalPlayers = 0;
        private Timer voteTimer;
        private HashSet<string> votedPlayers;


        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Which permission should be granted to players so they are allowed to vote")]
            public string permVoteDay = "skipnight.use";

            [JsonProperty("At what time should the vote start")]
            public int VoteStartTime = 19;

            [JsonProperty("To what hour should the time be set after a vote")]
            public int VoteToTime = 7;

            [JsonProperty("How long should the vote take (seconds)")]
            public float VoteDuration = 60f;

            [JsonProperty("What is the percentage required for the vote to pass")]
            public int RequiredPercentage = 31;

            [JsonProperty("Which group's vote is worth more than one vote")]
            public string GroupNameVIP = "vipplus";

            [JsonProperty("How many votes is the above group's vote actually worth")]
            public int AmountVIP = 3;

            [JsonProperty("Debug mode")]
            public bool DebugMode = false;

            public string ToJson() => JsonConvert.SerializeObject(this);
            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    LogWarning("Configuration looks outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            LogWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VoteStart"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> A vote to skip the night has started! You have {0} seconds to vote. Type /skipnight to vote. {1} votes are needed to pass.</color>",
                ["VoteAmount"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> Your vote made the total untill now: {0} votes out of {1} needed.</color>",
                ["VoteSuccess"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> You want daytime! There were {0} votes out of {1} needed. Skipping to daytime.</color>",
                ["VoteFail"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> The vote failed. {0} votes out of {1} needed. The night will continue.</color>",
                ["PermissionIssue"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> You do not have permission to use this command.</color>",
                ["NoRunningVote"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> There is currently no vote active.</color>",
                ["DoubleVote"] = "<color=#f3a535>[Skip Night] </color> <color=#e7e5e3> You have already voted.</color>"
            }, this);
            if (config.DebugMode)
            {
                LogWarning("Languages are now loaded");
            }
        }

        #endregion Localization


        #region Initialization

        private void Init()
        {
            permission.RegisterPermission(config.permVoteDay, this);
        }

        #endregion Initialization

        private void OnTick()
        {
            var time = TOD_Sky.Instance.Cycle.Hour;
            if (Mathf.Floor(time) == config.VoteStartTime && !isVotingActive)
            {
                totalPlayers = covalence.Players.Connected.Count();

                if (totalPlayers >= 1)
                {
                    isVotingActive = true;
                    yesVotes = 0;
                    votedPlayers = new HashSet<string>();
                    StartVote();
                }

            }
            if (isVotingActive)
            {
                CheckVotes();
            }
        }

        #region Commands

        [Command("skipnight")]
        private void VoteDayCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                HandleVoteCommand(player);
                return;
            }
        }

        #endregion Commands


        #region Voting
        private void HandleVoteCommand(IPlayer player)
        {
            int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
            if (!player.HasPermission(config.permVoteDay))
            {
                player.Message(lang.GetMessage("PermissionIssue", this, player.Id));
                return;
            }

            if (!isVotingActive)
            {
                player.Message(lang.GetMessage("NoRunningVote", this, player.Id));
                return;
            }

            if (votedPlayers.Contains(player.Id))
            {
                player.Message(lang.GetMessage("DoubleVote", this, player.Id));
                return;
            }
            if (permission.UserHasGroup(player.Id, config.GroupNameVIP))
            {
                yesVotes += config.AmountVIP;
                votedPlayers.Add(player.Id);
                player.Message(string.Format(lang.GetMessage("VoteAmount", this, player.Id), yesVotes, requiredVotes));
                return;
            }
            votedPlayers.Add(player.Id);
            yesVotes++;
            player.Message(string.Format(lang.GetMessage("VoteAmount", this, player.Id), yesVotes, requiredVotes));
        }

        private void StartVote()
        {
            if (config.DebugMode)
            {
                Puts("Skipnight vote started");
            }
            
            int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
            BroadcastToServer("VoteStart", config.VoteDuration, requiredVotes);
            
            voteTimer = timer.Once(config.VoteDuration, EndVote);
        }

        private void CheckVotes()
        {
            int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
            if (yesVotes >= requiredVotes)
            {
                EndVote();
            }
        }

        private void EndVote()
        {
            voteTimer.Destroy();
            if (isVotingActive)
            {
                int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
                isVotingActive = false;

                if (config.DebugMode)
                {
                    Puts("Skipnight vote ended");
                    Puts($"Votes with yes: {yesVotes}");
                    Puts($"Votes required: {requiredVotes}");
                }

                if (yesVotes >= requiredVotes)
                {
                    BroadcastToServer("VoteSuccess", yesVotes, requiredVotes);
                    TOD_Sky.Instance.Cycle.Day = TOD_Sky.Instance.Cycle.Day + 1;
                    TOD_Sky.Instance.Cycle.Hour = config.VoteToTime;
                }
                else
                {
                    BroadcastToServer("VoteFail", yesVotes, requiredVotes);
                }
            }

        }

        #endregion Voting

        private void BroadcastToServer(string messageKey, params object[] args)
        {
            string message = string.Format(lang.GetMessage(messageKey, this), args);
            foreach (var player in covalence.Players.Connected)
            {
                player.Message(message);
            }
        }
    }
}
