# Troubleshooting

[Back to the README](../README.md)

Find the problem you're seeing below. Each entry says what to check and which log lines to look for.

**Where the logs are**

| Log | Location |
|---|---|
| MelonLoader log | `MelonLoader\Latest.log` in your game folder or r2modman profile |
| Game log | `%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Player.log` |

## The mod doesn't load

### `Undefined target method … Lobby_OnLobbyCreated_Postfix`

You're running the original ifBars DLL, which doesn't work on game 0.4.7. Replace it with the one from the [latest release](https://github.com/mdoodm/BiggerLobbies/releases/latest). See [coming from ifBars](coming-from-ifbars.md).

### `Error Generating Interop Assemblies! (Pass11ComputeTypeSpecifics)`

That MelonLoader install is too old for game 0.4.7. Update to v0.7.3, then delete these two folders so it rebuilds them on the next launch:

- `MelonLoader\Il2CppAssemblies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator`

If you use r2modman, check you launched with **Start modded** and not from Steam.

## Friends can't join

### A friend can't join after I've loaded in

Make sure `AlwaysHostLobby` and `LateJoinEnabled` are on. Your game only accepts players if it started online; the log shows `Always host lobby: lobby ready; loading as host` when it did. When someone joins mid-game, the log shows `Player joined mid-game`.

### A 6th player can't connect, even though the lobby has room

Update to 1.0.7 or later. When you load a save, your log should show `Steam transport player limit raised from 4 to 20`. Earlier versions left the game's limit of 4 connections in place.

### Friends see themselves in my lobby, but I don't see them

Check your log shows `BiggerLobbies v1.0.3` or later. When you host, it should show a single lobby ID in `Lobby <id> created with 20 slots`. If two different IDs appear, open an issue with the full log.

### A player who crashed gets an authentication error

Look for `Cleared any old Steam auth session` or `re-authenticated with Steam` in your log, and include those lines in a report.

### A friend joins, but their world is broken or the character creator won't respond

Check their `Player.log` for `PrefabId … is out of range`. It means you're running a mod that adds new objects and they aren't. Install the same content mods on both sides, or disable them while you play together.

## Lost progress or items

### Players lose money or items after a crash

Make sure `AutoSaveEnabled` and `PlayerSaveEnabled` are on. Your log shows `Autosaving …` for each world save, and `A player left; saving now` when someone drops. A crashed player can still lose up to `PlayerSaveSeconds` of progress.

Player saves run more often than world saves, so a player's file can be a few minutes ahead of the world. If the host crashes in that window, items a player just moved between their inventory and storage can be lost or duplicated. The base game behaves the same way when a player quits normally.

### Leaves, petrol or deliveries disappear from cauldrons or employees

BiggerLobbies doesn't touch items. This happens in the base game too, solo or with 4 players: employees delete what doesn't fit in their pockets or has nowhere to go, deliveries are lost on reload, and a cauldron throws away a batch whose quality can't stack with its output.

Try [ItemSafety](https://github.com/mdoodm/ItemSafety) (alpha, host-only). Until then, empty your employees' pockets by talking to them, and clear a cauldron's output before cooking a different leaf quality.

## Plants look wrong

### Some players don't see growth, or see "needs water" on a watered pot

Make sure `PlantSyncEnabled` is on. Players only see the water and growth display inside the property and within about 2.5 m of the pot, and growth is hidden while the management clipboard is equipped.

## Still stuck?

[Open a bug report](https://github.com/mdoodm/BiggerLobbies/issues/new?template=bug-report.yml). Attach your `MelonLoader\Latest.log` and `Player.log`, plus the joining player's `Player.log` if it's a join problem. Lines starting `[diag]` show each step of the lobby and join flow.
