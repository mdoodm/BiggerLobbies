# Changelog

All versions are for **Schedule I 0.4.7** (Il2Cpp build) and need MelonLoader v0.7.3 or later. Only the host needs the mod.

## 1.0.8-beta.2 (beta)

A test build. Back up your saves before trying it.

### Changed
- Clearer presence on Steam avatars in Host settings: a bigger green dot for friends who are online or playing Schedule I, red for blacklisted, none when offline. Friends playing Schedule I also get a green name. The whitelist uses the same colours.

## 1.0.8-beta.1 (beta)

A test build. Back up your saves before trying it. The whitelist, blacklist and kick haven't been tested with a second player yet.

### Added
- **Host settings**, a new entry under Settings in the main menu and the pause menu, built from the game's own Settings screen. F6 opens it while you host (`HostPanelKey`). In the pause menu it only shows for the host.
- **Lobby type** (`LobbyPrivacy`): Private (invite only), Friends only (the game's default) or Open (listed in Steam's lobby search). Applies straight away to an open lobby.
- **Lobby size without a restart.** The slider updates the Steam lobby and the network limit straight away, and can't go below the number of players already in. Tested in game: Steam accepted 25, 65, 250, 4 and 20 and reported the same limit back each time.
- **Players**: everyone connected, with their Steam avatar and ping, and Kick and Blacklist buttons. Both screens have Back and Close buttons; Close in the pause menu goes straight back to the game. Friends are listed by who's playing Schedule I, then who's online, with a search box.
- **Whitelist** (`WhitelistEnabled`). When it's on, only players you add can join. The list starts empty each time you start the game, and you add players from your Steam friends. Players already in stay.
- **Blacklist** (`Blacklist`), saved between games. A blacklisted player is refused at login and disconnected straight away if they're already in. The blacklist always wins over the whitelist. Bans use the Steam ID that Steam verifies at login, not the one a player's game reports, and you can't blacklist yourself.

## 1.0.7

### Added
- **Adjustable lobby size** (`MaxPlayers`, default 20, from 4 to 250). Restart the game after changing it.
- The host raises the Steam network transport's connection limit to match, and logs it.
- Above 20 players, the lobby screen shows players as a table in rows of 20, with the invite button beside the first row. Up to 5 rows (100 players) are shown; past that the title adds `+N`. At 20 or fewer it looks exactly as before.
- `DebugFakePlayers` (testing only, default 0) fills the lobby screen with placeholder players to check the layout without real players.
- `DebugDummyPlayers` (testing only, default 0) spawns copies of your character around you after a save loads and logs the frame rate every 10 seconds. They aren't networked or saved. Tested with 40 and 250 simulated players on one save.

### Fixed
- **Only 5 players could actually connect.** The game's Steam network transport accepts just 4 connections besides the host, whatever the lobby size. The host now raises it to `MaxPlayers` before the server starts (the log shows `Steam transport player limit raised from 4 to 20`).
- A player whose own `MaxPlayers` is lower than the host's lobby size no longer breaks their lobby member list. The member list now always has room for Steam's 250-player cap.

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
