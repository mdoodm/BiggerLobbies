# BiggerLobbies

A MelonLoader mod for **Schedule I** that raises the multiplayer lobby limit from 4 to 20 players.

This is a fork of [ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies), updated to work with **game version 0.4.7** (Il2Cpp build). The original mod stopped loading after that update.

## Features

- Raises the lobby size from 4 to 20 players
- Adds extra player slots to the lobby screen and shows the count as `Lobby (n/20)`
- Keeps the Invite button available until the lobby has 20 players
- Fixes a bug where a joining player and the host ended up in two different Steam lobbies (see [What changed in 1.0.3](#what-changed-in-103))
- **Host autosave:** saves the game every 5 minutes so a crash doesn't wipe progress back to the last sleep (see [What changed in 1.0.4](#what-changed-in-104))
- **Plant sync:** keeps pot water levels and growth % the same for every player, so clients don't see a wrong "needs water" icon or frozen growth

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

## Who needs the mod

Only the **host** needs it. Autosave and plant sync also run on the host only. In testing, a player without the mod (and with no MelonLoader at all) joined a modded host and loaded into the game normally.

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
That was the duplicate-lobby bug fixed in 1.0.3. Check the log starts with `BiggerLobbies v1.0.3`. When you host, the log should show one lobby ID in `Lobby <id> created with 20 slots`. If a second ID appears, open an issue and include the full log.

**Players lose money or items after a crash**
Without autosave, the game saves only when everyone sleeps or when a player quits normally. Make sure `AutoSaveEnabled` is on; the log shows `Autosaving …` each time it runs. A crash can still lose up to `AutoSaveMinutes` of progress.

**Some players don't see the growth % or see "needs water" when the pot is watered**
Make sure `PlantSyncEnabled` is on for the host. Clients only see the water and growth display when they're inside the property and within about 2.5 m of the pot, and the growth % is hidden while the management clipboard is equipped.

**Diagnostic logging**
The mod logs each step of the lobby and join flow with a `[diag]` prefix in `MelonLoader\Latest.log`. Include those lines when reporting a problem.

## What changed in 1.0.4

Both problems below are in the base game, not caused by the larger lobby. They happen with 4 players too.

- **Host autosave.** The game saves only when everyone sleeps, or when a client quits normally and its game asks the host to save it. If a client crashes, that request never arrives and they lose everything since the last sleep; if the host crashes, everyone does. The host now runs the game's own full save (the same one used at sleep) every `AutoSaveMinutes`, skipping while a save or sleep is already in progress. The world and all connected players are saved together, so they stay consistent.
- **Plant sync.** The game doesn't stream pot water and growth to clients. Every machine simulates each pot on its own, and the host only corrects clients on some events. Two server-only events never reach clients: a **botanist watering** a pot, and the water drain applied during **sleep**. Clients then see a pot as dry when it isn't (and its growth % stops rising on their screen), or never see "needs water" when it is dry. Players who joined after the event see the right values, which is why only some players are affected. The host now sends the real water level after both events, and resends every pot's water and growth every `PlantSyncMinutes`.

## What changed in 1.0.3

Game 0.4.7 moved the Steam lobby code out of the `Lobby` class into a new `SteamLobbyService` class. The original patches pointed at methods and fields that no longer exist, so Harmony refused to load them.

For the Il2Cpp build:

- **Retargeted patches.** Lobby creation, the lobby-created callback and member tracking are now patched on `SteamLobbyService`. The lobby screen is patched on `LobbyInterface.Start`, because `Awake` no longer exists.
- **`Lobby.PlayerLimit` is left alone.** In Il2Cpp it's a compile-time constant, and writing to it crashes the game. The 20-player limit is applied when the lobby is created instead, and Steam's member limit is set to match.
- **Invites past 4 players.** Below the game's normal 4-player limit, the game's own invite code runs. Above it, the mod opens the Steam invite window itself, up to 20.
- **Duplicate-lobby fix.** One click on Invite could create two Steam lobbies. The host's game tracked the second while friends joined the first, so neither saw the other and the "start game" message never reached them. The mod now allows only one lobby creation at a time and waits until the lobby exists before opening the invite window.
- **Diagnostic logging** added (`[diag]` lines).

The Mono build is unchanged from the original.

## Known limitations

- **Autosave** has been tested in game as host: it ran on schedule, used the game's normal save (`Saving game … Save complete!`), wrote the world and player files, and logged no errors.
- **Plant sync** has only been tested solo so far. The host-side code ran on a save with 24 growing pots and logged no errors, but it hasn't been checked with clients connected yet. The fix is based on reading the game's compiled code.
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
- `Integrations/AutoSave.cs`: host autosave and the settings (Il2Cpp)
- `Integrations/GrowSyncFix.cs`: plant water/growth sync (Il2Cpp)
- `Integrations/HarmonyPatches.cs`: the original patches (Mono build only)
- `Utils/Constants.cs`: `MAX_PLAYERS` (20) and mod info

## Credits

- Original mod by **Bars** ([ifBars](https://github.com/ifBars/BiggerLobbies)).
- 0.4.7 update by [mdoodm](https://github.com/mdoodm), with AI assistance.

See [LICENSE](LICENSE).
