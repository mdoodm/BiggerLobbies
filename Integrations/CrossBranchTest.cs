using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine.InputSystem;

namespace BiggerLobbies.Integrations;

// Testing only: lets a player on the other Steam branch (Il2Cpp default vs Mono alternate) join.
//
// The only cross-branch block is on the joining player's game: SteamLobbyService.OnLobbyEntered
// compares the lobby's "version" data with its own Application.version ("0.4.7f12" vs
// "0.4.7f12 Alternate") and leaves on any difference. The host sets that value once, in
// OnLobbyCreated. Pressing the key flips it to the other branch's string and back. Players
// already in the lobby aren't checked again, so flip it while the friend joins, then flip back.
//
// Whether the two builds then stay in sync over FishNet is what this switch is for finding out.
[HarmonyPatch]
public static class CrossBranchTest
{
    private const string Suffix = " Alternate";

    private static MelonPreferences_Entry<string>? _keyEntry;
    private static Key _key = Key.None;
    private static bool _advertisingOther;

    /// <summary>True while the key has switched the lobby to the other branch's version.</summary>
    public static bool Active => _advertisingOther;

    public static void Init(MelonPreferences_Category cat)
    {
        _keyEntry = cat.CreateEntry("DebugCrossBranchKey", "", "Debug: cross-branch join key",
            "Testing only. A key name (for example F8). While you host, it switches the lobby's game version between yours and the other Steam branch's, so a friend on the other branch can join. Leave empty to turn it off.");
        string v = _keyEntry.Value?.Trim() ?? "";
        if (v.Length == 0) return;
        if (!Enum.TryParse(v, true, out _key) || _key == Key.None || !Enum.IsDefined(typeof(Key), _key))
        {
            MelonLogger.Warning($"DebugCrossBranchKey '{v}' isn't a key name; cross-branch test is off");
            _key = Key.None;
            return;
        }
        MelonLogger.Warning($"DebugCrossBranchKey is {_key}: while hosting, {_key} lets players on the other Steam branch ({OtherVersion()}) join. Testing only.");
    }

    /// <summary>The other branch's version string: adds or removes " Alternate".</summary>
    private static string OtherVersion()
    {
        string mine = UnityEngine.Application.version;
        return mine.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)
            ? mine.Substring(0, mine.Length - Suffix.Length)
            : mine + Suffix;
    }

    public static void Update()
    {
        if (_key == Key.None) return;
        var kb = Keyboard.current;
        if (kb == null || !kb[_key].wasPressedThisFrame) return;
        Toggle();
    }

    private static void Toggle()
    {
        if (!HostLobby.TryGet(out var id))
        {
            MelonLogger.Warning("Cross-branch test: you aren't hosting a Steam lobby, nothing to switch");
            return;
        }
        bool other = !_advertisingOther;
        string value = other ? OtherVersion() : UnityEngine.Application.version;
        bool ok = SteamMatchmaking.SetLobbyData(id, "version", value);
        string readBack = SteamMatchmaking.GetLobbyData(id, "version");
        if (!ok || readBack != value)
        {
            MelonLogger.Warning($"Cross-branch test: Steam didn't take the change (asked for '{value}', lobby says '{readBack}')");
            return;
        }
        _advertisingOther = other;
        MelonLogger.Warning(other
            ? $"Cross-branch test ON: lobby version is '{value}'. Only players on the OTHER branch can join now. Press {_key} again once they're in."
            : $"Cross-branch test OFF: lobby version is back to '{value}'. Players on your branch can join again.");
    }

    // The game writes its own version into every new lobby, so a new lobby starts switched off.
    [HarmonyPostfix, HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyCreated))]
    private static void OnLobbyCreated_Postfix()
    {
        if (CrossBranchLobby.Handling || !_advertisingOther) return;
        _advertisingOther = false;
        MelonLogger.Warning("Cross-branch test: new lobby, back to your own version");
    }
}
