using System;

namespace BiggerLobbies.Utils;

// Steam IDs the transport itself vouches for. FishySteamworks keeps CSteamID <-> connection id
// for every Steam connection it accepted; that comes from Steam's networking layer, unlike the
// SteamId inside a client's auth broadcast, which is just a claim. FishNet's Multipass wraps
// the transports and hands out its own connection ids (NetworkConnection.ClientId), mapped
// back to (transport index, transport connection id) by Multipass.GetTransportIdData.
public static class SteamIdentity
{
    private static Multipass? Mp()
    {
        try { return InstanceFinder.TransportManager?.Transport.As<Multipass>(); }
        catch { return null; }
    }

    private static SteamTransport? AsSteam(Transport? t)
    {
        try { return t.As<SteamTransport>(); }
        catch { return null; }
    }

    /// <summary>The FishySteamworks transport (first Steam transport under Multipass, or the transport itself), whether or not its server has started.</summary>
    public static SteamTransport? Transport()
    {
        try
        {
            var mp = Mp();
            if (mp?._transports != null)
            {
                for (int i = 0; i < mp._transports.Count; i++)
                {
                    var s = AsSteam(mp._transports[i]);
                    if (s != null) return s;
                }
                return null;
            }
            return AsSteam(InstanceFinder.TransportManager?.Transport);
        }
        catch { return null; }
    }

    /// <summary>The FishySteamworks server socket (first Steam transport under Multipass, or the transport itself).</summary>
    public static ServerSocket? Server()
    {
        try
        {
            var mp = Mp();
            if (mp?._transports != null)
                for (int i = 0; i < mp._transports.Count; i++)
                {
                    var s = AsSteam(mp._transports[i]);
                    if (s?._server != null) return s._server;
                }
            return AsSteam(InstanceFinder.TransportManager?.Transport)?._server;
        }
        catch { return null; }
    }

    // Server socket and its own connection id for a FishNet client id.
    private static bool Resolve(int clientId, out ServerSocket? server, out int transportConnId)
    {
        server = null; transportConnId = clientId;
        var mp = Mp();
        if (mp != null)
        {
            // Read the map directly: GetTransportIdData logs a FishNet error for unknown ids.
            if (mp._transports == null || mp._multipassToTransport == null || !mp._multipassToTransport.TryGetValue(clientId, out var data)) return false;
            if (data.TransportIndex < 0 || data.TransportIndex >= mp._transports.Count) return false;
            server = AsSteam(mp._transports[data.TransportIndex])?._server;
            transportConnId = data.TransportId;
            return server != null;
        }
        server = AsSteam(InstanceFinder.TransportManager?.Transport)?._server;
        return server != null;
    }

    /// <summary>
    /// The Steam ID the transport verified for this connection. False when it can't be told
    /// (the host's own local connection, a non-Steam transport, or a lookup failure); callers
    /// must then not act on a claimed ID as if it were confirmed.
    /// </summary>
    public static bool TryGetVerified(NetworkConnection? conn, out ulong steamId)
    {
        steamId = 0;
        try
        {
            if (conn == null || conn.IsLocalClient) return false;
            if (!Resolve(conn.ClientId, out var server, out int tid) || server?._steamIds == null) return false;
            if (!server._steamIds.Second.TryGetValue(tid, out CSteamID id) || id.m_SteamID == 0) return false;
            steamId = id.m_SteamID;
            return true;
        }
        catch { return false; }
    }

    /// <summary>FishNet client id of the open connection the transport verified for this Steam ID.</summary>
    public static bool TryGetClientId(ulong steamId, out int clientId)
    {
        clientId = -1;
        try
        {
            if (steamId == 0) return false;
            var mp = Mp();
            if (mp?._transports != null)
            {
                for (int i = 0; i < mp._transports.Count; i++)
                {
                    var server = AsSteam(mp._transports[i])?._server;
                    if (server?._steamIds == null) continue;
                    if (!server._steamIds.First.TryGetValue(new CSteamID(steamId), out int tid)) continue;
                    if (mp.GetMultipassId(i, tid, out int mid)) { clientId = mid; return true; }
                }
                return false;
            }
            var s = AsSteam(InstanceFinder.TransportManager?.Transport)?._server;
            if (s?._steamIds != null && s._steamIds.First.TryGetValue(new CSteamID(steamId), out int t)) { clientId = t; return true; }
        }
        catch { }
        return false;
    }
}
