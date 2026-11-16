using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// The game picks online or offline once, as a save loads: if the host is in a Steam
// lobby it starts the multiplayer server, otherwise it runs offline and nobody can
// join or be invited until the host goes back to the menu. Players normally get a
// lobby by pressing Invite in the main menu first.
//
// This does that step automatically: when a save is loaded without a lobby, create
// one with the game's own CreateLobby, wait until Steam confirms it, then let the
// load continue. Falls back to a normal solo load if Steam isn't running, the user
// isn't signed in to Steam, lobby creation fails, or Steam doesn't answer in time.
[HarmonyPatch]
public static class AlwaysHostLobby
{
    private static MelonPreferences_Entry<bool>? _enabled;
    private const float TimeoutSeconds = 10f;

    private static bool _waiting;       // a load is held while the lobby is created
    private static bool _resuming;      // our own call back into StartGame
    private static bool _created;       // Steam confirmed the lobby
    private static bool _failed;        // Steam reported a failure
    private static float _abandonedUntil; // we gave up waiting and loaded solo; a lobby that shows up before this time must be left
    private static ulong _abandonedId;  // the lobby we created and gave up on; only this one is left
    private static bool _expectLate;    // timed out before Steam answered; the next created lobby is ours
    private static bool Abandoned => UnityEngine.Time.realtimeSinceStartup < _abandonedUntil;

    public static void Init(MelonPreferences_Category cat)
    {
        _enabled = cat.CreateEntry("AlwaysHostLobby", true, "Always host a lobby",
            "Create a Steam lobby automatically when loading a save, so friends can be invited or use Steam's Join Game at any time.");
    }

    private static string? WhyNotOnline()
    {
        try
        {
            if (!SteamManager.Initialized) return "Steam isn't initialised";
            if (!SteamAPI.IsSteamRunning()) return "Steam isn't running";
            if (!SteamUser.BLoggedOn()) return "not signed in to Steam (offline)";
        }
        catch (Exception e) { return $"Steam check failed ({e.Message})"; }
        return null;
    }

    [HarmonyPatch(typeof(LoadManager), nameof(LoadManager.StartGame))]
    private static class StartGamePatch
    {
        private static bool Prefix(LoadManager __instance, SaveInfo info, bool allowLoadStacking, bool allowSaveBackup)
        {
            if (_resuming) return true;
            if (_waiting) return false; // a second click while we're waiting
            try
            {
                if (_enabled == null || !_enabled.Value) return true;
                var lobby = Singleton<Lobby>.Instance;
                if (lobby == null || lobby.IsInLobby) return true; // already hosting (or joining): vanilla

                string? why = WhyNotOnline();
                if (why != null)
                {
                    Utils.Log.Info($"Always host lobby: {why}; loading solo");
                    return true;
                }

                Utils.Log.Info("Always host lobby: creating a Steam lobby before loading");
                _waiting = true; _created = false; _failed = false; _abandonedUntil = 0f; _abandonedId = 0; _expectLate = false;
                lobby.CreateLobby();
                MelonCoroutines.Start(WaitThenLoad(__instance, info, allowLoadStacking, allowSaveBackup));
                return false;
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Always host lobby failed, loading solo: {e}");
                _waiting = false;
                return true;
            }
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void OnLobbyCreated_Postfix(SteamLobbyService __instance, LobbyCreated_t result)
    {
        if (CrossBranchLobby.Handling) return;
        if (!_waiting)
        {
            if (Abandoned && _expectLate && result.m_eResult == EResult.k_EResultOK)
            {
                _expectLate = false;
                _abandonedId = result.m_ulSteamIDLobby;
                LeaveAbandonedLobby(__instance);
            }
            return;
        }
        if (result.m_eResult == EResult.k_EResultOK) { _created = true; _abandonedId = result.m_ulSteamIDLobby; }
        else
        {
            _failed = true;
            MelonLogger.Warning($"Always host lobby: Steam couldn't create the lobby ({result.m_eResult})");
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static void OnLobbyEntered_Postfix(SteamLobbyService __instance, LobbyEnter_t result)
    {
        // Only our own abandoned lobby: a friend's lobby joined via invite has a different id.
        if (CrossBranchLobby.Handling) return;
        if (!_waiting && Abandoned && _abandonedId != 0 && result.m_ulSteamIDLobby == _abandonedId) LeaveAbandonedLobby(__instance);
    }

    // Residual edge: if the user presses Invite (creating their own lobby) after the timeout but
    // before our late create arrives, within 30 s, that lobby could be mistaken for ours.
    // The game is already running offline, so a lobby that Steam finished creating after
    // the timeout would sit open (and joinable) with nobody hosting it.
    /// <summary>The user explicitly wants a lobby now: stop treating a late-created lobby as abandoned.</summary>
    internal static void CancelAbandon() { _expectLate = false; _abandonedUntil = 0f; _abandonedId = 0; }

    private static void LeaveAbandonedLobby(SteamLobbyService svc)
    {
        try
        {
            // Leave only our own lobby; the service may currently be in a friend's lobby.
            if (_abandonedId == 0) return;
            SteamMatchmaking.LeaveLobby(new CSteamID(_abandonedId));
            if (svc._lobbySteamID.m_SteamID == _abandonedId) svc.LeaveLobby();
            Utils.Log.Info("Always host lobby: left a lobby that was created after the timeout");
        }
        catch (Exception e) { MelonLogger.Warning($"Always host lobby: couldn't leave the late lobby: {e.Message}"); }
    }

    private static IEnumerator WaitThenLoad(LoadManager load, SaveInfo info, bool allowLoadStacking, bool allowSaveBackup)
    {
        float deadline = UnityEngine.Time.realtimeSinceStartup + TimeoutSeconds;
        bool timedOut = false;
        while (true)
        {
            bool inLobby = false;
            try
            {
                var lobby = Singleton<Lobby>.Instance;
                if (lobby != null)
                {
                    var svc = lobby._lobbyService.As<SteamLobbyService>();
                    inLobby = lobby.IsInLobby || (svc != null && svc.IsInLobby);
                }
            }
            catch { }
            // Wait for the lobby to be entered (IsInLobby), not just created: the game starts
            // the multiplayer server from IsInLobby when the save loads.
            if (inLobby) { Utils.Log.Info("Always host lobby: lobby ready; loading as host"); break; }
            if (_failed) { Utils.Log.Info("Always host lobby: loading solo"); break; }
            if (UnityEngine.Time.realtimeSinceStartup > deadline)
            {
                MelonLogger.Warning($"Always host lobby: no answer from Steam after {TimeoutSeconds:0} s; loading solo");
                timedOut = true;
                break;
            }
            yield return null;
        }

        _waiting = false;
        if (timedOut)
        {
            _abandonedUntil = UnityEngine.Time.realtimeSinceStartup + 30f;
            if (_created) // created but never entered: drop it now (keep the id in case it is entered later)
            {
                var svc = HostLobby.Service();
                if (svc != null) LeaveAbandonedLobby(svc);
            }
            else _expectLate = true; // Steam may still answer; that lobby is ours
        }
        _resuming = true;
        try { load.StartGame(info, allowLoadStacking, allowSaveBackup); }
        catch (Exception e) { MelonLogger.Error($"Always host lobby: StartGame failed: {e}"); }
        finally { _resuming = false; }
    }
}
