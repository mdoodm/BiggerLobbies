# BiggerLobbies

A mod for Schedule I that increases the maximum player limit in lobbies from 4 to 20 players.

## Features

- Increases lobby capacity from 4 to 20 players
- Dynamically creates UI slots for all players
- Updates Steam lobby metadata (`max_players` and `num_slots`)
- Compatible with both Mono and Il2Cpp builds

## Installation

1. Build the mod for your target build (Mono or Il2Cpp)
2. Copy the compiled DLL (`BiggerLobbies_Mono.dll` or `BiggerLobbies_Il2cpp.dll`) to your `Mods/` folder
3. Launch the game

## Requirements

- MelonLoader
- Schedule I (Mono or Il2Cpp build)

## Notes

- All players in a lobby must have this mod installed for best compatibility
- The mod uses Harmony patches to modify lobby creation and UI
- Steam lobby creation limit is set to 20 players

## Version

**1.0.0** by Bars

