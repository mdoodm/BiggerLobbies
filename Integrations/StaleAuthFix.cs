#if !MONO
using System;
using HarmonyLib;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.Networking;
using Il2CppScheduleOne.Platform;
using Il2CppSteamworks;
using MelonLoader;

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
            if (conn == null || auth == null) return true;
            if (BanList.RejectIfBanned(conn, auth.SteamId)) return false;
            if (Whitelist.RejectIfNotListed(conn, auth.SteamId)) return false;
            // On a connection's first login Steam checks the ticket against this ID and a
            // mismatch disconnects them, so for anyone who gets in it's their real Steam ID.
            // A reused connection number is a new, unauthenticated connection, so it's replaced.
            if (!conn.Authenticated && auth.SteamId != 0) BanList.RecordLogin(conn.ClientId, auth.SteamId);

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

            ulong id = auth.SteamId;
            if (id == 0) return true;
            PlatformAuth.EndAuthSession(new CSteamID(id));
            Utils.Log.Info($"Cleared any old Steam auth session for {id} before authenticating connection {conn.ClientId}");
        }
        catch (Exception e) { MelonLogger.Error($"Steam auth fix failed: {e}"); }
        return true;
    }
}

#endif
