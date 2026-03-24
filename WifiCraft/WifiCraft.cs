using System;
using System.IO;
using System.Reflection;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace WifiCraft
{
    [ApiVersion(2, 1)]
    public class WifiCraftPlugin : TerrariaPlugin
    {
        public override string Name => "WifiCraft";
        public override string Author => "Neoslyke";
        public override Version Version => new Version(2, 1, 0);
        public override string Description => "Extend chest crafting range - use materials from distant chests!";

        private const int MaxChestItems = 40; // Terraria chest slot count

        private Configuration _config = null!;
        private RangeManager _chestManager = null!;
        private int _tickCounter;

        public WifiCraftPlugin(Main game) : base(game)
        {
            Order = 1;
        }

        public override void Initialize()
        {
            _config = Configuration.Load();
            _chestManager = new RangeManager(_config);
            _tickCounter = 0;

            // Register hooks
            ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
            ServerApi.Hooks.NetGetData.Register(this, OnNetGetData);
            ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);

            GeneralHooks.ReloadEvent += OnReload;

            // Register commands
            Commands.ChatCommands.Add(new Command("wificraft.use", WifiCraftCommand, "wificraft", "wc")
            {
                HelpText = "WifiCraft commands - extend your chest crafting range"
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
                ServerApi.Hooks.NetGetData.Deregister(this, OnNetGetData);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnPlayerLeave);

                GeneralHooks.ReloadEvent -= OnReload;
            }
            base.Dispose(disposing);
        }

        private void OnReload(ReloadEventArgs args)
        {
            _config = Configuration.Load();
            _chestManager = new RangeManager(_config);

            args.Player?.SendSuccessMessage("[WifiCraft] Configuration reloaded!");
            TShock.Log.ConsoleInfo("[WifiCraft] Configuration reloaded!");
        }

        private void OnGameUpdate(EventArgs args)
        {
            if (!_config.Enabled) return;

            _tickCounter++;

            if (_tickCounter < _config.SyncIntervalTicks) return;
            _tickCounter = 0;

            // Sync chests to all active players
            foreach (TSPlayer player in TShock.Players)
            {
                if (player?.Active != true) continue;
                if (!player.RealPlayer) continue;

                try
                {
                    _chestManager.SyncChestsToPlayer(player);
                }
                catch (Exception ex)
                {
                    if (_config.DebugMode)
                    {
                        TShock.Log.ConsoleError($"[WifiCraft] Error syncing to {player.Name}: {ex.Message}");
                    }
                }
            }
        }

        private void OnNetGetData(GetDataEventArgs args)
        {
            if (!_config.Enabled) return;
            if (args.Handled) return;

            TSPlayer? player = TShock.Players[args.Msg.whoAmI];
            if (player == null || !player.Active) return;

            try
            {
                switch (args.MsgID)
                {
                    case PacketTypes.ChestGetContents:
                        HandleChestGetContents(player, args);
                        break;

                    case PacketTypes.ChestItem:
                        HandleChestItem(player, args);
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_config.DebugMode)
                {
                    TShock.Log.ConsoleError($"[WifiCraft] Packet handling error: {ex.Message}");
                }
            }
        }

        private void HandleChestGetContents(TSPlayer player, GetDataEventArgs args)
        {
            using var stream = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length);
            using var reader = new BinaryReader(stream);

            int x = reader.ReadInt16();
            int y = reader.ReadInt16();

            int chestId = Chest.FindChest(x, y);
            if (chestId == -1) return;

            Chest? chest = Main.chest[chestId];
            if (chest == null) return;

            float distance = _chestManager.GetDistanceToChest(player, chest);
            int range = _chestManager.GetCraftingRange(player);

            // If chest is beyond vanilla range but within our extended range
            if (distance > 5 && distance <= range)
            {
                if (_config.DebugMode)
                {
                    TShock.Log.ConsoleInfo($"[WifiCraft] {player.Name} accessing chest at {distance:F1} tiles (extended range)");
                }

                // Send chest contents
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

                // Tell client to open the chest
                NetMessage.SendData(
                    (int)PacketTypes.ChestOpen,
                    player.Index,
                    -1,
                    null,
                    player.Index,
                    chestId
                );

                // Notify player if debug mode
                if (_config.DebugMode)
                {
                    player.SendInfoMessage($"[WifiCraft] Accessing chest at {distance:F1} tiles away!");
                }
            }
        }

        private void HandleChestItem(TSPlayer player, GetDataEventArgs args)
        {
            using var stream = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length);
            using var reader = new BinaryReader(stream);

            int chestId = reader.ReadInt16();

            if (chestId < 0 || chestId >= Main.maxChests) return;

            // Allow modification if chest is in extended range
            if (_chestManager.IsChestInRange(player, chestId))
            {
                if (_config.DebugMode)
                {
                    TShock.Log.ConsoleInfo($"[WifiCraft] {player.Name} modified extended range chest {chestId}");
                }
            }
        }

        private void OnPlayerLeave(LeaveEventArgs args)
        {
            _chestManager.ClearPlayer(args.Who);
        }

        #region Commands

        private void WifiCraftCommand(CommandArgs args)
        {
            TSPlayer player = args.Player;
            string subCmd = args.Parameters.Count > 0 ? args.Parameters[0].ToLower() : "help";

            switch (subCmd)
            {
                case "info":
                case "status":
                    ShowStatus(player);
                    break;

                case "chests":
                    ShowChests(player);
                    break;

                case "items":
                    ShowItems(player);
                    break;

                case "reload":
                    if (player.HasPermission("wificraft.admin"))
                    {
                        _config = Configuration.Load();
                        _chestManager = new RangeManager(_config);
                        player.SendSuccessMessage("[WifiCraft] Configuration reloaded!");
                    }
                    else
                    {
                        player.SendErrorMessage("You don't have permission to reload the config!");
                    }
                    break;

                case "toggle":
                    if (player.HasPermission("wificraft.admin"))
                    {
                        _config.Enabled = !_config.Enabled;
                        _config.Save();
                        player.SendSuccessMessage($"[WifiCraft] Plugin {(_config.Enabled ? "enabled" : "disabled")}!");
                        TShock.Log.ConsoleInfo($"[WifiCraft] Plugin {(_config.Enabled ? "enabled" : "disabled")} by {player.Name}");
                    }
                    else
                    {
                        player.SendErrorMessage("You don't have permission to toggle the plugin!");
                    }
                    break;

                case "setrange":
                    if (player.HasPermission("wificraft.admin"))
                    {
                        if (args.Parameters.Count < 2 || !int.TryParse(args.Parameters[1], out int newRange))
                        {
                            player.SendErrorMessage("Usage: /wificraft setrange <tiles>");
                            return;
                        }

                        if (newRange < 1 || newRange > 250)
                        {
                            player.SendErrorMessage("Range must be between 1 and 250 tiles!");
                            return;
                        }

                        _config.CraftingRangeTiles = newRange;
                        _config.QuickStackRangeTiles = newRange;
                        _config.Save();
                        player.SendSuccessMessage($"[WifiCraft] Default crafting range set to {newRange} tiles!");
                    }
                    else
                    {
                        player.SendErrorMessage("You don't have permission to change the range!");
                    }
                    break;

                case "debug":
                    if (player.HasPermission("wificraft.admin"))
                    {
                        _config.DebugMode = !_config.DebugMode;
                        _config.Save();
                        player.SendSuccessMessage($"[WifiCraft] Debug mode {(_config.DebugMode ? "enabled" : "disabled")}!");
                    }
                    else
                    {
                        player.SendErrorMessage("You don't have permission to toggle debug mode!");
                    }
                    break;

                case "help":
                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowHelp(TSPlayer player)
        {
            player.SendInfoMessage("=== WifiCraft Commands ===");
            player.SendInfoMessage("/wificraft info - Show your range and status");
            player.SendInfoMessage("/wificraft chests - Show chests in your range");
            player.SendInfoMessage("/wificraft items - Show items available for crafting");

            if (player.HasPermission("wificraft.admin"))
            {
                player.SendInfoMessage("--- Admin Commands ---");
                player.SendInfoMessage("/wificraft reload - Reload configuration");
                player.SendInfoMessage("/wificraft toggle - Enable/disable plugin");
                player.SendInfoMessage("/wificraft setrange <tiles> - Set default range");
                player.SendInfoMessage("/wificraft debug - Toggle debug mode");
            }
        }

        private void ShowStatus(TSPlayer player)
        {
            int craftRange = _chestManager.GetCraftingRange(player);
            int stackRange = _chestManager.GetQuickStackRange(player);
            int chestCount = _chestManager.GetAccessibleChestCount(player);

            player.SendInfoMessage("=== WifiCraft Status ===");
            player.SendInfoMessage($"Plugin: {(_config.Enabled ? "[c/00FF00:Enabled]" : "[c/FF0000:Disabled]")}");
            player.SendInfoMessage($"Your Crafting Range: [c/FFFF00:{craftRange}] tiles");
            player.SendInfoMessage($"Your Quick-Stack Range: [c/FFFF00:{stackRange}] tiles");
            player.SendInfoMessage($"Chests in Range: [c/00FFFF:{chestCount}]");
            player.SendInfoMessage($"(Vanilla range is ~5 tiles)");
        }

        private void ShowChests(TSPlayer player)
        {
            var chests = _chestManager.FindChestsInRange(player);
            int range = _chestManager.GetCraftingRange(player);

            player.SendInfoMessage($"=== Chests in Range ({range} tiles) ===");
            player.SendInfoMessage($"Total: [c/00FFFF:{chests.Count}] chests");

            if (chests.Count > 0 && chests.Count <= 10)
            {
                foreach (int chestId in chests)
                {
                    Chest? chest = Main.chest[chestId];
                    if (chest == null) continue;

                    float distance = _chestManager.GetDistanceToChest(player, chest);
                    int itemCount = 0;
                    for (int i = 0; i < MaxChestItems; i++)
                    {
                        if (chest.item[i] != null && !chest.item[i].IsAir)
                            itemCount++;
                    }

                    player.SendInfoMessage($"  Chest at ({chest.x}, {chest.y}) - {distance:F1} tiles - {itemCount} items");
                }
            }
            else if (chests.Count > 10)
            {
                player.SendInfoMessage("(Too many chests to list individually)");
            }
        }

        private void ShowItems(TSPlayer player)
        {
            var items = _chestManager.GetAccessibleItems(player);

            player.SendInfoMessage("=== Items Available for Crafting ===");
            player.SendInfoMessage($"Total unique items: [c/00FFFF:{items.Count}]");

            int shown = 0;
            foreach (var kvp in items)
            {
                if (shown >= 15)
                {
                    player.SendInfoMessage($"  ...and {items.Count - 15} more item types");
                    break;
                }

                Item tempItem = new Item();
                tempItem.SetDefaults(kvp.Key);
                player.SendInfoMessage($"  {tempItem.Name}: [c/FFFF00:{kvp.Value}]");
                shown++;
            }
        }

        #endregion
    }
}