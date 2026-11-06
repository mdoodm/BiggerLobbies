# BiggerLobbies

A mod for Schedule I that increases the maximum player limit in lobbies from 4 to 20 players.

## Features

- Increases lobby capacity from 4 to 20 players
- Dynamically creates UI slots for all players
- Compatible with both Mono and Il2Cpp builds

## Game 0.4.7 fix (Il2Cpp)

Game version 0.4.7 moved the Steam lobby code from `Lobby` into a new `SteamLobbyService` class, which broke the original patches (`Undefined target method ... Lobby_OnLobbyCreated_Postfix`). Version 1.0.3 updates the Il2Cpp build:

- Patches now target `SteamLobbyService` (`CreateLobby`, `OnLobbyCreated`, `UpdateLobbyMembers`) and `LobbyInterface.Start` (`Awake` no longer exists)
- `Lobby.PlayerLimit` is a constant in Il2Cpp and is never written (writing it crashes the game)
- Fixes a bug where one invite click created two Steam lobbies, so joining players ended up in a different lobby from the host and were never taken into the game
- Logs lobby steps with a `[diag]` prefix to help diagnose join problems

The Mono build still uses the original patches and has not been updated or tested for 0.4.7.

## Installation

1. Copy the compiled DLL for your target build (Mono or Il2Cpp) (`BiggerLobbies_Mono.dll` or `BiggerLobbies_Il2cpp.dll`) to your `Mods/` folder
2. Launch the game

## Requirements

- MelonLoader
- Schedule I (Mono or Il2Cpp build)

## Notes

- All players in a lobby should have this mod installed for best compatibility
- The mod uses Harmony patches to modify lobby creation and UI
- Steam lobby creation limit is set to 20 players