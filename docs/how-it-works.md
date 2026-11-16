# How it works

[Back to the README](../README.md)

For modders, curious players, and anyone reviewing the code. BiggerLobbies is a MelonLoader mod for Schedule I 0.4.7, built from one codebase for both Steam branches: `BiggerLobbies_Il2cpp.dll` for the default (Il2Cpp) game and `BiggerLobbies_Mono.dll` for the `alternate` (Mono) game. It patches the game's own classes with Harmony and runs entirely on the host. The one exception is kick messages, which a player's game shows only if they have the mod too.

## The fixes

| Area | What the game does | What the mod does |
|---|---|---|
| Lobby size | The Steam lobby is created for 4 players, and `Lobby.PlayerLimit` is a compile-time constant, so it can't be raised directly (on Il2Cpp, writing to it crashes the game) | Raises the size passed to `SteamLobbyService.CreateLobby`, then sets the Steam member limit and the `max_players` lobby data once the lobby exists |
| Network connections | The Steam transport (FishySteamworks, behind FishNet's Multipass) accepts 4 connections besides the host, and copies its limit into the server socket when the server starts | Raises the transport's limit to `MaxPlayers` before the server starts, and on the live socket if it's already running |
| Member list | `SteamLobbyService` writes each member into a fixed array without a bounds check | Resizes the array to Steam's 250-member cap, so a lobby bigger than any player's own setting can't overflow it |
| Lobby screen | One row of slots grown from a zero-size anchor, 75 px per player | Clones the game's own slot up to `MaxPlayers`. Above 20 it switches the grid to rows of 20, moves the Invite button beside the first row, and stops word-wrap in the title |
| Duplicate lobbies | One Invite click could create two Steam lobbies; the host tracked one and friends joined the other | Blocks a second lobby request while one is pending, and opens the invite overlay only once the lobby is entered |
| Going online after loading a save | The game decides online or offline once, as the save loads, depending on whether a lobby exists | Creates a lobby before the save loads and waits for Steam to confirm it (10-second timeout, then loads solo) |
| Joining late | A joining player's game stores lobby members in a 4-slot array and fails with 5 or more, before it reads the lobby's ready flag | With more than 4 members, re-sends the lobby's ready message, a path that doesn't use that array |
| Rejoining after a crash | The old Steam auth session stays open (`k_EBeginAuthSessionResultDuplicateRequest`), and a second authentication on the same connection is kicked | Ends any old session before checking a new connection, and re-checks a repeated authentication with Steam instead of disconnecting |
| Saving | The game saves only at sleep, or when a client quits cleanly and asks the host to save it | Runs the game's own save every 5 minutes, saves each player every 30 seconds through `PlayerManager.SavePlayer`, and saves when a player leaves |
| Plants | Each machine simulates pots itself; botanist watering and the overnight water drain are applied only on the host | Sends water levels after watering and sleep, and resyncs every pot's water and growth periodically |
| Lobby type | Every lobby is created Friends only | Swaps the type passed to `SteamMatchmaking.CreateLobby` for the `LobbyPrivacy` setting, and calls `SetLobbyType` when you change it on an open lobby |
| Open lobbies | `Player.SetPlayerNameAndId_Server` kicks anyone who isn't the host's Steam friend as they load in | When the lobby is Open, `PlatformFriends.IsLocalPlayerFriendsWith` answers yes, so strangers can stay |
| Lobby size while playing | The size is fixed when the lobby is created | Sets the Steam member limit, `max_players` and the live transport limit together, never below the players already in |
| Bans and the whitelist | Anyone who can reach the lobby can log in, and the Steam ID in a login ticket is only what the joining game claims | Checks each login in `FishNetSteamAuthenticator.OnSteamAuthBroadcast`. The claimed Steam ID must match the one Steam's networking already verified for that connection (FishySteamworks' `ServerSocket`, mapped through Multipass); a mismatch is disconnected without touching the real owner of that ID. Banned or unlisted players are then refused. Turning the whitelist on, or loading a crew, also disconnects players already in who aren't on the list, unless the list is empty |
| Kicking | The host can drop a connection, but Steam has no way to remove someone from a lobby, so their game says "Authentication Error" and keeps showing it | Posts a short notice in the lobby chat (`bl1\|kick\|<steam id>\|<kind>\|<seconds>`), waits 1.5 s (0.5 s for a player refused at their first login), then disconnects. Ban reasons stay on the host and aren't posted to the lobby. Notices for refused logins are limited to 6 every 10 s. A player with the mod who sees a notice from the lobby owner naming them returns to the main menu with "Kicked by the host", "Banned from this lobby" or "Not on the whitelist". The host's lobby screen drops removed players until they leave or rejoin the Steam lobby, or you let them in again; banned players stay hidden |
| Take items | Players who leave keep their inventory in the save, at `Players/Player_<Steam ID>/Inventory.json`, and get it back when they rejoin | Waits 10 s after the file was last written and copies it to `UserData/BiggerLobbies/Taken`. Then, one stack at a time: checks it fits, rewrites the player's file without it (written to a temporary file and swapped in), and only then adds it to the host's inventory with the game's own item loader. Cash works the same way. A failed write stops the take, so an item is never in both places. Half a second later it counts what arrived and warns if anything is short. Each take is appended to `TakenLog.tsv`, with a `.take.json` record of the exact slots and cash taken |
| Give back | Nothing | Only while the player is away and their file has settled. For each taken slot that is still empty in their file, takes up to the amount taken out of the host's inventory, counting only stacks whose item data matches the stored slot (quality, packaging and so on; quantity and game version aside). Waits half a second, counts what really left the host's inventory, and writes only that back: the stored slot text with its quantity changed. Cash works the same way. The record keeps what was returned, so a second press never goes over the original |
| Discord presence | Discord shows "Playing Schedule I" from its own list of known games, with nothing underneath | Talks to Discord over its local IPC pipe (`\\.\pipe\discord-ipc-N`, the discord-rpc protocol: a 4-byte opcode, a 4-byte length and JSON) from a background thread, sends the activity under Discord's own application entry for Schedule I at most every 15 s, and clears it on quit. No extra DLL |
| Version and mods | Nothing | A player with the mod posts `bl1\|hello\|<steam id>\|<version>\|<mods>` in the lobby chat when they enter. The host keeps it for the session and compares the mod list with its own, skipping HUD mods, MLVScan and `IgnoredModNames` |
| Host settings screen | No host controls | Clones the game's Settings screen into the main menu and the pause menu, strips its scripts, and builds the tabs and lists from the game's own buttons, sliders and text |

