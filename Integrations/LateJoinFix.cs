#if !MONO
using System;
using System.Collections;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Persistence;
using Il2CppSteamworks;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Let players join a game that has already started, without them installing the mod.
//
// A vanilla client entering the Steam lobby mid-game reads the lobby data "ready"=true
// and connects, but first it fills a 4-slot member array (SteamLobbyService._players).
// With 5+ members that throws, so the client never connects. The other way in is the
// "ready" lobby chat message, which the host sends once when it finishes loading;
// late joiners never see it. Fix: as host, re-send "ready" when someone enters the
// lobby while the game is loaded. Clients already in game ignore it (they check
// LoadManager.IsGameLoaded first). Re-sends are spaced out because a client that is
// still loading would start loading again if it got "ready" twice.
public static class LateJoinFix
{
    private static MelonPreferences_Entry<bool>? _enabled;
    private static bool _pending;
    private static float _lastSend = -999f;
    private const float FirstDelay = 2f;   // let the newcomer finish entering the lobby
    private const float MinSpacing = 5f;   // never send "ready" more often than this

    public static void Init(MelonPreferences_Category cat)
    {
        _enabled = cat.CreateEntry("LateJoinEnabled", true, "Late join enabled",
            "As host, let players join a game that has already started (re-sends the lobby 'ready' message when someone joins).");
    }

    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.PlayerEnterOrLeave))]
    private static class PlayerEnterOrLeavePatch
    {
        private static void Postfix(SteamLobbyService __instance, LobbyChatUpdate_t result)
        {
            try
            {
                if (_enabled == null || !_enabled.Value || !__instance.IsHost) return;
                if ((result.m_rgfChatMemberStateChange & 0x1u) == 0) return; // 1 = member entered
                var load = Singleton<LoadManager>.Instance;
                if (load == null || !load.IsGameLoaded) return;              // only mid-game
                if (_pending) return;                                        // one send covers a burst of joins
                _pending = true;
                MelonCoroutines.Start(SendReadySoon(__instance));
            }
            catch (Exception e) { MelonLogger.Error($"Late join check failed: {e}"); }
        }
    }

    private static IEnumerator SendReadySoon(SteamLobbyService svc)
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        float sendAt = Math.Max(now + FirstDelay, _lastSend + MinSpacing);
        while (UnityEngine.Time.realtimeSinceStartup < sendAt) yield return null;
        _pending = false;

        try
        {
            if (!svc.IsHost || !svc.IsInLobby) yield break;
            _lastSend = UnityEngine.Time.realtimeSinceStartup;
            svc.SetLobbyData("ready", "true");
            svc.SendMessage("ready");
            MelonLogger.Msg("Player joined mid-game; re-sent lobby 'ready' so they can connect");
        }
        catch (Exception e) { MelonLogger.Error($"Late join 'ready' re-send failed: {e}"); }
    }
}

#endif
