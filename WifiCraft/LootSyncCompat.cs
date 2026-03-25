using System;
using System.Linq;
using System.Reflection;
using TShockAPI;

namespace WifiCraft
{
    public static class LootSyncCompat
    {
        private static bool _initialized;
        private static bool _lootSyncAvailable;
        private static object? _databaseInstance;
        private static MethodInfo? _isChestPlayerPlacedMethod;

        public static bool IsLootSyncAvailable
        {
            get
            {
                EnsureInitialized();
                return _lootSyncAvailable;
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var lootSyncAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "LootSync");

                if (lootSyncAssembly == null)
                {
                    _lootSyncAvailable = false;
                    return;
                }

                var databaseType = lootSyncAssembly.GetType("LootSync.Database");
                if (databaseType == null)
                {
                    _lootSyncAvailable = false;
                    return;
                }

                var instanceProperty = databaseType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty != null)
                {
                    _databaseInstance = instanceProperty.GetValue(null);
                }
                else
                {
                    var pluginType = lootSyncAssembly.GetType("LootSync.Plugin");
                    if (pluginType != null)
                    {
                        var databaseProperty = pluginType.GetProperty("Database",
                            BindingFlags.Public | BindingFlags.Static);

                        if (databaseProperty != null)
                        {
                            _databaseInstance = databaseProperty.GetValue(null);
                        }
                    }
                }

                if (_databaseInstance == null)
                {
                    _lootSyncAvailable = false;
                    return;
                }

                _isChestPlayerPlacedMethod = databaseType.GetMethod("IsChestPlayerPlaced",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(int), typeof(int) },
                    null);

                if (_isChestPlayerPlacedMethod == null)
                {
                    _lootSyncAvailable = false;
                    return;
                }

                _lootSyncAvailable = true;
                TShock.Log.ConsoleInfo("[WifiCraft] LootSync compatibility enabled.");
            }
            catch (Exception ex)
            {
                _lootSyncAvailable = false;
                TShock.Log.ConsoleError($"[WifiCraft] Error initializing LootSync compatibility: {ex.Message}");
            }
        }

        public static bool IsChestSafeToSync(int x, int y)
        {
            EnsureInitialized();

            if (!_lootSyncAvailable || _databaseInstance == null || _isChestPlayerPlacedMethod == null)
            {
                return true;
            }

            try
            {
                var result = _isChestPlayerPlacedMethod.Invoke(_databaseInstance, new object[] { x, y });

                if (result is bool isPlayerPlaced)
                {
                    return isPlayerPlaced;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        public static void Reinitialize()
        {
            _initialized = false;
            _lootSyncAvailable = false;
            _databaseInstance = null;
            _isChestPlayerPlacedMethod = null;
            EnsureInitialized();
        }
    }
}