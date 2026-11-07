<p align="center">
  <img src="assets/banner.svg" alt="BiggerLobbies: a Schedule I lobby with 20 player slots, 4 from the base game and 16 added by the mod" width="100%">
</p>

<p align="center">
  <b>Play Schedule I with up to 20 friends, rejoin after a crash, and stop losing progress.</b>
</p>

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/mdoodm/BiggerLobbies?label=release&color=F0A23B&labelColor=2A3746"></a>
  <img alt="Schedule I 0.4.7" src="https://img.shields.io/badge/Schedule%20I-0.4.7-ECE7DD?labelColor=2A3746">
  <img alt="MelonLoader 0.7.3 or later" src="https://img.shields.io/badge/MelonLoader-0.7.3%2B-ECE7DD?labelColor=2A3746">
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/github/license/mdoodm/BiggerLobbies?color=ECE7DD&labelColor=2A3746"></a>
</p>

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><b>Download the latest release</b></a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="#install">Install in 2 minutes</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="CHANGELOG.md">Changelog</a>
</p>

---

## Why use it

Schedule I's multiplayer stops at 4 players, and the host has to restart the session whenever something goes wrong. BiggerLobbies fixes the parts that get in the way of a long co-op night:

- **Bring the whole group.** Lobbies hold 20 players instead of 4, with a slot on the lobby screen for each of them.
- **Invite people any time.** Your game starts online even if you load straight into your save, so friends can accept an invite or use Steam's **Join Game** whenever they like.
- **Crashes don't end the session.** A player whose game crashes can rejoin without you restarting, and you don't have to sleep to save their progress.
- **Keep your progress.** The game normally saves only when everyone sleeps. The mod saves the world every 5 minutes, saves each player every 30 seconds, and saves straight away when someone drops.
- **Everyone sees the same plants.** Water levels and growth progress are kept in step for every player, so nobody sees a pot as dry when it isn't.

**Only the host needs the mod.** Your friends can join with an unmodded game.

## How this fork differs from the original

This is a fork of [ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies) 1.0.0, the version on Thunderstore. That version was written before game 0.4.7 and stopped loading after the update.

| | ifBars 1.0.0 | This fork |
|---|---|---|
| Works on game 0.4.7 | No, fails to load | Yes (Il2Cpp build) |
| Builds | Mono and Il2Cpp | Il2Cpp; the Mono build is unchanged from the original |
| Lobby size | 20 | 20 |
| Who needs the mod | Recommends every player installs it | Only the host |
| Starting online | Only if you press Invite in the main menu first | Always, even if you load straight into your save |
| Joining a started game | Not possible; the host has to restart | Supported |
| Rejoining after a crash | Blocked by the player's old Steam session | Supported |
| Saving | The game's own saves only: when everyone sleeps, or when a player quits normally | Autosave every 5 minutes, player saves every 30 seconds, and a save when someone drops |
| Plant water and growth | Can differ between players (base-game behaviour) | Kept in step by the host |
| Settings | None | 8 settings in `MelonPreferences.cfg` |
| Diagnostic logging | None | `[diag]` lines for the lobby and join flow |
| Dependencies | MelonLoader 0.7.0, SwapperPlugin | MelonLoader 0.7.3 or later |

### Issues fixed

Problems with the original mod on game 0.4.7:

- **The mod failed to load** with `Undefined target method for patch method … Lobby_OnLobbyCreated_Postfix`. Game 0.4.7 moved the Steam lobby code from `Lobby` into a new `SteamLobbyService` class, and the lobby screen lost the `Awake` method the original patched. The patches now target the new code.
- **Setting the player limit crashed the game.** In the Il2Cpp build `Lobby.PlayerLimit` is a compile-time constant, and writing to it causes an access violation. The 20-player limit is now applied when the lobby is created instead.
- **Friends joined but stayed in the main menu.** One click on Invite could create two Steam lobbies. The host's game tracked one while friends joined the other, so they never saw each other and the "start game" message never reached them. Only one lobby is created now.
- **A player joining with 4 or fewer in the lobby loaded twice and failed authentication.** The "ready" message is now only re-sent when the base game can't connect a player by itself, with 5 or more in the lobby.

Problems in the base game that the fork works around:

- **Inviting from a save you loaded directly didn't work,** and Steam's Join Game was unavailable. The game only starts online if a lobby exists before the save loads, so the fork creates one automatically.
- **Players couldn't join after the host had loaded in.** A joining player's game only handles 4 lobby members and fails with more. The host now re-sends the "ready" message so they connect another way.
- **A player who crashed couldn't rejoin.** Their old Steam session stayed open (`k_EBeginAuthSessionResultDuplicateRequest`), and a game that sent its authentication twice was kicked. The host now clears the old session and re-checks the second request with Steam.
- **A crash lost everything since the last sleep.** The game only saves at sleep or when a player quits normally. The fork adds autosave, player saves and a save on leave.
- **Some players saw the wrong "needs water" icon or frozen growth.** Botanist watering and the overnight water drain are only applied on the host. The host now sends the real values to every player.

