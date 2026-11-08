#if !MONO
using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.UI.Multiplayer;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using static BiggerLobbies.Utils.Constants;

namespace BiggerLobbies.Integrations;

// Game 0.4.7 moved the Steam lobby logic from Lobby into SteamLobbyService,
// and made Lobby.PlayerLimit a static field the game itself uses.
[HarmonyPatch]
public static class HarmonyPatches
{
    private static Core? _modInstance;

    /// <summary>Set the mod instance for patch callbacks (called from Core).</summary>
    public static void SetModInstance(Core modInstance) => _modInstance = modInstance;

    // --- Lobby ---------------------------------------------------------------

    // NOTE: Lobby.PlayerLimit must NOT be written: in 0.4.7 it is a compile-time
    // constant in IL2CPP and il2cpp_field_static_set_value crashes the game.

    // The game's own invite check uses the constant limit, so replace it.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Lobby), nameof(Lobby.TryOpenInviteInterface))]
    private static bool Lobby_TryOpenInviteInterface_Prefix(Lobby __instance)
    {
        try
        {
            // Not in a lobby yet, or below the vanilla limit: let the game do its
            // normal flow (it creates the lobby and opens the invite once it exists).
            // Creating the lobby ourselves here produced two lobbies.
            if (!__instance.IsInLobby || __instance.PlayerCount < VANILLA_LIMIT)
                return true;

            if (__instance.PlayerCount >= LobbySize.MaxPlayers)
            {
                MelonLogger.Warning("Lobby already at max capacity!");
                return false;
            }

            // In a lobby and past the vanilla 4-player limit: open the invite UI directly.
            var svc = __instance._lobbyService?.TryCast<SteamLobbyService>();
            if (svc == null) return true;
            svc.OpenInviteUI();
            return false;
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Failed to patch TryOpenInviteInterface: {e}");
            return true;
        }
    }

    // --- SteamLobbyService ----------------------------------------------------

