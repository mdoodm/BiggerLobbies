#if !MONO
using System;
using System.Collections.Generic;
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Transporting.Multipass;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppSteamworks;
using MelonLoader;
using SteamTransport = Il2CppFishySteamworks.FishySteamworks;

namespace BiggerLobbies.Integrations;

// Connected players as the host sees them: name, verified Steam ID, Steam ping,
// plus kick and blacklist. Used by the Host settings screen.
public static class HostPlayers
{
    public struct Row
    {
        public string Name;
        public ulong SteamId;   // from the Steam login the host checked; 0 if unknown
        public int Ping;        // ms, -1 unknown
        public int ClientId;
    }

    public static List<Row> List()
    {
        var rows = new List<Row>();
        try
        {
            if (!InstanceFinder.IsServer) return rows;
            var list = Player.PlayerList;
            if (list == null) return rows;
            var server = SteamServer();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.IsLocalPlayer) continue;
                var conn = p.Connection;
                if (conn == null || conn.IsLocalClient) continue;
                ulong sid = BanList.VerifiedSteamId(conn.ClientId);
                rows.Add(new Row
                {
                    Name = string.IsNullOrEmpty(p.PlayerName) ? "Unnamed player" : p.PlayerName,
                    SteamId = sid,
                    Ping = PingOf(server, sid),
                    ClientId = conn.ClientId,
                });
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't read the player list: {e.Message}"); }
        return rows;
    }

    /// <summary>Disconnects the player, and blacklists them first if asked. Returns a status line.</summary>
    public static string Kick(Row r, bool blacklist)
    {
        try
        {
            if (blacklist) BanList.Ban(r.SteamId, r.Name);
            var conn = FindConnection(r.ClientId);
            if (conn == null) return $"{r.Name} has already left";
            if (conn.IsLocalClient) return "";
            conn.Disconnect(true);
            MelonLogger.Msg($"{(blacklist ? "Blacklisted and kicked" : "Kicked")} {r.Name}{(r.SteamId != 0 ? $" ({r.SteamId})" : "")}");
            return blacklist ? $"Blacklisted {r.Name}" : $"Kicked {r.Name}. They can rejoin unless you blacklist them";
        }
        catch (Exception e) { return $"Couldn't remove {r.Name}: {e.Message}"; }
    }

    /// <summary>Disconnects every connected player whose checked Steam ID is this one.</summary>
    public static void DisconnectSteamId(ulong id)
    {
        try
        {
            if (id == 0 || !InstanceFinder.IsServer) return;
            var list = Player.PlayerList;
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i]?.Connection;
                if (c == null || c.IsLocalClient || !c.IsActive) continue;
                if (BanList.VerifiedSteamId(c.ClientId) != id) continue;
                c.Disconnect(true);
                MelonLogger.Msg($"Disconnected {list[i].PlayerName} ({id}): blacklisted");
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't disconnect blacklisted player: {e.Message}"); }
    }

    // Look the player up again at click time; a row can be up to a second old.
    private static NetworkConnection? FindConnection(int clientId)
    {
        var list = Player.PlayerList;
        if (list == null) return null;
        for (int i = 0; i < list.Count; i++)
        {
            var c = list[i]?.Connection;
            if (c != null && c.ClientId == clientId && c.IsActive) return c;
        }
        return null;
    }

    private static Il2CppFishySteamworks.Server.ServerSocket? SteamServer()
    {
        try
        {
            var t = InstanceFinder.TransportManager?.Transport;
            if (t == null) return null;
            var mp = t.TryCast<Multipass>();
            if (mp?._transports != null)
                for (int i = 0; i < mp._transports.Count; i++)
                {
                    var s = mp._transports[i]?.TryCast<SteamTransport>();
                    if (s?._server != null) return s._server;
                }
            return t.TryCast<SteamTransport>()?._server;
        }
        catch { return null; }
    }

    private static bool _pingWarned;

    // FishySteamworks keeps Steam ID -> connection id -> Steam connection handle.
    // Steam's real-time status for that handle has the round-trip time in ms.
    private static int PingOf(Il2CppFishySteamworks.Server.ServerSocket? server, ulong sid)
    {
        if (server == null || sid == 0) return -1;
        try
        {
            if (!server._steamIds.First.TryGetValue(new CSteamID(sid), out int connId)) return -1;
            if (!server._steamConnections.Second.TryGetValue(connId, out HSteamNetConnection h)) return -1;
            var status = new SteamNetConnectionRealTimeStatus_t { reserved = new Il2CppStructArray<uint>(16) };
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t { reserved = new Il2CppStructArray<uint>(10) };
            var r = SteamNetworkingSockets.GetConnectionRealTimeStatus(h, ref status, 0, ref lanes);
            return r == EResult.k_EResultOK ? status.m_nPing : -1;
        }
        catch (Exception e)
        {
            if (!_pingWarned) { _pingWarned = true; MelonLogger.Warning($"Couldn't read ping: {e.Message}"); }
            return -1;
        }
    }
}

#endif
