#if !MONO
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppSteamworks;

namespace BiggerLobbies.Integrations;

// The host's Steam friends, for picking whitelist and blacklist entries.
public static class Friends
{
    public struct Friend
    {
        public ulong SteamId;
        public string Name;
        public bool Online;
        public bool InThisGame;
    }

    /// <summary>Steam friends, people playing Schedule I first, then online, then the rest, each by name.</summary>
    public static List<Friend> List()
    {
        var result = new List<Friend>();
        try
        {
            uint appId = SteamUtils.GetAppID().m_AppId;
            int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                var state = SteamFriends.GetFriendPersonaState(id);
                bool inGame = false;
                try
                {
                    var info = new FriendGameInfo_t();
                    inGame = SteamFriends.GetFriendGamePlayed(id, out info) && info.m_gameID.AppID().m_AppId == appId;
                }
                catch { }
                result.Add(new Friend
                {
                    SteamId = id.m_SteamID,
                    Name = SteamFriends.GetFriendPersonaName(id) ?? id.m_SteamID.ToString(),
                    Online = state != EPersonaState.k_EPersonaStateOffline,
                    InThisGame = inGame,
                });
            }
        }
        catch (Exception e) { MelonLoader.MelonLogger.Warning($"Couldn't read your Steam friends: {e.Message}"); }
        return result
            .OrderByDescending(f => f.InThisGame)
            .ThenByDescending(f => f.Online)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public enum Presence { Offline, Online, Playing }

    /// <summary>Whether a Steam user is offline, online, or playing Schedule I right now.</summary>
    public static Presence StateOf(ulong steamId)
    {
        try
        {
            var id = new CSteamID(steamId);
            if (SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline) return Presence.Offline;
            var info = new FriendGameInfo_t();
            if (SteamFriends.GetFriendGamePlayed(id, out info) && info.m_gameID.AppID().m_AppId == SteamUtils.GetAppID().m_AppId)
                return Presence.Playing;
            return Presence.Online;
        }
        catch { return Presence.Offline; }
    }

    /// <summary>Steam name for any ID Steam knows about (friends, lobby members), or the number.</summary>
    public static string NameOf(ulong steamId)
    {
        try
        {
            var n = SteamFriends.GetFriendPersonaName(new CSteamID(steamId));
            if (!string.IsNullOrEmpty(n) && n != "[unknown]") return n;
        }
        catch { }
        return steamId.ToString();
    }
}

#endif
