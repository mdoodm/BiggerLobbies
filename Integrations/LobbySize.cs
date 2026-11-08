#if !MONO
using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Multipass;
using SteamTransport = Il2CppFishySteamworks.FishySteamworks;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using static BiggerLobbies.Utils.Constants;

namespace BiggerLobbies.Integrations;

// Lobby size setting, the network transport's player limit, and a scrolling lobby screen
// for lobbies bigger than the 20 slots the screen was laid out for.
[HarmonyPatch]
public static class LobbySize
{
    public const int Min = VANILLA_LIMIT;      // 4
    public const int Max = 250;                // Steam's lobby member cap
    public const int Tested = MAX_PLAYERS;     // 20, the default; anything above is experimental

    private static MelonPreferences_Entry<int>? _entry;

    /// <summary>Lobby size for this session. Read once at startup; changing it needs a restart.</summary>
    public static int MaxPlayers { get; private set; } = MAX_PLAYERS;

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("MaxPlayers", MAX_PLAYERS, "Max players",
            $"Lobby size, {Min} to {Max}. {Tested} is the tested default; anything above it is experimental. Restart the game after changing it.");
        int v = _entry.Value;
        int clamped = Math.Clamp(v, Min, Max);
        if (clamped != v)
            MelonLogger.Warning($"MaxPlayers {v} is outside {Min}-{Max}; using {clamped}");
        MaxPlayers = clamped;

        if (MaxPlayers > Tested)
            MelonLogger.Warning($"MaxPlayers is {MaxPlayers}. Lobbies above {Tested} players are EXPERIMENTAL: the host carries every player's traffic, and parts of the game may still assume 4 players.");
        else
            MelonLogger.Msg($"Lobby size: {MaxPlayers} players");
    }

    // --- Network transport ---------------------------------------------------------
    // The game's transport is a FishNet Multipass wrapping FishySteamworks (Steam) and
    // others. Multipass ignores the index-less Get/SetMaximumClients, and FishySteamworks
    // copies its own _maximumClients field into the server socket when the server starts,
    // so raise that field (and the live socket, if it's already running) on the host.

    private static int _lastLogged = -1;

    public static void EnsureTransportCapacity(string when)
    {
        try
        {
            var transport = InstanceFinder.TransportManager?.Transport;
            if (transport == null) return;
            var multipass = transport.TryCast<Multipass>();
            if (multipass?._transports != null)
            {
                var list = multipass._transports;
                for (int i = 0; i < list.Count; i++) RaiseSteamLimit(list[i], when);
            }
            else RaiseSteamLimit(transport, when);
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't check the network transport's player limit: {e.Message}"); }
    }

    private static void RaiseSteamLimit(Transport? t, string when)
    {
        var steam = t?.TryCast<SteamTransport>();
        if (steam == null) return;
        int before = steam._maximumClients;
        if (before < MaxPlayers)
        {
            steam._maximumClients = (ushort)MaxPlayers;
            try { if (steam._server != null) steam.SetMaximumClients(MaxPlayers); } catch { }
            MelonLogger.Msg($"Steam transport player limit raised from {before} to {MaxPlayers} ({when})");
            _lastLogged = MaxPlayers;
        }
        else if (before != _lastLogged)
        {
            MelonLogger.Msg($"Steam transport player limit is {before}; lobby size {MaxPlayers} fits ({when})");
            _lastLogged = before;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.CreateLobby))]
    private static void CreateLobby_Prefix() => EnsureTransportCapacity("creating lobby");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LoadManager), nameof(LoadManager.StartGame))]
    private static void StartGame_Prefix() => EnsureTransportCapacity("loading save");

    // --- Lobby screen row ------------------------------------------------------------------
    // The lobby screen lays slots out in one row from a zero-size anchor: cell 75 px, spacing
    // -150 px, so each shown player adds 75 px. Empty slots are inactive and take no space.
    // 20 players fill about 1500 px. Above that, pull the slots closer together so the row
    // stays the same width (avatars overlap like a fanned hand). Past ~60 players the overlap
    // gets unreadable, so only the first 60 are shown and the title says how many more.

    private const float RowWidth = 1500f;  // what 20 players use with the original spacing
    private const float MinStep = 25f;     // tightest readable overlap
    public static int MaxShown => (int)(RowWidth / MinStep); // 60

    private static GridLayoutGroup? _grid;
    private static float _cellX = 75f, _origSpacingX = -150f, _spacingY;
    private static bool _loggedLayout;

    public static void RememberGrid(GridLayoutGroup grid)
    {
        _grid = grid;
        _cellX = grid.cellSize.x;
        _origSpacingX = grid.spacing.x;
        _spacingY = grid.spacing.y;
        if (_loggedLayout) return;
        _loggedLayout = true;
        try
        {
            var rt = grid.GetComponent<RectTransform>();
            MelonLogger.Msg($"[diag] lobby row: constraint={grid.constraint} count={grid.constraintCount} cell={grid.cellSize} spacing={grid.spacing} " +
                            $"start={grid.startCorner}/{grid.startAxis} align={grid.childAlignment} anchors={rt.anchorMin}-{rt.anchorMax} pos={rt.anchoredPosition}");
        }
        catch { }
    }

    /// <summary>Fits the shown slots into the row; returns how many players aren't shown.</summary>
    public static int FitRow(Il2CppReferenceArray<RectTransform>? slots)
    {
        if (_grid == null || slots == null) return 0;
        int active = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s == null || !s.gameObject.activeSelf) continue;
            active++;
            if (active > MaxShown) s.gameObject.SetActive(false);
        }

        int shown = Math.Min(active, MaxShown);
        float origStep = -(_cellX + _origSpacingX);          // 75 with the game's values
        float step = shown > 0 ? Math.Min(origStep, RowWidth / shown) : origStep;
        if (origStep <= 0f) return Math.Max(0, active - MaxShown); // unexpected layout: leave it alone
        float spacingX = -(_cellX + step);
        if (Math.Abs(_grid.spacing.x - spacingX) > 0.01f)
            _grid.spacing = new Vector2(spacingX, _spacingY);
        return Math.Max(0, active - MaxShown);
    }
}

#endif
