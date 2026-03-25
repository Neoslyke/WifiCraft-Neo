using System;
using System.IO;
using Newtonsoft.Json;
using TShockAPI;

namespace WifiCraft
{
    public class Configuration
    {
        [JsonProperty(Order = 0)]
        public bool Enabled { get; set; } = true;

        [JsonProperty(Order = 1)]
        public int CraftingRangeTiles { get; set; } = 15;

        [JsonProperty(Order = 2)]
        public int QuickStackRangeTiles { get; set; } = 15;

        [JsonProperty(Order = 3)]
        public int SyncIntervalTicks { get; set; } = 30;

        [JsonProperty(Order = 4)]
        public int MaxChestsPerSync { get; set; } = 100;

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