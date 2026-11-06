# BiggerLobbies

A MelonLoader mod for **Schedule I** that raises the multiplayer lobby limit from 4 to 20 players.

This is a fork of [ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies), updated to work with **game version 0.4.7** (Il2Cpp build). The original mod stopped loading after that update.

## Features

- Raises the lobby size from 4 to 20 players
- Adds extra player slots to the lobby screen and shows the count as `Lobby (n/20)`
- Keeps the Invite button available until the lobby has 20 players

## Additions in this fork

- **Game 0.4.7 support** for the Il2Cpp build
- **Invites past 4 players:** opens the Steam invite window for players 5 to 20
- **One lobby per host:** only one Steam lobby is created at a time, and the invite window opens once the lobby is ready, so friends always join the host's lobby
- **Host autosave:** saves the game every 5 minutes, so a crash only loses the last few minutes instead of everything since the last sleep
- **Client player saves:** the host saves each connected player's money, inventory and position every 30 seconds
- **Save on leave:** when a player leaves or crashes, the host saves everything straight away, so restarting the session doesn't lose progress
- **Join a started game:** players can join or rejoin after the host has loaded in, without the host restarting (1.0.5, unstable)
- **Plant sync:** the host sends pot water levels and growth % to every player, so everyone sees the same "needs water" icon and growth progress
- **Settings** for autosave, player saves, plant sync and late join (see [Settings](#settings))
- **Diagnostic logging** of the lobby and join flow (`[diag]` lines in the log)

## Compatibility

| Build | Game 0.4.7 | Notes |
|-------|------------|-------|
| Il2Cpp (default Steam branch) | Supported | Tested with MelonLoader v0.7.3 |
| Mono (alternate branch) | Untested | Still uses the original patches, which target code that 0.4.7 removed. Expect it not to work. |

Requires **MelonLoader v0.7.3 or later**. MelonLoader v0.7.2 fails on game 0.4.7 before any mod loads (see [Troubleshooting](#troubleshooting)).

The mod declares itself incompatible with **ModManager & PhoneApp**, so MelonLoader won't load both at once.

## Installation

### r2modman / Thunderstore Mod Manager

1. Install MelonLoader v0.7.3 or later in your profile.
2. Copy `BiggerLobbies_Il2cpp.dll` into the profile's mod folder, replacing any older copy:
   `%AppData%\r2modmanPlus-local\ScheduleI\profiles\<profile>\Mods\ifBars-BiggerLobbies\mods\`
3. Launch with **Start modded** in r2modman.

Don't update or reinstall the Thunderstore package of BiggerLobbies afterwards. That puts the original DLL back, which doesn't work on 0.4.7.

### Manual

1. Install MelonLoader v0.7.3 or later into the game folder.
2. Copy `BiggerLobbies_Il2cpp.dll` into `Schedule I\Mods\`.
3. Launch the game.

## Settings

After the first launch, the settings are in `UserData\MelonPreferences.cfg` under `[BiggerLobbies]`:

| Setting | Default | What it does |
|---------|---------|--------------|
| `AutoSaveEnabled` | `true` | As host, save the game every few minutes |
| `AutoSaveMinutes` | `5` | Real-time minutes between autosaves |
| `PlantSyncEnabled` | `true` | As host, send pot water levels and growth progress to clients |
| `PlantSyncMinutes` | `30` | In-game minutes between full resyncs of each pot |
| `PlayerSaveEnabled` | `true` | As host, save each connected player's data between autosaves |
| `PlayerSaveSeconds` | `30` | Seconds between player saves |
| `LateJoinEnabled` | `true` | As host, let players join a game that has already started |

`AutoSaveEnabled` also controls the save that runs when a player leaves.

Player saves run more often than full saves, so a player's file can be up to `AutoSaveMinutes` ahead of the world. If the host crashes in that window, items a player moved between their inventory and storage can be lost or duplicated. The game already behaves this way when a player quits normally.

## Who needs the mod

Only the **host** needs it. Autosave, player saves, plant sync and late join also run on the host only. In testing, a player without the mod (and with no MelonLoader at all) joined a modded host and loaded into the game normally.

A joining player who does have MelonLoader needs v0.7.3 or later, or no MelonLoader at all. A broken v0.7.2 install on their side can stop the join from working.

## Troubleshooting

**`Undefined target method for patch method ... Lobby_OnLobbyCreated_Postfix`**
You're running the original DLL. Replace it with the one from this fork.

**`Error Generating Interop Assemblies!` (`Pass11ComputeTypeSpecifics`)**
That MelonLoader install is too old for game 0.4.7. Update to v0.7.3, then delete these two folders so it rebuilds them on the next launch:
- `MelonLoader\Il2CppAssemblies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator`

If you use r2modman, also check you launched with **Start modded** and not from Steam. Launching from Steam loads the MelonLoader in the game folder, which may be a different, older install.

**Friends show up in the lobby on their screen but not on yours, or stay in the menu when you load in**
Check the log starts with `BiggerLobbies v1.0.3` or later. When you host, the log should show one lobby ID in `Lobby <id> created with 20 slots`. If a second ID appears, open an issue and include the full log.

**Players lose money or items after a crash**
Without the mod, the game saves only when everyone sleeps or when a player quits normally. Make sure `AutoSaveEnabled` and `PlayerSaveEnabled` are on. The log shows `Autosaving …` for each full save and `A player left; saving now` when someone drops. A crashed player can still lose up to `PlayerSaveSeconds` of progress.

**A player can't join after the host has loaded in**
Make sure `LateJoinEnabled` is on. When someone joins mid-game the host log shows `Player joined mid-game; re-sent lobby 'ready'`. If a crashed player is refused at first, wait a few seconds for Steam to drop their old connection and try again.

**Some players don't see the growth % or see "needs water" when the pot is watered**
Make sure `PlantSyncEnabled` is on for the host. Clients only see the water and growth display when they're inside the property and within about 2.5 m of the pot, and the growth % is hidden while the management clipboard is equipped.

**Diagnostic logging**
The mod logs each step of the lobby and join flow with a `[diag]` prefix in `MelonLoader\Latest.log`. Include those lines when reporting a problem.

## Known limitations

- **Autosave** has been tested in game as host: it ran on schedule, used the game's normal save (`Saving game … Save complete!`), wrote the world and player files, and logged no errors.
- **Plant sync** has only been tested solo so far. The host-side code ran on a save with 24 growing pots and logged no errors, but it hasn't been checked with clients connected yet. The fix is based on reading the game's compiled code.
- **Player saves, save on leave and late join** (1.0.5) are an unstable release: built and loaded, but not yet tested in game. They're based on reading the game's compiled code.
- Lobbies above 4 have been played with 5 players (host plus 4) on 1.0.3. Larger lobbies haven't been tested.
- The extra lobby slots are copies of the game's own slot. If a future update changes the lobby screen layout, they may look wrong.
- Mono build: not updated for 0.4.7.

## Building from source

Requires the .NET SDK (6.0 or later).

1. Run the game once with MelonLoader so it generates `MelonLoader\Il2CppAssemblies`.
2. Build the `Il2cpp` configuration, pointing `GamePath` at your game folder or r2modman profile:
   ```
   dotnet build BiggerLobbies.csproj -c Il2cpp -p:GamePath="C:\path\to\Schedule I"
   ```

The project has a post-build step that **force-closes `Schedule I.exe`**, copies the DLL into `$(GamePath)\Mods`, and starts the game again. Remove the `PostBuild` target from `BiggerLobbies.csproj` if you don't want that.

Source layout:

- `Integrations/Il2CppLobbyPatches.cs`: lobby size, Steam lobby data, invite handling and lobby screen patches (Il2Cpp)
- `Integrations/LobbyDedupePatches.cs`: the duplicate-lobby fix (Il2Cpp)
- `Integrations/DiagnosticPatches.cs`: `[diag]` logging (Il2Cpp)
- `Integrations/AutoSave.cs`: host autosave, player saves, save on leave and the settings (Il2Cpp)
- `Integrations/GrowSyncFix.cs`: plant water/growth sync (Il2Cpp)
- `Integrations/LateJoinFix.cs`: joining a game that has already started (Il2Cpp)
- `Integrations/HarmonyPatches.cs`: the original patches (Mono build only)
- `Utils/Constants.cs`: `MAX_PLAYERS` (20) and mod info

## Credits

- Original mod by **Bars** ([ifBars](https://github.com/ifBars/BiggerLobbies)).
- 0.4.7 update by [mdoodm](https://github.com/mdoodm), with AI assistance.

See [LICENSE](LICENSE).
