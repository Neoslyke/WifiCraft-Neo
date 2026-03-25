using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TShockAPI;

namespace WifiCraft
{
    public class RangeManager
    {
        private const int MaxChestItems = 40;

        private readonly Configuration _config;
        private readonly Dictionary<int, HashSet<int>> _playerAccessibleChests;

        public RangeManager(Configuration config)
        {
            _config = config;
            _playerAccessibleChests = new Dictionary<int, HashSet<int>>();
        }

        public int GetCraftingRange(TSPlayer player)
        {
            if (player == null) return _config.CraftingRangeTiles;

            foreach (var range in _config.PermissionRanges.OrderByDescending(r => r.CraftingRangeTiles))
            {
                if (player.HasPermission(range.Permission))
                {
                    return range.CraftingRangeTiles;
                }
            }

            return _config.CraftingRangeTiles;
        }

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

        public float GetDistanceToChest(TSPlayer player, Chest chest)
        {
            if (player?.TPlayer == null || chest == null) return float.MaxValue;

            float playerX = player.TPlayer.position.X + (player.TPlayer.width / 2f);
            float playerY = player.TPlayer.position.Y + (player.TPlayer.height / 2f);

            float chestX = (chest.x * 16f) + 16f;
            float chestY = (chest.y * 16f) + 16f;

            float distancePixels = (float)Math.Sqrt(
                Math.Pow(playerX - chestX, 2) + Math.Pow(playerY - chestY, 2)
            );

            return distancePixels / 16f;
        }

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
                if (chest.bankChest) continue;

                if (!LootSyncCompat.IsChestSafeToSync(chest.x, chest.y))
                {
                    continue;
                }

                chestsChecked++;

                float distance = GetDistanceToChest(player, chest);

                if (distance <= range)
                {
                    if (CanPlayerAccessChest(player, chest))
                    {
                        chests.Add(i);
                    }
                }
            }

            return chests;
        }

        private bool CanPlayerAccessChest(TSPlayer player, Chest chest)
        {
            if (!player.HasBuildPermission(chest.x, chest.y, false))
            {
                return false;
            }

            return true;
        }

        public void SyncChestsToPlayer(TSPlayer player)
        {
            if (player?.TPlayer == null || !player.Active) return;

            var chestsInRange = FindChestsInRange(player);

            _playerAccessibleChests[player.Index] = new HashSet<int>(chestsInRange);

            foreach (int chestId in chestsInRange)
            {
                SyncSingleChest(player, chestId);
            }
        }

        private void SyncSingleChest(TSPlayer player, int chestId)
        {
            if (chestId < 0 || chestId >= Main.maxChests) return;

            Chest? chest = Main.chest[chestId];
            if (chest == null) return;

            if (!LootSyncCompat.IsChestSafeToSync(chest.x, chest.y))
            {
                return;
            }

            try
            {
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
            catch
            {
            }
        }

        public bool IsChestInRange(TSPlayer player, int chestId)
        {
            if (player == null) return false;

            if (_playerAccessibleChests.TryGetValue(player.Index, out var chests))
            {
                return chests.Contains(chestId);
            }

            return false;
        }

        public int GetAccessibleChestCount(TSPlayer player)
        {
            if (player == null) return 0;

            if (_playerAccessibleChests.TryGetValue(player.Index, out var chests))
            {
                return chests.Count;
            }

            return 0;
        }

        public Dictionary<int, int> GetAccessibleItems(TSPlayer player)
        {
            var items = new Dictionary<int, int>();

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

                if (!LootSyncCompat.IsChestSafeToSync(chest.x, chest.y))
                {
                    continue;
                }

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

        public void ClearPlayer(int playerIndex)
        {
            _playerAccessibleChests.Remove(playerIndex);
        }

        public void ClearAll()
        {
            _playerAccessibleChests.Clear();
        }
    }
}