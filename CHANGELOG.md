# Changelog

All versions are for **Schedule I 0.4.7** (Il2Cpp build) and need MelonLoader v0.7.3 or later. Only the host needs the mod.

## 1.0.9

### Added
- **Take items** from players who left. The Players tab has a new **Left the game** section listing saved players who aren't connected and still carry items, with their item icons and cash. Take items moves their items (as many stacks as fit) and their cash into your inventory, and removes them from their saved inventory, so they don't get them back if they rejoin. A copy of their original inventory is kept in `UserData/BiggerLobbies/Taken`. It waits a few seconds after someone leaves so their own exit save lands first.
- Lists in Host settings sit on quiet panels with alternating rows, and each section has a one-line description.
- `DebugFakePlayers` (testing only) now also fills the Players tab with test entries that borrow your Steam friends' names and avatars. They can't be kicked or banned.
- **Tabs in Host settings**, using the game's own tab bar: Players (who's connected, kick, ban), Access (whitelist, saved crews, bans), Lobby (who can join, size) and Saving (autosave). It opens on Players in game and on Lobby from the main menu.
- **Saved crews** in Host settings. Save the current whitelist under a name, then load it in one click: it replaces the whitelist and turns it on. Banned players are left off. Each crew shows a few member avatars, with Load and Delete (Delete asks to confirm). Saved as `SavedCrews`.
- **Autosave** in Host settings: world saves and player saves on or off, how often each runs, a Save now button, and when the game last saved. Changes apply without a restart.
- **Taken items log** on the Players tab: who you took from, what, and when, for the save you're in.
- **Kick and ban messages for players who have BiggerLobbies.** Instead of "Authentication Error" and a lobby that still shows everyone, they go back to the main menu with "Kicked by the host", "Banned from this lobby" (with the ban length) or "Not on the whitelist", and leave the Steam lobby. Players without the mod are disconnected as before.
- Players you kick, ban or refuse no longer show on your lobby screen. A kicked player shows again if they rejoin; banned players stay hidden.
- `DiagnosticLog` setting. The mod now only writes warnings and errors to the MelonLoader log; turn this on for the detailed lines when reporting a problem.

### Fixed
- A list that grew or shrank while Host settings was open (for example adding to the whitelist) overlapped the section below it.
- In lobbies of 40+ players, the game's lobby avatar grid covered Host settings. It's hidden while the screen is open.

## 1.0.8

### Added
- **Host settings**, a new entry under Settings in the main menu and the pause menu, built from the game's own Settings screen. F6 opens it while you host (`HostPanelKey`). In the pause menu it only shows for the host. Both screens have Back and Close buttons.
- **Lobby type** (`LobbyPrivacy`): Private (invite only), Friends only (the game's default) or Open (listed in Steam's lobby search, and strangers can join). Applies straight away to an open lobby.
- **Lobby size without a restart.** The slider updates the Steam lobby and the network limit straight away, and can't go below the number of players already in.
- **Players**: everyone connected, with their Steam avatar, ping, and Kick and Ban buttons.
- **Whitelist** (`WhitelistEnabled`). When it's on, only players you add can join. It starts empty each time you start the game. Players already in stay.
- **Banned players** (`BannedPlayers`). Ban for 1 hour, 1 day, 1 week or Permanent (shown in red). Each ban shows the time left and has an Unban button; timed bans end on their own and are saved between games. A banned player is refused at login and removed straight away if they're already in. Bans always win over the whitelist. Bans use the Steam ID that Steam verifies at login, so nobody can get someone else banned, and you can't ban yourself.
- **Picking from your Steam friends** for the whitelist and bans: friends playing Schedule I first, then online, then the rest, with a search box. A green dot means online, a green name means playing Schedule I, a red dot means banned.

### Fixed
- Open lobbies now let strangers play. The base game kicked anyone who isn't the host's Steam friend as they loaded in.

### Known
- A kicked or banned player's game says "Authentication Error" and still shows the Steam lobby. Steam doesn't let the host remove someone from a lobby, and the message comes from their game. A banned player can't get back in.

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
