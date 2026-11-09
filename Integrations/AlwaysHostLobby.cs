#if !MONO
using System;
using System.Collections;
using HarmonyLib;
using Il2Cpp;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppSteamworks;
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
                _waiting = true; _created = false; _failed = false;
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
    private static void OnLobbyCreated_Postfix(LobbyCreated_t result)
    {
        if (!_waiting) return;
        if (result.m_eResult == EResult.k_EResultOK) _created = true;
        else
        {
            _failed = true;
            MelonLogger.Warning($"Always host lobby: Steam couldn't create the lobby ({result.m_eResult})");
        }
    }

    private static IEnumerator WaitThenLoad(LoadManager load, SaveInfo info, bool allowLoadStacking, bool allowSaveBackup)
    {
        float deadline = UnityEngine.Time.realtimeSinceStartup + TimeoutSeconds;
        while (true)
        {
            bool inLobby = false;
            try
            {
                var lobby = Singleton<Lobby>.Instance;
                if (lobby != null)
                {
                    var svc = lobby._lobbyService?.TryCast<SteamLobbyService>();
                    inLobby = lobby.IsInLobby || (svc != null && svc.IsInLobby);
                }
            }
            catch { }
            if (inLobby || _created) { Utils.Log.Info("Always host lobby: lobby ready; loading as host"); break; }
            if (_failed) { Utils.Log.Info("Always host lobby: loading solo"); break; }
            if (UnityEngine.Time.realtimeSinceStartup > deadline)
            {
                MelonLogger.Warning($"Always host lobby: no answer from Steam after {TimeoutSeconds:0} s; loading solo");
                break;
            }
            yield return null;
        }

        _waiting = false;
        _resuming = true;
        try { load.StartGame(info, allowLoadStacking, allowSaveBackup); }
        catch (Exception e) { MelonLogger.Error($"Always host lobby: StartGame failed: {e}"); }
        finally { _resuming = false; }
    }
}

#endif
