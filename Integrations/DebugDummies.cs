#if !MONO
using System;
using System.Collections.Generic;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BiggerLobbies.Integrations;

// Testing only. Spawns copies of the local player's character body around them so a host
// can see what a crowd costs in frame rate. The dummies are plain visual copies: they aren't
// networked, aren't in Player.PlayerList and aren't saved, so they can't reach save files,
// autosave or other players. They don't test network traffic, joining or saving.
//
// While the setting is above 0, the frame rate is logged every 10 seconds with the number
// of dummies out. Set it back to 0 (and restart) to remove them.
public static class DebugDummies
{
    private static MelonPreferences_Entry<int>? _count;
    private static readonly List<GameObject> _dummies = new();
    private static readonly List<float> _phase = new();
    private static Vector3 _centre;
    private static float _spawnAfter = -1f;

    private static float _fpsWindowStart, _minFps = float.MaxValue;
    private static int _frames;

    public static int Count => Math.Clamp(_count?.Value ?? 0, 0, LobbySize.Max);

    public static void Init(MelonPreferences_Category cat)
    {
        _count = cat.CreateEntry("DebugDummyPlayers", 0, "Debug: dummy characters",
            "Testing only. After a save loads, spawns this many copies of your character around you and logs the frame rate every 10 s. They aren't real players, aren't networked and aren't saved. Leave at 0.");
        if (Count > 0)
        {
            MelonLogger.Warning($"DebugDummyPlayers is {Count}: {Count} dummy characters will spawn around you after a save loads. Set it back to 0 to play normally.");
        }
    }

    public static void OnSceneLoaded(string scene)
    {
        Despawn();
        _spawnAfter = scene == "Main" && Count > 0 ? Time.realtimeSinceStartup + 10f : -1f;
    }

    public static void Update()
    {
        if (Count <= 0) return;
        try
        {
            if (_spawnAfter > 0f && Time.realtimeSinceStartup >= _spawnAfter)
            {
                _spawnAfter = -1f;
                Spawn();
                ResetFps();
            }

            Walk();
            TrackFps();
        }
        catch (Exception e) { MelonLogger.Error($"[dummies] {e}"); _spawnAfter = -1f; }
    }

    private static void Spawn()
    {
        Despawn();
        var player = Player.Local;
        var source = player?.Avatar?.gameObject;
        if (player == null || source == null) { MelonLogger.Warning("[dummies] no local player yet; not spawning"); return; }

        _centre = player.transform.position;
        int n = Count;
        for (int i = 0; i < n; i++)
        {
            var go = UnityEngine.Object.Instantiate(source);
            go.name = $"BiggerLobbies_Dummy_{i + 1}";
            MakeVisible(go);
            _dummies.Add(go);
            _phase.Add(i);
        }
        Place(0f);
        Utils.Log.Info($"[dummies] spawned {_dummies.Count} dummy characters around you");
    }

    // The local player's own body is hidden from the first-person camera (layer and
    // shadows-only renderers), so copies would be invisible. Undo that on the copies.
    private static void MakeVisible(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        go.SetActive(true);
    }

    // Rings around where the player stood: 8 on the first ring, 6 more per ring after.
    private static void Place(float t)
    {
        int idx = 0, ring = 0;
        while (idx < _dummies.Count)
        {
            int perRing = 8 + ring * 6;
            float radius = 3f + ring * 1.6f;
            float dir = ring % 2 == 0 ? 1f : -1f;
            for (int k = 0; k < perRing && idx < _dummies.Count; k++, idx++)
            {
                var go = _dummies[idx];
                if (go == null) continue;
                float a = (k / (float)perRing) * Mathf.PI * 2f + dir * t * 0.25f / (1f + ring * 0.3f);
                var pos = _centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * dir;
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent, Vector3.up));
            }
            ring++;
        }
    }

    private static void Walk()
    {
        if (_dummies.Count == 0) return;
        Place(Time.time);
    }

    public static void Despawn()
    {
        foreach (var go in _dummies)
            if (go != null) UnityEngine.Object.Destroy(go);
        if (_dummies.Count > 0) Utils.Log.Info($"[dummies] removed {_dummies.Count} dummy characters");
        _dummies.Clear();
        _phase.Clear();
    }

    private static void ResetFps()
    {
        _fpsWindowStart = Time.realtimeSinceStartup;
        _frames = 0;
        _minFps = float.MaxValue;
    }

    private static void TrackFps()
    {
        if (SceneManager.GetActiveScene().name != "Main") return;
        if (_fpsWindowStart <= 0f) ResetFps();
        _frames++;
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f) _minFps = Math.Min(_minFps, 1f / dt);
        float elapsed = Time.realtimeSinceStartup - _fpsWindowStart;
        if (elapsed < 10f) return;
        Utils.Log.Info($"[dummies] {_dummies.Count} dummies: {_frames / elapsed:0} fps average, {_minFps:0} lowest (last {elapsed:0} s)");
        ResetFps();
    }
}

#endif
