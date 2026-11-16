using System;
using System.Collections;
using System.Text;
using HarmonyLib;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Cross-branch play: friends on the other Steam branch (Il2Cpp default vs Mono alternate) join too.
//
// A joining player's game leaves any lobby whose "version" isn't exactly its own
// (SteamLobbyService.OnLobbyEntered), and a lobby holds one value per key. So while you host,
// this keeps a second, invisible Steam lobby next to the game's own one, with the other
// branch's version. Everything else in it copies the main lobby: "ready", "host_loading" and
// the rest of the lobby data once a second, and every lobby chat message straight away (the
// game's "ready"/"host_loading", kick and ban notices). Its owner is you, so a friend who
// enters it connects to your game exactly like everyone else; after that, the game itself
// never looks at Steam lobbies again (sleeping, the map and so on use the connected players).
//
// The game listens for every lobby's Steam events, not only its own lobby's. Left alone, it
// would take the second lobby for its own when it's created or entered, and read its chat
// with the wrong lobby's message ids. The prefixes below skip the game's handlers for the
// second lobby; other features check Handling to skip their own patches on the same events.
//
// Friends on the other branch join with Invite (other branch) in Host settings. Steam's
// Join Game on your profile points at the main lobby, so for them it still shows the
// game's "Version Mismatch".
[HarmonyPatch]
public static class CrossBranchLobby
{
    private const float PendingTimeout = 15f;
    private const float RetryAfter = 30f;
    private const float SyncEvery = 1f;
    private const float ReadyDelay = 2f, ReadySpacing = 5f;

    // Lobby data copied from the main lobby. "version" is the one key that differs.
    private static readonly string[] Keys = { "owner", "host_loading", "ready", "load_tutorial", "max_players", "num_slots" };

    private static MelonPreferences_Entry<bool>? _entry;
    private static ulong _second;               // the second lobby's id, 0 when there isn't one
    private static ulong _main;                 // the main lobby it belongs to
    private static float _pendingSince = -1f;
    private static float _retryAt, _nextSync;
    private static bool _readyPending;
    private static float _lastReady = -999f;
    private static int _noMirror;

    /// <summary>True while one of the game's lobby handlers is running for the second lobby.</summary>
    public static bool Handling { get; private set; }

    /// <summary>True during our own CreateLobby call: privacy and dedupe leave it alone.</summary>
    public static bool Creating { get; private set; }

    public static ulong SecondId => _second;

    public static bool Enabled
    {
        get => _entry?.Value ?? false;
        set
        {
            if (_entry == null) return;
            _entry.Value = value;
            MelonPreferences.Save();
            if (!value) LeaveSecond("turned off");
            _retryAt = 0f;
        }
    }

    private static bool Pending => _pendingSince >= 0f && UnityEngine.Time.realtimeSinceStartup - _pendingSince < PendingTimeout;

