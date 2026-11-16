using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Extra Steam lobbies next to the game's own one, for two experimental features:
//
//  - Cross-branch play (CrossBranchLobby): friends on the other Steam branch (Il2Cpp default
//    vs Mono alternate) join too. A joining player's game leaves any lobby whose "version"
//    isn't exactly its own (SteamLobbyService.OnLobbyEntered), and a lobby holds one value per
//    key, so one extra lobby carries the other branch's version.
//  - Large lobbies (LobbySize.LargeLobbies): Steam caps one lobby at 250 members, so up to two
//    extra lobbies with your own version take the places past 250.
//
// The two are never on together. Extra lobbies are invisible (Steam allows one normal lobby
// plus a couple of invisible ones per account). Everything in them copies the main lobby:
// "ready", "host_loading" and the rest of the lobby data once a second, and every lobby chat
// message straight away (the game's "ready"/"host_loading", kick and ban notices). Their
// owner is you, so a player who enters one connects to your game like everyone else; after
// that the game never looks at Steam lobbies again (sleeping, the map and so on use the
// connected players).
//
// The game listens for every lobby's Steam events, not only its own lobby's. Left alone, it
// would take an extra lobby for its own when it's created or entered, and read its chat with
// the wrong lobby's message ids. The prefixes below skip the game's handlers for extra
// lobbies; other features check Handling to skip their own patches on the same events.
//
// All lobbies share one lobby size: each one's Steam limit leaves room only for the places
// the others haven't taken. An extra lobby that's no longer wanted stops taking new players
// and closes once nobody but you is in it.
[HarmonyPatch]
public static class CrossBranchLobby
{
    private const float PendingTimeout = 15f;
    private const float RetryAfter = 30f;
    private const float SyncEvery = 1f;
    private const float ReadyDelay = 2f, ReadySpacing = 5f;
    private const int MaxExtras = 2;

    // Lobby data copied from the main lobby. "version" is the one key that can differ.
    private static readonly string[] Keys = { "owner", "host_loading", "ready", "load_tutorial", "max_players", "num_slots" };

    private sealed class Extra
    {
        public ulong Id;
        public string Version = "";
        public bool Draining;           // no longer wanted: closes once empty
        public bool ReadyPending;
        public float LastReady = -999f;
    }

    private static MelonPreferences_Entry<bool>? _entry;
    private static readonly List<Extra> _extras = new();
    private static ulong _main;                 // the main lobby the extras belong to
    // Our CreateLobby calls. Each answer is matched to its call by the call handle, so which
    // lobby is ours never depends on timing or order. Steamworks.NET's dispatcher reads every
    // call result itself (manual dispatch) and frees it, so a CallResult registered on the call
    // is how we're sure to see it; reading it directly (Resolve) covers the case where Steam
    // broadcasts LobbyCreated_t before the dispatcher gets to the result. A call we stopped
    // waiting for (timeout, or you stopped hosting) is "abandoned": if Steam still answers,
    // the lobby is closed at once instead of reaching the game.
    private sealed class OurCall
    {
        public SteamAPICall_t Call;
        public string Version = "";
        public float Since;
        public bool Abandoned, Done, Failed, Polled;
        public ulong Id;
        public object? Watch;                   // the CallResult; kept alive until the call is forgotten
        public Action? Unwatch;                 // disposes it (Il2Cpp's CallResult isn't a .NET IDisposable)
    }
    private static readonly List<OurCall> _calls = new();
    private static OurCall? _current;           // the call we're waiting on, if any
    private const float ForgetCallsAfter = 300f;
    private const int LobbyCreatedId = 513;     // LobbyCreated_t.k_iCallback
    private static bool _warnedWatch;
    // Lobbies we've left or closed; any late callback for them is swallowed.
    private static readonly HashSet<ulong> _closed = new();
    private static float _retryAt, _nextSync;
    private static int _noMirror;

