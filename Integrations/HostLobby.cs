#if !MONO
using System;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// The Steam lobby this machine is hosting, if any. Shared by the privacy setting,
// the live lobby size and the host panel.
public static class HostLobby
{
    public static SteamLobbyService? Service()
    {
        try { return Singleton<Lobby>.Instance?._lobbyService?.TryCast<SteamLobbyService>(); }
        catch { return null; }
    }

    /// <summary>True when we're in a Steam lobby and own it.</summary>
    public static bool TryGet(out CSteamID lobby)
    {
        lobby = default;
        try
        {
            var svc = Service();
            if (svc == null || !svc.IsInLobby) return false;
            lobby = svc._lobbySteamID;
            if (lobby.m_SteamID == 0) return false;
            return SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == SteamUser.GetSteamID().m_SteamID;
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't read the Steam lobby: {e.Message}");
            return false;
        }
    }

    public static int MemberCount()
    {
        try { return TryGet(out var id) ? SteamMatchmaking.GetNumLobbyMembers(id) : 0; }
        catch { return 0; }
    }
}

#endif
