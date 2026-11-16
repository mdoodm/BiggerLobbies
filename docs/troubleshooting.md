# Troubleshooting

[Back to the README](../README.md)

Find the problem you're seeing below. Each entry says what to check and which log lines to look for.

**Where the logs are**

| Log | Location |
|---|---|
| MelonLoader log | `MelonLoader\Latest.log` in your game folder or r2modman profile |
| Game log | `%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Player.log` |

BiggerLobbies only writes warnings and errors by default. Most log lines quoted on this page appear only with `DiagnosticLog = true` under `[BiggerLobbies]` in `UserData\MelonPreferences.cfg`. Turn it on, close the game, then reproduce the problem.

## The mod doesn't load

### `Undefined target method … Lobby_OnLobbyCreated_Postfix`

You're running the original ifBars DLL, which doesn't work on game 0.4.7. Replace it with the one from the [latest release](https://github.com/mdoodm/BiggerLobbies/releases/latest). See [coming from ifBars](coming-from-ifbars.md).

### `Failed to load Melon 'BiggerLobbies_Il2cpp'` on the `alternate` branch

That's expected if both DLLs are in `Mods`. `BiggerLobbies_Il2cpp.dll` is for the default branch, and the Mono game can't read it, so MelonLoader prints an error with a stack trace and moves on. Check for `BiggerLobbies (Mono) v…` and `1 Mod loaded` just below it: that's the build that runs. Delete `BiggerLobbies_Il2cpp.dll` if you'd rather not see the error.

### `'BiggerLobbies (Mono)' is incompatible … Domain: MONO` on the default branch

Also expected with both DLLs in `Mods`: MelonLoader skips the Mono build and loads `BiggerLobbies_Il2cpp.dll`. Delete `BiggerLobbies_Mono.dll` if you'd rather not see the notice.

### `This is the … build of BiggerLobbies, but the game is the … branch`

The DLL doesn't match your Steam branch, and nothing else is loaded. Usually MelonLoader skips the wrong build before this point, so seeing it means it got further. Use the DLL the message names. To see your branch, open the game's **Properties** in Steam, then **Betas**: "None" is the default branch (`BiggerLobbies_Il2cpp.dll`), and `alternate` is Mono (`BiggerLobbies_Mono.dll`). After switching branch, keep both DLLs or swap to the other one.

### `Error Generating Interop Assemblies! (Pass11ComputeTypeSpecifics)`

Default branch only. That MelonLoader install is too old for game 0.4.7. Update to v0.7.3, then delete these two folders so it rebuilds them on the next launch:

- `MelonLoader\Il2CppAssemblies`
- `MelonLoader\Dependencies\Il2CppAssemblyGenerator`

If you use r2modman, check you launched with **Start modded** and not from Steam.

### I updated, but nothing changed

Check the top of `Latest.log` for the version, for example `BiggerLobbies v2.2.0` (or `BiggerLobbies (Mono) v2.2.0` on the `alternate` branch). If it's older, a mod manager may have put its own copy back: update the mod inside r2modman, or close r2modman before copying the DLL in by hand.

## Host settings

### I can't find Host settings

It's under **Settings** in the main menu, and in the pause menu while you host. **F6** opens it while you host; change the key with `HostPanelKey`. In someone else's game the pause menu doesn't show it.

### Take items says "They only just left" or "Your inventory is full"

A player's own game saves their inventory as they leave, so Take items waits about 10 seconds after someone goes. If your inventory is full, it takes what fits and leaves the rest: make room and press it again. Each take is listed under **Taken items**, and the player's original inventory is copied to `UserData\BiggerLobbies\Taken`.

### Give back gives back less than I took

Give back never makes items. It only returns what you still carry, matching the item exactly (same quality and packaging), and only into the player's slots that are still empty. If you're short of an item, its slot gets what you have and isn't topped up later, because it's no longer empty. Items you had none of, and cash, can be given back with another press once you have them. Each give back is listed under **Taken items** too.

## Friends can't join

### A friend gets "Version Mismatch"

The host and that player are on different Steam branches: the popup shows `0.4.7f12` for the default branch and `0.4.7f12 Alternate` for Mono. The game itself refuses the join, with or without BiggerLobbies.

Either put everyone on the same branch, under the game's **Properties**, then **Betas** in Steam, or turn on the experimental [Other branches](experimental.md#playing-across-branches) and invite them with **Invite (other branch)**. With it on, a player who uses **Join Game** gets the mismatch once and gets in on a second try within 15 seconds.

### A friend can't join after I've loaded in

Make sure `AlwaysHostLobby` and `LateJoinEnabled` are on. Your game only accepts players if it started online; the log shows `Always host lobby: lobby ready; loading as host` when it did. When someone joins mid-game, the log shows `Player joined mid-game`.

### Large lobbies: players past 250 can't get in

Steam's **Join Game** only reaches your main lobby, which holds 250. Invite them instead: once your lobby is full, the invite button sends them to an extra lobby. On the Experimental tab, the line under **Large lobby size** should say how many extra lobbies are open. If it stays at "opening an extra lobby", look for `Extra lobby: Steam couldn't create it` in the log. See [Experimental features](experimental.md#large-lobbies).

### Nobody can join, and the whitelist is on

The whitelist starts empty every time you start the game, and the log warns `Whitelist is on and empty`. Add players in Host settings on the **Access** tab, or load a saved crew. A refused player shows as `Refused … not on the whitelist`.

Turning the whitelist on, or loading a crew, also removes players who are already in and aren't on the list. While the list is empty nobody is removed, so you can switch it on first and add players after.

### Someone who isn't my Steam friend can't join

Set **Who can join** to **Open** on the Lobby tab (`LobbyPrivacy = Open`). Friends only and Private lobbies only let in your friends and people you invite.

### A 6th player can't connect, even though the lobby has room

Update to 1.0.7 or later. When you load a save, your log should show `Steam transport player limit raised from 4 to 20`. Earlier versions left the game's limit of 4 connections in place.

### Friends see themselves in my lobby, but I don't see them

Check your log shows `BiggerLobbies v1.0.3` or later. When you host, it should show a single lobby ID in `Lobby <id> created with 20 slots`. If two different IDs appear, open an issue with the full log.

### A player who crashed gets an authentication error

Look for `Cleared any old Steam auth session` or `re-authenticated with Steam` in your log, and include those lines in a report. Also check they aren't banned: banned players see the same error if they don't have the mod.

### A friend joins, but their world is broken or the character creator won't respond

Check their `Player.log` for `PrefabId … is out of range`. It means you're running a mod that adds new objects and they aren't. Install the same content mods on both sides, or disable them while you play together.

## Kicked and banned players

### A kicked player says "Authentication Error" and still sees the lobby

That's what players without BiggerLobbies see. Steam doesn't let a host remove anyone from a Steam lobby, so their game keeps showing it. They're gone from your game and from your lobby screen, and a banned player can't get back in. Players who have BiggerLobbies go back to the main menu with "Kicked by the host", "Banned from this lobby" or "Not on the whitelist" instead.

### A ban didn't stop someone

Bans use the Steam ID Steam checks at login, so a renamed account is still banned. Check the ban is still listed on the **Access** tab: timed bans end on their own. A refused player shows as `Refused … banned` in the log.

### Host settings says "Couldn't ban …"

The player has no verified Steam ID yet (they're still connecting), or it's your own. The player is kicked instead and the reason you typed is kept, so you can ban them once they're back in.

## Discord

### Discord doesn't show anything

Discord has to be running on the same PC before the game starts, and the log shows `Discord presence: couldn't reach Discord` if it can't be found. The mod connects through Discord's Windows pipe, so on Linux or Steam Deck under Proton it only works with a Discord IPC bridge for Wine. Without one, the mod retries quietly, waiting longer each time.

## Lost progress or items

### Players lose money or items after a crash

Make sure `AutoSaveEnabled` and `PlayerSaveEnabled` are on, or check the **Saving** tab in Host settings. Your log shows `Autosaving …` for each world save, and `A player left; saving now` when someone drops. A crashed player can still lose up to `PlayerSaveSeconds` of progress.

Player saves run more often than world saves, so a player's file can be a few minutes ahead of the world. If the host crashes in that window, items a player just moved between their inventory and storage can be lost or duplicated. The base game behaves the same way when a player quits normally.

### A player lost their clothes after a crash

Update to 2.0.1 or later on the host. To get the outfit back, close the game and open `UserData/BiggerLobbies/PlayerBackups/<save>/<Steam ID>/`, which holds the last 10 versions of that player's files. Pick the newest folder from before the loss, and copy its `Clothing.json` into `Players/Player_<Steam ID>` in the save. Copy `Inventory.json` too only if their items were lost as well, because it also brings back items they've used or sold since.

### Leaves, petrol or deliveries disappear from cauldrons or employees

BiggerLobbies doesn't touch those items. This happens in the base game too, solo or with 4 players: employees delete what doesn't fit in their pockets or has nowhere to go, deliveries are lost on reload, and a cauldron throws away a batch whose quality can't stack with its output.

Try [ItemSafety](https://github.com/mdoodm/ItemSafety) (alpha, host-only). Until then, empty your employees' pockets by talking to them, and clear a cauldron's output before cooking a different leaf quality.

## Plants look wrong

### Some players don't see growth, or see "needs water" on a watered pot

Make sure `PlantSyncEnabled` is on. Players only see the water and growth display inside the property and within about 2.5 m of the pot, and growth is hidden while the management clipboard is equipped.

## Still stuck?

[Open a bug report](https://github.com/mdoodm/BiggerLobbies/issues/new?template=bug-report.yml). Turn on `DiagnosticLog`, reproduce the problem, then attach your `MelonLoader\Latest.log` and `Player.log`, plus the joining player's `Player.log` if it's a join problem. Lines starting `[diag]` show each step of the lobby and join flow.
