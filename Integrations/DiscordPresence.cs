#if !MONO
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppSteamworks;
using MelonLoader;

namespace BiggerLobbies.Integrations;

// Discord rich presence: "Schedule I - Hosting Hyland Point - Friends only (3 of 20)".
//
// Discord shows "Playing Schedule I" on its own, from its list of known games, but the
// details under it have to be sent by the game. The game doesn't, so the mod does, over
// Discord's local IPC pipe (\\.\pipe\discord-ipc-N): a 4-byte opcode, a 4-byte length and
// a JSON payload. The same protocol the discord-rpc library speaks, so no extra DLL.
//
// It's sent under Discord's own application entry for Schedule I by default, so there's
// nothing to register. That entry has no artwork; a host who wants a logo can register
// their own application and put its ID in DiscordAppId.
//
// Threading: the Unity main thread reads the game state every couple of seconds and
// builds the activity JSON. A background thread owns the pipe: it connects, handshakes,
// sends the activity whenever it changes (at most every 15 s, Discord's limit) and drains
// Discord's replies so the pipe never fills. No Il2Cpp call happens off the main thread.
public static class DiscordPresence
{
    public const string GameAppId = "1353866015351636098";   // Discord's detectable entry for Schedule I

    private static MelonPreferences_Entry<bool>? _enabled, _showSave, _showPlayers, _showLobbyType;
    private static MelonPreferences_Entry<string>? _appId;

    public static bool Enabled { get => _enabled?.Value ?? false; set { if (_enabled != null && _enabled.Value != value) { _enabled.Value = value; MelonPreferences.Save(); } } }
    public static bool ShowSaveName { get => _showSave?.Value ?? false; set { if (_showSave != null && _showSave.Value != value) { _showSave.Value = value; MelonPreferences.Save(); } } }
    public static bool ShowPlayers { get => _showPlayers?.Value ?? true; set { if (_showPlayers != null && _showPlayers.Value != value) { _showPlayers.Value = value; MelonPreferences.Save(); } } }
    public static bool ShowLobbyType { get => _showLobbyType?.Value ?? true; set { if (_showLobbyType != null && _showLobbyType.Value != value) { _showLobbyType.Value = value; MelonPreferences.Save(); } } }

    /// <summary>"Connected", "Discord isn't running", "Off". For the Host settings screen.</summary>
    public static string Status => !Enabled ? "Off" : _connected ? "Connected" : "Discord isn't running";

    public static void Init(MelonPreferences_Category cat)
    {
        _enabled = cat.CreateEntry("DiscordPresence", true, "Discord rich presence",
            "Show what you're doing under 'Playing Schedule I' on Discord: hosting, lobby type and player count. Needs Discord running on this PC.");
        _showPlayers = cat.CreateEntry("DiscordShowPlayers", true, "Discord: show player count",
            "Show the lobby's player count on Discord, like (3 of 20).");
        _showLobbyType = cat.CreateEntry("DiscordShowLobbyType", true, "Discord: show lobby type",
            "Show Friends only, Open or Private on Discord.");
        _showSave = cat.CreateEntry("DiscordShowSaveName", false, "Discord: show save name",
            "Show your organisation's name on Discord. Off by default: anyone who can see your profile can read it.");
        _appId = cat.CreateEntry("DiscordAppId", GameAppId, "Discord application ID",
            "The Discord application the presence is sent as. The default is Discord's own entry for Schedule I, which has no logo. Register your own at discord.com/developers to show one.");
    }

    // --- Main thread: what to show --------------------------------------------------

    private static float _nextPoll;
    private static long _sessionStart;      // unix seconds, set when a save finishes loading
    private static bool _wasLoaded;
    private static Thread? _thread;
    private static volatile string? _wanted;            // activity JSON, or null to clear
    private static volatile bool _connected;
    private static volatile bool _stop;

