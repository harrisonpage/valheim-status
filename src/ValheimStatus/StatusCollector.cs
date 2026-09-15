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
        private readonly Dictionary<long, double> firstSeen = new Dictionary<long, double>();

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
                if (!firstSeen.ContainsKey(peer.m_uid)) firstSeen[peer.m_uid] = now;

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
                }
                players.Add(peer.m_uid.ToString(), p);
            }

            // forget players who left so a rejoin gets a fresh start_time
            var gone = new List<long>();
            foreach (long uid in firstSeen.Keys) if (!seen.Contains(uid)) gone.Add(uid);
            foreach (long uid in gone) firstSeen.Remove(uid);
        }

        private static void AddTime(JsonObject root)
        {
            EnvMan env = EnvMan.instance;
            if (env == null) return;
            root.Add("day", env.GetDay());
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
