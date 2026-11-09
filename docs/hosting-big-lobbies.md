# Hosting big lobbies

[Back to the README](../README.md)

BiggerLobbies hosts 20 players by default. You can set any size from 4 to 250.

## Change the lobby size

1. Launch the game once with BiggerLobbies, so it writes its settings.
2. Open `UserData\MelonPreferences.cfg` and find `[BiggerLobbies]`.
3. Set `MaxPlayers` to the size you want, for example `MaxPlayers = 40`.
4. Restart the game. The log shows `Lobby size: 40 players`.

Only the host's setting counts. Friends don't need to change anything, or install the mod at all.

## What players see

| Players in the lobby | Lobby screen |
|---|---|
| Up to 20 | One row of player icons, exactly like the base game |
| 21 to 100 | A table of rows of 20 under the title, with the Invite button beside the first row |
| Over 100 | The first 100 icons, with `+N` in the title for the rest |

## What sets the real limit

Every player connects to the host, and the host runs the world and passes on everyone's updates. So the practical size of a lobby depends on the host:

- **Upload speed** matters most: the host sends every player's updates to everyone else.
- **CPU** decides how smoothly the world runs with many players moving around.
- **Graphics** only matters for what's on screen: a crowd in view costs frame rate, one out of sight doesn't.

The base game also limits connections at the network level, to 4 besides the host. BiggerLobbies raises that to your `MaxPlayers` when a save loads, and logs it:

```
Steam transport player limit raised from 4 to 40 (loading save)
```

## What's been checked

- **Lobby screen:** checked with 40 and 100 slots.
- **A crowd on the host:** a save ran with 250 simulated players walking around at a playable frame rate.

## Tell us how it went

If you host a big session, [open a big session report](https://github.com/mdoodm/BiggerLobbies/issues/new?template=big-session.yml). How many people joined, the host's PC and connection, and how it played is the most useful information this project can get.

## Testing tools

Two settings let you check a big lobby on your own. They're for testing only: set them back to `0` before you play.

| Setting | What it does |
|---|---|
| `DebugFakePlayers` | Fills the lobby screen and the Host settings Players tab with this many test players, to check the layout. Test players borrow your Steam friends' names and avatars, and can't be kicked or banned |
| `DebugDummyPlayers` | 10 seconds after a save loads, spawns this many copies of your character walking around you, and logs your frame rate every 10 seconds |

A frame-rate line looks like this:

```
[dummies] 250 dummies: 32 fps average, 7 lowest (last 10 s)
```

Neither tool is real. The placeholders and dummies aren't networked, aren't in the player list and are never saved. They show how the lobby screen looks and what a crowd costs your PC to draw, not network traffic, joining or saving.
