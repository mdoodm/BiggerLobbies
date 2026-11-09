#if !MONO
using System;
using HarmonyLib;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace BiggerLobbies.Integrations;

// Fix for 0.4.7: one "invite" click produced TWO Steam lobbies. The host ended up
// tracking the second one while friends joined the first, so they never saw each
// other and the host's "join game" message never reached them.
// We allow only one SteamMatchmaking.CreateLobby while one is pending or open, and
// defer the invite overlay until the lobby actually exists.
[HarmonyPatch]
public static class LobbyDedupePatches
{
    private static float _pendingSince = -1f;          // Time.realtimeSinceStartup of the pending create
    private const float PendingTimeout = 15f;
    private static bool _openInviteWhenReady;
    private static SteamLobbyService? _service;

    private static void Log(string s) => Utils.Log.Info($"[diag] {s}");

    private static bool CreatePending =>
        _pendingSince >= 0f && Time.realtimeSinceStartup - _pendingSince < PendingTimeout;

    private static bool ServiceInLobby()
    {
        try { return _service != null && _service.IsInLobby; } catch { return false; }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.Initialize))]
    private static void Svc_Initialize_Postfix(SteamLobbyService __instance) => _service = __instance;

    [HarmonyPrefix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.CreateLobby))]
    private static bool Steam_CreateLobby_Prefix(ELobbyType eLobbyType, int cMaxMembers, ref SteamAPICall_t __result)
    {
        if (CreatePending || ServiceInLobby())
        {
            Log($"Steam.CreateLobby BLOCKED duplicate (pending={CreatePending} inLobby={ServiceInLobby()})");
            __result = SteamAPICall_t.Invalid;
            return false;
        }
        _pendingSince = Time.realtimeSinceStartup;
        Log($"Steam.CreateLobby type={eLobbyType} max={cMaxMembers}");
        return true;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void Svc_OnLobbyCreated_Postfix() => _pendingSince = -1f;

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OpenInviteUI))]
    private static bool Svc_OpenInviteUI_Prefix(SteamLobbyService __instance)
    {
        _service = __instance;
        bool inLobby = __instance.IsInLobby;
        Log($"Svc.OpenInviteUI inLobby={inLobby} pending={CreatePending}");
        if (inLobby) return true; // normal path

        // Not in a lobby yet: create one (once) and open the overlay when we've entered it.
        _openInviteWhenReady = true;
        if (!CreatePending)
        {
            try { __instance.CreateLobby(LobbySize.MaxPlayers); }
            catch (Exception e) { MelonLogger.Error($"CreateLobby from OpenInviteUI failed: {e}"); }
        }
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static void Svc_OnLobbyEntered_Postfix(SteamLobbyService __instance)
    {
        _pendingSince = -1f;
        if (!_openInviteWhenReady) return;
        _openInviteWhenReady = false;
        try
        {
            Log($"Opening deferred invite overlay for lobby {__instance._lobbySteamID.m_SteamID}");
            __instance.OpenInviteUI();
        }
        catch (Exception e) { MelonLogger.Error($"Deferred OpenInviteUI failed: {e}"); }
    }

    // Extra logging of the raw Steam calls.
    [HarmonyPrefix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.JoinLobby))]
    private static void Steam_JoinLobby(CSteamID steamIDLobby) => Log($"Steam.JoinLobby {steamIDLobby.m_SteamID}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby))]
    private static void Steam_LeaveLobby(CSteamID steamIDLobby) => Log($"Steam.LeaveLobby {steamIDLobby.m_SteamID}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamFriends), nameof(SteamFriends.ActivateGameOverlayInviteDialog))]
    private static void Steam_InviteDialog(CSteamID steamIDLobby) => Log($"Steam.ActivateGameOverlayInviteDialog {steamIDLobby.m_SteamID}");
}

#endif
