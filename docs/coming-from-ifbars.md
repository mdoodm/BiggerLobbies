# Coming from ifBars/BiggerLobbies

[Back to the README](../README.md)

This fork picks up [ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies) 1.0.0, the version on Thunderstore, which stopped loading when Schedule I updated to 0.4.7.

## Switching over

1. Disable or uninstall the ifBars package (in r2modman, or delete its DLL from `Mods`).
2. Follow the [install steps](../README.md#install).
3. Friends who installed the ifBars version can remove it. Only the host needs this fork.

## What's different

| | ifBars 1.0.0 | This fork |
|---|---|---|
| Game 0.4.7 | Fails to load | Works |
| Who installs it | Every player | Only the host |
| Lobby size | 20 | 20 by default, anything from 4 to 250 |
| Inviting after you load a save | Only if you pressed Invite in the menu first | Always |
| Joining a game in progress | Host must restart | Supported |
| Rejoining after a crash | Blocked | Supported |
| Saving | Only at sleep or a clean quit | Every 5 min, players every 30 s, and when someone drops |
| Plant water and growth | Can differ between players | Same for everyone |
| Settings | None | 9, plus 2 testing tools |
| Builds | Mono and Il2Cpp | Il2Cpp (Mono left as it was) |

## Issues fixed

| What you saw | What the fork does |
|---|---|
| The mod didn't load on 0.4.7 | Patches the game's new lobby code |
| The game crashed when the mod set the player limit | Applies the player limit when the lobby is created |
| The lobby had room, but a 6th player couldn't connect | Raises the game's network connection limit to the lobby size |
| Friends joined but stayed stuck in the main menu | Creates only one Steam lobby per host |
| No way to invite anyone after loading a save directly | Creates a lobby before the save loads |
| Friends couldn't join once you'd loaded in | Re-sends the "game is ready" message to players who join late |
| A friend who crashed couldn't get back in | Clears their old Steam session and re-checks them instead of kicking them |
| A crash lost everything since the last sleep | Autosaves, saves players often, and saves when someone drops |
| Some players saw "needs water" on watered pots, or growth that never moved | Sends every player the host's water and growth values |

How each fix works is in [How it works](how-it-works.md).
