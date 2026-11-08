# How it works

[Back to the README](../README.md)

For modders, curious players, and anyone reviewing the code. BiggerLobbies is a MelonLoader mod for the Il2Cpp build of Schedule I 0.4.7. It patches the game's own classes with Harmony and runs entirely on the host.

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

Every step of the lobby and join flow is logged with a `[diag]` prefix.

## Source layout

| File | What's in it |
|---|---|
| `Integrations/Il2CppLobbyPatches.cs` | Lobby size, Steam lobby data, invites and the lobby screen |
| `Integrations/LobbySize.cs` | The `MaxPlayers` setting, the network connection limit, the lobby screen table and `DebugFakePlayers` |
| `Integrations/LobbyDedupePatches.cs` | One lobby per host |
| `Integrations/AlwaysHostLobby.cs` | Creating a lobby as a save loads |
| `Integrations/LateJoinFix.cs` | Joining a game that has already started |
| `Integrations/StaleAuthFix.cs` | Rejoining after a crash |
| `Integrations/AutoSave.cs` | Autosave, player saves, save on leave, and the settings category |
| `Integrations/GrowSyncFix.cs` | Plant sync |
| `Integrations/DebugDummies.cs` | `DebugDummyPlayers`: dummy characters and frame-rate logging |
| `Integrations/DiagnosticPatches.cs` | `[diag]` logging |
| `Integrations/HarmonyPatches.cs` | The original patches, used by the Mono build only |
| `Utils/Constants.cs` | Default player limit and mod info |

## Build from source

You need the .NET SDK (6.0 or later).

1. Run the game once with MelonLoader so it generates `MelonLoader\Il2CppAssemblies`.
2. Build the `Il2cpp` configuration, pointing `GamePath` at your game folder or r2modman profile:

   ```
   dotnet build BiggerLobbies.csproj -c Il2cpp -p:GamePath="C:\path\to\Schedule I"
   ```

> [!WARNING]
> The project's post-build step **force-closes `Schedule I.exe`**, copies the DLL into `$(GamePath)\Mods` and starts the game again. Remove the `PostBuild` target from `BiggerLobbies.csproj` if you don't want that.

The extra lobby slots are copies of the game's own slot, so a future game update that changes the lobby screen could make them look wrong.
