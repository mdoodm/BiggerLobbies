#if !MONO
using System;
using HarmonyLib;
using Il2CppSteamworks;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Who can join the host's Steam lobby. Applied when the lobby is created, and straight
// away to an open lobby when changed from the host panel.
//   Private      only people the host invites can join (Steam's Private lobby)
//   FriendsOnly  Steam friends can use Join Game, and anyone can be invited (the game's default)
//   Open         listed in Steam's lobby search for this game, so anyone using a lobby
//                browser can find and join it (Steam's Public lobby)
// The whitelist and blacklist apply on top of all three.
[HarmonyPatch]
public static class LobbyPrivacy
{
    public enum Mode { Private, FriendsOnly, Open }

    private static MelonPreferences_Entry<string>? _entry;

    public static Mode Current { get; private set; } = Mode.FriendsOnly;

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("LobbyPrivacy", nameof(Mode.FriendsOnly), "Lobby privacy",
            "Who can join your lobby: Private (only people you invite), FriendsOnly (Steam friends can use Join Game; the game's default) or Open (anyone who finds it in Steam's lobby search). Can also be changed in Host settings.");
        string v = (_entry.Value ?? "").Trim();
        if (v.Equals("InviteOnly", StringComparison.OrdinalIgnoreCase)) v = nameof(Mode.Private);   // names used before 1.0.8
        if (v.Equals("Public", StringComparison.OrdinalIgnoreCase)) v = nameof(Mode.Open);
        if (Enum.TryParse(v, true, out Mode m) && Enum.IsDefined(typeof(Mode), m)) Current = m;
        else MelonLogger.Warning($"LobbyPrivacy '{_entry.Value}' isn't Private, FriendsOnly or Open; using FriendsOnly");
        if (Current != Mode.FriendsOnly) Utils.Log.Info($"Lobby privacy: {Label(Current)}");
    }

    public static string Label(Mode m) => m switch
    {
        Mode.Private => "Private",
        Mode.Open => "Open",
        _ => "Friends only",
    };

    private static ELobbyType SteamType(Mode m) => m switch
    {
        Mode.Private => ELobbyType.k_ELobbyTypePrivate,
        Mode.Open => ELobbyType.k_ELobbyTypePublic,
        _ => ELobbyType.k_ELobbyTypeFriendsOnly,
    };

    [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.CreateLobby))]
    private static void CreateLobby_Prefix(ref ELobbyType eLobbyType)
    {
        var want = SteamType(Current);
        if (eLobbyType != want)
        {
            Utils.Log.Info($"Lobby privacy: {Label(Current)} (the game asked for {eLobbyType})");
            eLobbyType = want;
        }
    }

    // The base game kicks anyone who isn't the host's Steam friend as they load in
    // (Player.SetPlayerNameAndId_Server -> PlatformFriends.IsLocalPlayerFriendsWith, its only
    // caller). With an Open lobby, strangers are meant to get in, so skip that check; the
    // whitelist and blacklist still apply at login.
    [HarmonyPrefix, HarmonyPatch(typeof(Il2CppScheduleOne.Platform.PlatformFriends), nameof(Il2CppScheduleOne.Platform.PlatformFriends.IsLocalPlayerFriendsWith))]
    private static bool IsLocalPlayerFriendsWith_Prefix(ref bool __result)
    {
        if (Current != Mode.Open) return true;
        __result = true;
        return false;
    }

    /// <summary>Saves the setting and applies it to the open lobby. Returns a status line.</summary>
    public static string Set(Mode m)
    {
        Current = m;
        if (_entry != null) { _entry.Value = m.ToString(); MelonPreferences.Save(); }
        if (!HostLobby.TryGet(out var id)) return $"Privacy set to {Label(m)}. It applies when you next host.";
        bool ok;
        try { ok = SteamMatchmaking.SetLobbyType(id, SteamType(m)); }
        catch (Exception e) { MelonLogger.Warning($"SetLobbyType failed: {e.Message}"); ok = false; }
        Utils.Log.Info($"Lobby privacy changed to {Label(m)} on the open lobby: Steam {(ok ? "accepted" : "refused")} it");
        return ok ? $"Privacy is now {Label(m)}" : $"Steam refused the change. It applies when you next host.";
    }
}

#endif
