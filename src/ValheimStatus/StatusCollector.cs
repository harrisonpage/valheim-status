using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimStatus
{
    // Reads game state into a JsonObject. Every game read lives in its own small
    // method wrapped in try/catch: Mono resolves missing members when it JITs the
    // *containing* method, so a renamed field after a game update loses one key
    // instead of the whole file.
    internal sealed class StatusCollector
    {
        private static readonly Regex VersionPrefix = new Regex("^[a-zA-Z]-", RegexOptions.Compiled);

        private readonly Action<string, Exception> onError;
        private const int ActivityMax = 20;

        private readonly Dictionary<long, double> firstSeen = new Dictionary<long, double>();
        private readonly Dictionary<long, string> names = new Dictionary<long, string>();
        private readonly Dictionary<long, ZDOID> lastCharacter = new Dictionary<long, ZDOID>();
        private readonly Dictionary<long, ZDOID> deathCounted = new Dictionary<long, ZDOID>();
        private readonly List<JsonObject> activity = new List<JsonObject>();   // newest first
        private JsonObject spawn;   // the start temple never moves; found once

        public StatusCollector(Action<string, Exception> onError)
        {
            this.onError = onError;
        }

        public static bool IsReady()
        {
            try
            {
                return ZNet.instance != null && ZNet.instance.IsServer() && !string.IsNullOrEmpty(ZNet.instance.GetWorldName());
            }
            catch
            {
                return false;
            }
        }

        public void AddVersion(JsonObject root) => Try("version", () => AddVersionInner(root));

        // Game-derived fields; the caller has already added the always-present
        // keys (version, running, players, updated, uptime).
        public void Fill(JsonObject root, JsonObject players, double now)
        {
            Try("players", () => AddPlayers(players, now));
            Try("max_players", () => root.Add("max_players", MaxPlayers()));
            Try("world", () => root.Add("world", World()));
            Try("time", () => AddTime(root));
            Try("global_keys", () => AddGlobalKeys(root));
            Try("in_bed", () => root.Add("in_bed", InBedCount()));
            Try("wind", () => AddWeather(root));
            Try("events", () => root.Add("events", Events()));
            Try("spawn", () => root.Add("spawn", Spawn()));
            root.Add("activity", new List<JsonObject>(activity));
        }

        public int MaxPlayersOrDefault()
        {
            try { return MaxPlayers(); } catch { return PluginConfig.MaxPlayersDefault; }
        }

        public static string VersionOrNull()
        {
            try { return StripPrefix(global::Version.GetVersionString()); } catch { return null; }
        }

        private void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { onError(what, e); }
        }

        private static string StripPrefix(string raw) =>
            string.IsNullOrEmpty(raw) ? raw : VersionPrefix.Replace(raw, "");

        private static void AddVersionInner(JsonObject root)
        {
            string raw = global::Version.GetVersionString();
            root.Add("version", StripPrefix(raw));
            root.Add("version_string", raw);
        }

        // The game exposes no public player limit on the server (it is a launch
        // argument), so this is config only.
        private static int MaxPlayers() => PluginConfig.MaxPlayersDefault;

        private static JsonObject World()
        {
            var o = new JsonObject();
            o.Add("name", ZNet.instance.GetWorldName());
            global::World w = ZNet.World;
            o.Add("seed_name", w != null ? w.m_seedName : null);
            if (w != null) o.Add("seed", w.m_seed); else o.Add("seed", null);
            return o;
        }

        // Players come from the peer list, not ZNet.m_players: the WebMap plugin
        // splices a fake "Server" PlayerInfo into the latter, and peers are what the
        // old log-based producer keyed on (the ZDOID user id == peer m_uid).
        private void AddPlayers(JsonObject players, double now)
        {
            var seen = new HashSet<long>();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_server || !peer.IsReady() || peer.m_uid == 0L || string.IsNullOrEmpty(peer.m_playerName)) continue;
                seen.Add(peer.m_uid);
                names[peer.m_uid] = peer.m_playerName;
                if (!firstSeen.ContainsKey(peer.m_uid))
                {
                    firstSeen[peer.m_uid] = now;
                    AddActivity(now, "join", peer.m_playerName);
                }

                var p = new JsonObject();
                p.Add("player", peer.m_playerName);
                p.Add("start_time", firstSeen[peer.m_uid]);
                if (PluginConfig.IncludeCharacterId)
                    p.Add("character_id", peer.m_characterID.IsNone() ? null : peer.m_characterID.ToString());
                if (PluginConfig.Positions == "always" || (PluginConfig.Positions == "public" && peer.m_publicRefPos))
                {
                    Vector3 pos = peer.m_refPos;
                    p.Add("x", Mathf.RoundToInt(pos.x));
                    p.Add("y", Mathf.RoundToInt(pos.y));
                    p.Add("z", Mathf.RoundToInt(pos.z));
                    Try("biome", () => p.Add("biome", WorldGenerator.instance.GetBiome(pos).ToString()));
                }
                Try("player_state", () => AddPlayerState(p, peer, now));
                players.Add(peer.m_uid.ToString(), p);
            }

            // forget players who left so a rejoin gets a fresh start_time
            var gone = new List<long>();
            foreach (long uid in firstSeen.Keys) if (!seen.Contains(uid)) gone.Add(uid);
            foreach (long uid in gone)
            {
                AddActivity(now, "leave", names.TryGetValue(uid, out string name) ? name : null);
                firstSeen.Remove(uid);
                names.Remove(uid);
                lastCharacter.Remove(uid);
                deathCounted.Remove(uid);
            }
        }

        // in_bed and dead come from the character ZDO, which the server holds for
        // every player. A death sets "dead" on the old character, and the respawn
        // gets a new character id; the dead flag can be gone before the next write,
        // so a character id change while connected also counts as a death.
        private void AddPlayerState(JsonObject p, ZNetPeer peer, double now)
        {
            long uid = peer.m_uid;
            ZDOID id = peer.m_characterID;
            ZDO zdo = id.IsNone() ? null : ZDOMan.instance.GetZDO(id);
            bool dead = zdo != null && zdo.GetBool(ZDOVars.s_dead);
            p.Add("in_bed", zdo != null && zdo.GetBool(ZDOVars.s_inBed));
            p.Add("dead", dead);

            deathCounted.TryGetValue(uid, out ZDOID counted);
            if (dead && counted != id)
            {
                AddActivity(now, "death", peer.m_playerName);
                deathCounted[uid] = id;
            }
            else if (lastCharacter.TryGetValue(uid, out ZDOID prev) && !prev.IsNone() && !id.IsNone() && prev != id && counted != prev)
            {
                AddActivity(now, "death", peer.m_playerName);
                deathCounted[uid] = prev;
            }
            if (!id.IsNone()) lastCharacter[uid] = id;
        }

        private void AddActivity(double now, string type, string player)
        {
            var a = new JsonObject();
            a.Add("time", (long)now);
            a.Add("type", type);
            a.Add("player", player);
            activity.Insert(0, a);
            if (activity.Count > ActivityMax) activity.RemoveAt(activity.Count - 1);
        }

        // Same test the game uses to skip the night (Game.EverybodyIsTryingToSleep).
        private static int InBedCount()
        {
            int n = 0;
            foreach (ZDO zdo in ZNet.instance.GetAllCharacterZDOS())
                if (zdo.GetBool(ZDOVars.s_inBed)) n++;
            return n;
        }

        // A dedicated server has no camera, so EnvMan never picks weather or wind
        // itself; clients do, from game time alone. This replays their math
        // (EnvMan.UpdateEnvironment / UpdateWind) for every biome. It ignores what
        // the server cannot see: dungeon and building interiors (EnvZone), raid and
        // boss-fight weather, and the per-sector AltBiome weather tweaks.
        private static void AddWeather(JsonObject root)
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return;
            long sec = (long)ZNet.instance.GetTimeSeconds();

            UnityEngine.Random.State saved = UnityEngine.Random.state;
            try
            {
                float angle = 0f;
                float intensity = 0.5f;
                foreach (int octave in new[] { 1, 2, 4, 8 })
                {
                    UnityEngine.Random.InitState((int)(sec / (env.m_windPeriodDuration / octave)));
                    angle += UnityEngine.Random.value * (Mathf.PI * 2f / octave);
                    intensity += -0.5f / octave + UnityEngine.Random.value / octave;
                }
                var wind = new JsonObject();
                wind.Add("toward_deg", Mathf.RoundToInt(Mathf.Repeat(angle * Mathf.Rad2Deg, 360f)) % 360);
                wind.Add("base", Round2(intensity));
                root.Add("wind", wind);

                var byBiome = new JsonObject();
                foreach (BiomeEnvSetup biome in env.m_biomes)
                {
                    UnityEngine.Random.InitState((int)(sec / env.m_environmentDuration));
                    EnvSetup picked = SelectWeather(biome);
                    if (picked == null) continue;
                    var w = new JsonObject();
                    w.Add("env", picked.m_name);
                    w.Add("wind", Round2(Mathf.Clamp(Mathf.Lerp(picked.m_windMin, picked.m_windMax, intensity), 0.05f, 1f)));
                    byBiome.Add(biome.m_biome.ToString(), w);
                }
                root.Add("weather_by_biome", byBiome);
            }
            finally
            {
                UnityEngine.Random.state = saved;
            }
        }

        // EnvMan.SelectWeightedEnvironment, then its Ashlands/Deep North override
        // (on land in those biomes the override entry always wins).
        private static EnvSetup SelectWeather(BiomeEnvSetup biome)
        {
            List<EnvEntry> entries = biome.m_environments;
            if (entries == null || entries.Count == 0) return null;
            float total = 0f;
            foreach (EnvEntry e in entries)
                if (!e.m_ashlandsOverride && !e.m_deepnorthOverride) total += e.m_weight;
            float roll = UnityEngine.Random.Range(0f, total);
            EnvSetup picked = null;
            float sum = 0f;
            foreach (EnvEntry e in entries)
            {
                if (e.m_ashlandsOverride || e.m_deepnorthOverride) continue;
                sum += e.m_weight;
                if (sum >= roll) { picked = e.m_env; break; }
            }
            if (picked == null)
            {
                EnvEntry last = entries[entries.Count - 1];
                if (!last.m_ashlandsOverride && !last.m_deepnorthOverride) picked = last.m_env;
            }
            foreach (EnvEntry e in entries)
            {
                if (e.m_ashlandsOverride && biome.m_biome == Heightmap.Biome.AshLands) picked = e.m_env;
                if (e.m_deepnorthOverride && biome.m_biome == Heightmap.Biome.DeepNorth) picked = e.m_env;
            }
            return picked;
        }

        private static double Round2(float v) => Math.Round(v, 2);

        // A list so other kinds of event can be added later; today only the
        // running raid (RandEventSystem allows one at a time).
        private static List<JsonObject> Events()
        {
            var list = new List<JsonObject>();
            RandomEvent raid = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            if (raid != null)
            {
                var e = new JsonObject();
                e.Add("type", "raid");
                e.Add("name", raid.m_name);
                e.Add("elapsed", Mathf.RoundToInt(raid.m_time));
                e.Add("duration", Mathf.RoundToInt(raid.m_duration));
                if (PluginConfig.Positions != "never")
                {
                    e.Add("x", Mathf.RoundToInt(raid.m_pos.x));
                    e.Add("z", Mathf.RoundToInt(raid.m_pos.z));
                }
                list.Add(e);
            }
            return list;
        }

        private JsonObject Spawn()
        {
            if (spawn != null) return spawn;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.FindClosestLocation("StartTemple", Vector3.zero, out ZoneSystem.LocationInstance temple))
                return null;
            spawn = new JsonObject();
            spawn.Add("x", Mathf.RoundToInt(temple.m_position.x));
            spawn.Add("y", Mathf.RoundToInt(temple.m_position.y));
            spawn.Add("z", Mathf.RoundToInt(temple.m_position.z));
            return spawn;
        }

        private static void AddTime(JsonObject root)
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return;
            root.Add("day", env.GetDay());
            root.Add("day_length_sec", env.m_dayLengthSec);
            float fraction = env.GetDayFraction();
            root.Add("day_fraction", (double)fraction);
            int minutes = (int)(fraction * 24f * 60f) % (24 * 60);
            root.Add("clock", string.Format("{0:00}:{1:00}", minutes / 60, minutes % 60));
            string label;
            if (EnvMan.IsNight()) label = "night";
            else if (EnvMan.IsAfternoon()) label = "afternoon";
            else label = "morning";
            root.Add("time_of_day", label);
        }

        private static void AddGlobalKeys(JsonObject root)
        {
            if (ZoneSystem.instance == null) return;
            var keys = new List<string>(ZoneSystem.instance.GetGlobalKeys());
            keys.Sort(StringComparer.Ordinal);
            var bosses = new List<string>();
            foreach (string k in keys) if (k.StartsWith("defeated_", StringComparison.OrdinalIgnoreCase)) bosses.Add(k);
            root.Add("bosses", bosses);
            root.Add("global_keys", keys);
        }
    }
}