The mod writes only warnings and errors to the log unless `DiagnosticLog` is on. With it on, every step of the lobby and join flow is logged with a `[diag]` prefix.

## Two builds, one codebase

Both branches are the same game version, built from the same source; the Il2Cpp game just reaches its code through MelonLoader's generated interop assemblies, with `Il2Cpp` in front of every namespace. The mod keeps every backend difference in two files:

- `Utils/GlobalUsings.cs` imports the game's namespaces once, with or without the `Il2Cpp` prefix, so no other file names a backend.
- `Utils/Shim.cs` holds the rest: array and dictionary types (`Il2CppStructArray<byte>` against `byte[]`, for example), checked casts (`TryCast<T>` against `as`), runtime type names, `WasCollected`, and file moves.

On Mono, private game members (such as the Steam transport's verified Steam IDs) are reached through a publicized copy of the game's assemblies, as the Il2Cpp interop already exposes them.

**Loading next to each other.** Each DLL declares its backend with `MelonPlatformDomain`, so MelonLoader skips the other build. The two builds also need different names (`BiggerLobbies` and `BiggerLobbies (Mono)`): MelonLoader orders mods by name and silently drops a second one with the same name before checking its backend, so the wrong one could win. Harmony patches are applied by the mod after a backend check (`HarmonyDontPatchAll`), not automatically on load. On the `alternate` branch the Il2Cpp DLL can't even be read, because it targets .NET 6 and the Mono runtime can't resolve its assembly attributes, so MelonLoader logs a load error for it instead.

## Source layout

| File | What's in it |
|---|---|
| `Core.cs` | Entry point: the backend check, applying patches, and starting each feature |
| `Integrations/LobbyPatches.cs` | Lobby size, Steam lobby data, invites and the lobby screen |
| `Integrations/LobbySize.cs` | The `MaxPlayers` setting, live size changes, the network connection limit, the lobby screen table and `DebugFakePlayers` |
| `Integrations/LobbyDedupePatches.cs` | One lobby per host |
| `Integrations/LobbyPrivacy.cs` | Lobby type, and letting strangers stay in Open lobbies |
| `Integrations/HostLobby.cs` | The Steam lobby this machine is hosting |
| `Integrations/AlwaysHostLobby.cs` | Creating a lobby as a save loads |
| `Integrations/LateJoinFix.cs` | Joining a game that has already started |
| `Integrations/StaleAuthFix.cs` | Rejoining after a crash, and the login check for bans and the whitelist |
| `Integrations/BanList.cs` | Bans, ban lengths, and the Steam ID each connection logged in with |
| `Integrations/Whitelist.cs` | The whitelist |
| `Integrations/Crews.cs` | Saved crews |
| `Integrations/HostPlayers.cs` | The connected players list, ping, kick and ban |
| `Integrations/KickNotice.cs` | Kick messages, both sides, and hiding removed players from the host's lobby screen |
| `Integrations/DepartedPlayers.cs` | Players who left with items, Take items, Give back, the taken log and remembered player names |
| `Integrations/DiscordPresence.cs` | Discord rich presence and its settings |
| `Integrations/ModHandshake.cs` | The version and mod list players with the mod send, and the comparison shown on the Players tab |
| `Integrations/Friends.cs`, `Integrations/Avatars.cs` | Steam friends, online status and avatars for the pickers |
| `Integrations/HostSettingsMenu.cs` | The Host settings screen |
| `Integrations/AutoSave.cs` | Autosave, player saves, save on leave, and the settings category |
| `Integrations/GrowSyncFix.cs` | Plant sync |
| `Integrations/DebugDummies.cs` | `DebugDummyPlayers`: dummy characters and frame-rate logging |
| `Integrations/DiagnosticPatches.cs` | `[diag]` logging |
| `Utils/SteamIdentity.cs` | The Steam ID Steam's networking verified for each connection, and the Steam transport |
| `Utils/Log.cs` | Quiet logging and the `DiagnosticLog` setting |
| `Utils/MiniJson.cs` | A small JSON reader and writer for the Give back records |
| `Utils/Constants.cs` | Default player limit and mod info, including each build's name |
| `Utils/GlobalUsings.cs` | The game's namespaces for each backend |
| `Utils/Shim.cs` | Everything else that differs between the Il2Cpp and Mono builds |

## Build from source

You need the .NET SDK (6.0 or later).

**Il2Cpp build** (`BiggerLobbies_Il2cpp.dll`), from a game on the default branch:

1. Run the game once with MelonLoader so it generates `MelonLoader\Il2CppAssemblies`.
2. Build the `Il2cpp` configuration, pointing `GamePath` at your game folder or r2modman profile:

   ```
   dotnet build BiggerLobbies.csproj -c Il2cpp -p:GamePath="C:\path\to\Schedule I"
   ```

**Mono build** (`BiggerLobbies_Mono.dll`), from a game on the `alternate` branch with MelonLoader installed:

```
dotnet build BiggerLobbies.csproj -c Mono -p:GamePath="C:\path\to\Schedule I"
```

It references the game's `Schedule I_Data\Managed` folder, which only exists while the game is on the `alternate` branch, since switching branch replaces it. To build both from one install, copy `Managed` somewhere else while on `alternate`, then add `-p:ManagedPath="C:\path\to\copy\Managed"` to the Mono build. The first Mono build downloads Krafs.Publicizer from NuGet.

> [!WARNING]
> On Windows the post-build step copies the DLL into `$(GamePath)\Mods`; add `-p:SkipDeploy=true` to skip that. Add `-p:Deploy=true` to also force-close `Schedule I.exe` before the copy and start the game again afterwards.

The extra lobby slots are copies of the game's own slot, and Host settings is a copy of the game's Settings screen, so a future game update that changes either screen could make them look wrong.
