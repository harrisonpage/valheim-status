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
            "character_id": "2552654088:7013",
            "x": 762, "y": 34, "z": -3335,
            "biome": "Meadows",
            "in_bed": false,
            "dead": false
        }
    },
    "updated": 1789138868,
    "uptime": 1234,
    "max_players": 10,
    "world": { "name": "Midgard", "seed_name": "AbCdEfGhIj", "seed": 123456 },
    "day": 12,
    "day_length_sec": 1800,
    "day_fraction": 0.42,
    "clock": "10:04",
    "time_of_day": "morning",
    "bosses": ["defeated_eikthyr", "defeated_gdking"],
    "global_keys": ["defeated_eikthyr", "defeated_gdking"],
    "in_bed": 0,
    "wind": { "toward_deg": 214, "base": 0.63 },
    "weather_by_biome": {
        "Meadows": { "env": "Clear", "wind": 0.38 },
        "Swamp": { "env": "SwampRain", "wind": 0.5 }
    },
    "events": [{ "type": "raid", "name": "army_eikthyr", "elapsed": 41, "duration": 90, "x": 700, "z": -3300 }],
    "spawn": { "x": 12, "y": 31, "z": -8 },
    "activity": [
        { "time": 1789138000, "type": "join", "player": "Bjorn" }
    ],
    "host": "valheim.example.com",
    "state": "running",
    "plugin": { "name": "ValheimStatus", "version": "1.1.0" }
}
```

## Schema

The first six keys mirror what a common log-tailing status script produced, so
a consumer written for that keeps working. Everything else is new.

| Key | Type | Meaning |
|---|---|---|
| `version` | string or null | game version without the platform prefix (`l-1.0.12` -> `1.0.12`) |
| `running` | bool | world is loaded and accepting players |
| `players` | object | keyed by the player's user id (a numeric string). `player` is the display name, `start_time` a unix float of when this plugin first saw them (resets on server restart). `character_id` is the character ZDOID, null until spawned. `x`/`y`/`z` and `biome` appear only per the `positions` setting. `in_bed` and `dead` come from the character |
| `updated` | int | unix seconds of this write. Advances every `interval_seconds`, so a stale value means the server is down or wedged |
| `uptime` | int | seconds since the world came up |
| `max_players` | int | `max_players_default` from config (the server's limit is a launch flag the game does not expose) |
| `version_string` | string | raw game version string |
| `world` | object | `name`, `seed_name` (the string players type), `seed` (its int hash) |
| `day` | int | in-game day number |
| `day_length_sec` | int | real seconds per in-game day |
| `day_fraction` | float | 0..1 through the current day |
| `clock` | string | `day_fraction` as HH:MM, for humans |
| `time_of_day` | string | `night`, `morning`, `afternoon` (from the game's own EnvMan checks) |
| `bosses` | array | global keys starting with `defeated_`, raw, sorted |
| `global_keys` | array | all global keys, sorted |
| `in_bed` | int | characters in bed; the night is skipped when this equals the number online |
| `wind` | object | `toward_deg`: compass direction the wind blows toward (0 = north/+z, 90 = east/+x), the same everywhere. `base`: 0..1 before weather scaling |
| `weather_by_biome` | object | per biome, the weather (`env`) a client there would get right now and its wind strength 0..1. Replays the clients' time-seeded pick (the server never computes weather itself), so it is wrong inside dungeons, during raids and boss fights, and may differ in the few sectors with alternate weather |
| `events` | array | in progress: today only `{type: "raid", name, elapsed, duration}` plus `x`/`z` unless `positions` is `never`. A raid is paused (elapsed stops) while nobody is near it |
| `spawn` | object or null | start temple position |
| `activity` | array | newest first, at most 20: `{time, type, player}` with type `join`, `leave` or `death`. In memory, so empty after a restart |
| `host` | string or null | `host` from config, null if unset |
| `state` | string | `starting` (plugin up, world not yet loaded), `running`, `stopping` (written once on clean shutdown) |
| `plugin` | object | this plugin's name and version |

Consumers: treat `now - updated > 3 * interval_seconds` as "unknown". After a
crash the last file stays on disk with `running: true`.

Players are read from the network peer list, not `ZNet.m_players`, so the
phantom "Server" player some mods (WebMap) inject never appears.

### events and activity

```json
"events": [
    { "type": "raid", "name": "army_eikthyr", "elapsed": 47, "duration": 90, "x": 754, "z": -3310 }
],
"activity": [
    { "time": 1792530912, "type": "death", "player": "Fel" },
    { "time": 1792530640, "type": "leave", "player": "Kavion" },
    { "time": 1792529931, "type": "join",  "player": "Kavion" },
    { "time": 1792529455, "type": "join",  "player": "Fel" }
]
```

`events` is `[]` when nothing is happening. A raid's `name` is the game's
internal id (`army_eikthyr`, `army_theelder`, `foresttrolls`, `skeletons`,
`blobs`, `wolves`, `bats`, ...), not the on-screen text, so map it yourself.
`elapsed` stops while no player is near the raid, so `duration - elapsed` is not
wall-clock time left.

`activity` is kept in memory, so it starts empty on every server restart. A death is seen either as the
character's `dead` flag or as the player getting a new character while still
connected, so two deaths within one `interval_seconds` count as one.

## Install

Requires BepInEx 5.4.23.x on a Linux or Windows dedicated server. Grab
`ValheimStatus.dll` from the [latest release] (or build it, below) and drop
it in:

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
| `[Server] host` | empty | reported as `host` (null when empty) |
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
deploy (copy it into the plugins dir and chown it to the game user). Builds are
deterministic; a release asset is reproducible from its tag.

Releases: the tag, `<Version>` in the csproj and `Plugin.Version` are kept
identical, so `plugin.version` in the JSON tells you which release a server
runs. To cut one, bump both, commit and push, then:

```bash
BUILD_HOST=myserver VALHEIM_DIR=/opt/valheim ./release.sh
```

It builds the pushed commit in a fresh clone on `BUILD_HOST` (any ssh host with
docker and the game installed), pushes the `v<version>` tag, and creates a
draft release with the dll and its sha256 for you to review and publish. Needs
`gh` logged in.

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
[latest release]: https://github.com/harrisonpage/valheim-status/releases/latest
