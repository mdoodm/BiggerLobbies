#if !MONO
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppSteamworks;
using MelonLoader;
using UnityEngine;

namespace BiggerLobbies.Integrations;

// Steam doesn't let a host remove anyone from a Steam lobby, so a kicked player used to
// drop out of the game but stay in the lobby with "Authentication Error".
//
// Host: before disconnecting someone, post a short notice in the lobby's own chat
// ("bl1|kick|<steam id>|<kind>|<seconds>") and wait a moment so it arrives first.
// Players the host removed are also hidden from the host's lobby screen until they
// leave or rejoin the Steam lobby; banned players stay hidden.
//
// Client with this mod: a notice from the lobby owner naming us takes us back to the
// main menu with a clear popup and leaves the lobby. Vanilla clients log the line and
// ignore it (the game only acts on "ready", "load_tutorial" and "host_loading").
[HarmonyPatch]
public static class KickNotice
{
    public enum Kind { Kick, Ban, Whitelist }

    private const string Prefix = "bl1|kick|";
    private const float DisconnectDelay = 1.5f;

    // --- Host ------------------------------------------------------------------

    private static readonly HashSet<ulong> _hidden = new();
    private static readonly Dictionary<ulong, float> _lastSent = new();

    /// <summary>True for players the host removed this session, or has banned.</summary>
    public static bool IsHidden(ulong id) => id != 0 && (_hidden.Contains(id) || BanList.IsBanned(id));

    /// <summary>Tells the player (if they have the mod) why, hides them, then disconnects this connection.</summary>
    public static void Remove(NetworkConnection? conn, ulong steamId, Kind kind, TimeSpan? length = null)
    {
        Send(steamId, kind, length);
        Hide(steamId);
        if (conn != null) MelonCoroutines.Start(DisconnectLater(conn));
    }

    private static IEnumerator DisconnectLater(NetworkConnection conn)
    {
        yield return new WaitForSecondsRealtime(DisconnectDelay);
        try { if (conn.IsActive) conn.Disconnect(true); }
        catch (Exception e) { MelonLogger.Warning($"Couldn't disconnect player: {e.Message}"); }
    }

