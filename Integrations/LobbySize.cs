#if !MONO
using System;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Multipass;
using SteamTransport = Il2CppFishySteamworks.FishySteamworks;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppSteamworks;
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
    public const int Tested = MAX_PLAYERS;     // 20, the default; above it the lobby screen switches to a table

    private static MelonPreferences_Entry<int>? _entry;
    private static MelonPreferences_Entry<int>? _fake;

    /// <summary>Placeholder slots shown on the lobby screen for layout testing. 0 = off.</summary>
    public static int FakePlayers => Math.Clamp(_fake?.Value ?? 0, 0, Max);

    /// <summary>Lobby size. Read at startup; the host panel can change it while hosting.</summary>
    public static int MaxPlayers { get; private set; } = MAX_PLAYERS;

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("MaxPlayers", MAX_PLAYERS, "Max players",
            $"Lobby size, {Min} to {Max}. Default {Tested}. If you edit this file, restart the game. The host panel changes it without a restart.");
        int v = _entry.Value;
        int clamped = Math.Clamp(v, Min, Max);
        if (clamped != v)
            MelonLogger.Warning($"MaxPlayers {v} is outside {Min}-{Max}; using {clamped}");
        MaxPlayers = clamped;

        _fake = cat.CreateEntry("DebugFakePlayers", 0, "Debug: fake lobby players",
            "Testing only. Shows this many placeholder players on the lobby screen to check the layout. They aren't real, aren't sent over the network and can't be invited. Leave at 0.");
        if (FakePlayers > 0)
            MelonLogger.Warning($"DebugFakePlayers is {FakePlayers}: the lobby screen shows placeholder players for testing. Set it back to 0 to play normally.");

        Utils.Log.Info($"Lobby size: {MaxPlayers} players");
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
            Utils.Log.Info($"Steam transport player limit raised from {before} to {MaxPlayers} ({when})");
            _lastLogged = MaxPlayers;
        }
        else if (before != _lastLogged)
        {
            Utils.Log.Info($"Steam transport player limit is {before}; lobby size {MaxPlayers} fits ({when})");
            _lastLogged = before;
        }
    }

    // --- Changing the size while hosting -------------------------------------------------
    // Steam lets a lobby's owner change its member limit at any time (SetLobbyMemberLimit),
    // and the Steam transport's server socket has a live SetMaximumClients. Both are set,
    // then read back, so the log shows what Steam actually accepted.

    /// <summary>Lowest size allowed right now: never below the players already in.</summary>
    public static int LiveMin => Math.Max(Min, HostLobby.MemberCount());

    public static string SetLive(int requested)
    {
        int n = Math.Clamp(requested, LiveMin, Max);
        int before = MaxPlayers;
        if (n == before) return $"Lobby size is {n}";
        MaxPlayers = n;
        if (_entry != null) { _entry.Value = n; MelonPreferences.Save(); }

        string steamPart = "not hosting a Steam lobby yet";
        if (HostLobby.TryGet(out var id))
        {
            bool ok = false; int readBack = -1;
            try
            {
                ok = SteamMatchmaking.SetLobbyMemberLimit(id, n);
                SteamMatchmaking.SetLobbyData(id, "max_players", n.ToString());
                SteamMatchmaking.SetLobbyData(id, "num_slots", n.ToString());
                readBack = SteamMatchmaking.GetLobbyMemberLimit(id);
            }
            catch (Exception e) { MelonLogger.Warning($"SetLobbyMemberLimit failed: {e.Message}"); }
            steamPart = ok && readBack == n
                ? $"Steam lobby limit is now {readBack}"
                : $"Steam {(ok ? "accepted" : "refused")} the change and reports {readBack}";
        }

        string transportPart = SetSteamLimit(n);
        HarmonyPatches.RefreshLobbyScreen();
        string msg = $"Lobby size changed from {before} to {n} while running: {steamPart}; {transportPart}";
        Utils.Log.Info(msg);
        return $"Lobby size {n}. {steamPart}";
    }

    private static string SetSteamLimit(int n)
    {
        try
        {
            var transport = InstanceFinder.TransportManager?.Transport;
            if (transport == null) return "no network transport yet";
            var multipass = transport.TryCast<Multipass>();
            SteamTransport? steam = null;
            if (multipass?._transports != null)
            {
                for (int i = 0; i < multipass._transports.Count && steam == null; i++)
                    steam = multipass._transports[i]?.TryCast<SteamTransport>();
            }
            else steam = transport.TryCast<SteamTransport>();
            if (steam == null) return "no Steam transport found";
            steam._maximumClients = (ushort)n;
            _lastLogged = n;
            if (steam._server == null) return $"transport limit {n} (server not started)";
            steam._server.SetMaximumClients(n);
            return $"transport limit now {steam._server.GetMaximumClients()}";
        }
        catch (Exception e) { return $"transport limit not changed ({e.Message})"; }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.CreateLobby))]
    private static void CreateLobby_Prefix() => EnsureTransportCapacity("creating lobby");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LoadManager), nameof(LoadManager.StartGame))]
    private static void StartGame_Prefix() => EnsureTransportCapacity("loading save");

    // --- Lobby screen table -----------------------------------------------------------------
    // The lobby screen lays slots out in one row from a zero-size anchor at the top right:
    // cell 75 px, spacing -150 px, so each shown player adds 75 px and 20 players fill about
    // 1500 px. Empty slots are inactive and take no space. Above 20, switch the grid to
    // rows of 20 (a table growing downwards) with the game's own spacing, so nothing
    // overlaps. Up to 5 rows (100 players) are shown; beyond that the title says how many more.

    public const int PerRow = 20;
    public const int MaxRows = 5;
    public static int MaxShown => PerRow * MaxRows; // 100

    private static GridLayoutGroup? _grid;
    public static GridLayoutGroup? Grid => _grid;
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
            Utils.Log.Info($"[diag] lobby row: constraint={grid.constraint} count={grid.constraintCount} cell={grid.cellSize} spacing={grid.spacing} " +
                            $"start={grid.startCorner}/{grid.startAxis} align={grid.childAlignment} anchors={rt.anchorMin}-{rt.anchorMax} pos={rt.anchoredPosition}");
        }
        catch { }
    }

    /// <summary>Turns on placeholder slots after the real players; returns how many were added
    /// (including any beyond the slots that exist, so the title can count them).</summary>
    public static int ShowFakePlayers(Il2CppReferenceArray<RectTransform>? slots)
    {
        int want = FakePlayers;
        if (want <= 0 || slots == null) return 0;
        int added = 0;
        for (int i = 0; i < slots.Length && added < want; i++)
        {
            var s = slots[i];
            if (s == null || s.gameObject.activeSelf) continue; // real player (or the local one)
            s.gameObject.SetActive(true);
            added++;
        }
        return want; // slots past MaxPlayers can't be shown; they count as "+N more"
    }

    // The invite button is the grid's first child, so it would take a table cell and push
    // the 100th player onto a sixth row. Take it out of the layout and park it just left of
    // the first row (where it sat before, one cell further out).
    private static void MoveInviteOutOfTable()
    {
        try
        {
            if (_grid == null || _grid.transform.childCount == 0) return;
            var gridRT = _grid.GetComponent<RectTransform>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridRT);
            var invite = _grid.transform.GetChild(0).GetComponent<RectTransform>();
            if (invite == null) return;
            Vector2 first = invite.anchoredPosition;          // cell 0 of the table
            var le = invite.GetComponent<LayoutElement>();
            if (le == null) le = invite.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            float step = -(_cellX + _origSpacingX);           // 75 px between columns
            invite.anchoredPosition = new Vector2(first.x - step, first.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridRT);
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't move the invite button out of the table: {e.Message}"); }
    }

    /// <summary>Lays the shown slots out as rows of 20; returns how many players aren't shown.</summary>
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
        if (MaxPlayers <= Tested) return 0; // original single row, untouched

        if (_grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount || _grid.constraintCount != PerRow)
        {
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = PerRow;
            _grid.spacing = new Vector2(_origSpacingX, _spacingY);
            MoveInviteOutOfTable();
        }
        return Math.Max(0, active - MaxShown);
    }
}

#endif
