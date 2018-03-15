﻿using Discord;
using WowDotNetAPI;

namespace HeroismDiscordBot.Service.Common
{
    public interface IConfiguration
    {
        ulong DiscordGuildId { get; }

        string DiscordToken { get; }

        string WoWApiKey { get; }

        string WoWRealm { get; }

        Region WoWRegion { get; }

        string WoWGuild { get; }

        ulong DiscordMemberChangeChannelId { get; }

        Locale WoWLocale { get; }

        ulong ErrorMessageTargetId { get;  }

        string DiscordCommandPrefix { get; }

        Color DiscordBotMessageColor { get; }

        // ReSharper disable InconsistentNaming
        string WoWOAuthClientId { get; }

        string WoWOAuthClientSecret { get; }

        string WoWOAuthTokenEndpoint { get; }
        // ReSharper restore InconsistentNaming
    }
}