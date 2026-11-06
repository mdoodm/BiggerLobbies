#if !MONO
using System;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using UnityEngine.SceneManagement;

namespace BiggerLobbies.Integrations;

// The game only saves when everyone sleeps, or when a client leaves cleanly and
// asks the host to save it. A client crash (no save request) or a host crash loses
// everything since the last sleep. As host, trigger the game's own full save
// periodically so the world and all connected players are saved together.
public static class AutoSave
{
    private static MelonPreferences_Category? _cat;
    private static MelonPreferences_Entry<bool>? _enabled;
    private static MelonPreferences_Entry<int>? _minutes;
    private static float _nextCheck;

    public static void Init()
    {
        _cat = MelonPreferences.CreateCategory("BiggerLobbies");
        _enabled = _cat.CreateEntry("AutoSaveEnabled", true, "Host autosave enabled",
            "As host, periodically run the game's normal save so a crash doesn't lose progress since the last sleep.");
        _minutes = _cat.CreateEntry("AutoSaveMinutes", 5, "Autosave interval (minutes)");
        GrowSyncFix.Init(_cat);
    }

    public static void Update()
    {
        if (_enabled == null || !_enabled.Value) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextCheck) return;
        _nextCheck = now + 10f; // check every 10 s

        try
        {
            if (SceneManager.GetActiveScene().name != "Main") return;
            if (!InstanceFinder.IsServer) return; // host / single player only

            var save = PersistentSingleton<SaveManager>.Instance;
            if (save == null || save.IsSaving) return;

            var sleep = NetworkSingleton<SleepController>.Instance;
            if (sleep != null && sleep.IsSleepInProgress) return;

            float interval = Math.Max(1, _minutes!.Value) * 60f;
            if (save.SecondsSinceLastSave < interval) return; // the game resets this on every save

            MelonLogger.Msg($"Autosaving (last save {save.SecondsSinceLastSave / 60f:0.0} min ago)");
            save.Save();
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Autosave failed: {e}");
            _nextCheck = now + 60f;
        }
    }
}

#endif
