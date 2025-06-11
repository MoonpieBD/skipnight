using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("SkipNight", "C-Rust", "1.2")]
    [Description("Advanced player skipnight system")]

    public class SkipNight : CovalencePlugin
    {
        private const string permVoteDay = "skipnight.use";
        private bool isVotingActive = false;
        private int yesVotes = 0;
        private int totalPlayers = 0;
        private Timer voteTimer;
        private HashSet<string> votedPlayers;

        private ConfigData config;

        private class ConfigData
        {
            public int Votestart { get; set; }  
            public float VoteDuration { get; set; }
            public int RequiredPercentage { get; set; }
            public string GroupNameVip { get; set; }
            public int AmountVIP { get; set; }
            public bool DebugMode { get; set; }
        }
        
        protected override void LoadDefaultConfig()
        {
            config = new ConfigData
            {
                Votestart = 20,
                VoteDuration = 60f,
                RequiredPercentage = 31,
                GroupNameVip = "vipplus",
                AmountVIP = 3,
                DebugMode = false
            };
            Puts("Config created");
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<ConfigData>();
            if (config.DebugMode)
            {
                Puts("Config loaded");
            }
            
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        private void Init()
        {
            permission.RegisterPermission(permVoteDay, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VoteStarted"] = "<color=#FFFF00>[Skip night] A vote to skip the night has started! You have {0} seconds to vote. Type /skipnight to vote. {1} votes are needed to pass.</color>",
                ["VoteCount"] = "<color=#00FF00>[Skip night] {0} votes out of {1} needed.</color>",
                ["VotePassed"] = "<color=#00FF00>[Skip night] The vote passed! {0} votes out of {1} needed. Skipping to day.</color>",
                ["VoteFailed"] = "<color=#FF0000>[Skip night] The vote failed. {0} votes out of {1} needed. The night will continue.</color>",
                ["NoPermission"] = "<color=#FF0000>[Skip night] You do not have permission to use this command.</color>",
                ["AlreadyVoting"] = "<color=#FF0000>[Skip night] There is currently no vote active.</color>",
                ["AlreadyVoted"] = "<color=#FF0000>[Skip night] You have already voted.</color>",
                ["ConfigReloaded"] = "<color=#00FF00>[Skip night] Configuration reloaded successfully.</color>",
                ["InvalidCommand"] = "<color=#FF0000>[Skip night] Invalid command usage. Use /skipnight set timevote <seconds> or /skipnight set requiredpercentage <percentage>.</color>",
                ["VoteDurationSet"] = "<color=#00FF00>[Skip night] Vote duration set to {0} seconds.</color>",
                ["RequiredPercentageSet"] = "<color=#00FF00>[Skip night] Required vote percentage set to {0}%.</color>",
                ["InvalidPercentage"] = "<color=#FF0000>[Skip night] Invalid percentage. Please enter a value between 1 and 100.</color>"
            }, this);
            if (config.DebugMode)
            {
                Puts("Languagesfile loaded");
            }
        }

        private void OnTick()
        {
            var time = TOD_Sky.Instance.Cycle.Hour;
            if (Mathf.Floor(time) == config.Votestart && !isVotingActive)
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

        [Command("skipnight")]
        private void VoteDayCommand(IPlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                HandleVoteCommand(player);
                return;
            }
        }

        private void HandleVoteCommand(IPlayer player)
        {
            int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
            if (!player.HasPermission(permVoteDay))
            {
                player.Message(lang.GetMessage("NoPermission", this, player.Id));
                return;
            }

            if (!isVotingActive)
            {
                player.Message(lang.GetMessage("AlreadyVoting", this, player.Id));
                return;
            }

            if (votedPlayers.Contains(player.Id))
            {
                player.Message(lang.GetMessage("AlreadyVoted", this, player.Id));
                return;
            }
            if (permission.UserHasGroup(player.Id, config.GroupNameVip))
            {
                yesVotes += config.AmountVIP;
                votedPlayers.Add(player.Id);
                player.Message(string.Format(lang.GetMessage("VoteCount", this, player.Id), yesVotes, requiredVotes));
                return;
            }
            votedPlayers.Add(player.Id);
            yesVotes++;
            player.Message(string.Format(lang.GetMessage("VoteCount", this, player.Id), yesVotes, requiredVotes));
        }

        private void StartVote()
        {
            if (config.DebugMode)
            {
                Puts("Skipnight vote started");
            }
            
            int requiredVotes = Mathf.CeilToInt(totalPlayers * (config.RequiredPercentage / 100f));
            BroadcastToServer("VoteStarted", config.VoteDuration, requiredVotes);
            
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
            Puts("Skipnight has ENDED");
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
                    BroadcastToServer("VotePassed", yesVotes, requiredVotes);
                    TOD_Sky.Instance.Cycle.Hour = 8f;
                }
                else
                {
                    BroadcastToServer("VoteFailed", yesVotes, requiredVotes);
                }
            }

        }

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