    public static void Update()
    {
        try
        {
            if (!Enabled)
            {
                if (_thread != null) { _wanted = null; }
                return;
            }
            if (_thread == null)
            {
                _stop = false;
                _thread = new Thread(Worker) { IsBackground = true, Name = "BiggerLobbies Discord" };
                _thread.Start();
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 2f;
            _wanted = Build();
        }
        catch (Exception e) { MelonLogger.Warning($"Discord presence: {e.Message}"); }
    }

    public static void Shutdown()
    {
        _stop = true;
        _wanted = null;
    }

    private static string Build()
    {
        var lm = Singleton<LoadManager>.Instance;
        bool loaded = lm != null && lm.IsGameLoaded;
        if (loaded && !_wasLoaded) _sessionStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _wasLoaded = loaded;

        string details, state = "";
        int cur = 0, max = 0;
        ulong partyId = 0;
        if (!loaded)
        {
            details = "In the main menu";
        }
        else
        {
            string save = ShowSaveName ? SaveName(lm) : "";
            bool host = SafeIsServer();
            var svc = HostLobby.Service();
            bool inLobby = false;
            CSteamID lobby = default;
            try { inLobby = svc != null && svc.IsInLobby; if (inLobby) { lobby = svc!._lobbySteamID; partyId = lobby.m_SteamID; } } catch { }

            if (host && inLobby)
            {
                details = save.Length > 0 ? $"Hosting {save}" : "Hosting a lobby";
                if (ShowLobbyType) state = LobbyPrivacy.Label(LobbyPrivacy.Current) + (LobbyPrivacy.Current == LobbyPrivacy.Mode.Open ? " lobby" : "");
                if (ShowPlayers) { cur = Math.Max(1, HostLobby.MemberCount()); max = LobbySize.MaxPlayers; }
            }
            else if (inLobby)
            {
                details = "In a friend's lobby";
                if (ShowPlayers)
                {
                    try
                    {
                        cur = Math.Max(1, SteamMatchmaking.GetNumLobbyMembers(lobby));
                        if (!int.TryParse(SteamMatchmaking.GetLobbyData(lobby, "max_players"), out max) || max <= 0) max = SteamMatchmaking.GetLobbyMemberLimit(lobby);
                    }
                    catch { cur = 0; max = 0; }
                }
            }
            else
            {
                details = save.Length > 0 ? $"Playing {save}" : "Playing solo";
            }
        }

        var sb = new StringBuilder(256);
        sb.Append("{\"details\":").Append(Quote(Trim(details, 128)));
        if (state.Length > 0) sb.Append(",\"state\":").Append(Quote(Trim(state, 128)));
        if (loaded && _sessionStart > 0) sb.Append(",\"timestamps\":{\"start\":").Append(_sessionStart).Append('}');
        if (cur > 0 && max >= cur)
            sb.Append(",\"party\":{\"id\":\"bl-").Append(partyId).Append("\",\"size\":[").Append(cur).Append(',').Append(max).Append("]}");
        sb.Append(",\"instance\":").Append(loaded ? "true" : "false").Append('}');
        return sb.ToString();
    }

    private static string SaveName(LoadManager? lm)
    {
        try
        {
            var n = lm?.ActiveSaveInfo?.OrganisationName;
            if (!string.IsNullOrWhiteSpace(n)) return n!.Trim();
            var p = lm?.LoadedGameFolderPath;
            if (!string.IsNullOrEmpty(p)) return Path.GetFileName(p!.TrimEnd('/', '\\'));
        }
        catch { }
        return "";
    }

    private static bool SafeIsServer()
    {
        try { return InstanceFinder.IsServer; } catch { return false; }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    private static string Quote(string v)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in v)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': case '\r': case '\t': sb.Append(' '); break;
                default: if (c < 0x20) sb.Append(' '); else sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    // --- Background thread: the pipe -------------------------------------------------

    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
    private const int MinSendGap = 15_000;     // ms; Discord allows about 5 updates per 20 s
    private const int RetryGap = 30_000;       // ms between connection attempts

    private static bool _warnedOnce;

    private static void Worker()
    {
        string? sent = null;
        while (!_stop)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = Connect();
                if (pipe == null)
                {
                    _connected = false;
                    if (!_warnedOnce) { _warnedOnce = true; Utils.Log.Info("Discord presence: Discord isn't running; will keep trying"); }
                    Sleep(RetryGap);
                    continue;
                }

                string appId = (_appId?.Value ?? "").Trim();
                if (appId.Length == 0) appId = GameAppId;
                Send(pipe, OpHandshake, "{\"v\":1,\"client_id\":" + Quote(appId) + "}");
                var reply = Read(pipe, out int op);
                if (op != OpFrame || reply == null || !reply.Contains("\"evt\":\"READY\""))
                {
                    MelonLogger.Warning($"Discord presence: Discord refused the handshake ({(reply == null ? "no reply" : Trim(reply, 160))}). Check DiscordAppId.");
                    pipe.Dispose();
                    Sleep(RetryGap * 2);
                    continue;
                }
                _connected = true;
                _warnedOnce = false;
                Utils.Log.Info("Discord presence connected");
                sent = null; _headTask = null;
                long lastSend = 0;
                int pid = Process.GetCurrentProcess().Id;

                while (!_stop && pipe.IsConnected)
                {
                    string? want = _wanted;
                    long now = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
                    if (want != sent && now - lastSend >= MinSendGap)
                    {
                        string activity = want ?? "null";
                        Send(pipe, OpFrame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + pid + ",\"activity\":" + activity + "},\"nonce\":" + Quote(Guid.NewGuid().ToString()) + "}");
                        sent = want;
                        lastSend = now;
                    }
                    Drain(pipe);
                    if (_stop && want == null) break;
                    Thread.Sleep(500);
                }
                if (pipe.IsConnected)
                {
                    try { Send(pipe, OpFrame, "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + pid + ",\"activity\":null},\"nonce\":\"bye\"}"); } catch { }
                }
            }
            catch (Exception e)
            {
                if (!_stop) Utils.Log.Info($"Discord presence: connection dropped ({e.GetType().Name}: {e.Message}); will reconnect");
            }
            finally
            {
                _connected = false;
                try { pipe?.Dispose(); } catch { }
            }
            if (!_stop) Sleep(RetryGap);
        }
        _connected = false;
        _thread = null;
    }

    private static void Sleep(int ms)
    {
        for (int t = 0; t < ms && !_stop; t += 250) Thread.Sleep(250);
    }

    private static NamedPipeClientStream? Connect()
    {
        for (int i = 0; i < 10; i++)
        {
            var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { p.Connect(300); return p; }
            catch (TimeoutException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            p.Dispose();
        }
        return null;
    }

    private static void Send(NamedPipeClientStream pipe, int op, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(frame, 0);
        BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
        body.CopyTo(frame, 8);
        pipe.Write(frame, 0, frame.Length);
        pipe.Flush();
    }

    // Reads one frame. Blocks, so only used for the handshake reply.
    private static string? Read(NamedPipeClientStream pipe, out int op)
    {
        op = -1;
        var head = new byte[8];
        if (!ReadFully(pipe, head, 8)) return null;
        op = BitConverter.ToInt32(head, 0);
        int len = BitConverter.ToInt32(head, 4);
        if (len < 0 || len > 1 << 20) return null;
        var body = new byte[len];
        if (!ReadFully(pipe, body, len)) return null;
        return Encoding.UTF8.GetString(body);
    }

    private static bool ReadFully(Stream s, byte[] buf, int n) => ReadFully(s, buf, n, 0);

    // Discord answers every SET_ACTIVITY and may ping. One header read stays pending; when
    // it completes the body is read (Discord writes whole frames), pings are answered and a
    // close frame ends the connection. Nothing here blocks for more than a few ms.
    private static byte[]? _head;
    private static System.Threading.Tasks.Task<int>? _headTask;

    private static void Drain(NamedPipeClientStream pipe)
    {
        for (int guard = 0; guard < 8; guard++)
        {
            if (_headTask == null)
            {
                _head = new byte[8];
                _headTask = pipe.ReadAsync(_head, 0, 8);
            }
            if (!_headTask.IsCompleted) return;
            int got = _headTask.GetAwaiter().GetResult();
            _headTask = null;
            if (got <= 0) throw new IOException("Discord closed the pipe");
            if (got < 8 && !ReadFully(pipe, _head!, 8 - got, got)) throw new IOException("short frame header");
            int op = BitConverter.ToInt32(_head!, 0);
            int len = BitConverter.ToInt32(_head!, 4);
            if (len < 0 || len > 1 << 20) throw new IOException("bad frame length");
            var body = new byte[len];
            if (!ReadFully(pipe, body, len)) throw new IOException("short frame body");
            switch (op)
            {
                case OpPing: Send(pipe, OpPong, Encoding.UTF8.GetString(body)); break;
                case OpClose: throw new IOException("Discord sent close: " + Trim(Encoding.UTF8.GetString(body), 120));
                default: break;   // SET_ACTIVITY replies and events: nothing to do
            }
        }
    }

    private static bool ReadFully(Stream s, byte[] buf, int n, int offset)
    {
        int got = 0;
        while (got < n)
        {
            int r = s.Read(buf, offset + got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }
}

#endif
