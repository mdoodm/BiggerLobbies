#if !MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BiggerLobbies.Utils;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine.SceneManagement;

namespace BiggerLobbies.Integrations;

// What each player left with, compared with what their save file holds now.
//
// No extra copies while they play: what they left with is their own last save, the exit save
// when they quit, or the last player save before a crash. Ten seconds after they leave (once
// the exit save has landed) their Inventory.json and Clothing.json are copied once into
// UserData/BiggerLobbies/LeftWith/<save>/<steamid>.json. If you took from them before that,
// the take's own copy of their file from before the take is used instead.
//
// Expected = what they left with, minus every take since they left (net of what was given
// back). Host settings shows any difference from their file. When they rejoin, just before
// the game reads their file, a file that differs is put back to the expected one (a copy of
// the replaced file goes to PlayerBackups first), so they spawn with exactly what they left with.
public static partial class DepartedPlayers
{
    private sealed class LeftRecord
    {
        public string Path = "", Save = "", Name = "", Inventory = "", Clothing = "";
        public ulong SteamId;
        public DateTime Utc;   // when they left
    }

    /// <summary>What their file should hold, and how it differs from what it does hold.</summary>
    public sealed class LeftCheck
    {
        public DateTime LeftUtc;
        public List<string> Missing = new(), Extra = new(), Taken = new();
        public bool Matches => Missing.Count == 0 && Extra.Count == 0;
        public bool CanRestore;   // false when a take doesn't line up with what they left with
        internal string ExpectedInventory = "", ExpectedClothing = "";
    }

    private sealed class Departure { public ulong Id; public string Name = ""; public DateTime Utc; public float At; }

    private static MelonPreferences_Entry<bool>? _restoreEntry;
    private static readonly Dictionary<string, string> _seen = new();   // loaded players: code -> name
    private static readonly List<Departure> _pending = new();
    private static float _nextScan;
    private static bool _inMain;

    public static void InitLeftWith(MelonPreferences_Category cat)
    {
        _restoreEntry = cat.CreateEntry("RestoreLeftWith", true, "Put back what players left with",
            "As host, when a player rejoins and their saved items or clothes differ from what they left with (minus anything you took), put their file back first so they spawn with exactly that.");
    }

    public static bool RestoreLeftWith
    {
        get => _restoreEntry?.Value ?? false;
        set { if (_restoreEntry != null && _restoreEntry.Value != value) { _restoreEntry.Value = value; MelonPreferences.Save(); } }
    }

    private static string LeftDir(string save) => Path.Combine(MelonEnvironment.UserDataDirectory, "BiggerLobbies", "LeftWith", save);

    private static bool SafeCode(string code) => code.Length > 0 && code.All(char.IsDigit) && code != "0";

    // --- Who's here: player codes only, every 2 s. Nothing is serialized or written. ---------

