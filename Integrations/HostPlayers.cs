using System;
using System.Collections.Generic;
using MelonLoader;
using BiggerLobbies.Utils;

namespace BiggerLobbies.Integrations;

// Ban lengths offered in Host settings.
public static class BanLength
{
    public static readonly (string label, TimeSpan? length)[] Options =
    {
        ("1 hour", TimeSpan.FromHours(1)),
        ("1 day", TimeSpan.FromDays(1)),
        ("1 week", TimeSpan.FromDays(7)),
        ("Permanent", null),
    };

    public static string Label(TimeSpan? length)
    {
        foreach (var (label, l) in Options) if (l == length) return label;
        return length.HasValue ? $"{length.Value.TotalHours:0} h" : "Permanent";
    }
}

// Connected players as the host sees them: name, verified Steam ID, Steam ping,
// plus kick and ban. Used by the Host settings screen.
public static class HostPlayers
{
    public struct Row
    {
        public string Name;
        public ulong SteamId;   // from the Steam login the host checked; 0 if unknown
        public int Ping;        // ms, -1 unknown
        public int ClientId;
        public bool Fake;       // DebugFakePlayers test entry: borrows a friend's name and avatar, can't be kicked or banned
    }

    private static List<Row>? _fakes;
    private static int _fakeCount = -1;

    // Test entries for DebugFakePlayers, so the Players tab can be checked at size without real players.
    private static void AddFakes(List<Row> rows)
    {
        int want = LobbySize.FakePlayers;
        if (want <= 0) return;
        if (_fakes == null || _fakeCount != want)
        {
            _fakeCount = want;
            _fakes = new List<Row>();
            var friends = Friends.List();
            for (int i = 0; i < want; i++)
            {
                bool useFriend = friends.Count > 0;
                var f = useFriend ? friends[i % friends.Count] : default;
                _fakes.Add(new Row
                {
                    Name = useFriend ? (i < friends.Count ? f.Name : $"{f.Name} {i / friends.Count + 1}") : $"Test player {i + 1}",
                    SteamId = useFriend ? f.SteamId : 0,
                    ClientId = -1000 - i,
                    Fake = true,
                });
            }
        }
        float t = UnityEngine.Time.unscaledTime;
        for (int i = 0; i < _fakes.Count; i++)
        {
            var r = _fakes[i];
            r.Ping = 30 + (i * 37) % 140 + (int)(UnityEngine.Mathf.PerlinNoise(i * 0.7f, t * 0.3f) * 25);
            rows.Add(r);
        }
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
                if (conn == null || conn.IsLocalClient || !conn.IsActive) continue;
                ulong sid = IdOf(conn);
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
        // Showcase brings its own players; the friend-named test players would mix in with them.
        try { if (InstanceFinder.IsServer) { if (Showcase.On) { rows.Clear(); Showcase.AddPlayers(rows); } else AddFakes(rows); } } catch { }
        return rows;
    }

    /// <summary>Disconnects the player, and bans them first if asked (null length = permanent). Returns a status line.</summary>
    public static string Kick(Row r, bool ban, TimeSpan? length = null, string reason = "")
    {
        if (Showcase.IsFake(r.SteamId)) return Showcase.Inert(r.Name);
        if (r.Fake) return $"{r.Name} is a test player (DebugFakePlayers). Nothing to {(ban ? "ban" : "kick")}";
        try
        {
            var conn = FindConnection(r.ClientId);
            if (ban)
            {
                if (BanList.Ban(r.SteamId, r.Name, length, reason)) return $"Banned {r.Name} ({BanLength.Label(length).ToLowerInvariant()})";
                // No usable Steam ID (or it's the host's own): the ban can't be recorded, so only remove them.
                if (conn == null || conn.IsLocalClient) return $"Couldn't ban {r.Name}: no verified Steam ID";
                KickNotice.Remove(conn, r.SteamId, KickNotice.Kind.Kick, bypassCap: true);
                Utils.Log.Info($"Couldn't ban {r.Name}; kicked instead");
                return $"Couldn't ban {r.Name}: no verified Steam ID. Kicked them instead";
            }
            if (conn == null) return $"{r.Name} has already left";
            if (conn.IsLocalClient) return "";
            KickNotice.Remove(conn, r.SteamId, KickNotice.Kind.Kick, bypassCap: true);
            Utils.Log.Info($"Kicked {r.Name}{(r.SteamId != 0 ? $" ({r.SteamId})" : "")}");
            return $"Kicked {r.Name}. They can rejoin unless you ban them";
        }
        catch (Exception e) { return $"Couldn't remove {r.Name}: {e.Message}"; }
    }