    /// <summary>True while one of the game's lobby handlers is running for an extra lobby.</summary>
    public static bool Handling { get; private set; }

    /// <summary>True during our own CreateLobby call: privacy and dedupe leave it alone.</summary>
    public static bool Creating { get; private set; }

    /// <summary>The cross-branch setting. Large lobbies turn it off.</summary>
    public static bool Enabled
    {
        get => _entry?.Value ?? false;
        set
        {
            if (_entry == null) return;
            if (value && LobbySize.LargeLobbies) return;   // refused: see SetEnabled
            _entry.Value = value;
            MelonPreferences.Save();
            _retryAt = 0f;
        }
    }

    /// <summary>Cross-branch play is on and not overridden by large lobbies.</summary>
    public static bool CrossActive => Enabled && !LobbySize.LargeLobbies;

    private static bool Pending => _current != null && UnityEngine.Time.realtimeSinceStartup - _current.Since < PendingTimeout;

    private static void Log(Func<string> s) { if (Utils.Log.Verbose) Utils.Log.Info($"[diag] [extra lobbies] {s()}"); }

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("CrossBranchLobby", false, "Cross-branch lobby",
            "Experimental. While you host, also open a hidden lobby for players on the other Steam branch (Il2Cpp default or Mono alternate), so both can join your game. Invite them with Invite (other branch) in Host settings. Off while ExperimentalLargeLobbies is on.");
        if (Enabled && LobbySize.LargeLobbies)
            MelonLogger.Warning("CrossBranchLobby is on, but ExperimentalLargeLobbies is too; cross-branch play stays off");
        else if (Enabled)
            MelonLogger.Warning($"CrossBranchLobby is on: players on the other Steam branch ({OtherVersion()}) can join through a second lobby. Experimental.");
    }

    /// <summary>Turns cross-branch play on or off. Returns a status line.</summary>
    public static string SetEnabled(bool on)
    {
        if (on && LobbySize.LargeLobbies) return "Turn off large lobbies first";
        Enabled = on;
        return on ? $"Players on {OtherVersion()} can join too" : "Only players on your branch can join";
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

    public static bool IsExtra(ulong id) => id != 0 && Find(id) != null;

    private static Extra? Find(ulong id)
    {
        foreach (var e in _extras) if (e.Id == id) return e;
        return null;
    }

    // --- What should be open ------------------------------------------------------------------

    // The versions the extra lobbies should carry right now, one entry per lobby.
    private static List<string> Wanted()
    {
        var want = new List<string>();
        if (LobbySize.LargeLobbies)
        {
            int past = LobbySize.MaxPlayers - LobbySize.SteamMax;
            int n = past <= 0 ? 0 : (past + LobbySize.SteamMax - 2) / (LobbySize.SteamMax - 1);   // 249 more each
            for (int i = 0; i < Math.Min(n, MaxExtras); i++) want.Add(UnityEngine.Application.version);
        }
        else if (Enabled)
        {
            want.Add(OtherVersion());
        }
        return want;
    }

    // --- Lifecycle --------------------------------------------------------------------------

    public static void Update()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (_current != null && !Pending)
        {
            AbandonPending();
            _retryAt = now + RetryAfter;
            MelonLogger.Warning("Extra lobby: Steam didn't answer in time; trying again in 30 s");
        }
        if (_calls.Count > 0) Resolve();

        try { EndFlip(now); }
        catch (Exception e) { Log(() => $"ending the Join Game window failed: {e.Message}"); }

        bool hosting = HostLobby.TryGet(out var main);
        if (_extras.Count > 0 && (!hosting || main.m_SteamID != _main))
        {
            LeaveAll(!hosting ? "you're not hosting" : "the main lobby changed");
            return;
        }
        if (!hosting)
        {
            AbandonPending();   // a late answer is closed, never mistaken for your next lobby
            return;
        }
        _main = main.m_SteamID;

        // Match open lobbies to what's wanted; the rest drain.
        var want = Wanted();
        var kept = new List<Extra>();
        foreach (var e in _extras)
        {
            int i = want.IndexOf(e.Version);
            if (i >= 0) { want.RemoveAt(i); kept.Add(e); if (e.Draining) { e.Draining = false; Log(() => $"lobby {e.Id} wanted again"); } }
            else if (!e.Draining) { e.Draining = true; Utils.Log.Info($"Extra lobby {e.Id} no longer needed: it takes no new players and closes once empty"); }
        }
        foreach (var e in _extras.ToArray())
            if (e.Draining && Others(new CSteamID(e.Id)) == 0) Leave(e, "no longer needed");

        if (want.Count > 0 && !Pending && now >= _retryAt && _extras.Count < MaxExtras) Create(main, want[0]);

        if (_extras.Count > 0 && now >= _nextSync)
        {
            _nextSync = now + SyncEvery;
            foreach (var e in _extras) Sync(main, e);
            ApplyLimits();
        }
    }

    private static void Create(CSteamID main, string version)
    {
        _main = main.m_SteamID;
        SteamAPICall_t call;
        Creating = true;
        try { call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeInvisible, LobbySize.SteamMax); }
        catch (Exception e)
        {
            MelonLogger.Warning($"Extra lobby: couldn't create it: {e.Message}");
            call = SteamAPICall_t.Invalid;
        }
        finally { Creating = false; }
        if (call == SteamAPICall_t.Invalid)
        {
            _retryAt = UnityEngine.Time.realtimeSinceStartup + RetryAfter;
            return;
        }
        var c = new OurCall { Call = call, Version = version, Since = UnityEngine.Time.realtimeSinceStartup };
        try
        {
            var watch = CallResult<LobbyCreated_t>.Create(
                (CallResult<LobbyCreated_t>.APIDispatchDelegate)((LobbyCreated_t r, bool ioFailure) => OnResult(c, r, ioFailure)));
            watch.Set(call);
            c.Watch = watch;
            c.Unwatch = () => watch.Dispose();
        }
        catch (Exception e)
        {
            if (!_warnedWatch) { _warnedWatch = true; MelonLogger.Warning($"Extra lobby: couldn't watch Steam's answer ({e.Message}); reading it directly instead"); }
        }
        _current = c;
        _calls.Add(c);
        Log(() => $"creating an extra lobby ({version}) for main lobby {_main}");
    }

    // Stop waiting for the current call. It stays in _calls, so a late answer is recognised and closed.
    private static void AbandonPending()
    {
        if (_current == null) return;
        _current.Abandoned = true;
        _current = null;
    }

    // Steam's answer to one of our calls, from its CallResult. The first real answer wins.
    private static void OnResult(OurCall c, LobbyCreated_t r, bool ioFailure)
    {
        if (c.Done) return;
        c.Done = true;
        c.Id = r.m_ulSteamIDLobby;
        c.Failed = ioFailure || r.m_eResult != EResult.k_EResultOK || c.Id == 0;
        Log(() => $"create answered: lobby {c.Id}{(c.Failed ? " (failed)" : "")}{(c.Abandoned ? " (abandoned)" : "")}");
    }

    private static void RemoveCall(OurCall c)
    {
        _calls.Remove(c);
        if (_current == c) _current = null;
        try { c.Unwatch?.Invoke(); } catch { }
        c.Watch = null;
        c.Unwatch = null;
    }

    // Reads Steam's answer directly for calls the CallResult hasn't answered yet (Steam may
    // broadcast LobbyCreated_t first), and forgets old calls.
    private static void Resolve()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        foreach (var c in _calls.ToArray())
        {
            if (now - c.Since > ForgetCallsAfter) { RemoveCall(c); continue; }
            if (c.Done || c.Polled) continue;
            try
            {
                if (!SteamUtils.IsAPICallCompleted(c.Call, out bool ioFailed)) continue;
                c.Polled = true;
                if (ioFailed)
                {
                    // Only a real failure counts; an unknown handle (the dispatcher already
                    // took the result) leaves the answer to the CallResult.
                    var why = SteamUtils.GetAPICallFailureReason(c.Call);
                    if (why == ESteamAPICallFailure.k_ESteamAPICallFailureNetworkFailure || why == ESteamAPICallFailure.k_ESteamAPICallFailureSteamGone)
                    { c.Done = true; c.Failed = true; }
                    continue;
                }
                ReadResult(c);
            }
            catch (Exception e) { c.Polled = true; Log(() => $"couldn't read a create result: {e.Message}"); }
        }
    }

    // LobbyCreated_t here is Pack = 8: { EResult m_eResult; ulong m_ulSteamIDLobby; }, 16 bytes
    // with the id at 8. Steam refuses a wrong size without reading, so 12/4 is tried after.
    private static void ReadResult(OurCall c)
    {
        IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
        try
        {
            foreach (var (size, offset) in new[] { (16, 8), (12, 4) })
            {
                if (!SteamUtils.GetAPICallResult(c.Call, buf, size, LobbyCreatedId, out bool failed)) continue;
                c.Done = true;
                if (failed) { c.Failed = true; return; }
                int result = System.Runtime.InteropServices.Marshal.ReadInt32(buf, 0);
                c.Id = (ulong)System.Runtime.InteropServices.Marshal.ReadInt64(buf, offset);
                c.Failed = result != (int)EResult.k_EResultOK || c.Id == 0;
                return;
            }
            // Already taken by the dispatcher: the CallResult has or will have it.
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    // The call a lobby id came from, if it's ours. Falls back to the current call when Steam's
    // answer couldn't be read, but only while the game isn't creating a lobby of its own.
    private static OurCall? CallFor(ulong id, bool enter)
    {
        Resolve();
        foreach (var c in _calls) if (c.Done && !c.Failed && c.Id == id) return c;
        var cur = _current;
        if (cur == null || cur.Done || LobbyDedupePatches.GameCreateSince >= 0f) return null;
        try
        {
            var svc = HostLobby.Service();
            if (svc != null && svc._lobbySteamID.m_SteamID == id) return null;
            if (enter && SteamMatchmaking.GetLobbyOwner(new CSteamID(id)).m_SteamID != SteamUser.GetSteamID().m_SteamID) return null;
        }
        catch { return null; }
        return cur;
    }

    private static void Claim(ulong id)
    {
        // Steam sends both LobbyCreated and LobbyEnter for each create: the second one finds
        // the lobby already handled.
        if (Find(id) != null || _closed.Contains(id)) return;
        var c = CallFor(id, enter: false);
        if (c == null) return;
        bool current = c == _current;
        RemoveCall(c);
        if (!current)
        {
            // A late answer to a call we gave up on: leave it straight away.
            if (_closed.Count > 64) _closed.Clear();
            _closed.Add(id);
            try { SteamMatchmaking.LeaveLobby(new CSteamID(id)); } catch { }
            Utils.Log.Info($"Extra lobby {id} arrived after its create was abandoned; closed it");
            return;
        }
        _current = null;
        var e = new Extra { Id = id, Version = c.Version };
        _extras.Add(e);
        _nextSync = 0f;
        try
        {
            if (HostLobby.TryGet(out var main)) Sync(main, e);
            ApplyLimits();
            Utils.Log.Info(e.Version == UnityEngine.Application.version
                ? $"Overflow lobby {id} open for players past the first {LobbySize.SteamMax}"
                : $"Cross-branch lobby {id} open for players on {e.Version}");
        }
        catch (Exception ex) { MelonLogger.Warning($"Extra lobby: couldn't set it up: {ex.Message}"); }
    }

    private static void Sync(CSteamID main, Extra e)
    {
        try
        {
            var lobby = new CSteamID(e.Id);
            foreach (var key in Keys)
            {
                string v = SteamMatchmaking.GetLobbyData(main, key) ?? "";
                if (SteamMatchmaking.GetLobbyData(lobby, key) != v) SteamMatchmaking.SetLobbyData(lobby, key, v);
            }
            if (SteamMatchmaking.GetLobbyData(lobby, "version") != e.Version) SteamMatchmaking.SetLobbyData(lobby, "version", e.Version);
        }
        catch (Exception ex) { Log(() => $"sync of {e.Id} failed: {ex.Message}"); }
    }

    private static void Leave(Extra e, string why)
    {
        _extras.Remove(e);
        if (_closed.Count > 64) _closed.Clear();
        _closed.Add(e.Id);
        try { SteamMatchmaking.LeaveLobby(new CSteamID(e.Id)); }
        catch (Exception ex) { MelonLogger.Warning($"Extra lobby: couldn't leave it: {ex.Message}"); }
        Utils.Log.Info($"Extra lobby {e.Id} closed ({why})");
        ApplyLimits();
        if (_extras.Count == 0)
            try { if (HostLobby.TryGet(out var main)) SteamMatchmaking.SetLobbyMemberLimit(main, LobbySize.SteamLimit(LobbySize.MaxPlayers)); }
            catch { }
    }

    private static void LeaveAll(string why)
    {
        AbandonPending();
        _enteredAt.Clear();
        foreach (var e in _extras.ToArray()) Leave(e, why);
        _main = 0;
    }

    // --- Host actions -----------------------------------------------------------------------

    /// <summary>Opens Steam's invite dialog for the cross-branch lobby. Returns a status line.</summary>
    public static string OpenInvite()
    {
        if (LobbySize.LargeLobbies) return "Other branches are off while large lobbies are on";
        if (!Enabled) return "Turn on Accept other branches first";
        if (!HostLobby.TryGet(out _)) return "Host a save first";
        Extra? e = null;
        foreach (var x in _extras) if (!x.Draining && x.Version != UnityEngine.Application.version) { e = x; break; }
        if (e == null) return "The other-branch lobby isn't open yet. Try again in a moment";
        try
        {
            SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(e.Id));
            return $"Invite friends on {OtherVersion()}";
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"Extra lobby: couldn't open the invite dialog: {ex.Message}");
            return "Couldn't open Steam's invite dialog";
        }
    }

    /// <summary>
    /// Large lobbies: once the main lobby is full, the game's invite button invites into an
    /// overflow lobby with room. Returns true when it handled the invite.
    /// </summary>
    public static bool RedirectInvite()
    {
        try
        {
            if (!LobbySize.LargeLobbies || _extras.Count == 0 || !HostLobby.TryGet(out var main)) return false;
            if (SteamMatchmaking.GetNumLobbyMembers(main) < SteamMatchmaking.GetLobbyMemberLimit(main)) return false;
            foreach (var e in _extras)
            {
                if (e.Draining || e.Version != UnityEngine.Application.version) continue;
                var lobby = new CSteamID(e.Id);
                if (SteamMatchmaking.GetNumLobbyMembers(lobby) >= SteamMatchmaking.GetLobbyMemberLimit(lobby)) continue;
                SteamFriends.ActivateGameOverlayInviteDialog(lobby);
                Utils.Log.Info($"Main lobby is full: inviting into overflow lobby {e.Id}");
                return true;
            }
            MelonLogger.Warning("Every lobby is full; nobody else can be invited");
            return true;
        }
        catch (Exception ex) { Log(() => $"invite redirect failed: {ex.Message}"); return false; }
    }

    // Members of a lobby other than you.
    private static int Others(CSteamID lobby)
    {
        try
        {
            ulong me = SteamUser.GetSteamID().m_SteamID;
            int n = SteamMatchmaking.GetNumLobbyMembers(lobby), others = 0;
            for (int i = 0; i < n; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID != me) others++;
            return others;
        }
        catch { return 0; }
    }

    /// <summary>Players in the extra lobbies, not counting you.</summary>
    public static int ExtraMembers()
    {
        int n = 0;
        foreach (var e in _extras) n += Others(new CSteamID(e.Id));
        return n;
    }

    /// <summary>The other branch in plain words, for the menu.</summary>
    public static string OtherBranchName() =>
        OtherVersion().EndsWith("Alternate", StringComparison.OrdinalIgnoreCase)
            ? "the alternate (Mono) Steam branch"
            : "the default Steam branch";

    public static string StatusLine()
    {
        if (LobbySize.LargeLobbies) return "Off while large lobbies are on";
        if (!Enabled) return "Off. Only players on your branch can join";
        if (!HostLobby.TryGet(out _)) return "Ready. It opens when you host a save";
        if (_extras.Count == 0) return "Opening...";
        if (_flipUntil > 0f)
            return $"Letting a friend in through Join Game: {Math.Max(0, _flipUntil - UnityEngine.Time.realtimeSinceStartup):0} s left";
        int n = ExtraMembers();
        return n == 0 ? "Open. Nobody from the other branch yet" : $"Open. {n} from the other branch";
    }

    public static string LargeStatusLine()
    {
        if (!LobbySize.LargeLobbies) return $"Off. Lobby size goes up to {LobbySize.NormalMax}";
        if (!HostLobby.TryGet(out var main)) return $"Extra lobbies open when you host with more than {LobbySize.SteamMax} places";
        // People, not Steam members: you're in every lobby, so count yourself once.
        int others = Math.Max(0, SteamMatchmaking.GetNumLobbyMembers(main) - 1) + ExtraMembers();
        int open = 0, closing = 0;
        foreach (var e in _extras) { if (e.Draining) closing++; else open++; }
        string extras = _extras.Count == 0
            ? (LobbySize.MaxPlayers > LobbySize.SteamMax ? "opening an extra lobby..." : "no extra lobby needed")
            : $"{open} extra {(open == 1 ? "lobby" : "lobbies")} open" + (closing > 0 ? $", {closing} closing" : "");
        return $"{others + 1} of {LobbySize.MaxPlayers} players, {extras}";
    }

    // --- Shared size budget -------------------------------------------------------------------
    // All lobbies share one lobby size. You're a member of each, so the people in your game are
    // you + the others in every lobby. Each lobby's Steam limit is what's left of the size after
    // the others in all the *other* lobbies, never more than Steam's 250 and never below who's
    // already in. A draining lobby takes nobody new. The "max_players" data the lobby screen
    // shows stays at the full size.

    /// <summary>Steam limit for the main lobby at lobby size <paramref name="size"/>.</summary>
    public static int MainLimitFor(int size) =>
        LobbySize.SteamLimit(_extras.Count == 0 ? size : size - ExtraMembers());

    /// <summary>Re-balances every lobby's Steam limit. Called on member changes, size changes and every sync.</summary>
    public static void ApplyLimits()
    {
        if (_extras.Count == 0 || !HostLobby.TryGet(out var main)) return;
        try
        {
            int size = LobbySize.MaxPlayers;
            var lobbies = new List<(CSteamID id, string name, bool draining)> { (main, "main", false) };
            foreach (var e in _extras) lobbies.Add((new CSteamID(e.Id), e.Id.ToString(), e.Draining));
            var others = new int[lobbies.Count];
            int total = 0;
            for (int i = 0; i < lobbies.Count; i++) { others[i] = Others(lobbies[i].id); total += others[i]; }
            for (int i = 0; i < lobbies.Count; i++)
            {
                int members = others[i] + 1;
                int limit = lobbies[i].draining ? members : size - (total - others[i]);
                limit = Math.Max(members, LobbySize.SteamLimit(limit));
                SetLimit(lobbies[i].id, limit, lobbies[i].name);
            }
        }
        catch (Exception e) { Log(() => $"limit update failed: {e.Message}"); }
    }

    private static void SetLimit(CSteamID lobby, int limit, string which)
    {
        if (SteamMatchmaking.GetLobbyMemberLimit(lobby) == limit) return;
        bool ok = SteamMatchmaking.SetLobbyMemberLimit(lobby, limit);
        Log(() => $"{which} lobby limit -> {limit} ({(ok ? "accepted" : "refused")})");
    }

    // Someone joined or left the main lobby: the others' shares change, and a quick leave may
    // be a friend on the other branch who used Join Game.
    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static void Main_PlayerEnterOrLeave_Postfix(LobbyChatUpdate_t result)
    {
        if (Handling) return;
        ApplyLimits();
        try { WatchJoinGame(result); }
        catch (Exception e) { Log(() => $"join game fallback failed: {e.Message}"); }
    }

    // --- Join Game fallback (cross-branch only) -----------------------------------------------
    // Steam's Join Game always leads to the main lobby; invisible lobbies can't be its target.
    // A friend on the other branch enters it, their game sees the wrong version and leaves at
    // once. Nobody else leaves that fast: a player on your branch stays in the lobby for as
    // long as they play. So a quick leave switches the main lobby's version to the other one
    // for a short window, and the friend's second Join Game works. A quick leave during the
    // window (someone on your branch) switches it straight back.

    private const float QuickLeave = 5f;
    private const float FlipWindow = 15f;
    private static readonly Dictionary<ulong, float> _enteredAt = new();
    private static float _flipUntil = -1f;

    private static void WatchJoinGame(LobbyChatUpdate_t result)
    {
        if (!CrossActive || !HostLobby.TryGet(out var main) || result.m_ulSteamIDLobby != main.m_SteamID) return;
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

    // --- Keeping the game's lobby handlers out of the extra lobbies ---------------------------

    // Is this create/enter callback about one of our extra lobbies? A lobby we already have or
    // closed is; a new one is if one of our create calls returned it (see CallFor). Steam may
    // report the enter before the create.
    private static bool IsOurs(ulong id, bool enter)
    {
        if (id == 0) return false;
        if (IsExtra(id) || _closed.Contains(id)) return true;
        return CallFor(id, enter) != null;
    }

    // A failed create carries no lobby id: find which of our calls failed, if any.
    private static OurCall? FailedCall()
    {
        Resolve();
        foreach (var c in _calls) if (c.Done && c.Failed) return c;
        var cur = _current;
        if (cur != null && !cur.Done && LobbyDedupePatches.GameCreateSince < 0f) return cur;
        return null;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static bool OnLobbyCreated_Prefix(LobbyCreated_t result)
    {
        Handling = false;
        ulong id = result.m_ulSteamIDLobby;
        bool failed = result.m_eResult != EResult.k_EResultOK || id == 0;
        var failedCall = failed ? FailedCall() : null;
        bool ours = failed ? failedCall != null : IsOurs(id, enter: false);
        if (!ours) return true;
        Handling = true;
        if (failed)
        {
            bool current = failedCall == _current;
            RemoveCall(failedCall!);
            if (!current) { Log(() => "an abandoned create failed"); return false; }
            _retryAt = UnityEngine.Time.realtimeSinceStartup + RetryAfter;
            MelonLogger.Warning($"Extra lobby: Steam couldn't create it ({result.m_eResult}); trying again in 30 s");
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
        if (!IsOurs(result.m_ulSteamIDLobby, enter: true)) return true;
        Handling = true;
        Claim(result.m_ulSteamIDLobby);
        return false;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static bool PlayerEnterOrLeave_Prefix(LobbyChatUpdate_t result)
    {
        Handling = false;
        var e = Find(result.m_ulSteamIDLobby);
        if (e == null) return true;
        Handling = true;
        try
        {
            ulong who = result.m_ulSteamIDUserChanged;
            bool entered = (result.m_rgfChatMemberStateChange & 0x1u) != 0;
            Utils.Log.Info($"Extra lobby {e.Id}: {DepartedPlayers.NameOf(who)} ({who}) {(entered ? "joined" : "left")}");
            ApplyLimits();
            if (entered) MaybeSendReady(e);
        }
        catch (Exception ex) { MelonLogger.Warning($"Extra lobby: couldn't handle a member change: {ex.Message}"); }
        return false;
    }

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static bool OnLobbyChatMessage_Prefix(LobbyChatMsg_t result)
    {
        Handling = IsExtra(result.m_ulSteamIDLobby);
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

    // --- Late joins into an extra lobby --------------------------------------------------------

    // Same as LateJoinFix, per extra lobby: past 4 members a vanilla client fails to read the
    // member list as it enters and never reaches the "ready" check, so it needs the chat
    // message. With 4 or fewer it joins from the lobby data by itself.
    private static void MaybeSendReady(Extra e)
    {
        if (!LateJoinFix.Enabled || e.ReadyPending) return;
        var load = Singleton<LoadManager>.Instance;
        if (load == null || !load.IsGameLoaded) return;
        e.ReadyPending = true;
        MelonCoroutines.Start(SendReadySoon(e));
    }

    private static IEnumerator SendReadySoon(Extra e)
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        float sendAt = Math.Max(now + ReadyDelay, e.LastReady + ReadySpacing);
        while (UnityEngine.Time.realtimeSinceStartup < sendAt) yield return null;
        e.ReadyPending = false;
        try
        {
            if (!_extras.Contains(e)) yield break;
            var lobby = new CSteamID(e.Id);
            // Like LateJoinFix's svc.PlayerCount: leave out players the mod hides after a kick or
            // ban, or a stale member could push the count past 4 and make a client load twice.
            int members = 0, n = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < n; i++)
                if (!KickNotice.IsHidden(SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID)) members++;
            if (members <= Utils.Constants.VANILLA_LIMIT) yield break;
            e.LastReady = UnityEngine.Time.realtimeSinceStartup;
            SteamMatchmaking.SetLobbyData(lobby, "ready", "true");
            SendText(lobby, "ready");
            Utils.Log.Info($"Extra lobby {e.Id}: player joined mid-game; sent 'ready' so they can connect");
        }
        catch (Exception ex) { MelonLogger.Warning($"Extra lobby: 'ready' re-send failed: {ex.Message}"); }
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

    // Every chat message to the main lobby goes to the extra lobbies too: the game's "ready"
    // and "host_loading", and the mod's kick and ban notices. (Positional names: the parameter
    // names differ between the two builds' Steamworks.)
    [HarmonyPostfix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.SendLobbyChatMsg))]
    private static void SendLobbyChatMsg_Postfix(CSteamID __0, ByteArray __1, int __2)
    {
        if (_extras.Count == 0 || _noMirror > 0 || __0.m_SteamID != _main || __1 == null) return;
        _noMirror++;
        try
        {
            foreach (var e in _extras)
                try { SteamMatchmaking.SendLobbyChatMsg(new CSteamID(e.Id), __1, __2); }
                catch (Exception ex) { Log(() => $"chat mirror to {e.Id} failed: {ex.Message}"); }
        }
        finally { _noMirror--; }
    }

    // Leaving the main lobby leaves the extra ones too.
    [HarmonyPrefix, HarmonyPatch(typeof(SteamMatchmaking), nameof(SteamMatchmaking.LeaveLobby))]
    private static void LeaveLobby_Prefix(CSteamID __0)
    {
        if (_extras.Count == 0) return;
        var e = Find(__0.m_SteamID);
        if (e != null) { _extras.Remove(e); return; }
        if (__0.m_SteamID == _main) LeaveAll("you left your lobby");
    }
}
