#if !MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Il2CppFishNet.Connection;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Banned players: Steam IDs the host won't let in, for a set time or for good. Saved as
//   BannedPlayers = "76561198000000001 (Alex), 76561198000000002 (Sam) until 2026-11-10T18:00:00Z"
// The name is only a reminder; the number is what's checked. No "until" means permanent.
// A banned player is disconnected as soon as their game sends its Steam login (StaleAuthFix),
// and straight away if they're connected when the ban is made. Bans end on their own.
public static class BanList
{
    public readonly struct Entry
    {
        public readonly ulong SteamId;
        public readonly string Name;
        public readonly DateTime? Until;   // UTC; null = permanent
        public Entry(ulong steamId, string name, DateTime? until = null) { SteamId = steamId; Name = name; Until = until; }
        public bool Permanent => Until == null;
    }

    private static MelonPreferences_Entry<string>? _entry;
    private static readonly List<Entry> _bans = new();

    /// <summary>Current bans, newest last. Expired bans are dropped first.</summary>
    public static IReadOnlyList<Entry> Bans { get { Prune(); return _bans; } }

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("BannedPlayers", "", "Banned players",
            "Steam IDs that can't join your games, separated by commas. A name in brackets is just a reminder; 'until' and a UTC time make the ban end then. Easiest to manage in Host settings.");
        Parse(_entry.Value);

        // 1.0.8 beta 1 and 2 saved bans as "Blacklist"; move them over once.
        var old = cat.CreateEntry("Blacklist", "", "Blacklist (old)", "Moved to BannedPlayers.", is_hidden: true);
        if (!string.IsNullOrWhiteSpace(old.Value))
        {
            int before = _bans.Count;
            foreach (var e in ParseList(old.Value))
                if (_bans.All(b => b.SteamId != e.SteamId)) _bans.Add(e);
            old.Value = "";
            Store();
            MelonLogger.Msg($"Moved {_bans.Count - before} ban(s) from the old Blacklist setting to BannedPlayers");
        }

        Prune();
        if (_bans.Count > 0) MelonLogger.Msg($"Banned players: {_bans.Count}");
    }

    private static void Parse(string? raw)
    {
        _bans.Clear();
        _bans.AddRange(ParseList(raw));
    }

    private static List<Entry> ParseList(string? raw)
    {
        var list = new List<Entry>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (var part in raw.Split(','))
        {
            var s = part.Trim();
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0 || !ulong.TryParse(s.Substring(0, i), out ulong id) || id == 0)
            {
                if (s.Length > 0) MelonLogger.Warning($"Banned players: '{s}' doesn't start with a Steam ID; skipped");
                continue;
            }
            string rest = s.Substring(i).Trim();
            DateTime? until = null;
            string? datePart = null;
            int u = rest.LastIndexOf("until ", StringComparison.OrdinalIgnoreCase);
            if (u >= 0 && (u == 0 || rest[u - 1] == ' ' || rest[u - 1] == ')'))
            {
                datePart = rest.Substring(u + 6);
                rest = rest.Substring(0, u);
            }
            if (datePart != null)
            {
                if (DateTime.TryParse(datePart.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
                    until = DateTime.SpecifyKind(d, DateTimeKind.Utc);
                else MelonLogger.Warning($"Banned players: couldn't read the end time '{datePart.Trim()}' for {id}; treating the ban as permanent");
            }
            string name = rest.Trim().Trim('(', ')').Trim();
            if (list.All(b => b.SteamId != id)) list.Add(new Entry(id, name, until));
        }
        return list;
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
            if (b.Until.HasValue) sb.Append(" until ").Append(b.Until.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }
        _entry.Value = sb.ToString();
        MelonPreferences.Save();
    }

    private static void Prune()
    {
        var now = DateTime.UtcNow;
        var ended = _bans.Where(b => b.Until.HasValue && b.Until.Value <= now).ToList();
        if (ended.Count == 0) return;
        _bans.RemoveAll(b => b.Until.HasValue && b.Until.Value <= now);
        foreach (var e in ended) MelonLogger.Msg($"Ban ended for {(e.Name.Length > 0 ? e.Name + " " : "")}({e.SteamId})");
        Store();
    }

    private static string Clean(string? n) =>
        new string((n ?? "").Where(c => c != ',' && c != '(' && c != ')' && !char.IsControl(c)).ToArray()).Replace(" until ", " ").Trim();

    public static bool IsBanned(ulong id)
    {
        if (id == 0) return false;
        Prune();
        return _bans.Any(b => b.SteamId == id);
    }

    /// <summary>"Permanent", "45 min left", "23 h left", "6 days left".</summary>
    public static string Remaining(Entry e)
    {
        if (!e.Until.HasValue) return "Permanent";
        var left = e.Until.Value - DateTime.UtcNow;
        if (left.TotalMinutes < 1) return "Ending";
        if (left.TotalMinutes < 60) return $"{Math.Ceiling(left.TotalMinutes)} min left";
        if (left.TotalHours < 48) return $"{Math.Ceiling(left.TotalHours)} h left";
        return $"{Math.Ceiling(left.TotalDays)} days left";
    }

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

    /// <summary>Bans for the given time (null = permanent). Re-banning replaces the old length.</summary>
    public static void Ban(ulong id, string name, TimeSpan? length = null)
    {
        if (id == 0) return;
        if (id == HostSteamId()) { MelonLogger.Warning("Refused to ban your own Steam ID"); return; }
        DateTime? until = length.HasValue ? DateTime.UtcNow + length.Value : null;
        _bans.RemoveAll(b => b.SteamId == id);
        Whitelist.Remove(id);
        _bans.Add(new Entry(id, Clean(name), until));
        Store();
        MelonLogger.Msg($"Banned {Clean(name)} ({id}) {(until.HasValue ? "until " + until.Value.ToString("u", CultureInfo.InvariantCulture) : "permanently")}");
        HostPlayers.DisconnectSteamId(id);
    }

    public static void Unban(ulong id)
    {
        int n = _bans.RemoveAll(b => b.SteamId == id);
        if (n == 0) return;
        Store();
        MelonLogger.Msg($"Unbanned {id}");
    }

    /// <summary>Called when a connection sends its Steam login. Disconnects banned players.</summary>
    public static bool RejectIfBanned(NetworkConnection conn, ulong steamId)
    {
        if (!IsBanned(steamId)) return false;
        var e = _bans.First(b => b.SteamId == steamId);
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (!_lastRefusedLog.TryGetValue(steamId, out float last) || now - last > 60f)
        {
            _lastRefusedLog[steamId] = now;
            MelonLogger.Msg($"Refused {(e.Name.Length > 0 ? e.Name + " " : "")}({steamId}): banned, {Remaining(e).ToLowerInvariant()}");
        }
        try { conn.Disconnect(true); } catch (Exception ex) { MelonLogger.Warning($"Couldn't disconnect banned player: {ex.Message}"); }
        return true;
    }
}

#endif