    /// <summary>Disconnects every connected player whose checked Steam ID is this one, including connections still loading.</summary>
    public static void DisconnectSteamId(ulong id, TimeSpan? length = null, string reason = "")
    {
        try
        {
            if (id == 0 || !InstanceFinder.IsServer) return;
            int n = DisconnectWhere(x => x == id, KickNotice.Kind.Ban, "banned", length, reason);
            // A connection the transport knows by this ID that hasn't sent its login yet.
            if (n == 0 && SteamIdentity.TryGetClientId(id, out int cid)
                && Clients()?.TryGetValue(cid, out var c) == true && c != null && !c.IsLocalClient && c.IsActive)
            {
                KickNotice.Remove(c, id, KickNotice.Kind.Ban, length, reason, bypassCap: true);
                Utils.Log.Info($"Disconnected connection {cid} ({id}): banned");
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't disconnect banned player: {e.Message}"); }
    }

    private static ClientMap? Clients()
    {
        try { return InstanceFinder.ServerManager?.Clients; } catch { return null; }
    }

    // Steam ID of a remote connection: the transport's word first, then the login the host saw.
    private static ulong IdOf(NetworkConnection c) =>
        SteamIdentity.TryGetVerified(c, out ulong v) ? v : BanList.VerifiedSteamId(c.ClientId);

    /// <summary>
    /// Disconnects connected non-host players (spawned or still loading) whose Steam ID matches,
    /// with a notice of this kind. Returns how many.
    /// </summary>
    public static int DisconnectWhere(Func<ulong, bool> match, KickNotice.Kind kind, string why, TimeSpan? length = null, string reason = "")
    {
        int count = 0;
        try
        {
            if (!InstanceFinder.IsServer) return 0;
            ulong host = BanList.HostSteamId();
            var seen = new HashSet<int>();
            var conns = new List<NetworkConnection>();
            var list = Player.PlayerList;
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                {
                    var pc = list[i]?.Connection;
                    if (pc != null && seen.Add(pc.ClientId)) conns.Add(pc);
                }
            var clients = Clients();
            if (clients != null)
                foreach (var kv in BanList.Logins())
                    if (seen.Add(kv.Key) && clients.TryGetValue(kv.Key, out var lc) && lc != null) conns.Add(lc);
            foreach (var c in conns)
            {
                if (c.IsLocalClient || !c.IsActive) continue;
                ulong id = IdOf(c);
                if (id == 0 || id == host || !match(id)) continue;
                KickNotice.Remove(c, id, kind, length, reason, bypassCap: true);
                Utils.Log.Info($"Disconnected {Friends.NameOf(id)} ({id}): {why}");
                count++;
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't disconnect players ({why}): {e.Message}"); }
        return count;
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

    private static ServerSocket? SteamServer() => SteamIdentity.Server();

    private static bool _pingWarned;

    // FishySteamworks keeps Steam ID -> connection id -> Steam connection handle.
    // Steam's real-time status for that handle has the round-trip time in ms.
    private static int PingOf(ServerSocket? server, ulong sid)
    {
        if (server == null || sid == 0) return -1;
        try
        {
            if (!server._steamIds.First.TryGetValue(new CSteamID(sid), out int connId)) return -1;
            if (!server._steamConnections.Second.TryGetValue(connId, out HSteamNetConnection h)) return -1;
            var status = new SteamNetConnectionRealTimeStatus_t { reserved = Shim.UInts(16) };
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t { reserved = Shim.UInts(10) };
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
