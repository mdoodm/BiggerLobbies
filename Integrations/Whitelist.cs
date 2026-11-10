#if !MONO
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppFishNet.Connection;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// The whitelist. When it's on, only the host and the Steam IDs on it can join.
// The on/off switch is saved; the list itself is not, so it starts empty every time the
// game starts and the host picks who's allowed for this session. Bans still apply on
// top: a banned player is refused even if whitelisted.
public static class Whitelist
{
    private static MelonPreferences_Entry<bool>? _enabled;
    private static readonly List<BanList.Entry> _allowed = new();
    private static readonly Dictionary<ulong, float> _lastRefusedLog = new();

    public static bool Enabled => _enabled?.Value ?? false;
    /// <summary>Listed players: in Schedule I first, then online, then the rest, each by name.</summary>
    public static IReadOnlyList<BanList.Entry> Players => _allowed
        .OrderByDescending(e => (int)Friends.StateOf(e.SteamId))
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static void Init(MelonPreferences_Category cat)
    {
        _enabled = cat.CreateEntry("WhitelistEnabled", false, "Whitelist",
            "When true, only players you add to the whitelist in Host settings can join. The list starts empty every time you start the game.");
        if (Enabled) Utils.Log.Info("Whitelist is on: only players you add in Host settings can join, and the list starts empty");
    }

    public static void SetEnabled(bool on)
    {
        if (_enabled == null || _enabled.Value == on) return;
        _enabled.Value = on;
        MelonPreferences.Save();
        Utils.Log.Info(on ? "Whitelist turned on" : "Whitelist turned off");
    }

    public static void WarnIfEmpty()
    {
        if (Enabled && _allowed.Count == 0)
            MelonLogger.Warning("Whitelist is on and empty: nobody else can join until you add players in Host settings (pause menu or F6)");
    }

    public static bool Contains(ulong id) => _allowed.Any(e => e.SteamId == id);

    /// <summary>False if the player is banned: bans always win.</summary>
    public static bool Add(ulong id, string name)
    {
        if (id == 0 || BanList.IsBanned(id)) return false;
        if (Contains(id)) return true;
        _allowed.Add(new BanList.Entry(id, name ?? ""));
        Utils.Log.Info($"Whitelisted {name} ({id}) for this session");
        return true;
    }

    /// <summary>Replaces the whole list (for loading a saved crew). Banned players are left off. Returns how many were added.</summary>
    public static int Replace(IEnumerable<BanList.Entry> members)
    {
        _allowed.Clear();
        int n = 0;
        foreach (var m in members)
        {
            if (m.SteamId == 0 || BanList.IsBanned(m.SteamId) || Contains(m.SteamId)) continue;
            _allowed.Add(new BanList.Entry(m.SteamId, m.Name ?? ""));
            n++;
        }
        return n;
    }

    public static void Remove(ulong id)
    {
        if (_allowed.RemoveAll(e => e.SteamId == id) > 0) Utils.Log.Info($"Removed {id} from the whitelist");
    }

    /// <summary>Called when a connection sends its Steam login. Disconnects players who aren't listed.</summary>
    public static bool RejectIfNotListed(NetworkConnection conn, ulong steamId)
    {
        if (!Enabled || steamId == 0 || steamId == BanList.HostSteamId() || Contains(steamId)) return false;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (!_lastRefusedLog.TryGetValue(steamId, out float last) || now - last > 60f)
        {
            _lastRefusedLog[steamId] = now;
            Utils.Log.Info($"Refused {Friends.NameOf(steamId)} ({steamId}): not on the whitelist");
        }
        KickNotice.Remove(conn, steamId, KickNotice.Kind.Whitelist, null, "", conn.Authenticated ? KickNotice.DisconnectDelay : KickNotice.QuickDelay);
        return true;
    }
}

#endif
