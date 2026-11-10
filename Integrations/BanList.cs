#if !MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Banned players: Steam IDs the host won't let in, for a set time or for good. Saved as
//   BannedPlayers = "76561198000000001 (Alex), 76561198000000002 (Sam) [griefing] until 2026-11-10T18:00:00Z"
// The name and the reason in square brackets are only reminders; the number is what's
// checked. No "until" means permanent. Lines saved before reasons existed still read.
// A banned player is disconnected as soon as their game sends its Steam login (StaleAuthFix),
// and straight away if they're connected when the ban is made. Bans end on their own.
public static class BanList
{
    public readonly struct Entry
    {
        public readonly ulong SteamId;
        public readonly string Name;
        public readonly DateTime? Until;   // UTC; null = permanent
        public readonly string Reason;     // may be empty
        public Entry(ulong steamId, string name, DateTime? until = null, string reason = "") { SteamId = steamId; Name = name; Until = until; Reason = reason ?? ""; }
        public bool Permanent => Until == null;
    }

    private static MelonPreferences_Entry<string>? _entry;
    private static readonly List<Entry> _bans = new();

    /// <summary>Current bans, newest last. Expired bans are dropped first.</summary>
    public static IReadOnlyList<Entry> Bans { get { Prune(); return _bans; } }

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("BannedPlayers", "", "Banned players",
            "Steam IDs that can't join your games, separated by commas. A name in round brackets and a reason in square brackets are just reminders; 'until' and a UTC time make the ban end then. Easiest to manage in Host settings.");
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
            Utils.Log.Info($"Moved {_bans.Count - before} ban(s) from the old Blacklist setting to BannedPlayers");
        }

        Prune();
        if (_bans.Count > 0) Utils.Log.Info($"Banned players: {_bans.Count}");
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
            // "(name) [reason]". Names can't hold ')' and reasons can't hold '[' or ']'.
            string reason = "";
            rest = rest.Trim();
            int close = rest.StartsWith("(") ? rest.IndexOf(')') : -1;
            int rs = rest.IndexOf('[', close + 1);
            if (rs >= 0 && rest.EndsWith("]"))
            {
                reason = rest.Substring(rs + 1, rest.Length - rs - 2).Trim();
                rest = rest.Substring(0, rs);
            }
            string name = rest.Trim().Trim('(', ')').Trim();
            if (list.All(b => b.SteamId != id)) list.Add(new Entry(id, name, until, reason));
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
            string r = CleanReason(b.Reason);
            if (r.Length > 0) sb.Append(" [").Append(r).Append(']');
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
        foreach (var e in ended) Utils.Log.Info($"Ban ended for {(e.Name.Length > 0 ? e.Name + " " : "")}({e.SteamId})");
        Store();
    }

    private static string Clean(string? n) =>
        new string((n ?? "").Where(c => c != ',' && c != '(' && c != ')' && !char.IsControl(c)).ToArray()).Replace(" until ", " ").Trim();

    /// <summary>A reason that fits the saved list: no commas or square brackets, no "until ", at most 40 characters.</summary>
    public static string CleanReason(string? r)
    {
        var s = new string((r ?? "").Where(c => c != ',' && c != '[' && c != ']' && !char.IsControl(c)).ToArray());
        s = Regex.Replace(s, "until ", "till ", RegexOptions.IgnoreCase).Trim();
        return s.Length > 40 ? s.Substring(0, 40).Trim() : s;
    }

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

    /// <summary>Called when a connection closes (StaleAuthFix), so the map doesn't grow all session.</summary>
    public static void ForgetLogin(int clientId) => _verified.Remove(clientId);

    /// <summary>True while any open connection logged in with this Steam ID, even before their player spawns.</summary>
    public static bool IsConnected(ulong steamId)
    {
        if (steamId == 0) return false;
        Il2CppSystem.Collections.Generic.Dictionary<int, NetworkConnection>? clients = null;
        try { clients = InstanceFinder.ServerManager?.Clients; } catch { }
        foreach (var kv in _verified)
        {
            if (kv.Value != steamId) continue;
            if (clients == null) return true;   // can't check the connection: assume they're here
            try { if (clients.TryGetValue(kv.Key, out var c) && c != null && c.IsActive) return true; }
            catch { return true; }
        }
        return false;
    }

    public static ulong HostSteamId()
    {
        try { return Il2CppSteamworks.SteamUser.GetSteamID().m_SteamID; } catch { return 0; }
    }

    private static readonly Dictionary<ulong, float> _lastRefusedLog = new();

    /// <summary>Bans for the given time (null = permanent). Re-banning replaces the old length.</summary>
    public static void Ban(ulong id, string name, TimeSpan? length = null, string reason = "")
    {
        if (id == 0) return;
        if (id == HostSteamId()) { MelonLogger.Warning("Refused to ban your own Steam ID"); return; }
        DateTime? until = length.HasValue ? DateTime.UtcNow + length.Value : null;
        _bans.RemoveAll(b => b.SteamId == id);
        Whitelist.Remove(id);
        reason = CleanReason(reason);
        _bans.Add(new Entry(id, Clean(name), until, reason));
        Store();
        Utils.Log.Info($"Banned {Clean(name)} ({id}) {(until.HasValue ? "until " + until.Value.ToString("u", CultureInfo.InvariantCulture) : "permanently")}{(reason.Length > 0 ? ": " + reason : "")}");
        HostPlayers.DisconnectSteamId(id, length, reason);
    }

    public static void Unban(ulong id)
    {
        int n = _bans.RemoveAll(b => b.SteamId == id);
        if (n == 0) return;
        Store();
        Utils.Log.Info($"Unbanned {id}");
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
            Utils.Log.Info($"Refused {(e.Name.Length > 0 ? e.Name + " " : "")}({steamId}): banned, {Remaining(e).ToLowerInvariant()}");
        }
        TimeSpan? left = e.Until.HasValue ? e.Until.Value - DateTime.UtcNow : null;
        // Refused at their first login nothing else is in flight, so a short wait is enough for the notice.
        KickNotice.Remove(conn, steamId, KickNotice.Kind.Ban, left, e.Reason, conn.Authenticated ? KickNotice.DisconnectDelay : KickNotice.QuickDelay);
        return true;
    }
}

#endif
