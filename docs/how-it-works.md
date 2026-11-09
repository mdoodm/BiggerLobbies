# How it works

[Back to the README](../README.md)

For modders, curious players, and anyone reviewing the code. BiggerLobbies is a MelonLoader mod for the Il2Cpp build of Schedule I 0.4.7. It patches the game's own classes with Harmony and runs entirely on the host. The one exception is kick messages, which a player's game shows only if they have the mod too.

## The fixes

| Area | What the game does | What the mod does |
|---|---|---|
| Lobby size | The Steam lobby is created for 4 players, and `Lobby.PlayerLimit` is a compile-time constant in the Il2Cpp build, so writing to it crashes the game | Raises the size passed to `SteamLobbyService.CreateLobby`, then sets the Steam member limit and the `max_players` lobby data once the lobby exists |
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
| Bans and the whitelist | Anyone who can reach the lobby can log in | Checks each login in `FishNetSteamAuthenticator.OnSteamAuthBroadcast`, using the Steam ID Steam verifies, not the name or code a player's game reports, and refuses banned or unlisted players |
| Kicking | The host can drop a connection, but Steam has no way to remove someone from a lobby, so their game says "Authentication Error" and keeps showing it | Posts a short notice in the lobby chat (`bl1\|kick\|<steam id>\|<kind>\|<seconds>`), waits 1.5 s, then disconnects. A player with the mod who sees a notice from the lobby owner naming them returns to the main menu with the reason. The host's lobby screen drops removed players until they leave or rejoin the Steam lobby; banned players stay hidden |
| Take items | Players who leave keep their inventory in the save, at `Players/Player_<Steam ID>/Inventory.json`, and get it back when they rejoin | Waits 10 s after the file was last written, copies it to `UserData/BiggerLobbies/Taken`, moves the stacks that fit into the host's inventory with the game's own item loader, adds their cash, then blanks only the slots it took. Each take is appended to `TakenLog.tsv` |
| Host settings screen | No host controls | Clones the game's Settings screen into the main menu and the pause menu, strips its scripts, and builds the tabs and lists from the game's own buttons, sliders and text |

The mod writes only warnings and errors to the log unless `DiagnosticLog` is on. With it on, every step of the lobby and join flow is logged with a `[diag]` prefix.

## Source layout

| File | What's in it |
|---|---|
| `Integrations/Il2CppLobbyPatches.cs` | Lobby size, Steam lobby data, invites and the lobby screen |
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
| `Integrations/DepartedPlayers.cs` | Players who left with items, Take items and the taken log |
| `Integrations/Friends.cs`, `Integrations/Avatars.cs` | Steam friends, online status and avatars for the pickers |
| `Integrations/HostSettingsMenu.cs` | The Host settings screen |
| `Integrations/AutoSave.cs` | Autosave, player saves, save on leave, and the settings category |
| `Integrations/GrowSyncFix.cs` | Plant sync |
| `Integrations/DebugDummies.cs` | `DebugDummyPlayers`: dummy characters and frame-rate logging |
| `Integrations/DiagnosticPatches.cs` | `[diag]` logging |
| `Integrations/HarmonyPatches.cs` | The original patches, used by the Mono build only |
| `Utils/Log.cs` | Quiet logging and the `DiagnosticLog` setting |
| `Utils/Constants.cs` | Default player limit and mod info |

## Build from source

You need the .NET SDK (6.0 or later).

1. Run the game once with MelonLoader so it generates `MelonLoader\Il2CppAssemblies`.
2. Build the `Il2cpp` configuration, pointing `GamePath` at your game folder or r2modman profile:

   ```
   dotnet build BiggerLobbies.csproj -c Il2cpp -p:GamePath="C:\path\to\Schedule I"
   ```

> [!WARNING]
> The project's post-build step **force-closes `Schedule I.exe`**, copies the DLL into `$(GamePath)\Mods` and starts the game again. Add `-p:SkipDeploy=true` to build without it.

The extra lobby slots are copies of the game's own slot, and Host settings is a copy of the game's Settings screen, so a future game update that changes either screen could make them look wrong.