    private static void Log(Func<string> s) { if (Utils.Log.Verbose) Utils.Log.Info($"[diag] [cross-branch] {s()}"); }

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("CrossBranchLobby", false, "Cross-branch lobby",
            "Experimental. While you host, also open a hidden lobby for players on the other Steam branch (Il2Cpp default or Mono alternate), so both can join your game. Invite them with Invite (other branch) in Host settings.");
        if (Enabled) MelonLogger.Warning($"CrossBranchLobby is on: players on the other Steam branch ({OtherVersion()}) can join through a second lobby. Experimental.");
    }

    /// <summary>The other branch's version string: adds or removes " Alternate".</summary>
    public static string OtherVersion()
    {
        const string suffix = " Alternate";
        string mine = UnityEngine.Application.version;
        return mine.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? mine.Substring(0, mine.Length - suffix.Length)
            : mine + suffix;
    }

    /// <summary>Chat messages sent inside this scope stay in the main lobby (for per-lobby "ready" re-sends).</summary>
    public static IDisposable NoMirror() { _noMirror++; return new Scope(); }
    private sealed class Scope : IDisposable { public void Dispose() => _noMirror--; }

    // --- Lifecycle --------------------------------------------------------------------------

    public static void Update()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (_pendingSince >= 0f && !Pending)
        {
            _pendingSince = -1f;
            _retryAt = now + RetryAfter;
            MelonLogger.Warning("Cross-branch lobby: Steam didn't answer in time; trying again in 30 s");
        }

        try { EndFlip(now); }
        catch (Exception e) { Log(() => $"ending the Join Game window failed: {e.Message}"); }

        bool hosting = HostLobby.TryGet(out var main);
        if (_second != 0 && (!Enabled || !hosting || main.m_SteamID != _main))
        {
            LeaveSecond(!hosting ? "you're not hosting" : !Enabled ? "turned off" : "the main lobby changed");
            return;
        }
        if (!Enabled || !hosting) return;

        if (_second == 0)
        {
            if (!Pending && now >= _retryAt) Create(main);
            return;
        }
        if (now >= _nextSync)
        {
            _nextSync = now + SyncEvery;
            Sync(main, new CSteamID(_second));
        }
    }

    private static void Create(CSteamID main)
    {
        _pendingSince = UnityEngine.Time.realtimeSinceStartup;
        _main = main.m_SteamID;
        SteamAPICall_t call;
        Creating = true;
        try { call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeInvisible, LobbySize.MaxPlayers); }
        catch (Exception e)
        {
            MelonLogger.Warning($"Cross-branch lobby: couldn't create it: {e.Message}");
            call = SteamAPICall_t.Invalid;
        }
        finally { Creating = false; }
        if (call == SteamAPICall_t.Invalid)
        {
            _pendingSince = -1f;
            _retryAt = UnityEngine.Time.realtimeSinceStartup + RetryAfter;
            return;
        }
        Log(() => $"creating the second lobby for main lobby {_main}");
    }

    private static void Claim(ulong id)
    {
        _pendingSince = -1f;
        if (_second == id) return;
        _second = id;
        _nextSync = 0f;
        try
        {
            var lobby = new CSteamID(id);
            if (HostLobby.TryGet(out var main)) Sync(main, lobby);
            Utils.Log.Info($"Cross-branch lobby {id} open for players on {OtherVersion()}");
        }
        catch (Exception e) { MelonLogger.Warning($"Cross-branch lobby: couldn't set it up: {e.Message}"); }
    }

    private static void Sync(CSteamID main, CSteamID second)
    {
        try
        {
            foreach (var key in Keys)
            {
                string v = SteamMatchmaking.GetLobbyData(main, key) ?? "";
                if (SteamMatchmaking.GetLobbyData(second, key) != v) SteamMatchmaking.SetLobbyData(second, key, v);
            }
            string other = OtherVersion();
            if (SteamMatchmaking.GetLobbyData(second, "version") != other) SteamMatchmaking.SetLobbyData(second, "version", other);
        }
        catch (Exception e) { Log(() => $"sync failed: {e.Message}"); }
        ApplyLimits();
    }

    // --- Shared size budget -------------------------------------------------------------------
    // Both lobbies share one lobby size. You're a member of both, so the people in your game
    // are you + the others in the main lobby + the others in the second one. Each lobby's
    // Steam limit is the size minus the others in the *other* lobby: whichever fills the
    // remaining places first, the total stops at the lobby size. When the total is within the
    // size, neither limit ever drops below the members a lobby already has.
    // The "max_players" data the lobby screen shows stays at the full size.

    /// <summary>Steam limit for the main lobby at lobby size <paramref name="size"/>.</summary>
    public static int MainLimitFor(int size) =>
        _second == 0 ? size : Math.Max(1, size - ExtraMembers());

    /// <summary>Re-balances both lobbies' Steam limits. Called on member changes, size changes and every sync.</summary>
    public static void ApplyLimits()
    {
        if (_second == 0 || !HostLobby.TryGet(out var main)) return;
        try
        {
            var second = new CSteamID(_second);
            int size = LobbySize.MaxPlayers;
            int inMain = SteamMatchmaking.GetNumLobbyMembers(main);
            int inSecond = SteamMatchmaking.GetNumLobbyMembers(second);
            int othersMain = Math.Max(0, inMain - 1), othersSecond = ExtraMembers();
            int mainLimit = Math.Max(Math.Max(1, inMain), size - othersSecond);
            int secondLimit = Math.Max(Math.Max(1, inSecond), size - othersMain);
            SetLimit(main, mainLimit, "main");
            SetLimit(second, secondLimit, "second");
        }
        catch (Exception e) { Log(() => $"limit update failed: {e.Message}"); }
    }

    private static void SetLimit(CSteamID lobby, int limit, string which)
    {
        if (SteamMatchmaking.GetLobbyMemberLimit(lobby) == limit) return;
        bool ok = SteamMatchmaking.SetLobbyMemberLimit(lobby, limit);
        Log(() => $"{which} lobby limit -> {limit} ({(ok ? "accepted" : "refused")})");
    }

    // Someone joined or left the main lobby: the second lobby's share changes, and a quick
    // leave may be a friend on the other branch who used Join Game.
    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static void Main_PlayerEnterOrLeave_Postfix(LobbyChatUpdate_t result)
    {
        if (Handling) return;
        ApplyLimits();
        try { WatchJoinGame(result); }
        catch (Exception e) { Log(() => $"join game fallback failed: {e.Message}"); }
    }

    // --- Join Game fallback -------------------------------------------------------------------
    // Steam's Join Game always leads to the main lobby; invisible lobbies can't be its target.
    // A friend on the other branch enters it, their game sees the wrong version and leaves at
    // once. Nobody else leaves that fast: a player on your branch stays in the lobby for as
    // long as they play. So a quick leave switches the main lobby's version to the other one
    // for a short window, and the friend's second Join Game works. A quick leave during the
    // window (someone on your branch) switches it straight back.

    private const float QuickLeave = 5f;
    private const float FlipWindow = 15f;
    private static readonly System.Collections.Generic.Dictionary<ulong, float> _enteredAt = new();
    private static float _flipUntil = -1f;

    private static void WatchJoinGame(LobbyChatUpdate_t result)
    {
        if (!Enabled || !HostLobby.TryGet(out var main) || result.m_ulSteamIDLobby != main.m_SteamID) return;
        ulong who = result.m_ulSteamIDUserChanged;
        if (who == 0 || who == SteamUser.GetSteamID().m_SteamID) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        uint change = result.m_rgfChatMemberStateChange;
        if ((change & 0x1u) != 0) { _enteredAt[who] = now; return; }
        if ((change & (0x2u | 0x4u)) == 0) return;   // left or disconnected; kicks and bans don't count
        if (!_enteredAt.TryGetValue(who, out float at)) return;
        _enteredAt.Remove(who);
        if (now - at > QuickLeave || KickNotice.IsHidden(who)) return;

        string mine = UnityEngine.Application.version;
        string target = SteamMatchmaking.GetLobbyData(main, "version") == mine ? OtherVersion() : mine;
        SteamMatchmaking.SetLobbyData(main, "version", target);
        string name = DepartedPlayers.NameOf(who);
        if (target != mine)
        {
            _flipUntil = now + FlipWindow;
            MelonLogger.Warning($"{name} left your lobby straight away, probably on the other branch. For {FlipWindow:0} s your lobby accepts {target}, so their next Join Game works. Players on your branch can't join until then");
        }
        else
        {
            _flipUntil = -1f;
            MelonLogger.Warning($"{name} left your lobby straight away, probably on your branch; it accepts {mine} again");
        }
    }

    // Called from Update: switch the main lobby back once the window is over.
    private static void EndFlip(float now)
    {
        if (_flipUntil < 0f || now < _flipUntil) return;
        _flipUntil = -1f;
        if (CrossBranchTest.Active || !HostLobby.TryGet(out var main)) return;   // the manual switch is in charge
        string mine = UnityEngine.Application.version;
        if (SteamMatchmaking.GetLobbyData(main, "version") == mine) return;
        SteamMatchmaking.SetLobbyData(main, "version", mine);
        Utils.Log.Info($"Join Game window over: your lobby accepts {mine} again");
    }

    private static void LeaveSecond(string why)
    {
        ulong id = _second;
        _second = 0;
        _main = 0;
        _pendingSince = -1f;
        _enteredAt.Clear();
        if (id == 0) return;
        try { SteamMatchmaking.LeaveLobby(new CSteamID(id)); }
        catch (Exception e) { MelonLogger.Warning($"Cross-branch lobby: couldn't leave it: {e.Message}"); }
        // The main lobby gets the whole size back.
        try { if (HostLobby.TryGet(out var main)) SteamMatchmaking.SetLobbyMemberLimit(main, LobbySize.MaxPlayers); }
        catch { }
        Utils.Log.Info($"Cross-branch lobby closed ({why})");
    }

    // --- Host actions -----------------------------------------------------------------------

    /// <summary>Opens Steam's invite dialog for the second lobby. Returns a status line.</summary>
    public static string OpenInvite()
    {
        if (!Enabled) return "Turn on Other branch first";
        if (!HostLobby.TryGet(out _)) return "Host a save first";
        if (_second == 0) return "The other-branch lobby isn't open yet. Try again in a moment";
        try
        {
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(_second));
            return $"Invite friends on {OtherVersion()}";
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Cross-branch lobby: couldn't open the invite dialog: {e.Message}");
            return "Couldn't open Steam's invite dialog";
        }
    }

    /// <summary>Players in the second lobby, not counting you.</summary>
    public static int ExtraMembers()
    {
        if (_second == 0) return 0;
        try
        {
            var lobby = new CSteamID(_second);
            ulong me = SteamUser.GetSteamID().m_SteamID;
            int n = SteamMatchmaking.GetNumLobbyMembers(lobby), extra = 0;
            for (int i = 0; i < n; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID != me) extra++;
            return extra;
        }
        catch { return 0; }
    }

    public static string StatusLine()
    {
        if (!Enabled) return "Off. Only players on your branch can join";
        if (!HostLobby.TryGet(out _)) return $"Opens when you host. Players on {OtherVersion()} join with Invite";
        if (_second == 0) return "Opening the other-branch lobby...";
        int n = ExtraMembers();
        if (_flipUntil > 0f)
            return $"Your lobby accepts {OtherVersion()} for {Math.Max(0, _flipUntil - UnityEngine.Time.realtimeSinceStartup):0} s, for a friend using Join Game";
        return $"Open for {OtherVersion()}. {n} joined through it. Invite them with the button, or their second Join Game works";
    }

    // --- Keeping the game's lobby handlers out of the second lobby ----------------------------

    // Is this callback about our second lobby? While our create is pending, any lobby that
    // isn't the main one is ours: Steam may report the enter before the create.
    private static bool IsOurs(ulong id)
    {
        if (id != 0 && id == _second) return true;
        return Pending && id != _main;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static bool OnLobbyCreated_Prefix(LobbyCreated_t result)
    {
        Handling = false;
        if (!IsOurs(result.m_ulSteamIDLobby)) return true;
        Handling = true;
        if (result.m_eResult != EResult.k_EResultOK || result.m_ulSteamIDLobby == 0)
        {
            _pendingSince = -1f;
            _retryAt = UnityEngine.Time.realtimeSinceStartup + RetryAfter;
            MelonLogger.Warning($"Cross-branch lobby: Steam couldn't create it ({result.m_eResult}); trying again in 30 s");
            return false;
        }
        Claim(result.m_ulSteamIDLobby);
        return false;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static bool OnLobbyEntered_Prefix(LobbyEnter_t result)
    {
        Handling = false;
        if (result.m_ulSteamIDLobby == 0 || !IsOurs(result.m_ulSteamIDLobby)) return true;
        Handling = true;
        Claim(result.m_ulSteamIDLobby);
        return false;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static bool PlayerEnterOrLeave_Prefix(LobbyChatUpdate_t result)
    {
        Handling = false;
        if (_second == 0 || result.m_ulSteamIDLobby != _second) return true;
        Handling = true;
        try
        {
            ulong who = result.m_ulSteamIDUserChanged;
            bool entered = (result.m_rgfChatMemberStateChange & 0x1u) != 0;
            Utils.Log.Info($"Cross-branch lobby: {DepartedPlayers.NameOf(who)} ({who}) {(entered ? "joined" : "left")}");
            ApplyLimits();
            if (entered) MaybeSendReady();
        }
        catch (Exception e) { MelonLogger.Warning($"Cross-branch lobby: couldn't handle a member change: {e.Message}"); }
        return false;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static bool OnLobbyChatMessage_Prefix(LobbyChatMsg_t result)
    {
        Handling = _second != 0 && result.m_ulSteamIDLobby == _second;
        return !Handling;
    }

    // Postfixes run even when a prefix skipped the original; clear the flag after all of them.
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void OnLobbyCreated_Postfix() => Handling = false;

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static void OnLobbyEntered_Postfix() => Handling = false;

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static void PlayerEnterOrLeave_Postfix() => Handling = false;

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static void OnLobbyChatMessage_Postfix() => Handling = false;

    // --- Late joins into the second lobby -----------------------------------------------------

    // Same as LateJoinFix, for the second lobby: past 4 members a vanilla client fails to read
    // the member list as it enters and never reaches the "ready" check, so it needs the chat
    // message. With 4 or fewer it joins from the lobby data by itself.
    private static void MaybeSendReady()
    {
        if (!LateJoinFix.Enabled || _readyPending) return;
        var load = Singleton<LoadManager>.Instance;
        if (load == null || !load.IsGameLoaded) return;
        _readyPending = true;
        MelonCoroutines.Start(SendReadySoon());
    }

    private static IEnumerator SendReadySoon()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        float sendAt = Math.Max(now + ReadyDelay, _lastReady + ReadySpacing);
        while (UnityEngine.Time.realtimeSinceStartup < sendAt) yield return null;
        _readyPending = false;
        try
        {
            if (_second == 0) yield break;
            var lobby = new CSteamID(_second);
            int members = SteamMatchmaking.GetNumLobbyMembers(lobby);
            if (members <= Utils.Constants.VANILLA_LIMIT) yield break;
            _lastReady = UnityEngine.Time.realtimeSinceStartup;
            SteamMatchmaking.SetLobbyData(lobby, "ready", "true");
            SendText(lobby, "ready");
            Utils.Log.Info("Cross-branch lobby: player joined mid-game; sent 'ready' so they can connect");
        }
        catch (Exception e) { MelonLogger.Warning($"Cross-branch lobby: 'ready' re-send failed: {e.Message}"); }
    }

    private static void SendText(CSteamID lobby, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var buf = Shim.Bytes(bytes.Length + 1);
        for (int i = 0; i < bytes.Length; i++) buf[i] = bytes[i];
        _noMirror++;
        try { SteamMatchmaking.SendLobbyChatMsg(lobby, buf, buf.Length); }
        finally { _noMirror--; }
    }

    // --- Mirroring and cleanup on the raw Steam calls -----------------------------------------

    // Every chat message to the main lobby goes to the second one too: the game's "ready" and
    // "host_loading", and the mod's kick and ban notices. (Positional names: the parameter
    // names differ between the two builds' Steamworks.)
    [HarmonyPostfix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.SendLobbyChatMsg))]
    private static void SendLobbyChatMsg_Postfix(CSteamID __0, ByteArray __1, int __2)
    {
        if (_second == 0 || _noMirror > 0 || __0.m_SteamID != _main || __1 == null) return;
        _noMirror++;
        try { SteamMatchmaking.SendLobbyChatMsg(new CSteamID(_second), __1, __2); }
        catch (Exception e) { Log(() => $"chat mirror failed: {e.Message}"); }
        finally { _noMirror--; }
    }

    // Leaving the main lobby leaves the second one too.
    [HarmonyPrefix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby))]
    private static void LeaveLobby_Prefix(CSteamID __0)
    {
        if (_second == 0) return;
        if (__0.m_SteamID == _second) { _second = 0; _main = 0; return; }
        if (__0.m_SteamID == _main) LeaveSecond("you left your lobby");
    }
}
