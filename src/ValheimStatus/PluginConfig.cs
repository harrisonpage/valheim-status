using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace ValheimStatus
{
    // Bound once in Plugin.Awake. BepInEx writes the file on first bind, so the
    // config never needs Config.Save() and can be edited while the server runs
    // (takes effect on next restart).
    internal static class PluginConfig
    {
        public static string OutputPath = "";   // default: valheim.json beside the plugin dll
        public static float IntervalSeconds = 10f;
        public static bool Pretty = false;
        public static int MaxPlayersDefault = 10;
        public static string Positions = "public";
        public static bool IncludeCharacterId = true;
        public static bool Debug = false;

        public static void Bind(ConfigFile config)
        {
            string defaultPath = Path.Combine(PluginDir(), "valheim.json");
            OutputPath = config.Bind("Output", "path", defaultPath,
                "Path of the JSON file to write; relative paths are resolved against the BepInEx directory. Point it at your web root. A .tmp file is written beside it and renamed into place.").Value;
            if (string.IsNullOrEmpty(OutputPath)) OutputPath = defaultPath;
            if (!Path.IsPathRooted(OutputPath)) OutputPath = Path.Combine(Paths.BepInExRootPath, OutputPath);

            IntervalSeconds = config.Bind("Output", "interval_seconds", IntervalSeconds,
                "How often to rewrite the file, in seconds. The 'updated' field advances on every write, so treat it as a heartbeat.").Value;
            if (IntervalSeconds < 1f) IntervalSeconds = 1f;

            Pretty = config.Bind("Output", "pretty", Pretty,
                "Indent the JSON for humans.").Value;

            MaxPlayersDefault = config.Bind("Server", "max_players_default", MaxPlayersDefault,
                "Value of 'max_players' when it cannot be read from the game.").Value;

            Positions = config.Bind("Players", "positions", Positions,
                "When to include player x/y/z: 'public' (only players who enabled map visibility), 'always', or 'never'.").Value;
            Positions = (Positions ?? "public").Trim().ToLowerInvariant();
            if (Positions != "public" && Positions != "always" && Positions != "never") Positions = "public";

            IncludeCharacterId = config.Bind("Players", "include_character_id", IncludeCharacterId,
                "Include each player's character ZDOID ('userid:id'); null until the character has spawned.").Value;

            Debug = config.Bind("Logging", "debug", Debug,
                "Log a line on every write.").Value;
        }

        private static string PluginDir()
        {
            string loc = typeof(PluginConfig).Assembly.Location;
            string dir = string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
            return string.IsNullOrEmpty(dir) ? Paths.PluginPath : dir;
        }
    }
}
