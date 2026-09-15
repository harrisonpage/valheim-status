# valheim-status

A tiny server-side [BepInEx] plugin for Valheim dedicated servers. Every few
seconds it writes a JSON status file straight from the running game: players
online, in-game day and time, bosses defeated (global keys), world name and
seed, server version. Serve the file with any web server and point your bot at
it.

It replaces the usual approach of tailing the server log, which can only
report what the log happens to print. Clients need nothing; the plugin never
patches game code, it only reads.

```bash
curl -s https://valheim.example.com/valheim.json | python3 -m json.tool
```

```json
{
    "version": "1.0.12",
    "version_string": "l-1.0.12",
    "running": true,
    "players": {
        "2552654088": {
            "player": "Bjorn",
            "start_time": 1789138000.0,
            "character_id": "2552654088:7013"
        }
    },
    "updated": 1789138868,
    "uptime": 1234,
    "max_players": 10,
    "world": { "name": "Midgard", "seed_name": "AbCdEfGhIj", "seed": 123456 },
    "day": 12,
    "day_fraction": 0.42,
    "clock": "10:04",
    "time_of_day": "morning",
    "bosses": ["defeated_eikthyr", "defeated_gdking"],
    "global_keys": ["defeated_eikthyr", "defeated_gdking"],
    "state": "running",
    "plugin": { "name": "ValheimStatus", "version": "1.0.0" }
}
```

## Schema

The first six keys mirror what a common log-tailing status script produced, so
a consumer written for that keeps working. Everything else is new.

| Key | Type | Meaning |
|---|---|---|
| `version` | string or null | game version without the platform prefix (`l-1.0.12` -> `1.0.12`) |
| `running` | bool | world is loaded and accepting players |
| `players` | object | keyed by the player's user id (a numeric string). `player` is the display name, `start_time` a unix float of when this plugin first saw them (resets on server restart). `character_id` is the character ZDOID, null until spawned. `x`/`y`/`z` appear only per the `positions` setting |
| `updated` | int | unix seconds of this write. Advances every `interval_seconds`, so a stale value means the server is down or wedged |
| `uptime` | int | seconds since the world came up |
| `max_players` | int | `max_players_default` from config (the server's limit is a launch flag the game does not expose) |
| `version_string` | string | raw game version string |
| `world` | object | `name`, `seed_name` (the string players type), `seed` (its int hash) |
| `day` | int | in-game day number |
| `day_fraction` | float | 0..1 through the current day |
| `clock` | string | `day_fraction` as HH:MM, for humans |
| `time_of_day` | string | `night`, `morning`, `afternoon` (from the game's own EnvMan checks) |
| `bosses` | array | global keys starting with `defeated_`, raw, sorted |
| `global_keys` | array | all global keys, sorted |
| `state` | string | `starting` (plugin up, world not yet loaded), `running`, `stopping` (written once on clean shutdown) |
| `plugin` | object | this plugin's name and version |

Consumers: treat `now - updated > 3 * interval_seconds` as "unknown". After a
crash the last file stays on disk with `running: true`.

Players are read from the network peer list, not `ZNet.m_players`, so the
phantom "Server" player some mods (WebMap) inject never appears.

## Install

Requires BepInEx 5.4.23.x on the server.

```
BepInEx/plugins/ValheimStatus/ValheimStatus.dll
BepInEx/config/com.github.harrisonpage.valheimstatus.cfg   # created on first start
```

By default the file is written beside the plugin dll. Set `path` to a file
under your web root; the plugin runs as the game's user, so that user must be
able to write there (own the directory, or have the web server alias a
directory it owns).

The config file is written on first start with defaults; edit and restart.
Unlike some plugins this one never rewrites the file on shutdown, so edits made
while the server is up survive.

| Section / key | Default | |
|---|---|---|
| `[Output] path` | `<plugin dir>/valheim.json` | absolute, or relative to the BepInEx dir; a `.tmp` beside it is renamed into place |
| `[Output] interval_seconds` | `10` | write period, min 1 |
| `[Output] pretty` | `false` | indent the JSON |
| `[Server] max_players_default` | `10` | reported as `max_players` |
| `[Players] positions` | `public` | `public` = only players with map visibility on, `always`, `never` |
| `[Players] include_character_id` | `true` | |
| `[Logging] debug` | `false` | log every write |

## Build

Needs only docker. Compiles against the game's own assemblies and the
installed BepInEx core, mounted read-only into a stock .NET SDK image, so the
result matches the exact game version on disk.

```bash
./build.sh                                  # game install at ~/valheim
VALHEIM_DIR=/opt/valheim ./build.sh         # or GAME_MANAGED=... BEPINEX_CORE=... individually
sudo RUN_AS="$(id -u steam):$(id -g steam)" ./build.sh   # only if the game dir is readable by steam alone
```

Build from any user's checkout that can read the game dir; the game user needs
no access to this repo. Output: `out/ValheimStatus.dll`, the only file to
deploy (copy it into the plugins dir and chown it to the game user).

## Rollback

Delete `BepInEx/plugins/ValheimStatus/` and restart. The plugin never touches
the world.

## Troubleshooting

* `BepInEx/LogOutput.log` has one `ValheimStatus 1.0.0: writing ...` line at
  startup. A `cannot prepare` error there means the output directory is not
  writable by the game user.
* Each game read is isolated: after a game update a renamed field logs one
  warning and drops that key while everything else keeps working. Rebuild
  against the new assemblies to fix it.
* `state` stuck at `starting`: the world never loaded; look at the game log,
  not this plugin.

[BepInEx]: https://github.com/BepInEx/BepInEx
