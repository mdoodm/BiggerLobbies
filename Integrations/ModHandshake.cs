#if !MONO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Networking;
using Il2CppSteamworks;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Which BiggerLobbies version and which mods each player has, so the host can spot a
// player who's missing a content mod (the usual cause of things looking different for
// them, or desyncing).
//
// Client with this mod: on entering someone else's Steam lobby, post one line in the
// lobby chat: "bl1|hello|<steam id>|<version>|<mod,mod,...>". Vanilla games ignore it.
// Host: keep each member's hello for the session and compare their mods with its own,
// skipping mods that only change what one player sees (built-in list plus IgnoredModNames).
[HarmonyPatch]
public static class ModHandshake
{
    private const string Prefix = "bl1|hello|";
    private const int MaxBytes = 3000;   // Steam lobby chat messages are capped at 4 KB

    public sealed class Hello
    {
        public string Version = "";
        public HashSet<string> Mods = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Missing = new(), Extra = new();   // compared with the host's mods
    }

    private static MelonPreferences_Entry<string>? _ignored;
    private static readonly Dictionary<ulong, Hello> _hellos = new();
    private static readonly HashSet<ulong> _warned = new();
    private static readonly string[] IgnoredBuiltIn = { "BiggerLobbies", "MLVScan" };

    public static void Init(MelonPreferences_Category cat)
    {
        _ignored = cat.CreateEntry("IgnoredModNames", "", "Ignored mod names",
            "Mod names, separated by commas, left out when Host settings compares your mods with a player's. Add mods that only change what one player sees.");
    }

    /// <summary>The hello this player sent, if they have BiggerLobbies.</summary>
    public static bool TryGet(ulong steamId, out Hello hello) => _hellos.TryGetValue(steamId, out hello!);

    /// <summary>Below zero when <paramref name="version"/> is older than this mod.</summary>
    public static int CompareToMine(string version) => Compare(version, Utils.Constants.MOD_VERSION);

    private static int Compare(string a, string b)
    {
        int[] Parts(string v) => (v ?? "").Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0).ToArray();
        var x = Parts(a); var y = Parts(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    private static bool Ignored(string name)
    {
        if (IgnoredBuiltIn.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return true;
        if (name.Contains("Hud") || name.Contains("HUD")) return true;   // HUD mods only change one player's screen
        var extra = _ignored?.Value ?? "";
        return extra.Split(',').Any(n => n.Trim().Length > 0 && string.Equals(n.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string Clean(string? s) => (s ?? "").Replace("|", "").Replace(',', ' ').Trim();

    // Every loaded MelonLoader mod except this one, cleaned for the chat line.
    private static List<string> MyMods()
    {
        var list = new List<string>();
        try
        {
            foreach (var m in MelonMod.RegisteredMelons)
            {
                string n = Clean(m?.Info?.Name);
                if (n.Length == 0 || string.Equals(n, Utils.Constants.MOD_NAME, StringComparison.OrdinalIgnoreCase)) continue;
                if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
            }
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't list your mods: {e.Message}"); }
        return list;
    }

    // --- Client ----------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyEntered))]
    private static void OnLobbyEntered_Postfix(LobbyEnter_t result)
    {
        try
        {
            if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) return;
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (lobby.m_SteamID == 0) return;
            ulong me = SteamUser.GetSteamID().m_SteamID;
            if (SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == me) return;   // hosts don't say hello

            var sb = new StringBuilder($"{Prefix}{me}|{Clean(Utils.Constants.MOD_VERSION)}|");
            int first = sb.Length;
            foreach (var n in MyMods())
            {
                if (Encoding.UTF8.GetByteCount(sb.ToString()) + Encoding.UTF8.GetByteCount(n) + 1 > MaxBytes) break;
                if (sb.Length > first) sb.Append(',');
                sb.Append(n);
            }
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var buf = new Il2CppStructArray<byte>(bytes.Length + 1);
            for (int i = 0; i < bytes.Length; i++) buf[i] = bytes[i];
            SteamMatchmaking.SendLobbyChatMsg(lobby, buf, buf.Length);
            Utils.Log.Info($"Sent hello to the host: version {Utils.Constants.MOD_VERSION}, {bytes.Length} bytes");
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't send the hello to the host: {e.Message}"); }
    }

    // --- Host ------------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SteamLobbyService), nameof(SteamLobbyService.OnLobbyChatMessage))]
    private static void OnLobbyChatMessage_Postfix(LobbyChatMsg_t result)
    {
        try
        {
            if (!HostLobby.TryGet(out var lobby) || lobby.m_SteamID != result.m_ulSteamIDLobby) return;
            ulong from = result.m_ulSteamIDUser;
            if (from == 0 || from == SteamUser.GetSteamID().m_SteamID || !IsMember(lobby, from)) return;

            var buf = new Il2CppStructArray<byte>(4096);
            int n = SteamMatchmaking.GetLobbyChatEntry(lobby, (int)result.m_iChatID, out _, buf, buf.Length, out _);
            if (n <= Prefix.Length) return;
            var bytes = new byte[n];
            for (int i = 0; i < n; i++) bytes[i] = buf[i];
            string msg = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            if (!msg.StartsWith(Prefix, StringComparison.Ordinal)) return;

            var parts = msg.Substring(Prefix.Length).Split('|');
            if (parts.Length < 2) return;
            // Steam vouches for the sender; the ID in the line is only checked against it.
            if (ulong.TryParse(parts[0], out ulong said) && said != from) return;

            var hello = new Hello { Version = parts[1].Trim() };
            if (parts.Length >= 3)
                foreach (var m in parts[2].Split(','))
                    if (m.Trim().Length > 0) hello.Mods.Add(m.Trim());
            Compare(hello);
            _hellos[from] = hello;
            Utils.Log.Info($"Hello from {DepartedPlayers.NameOf(from)} ({from}): version {hello.Version}, {hello.Mods.Count} other mod(s)"
                + (hello.Missing.Count > 0 ? $", missing {string.Join(", ", hello.Missing)}" : "")
                + (hello.Extra.Count > 0 ? $", extra {string.Join(", ", hello.Extra)}" : ""));
            if (hello.Missing.Count > 0 && _warned.Add(from))
                MelonLogger.Warning($"{DepartedPlayers.NameOf(from)} ({from}) doesn't have these mods you use: {string.Join(", ", hello.Missing)}. Things may look different for them or go out of sync");
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't read a player's hello: {e.Message}"); }
    }

    private static void Compare(Hello h)
    {
        var mine = new HashSet<string>(MyMods().Where(m => !Ignored(m)), StringComparer.OrdinalIgnoreCase);
        var theirs = new HashSet<string>(h.Mods.Where(m => !Ignored(m)), StringComparer.OrdinalIgnoreCase);
        h.Missing = mine.Where(m => !theirs.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
        h.Extra = theirs.Where(m => !mine.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsMember(CSteamID lobby, ulong id)
    {
        int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
        for (int i = 0; i < n; i++)
            if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == id) return true;
        return false;
    }
}

#endif
