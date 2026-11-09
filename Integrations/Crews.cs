#if !MONO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Saved crews: named whitelists the host can load in one click. The whitelist itself
// still starts empty every game; loading a crew fills it and turns it on. Saved as
//   SavedCrews = "Friday crew=76561198000000001 (Alex)|76561198000000002 (Sam); Weekend=..."
public static class Crews
{
    public sealed class Crew
    {
        public string Name = "";
        public List<BanList.Entry> Members = new();
    }

    private static MelonPreferences_Entry<string>? _entry;
    private static readonly List<Crew> _crews = new();
    public const int MaxCrews = 12;

    public static IReadOnlyList<Crew> All => _crews;

    public static void Init(MelonPreferences_Category cat)
    {
        _entry = cat.CreateEntry("SavedCrews", "", "Saved crews",
            "Named whitelists you can load in Host settings. Easiest to manage there.");
        Parse(_entry.Value);
    }

    private static void Parse(string? raw)
    {
        _crews.Clear();
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var part in raw.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var crew = new Crew { Name = CleanName(part.Substring(0, eq)) };
            if (crew.Name.Length == 0) continue;
            foreach (var m in part.Substring(eq + 1).Split('|'))
            {
                var s = m.Trim();
                int i = 0;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i == 0 || !ulong.TryParse(s.Substring(0, i), out ulong id) || id == 0) continue;
                string name = s.Substring(i).Trim().Trim('(', ')').Trim();
                if (crew.Members.All(e => e.SteamId != id)) crew.Members.Add(new BanList.Entry(id, name));
            }
            if (_crews.All(c => !c.Name.Equals(crew.Name, StringComparison.OrdinalIgnoreCase))) _crews.Add(crew);
        }
    }

    private static void Store()
    {
        if (_entry == null) return;
        var sb = new StringBuilder();
        foreach (var c in _crews)
        {
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(c.Name).Append('=');
            sb.Append(string.Join("|", c.Members.Select(m => $"{m.SteamId} ({CleanMember(m.Name)})")));
        }
        _entry.Value = sb.ToString();
        MelonPreferences.Save();
    }

    public static string CleanName(string? s) =>
        new string((s ?? "").Where(c => c != ';' && c != '=' && c != '|' && c != ',' && !char.IsControl(c)).ToArray()).Trim();

    private static string CleanMember(string? s) =>
        new string((s ?? "").Where(c => c != ';' && c != '=' && c != '|' && c != '(' && c != ')' && !char.IsControl(c)).ToArray()).Trim();

    /// <summary>Saves the current whitelist under this name, replacing a crew with the same name.</summary>
    public static string SaveCurrent(string name)
    {
        name = CleanName(name);
        if (name.Length == 0) return "Type a name for the crew first";
        var members = Whitelist.Players.ToList();
        if (members.Count == 0) return "Add players to the whitelist first";
        int existing = _crews.FindIndex(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing < 0 && _crews.Count >= MaxCrews) return $"You can keep up to {MaxCrews} crews. Delete one first";
        var crew = new Crew { Name = name, Members = members };
        if (existing >= 0) _crews[existing] = crew; else _crews.Add(crew);
        Store();
        return existing >= 0 ? $"Updated {name} ({members.Count})" : $"Saved {name} ({members.Count})";
    }

    /// <summary>Replaces the whitelist with this crew and turns the whitelist on.</summary>
    public static string Load(string name)
    {
        var crew = _crews.FirstOrDefault(c => c.Name == name);
        if (crew == null) return "That crew no longer exists";
        int added = Whitelist.Replace(crew.Members);
        Whitelist.SetEnabled(true);
        int skipped = crew.Members.Count - added;
        return skipped > 0
            ? $"Loaded {name}: {added} on the whitelist, {skipped} banned and left off"
            : $"Loaded {name}: whitelist on with {added}";
    }

    public static void Delete(string name)
    {
        if (_crews.RemoveAll(c => c.Name == name) > 0) Store();
    }
}

#endif
