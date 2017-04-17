﻿using DEA.Database.Models;
using DEA.Database.Repository;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Threading;

namespace DEA.Services.Timers
{
    class AutoUnmute
    {
        private IDependencyMap _map;
        private DiscordSocketClient _client;
        private IMongoCollection<Mute> _mutes;
        private GuildRepository _guildRepo;
        private MuteRepository _muteRepo;

        private Timer _timer;

        public AutoUnmute(IDependencyMap map)
        {
            _map = map;
            _client = _map.Get<DiscordSocketClient>();
            _mutes = _map.Get<IMongoCollection<Mute>>();
            _guildRepo = _map.Get<GuildRepository>();
            _muteRepo = _map.Get<MuteRepository>();

            ObjectState StateObj = new ObjectState();

            TimerCallback TimerDelegate = new TimerCallback(TimerTask);

            _timer = new Timer(TimerDelegate, StateObj, 0, Config.AUTO_UNMUTE_COOLDOWN);

            StateObj.TimerReference = _timer;
        }

        private async void TimerTask(object stateObj)
        {
            var builder = Builders<Mute>.Filter;
            foreach (var mute in await (await _mutes.FindAsync(builder.Empty)).ToListAsync())
            {
                if (DateTime.UtcNow.Subtract(mute.MutedAt).TotalMilliseconds > mute.MuteLength)
                {
                    var guild = _client.GetGuild(mute.GuildId);
                    if (guild != null && guild.GetUser(mute.UserId) != null)
                    {
                        var guildData = await _guildRepo.FetchGuildAsync(guild.Id);
                        var mutedRole = guild.GetRole(guildData.MutedRoleId);
                        if (mutedRole != null && guild.GetUser(mute.UserId).Roles.Any(x => x.Id == mutedRole.Id))
                        {
                            var channel = guild.GetTextChannel(guildData.ModLogId);
                            if (channel != null && guild.CurrentUser.GuildPermissions.EmbedLinks &&
                                (guild.CurrentUser as IGuildUser).GetPermissions(channel as SocketTextChannel).SendMessages
                                && (guild.CurrentUser as IGuildUser).GetPermissions(channel as SocketTextChannel).EmbedLinks)
                            {
                                await guild.GetUser(mute.UserId).RemoveRoleAsync(mutedRole);
                                var footer = new EmbedFooterBuilder()
                                {
                                    IconUrl = "http://i.imgur.com/BQZJAqT.png",
                                    Text = $"Case #{guildData.CaseNumber}"
                                };
                                var embedBuilder = new EmbedBuilder()
                                {
                                    Color = new Color(12, 255, 129),
                                    Description = $"**Action:** Automatic Unmute\n**User:** {guild.GetUser(mute.UserId)} ({guild.GetUser(mute.UserId).Id})",
                                    Footer = footer
                                }.WithCurrentTimestamp();
                                await _guildRepo.ModifyAsync(guild.Id, x => x.CaseNumber, ++guildData.CaseNumber);
                                await channel.SendMessageAsync(string.Empty, embed: embedBuilder);
                            }
                        }
                    }
                    await _muteRepo.RemoveMuteAsync(mute.UserId, mute.GuildId);
                }
            }
        }
    }
}