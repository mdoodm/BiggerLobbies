# Changelog

All versions are for **Schedule I 0.4.7** (Il2Cpp build) and need MelonLoader v0.7.3 or later. Only the host needs the mod.

## 1.0.6

### Added
- **Always host a lobby** (`AlwaysHostLobby`, on by default). Loading a save without pressing Invite first now creates a Steam lobby before the save loads, so the game starts online and friends can be invited or use Steam's **Join Game** at any time. It loads solo straight away if Steam isn't running or you're signed out, if Steam reports the lobby couldn't be created, or if Steam doesn't answer within 10 seconds.
- **Rejoin after a crash.** Before checking a new connection, the host ends any old Steam auth session for that player, which used to block rejoins with `k_EBeginAuthSessionResultDuplicateRequest`.
- **Re-authentication instead of a kick.** When a player's game sends its authentication a second time on a connection that's already accepted, the host checks the new Steam ticket again and keeps them if Steam accepts it. The base game disconnects them.

### Fixed
- Joining a started game with 4 or fewer players made the player load twice and fail authentication. The host now only re-sends the lobby's "ready" message when there are 5 or more players, which is when the base game can't connect them by itself.
- Pressing Continue with the automatic lobby waited the full 10-second timeout before loading.

## 1.0.5 (pre-release)

### Added
- **Join a started game** (`LateJoinEnabled`). With 5 or more players in the lobby, the host re-sends the lobby's "ready" message when someone joins mid-game, so they can connect without the host restarting.
- **Client player saves** (`PlayerSaveEnabled`, `PlayerSaveSeconds`, default 30 s). The host saves each connected player's money, inventory and position from its own copy.
- **Save on leave.** When a player leaves or crashes, the host runs a full save straight away.

## 1.0.4

### Added
- **Host autosave** (`AutoSaveEnabled`, `AutoSaveMinutes`, default 5). Runs the game's own full save, skipping while a save or sleep is in progress.
- **Plant sync** (`PlantSyncEnabled`, `PlantSyncMinutes`, default 30 in-game minutes). The host sends pot water levels after a botanist waters and after sleeping, and resends every pot's water and growth periodically, so clients don't see a wrong "needs water" icon or frozen growth.

## 1.0.3

### Fixed
- The mod failed to load on game 0.4.7 (`Undefined target method ... Lobby_OnLobbyCreated_Postfix`). Patches now target the new `SteamLobbyService` class and `LobbyInterface.Start`.
- Writing `Lobby.PlayerLimit` crashed the game. The 20-player limit is now applied when the lobby is created.
- One click on Invite could create two Steam lobbies, so friends joined a different lobby from the host and stayed in the main menu.

### Added
- Invites past 4 players.
- `[diag]` logging of the lobby and join flow.