    public static void UpdateLeftWith()
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now < _nextScan) return;
        _nextScan = now + 2f;
        try
        {
            bool hosting = SceneManager.GetActiveScene().name == "Main" && InstanceFinder.IsServer;
            if (!hosting)
            {
                // Out of the game or not hosting: nobody "left" the session.
                if (_inMain) { _seen.Clear(); _pending.Clear(); }
                _inMain = false;
                return;
            }
            _inMain = true;

            var list = Player.PlayerList;
            if (list == null) return;
            var count = new Dictionary<string, int>();
            var loaded = new Dictionary<string, string>();
            string host = BanList.HostSteamId().ToString();
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == null || p.IsLocalPlayer) continue;
                string code = p.PlayerCode ?? "";
                if (!SafeCode(code) || code == host) continue;
                count[code] = count.TryGetValue(code, out var n) ? n + 1 : 1;
                if (p.PlayerLoaded) loaded[code] = (p.PlayerName ?? "").Trim();
            }

            foreach (var kv in loaded)
            {
                // Two copies of one player (a rejoin while the old connection hangs): wait.
                if (count[kv.Key] != 1) continue;
                if (_seen.ContainsKey(kv.Key)) { _seen[kv.Key] = kv.Value; continue; }
                // Back and loaded: what they left with last time has been used.
                _seen[kv.Key] = kv.Value;
                _pending.RemoveAll(x => x.Id.ToString() == kv.Key);
                try { var f = Path.Combine(LeftDir(SaveName()), kv.Key + ".json"); if (File.Exists(f)) File.Delete(f); } catch { }
            }

            foreach (var code in _seen.Keys.ToList())
            {
                if (count.ContainsKey(code)) continue;
                string name = _seen[code];
                _seen.Remove(code);
                if (ulong.TryParse(code, out var id))
                    _pending.Add(new Departure { Id = id, Name = name, Utc = DateTime.UtcNow, At = now + (float)SettleSeconds });
            }

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pd = _pending[i];
                if (now < pd.At || IsHere(pd.Id)) continue;   // back already: the rejoin check records it
                _pending.RemoveAt(i);
                Record(pd);
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't keep track of who left: {e.Message}");
            _nextScan = now + 30f;
        }
    }

    // One copy of their saved files, made once when they leave.
    private static void Record(Departure pd)
    {
        try
        {
            var folder = PlayersFolder();
            if (folder == null) return;
            var dir = Path.Combine(folder, "Player_" + pd.Id);
            var invFile = Path.Combine(dir, "Inventory.json");
            if (!File.Exists(invFile)) return;
            var rec = new LeftRecord { Save = SaveName(), SteamId = pd.Id, Name = pd.Name, Utc = pd.Utc };
            // Already taken from: their file has changed, so use the take's copy from before it.
            var first = TakesSince(rec).FirstOrDefault();
            var before = first != null && first.Backup.Length > 0 ? Path.Combine(TakenDir, Path.GetFileName(first.Backup)) : "";
            rec.Inventory = before.Length > 0 && File.Exists(before) ? File.ReadAllText(before) : File.ReadAllText(invFile);
            var cl = Path.Combine(dir, "Clothing.json");
            rec.Clothing = File.Exists(cl) ? File.ReadAllText(cl) : "";
            Directory.CreateDirectory(LeftDir(rec.Save));
            rec.Path = Path.Combine(LeftDir(rec.Save), pd.Id + ".json");
            WriteLeft(rec);
            Utils.Log.Info($"{NameOf(pd.Id)} left; kept a copy of what they left with");
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't keep what {NameOf(pd.Id)} left with: {e.Message}"); }
    }

    // --- Records on disk -------------------------------------------------------------------

    private static void WriteLeft(LeftRecord r)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"version\": 1,\n");
        sb.Append("  \"save\": ").Append(MiniJson.Quote(r.Save)).Append(",\n");
        sb.Append("  \"steamId\": ").Append(MiniJson.Quote(r.SteamId.ToString())).Append(",\n");
        sb.Append("  \"name\": ").Append(MiniJson.Quote(r.Name)).Append(",\n");
        sb.Append("  \"utc\": ").Append(MiniJson.Quote(r.Utc.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
        sb.Append("  \"inventory\": ").Append(MiniJson.Quote(r.Inventory)).Append(",\n");
        sb.Append("  \"clothing\": ").Append(MiniJson.Quote(r.Clothing)).Append('\n');
        sb.Append("}\n");
        File.WriteAllText(r.Path, sb.ToString());
    }

    private static LeftRecord? ReadLeft(ulong id)
    {
        try
        {
            var path = Path.Combine(LeftDir(SaveName()), id + ".json");
            if (!File.Exists(path)) return null;
            if (MiniJson.Parse(File.ReadAllText(path)) is not Dictionary<string, object?> o) return null;
            if (MiniJson.Str(o, "steamId") != id.ToString()) return null;
            DateTime.TryParse(MiniJson.Str(o, "utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc);
            var r = new LeftRecord
            {
                Path = path, Save = MiniJson.Str(o, "save"), SteamId = id, Name = MiniJson.Str(o, "name"),
                Utc = utc.ToUniversalTime(), Inventory = MiniJson.Str(o, "inventory"), Clothing = MiniJson.Str(o, "clothing"),
            };
            return r.Inventory.Length == 0 ? null : r;
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't read what {NameOf(id)} left with: {e.Message}");
            return null;
        }
    }

    // Takes from this player since they left, oldest first, read fresh from disk.
    private static List<TakeRecord> TakesSince(LeftRecord rec)
    {
        var list = new List<TakeRecord>();
        try
        {
            if (!Directory.Exists(TakenDir)) return list;
            foreach (var f in Directory.GetFiles(TakenDir, rec.SteamId + "_*.take.json"))
            {
                var t = ReadRecord(f);
                if (t != null && t.SteamId == rec.SteamId && t.Save == rec.Save && t.Utc > rec.Utc) list.Add(t);
            }
        }
        catch { }
        return list.OrderBy(t => t.Utc).ToList();
    }

    // --- Comparing -------------------------------------------------------------------------

    /// <summary>What this player left with against their save file now, or null if there's no record.</summary>
    public static LeftCheck? Compare(ulong id)
    {
        try
        {
            var rec = ReadLeft(id);
            var folder = PlayersFolder();
            if (rec == null || folder == null) return null;
            var dir = Path.Combine(folder, "Player_" + id);
            var c = new LeftCheck { LeftUtc = rec.Utc, CanRestore = true };

            // Inventory: what they left with, with each take since applied to it.
            string expInv = rec.Inventory;
            var lits = ParseItemsArray(expInv);
            var replace = new Dictionary<int, string>();
            var cur = lits.Select(l => l.Value).ToList();
            string empty = lits.Count > 0 ? EmptySlot(lits[0].Value) : "";
            double takenCash = 0;
            foreach (var t in TakesSince(rec))
            {
                foreach (var s in t.Slots)
                {
                    int net = s.Quantity - s.Returned;
                    if (net <= 0) continue;
                    c.Taken.Add($"{net}x {ItemName(s.Literal, s.Id)}");
                    if (s.Index < 0 || s.Index >= cur.Count || DataKey(cur[s.Index]) != DataKey(s.Literal)) { c.CanRestore = false; continue; }
                    int.TryParse(QtyRx.Match(cur[s.Index]).Groups[1].Value, out int have);
                    int left = have - net;
                    if (left < 0) { c.CanRestore = false; left = 0; }
                    cur[s.Index] = left == 0 ? empty : QtyRx.Replace(cur[s.Index], "\"Quantity\":" + left, 1);
                    replace[s.Index] = MiniJson.Quote(cur[s.Index]);
                }
                double cashNet = t.Cash - t.CashReturned;
                if (cashNet >= 0.01) { takenCash += cashNet; c.Taken.Add($"${cashNet:N0} cash"); }
            }
            if (takenCash >= 0.01)
            {
                int ci = -1;
                for (int i = 0; i < cur.Count; i++) if (TypeRx.Match(cur[i]).Groups[1].Value == "CashData") { ci = i; break; }
                var m = ci >= 0 ? CashRx.Match(cur[ci]) : null;
                if (m == null || !m.Success) c.CanRestore = false;
                else
                {
                    double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var had);
                    double now = Math.Round(had - takenCash, 2);
                    if (now < -0.01) c.CanRestore = false;
                    string total = MiniJson.Number(Math.Max(0, now));
                    if (total.IndexOf('.') < 0 && total.IndexOf('E') < 0) total += ".0";
                    cur[ci] = CashRx.Replace(cur[ci], "\"CashBalance\":" + total, 1);
                    replace[ci] = MiniJson.Quote(cur[ci]);
                }
            }
            c.ExpectedInventory = replace.Count > 0 ? Splice(expInv, lits, replace) : expInv;
            c.ExpectedClothing = rec.Clothing;

            var invFile = Path.Combine(dir, "Inventory.json");
            var clFile = Path.Combine(dir, "Clothing.json");
            Diff(cur, File.Exists(invFile) ? ParseItemsArray(File.ReadAllText(invFile)).Select(l => l.Value).ToList() : new List<string>(), c, "");
            if (rec.Clothing.Length > 0)
                Diff(ParseItemsArray(rec.Clothing).Select(l => l.Value).ToList(),
                     File.Exists(clFile) ? ParseItemsArray(File.ReadAllText(clFile)).Select(l => l.Value).ToList() : new List<string>(), c, " (worn)");
            return c;
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Couldn't compare what {NameOf(id)} left with: {e.Message}");
            return null;
        }
    }

    // Counts each kind of item (same data, any slot) and cash on both sides.
    private static void Diff(List<string> expected, List<string> actual, LeftCheck c, string suffix)
    {
        var want = Tally(expected, out double wantCash);
        var have = Tally(actual, out double haveCash);
        foreach (var k in want.Keys.Union(have.Keys))
        {
            want.TryGetValue(k, out var w); have.TryGetValue(k, out var h);
            int d = (w.n) - (h.n);
            string name = ItemName(w.json ?? h.json ?? "", k);
            if (d > 0) c.Missing.Add($"{d}x {name}{suffix}");
            else if (d < 0) c.Extra.Add($"{-d}x {name}{suffix}");
        }
        double dc = Math.Round(wantCash - haveCash, 2);
        if (dc >= 0.01) c.Missing.Add($"${dc:N0} cash");
        else if (dc <= -0.01) c.Extra.Add($"${-dc:N0} cash");
    }

    private static Dictionary<string, (int n, string? json)> Tally(List<string> slots, out double cash)
    {
        cash = 0;
        var d = new Dictionary<string, (int n, string? json)>();
        foreach (var s in slots)
        {
            var type = TypeRx.Match(s).Groups[1].Value;
            if (type == "CashData")
            {
                var m = CashRx.Match(s);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) cash += v;
                continue;
            }
            var id = IdRx.Match(s).Groups[1].Value;
            int.TryParse(QtyRx.Match(s).Groups[1].Value, out int q);
            if (id.Length == 0 || q <= 0) continue;
            var key = DataKey(s);
            if (key.Length == 0) key = id;
            d.TryGetValue(key, out var e);
            d[key] = (e.n + q, e.json ?? s);
        }
        return d;
    }

    private static string ItemName(string json, string fallback)
    {
        ItemInstance? item = null;
        try { if (json.Length > 0) item = ItemDeserializer.LoadItem(json); } catch { }
        var id = IdRx.Match(json).Groups[1].Value;
        return SafeName(item, id.Length > 0 ? id : fallback);
    }

    /// <summary>One line for the Host settings row.</summary>
    public static string CompareLine(LeftCheck c)
    {
        string taken = c.Taken.Count > 0 ? $"You took {string.Join(", ", c.Taken)}. " : "";
        if (c.Matches) return taken + (taken.Length > 0 ? "The rest matches what they left with" : "Their save matches what they left with");
        var parts = new List<string>();
        if (c.Missing.Count > 0) parts.Add("missing " + string.Join(", ", c.Missing));
        if (c.Extra.Count > 0) parts.Add("extra " + string.Join(", ", c.Extra));
        string fix = !c.CanRestore ? " Can't put it back: a take doesn't match what they left with"
            : RestoreLeftWith ? " It'll be put back when they rejoin" : "";
        return taken + "Their save differs from what they left with: " + string.Join("; ", parts) + "." + fix;
    }

    // --- Putting it back on rejoin -----------------------------------------------------------

    // Runs on the host just before the game reads a joining player's files.
    [HarmonyPatch(typeof(Player), nameof(Player.RpcLogic___RequestPlayerData_Server_310431262))]
    private static class RequestPlayerDataPatch
    {
        private static void Prefix(string playerCode, bool isHost)
        {
            try
            {
                if (isHost || !InstanceFinder.IsServer || !RestoreLeftWith) return;
                if (!SafeCode(playerCode ?? "") || !ulong.TryParse(playerCode, out var id)) return;
                // Left less than 10 s ago: their exit save is in, so record it now.
                for (int i = _pending.Count - 1; i >= 0; i--)
                    if (_pending[i].Id == id) { var pd = _pending[i]; _pending.RemoveAt(i); Record(pd); }
                PutBack(id);
            }
            catch (Exception e) { MelonLogger.Warning($"Couldn't check what a rejoining player left with: {e.Message}"); }
        }
    }

    private static void PutBack(ulong id)
    {
        var c = Compare(id);
        if (c == null || c.Matches) return;
        string name = NameOf(id);
        if (!c.CanRestore)
        {
            MelonLogger.Warning($"{name}'s save differs from what they left with, but a take doesn't line up, so it wasn't changed: {CompareLine(c)}");
            return;
        }
        var folder = PlayersFolder();
        if (folder == null) return;
        var dir = Path.Combine(folder, "Player_" + id);
        if (!Directory.Exists(dir)) return;
        AutoSave.BackUp(id.ToString(), force: true);
        File.WriteAllText(Path.Combine(dir, "Inventory.json"), c.ExpectedInventory);
        if (c.ExpectedClothing.Length > 0) File.WriteAllText(Path.Combine(dir, "Clothing.json"), c.ExpectedClothing);
        Invalidate();
        string what = string.Join("; ", new[] { c.Missing.Count > 0 ? "gave back " + string.Join(", ", c.Missing) : "", c.Extra.Count > 0 ? "removed " + string.Join(", ", c.Extra) : "" }.Where(s => s.Length > 0));
        MelonLogger.Msg($"{name} rejoined with a save that differed from what they left with; put it back ({what}). The replaced files are in UserData/BiggerLobbies/PlayerBackups");
    }
}

#endif
