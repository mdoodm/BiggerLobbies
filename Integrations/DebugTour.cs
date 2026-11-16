using System;
using System.Collections;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace BiggerLobbies.Integrations;

// Testing only (DebugTour): drives the game without keyboard or mouse, for checking the menus
// on a machine where input can't be scripted. On the main menu it takes a screenshot, opens Host
// settings and captures every tab, then loads your last save. In the save it waits for hosting
// to settle, then captures the pause menu's lobby screen and every Host settings tab again.
// Screenshots are of the game only (Unity's own capture), in UserData/BiggerLobbies/Tour.
// Each part runs once per launch. Turn it off afterwards.
public static class DebugTour
{
    private static MelonPreferences_Entry<bool>? _entry;
    public static bool On => _entry?.Value ?? false;
    private static bool _menuDone, _gameDone;
    private static string Dir => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BiggerLobbies", "Tour");

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("DebugTour", false, "Debug: automatic menu tour",
            "Testing only. On the main menu, screenshots Host settings and loads your last save; in the save, screenshots the lobby and every Host settings tab. Saves to UserData/BiggerLobbies/Tour. Leave off.");
        if (On) MelonLogger.Warning($"DebugTour is on: the game will load your last save by itself and take screenshots into {Dir}");
    }

    public static void OnSceneLoaded(string scene)
    {
        if (!On) return;
        if (scene == "Menu" && !_menuDone) { _menuDone = true; MelonCoroutines.Start(MenuTour()); }
        else if (scene == "Main" && !_gameDone) { _gameDone = true; MelonCoroutines.Start(GameTour()); }
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static void Shot(string name)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Utils.Log.Info($"[tour] screenshot {name}");
        }
        catch (Exception e) { MelonLogger.Warning($"[tour] screenshot {name} failed: {e.Message}"); }
    }

    private static IEnumerator Tabs(bool inGame, string prefix)
    {
        if (!HostSettingsMenu.TourOpen(inGame)) { MelonLogger.Warning("[tour] Host settings isn't ready"); yield break; }
        yield return Wait(1.5f);
        for (int i = 0; i < HostSettingsMenu.TourTabCount; i++)
        {
            HostSettingsMenu.TourTab(inGame, i);
            yield return Wait(2f);   // avatars, icons and the layout settle
            string name = $"{prefix}-{i + 1}-{HostSettingsMenu.TourTabName(i).ToLowerInvariant()}";
            Shot(name);
            yield return Wait(0.5f);
            if (HostSettingsMenu.TourScrollBottom(inGame))
            {
                yield return Wait(1f);
                Shot(name + "-bottom");
                yield return Wait(0.5f);
            }
        }
        HostSettingsMenu.TourClose(inGame);
    }

    private static IEnumerator MenuTour()
    {
        yield return Wait(8f);
        Shot("menu-0-main");
        yield return Wait(0.5f);
        yield return Tabs(false, "menu");
        yield return Wait(1f);

        var last = LoadManager.LastPlayedGame;
        var lm = Singleton<LoadManager>.Instance;
        if (last == null || lm == null) { MelonLogger.Warning("[tour] no save to load; stopping"); yield break; }
        Utils.Log.Info($"[tour] loading the last save: {last.SavePath}");
        try { lm.StartGame(last, false, true); }
        catch (Exception e) { MelonLogger.Warning($"[tour] couldn't load the save: {e.Message}"); }
    }

    private static IEnumerator GameTour()
    {
        // Let the save finish loading and hosting settle (extra lobbies, avatars, NPCs).
        float give = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < give)
        {
            var lm = Singleton<LoadManager>.Instance;
            if (lm != null && lm.IsGameLoaded && !lm.IsLoading) break;
            yield return null;
        }
        yield return Wait(20f);
        Shot("game-0-world");
        var pm = Singleton<PauseMenu>.Instance;
        if (pm != null && !pm.IsPaused) pm.Pause();
        yield return Wait(2f);
        Shot("game-0-pause-lobby");
        yield return Tabs(true, "game");
        Utils.Log.Info($"[tour] done; screenshots are in {Dir}");
    }
}
