using System;
using HarmonyLib;
using MelonLoader;
using BiggerLobbies.Utils;

namespace BiggerLobbies.Integrations;

// The host keeps one Steam auth session per player and ends it when that player's
// connection closes. If the old connection hangs instead (a crash, or a failed join),
// the session stays open and every rejoin fails with
// "Steam auth failed ... k_EBeginAuthSessionResultDuplicateRequest".
// Before checking a new, not-yet-authenticated connection, end any old session for
// that Steam ID so the new check can start.
[HarmonyPatch]
public static class StaleAuthFix
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FishNetSteamAuthenticator), nameof(FishNetSteamAuthenticator.OnSteamAuthBroadcast))]
    private static bool OnSteamAuthBroadcast_Prefix(FishNetSteamAuthenticator __instance, NetworkConnection conn, SteamSessionAuthTicket auth)
    {
        try
        {
            if (conn == null || Shim.IsNull(auth)) return true; // a class on Il2Cpp, a struct on Mono
            ulong claimed = auth.SteamId;

            // The SteamId in the broadcast is only the client's claim until Steam has checked
            // the ticket against it. The transport already knows who this connection really is
            // (FishySteamworks, via Steam networking), so use that: a claim for someone else's
            // ID is dropped with no side effect on that someone (no ban/whitelist notice, no
            // unhide, no login record, no ended auth session).
            bool confirmed = SteamIdentity.TryGetVerified(conn, out ulong verified);
            if (confirmed && verified != claimed)
            {
                MelonLogger.Warning($"Connection {conn.ClientId} claimed Steam ID {claimed} but is {verified}; disconnecting");
                try { conn.Disconnect(true); } catch { }
                return false;
            }
            // Transport can't tell (local connection, other transport): an already
            // authenticated connection that repeats its recorded ID is still confirmed.
            if (!confirmed && conn.Authenticated)
            {
                ulong known0 = BanList.VerifiedSteamId(conn.ClientId);
                confirmed = known0 != 0 && known0 == claimed;
            }

            bool isHost = conn.IsLocalClient || (claimed != 0 && claimed == BanList.HostSteamId());
            if (!isHost && BanList.RejectIfBanned(conn, claimed, confirmed)) return false;
            if (Whitelist.RejectIfNotListed(conn, claimed, confirmed)) return false;
            // A reused connection number is a new, unauthenticated connection, so it's replaced.
            // Only a confirmed ID is recorded or unhidden; an unconfirmed one is Steam-checked
            // by vanilla right after this and a mismatch disconnects it.
            if (!conn.Authenticated && claimed != 0)
            {
                // Only a transport-verified ID is recorded (an unconfirmed claim must never seed the
                // re-authentication path below); the host's own local connection is recorded explicitly.
                if (confirmed) BanList.RecordLogin(conn.ClientId, claimed);
                else if (conn.IsLocalClient && claimed == BanList.HostSteamId()) BanList.RecordLogin(conn.ClientId, claimed);
                if (confirmed) KickNotice.Unhide(claimed);   // let in again after a kick or a whitelist refusal: show them
            }

            // A client that starts its join twice (e.g. Steam "Join Game" plus the lobby's
            // ready flag after a crash) sends a second auth on a connection that is already
            // authenticated. Vanilla kicks it. Re-authenticate instead: end the old Steam
            // session, verify the new ticket from scratch, and only keep the connection if
            // Steam accepts it. The player is not spawned again; only the reply is sent.
            if (conn.Authenticated)
            {
                ulong rid = auth.SteamId;
                if (rid == 0) return true; // let vanilla disconnect
                ulong known = BanList.VerifiedSteamId(conn.ClientId);
                if (known != 0 && known != rid)
                {
                    MelonLogger.Warning($"Connection {conn.ClientId} logged in again as a different Steam ID ({rid}, was {known}); disconnecting");
                    try { conn.Disconnect(true); } catch { }
                    return false;
                }
                if (!confirmed) return true;   // can't tell whose session this is: leave it, vanilla disconnects
                PlatformAuth.EndAuthSession(new CSteamID(rid));
                var result = PlatformAuth.BeginAuthSession(auth);
                if (result != EBeginAuthSessionResult.k_EBeginAuthSessionResultOK)
                {
                    MelonLogger.Warning($"Connection {conn.ClientId} re-authentication failed ({result}); disconnecting");
                    return true; // vanilla path disconnects
                }
                __instance.SendAuthenticationResponse(conn, true);
                Utils.Log.Info($"Connection {conn.ClientId} sent authentication again; re-authenticated with Steam ({result})");
                return false;
            }

            ulong id = claimed;
            if (id == 0 || !confirmed) return true;   // never end a session for an ID that isn't confirmed
            PlatformAuth.EndAuthSession(new CSteamID(id));
            Utils.Log.Info($"Cleared any old Steam auth session for {id} before authenticating connection {conn.ClientId}");
        }
        catch (Exception e) { MelonLogger.Error($"Steam auth fix failed: {e}"); }
        return true;
    }

    // A closed connection's number can be reused by the next player, and the Steam ID map
    // would otherwise grow all session. Forget the login when the connection stops.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerManager), nameof(ServerManager.Transport_OnRemoteConnectionState))]
    private static void Transport_OnRemoteConnectionState_Postfix(RemoteConnectionStateArgs args)
    {
        try
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped) BanList.ForgetLogin(args.ConnectionId);
        }
        catch (Exception e) { MelonLogger.Warning($"Couldn't clear a closed connection's login: {e.Message}"); }
    }
}
