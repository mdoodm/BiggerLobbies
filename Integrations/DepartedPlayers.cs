#if !MONO
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BiggerLobbies.Utils;
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
//
// Each take also writes a record next to it (<steamid>_<time>.take.json) with the exact
// slot text that was taken. "Give back" uses it to return a take: only what the host still
// carries (same item, same quality and packaging), only into slots that are still empty in
// the player's file, and never more than was taken. It never makes items.
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

    private static string TakenDir => Path.Combine(MelonEnvironment.UserDataDirectory, "BiggerLobbies", "Taken");

    // --- Names ---------------------------------------------------------------------------
    // A save doesn't keep player names (Player.json only has the Steam ID, as PlayerCode),
    // so the in-game name of everyone the host sees is kept in
    // UserData/BiggerLobbies/PlayerNames.tsv, for players who aren't Steam friends.

    private static string NamesFile => Path.Combine(MelonEnvironment.UserDataDirectory, "BiggerLobbies", "PlayerNames.tsv");
    private static Dictionary<ulong, string>? _names;
    private static float _nextNameScan;
    private static bool _namesWarned;

    private static Dictionary<ulong, string> Names()
    {
        if (_names != null) return _names;
        _names = new Dictionary<ulong, string>();
        try
        {
            if (File.Exists(NamesFile))
                foreach (var line in File.ReadAllLines(NamesFile))
                {
                    var f = line.Split('\t');
                    if (f.Length >= 2 && ulong.TryParse(f[0], out var id) && id != 0 && f[1].Trim().Length > 0) _names[id] = f[1].Trim();
                }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't read saved player names: {e.Message}"); }
        return _names;
    }

    /// <summary>Remembers connected players' names (by checked Steam ID). Cheap; runs every few seconds while hosting.</summary>
    public static void Update()
    {
        if (UnityEngine.Time.unscaledTime < _nextNameScan) return;
        _nextNameScan = UnityEngine.Time.unscaledTime + 5f;
        try
        {
            if (!InstanceFinder.IsServer) return;
            var list = Player.PlayerList;
            if (list == null) return;
            var names = Names();
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.IsLocalPlayer) continue;
                var conn = p.Connection;
                if (conn == null || conn.IsLocalClient) continue;
                ulong id = BanList.VerifiedSteamId(conn.ClientId);
                string n = Tsv(p.PlayerName ?? "").Trim();
                if (id == 0 || n.Length == 0) continue;
                if (names.TryGetValue(id, out var old) && old == n) continue;
                names[id] = n;
                changed = true;
            }
            if (!changed) return;
            Directory.CreateDirectory(Path.GetDirectoryName(NamesFile)!);
            File.WriteAllLines(NamesFile, names.Select(kv => kv.Key + "\t" + kv.Value));
        }
        catch (Exception e)
        {
            if (!_namesWarned) { _namesWarned = true; MelonLogger.Warning($"Couldn't save player names: {e.Message}"); }
        }
    }

    /// <summary>The name the player last had in a game you hosted, else their Steam name, else the number.</summary>
    public static string NameOf(ulong steamId)
    {
        try { if (Names().TryGetValue(steamId, out var n) && n.Length > 0) return n; } catch { }
        return Friends.NameOf(steamId);
    }

    // --- Take log: one line per take or give back, kept next to the backups. ---
    // utc, save, steam id, name, summary, record file (empty for give backs and old lines)

    public sealed class TakenSlot
    {
        public int Index;
        public string Literal = "";   // the slot exactly as it was in their file
        public string Id = "";
        public int Quantity;
        public int Returned;
    }

    /// <summary>What one take moved, read from its .take.json record.</summary>
    public sealed class TakeRecord
    {
        public string Path = "";
        public string Save = "", Name = "", Backup = "";
        public ulong SteamId;
        public DateTime Utc;
        public List<TakenSlot> Slots = new();
        public double Cash, CashReturned;
        public bool AllReturned => Slots.All(s => s.Returned >= s.Quantity) && Cash - CashReturned < 0.01;
    }

    public sealed class Taken
    {
        public DateTime Utc;
        public ulong SteamId;
        public string Name = "", Summary = "", Save = "";
        public TakeRecord? Record;     // null: a give back, or a take logged before records existed
        public bool GiveBack;
    }

    private static string LogFile => Path.Combine(TakenDir, "TakenLog.tsv");
    private static List<Taken>? _log;
    private static DateTime _logStamp;

    private static string Tsv(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static void AppendLog(ulong id, string name, string summary, string record = "")
    {
        try
        {
            var line = string.Join("\t", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Tsv(SaveName()), id.ToString(), Tsv(name), Tsv(summary), Tsv(record));
            File.AppendAllText(LogFile, line + "\n");
            _log = null;
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't write the take log: {e.Message}"); }
    }

    /// <summary>Takes and give backs in this save, newest first.</summary>
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
                    var e = new Taken { Utc = t.ToUniversalTime(), Save = f[1], SteamId = id, Name = f[3], Summary = f[4] };
                    e.GiveBack = e.Summary.StartsWith("Gave back", StringComparison.Ordinal);
                    if (f.Length >= 6 && f[5].Trim().Length > 0) e.Record = ReadRecord(Path.Combine(TakenDir, Path.GetFileName(f[5].Trim())));
                    _log.Add(e);
                }
            }
            string save = SaveName();
            return _log.Where(e => e.Save == save).OrderByDescending(e => e.Utc).Take(max).ToList();
        }
        catch { return new List<Taken>(); }
    }

    // --- Take records (.take.json) ---------------------------------------------------------

    private static void WriteRecord(TakeRecord r)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"version\": 1,\n");
        sb.Append("  \"save\": ").Append(MiniJson.Quote(r.Save)).Append(",\n");
        sb.Append("  \"steamId\": ").Append(MiniJson.Quote(r.SteamId.ToString())).Append(",\n");
        sb.Append("  \"name\": ").Append(MiniJson.Quote(r.Name)).Append(",\n");
        sb.Append("  \"utc\": ").Append(MiniJson.Quote(r.Utc.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
        sb.Append("  \"backup\": ").Append(MiniJson.Quote(r.Backup)).Append(",\n");
        sb.Append("  \"slots\": [");
        for (int i = 0; i < r.Slots.Count; i++)
        {
            var s = r.Slots[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append("    {\"index\": ").Append(s.Index)
              .Append(", \"id\": ").Append(MiniJson.Quote(s.Id))
              .Append(", \"quantity\": ").Append(s.Quantity)
              .Append(", \"returned\": ").Append(s.Returned)
              .Append(", \"literal\": ").Append(MiniJson.Quote(s.Literal)).Append('}');
        }
        sb.Append(r.Slots.Count > 0 ? "\n  ],\n" : "],\n");
        sb.Append("  \"cash\": ").Append(MiniJson.Number(r.Cash)).Append(",\n");
        sb.Append("  \"cashReturned\": ").Append(MiniJson.Number(r.CashReturned)).Append('\n');
        sb.Append("}\n");
        File.WriteAllText(r.Path, sb.ToString());
    }

    private static TakeRecord? ReadRecord(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (MiniJson.Parse(File.ReadAllText(path)) is not Dictionary<string, object?> o) return null;
            if (!ulong.TryParse(MiniJson.Str(o, "steamId"), out var id) || id == 0) return null;
            DateTime.TryParse(MiniJson.Str(o, "utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc);
            var r = new TakeRecord
            {
                Path = path,
                Save = MiniJson.Str(o, "save"),
                SteamId = id,
                Name = MiniJson.Str(o, "name"),
                Utc = utc.ToUniversalTime(),
                Backup = MiniJson.Str(o, "backup"),
                Cash = MiniJson.Num(o, "cash"),
                CashReturned = MiniJson.Num(o, "cashReturned"),
            };
            if (o.TryGetValue("slots", out var sv) && sv is List<object?> list)
                foreach (var x in list)
                {
                    if (x is not Dictionary<string, object?> so) continue;
                    var s = new TakenSlot
                    {
                        Index = (int)MiniJson.Num(so, "index"),
                        Id = MiniJson.Str(so, "id"),
                        Quantity = (int)MiniJson.Num(so, "quantity"),
                        Returned = (int)MiniJson.Num(so, "returned"),
                        Literal = MiniJson.Str(so, "literal"),
                    };
                    if (s.Literal.Length == 0 || s.Id.Length == 0 || s.Quantity <= 0) continue;
                    r.Slots.Add(s);
                }
            return r;
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't read the take record {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
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

    // In the game, or connected and logged in but not spawned yet (rejoining). Their game may
    // already have their saved inventory then, so changing the file could duplicate items.
    private static bool IsHere(ulong id, HashSet<string>? online = null) =>
        (online ?? OnlineCodes()).Contains(id.ToString()) || BanList.IsConnected(id);

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
                if (IsHere(id, online) || id == BanList.HostSteamId()) continue;
                var file = Path.Combine(dir, "Inventory.json");
                if (!File.Exists(file)) continue;
                var text = File.ReadAllText(file);
                var slots = ReadItems(text, out var lits, out _);
                float cash = CashOf(lits, out _);
                if (slots.Count == 0 && cash < 0.01f) continue;
                result.Add(new Info
                {
                    SteamId = id,
                    Name = NameOf(id),
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

    // The file text with some slot literals swapped; everything else is kept byte for byte.
    private static string Splice(string text, List<Literal> literals, Dictionary<int, string> replace)
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
        return sb.ToString();
    }

    // An empty slot in the same format the game writes, reusing the file's own GameVersion.
    private static string EmptySlot(string sample)
    {
        var ver = Regex.Match(sample, "\"GameVersion\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        if (ver.Length == 0) ver = UnityEngine.Application.version;
        return "{\"DataType\":\"ItemData\",\"DataVersion\":0,\"GameVersion\":\"" + ver + "\",\"ID\":\"\",\"Quantity\":0}";
    }

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>Moves as many of this player's item stacks into the host's inventory as fit. Returns a status line.</summary>
    public static string Take(ulong steamId)
    {
        try
        {
            if (!InstanceFinder.IsServer) return "Only the host can take items";
            if (_givingBack) return "Wait for the give back to finish";
            string code = steamId.ToString();
            if (IsHere(steamId)) return "They're in the game. Items can only be taken after they leave";
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
            Directory.CreateDirectory(TakenDir);
            string stamp = Stamp();
            string backup = $"{code}_{stamp}_Inventory.json";
            File.Copy(file, Path.Combine(TakenDir, backup), true);

            string name = NameOf(steamId);
            var record = new TakeRecord { Save = SaveName(), SteamId = steamId, Name = name, Utc = DateTime.UtcNow, Backup = backup };
            var replace = new Dictionary<int, string>();
            var taken = new List<string>();
            int left = 0;
            string empty = MiniJson.Quote(EmptySlot(literals[0].Value));
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
                record.Slots.Add(new TakenSlot { Index = s.Index, Literal = s.Json, Id = s.Id, Quantity = qty });
            }

            // Cash on hand always fits.
            if (cash >= 0.01f && cashIndex >= 0 && inv.cashInstance != null)
            {
                inv.cashInstance.ChangeBalance(cash);
                replace[cashIndex] = MiniJson.Quote(CashRx.Replace(literals[cashIndex].Value, "\"CashBalance\":0.0", 1));
                taken.Add($"${cash:N0} cash");
                record.Cash = cash;
            }

            if (replace.Count > 0)
            {
                File.WriteAllText(file, Splice(text, literals, replace));
                string recordFile = "";
                try
                {
                    recordFile = $"{code}_{stamp}.take.json";
                    for (int n = 2; File.Exists(Path.Combine(TakenDir, recordFile)); n++) recordFile = $"{code}_{stamp}-{n}.take.json";
                    record.Path = Path.Combine(TakenDir, recordFile);
                    WriteRecord(record);
                }
                catch (Exception e)
                {
                    recordFile = "";
                    MelonLogger.Warning($"Couldn't write the take record, so this take can't be given back: {e.Message}");
                }
                Utils.Log.Info($"Took from {name} ({steamId}): {string.Join(", ", taken)}. Their original inventory is saved in UserData/BiggerLobbies/Taken");
                AppendLog(steamId, name, string.Join(", ", taken), recordFile);
            }
            Invalidate();
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

    // --- Give back -------------------------------------------------------------------------

    private static bool _givingBack;

    // An item's data without the fields that don't make it a different item, so a stack in
    // the host's inventory can be matched to the slot that was taken.
    private static string DataKey(string json)
    {
        try
        {
            if (MiniJson.Parse(json) is not Dictionary<string, object?> o) return "";
            o.Remove("Quantity");
            o.Remove("GameVersion");
            return MiniJson.Canonical(o);
        }
        catch { return ""; }
    }

    // Host inventory slots that hold exactly this item and aren't in use, with their counts.
    private static void Holding(PlayerInventory inv, string id, string key, List<Held> found)
    {
        var all = inv.GetAllInventorySlots();
        if (all == null) return;
        for (int i = 0; i < all.Count; i++)
        {
            var slot = all[i];
            if (slot == null) continue;
            ItemInstance? it = null;
            try { it = slot.ItemInstance; } catch { }
            if (it == null || it.ID != id || it.Quantity <= 0) continue;
            if (slot.IsLocked || slot.IsRemovalLocked) continue;   // in use (e.g. a station holds it)
            string k = "";
            try { k = DataKey(it.GetItemData().GetJson(false)); } catch { }
            if (k.Length == 0 || k != key) continue;
            found.Add(new Held { Slot = slot, Before = it.Quantity });
        }
    }

    private sealed class Held
    {
        public ItemSlot Slot = null!;
        public int Before;

        // How many left this slot, by the slot's own count; 0 if the slot now holds something else.
        public int Dropped(string id)
        {
            try
            {
                var it = Slot.ItemInstance;
                if (it == null) return Before;
                if (it.ID != id) return 0;
                return Math.Max(0, Before - it.Quantity);
            }
            catch { return 0; }
        }
    }

    // One per kind of item (same data): two taken stacks of the same item share the host's stock.
    private sealed class Stock
    {
        public string Id = "";
        public int Usable, Asked;
        public readonly List<Held> Slots = new();
    }

    private sealed class Pending
    {
        public TakenSlot Slot = null!;
        public string Key = "";
        public int Asked;
    }

    /// <summary>Starts returning a take. The status comes back through <paramref name="done"/> once the host's inventory has updated.</summary>
    public static string GiveBack(Taken t, Action<string> done)
    {
        try
        {
            if (!InstanceFinder.IsServer) return "Only the host can give items back";
            if (_givingBack) return "Already giving back. Wait a moment";
            if (t.Record == null) return "That take was logged before Give back existed";
            var rec = ReadRecord(t.Record.Path);   // fresh from disk: the latest returned counts
            if (rec == null) return "Couldn't read what was taken. See the log";
            string name = NameOf(rec.SteamId);
            if (rec.Save != SaveName()) return "That take is from a different save";
            if (IsHere(rec.SteamId)) return $"{name} is in the game. Items can only be given back while they're away";
            if (rec.AllReturned) return "Nothing left to give back";
            var folder = PlayersFolder();
            if (folder == null) return "Couldn't find this save's player folders";
            var file = Path.Combine(folder, "Player_" + rec.SteamId, "Inventory.json");
            if (!File.Exists(file)) return $"{name} has no saved inventory in this save";
            var age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds;
            if (age < SettleSeconds) return $"{name} only just left. Try again in {Math.Ceiling(SettleSeconds - age)} s";
            var inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null) return "Your inventory isn't ready";

            var literals = ParseItemsArray(File.ReadAllText(file));
            if (literals.Count == 0) return $"Couldn't read {name}'s saved inventory";

            bool busy = false, shortOf = false;
            var used = new HashSet<int>();
            var pending = new List<Pending>();
            var stock = new Dictionary<string, Stock>();
            foreach (var s in rec.Slots)
            {
                int want = s.Quantity - s.Returned;
                if (want <= 0) continue;
                // Only into the same slot, and only while it's empty there.
                if (s.Index < 0 || s.Index >= literals.Count || used.Contains(s.Index)) { busy = true; continue; }
                var cur = literals[s.Index].Value;
                var idm = IdRx.Match(cur);
                if (!idm.Success || idm.Groups[1].Value.Length != 0 || TypeRx.Match(cur).Groups[1].Value == "CashData") { busy = true; continue; }
                string key = DataKey(s.Literal);
                if (key.Length == 0) { shortOf = true; continue; }

                if (!stock.TryGetValue(key, out var st))
                {
                    st = new Stock { Id = s.Id };
                    Holding(inv, s.Id, key, st.Slots);
                    uint byId = 0;
                    try { byId = inv.GetAmountOfItem(s.Id); } catch { }
                    st.Usable = Math.Min(st.Slots.Sum(x => x.Before), (int)Math.Min(byId, (uint)int.MaxValue));
                    stock[key] = st;
                }
                int n = Math.Min(want, st.Usable - st.Asked);
                if (n <= 0) { shortOf = true; continue; }
                if (n < want) shortOf = true;
                st.Asked += n;
                used.Add(s.Index);
                pending.Add(new Pending { Slot = s, Key = key, Asked = n });
            }

            // Take them out of the host's inventory: one change per slot. If anything fails part
            // way, carry on: the count afterwards decides what goes back, so nothing is lost or made.
            foreach (var st in stock.Values)
            {
                int left = st.Asked;
                foreach (var h in st.Slots)
                {
                    if (left <= 0) break;
                    try
                    {
                        int k = Math.Min(left, h.Before);
                        if (k <= 0) continue;
                        h.Slot.ChangeQuantity(-k);
                        left -= k;
                    }
                    catch (Exception e) { MelonLogger.Warning($"Couldn't take {st.Id} out of your inventory: {e.Message}"); }
                }
            }

            // Cash: never more than what's left of the take, or than the host carries.
            int cashIndex = -1;
            float cashBefore = 0, cashAsked = 0;
            double cashLeft = rec.Cash - rec.CashReturned;
            if (cashLeft >= 0.01)
            {
                CashOf(literals, out cashIndex);
                if (cashIndex < 0 || !CashRx.IsMatch(literals[cashIndex].Value)) { busy = true; cashIndex = -1; }
                else if (inv.cashInstance == null) { shortOf = true; cashIndex = -1; }
                else
                {
                    cashBefore = inv.cashInstance.Balance;
                    cashAsked = (float)(Math.Floor(Math.Min(cashLeft, cashBefore) * 100) / 100);
                    if (cashAsked >= 0.01f)
                    {
                        try { inv.cashInstance.ChangeBalance(-cashAsked); }
                        catch (Exception e) { MelonLogger.Warning($"Couldn't take the cash out of your inventory: {e.Message}"); }
                    }
                    else { cashAsked = 0; cashIndex = -1; }
                    if (cashAsked < cashLeft - 0.005) shortOf = true;
                }
            }

            if (pending.Count == 0 && cashIndex < 0)
                return shortOf && !busy ? $"You don't have any of what you took from {name}"
                    : busy && !shortOf ? $"{name}'s slots for those items aren't empty any more. Nothing given back"
                    : $"Nothing given back to {name}. You don't have the items, or their slots are in use";

            _givingBack = true;
            MelonCoroutines.Start(FinishGiveBack(rec, file, name, inv, pending, stock, cashIndex, cashBefore, cashAsked, busy, shortOf, done));
            return $"Giving back to {name}...";
        }
        catch (Exception e)
        {
            MelonLogger.Error($"Give back failed: {e}");
            return "Couldn't give the items back. See the log";
        }
    }

    // The game can apply an inventory change a moment after it's asked for, so wait, then
    // count what really left the host's inventory and write only that into the player's file.
    private static IEnumerator FinishGiveBack(TakeRecord rec, string file, string name, PlayerInventory inv, List<Pending> pending,
        Dictionary<string, Stock> stock, int cashIndex, float cashBefore, float cashAsked, bool busy, bool shortOf, Action<string> done)
    {
        yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
        string msg;
        try { msg = WriteGiveBack(rec, file, name, inv, pending, stock, cashIndex, cashBefore, cashAsked, busy, shortOf); }
        catch (Exception e)
        {
            MelonLogger.Error($"Give back failed after taking from your inventory: {e}");
            msg = "Give back failed. See the log";
        }
        _givingBack = false;
        Invalidate();
        try { done(msg); } catch { }
    }

    private static string WriteGiveBack(TakeRecord rec, string file, string name, PlayerInventory inv, List<Pending> pending,
        Dictionary<string, Stock> stock, int cashIndex, float cashBefore, float cashAsked, bool busy, bool shortOf)
    {
        // What actually left the host's inventory per item, measured on the very slots that were
        // changed (so other stacks, locked or added meanwhile, can't skew it), never more than asked.
        var gone = new Dictionary<string, int>();
        foreach (var kv in stock)
            gone[kv.Key] = Math.Min(kv.Value.Asked, kv.Value.Slots.Sum(h => h.Dropped(kv.Value.Id)));
        var moved = new List<(Pending p, int n)>();
        foreach (var p in pending)
        {
            int n = Math.Min(p.Asked, gone[p.Key]);
            gone[p.Key] -= n;
            if (n < p.Asked) shortOf = true;
            if (n > 0) moved.Add((p, n));
        }
        float cashMoved = 0;
        if (cashIndex >= 0 && inv.cashInstance != null)
            cashMoved = (float)(Math.Floor(Math.Min(cashAsked, cashBefore - inv.cashInstance.Balance) * 100) / 100);

        // They came back while we waited: their game has the file already, so put it all back on the host.
        if (IsHere(rec.SteamId) || !File.Exists(file))
        {
            foreach (var (p, n) in moved) Restore(inv, p.Slot, n);
            if (cashMoved >= 0.01f) inv.cashInstance?.ChangeBalance(cashMoved);
            return File.Exists(file) ? $"{name} rejoined while giving back. Nothing was given back" : $"{name}'s saved inventory is gone. Nothing was given back";
        }

        if (moved.Count == 0 && cashMoved < 0.01f) return $"Nothing given back to {name}. Your inventory didn't change";

        string text = File.ReadAllText(file);
        var literals = ParseItemsArray(text);
        // Read again: a slot that filled up meanwhile gets nothing, and the host keeps those items.
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            int at = moved[i].p.Slot.Index;
            var idm = at < literals.Count ? IdRx.Match(literals[at].Value) : null;
            if (idm != null && idm.Success && idm.Groups[1].Value.Length == 0) continue;
            Restore(inv, moved[i].p.Slot, moved[i].n);
            moved.RemoveAt(i);
            busy = true;
        }
        if (cashIndex >= literals.Count || (cashIndex >= 0 && !CashRx.IsMatch(literals[cashIndex].Value)))
        {
            if (cashMoved >= 0.01f) inv.cashInstance?.ChangeBalance(cashMoved);
            cashMoved = 0;
            busy = true;
        }
        if (moved.Count == 0 && cashMoved < 0.01f) return $"Nothing given back to {name}. Their slots aren't empty any more";
        var replace = new Dictionary<int, string>();
        var given = new List<string>();
        foreach (var (p, n) in moved)
        {
            // The taken slot, copied as it was; only the count changes.
            replace[p.Slot.Index] = MiniJson.Quote(QtyRx.Replace(p.Slot.Literal, "\"Quantity\":" + n, 1));
            p.Slot.Returned += n;
            ItemInstance? item = null;
            try { item = ItemDeserializer.LoadItem(p.Slot.Literal); } catch { }
            given.Add($"{n}x {SafeName(item, p.Slot.Id)}");
        }
        if (cashMoved >= 0.01f)
        {
            var lit = literals[cashIndex].Value;
            var m = CashRx.Match(lit);
            double cur = m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            string total = MiniJson.Number(Math.Round(cur + cashMoved, 2));
            if (total.IndexOf('.') < 0 && total.IndexOf('E') < 0) total += ".0";
            replace[cashIndex] = MiniJson.Quote(CashRx.Replace(lit, "\"CashBalance\":" + total, 1));
            rec.CashReturned += cashMoved;
            given.Add($"${cashMoved:N0}");
        }

        // Copy of their file as it is now, then the change, then the record.
        Directory.CreateDirectory(TakenDir);
        File.Copy(file, Path.Combine(TakenDir, $"{rec.SteamId}_{Stamp()}_BeforeGiveBack_Inventory.json"), true);
        File.WriteAllText(file, Splice(text, literals, replace));
        WriteRecord(rec);

        string what = string.Join(", ", given);
        Utils.Log.Info($"Gave back to {name} ({rec.SteamId}) from the take at {rec.Utc:u}: {what}");
        AppendLog(rec.SteamId, name, "Gave back " + what);
        if (shortOf) return $"Gave back {what} to {name}. You don't have the rest";
        if (busy) return $"Gave back {what} to {name}. Some of their slots aren't empty any more";
        return $"Gave back {what} to {name}";
    }

    // Puts back on the host exactly what this give back just took off them.
    private static void Restore(PlayerInventory inv, TakenSlot s, int n)
    {
        ItemInstance? item = null;
        try { item = ItemDeserializer.LoadItem(QtyRx.Replace(s.Literal, "\"Quantity\":" + n, 1)); } catch { }
        if (item != null) inv.AddItemToInventory(item);
        else MelonLogger.Warning($"Couldn't put {n}x {s.Id} back in your inventory");
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
