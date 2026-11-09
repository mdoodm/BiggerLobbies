#if !MONO
using System;
using HarmonyLib;
using Il2CppScheduleOne.Networking;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Temporary logging to trace the lobby/join flow. Every hook only logs.
[HarmonyPatch]
public static class DiagnosticPatches
{
    private static string Id(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? o) => o == null ? "null" : o.Pointer.ToString("X");
    private static void Log(string s) => Utils.Log.Info($"[diag] {s}");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.Awake))]
    private static void Lobby_Awake(Lobby __instance) => Log($"Lobby.Awake lobby={Id(__instance)}");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.Start))]
    private static void Lobby_Start(Lobby __instance) => Log($"Lobby.Start lobby={Id(__instance)}");

    [HarmonyPostfix, HarmonyPatch(typeof(Lobby), nameof(Lobby.CreateLobbyService))]
    private static void Lobby_CreateLobbyService(Lobby __instance) =>
        Log($"Lobby.CreateLobbyService lobby={Id(__instance)} service={Id(__instance._lobbyService)}");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.CreateLobby))]
    private static void Lobby_CreateLobby(Lobby __instance) =>
        Log($"Lobby.CreateLobby lobby={Id(__instance)} inLobby={__instance.IsInLobby}");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.TryOpenInviteInterface))]
    private static void Lobby_TryOpenInvite(Lobby __instance) =>
        Log($"Lobby.TryOpenInviteInterface lobby={Id(__instance)} inLobby={__instance.IsInLobby} count={__instance.PlayerCount}");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.SendLobbyMessage))]
    private static void Lobby_SendLobbyMessage(string message) => Log($"Lobby.SendLobbyMessage '{message}'");

    [HarmonyPrefix, HarmonyPatch(typeof(Lobby), nameof(Lobby.SetLobbyData))]
    private static void Lobby_SetLobbyData(string key, string value) => Log($"Lobby.SetLobbyData {key}={value}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.Initialize))]
    private static void Svc_Initialize(SteamLobbyService __instance) => Log($"Svc.Initialize service={Id(__instance)}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.CreateLobby))]
    private static void Svc_CreateLobby(SteamLobbyService __instance, int maxPlayers) =>
        Log($"Svc.CreateLobby service={Id(__instance)} maxPlayers={maxPlayers} inLobby={__instance.IsInLobby}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void Svc_OnLobbyCreated(SteamLobbyService __instance, Il2CppSteamworks.LobbyCreated_t result) =>
        Log($"Svc.OnLobbyCreated service={Id(__instance)} lobby={result.m_ulSteamIDLobby} result={result.m_eResult}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static void Svc_OnLobbyEntered(SteamLobbyService __instance, Il2CppSteamworks.LobbyEnter_t result) =>
        Log($"Svc.OnLobbyEntered service={Id(__instance)} lobby={result.m_ulSteamIDLobby} response={result.m_EChatRoomEnterResponse}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.JoinLobby))]
    private static void Svc_JoinLobby(SteamLobbyService __instance) => Log($"Svc.JoinLobby service={Id(__instance)}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.LeaveLobby))]
    private static void Svc_LeaveLobby(SteamLobbyService __instance) => Log($"Svc.LeaveLobby service={Id(__instance)}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.SendMessage))]
    private static void Svc_SendMessage(SteamLobbyService __instance, string message) =>
        Log($"Svc.SendMessage service={Id(__instance)} '{message}' lobby={__instance._lobbySteamID.m_SteamID}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.SetLobbyData))]
    private static void Svc_SetLobbyData(SteamLobbyService __instance, string key, string value) =>
        Log($"Svc.SetLobbyData service={Id(__instance)} {key}={value} lobby={__instance._lobbySteamID.m_SteamID}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.UpdateLobbyMembers))]
    private static void Svc_UpdateMembers(SteamLobbyService __instance) =>
        Log($"Svc.UpdateLobbyMembers service={Id(__instance)} lobby={__instance._lobbySteamID.m_SteamID}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static void Svc_PlayerEnterOrLeave(SteamLobbyService __instance) => Log($"Svc.PlayerEnterOrLeave service={Id(__instance)}");

    [HarmonyPrefix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static void Svc_OnChat(SteamLobbyService __instance) => Log($"Svc.OnLobbyChatMessage service={Id(__instance)}");
}

#endif