    private static void EnsurePlayersArray(SteamLobbyService svc)
    {
        // Sized for Steam's cap, not our own MaxPlayers: a host can run a bigger lobby than
        // this machine's setting, and the game writes _players[i] for every member unchecked.
        var players = svc._players;
        if (players != null && players.Length >= LobbySize.Max) return;
        var arr = new Il2CppStructArray<CSteamID>(LobbySize.Max);
        if (players != null)
            for (int i = 0; i < players.Length; i++) arr[i] = players[i];
        svc._players = arr;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.Initialize))]
    private static void Service_Initialize_Postfix(SteamLobbyService __instance)
    {
        try { EnsurePlayersArray(__instance); }
        catch (Exception e) { MelonLogger.Error($"Failed to resize players array: {e}"); }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.UpdateLobbyMembers))]
    private static void Service_UpdateLobbyMembers_Prefix(SteamLobbyService __instance)
    {
        try { EnsurePlayersArray(__instance); }
        catch (Exception e) { MelonLogger.Error($"Failed to resize players array: {e}"); }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.CreateLobby))]
    private static void Service_CreateLobby_Prefix(SteamLobbyService __instance, ref int maxPlayers)
    {
        if (maxPlayers < LobbySize.MaxPlayers) maxPlayers = LobbySize.MaxPlayers;
        try { EnsurePlayersArray(__instance); }
        catch (Exception e) { MelonLogger.Error($"Failed to resize players array: {e}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void Service_OnLobbyCreated_Postfix(LobbyCreated_t result)
    {
        try
        {
            if (result.m_eResult != EResult.k_EResultOK) return;
            var id = new CSteamID(result.m_ulSteamIDLobby);
            string n = LobbySize.MaxPlayers.ToString();
            SteamMatchmaking.SetLobbyMemberLimit(id, LobbySize.MaxPlayers);
            SteamMatchmaking.SetLobbyData(id, "max_players", n);
            SteamMatchmaking.SetLobbyData(id, "num_slots", n);
            MelonLogger.Msg($"Lobby {result.m_ulSteamIDLobby} created with {LobbySize.MaxPlayers} slots");
        }
        catch (Exception e) { MelonLogger.Error($"Failed to set lobby limits: {e}"); }
    }

    // --- LobbyInterface (UI) ---------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LobbyInterface), nameof(LobbyInterface.Start))]
    private static void LobbyInterface_Start_Postfix(LobbyInterface __instance)
    {
        try { ExpandSlots(__instance); }
        catch (Exception e) { MelonLogger.Error($"Failed to expand lobby UI: {e}"); }
    }

    private static void ExpandSlots(LobbyInterface ui)
    {
        var grid = ui.GetComponentInChildren<GridLayoutGroup>(true);
        if (grid == null) { MelonLogger.Error("Could not find GridLayoutGroup in LobbyInterface"); return; }
        var entries = grid.transform;
        if (entries.childCount < 2) { MelonLogger.Warning("Lobby UI has no player slot to clone"); return; }

        int existing = entries.childCount - 1; // child 0 is not a player slot
        var template = entries.GetChild(1);
        for (int i = existing; i < LobbySize.MaxPlayers; i++)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, entries);
            go.name = $"{template.gameObject.name} ({entries.childCount - 1})";
        }

        LobbySize.RememberGrid(grid);

        int slotCount = entries.childCount - 1;
        var slots = new Il2CppReferenceArray<RectTransform>(slotCount);
        for (int j = 1; j < entries.childCount; j++)
            slots[j - 1] = entries.GetChild(j).GetComponent<RectTransform>();
        ui.PlayerSlots = slots;
        UpdateTitle(ui);
        try { ui.UpdateUI(); } catch { }
    }

    private static void UpdateTitle(LobbyInterface ui, int hidden = 0, int fake = 0)
    {
        if (ui.Lobby == null || ui.LobbyTitle == null) return;
        string more = hidden > 0 ? $", +{hidden} more" : "";
        string test = fake > 0 ? $" [{fake} fake]" : "";
        ui.LobbyTitle.text = $"Lobby ({ui.Lobby.PlayerCount + fake}/{LobbySize.MaxPlayers}{more}){test}";
    }

    private static int CountActive(Il2CppReferenceArray<RectTransform>? slots)
    {
        int n = 0;
        if (slots != null)
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && slots[i].gameObject.activeSelf) n++;
        return n;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LobbyInterface), nameof(LobbyInterface.UpdatePlayers))]
    private static void LobbyInterface_UpdatePlayers_Postfix(LobbyInterface __instance)
    {
        try
        {
            int hidden = 0, fake = 0, unshownFake = 0;
            try
            {
                var slots = __instance.PlayerSlots;
                if (LobbySize.FakePlayers > 0)
                {
                    int before = CountActive(slots);
                    fake = LobbySize.ShowFakePlayers(slots);
                    unshownFake = fake - (CountActive(slots) - before); // more fakes than slots
                }
                hidden = LobbySize.FitRow(slots) + unshownFake;
            }
            catch (Exception e) { MelonLogger.Error($"Failed to fit lobby row: {e}"); }
            UpdateTitle(__instance, hidden, fake);
        }
        catch (Exception e) { MelonLogger.Error($"Failed to update lobby title: {e}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LobbyInterface), nameof(LobbyInterface.UpdateButtons))]
    private static void LobbyInterface_UpdateButtons_Postfix(LobbyInterface __instance)
    {
        try
        {
            if (__instance.Lobby == null || __instance.InviteButton == null) return;
            __instance.InviteButton.gameObject.SetActive(__instance.Lobby.IsHost && __instance.Lobby.PlayerCount < LobbySize.MaxPlayers);
        }
        catch (Exception e) { MelonLogger.Error($"Failed to patch UpdateButtons: {e}"); }
    }
}

#endif
