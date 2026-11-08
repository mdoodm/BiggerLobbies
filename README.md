<p align="center">
  <img src="assets/banner-lobbies.svg" alt="BiggerLobbies: bigger lobbies for Schedule I. 4 slots in the base game, 16 more by default, and up to 250 with the MaxPlayers setting" width="100%">
</p>

<p align="center">
  <b>Host Schedule I for your whole crew. Friends join any time, rejoin after a crash, and nobody loses progress.</b>
</p>

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/mdoodm/BiggerLobbies?label=release&color=F0A23B&labelColor=2A3746"></a>
  <img alt="Schedule I 0.4.7" src="https://img.shields.io/badge/Schedule%20I-0.4.7-ECE7DD?labelColor=2A3746">
  <img alt="MelonLoader 0.7.3 or later" src="https://img.shields.io/badge/MelonLoader-0.7.3%2B-ECE7DD?labelColor=2A3746">
  <img alt="Only the host installs it" src="https://img.shields.io/badge/installed%20by-host%20only-ECE7DD?labelColor=2A3746">
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/github/license/mdoodm/BiggerLobbies?color=ECE7DD&labelColor=2A3746"></a>
</p>

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><b>Download the latest release</b></a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="#install">Install</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="docs/troubleshooting.md">Troubleshooting</a>
  &nbsp;&nbsp;|&nbsp;&nbsp;
  <a href="CHANGELOG.md">Changelog</a>
</p>

---

## What you get

Schedule I's co-op stops at 4 players, and one crash can end the night. BiggerLobbies takes those limits away. Only the host installs it; everyone else joins with the normal game.

- **Room for everyone.** 20 players by default, and anything up to 250 if your PC and connection can carry it.
- **Invite whenever you like.** Your game goes online as your save loads, so friends can accept an invite or use Steam's **Join Game** at any point in the session.
- **Crashes don't end the session.** A player whose game crashes can come straight back in, without you restarting.
- **Nobody loses progress.** The world saves every 5 minutes, each player every 30 seconds, and everything saves the moment someone drops.
- **Everyone sees the same plants.** Water levels and growth stay in step for every player, so a watered pot never shows as dry.

## Install

You need **Schedule I 0.4.7** (the default Steam branch).

1. Install <a href="https://github.com/LavaGang/MelonLoader/releases/latest"><b>MelonLoader v0.7.3</b></a> or later into the game folder.
2. Download `BiggerLobbies_Il2cpp.dll` from the [latest release](https://github.com/mdoodm/BiggerLobbies/releases/latest) and put it in `Schedule I\Mods\`.
3. Launch the game, load a save, and invite your friends.

Each release lists the DLL's SHA-256. To check your download in PowerShell:

```
Get-FileHash .\BiggerLobbies_Il2cpp.dll -Algorithm SHA256
```

> [!TIP]
> **Losing leaves, petrol or deliveries?** That's a bug in the base game, not the lobby. The companion mod [**ItemSafety**](https://github.com/mdoodm/ItemSafety) catches it on the host. It's in alpha, so back up your saves first.

## Settings

Everything is on by default. After the first launch, change settings in `UserData\MelonPreferences.cfg` under `[BiggerLobbies]`, then restart the game.

| Setting | Default | What it does |
|---|---|---|
| `MaxPlayers` | `20` | Lobby size, from 4 to 250. See [hosting big lobbies](docs/hosting-big-lobbies.md) |
| `AlwaysHostLobby` | `true` | Go online automatically when you load a save |
| `LateJoinEnabled` | `true` | Let players join a game that has already started |
| `AutoSaveEnabled` | `true` | Save the world every few minutes, and when a player leaves |
| `AutoSaveMinutes` | `5` | Real-time minutes between world saves |
| `PlayerSaveEnabled` | `true` | Save each connected player between world saves |
| `PlayerSaveSeconds` | `30` | Seconds between player saves |
| `PlantSyncEnabled` | `true` | Send pot water levels and growth to every player |
| `PlantSyncMinutes` | `30` | In-game minutes between full resyncs of each pot |

There are also two [testing tools](docs/hosting-big-lobbies.md#testing-tools) for checking a big lobby without a big group. Leave them at `0`.

## Good to know

- **Your friends don't need the mod.** If they run MelonLoader for other mods, it must be v0.7.3 or later.
- **Everyone needs the same content mods.** If you host with a mod that adds new items or objects, every player needs it too, or their world won't load properly.
- **Using r2modman?** Launch with **Start modded**, not from Steam.
- **Il2Cpp build only.** That's the default Steam branch; the Mono (alternate) branch isn't supported on 0.4.7.
- **Not compatible with ModManager & PhoneApp.** MelonLoader won't load BiggerLobbies alongside it.

## Read more

| Page | For when you want to |
|---|---|
| [Hosting big lobbies](docs/hosting-big-lobbies.md) | Go past 20 players, understand the limits, or test a big lobby on your own |
| [Troubleshooting](docs/troubleshooting.md) | Fix a load error, a friend who can't join, or lost progress |
| [Coming from ifBars/BiggerLobbies](docs/coming-from-ifbars.md) | Switch from the original mod and see what changed |
| [How it works](docs/how-it-works.md) | Read what each fix does inside the game, or build from source |
| [Changelog](CHANGELOG.md) | See what's new in each version |

Found a bug, or ran a big session? [Open an issue](https://github.com/mdoodm/BiggerLobbies/issues/new/choose). The templates ask for the logs that help.

## Credits

BiggerLobbies was created by **Bars** ([ifBars/BiggerLobbies](https://github.com/ifBars/BiggerLobbies)). This fork, with the 0.4.7 update and everything since, is by [mdoodm](https://github.com/mdoodm), with AI assistance. Released under the [MIT licence](LICENSE).

<p align="center">
  <a href="https://github.com/mdoodm/BiggerLobbies/releases/latest"><b>Download the latest release</b></a>
</p>
