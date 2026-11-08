#if !MONO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppFishNet.Connection;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// The blacklist: Steam IDs the host won't let in, saved in the config as
//   Blacklist = "76561198000000001 (Alex), 76561198000000002 (Sam)"
// The name is only a reminder; the number is what's checked. A banned player is
// disconnected as soon as their game sends its Steam login to the host (StaleAuthFix).
public static class BanList
{
    public readonly struct Entry
    {
        public readonly ulong SteamId;
        public readonly string Name;
        public Entry(ulong steamId, string name) { SteamId = steamId; Name = name; }
    }

    private static MelonPreferences_Entry<string>? _entry;
    private static readonly List<Entry> _bans = new();

    public static IReadOnlyList<Entry> Bans => _bans;

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("Blacklist", "", "Blacklist",
            "Steam IDs that can never join your games, separated by commas. A name in brackets after the ID is just a reminder. Easiest to manage in Host settings.");
        Parse(_entry.Value);
        if (_bans.Count > 0) MelonLogger.Msg($"Blacklist: {_bans.Count} player{(_bans.Count == 1 ? "" : "s")}");
    }

    private static void Parse(string? raw)
    {
        _bans.Clear();
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var part in raw.Split(','))
        {
            var s = part.Trim();
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0 || !ulong.TryParse(s.Substring(0, i), out ulong id) || id == 0)
            {
                if (s.Length > 0) MelonLogger.Warning($"Blacklist: '{s}' doesn't start with a Steam ID; skipped");
                continue;
            }
            string name = s.Substring(i).Trim().Trim('(', ')').Trim();
            if (_bans.All(b => b.SteamId != id)) _bans.Add(new Entry(id, name));
        }
    }

    private static void Store()
    {
        if (_entry == null) return;
        var sb = new StringBuilder();
        foreach (var b in _bans)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(b.SteamId);
            string n = Clean(b.Name);
            if (n.Length > 0) sb.Append(" (").Append(n).Append(')');
        }
        _entry.Value = sb.ToString();
        MelonPreferences.Save();
    }

    private static string Clean(string? n) =>
        new string((n ?? "").Where(c => c != ',' && c != '(' && c != ')' && !char.IsControl(c)).ToArray()).Trim();

    public static bool IsBanned(ulong id) => id != 0 && _bans.Any(b => b.SteamId == id);

    // Steam ID per connection, taken from the Steam login the host checks (StaleAuthFix).
    // A player's PlayerCode is reported by their own game, so it's never used for bans.
    private static readonly Dictionary<int, ulong> _verified = new();
    public static void RecordLogin(int clientId, ulong steamId) => _verified[clientId] = steamId;
    public static ulong VerifiedSteamId(int clientId) => _verified.TryGetValue(clientId, out var id) ? id : 0;

    public static ulong HostSteamId()
    {
        try { return Il2CppSteamworks.SteamUser.GetSteamID().m_SteamID; } catch { return 0; }
    }

    private static readonly Dictionary<ulong, float> _lastRefusedLog = new();

    public static void Ban(ulong id, string name)
    {
        if (id == 0 || IsBanned(id)) return;
        if (id == HostSteamId()) { MelonLogger.Warning("Refused to blacklist your own Steam ID"); return; }
        Whitelist.Remove(id);
        _bans.Add(new Entry(id, Clean(name)));
        Store();
        MelonLogger.Msg($"Blacklisted {Clean(name)} ({id})");
        HostPlayers.DisconnectSteamId(id);
    }

    public static void Unban(ulong id)
    {
        int n = _bans.RemoveAll(b => b.SteamId == id);
        if (n == 0) return;
        Store();
        MelonLogger.Msg($"Removed {id} from the blacklist");
    }

    /// <summary>Called when a connection sends its Steam login. Disconnects banned players.</summary>
    public static bool RejectIfBanned(NetworkConnection conn, ulong steamId)
    {
        if (!IsBanned(steamId)) return false;
        var name = _bans.First(b => b.SteamId == steamId).Name;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (!_lastRefusedLog.TryGetValue(steamId, out float last) || now - last > 60f)
        {
            _lastRefusedLog[steamId] = now;
            MelonLogger.Msg($"Refused {(name.Length > 0 ? name + " " : "")}({steamId}): on the blacklist");
        }
        try { conn.Disconnect(true); } catch (Exception e) { MelonLogger.Warning($"Couldn't disconnect banned player: {e.Message}"); }
        return true;
    }
}

#endif
