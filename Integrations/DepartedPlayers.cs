#if !MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using MelonLoader.Utils;

namespace BiggerLobbies.Integrations;

// Players who left still have their inventory in the host's save, in
//   <save>/Players/Player_<SteamID>/Inventory.json
// The game only reads that file again when they rejoin, and host saves never rewrite a
// departed player's folder. "Take items" moves their items into the host's inventory (as
// much as fits) plus their cash on hand, and blanks those slots in the file, so they
// don't get them back. A copy of the file is kept in UserData/BiggerLobbies/Taken.
public static class DepartedPlayers
{
    public sealed class Slot
    {
        public int Index;
        public string Json = "";
        public string Id = "";
        public int Quantity;
        public ItemInstance? Item;
    }

    public sealed class Info
    {
        public ulong SteamId;
        public string Name = "";
        public string Folder = "";
        public DateTime SavedUtc;
        public List<Slot> Items = new();
        public float Cash;
    }

    private static List<Info> _cache = new();
    private static float _cacheUntil;
    private const double SettleSeconds = 10;   // let the player's own exit save land first

    private static readonly Regex IdRx = new("\"ID\"\\s*:\\s*\"([^\"]*)\"");
    private static readonly Regex QtyRx = new("\"Quantity\"\\s*:\\s*(\\d+)");
    private static readonly Regex TypeRx = new("\"DataType\"\\s*:\\s*\"([^\"]*)\"");
    private static readonly Regex CashRx = new("\"CashBalance\"\\s*:\\s*(-?[0-9.eE+-]+)");

    private static string SaveName()
    {
        try { return Path.GetFileName((Singleton<LoadManager>.Instance?.LoadedGameFolderPath ?? "").TrimEnd('/', '\\')); }
        catch { return ""; }
    }

    // --- Take log: one line per take, kept next to the backups. ---
    public sealed class Taken
    {
        public DateTime Utc;
        public ulong SteamId;
        public string Name = "", Summary = "", Save = "";
    }

    private static string LogFile => Path.Combine(MelonEnvironment.UserDataDirectory, "BiggerLobbies", "Taken", "TakenLog.tsv");
    private static List<Taken>? _log;
    private static DateTime _logStamp;

