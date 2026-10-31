using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace ValheimStatus
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.github.harrisonpage.valheimstatus";
        public const string Name = "ValheimStatus";
        public const string Version = "1.2.0";

        private StatusCollector collector;
        private readonly FrameStats frames = new FrameStats();
        private float nextWriteAt;          // Time.realtimeSinceStartup
        private double serverUpSince;       // unix seconds, 0 until the world is up
        private bool finalWritten;
        private readonly HashSet<string> loggedErrors = new HashSet<string>();

        private void Awake()
        {
            PluginConfig.Bind(Config);
            collector = new StatusCollector(OnFieldError);
            try
            {
                AtomicFile.EnsureDir(PluginConfig.OutputPath);
                AtomicFile.CleanStaleTmp(PluginConfig.OutputPath);
            }
            catch (Exception e)
            {
                Logger.LogError($"cannot prepare {PluginConfig.OutputPath}: {e.Message}");
            }
            Logger.LogInfo($"{Name} {Version}: writing {PluginConfig.OutputPath} every {PluginConfig.IntervalSeconds}s");
        }

        private void Update()
        {
            frames.Sample(Time.unscaledDeltaTime);
            if (Time.realtimeSinceStartup < nextWriteAt) return;
            nextWriteAt = Time.realtimeSinceStartup + PluginConfig.IntervalSeconds;
            WriteSnapshot();
        }

        // Valheim saves and exits on SIGINT; both hooks fire, the flag makes the
        // final write happen once. Neither touches game singletons, which may
        // already be destroyed.
        private void OnApplicationQuit() => WriteFinal();
        private void OnDestroy() => WriteFinal();

        private static double Now() =>
            (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private void WriteSnapshot()
        {
            double now = Now();
            bool ready = StatusCollector.IsReady();
            if (ready && serverUpSince == 0) serverUpSince = now;

            var root = new JsonObject();
            var players = new JsonObject();

            // The six keys the old log-tailing producer wrote come first, same names
            // and types; everything after them is new.
            if (ready) collector.AddVersion(root);
            else root.Add("version", StatusCollector.VersionOrNull());
            root.Add("running", ready);
            root.Add("players", players);
            root.Add("updated", (long)now);
            root.Add("uptime", ready ? (long)(now - serverUpSince) : 0L);
            if (ready) collector.Fill(root, players, now);
            else root.Add("max_players", PluginConfig.MaxPlayersDefault);
            JsonObject server = frames.TakeSnapshot();
            root.Add("server", server);
            root.Add("host", HostOrNull());
            root.Add("state", ready ? "running" : "starting");

            AddPluginInfo(root);
            Write(root);
            if (ready && players.Count > 0) AppendPerfLog((long)now, server, players);
        }

        // One compact line per write while anyone is online: the server block and
        // each player's net block, keyed by name. No positions.
        private void AppendPerfLog(long now, JsonObject server, JsonObject players)
        {
            if (string.IsNullOrEmpty(PluginConfig.PerfLogPath)) return;
            try
            {
                var byName = new JsonObject();
                foreach (var kv in players)
                {
                    string name = kv.Key;
                    object net = null;
                    foreach (var f in (JsonObject)kv.Value)
                    {
                        if (f.Key == "player" && f.Value is string s) name = s;
                        else if (f.Key == "net") net = f.Value;
                    }
                    byName.Add(name, net);
                }
                var line = new JsonObject();
                line.Add("t", now);
                line.Add("server", server);
                line.Add("players", byName);
                AtomicFile.Append(PluginConfig.PerfLogPath, Json.Serialize(line, false) + "\n");
            }
            catch (Exception e)
            {
                LogOnce("perf_log", e);
            }
        }

        private void WriteFinal()
        {
            if (finalWritten) return;
            finalWritten = true;
            double now = Now();
            var root = new JsonObject();
            root.Add("version", StatusCollector.VersionOrNull());
            root.Add("running", false);
            root.Add("players", new JsonObject());
            root.Add("updated", (long)now);
            root.Add("uptime", 0L);
            root.Add("max_players", collector != null ? collector.MaxPlayersOrDefault() : PluginConfig.MaxPlayersDefault);
            root.Add("host", HostOrNull());
            root.Add("state", "stopping");
            AddPluginInfo(root);
            Write(root);
        }

        private static string HostOrNull() =>
            string.IsNullOrEmpty(PluginConfig.Host) ? null : PluginConfig.Host;

        private static void AddPluginInfo(JsonObject root)
        {
            var plugin = new JsonObject();
            plugin.Add("name", Name);
            plugin.Add("version", Version);
            root.Add("plugin", plugin);
        }

        private void Write(JsonObject root)
        {
            try
            {
                AtomicFile.Write(PluginConfig.OutputPath, Json.Serialize(root, PluginConfig.Pretty));
                if (PluginConfig.Debug) Logger.LogInfo($"wrote {PluginConfig.OutputPath}");
            }
            catch (Exception e)
            {
                LogOnce("write", e);
            }
        }

        private void OnFieldError(string what, Exception e) => LogOnce(what, e);

        // A broken field would otherwise log every interval forever.
        private void LogOnce(string what, Exception e)
        {
            string key = what + ":" + e.GetType().Name + ":" + e.Message;
            if (loggedErrors.Add(key)) Logger.LogWarning($"{what}: {e.GetType().Name}: {e.Message}");
        }
    }
}
