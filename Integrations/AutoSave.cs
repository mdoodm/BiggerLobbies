#if !MONO
using System;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using UnityEngine.SceneManagement;

namespace BiggerLobbies.Integrations;

// The game only saves when everyone sleeps, or when a client leaves cleanly and
// asks the host to save it. A client crash (no save request) or a host crash loses
// everything since the last sleep.
//
// 1. Full autosave: as host, run the game's own full save periodically so the world
//    and all connected players are saved together.
// 2. Player saves: as host, save each connected client's player data more often.
//    The host already has every player's current state (the vanilla "save me on quit"
//    request carries no data; the host writes the file from its own copy), so this
//    works without clients installing anything.
public static class AutoSave
{
    private static MelonPreferences_Category? _cat;
    private static MelonPreferences_Entry<bool>? _enabled;
    private static MelonPreferences_Entry<int>? _minutes;
    private static MelonPreferences_Entry<bool>? _playerSaveEnabled;
    private static MelonPreferences_Entry<int>? _playerSaveSeconds;
    private static float _nextCheck;
    private static float _nextPlayerSave;
    private static int _lastPlayerCount = -1;

    public static void Init()
    {
        _cat = MelonPreferences.CreateCategory("BiggerLobbies");
        _enabled = _cat.CreateEntry("AutoSaveEnabled", true, "Host autosave enabled",
            "As host, periodically run the game's normal save so a crash doesn't lose progress since the last sleep.");
        _minutes = _cat.CreateEntry("AutoSaveMinutes", 5, "Autosave interval (minutes)");
        _playerSaveEnabled = _cat.CreateEntry("PlayerSaveEnabled", true, "Client player saves enabled",
            "As host, save each connected client's money, inventory and position more often than the full autosave.");
        _playerSaveSeconds = _cat.CreateEntry("PlayerSaveSeconds", 30, "Client player save interval (seconds)");
        GrowSyncFix.Init(_cat);
        LateJoinFix.Init(_cat);
    }

    public static void Update()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextCheck) return;
        _nextCheck = now + 5f; // check every 5 s

        try
        {
            if (SceneManager.GetActiveScene().name != "Main") return;
            if (!InstanceFinder.IsServer) return; // host / single player only

            var save = PersistentSingleton<SaveManager>.Instance;
            if (save == null || save.IsSaving) return;

            var sleep = NetworkSingleton<SleepController>.Instance;
            if (sleep != null && sleep.IsSleepInProgress) return;

            if (ClientLeft())
            {
                // A client dropped (crash or quit). Save everything now so the host can
                // restart the session without losing progress.
                MelonLogger.Msg("A player left; saving now");
                save.Save();
                return;
            }
            if (TryFullSave(save)) return;
            TrySavePlayers(now);
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Autosave failed: {e}");
            _nextCheck = now + 60f;
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> _remoteCodes = new();

    // True once when a connected client is no longer in the player list.
    private static bool ClientLeft()
    {
        if (_enabled == null || !_enabled.Value) return false;
        var players = Player.PlayerList;
        if (players == null) return false;

        var current = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p.IsLocalPlayer || string.IsNullOrEmpty(p.PlayerCode)) continue;
            current.Add(p.PlayerCode);
        }

        bool left = false;
        foreach (var code in _remoteCodes)
            if (!current.Contains(code)) { left = true; break; }

        _remoteCodes.Clear();
        _remoteCodes.UnionWith(current);
        return left;
    }

    private static bool TryFullSave(SaveManager save)
    {
        if (_enabled == null || !_enabled.Value) return false;
        float interval = Math.Max(1, _minutes!.Value) * 60f;
        if (save.SecondsSinceLastSave < interval) return false; // the game resets this on every save

        MelonLogger.Msg($"Autosaving (last save {save.SecondsSinceLastSave / 60f:0.0} min ago)");
        save.Save();
        return true;
    }

    private static void TrySavePlayers(float now)
    {
        if (_playerSaveEnabled == null || !_playerSaveEnabled.Value) return;
        if (now < _nextPlayerSave) return;
        _nextPlayerSave = now + Math.Max(10, _playerSaveSeconds!.Value);

        var manager = Singleton<PlayerManager>.Instance;
        var players = Player.PlayerList;
        if (manager == null || players == null) return;

        int saved = 0;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p.IsLocalPlayer || string.IsNullOrEmpty(p.PlayerCode)) continue; // host is covered by the full save
            try
            {
                manager.SavePlayer(p);
                saved++;
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Saving player {p.PlayerCode} failed: {e}");
            }
        }

        if (saved != _lastPlayerCount)
        {
            if (saved > 0) MelonLogger.Msg($"Saving {saved} connected player(s) every {Math.Max(10, _playerSaveSeconds!.Value)} s");
            _lastPlayerCount = saved;
        }
    }
}

#endif
