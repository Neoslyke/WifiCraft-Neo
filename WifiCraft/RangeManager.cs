using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TShockAPI;

namespace WifiCraft
{
    public class RangeManager
    {
        private const int MaxChestItems = 40; // Terraria chest slot count

        private readonly Configuration _config;
        private readonly Dictionary<int, HashSet<int>> _playerAccessibleChests;

        public RangeManager(Configuration config)
        {
            _config = config;
            _playerAccessibleChests = new Dictionary<int, HashSet<int>>();
        }

        /// <summary>
        /// Get crafting range for a player based on their permissions
        /// </summary>
        public int GetCraftingRange(TSPlayer player)
        {
            if (player == null) return _config.CraftingRangeTiles;

            // Check permission ranges from highest to lowest
            foreach (var range in _config.PermissionRanges.OrderByDescending(r => r.CraftingRangeTiles))
            {
                if (player.HasPermission(range.Permission))
                {
                    return range.CraftingRangeTiles;
                }
            }

            return _config.CraftingRangeTiles;
        }

        /// <summary>
        /// Get quick-stack range for a player based on their permissions
        /// </summary>
        public int GetQuickStackRange(TSPlayer player)
        {
            if (player == null) return _config.QuickStackRangeTiles;

            foreach (var range in _config.PermissionRanges.OrderByDescending(r => r.QuickStackRangeTiles))
            {
                if (player.HasPermission(range.Permission))
                {
                    return range.QuickStackRangeTiles;
                }
            }

            return _config.QuickStackRangeTiles;
        }

        /// <summary>
        /// Calculate distance between player and chest in tiles
        /// </summary>
        public float GetDistanceToChest(TSPlayer player, Chest chest)
        {
            if (player?.TPlayer == null || chest == null) return float.MaxValue;

            // Player center position
            float playerX = player.TPlayer.position.X + (player.TPlayer.width / 2f);
            float playerY = player.TPlayer.position.Y + (player.TPlayer.height / 2f);

            // Chest center position (chests are 2x2 tiles)
            float chestX = (chest.x * 16f) + 16f;
            float chestY = (chest.y * 16f) + 16f;

            // Distance in pixels, convert to tiles
            float distancePixels = (float)Math.Sqrt(
                Math.Pow(playerX - chestX, 2) + Math.Pow(playerY - chestY, 2)
            );

            return distancePixels / 16f;
        }

        /// <summary>
        /// Find all chests within the player's crafting range
        /// </summary>
        public List<int> FindChestsInRange(TSPlayer player)
        {
            var chests = new List<int>();

            if (player?.TPlayer == null) return chests;

            int range = GetCraftingRange(player);
            int chestsChecked = 0;

            for (int i = 0; i < Main.maxChests; i++)
            {
                if (chestsChecked >= _config.MaxChestsPerSync) break;

                Chest? chest = Main.chest[i];
                if (chest == null) continue;

                chestsChecked++;

                float distance = GetDistanceToChest(player, chest);

                if (distance <= range)
                {
                    // Check if player can access this chest (TShock protection)
                    if (CanPlayerAccessChest(player, chest))
                    {
                        chests.Add(i);
                    }
                }
            }

            return chests;
        }

        /// <summary>
        /// Check if player has permission to access a chest
        /// </summary>
        private bool CanPlayerAccessChest(TSPlayer player, Chest chest)
        {
            // Check TShock region/chest protection
            if (!player.HasBuildPermission(chest.x, chest.y, false))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sync all chests in range to the player so they can use items for crafting
        /// </summary>
        public void SyncChestsToPlayer(TSPlayer player)
        {
            if (player?.TPlayer == null || !player.Active) return;

            var chestsInRange = FindChestsInRange(player);

            // Store accessible chests for this player
            _playerAccessibleChests[player.Index] = new HashSet<int>(chestsInRange);

            // Send chest contents to player
            foreach (int chestId in chestsInRange)
            {
                SyncSingleChest(player, chestId);
            }

            if (_config.DebugMode && chestsInRange.Count > 0)
            {
                int range = GetCraftingRange(player);
                TShock.Log.ConsoleInfo($"[WifiCraft] Synced {chestsInRange.Count} chests to {player.Name} (range: {range} tiles)");
            }
        }

        /// <summary>
        /// Sync a single chest's contents to a player
        /// </summary>
        private void SyncSingleChest(TSPlayer player, int chestId)
        {
            if (chestId < 0 || chestId >= Main.maxChests) return;

            Chest? chest = Main.chest[chestId];
            if (chest == null) return;

            try
            {
                // Send each item slot in the chest
                for (int slot = 0; slot < MaxChestItems; slot++)
                {
                    Item item = chest.item[slot] ?? new Item();

                    NetMessage.SendData(
                        (int)PacketTypes.ChestItem,
                        player.Index,
                        -1,
                        null,
                        chestId,
                        slot,
                        item.IsAir ? 0 : item.stack,
                        item.IsAir ? 0 : item.prefix,
                        item.IsAir ? 0 : item.type
                    );
                }
            }
            catch (Exception ex)
            {
                if (_config.DebugMode)
                {
                    TShock.Log.ConsoleError($"[WifiCraft] Error syncing chest {chestId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Check if a specific chest is in range for a player
        /// </summary>
        public bool IsChestInRange(TSPlayer player, int chestId)
        {
            if (player == null) return false;

            if (_playerAccessibleChests.TryGetValue(player.Index, out var chests))
            {
                return chests.Contains(chestId);
            }

            return false;
        }

        /// <summary>
        /// Get count of accessible chests for a player
        /// </summary>
        public int GetAccessibleChestCount(TSPlayer player)
        {
            if (player == null) return 0;

            if (_playerAccessibleChests.TryGetValue(player.Index, out var chests))
            {
                return chests.Count;
            }

            return 0;
        }

        /// <summary>
        /// Get all items available to a player from nearby chests
        /// </summary>
        public Dictionary<int, int> GetAccessibleItems(TSPlayer player)
        {
            var items = new Dictionary<int, int>(); // ItemType -> TotalStack

            if (player == null) return items;

            if (!_playerAccessibleChests.TryGetValue(player.Index, out var chestIds))
            {
                return items;
            }

            foreach (int chestId in chestIds)
            {
                if (chestId < 0 || chestId >= Main.maxChests) continue;

                Chest? chest = Main.chest[chestId];
                if (chest == null) continue;

                for (int slot = 0; slot < MaxChestItems; slot++)
                {
                    Item? item = chest.item[slot];
                    if (item == null || item.IsAir) continue;

                    if (items.ContainsKey(item.type))
                        items[item.type] += item.stack;
                    else
                        items[item.type] = item.stack;
                }
            }

            return items;
        }

        /// <summary>
        /// Clear data for a player (when they leave)
        /// </summary>
        public void ClearPlayer(int playerIndex)
        {
            _playerAccessibleChests.Remove(playerIndex);
        }

        /// <summary>
        /// Clear all cached data
        /// </summary>
        public void ClearAll()
        {
            _playerAccessibleChests.Clear();
        }
    }
}