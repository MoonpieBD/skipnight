using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Skipnight", "Moonpie", "1.0.0")]
    [Description("Allows players to vote to skip the night.")]
    public class Skipnight : CovalencePlugin
    {
        private const string permVoteDay = "skipnight.use";
        private const string permVoteDayAdmin = "skipnight.admin";
        private bool isVotingActive = false;
        private int yesVotes = 0;
        private int totalPlayers = 0;
        private Timer voteTimer;
        private HashSet<string> votedPlayers;

        private ConfigData config;

        private class ConfigData
        {
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
            permission.RegisterPermission(permVoteDayAdmin, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["VoteStarted"] = "<color=#FFFF00>[Skipnight] A vote to skip the night has started! You have {0} seconds to vote. Type /skipnight to vote. {1} votes are needed to pass.</color>",
                ["VoteCount"] = "<color=#00FF00>[Skipnight] {0} votes out of {1} needed.</color>",
                ["VotePassed"] = "<color=#00FF00>[Skipnight] The vote passed! {0} votes out of {1} needed. Skipping to day.</color>",
                ["VoteFailed"] = "<color=#FF0000>[Skipnight] The vote failed. {0} votes out of {1} needed. The night will continue.</color>",
                ["NoPermission"] = "<color=#FF0000>[Skipnight] You do not have permission to use this command.</color>",
                ["AlreadyVoting"] = "<color=#FF0000>[Skipnight] A vote is already in progress.</color>",
                ["AlreadyVoted"] = "<color=#FF0000>[Skipnight] You have already voted.</color>",
                ["ConfigReloaded"] = "<color=#00FF00>[Skipnight] Configuration reloaded successfully.</color>",
                ["InvalidCommand"] = "<color=#FF0000>[Skipnight] Invalid command usage. Use /skipnight set timevote <seconds> or /skipnight set requiredpercentage <percentage>.</color>",
                ["VoteDurationSet"] = "<color=#00FF00>[Skipnight] Vote duration set to {0} seconds.</color>",
                ["RequiredPercentageSet"] = "<color=#00FF00>[Skipnight] Required vote percentage set to {0}%.</color>",
                ["InvalidPercentage"] = "<color=#FF0000>[Skipnight] Invalid percentage. Please enter a value between 1 and 100.</color>"
            }, this);
            if (config.DebugMode)
            {
                Puts("Languagesfile loaded");
            }
        }

        private void OnTick()
        {
            var time = TOD_Sky.Instance.Cycle.Hour;
            if (Mathf.Floor(time) == 20f && !isVotingActive)
            {
                totalPlayers = covalence.Players.Connected.Count();
                /*
                if (config.DebugMode)
                {
                    Puts("Vote triggered");
                    Puts($"Active players: {totalPlayers}");
                }
                */
                if (totalPlayers >= 2)
                {
                    isVotingActive = true;
                    yesVotes = 0;
                    votedPlayers = new HashSet<string>();
                    StartVote();
                }

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

            if (args.Length >= 1 && !player.HasPermission(permVoteDayAdmin))
            {
                player.Message(lang.GetMessage("NoPermission", this, player.Id));
                return;
            }

            switch (args[0].ToLower())
            {
                case "reload":
                    ReloadConfigCommand(player);
                    break;

                case "set":
                    if (args.Length == 3)
                    {
                        switch (args[1].ToLower())
                        {
                            case "timevote":
                                if (float.TryParse(args[2], out float newDuration))
                                {
                                    SetVoteDurationCommand(player, newDuration);
                                }
                                else
                                {
                                    player.Message(lang.GetMessage("InvalidCommand", this, player.Id));
                                }
                                break;

                            case "requiredpercentage":
                                if (int.TryParse(args[2], out int newPercentage) && newPercentage >= 1 && newPercentage <= 100)
                                {
                                    SetRequiredPercentageCommand(player, newPercentage);
                                }
                                else
                                {
                                    player.Message(lang.GetMessage("InvalidPercentage", this, player.Id));
                                }
                                break;

                            default:
                                player.Message(lang.GetMessage("InvalidCommand", this, player.Id));
                                break;
                        }
                    }
                    else
                    {
                        player.Message(lang.GetMessage("InvalidCommand", this, player.Id));
                    }
                    break;

                default:
                    player.Message(lang.GetMessage("InvalidCommand", this, player.Id));
                    break;
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

        private void ReloadConfigCommand(IPlayer player)
        {
            LoadConfig();
            player.Message(lang.GetMessage("ConfigReloaded", this, player.Id));
        }

        private void SetVoteDurationCommand(IPlayer player, float newDuration)
        {
            config.VoteDuration = newDuration;
            SaveConfig();
            player.Message(string.Format(lang.GetMessage("VoteDurationSet", this, player.Id), newDuration));
        }

        private void SetRequiredPercentageCommand(IPlayer player, int newPercentage)
        {
            config.RequiredPercentage = newPercentage;
            SaveConfig();
            player.Message(string.Format(lang.GetMessage("RequiredPercentageSet", this, player.Id), newPercentage));
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

        private void EndVote()
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
