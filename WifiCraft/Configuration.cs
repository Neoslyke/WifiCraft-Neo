using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace WifiCraft
{
    public class Configuration
    {
        /// <summary>
        /// Enable or disable the plugin
        /// </summary>
        [JsonProperty(Order = 0)]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The range in tiles for chest crafting (vanilla Terraria is ~4-5 tiles)
        /// </summary>
        [JsonProperty(Order = 1)]
        public int CraftingRangeTiles { get; set; } = 15;

        /// <summary>
        /// The range in tiles for quick-stack to nearby chests
        /// </summary>
        [JsonProperty(Order = 2)]
        public int QuickStackRangeTiles { get; set; } = 15;

        /// <summary>
        /// How often to sync chest contents to players (in game ticks, 60 = 1 second)
        /// </summary>
        [JsonProperty(Order = 3)]
        public int SyncIntervalTicks { get; set; } = 30;

        /// <summary>
        /// Maximum number of chests to process per player per sync
        /// </summary>
        [JsonProperty(Order = 4)]
        public int MaxChestsPerSync { get; set; } = 100;

        /// <summary>
        /// Permission-based range overrides
        /// </summary>
        [JsonProperty(Order = 5)]
        public PermissionRange[] PermissionRanges { get; set; } = new[]
        {
            new PermissionRange
            {
                Permission = "wificraft.vip",
                CraftingRangeTiles = 25,
                QuickStackRangeTiles = 25
            },
            new PermissionRange
            {
                Permission = "wificraft.premium",
                CraftingRangeTiles = 40,
                QuickStackRangeTiles = 40
            },
            new PermissionRange
            {
                Permission = "wificraft.unlimited",
                CraftingRangeTiles = 100,
                QuickStackRangeTiles = 100
            }
        };

        /// <summary>
        /// Show debug messages in console
        /// </summary>
        [JsonProperty(Order = 6)]
        public bool DebugMode { get; set; } = false;

        private static string ConfigPath => Path.Combine(TShock.SavePath, "WifiCraft.json");

        public static Configuration Load()
        {
            Configuration config;

            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    config = JsonConvert.DeserializeObject<Configuration>(json) ?? new Configuration();
                    config.Save();
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[WifiCraft] Error loading config: {ex.Message}");
                    config = new Configuration();
                    config.Save();
                }
            }
            else
            {
                config = new Configuration();
                config.Save();
                TShock.Log.ConsoleInfo("[WifiCraft] Created default configuration file.");
            }

            return config;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(TShock.SavePath);
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[WifiCraft] Error saving config: {ex.Message}");
            }
        }
    }

    public class PermissionRange
    {
        [JsonProperty("Permission")]
        public string Permission { get; set; } = "";

        [JsonProperty("CraftingRangeTiles")]
        public int CraftingRangeTiles { get; set; } = 15;

        [JsonProperty("QuickStackRangeTiles")]
        public int QuickStackRangeTiles { get; set; } = 15;
    }
}