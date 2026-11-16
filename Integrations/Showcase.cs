using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace BiggerLobbies.Integrations;

// Screenshot mode (DebugShowcase): fills Host settings with made-up players so every list can
// be shown off without a real crowd. The cast is random NPCs from the save you're in, with their
// names and portraits, so load a save first (the main menu has no NPCs). Showcase players have
// IDs no Steam account can have, and every button on their rows does nothing.
public static class Showcase
{
    private static MelonPreferences_Entry<bool>? _entry;
    public static bool On => _entry?.Value ?? false;

    // Real Steam IDs start at 76561197960265728; these can't be confused with one.
    private const ulong FakeBase = 900_000;
    private const int CastSize = 24;
    public static bool IsFake(ulong id) => id >= FakeBase && id < FakeBase + CastSize;

    private sealed class Person { public string Name = ""; public Sprite? Face; }
    private static List<Person>? _cast, _fallback;
    private static float _nextLook;
    private static readonly System.Random _rng = new();

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("DebugShowcase", false, "Debug: showcase players",
            "Testing only. Fills Host settings with made-up players (random NPCs from your save, with their portraits) for screenshots. Their buttons do nothing. Leave off.");
        if (On) MelonLogger.Warning("DebugShowcase is on: Host settings shows made-up players for screenshots. Load a save so they get NPC portraits.");
    }

    // --- Cast ---------------------------------------------------------------------------------

    private static readonly string[] FallbackNames =
        { "Benji", "Kyle Cooley", "Jessi", "Doris", "Chloe", "Mac", "Jeff", "Kathy", "Hank", "Lucy", "Geraldine", "Dean" };

    private static List<Person> Cast()
    {
        if (_cast != null) return _cast;
        // Scanning the NPCs costs a little: at most every few seconds until a save is loaded.
        if (_fallback != null && Time.unscaledTime < _nextLook) return _fallback;
        _nextLook = Time.unscaledTime + 5f;
        var people = new List<Person>();
        try
        {
            var reg = S1.NPCs.NPCManager.NPCRegistry;
            if (reg != null)
                for (int i = 0; i < reg.Count; i++)
                {
                    var npc = reg[i];
                    if (npc == null) continue;
                    Sprite? face = null;
                    try { face = npc.MugshotSprite; } catch { }
                    string name = $"{npc.FirstName} {npc.LastName}".Trim();
                    if (face == null || name.Length == 0) continue;
                    people.Add(new Person { Name = name, Face = face });
                }
        }
        catch { }
        if (people.Count < CastSize)
        {
            // No save loaded yet: names only, and look for NPCs again next time.
            if (_fallback == null)
            {
                _fallback = new List<Person>();
                for (int i = 0; i < CastSize; i++) _fallback.Add(new Person { Name = FallbackNames[i % FallbackNames.Length] + (i >= FallbackNames.Length ? $" {i / FallbackNames.Length + 1}" : "") });
            }
            return _fallback;
        }
        for (int i = people.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (people[i], people[j]) = (people[j], people[i]); }
        _cast = people.GetRange(0, CastSize);
        Utils.Log.Info($"Showcase: cast of {CastSize} picked from {people.Count} NPCs");
        return _cast;
    }

    private static ulong Id(int i) => FakeBase + (ulong)i;
    private static string Name(int i) => Cast()[i].Name;

    /// <summary>The NPC portrait for a showcase player.</summary>
    public static bool TryFace(ulong id, out Sprite face)
    {
        face = null!;
        if (!On || !IsFake(id)) return false;
        var c = Cast();
        var f = c[(int)(id - FakeBase)].Face;
        if (f == null) return false;
        face = f;
        return true;
    }

    /// <summary>A mix of presence for showcase players: some playing, some online, some offline.</summary>
    public static bool TryPresence(ulong id, out Friends.Presence presence)
    {
        presence = default;
        if (!On || !IsFake(id)) return false;
        int i = (int)(id - FakeBase);
        presence = (i % 3) switch { 0 => Friends.Presence.Playing, 1 => Friends.Presence.Online, _ => Friends.Presence.Offline };
        return true;
    }

    /// <summary>The portrait for the n-th placeholder slot on the lobby screen (DebugFakePlayers).</summary>
    public static bool TrySlotFace(int n, out Sprite face)
    {
        face = null!;
        if (!On) return false;
        var f = Cast()[n % CastSize].Face;
        if (f == null) return false;
        face = f;
        return true;
    }

    // --- Lists (cast 0-7 in the game, 8-9 left, 10-12 taken from, 13-17 whitelist, 18-19 banned, 20-23 crews) ---

    public static void AddPlayers(List<HostPlayers.Row> rows)
    {
        if (!On) return;
        float t = Time.unscaledTime;
        for (int i = 0; i < 8; i++)
            rows.Add(new HostPlayers.Row
            {
                Name = Name(i),
                SteamId = Id(i),
                ClientId = -2000 - i,
                Ping = 28 + (i * 41) % 110 + (int)(Mathf.PerlinNoise(i * 0.7f, t * 0.3f) * 18),
                Fake = true,
            });
    }

    /// <summary>A few showcase players also have BiggerLobbies: one is a version behind, one misses a mod.</summary>
    public static bool TryHello(ulong id, out ModHandshake.Hello hello)
    {
        hello = null!;
        if (!On || !IsFake(id)) return false;
        int i = (int)(id - FakeBase);
        if (i == 1) hello = new ModHandshake.Hello { Version = Utils.Constants.MOD_VERSION };
        else if (i == 3) hello = new ModHandshake.Hello { Version = "2.0.0" };
        else if (i == 5) hello = new ModHandshake.Hello { Version = Utils.Constants.MOD_VERSION, Missing = new List<string> { "ItemSafety" } };
        return hello != null;
    }

    private static DepartedPlayers.Slot Item(int index, string id, int qty)
    {
        string type = id == "m1911" ? "IntegerItemData" : "ItemData";
        string extra = id == "m1911" ? ",\"Value\":7" : "";
        string json = $"{{\"DataType\":\"{type}\",\"DataVersion\":0,\"GameVersion\":\"{Application.version}\",\"ID\":\"{id}\",\"Quantity\":{qty}{extra}}}";
        ItemInstance? item = null;
        try { item = ItemDeserializer.LoadItem(json); } catch { }
        return new DepartedPlayers.Slot { Index = index, Json = json, Id = id, Quantity = qty, Item = item };
    }

    // Made once per cast, so the lists' keys (which include times and items) stay the same
    // between refreshes and the rows aren't rebuilt every second.
    private static object? _builtFor;
    private static List<DepartedPlayers.Info>? _departed;
    private static List<DepartedPlayers.Taken>? _taken;

    private static void Rebuild()
    {
        var cast = Cast();
        if (_builtFor == cast) return;
        _builtFor = cast;
        _departed = new List<DepartedPlayers.Info>();
        BuildDeparted(_departed);
        _taken = new List<DepartedPlayers.Taken>();
        BuildTaken(_taken);
    }

    public static void AddDeparted(List<DepartedPlayers.Info> list)
    {
        if (!On) return;
        Rebuild();
        list.AddRange(_departed!);
    }

    public static void AddTaken(List<DepartedPlayers.Taken> list)
    {
        if (!On) return;
        Rebuild();
        list.AddRange(_taken!);
        list.Sort((a, b) => b.Utc.CompareTo(a.Utc));
    }

    private static void BuildDeparted(List<DepartedPlayers.Info> list)
    {
        var now = DateTime.UtcNow;
        list.Add(new DepartedPlayers.Info
        {
            SteamId = Id(8), Name = Name(8), SavedUtc = now.AddMinutes(-3), Cash = 1840,
            Items = new List<DepartedPlayers.Slot> { Item(0, "jar", 12), Item(1, "baggie", 20), Item(2, "m1911", 1), Item(3, "trimmers", 1) },
        });
        list.Add(new DepartedPlayers.Info
        {
            SteamId = Id(9), Name = Name(9), SavedUtc = now.AddMinutes(-26), Cash = 325,
            Items = new List<DepartedPlayers.Slot> { Item(0, "cuke", 4), Item(1, "energydrink", 2), Item(2, "paracetamol", 3) },
        });
    }

    private static void BuildTaken(List<DepartedPlayers.Taken> list)
    {
        var now = DateTime.UtcNow;
        list.Add(new DepartedPlayers.Taken
        {
            Utc = now.AddMinutes(-8), SteamId = Id(10), Name = Name(10),
            Summary = "12x Jar, 20x Baggie, 1x M1911, 1x Trimmers, $2,450",
            Record = new DepartedPlayers.TakeRecord { SteamId = Id(10), Slots = new List<DepartedPlayers.TakenSlot> { new() { Id = "jar", Quantity = 12 } }, Cash = 2450 },
        });
        list.Add(new DepartedPlayers.Taken { Utc = now.AddMinutes(-41), SteamId = Id(11), Name = Name(11), Summary = "Gave back 4x Cuke, 2x Energy Drink", GiveBack = true });
        list.Add(new DepartedPlayers.Taken { Utc = now.AddHours(-2), SteamId = Id(12), Name = Name(12), Summary = "6x Viagor, 3x Paracetamol, $780" });
    }

    public static void AddWhitelist(List<BanList.Entry> list)
    {
        if (!On) return;
        for (int i = 13; i < 18; i++) list.Add(new BanList.Entry(Id(i), Name(i)));
    }

    public static void AddBans(List<BanList.Entry> list)
    {
        if (!On) return;
        list.Add(new BanList.Entry(Id(18), Name(18), DateTime.UtcNow.AddHours(20), "Burned down the motel"));
        list.Add(new BanList.Entry(Id(19), Name(19)));
    }

    public static void AddCrews(List<Crews.Crew> list)
    {
        if (!On) return;
        list.Add(new Crews.Crew { Name = "Friday crew", Members = new List<BanList.Entry> { new(Id(20), Name(20)), new(Id(21), Name(21)), new(Id(13), Name(13)), new(Id(14), Name(14)), new(Id(15), Name(15)) } });
        list.Add(new Crews.Crew { Name = "Grow team", Members = new List<BanList.Entry> { new(Id(22), Name(22)), new(Id(23), Name(23)) } });
    }

    /// <summary>Status line for any button pressed on a showcase row.</summary>
    public static string Inert(string name) => $"{name} is a showcase player. Nothing happens";
}