    private static string Tsv(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static void AppendLog(ulong id, string name, string summary)
    {
        try
        {
            var line = string.Join("\t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Tsv(SaveName()), id.ToString(), Tsv(name), Tsv(summary));
            File.AppendAllText(LogFile, line + "\n");
            _log = null;
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't write the take log: {e.Message}"); }
    }

    /// <summary>Takes from this save, newest first.</summary>
    public static List<Taken> History(int max = 8)
    {
        try
        {
            if (!File.Exists(LogFile)) return new List<Taken>();
            var stamp = File.GetLastWriteTimeUtc(LogFile);
            if (_log == null || stamp != _logStamp)
            {
                _logStamp = stamp;
                _log = new List<Taken>();
                foreach (var line in File.ReadAllLines(LogFile))
                {
                    var f = line.Split('\t');
                    if (f.Length < 5 || !ulong.TryParse(f[2], out var id)) continue;
                    if (!DateTime.TryParse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) continue;
                    _log.Add(new Taken { Utc = t.ToUniversalTime(), Save = f[1], SteamId = id, Name = f[3], Summary = f[4] });
                }
            }
            string save = SaveName();
            return _log.Where(e => e.Save == save).OrderByDescending(e => e.Utc).Take(max).ToList();
        }
        catch { return new List<Taken>(); }
    }

    private static string? PlayersFolder()
    {
        try
        {
            var lm = Singleton<LoadManager>.Instance;
            var root = lm?.LoadedGameFolderPath;
            if (string.IsNullOrEmpty(root)) return null;
            var p = Path.Combine(root, "Players");
            return Directory.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    private static HashSet<string> OnlineCodes()
    {
        var set = new HashSet<string>();
        var list = Player.PlayerList;
        if (list != null)
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !string.IsNullOrEmpty(list[i].PlayerCode)) set.Add(list[i].PlayerCode);
        return set;
    }

    /// <summary>Saved players who aren't connected and are carrying items, most recent first.</summary>
    public static List<Info> List()
    {
        if (UnityEngine.Time.unscaledTime < _cacheUntil) return _cache;
        _cacheUntil = UnityEngine.Time.unscaledTime + 2f;
        var result = new List<Info>();
        try
        {
            if (!InstanceFinder.IsServer) { _cache = result; return result; }
            var folder = PlayersFolder();
            if (folder == null) { _cache = result; return result; }
            var online = OnlineCodes();
            foreach (var dir in Directory.GetDirectories(folder, "Player_*"))
            {
                string code = Path.GetFileName(dir).Substring("Player_".Length);
                if (!ulong.TryParse(code, out ulong id) || id == 0) continue;   // Player_0 is the host
                if (online.Contains(code) || id == BanList.HostSteamId()) continue;
                var file = Path.Combine(dir, "Inventory.json");
                if (!File.Exists(file)) continue;
                var text = File.ReadAllText(file);
                var slots = ReadItems(text, out var lits, out _);
                float cash = CashOf(lits, out _);
                if (slots.Count == 0 && cash < 0.01f) continue;
                result.Add(new Info
                {
                    SteamId = id,
                    Name = Friends.NameOf(id),
                    Folder = dir,
                    SavedUtc = File.GetLastWriteTimeUtc(file),
                    Items = slots,
                    Cash = cash,
                });
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't read saved players: {e.Message}"); }
        _cache = result.OrderByDescending(r => r.SavedUtc).ToList();
        return _cache;
    }

    public static void Invalidate() => _cacheUntil = 0;

    // --- Inventory.json: {"Items":["<json>", ...], "SlotFilters":[...]} ---------------------

    private sealed class Literal { public int Start, End; public string Value = ""; }

    /// <summary>Non-empty, non-cash item slots. Also returns every slot literal and the file text.</summary>
    private static List<Slot> ReadItems(string text, out List<Literal> literals, out string fileText)
    {
        fileText = text;
        literals = ParseItemsArray(text);
        var slots = new List<Slot>();
        for (int i = 0; i < literals.Count; i++)
        {
            string json = literals[i].Value;
            var type = TypeRx.Match(json).Groups[1].Value;
            var id = IdRx.Match(json).Groups[1].Value;
            int.TryParse(QtyRx.Match(json).Groups[1].Value, out int qty);
            if (type == "CashData" || id.Length == 0 || id == "cash" || qty <= 0) continue;
            ItemInstance? item = null;
            try { item = ItemDeserializer.LoadItem(json); } catch { }
            slots.Add(new Slot { Index = i, Json = json, Id = id, Quantity = qty, Item = item });
        }
        return slots;
    }

    private static float CashOf(List<Literal> literals, out int index)
    {
        index = -1;
        for (int i = 0; i < literals.Count; i++)
        {
            var json = literals[i].Value;
            if (TypeRx.Match(json).Groups[1].Value != "CashData") continue;
            index = i;
            var m = CashRx.Match(json);
            return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }
        return 0f;
    }

    private static List<Literal> ParseItemsArray(string s)
    {
        var list = new List<Literal>();
        int k = s.IndexOf("\"Items\"", StringComparison.Ordinal);
        if (k < 0) return list;
        k = s.IndexOf('[', k);
        if (k < 0) return list;
        int i = k + 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ']') break;
            if (c != '"') { i++; continue; }
            int start = i;
            var sb = new StringBuilder();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char e = s[i + 1];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); i += 2; continue;
                        case 't': sb.Append('\t'); i += 2; continue;
                        case 'r': sb.Append('\r'); i += 2; continue;
                        case 'b': sb.Append('\b'); i += 2; continue;
                        case 'f': sb.Append('\f'); i += 2; continue;
                        case 'u':
                            if (i + 5 < s.Length && int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber, null, out int cp))
                            { sb.Append((char)cp); i += 6; continue; }
                            break;
                        default: sb.Append(e); i += 2; continue;
                    }
                }
                sb.Append(s[i]);
                i++;
            }
            list.Add(new Literal { Start = start, End = i + 1, Value = sb.ToString() });
            i++;
        }
        return list;
    }

    private static string Quote(string v)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in v)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // An empty slot in the same format the game writes, reusing the file's own GameVersion.
    private static string EmptySlot(string sample)
    {
        var ver = Regex.Match(sample, "\"GameVersion\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        if (ver.Length == 0) ver = UnityEngine.Application.version;
        return "{\"DataType\":\"ItemData\",\"DataVersion\":0,\"GameVersion\":\"" + ver + "\",\"ID\":\"\",\"Quantity\":0}";
    }

    /// <summary>Moves as many of this player's item stacks into the host's inventory as fit. Returns a status line.</summary>
    public static string Take(ulong steamId)
    {
        try
        {
            if (!InstanceFinder.IsServer) return "Only the host can take items";
            string code = steamId.ToString();
            if (OnlineCodes().Contains(code)) return "They're in the game. Items can only be taken after they leave";
            var folder = PlayersFolder();
            if (folder == null) return "Couldn't find this save's player folders";
            var dir = Path.Combine(folder, "Player_" + code);
            var file = Path.Combine(dir, "Inventory.json");
            if (!File.Exists(file)) return "Nothing saved for that player";
            var age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds;
            if (age < SettleSeconds) return $"They only just left. Try again in {Math.Ceiling(SettleSeconds - age)} s";

            var inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null) return "Your inventory isn't ready";

            string text = File.ReadAllText(file);
            var slots = ReadItems(text, out var literals, out _);
            float cash = CashOf(literals, out int cashIndex);
            if (slots.Count == 0 && cash < 0.01f) { Invalidate(); return "They aren't carrying anything"; }

            // Keep a copy of their file before changing anything.
            var backupDir = Path.Combine(MelonEnvironment.UserDataDirectory, "BiggerLobbies", "Taken");
            Directory.CreateDirectory(backupDir);
            File.Copy(file, Path.Combine(backupDir, $"{code}_{DateTime.Now:yyyyMMdd-HHmmss}_Inventory.json"), true);

            var replace = new Dictionary<int, string>();
            var taken = new List<string>();
            int left = 0;
            string empty = Quote(EmptySlot(literals[0].Value));
            foreach (var s in slots)
            {
                ItemInstance? item = null;
                try { item = ItemDeserializer.LoadItem(s.Json); } catch { }
                if (item == null) { left++; continue; }
                int qty = Math.Max(1, s.Quantity);
                if (!inv.CanItemFitInInventory(item, qty)) { left++; continue; }
                inv.AddItemToInventory(item);
                replace[s.Index] = empty;
                taken.Add($"{qty}x {SafeName(item, s.Id)}");
            }

            // Cash on hand always fits.
            if (cash >= 0.01f && cashIndex >= 0 && inv.cashInstance != null)
            {
                inv.cashInstance.ChangeBalance(cash);
                replace[cashIndex] = Quote(CashRx.Replace(literals[cashIndex].Value, "\"CashBalance\":0.0", 1));
                taken.Add($"${cash:N0} cash");
            }

            if (replace.Count > 0)
            {
                var sb = new StringBuilder();
                int pos = 0;
                for (int i = 0; i < literals.Count; i++)
                {
                    if (!replace.TryGetValue(i, out var lit)) continue;
                    sb.Append(text, pos, literals[i].Start - pos).Append(lit);
                    pos = literals[i].End;
                }
                sb.Append(text, pos, text.Length - pos);
                File.WriteAllText(file, sb.ToString());
                Utils.Log.Info($"Took from {Friends.NameOf(steamId)} ({steamId}): {string.Join(", ", taken)}. Their original inventory is saved in UserData/BiggerLobbies/Taken");
                AppendLog(steamId, Friends.NameOf(steamId), string.Join(", ", taken));
            }
            Invalidate();
            string name = Friends.NameOf(steamId);
            if (replace.Count == 0) return "Your inventory is full. Make room and try again";
            return left > 0
                ? $"Took {string.Join(", ", taken)} from {name}. {left} stack{(left == 1 ? "" : "s")} left: make room to take the rest"
                : $"Took everything from {name}: {string.Join(", ", taken)}";
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Take items failed: {e}");
            return "Couldn't take the items. See the log";
        }
    }

    public static string SafeName(ItemInstance? item, string fallback)
    {
        try { var n = item?.Name; if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return fallback;
    }

    public static UnityEngine.Sprite? Icon(ItemInstance? item)
    {
        try { return item?.Icon; } catch { return null; }
    }
}

#endif