    /// <summary>Posts the notice in the lobby chat. Repeats for the same player are spaced out.</summary>
    public static void Send(ulong steamId, Kind kind, TimeSpan? length = null)
    {
        try
        {
            if (steamId == 0 || !HostLobby.TryGet(out var lobby)) return;
            float now = Time.realtimeSinceStartup;
            if (_lastSent.TryGetValue(steamId, out float last) && now - last < 3f) return;
            _lastSent[steamId] = now;
            long secs = length.HasValue ? (long)Math.Max(1, length.Value.TotalSeconds) : -1;
            var bytes = Encoding.ASCII.GetBytes($"{Prefix}{steamId}|{kind.ToString().ToLowerInvariant()}|{secs}");
            var buf = new Il2CppStructArray<byte>(bytes.Length + 1);
            for (int i = 0; i < bytes.Length; i++) buf[i] = bytes[i];
            SteamMatchmaking.SendLobbyChatMsg(lobby, buf, buf.Length);
            Utils.Log.Info($"Sent {kind} notice to {steamId}");
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't send the kick notice: {e.Message}"); }
    }

    private static void Hide(ulong steamId)
    {
        if (steamId == 0) return;
        _hidden.Add(steamId);
        try { HostLobby.Service()?.UpdateLobbyMembers(); } catch { }
        HarmonyPatches.RefreshLobbyScreen();
    }

    // Drop hidden players from the member list the lobby screen and counts read.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.UpdateLobbyMembers))]
    private static void UpdateLobbyMembers_Postfix(SteamLobbyService __instance)
    {
        try
        {
            if (_hidden.Count == 0 && BanList.Bans.Count == 0) return;
            if (!HostLobby.TryGet(out _)) return;
            var p = __instance._players;
            if (p == null) return;
            int w = 0;
            for (int r = 0; r < p.Length; r++)
            {
                var id = p[r];
                if (id.m_SteamID != 0 && IsHidden(id.m_SteamID)) continue;
                p[w++] = id;
            }
            for (; w < p.Length; w++) p[w] = default;
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't hide removed players: {e.Message}"); }
    }

    // Anyone who leaves or joins the Steam lobby again starts fresh (bans still apply).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static void PlayerEnterOrLeave_Prefix(LobbyChatUpdate_t result)
    {
        try { _hidden.Remove(result.m_ulSteamIDUserChanged); } catch { }
    }

    // --- Client ----------------------------------------------------------------

    private static MainMenuPopup.Data? _notice;
    private static string _title = "", _text = "";
    private static float _noticeAt = -999f;
    private static bool Recent => _notice != null && Time.realtimeSinceStartup - _noticeAt < 30f;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static void OnLobbyChatMessage_Postfix(LobbyChatMsg_t result)
    {
        try
        {
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            ulong me = SteamUser.GetSteamID().m_SteamID;
            ulong owner = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID;
            if (result.m_ulSteamIDUser != owner || owner == me) return; // only the host's word counts

            var buf = new Il2CppStructArray<byte>(256);
            int n = SteamMatchmaking.GetLobbyChatEntry(lobby, (int)result.m_iChatID, out _, buf, buf.Length, out _);
            if (n <= Prefix.Length) return;
            var bytes = new byte[n];
            for (int i = 0; i < n; i++) bytes[i] = buf[i];
            string msg = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            if (!msg.StartsWith(Prefix, StringComparison.Ordinal)) return;

            var parts = msg.Substring(Prefix.Length).Split('|');
            if (parts.Length < 3 || !ulong.TryParse(parts[0], out ulong target) || target != me) return;
            long.TryParse(parts[2], out long secs);
            Show(parts[1], secs, SafeName(owner));
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't read a lobby message: {e.Message}"); }
    }

    private static string SafeName(ulong id)
    {
        try { var n = SteamFriends.GetFriendPersonaName(new CSteamID(id)); if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return "The host";
    }

    private static string Span(long secs)
    {
        if (secs < 0) return "permanently";
        var t = TimeSpan.FromSeconds(secs);
        if (t.TotalMinutes < 60) return $"for {Math.Max(1, Math.Ceiling(t.TotalMinutes))} min";
        if (t.TotalHours < 48) return $"for {Math.Ceiling(t.TotalHours)} h";
        return $"for {Math.Ceiling(t.TotalDays)} days";
    }

    private static void Show(string kind, long secs, string host)
    {
        switch (kind)
        {
            case "ban":
                _title = "Banned from this lobby";
                _text = $"{host} banned you {Span(secs)}.";
                break;
            case "whitelist":
                _title = "Not on the whitelist";
                _text = $"{host} only lets in players on their whitelist. Ask them to add you, then join again.";
                break;
            default:
                _title = "Kicked by the host";
                _text = $"{host} removed you from the game. You can join again.";
                break;
        }
        _notice = new MainMenuPopup.Data(_title, _text, true);
        _noticeAt = Time.realtimeSinceStartup;
        MelonLogger.Warning($"{_title}: {_text}");

        try
        {
            var lm = Singleton<LoadManager>.Instance;
            if (lm != null && lm.IsGameLoaded && !lm.IsLoading)
            {
                lm.ExitToMenu(null, _notice, false); // leaves the lobby too
                return;
            }
            if (lm != null && lm.IsLoading) return; // the failed load returns to the menu; its popup is replaced below
            try { Singleton<Lobby>.Instance?.LeaveLobby(); } catch { }
            Singleton<MainMenuPopup>.Instance?.Open(_notice);
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't return to the menu: {e.Message}"); }
    }

    // The disconnect that follows makes the game show its own error popup
    // ("Authentication Error"). Show the host's reason instead.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuPopup), nameof(MainMenuPopup.Open), new[] { typeof(MainMenuPopup.Data) })]
    private static void Open_Data_Prefix(ref MainMenuPopup.Data data)
    {
        if (Recent && data != null && data.Title != _title) data = _notice!;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuPopup), nameof(MainMenuPopup.Open), new[] { typeof(string), typeof(string), typeof(bool) })]
    private static void Open_Text_Prefix(ref string title, ref string description, ref bool isBad)
    {
        if (!Recent || title == _title) return;
        title = _title; description = _text; isBad = true;
    }
}

#endif