## Install

You need **Schedule I 0.4.7** (the default Steam branch) and **MelonLoader v0.7.3 or later**.

### With r2modman

1. Download `BiggerLobbies_Il2cpp.dll` from the [latest release](https://github.com/mdoodm/BiggerLobbies/releases/latest).
2. In your profile, create the folder `Mods\mdoodm-BiggerLobbies\mods\` and put the DLL in it. Your profile is in `%AppData%\r2modmanPlus-local\ScheduleI\profiles\<profile>\`.
3. Disable or uninstall the original **ifBars-BiggerLobbies** package, so the two don't load together.
4. Launch with **Start modded**.

### Without a mod manager

1. Install MelonLoader v0.7.3 or later into the game folder.
2. Put `BiggerLobbies_Il2cpp.dll` in `Schedule I\Mods\`.
3. Launch the game.

To check your download, compare its SHA-256 with the hash on the release page:

```
Get-FileHash .\BiggerLobbies_Il2cpp.dll -Algorithm SHA256
```

## What's included

| | Feature | What it does |
|---|---|---|
| Lobby | 20 players | Raises the lobby size from 4 to 20 and shows `Lobby (n/20)` on the lobby screen |
| | Invites past 4 | Keeps the Invite button working until the lobby is full |
| | Always online | Creates a Steam lobby as your save loads, so you can invite people without going back to the menu. Loads solo if Steam is offline |
| | One lobby per host | Stops a double click on Invite from creating two lobbies, which left friends stuck in the menu |
| Joining | Join a started game | Lets players join or rejoin after you've loaded in |
| | Rejoin after a crash | Clears a crashed player's old Steam session, and re-checks a player whose game authenticates twice instead of kicking them |
| Saving | Autosave | Runs the game's normal save every 5 minutes |
| | Player saves | Saves each connected player's money, inventory and position every 30 seconds |
| | Save on leave | Saves everything the moment a player leaves or crashes |
| Farming | Plant sync | Sends pot water levels and growth progress to every player |

The fork also logs each step of the lobby and join flow (lines starting `[diag]`), which helps when reporting a problem.

## Settings

Every feature is on by default. After the first launch you can change them in `UserData\MelonPreferences.cfg`, under `[BiggerLobbies]`:

| Setting | Default | What it does |
|---|---|---|
| `AlwaysHostLobby` | `true` | Create a Steam lobby automatically when you load a save |
| `LateJoinEnabled` | `true` | Let players join a game that has already started |
| `AutoSaveEnabled` | `true` | Save the game every few minutes, and when a player leaves |
| `AutoSaveMinutes` | `5` | Real-time minutes between autosaves |
| `PlayerSaveEnabled` | `true` | Save each connected player between autosaves |
| `PlayerSaveSeconds` | `30` | Seconds between player saves |
| `PlantSyncEnabled` | `true` | Send pot water levels and growth progress to players |
| `PlantSyncMinutes` | `30` | In-game minutes between full resyncs of each pot |

Player saves run more often than full saves, so a player's file can be a few minutes ahead of the world. If the host crashes in that window, items a player just moved between their inventory and storage can be lost or duplicated. The base game behaves the same way when a player quits normally.

## Before you play

- **Your friends don't need the mod.** If they do run MelonLoader, it has to be v0.7.3 or later. A broken v0.7.2 install on their side can stop them joining.
- **Everyone needs the same content mods.** If you host with a mod that adds new items or objects and a friend doesn't have it, their world won't load properly.
- **Launch from r2modman, not Steam.** Launching from Steam uses whatever MelonLoader is in the game folder, which may be an older one.
- **This is the Il2Cpp build only.** The Mono (alternate) branch still uses the original patches and isn't expected to work on 0.4.7.
- **ModManager & PhoneApp won't load alongside it.** The mod declares itself incompatible with that one.

## Troubleshooting

<details>
<summary><b>Undefined target method … Lobby_OnLobbyCreated_Postfix</b></summary>

You're running the original ifBars DLL. Replace it with the one from this fork's [latest release](https://github.com/mdoodm/BiggerLobbies/releases/latest).
</details>

<details>
<summary><b>Error Generating Interop Assemblies! (Pass11ComputeTypeSpecifics)</b></summary>

That MelonLoader install is too old for game 0.4.7. Update to v0.7.3, then delete these two folders so it rebuilds them on the next launch:

- `MelonLoader\Il2CppAssemblies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator`

If you use r2modman, check you launched with **Start modded** and not from Steam.
</details>

<details>
<summary><b>A friend can't join after I've loaded in</b></summary>

Make sure `AlwaysHostLobby` and `LateJoinEnabled` are on. Your game only accepts players if it started online; the log shows `Always host lobby: lobby ready; loading as host` when it did. When someone joins mid-game, the log shows `Player joined mid-game`.

If a player who crashed gets an authentication error, look for `Cleared any old Steam auth session` or `re-authenticated with Steam` in your log and include it in a report.
</details>

<details>
<summary><b>Friends see themselves in my lobby, but I don't see them</b></summary>

Check your log shows `BiggerLobbies v1.0.3` or later. When you host, it should show a single lobby ID in `Lobby <id> created with 20 slots`. If two different IDs appear, open an issue with the full log.
</details>

<details>
<summary><b>A friend joins but their world is broken, or the character creator won't respond</b></summary>

Check their `Player.log` for `PrefabId … is out of range`. It means you're running a mod that adds new objects and they aren't. Install the same content mods on both sides, or disable them while you play together.
</details>

<details>
<summary><b>Players lose money or items after a crash</b></summary>

Make sure `AutoSaveEnabled` and `PlayerSaveEnabled` are on. Your log shows `Autosaving …` for each full save and `A player left; saving now` when someone drops. A crashed player can still lose up to `PlayerSaveSeconds` of progress.
</details>

<details>
<summary><b>Some players don't see growth progress, or see "needs water" on a watered pot</b></summary>

Make sure `PlantSyncEnabled` is on. Players only see the water and growth display inside the property and within about 2.5 m of the pot, and growth progress is hidden while the management clipboard is equipped.
</details>

Still stuck? [Open an issue](https://github.com/mdoodm/BiggerLobbies/issues) with your `MelonLoader\Latest.log` and `Player.log` (in `%AppData%\..\LocalLow\TVGS\Schedule I\`), plus the joining player's `Player.log` if it's a join problem. Include the `[diag]` lines.

## What's been tested

| Feature | Status |
|---|---|
| 20-player lobbies | Played with 5 players (host plus 4). Larger lobbies haven't been tested yet |
| Always online, joining a started game | Tested with one friend joining through Steam's Join Game |
| Joining a started game with 5 or more players | Not tested yet |
| Rejoin after a crash | Tested over several crash-and-rejoin cycles |
| Autosave, player saves, save on leave | Tested in game |
| Plant sync | Runs without errors on a save with 24 growing pots; not yet checked with friends connected |

The extra lobby slots are copies of the game's own slot, so a future update that changes the lobby screen could make them look wrong.

## Build from source

You need the .NET SDK (6.0 or later).

1. Run the game once with MelonLoader so it generates `MelonLoader\Il2CppAssemblies`.
2. Build the `Il2cpp` configuration, pointing `GamePath` at your game folder or r2modman profile:

   ```
   dotnet build BiggerLobbies.csproj -c Il2cpp -p:GamePath="C:\path\to\Schedule I"
   ```

The project's post-build step **force-closes `Schedule I.exe`**, copies the DLL into `$(GamePath)\Mods` and starts the game again. Remove the `PostBuild` target from `BiggerLobbies.csproj` if you don't want that.

<details>
<summary>Source layout</summary>

| File | What's in it |
|---|---|
| `Integrations/Il2CppLobbyPatches.cs` | Lobby size, Steam lobby data, invites and the lobby screen |
| `Integrations/LobbyDedupePatches.cs` | One lobby per host |
| `Integrations/AlwaysHostLobby.cs` | Creating a lobby as a save loads |
| `Integrations/LateJoinFix.cs` | Joining a game that has already started |
| `Integrations/StaleAuthFix.cs` | Rejoining after a crash |
| `Integrations/AutoSave.cs` | Autosave, player saves, save on leave and the settings |
| `Integrations/GrowSyncFix.cs` | Plant sync |
| `Integrations/DiagnosticPatches.cs` | `[diag]` logging |
| `Integrations/HarmonyPatches.cs` | The original patches, used by the Mono build only |
| `Utils/Constants.cs` | Player limit and mod info |

</details>

## Credits

BiggerLobbies was created by **Bars** ([ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies)). The 0.4.7 update and the additions in this fork are by [mdoodm](https://github.com/mdoodm), with AI assistance. Released under the [MIT licence](LICENSE).

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><b>Download the latest release</b></a>
</p>
