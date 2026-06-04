using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ShipCommander.Systems
{
    public enum UpgradeType
    {
        Sprint,
        Armor,
        Jump,
        Carry,
        Stamina,
        Health
    }

    public class PlayerUpgradeData
    {
        public string PlayerName { get; set; } = "";
        public Dictionary<UpgradeType, int> Levels { get; set; } = new Dictionary<UpgradeType, int>();

        public PlayerUpgradeData()
        {
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                Levels[type] = 0;
            }
        }

        public int GetLevel(UpgradeType type)
        {
            return Levels.ContainsKey(type) ? Levels[type] : 0;
        }

        public void SetLevel(UpgradeType type, int level)
        {
            Levels[type] = Mathf.Clamp(level, 0, SuitUpgradeManager.MAX_LEVEL);
        }

        public int GetTotalInvested()
        {
            int total = 0;
            foreach (var kvp in Levels)
            {
                for (int i = 1; i <= kvp.Value; i++)
                {
                    total += SuitUpgradeManager.GetPrice(kvp.Key, i);
                }
            }
            return total;
        }
    }

    public class SuitUpgradeManager
    {
        public const int MAX_LEVEL = 3;

        // Store upgrades per player by Steam ID (as string)
        private Dictionary<string, PlayerUpgradeData> _playerUpgrades = new Dictionary<string, PlayerUpgradeData>();

        // Singleton
        private static SuitUpgradeManager _instance;
        public static SuitUpgradeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SuitUpgradeManager();
                }
                return _instance;
            }
        }

        // ============================================================
        //  PRICE TABLE
        // ============================================================
        //  Prices per level (index 0 = Lv1, index 1 = Lv2, index 2 = Lv3)
        private static readonly Dictionary<UpgradeType, int[]> Prices = new Dictionary<UpgradeType, int[]>
        {
            { UpgradeType.Sprint,  new int[] { 80,  200, 450 } },
            { UpgradeType.Armor,   new int[] { 100, 250, 500 } },
            { UpgradeType.Jump,    new int[] { 60,  150, 350 } },
            { UpgradeType.Carry,   new int[] { 80,  200, 400 } },
            { UpgradeType.Stamina, new int[] { 100, 250, 500 } },
            { UpgradeType.Health,  new int[] { 100, 250, 500 } }
        };

        // ============================================================
        //  BONUS VALUES
        // ============================================================
        //  Multipliers/values per level (index 0 = Lv1, index 1 = Lv2, index 2 = Lv3)

        // Sprint: speed multiplier bonus (+15%, +25%, +35%)
        public static readonly float[] SprintBonuses = { 0.15f, 0.25f, 0.35f };

        // Armor: damage reduction fraction (-10%, -18%, -25%)
        public static readonly float[] ArmorBonuses = { 0.10f, 0.18f, 0.25f };

        // Jump: jump force multiplier bonus (+20%, +35%, +50%)
        public static readonly float[] JumpBonuses = { 0.20f, 0.35f, 0.50f };

        // Carry: weight penalty reduction (-15%, -25%, -35%)
        public static readonly float[] CarryBonuses = { 0.15f, 0.25f, 0.35f };

        // Stamina: sprint time recovery multiplier bonus (+20%, +35%, +50%)
        public static readonly float[] StaminaBonuses = { 0.20f, 0.35f, 0.50f };

        // Health: max HP values (120, 140, 160)
        public static readonly int[] HealthValues = { 120, 140, 160 };

        // ============================================================
        //  PUBLIC API
        // ============================================================

        /// <summary>
        /// Get the price for a specific upgrade at a specific level (1-indexed).
        /// </summary>
        public static int GetPrice(UpgradeType type, int level)
        {
            if (level < 1 || level > MAX_LEVEL) return 0;
            return Prices[type][level - 1];
        }

        /// <summary>
        /// Get the price for the NEXT level of an upgrade for a player.
        /// Returns -1 if already at max.
        /// </summary>
        public int GetNextPrice(string steamId, UpgradeType type)
        {
            var data = GetPlayerData(steamId);
            int currentLevel = data.GetLevel(type);
            if (currentLevel >= MAX_LEVEL) return -1;
            return GetPrice(type, currentLevel + 1);
        }

        /// <summary>
        /// Try to purchase an upgrade for a player. Returns true if successful.
        /// </summary>
        public bool TryPurchase(string steamId, UpgradeType type, Terminal terminal)
        {
            var data = GetPlayerData(steamId);
            int currentLevel = data.GetLevel(type);

            if (currentLevel >= MAX_LEVEL)
            {
                Plugin.Logger.LogInfo($"[SuitUpgradeManager] {steamId} already at max level for {type}");
                return false;
            }

            int price = GetPrice(type, currentLevel + 1);

            if (terminal.groupCredits < price)
            {
                Plugin.Logger.LogInfo($"[SuitUpgradeManager] Not enough credits. Need {price}, have {terminal.groupCredits}");
                return false;
            }

            // Deduct credits
            terminal.groupCredits -= price;

            // Apply upgrade
            data.SetLevel(type, currentLevel + 1);

            Plugin.Logger.LogInfo($"[SuitUpgradeManager] {data.PlayerName} upgraded {type} to Lv.{currentLevel + 1} for {price}cr");

            // Save
            UpgradeSaveData.Save(_playerUpgrades);

            return true;
        }

        /// <summary>
        /// Force-set a specific upgrade level for a player (for debug/network sync).
        /// </summary>
        public void SetUpgradeLevel(string steamId, UpgradeType type, int level, string playerName = null)
        {
            var data = GetPlayerData(steamId);
            if (playerName != null) data.PlayerName = playerName;
            data.SetLevel(type, level);
            Plugin.Logger.LogInfo($"[SuitUpgradeManager] Set {type} to Lv.{level} for {steamId}");
        }

        /// <summary>
        /// Max out all upgrades for a player (debug command).
        /// </summary>
        public void MaxAllUpgrades(string steamId, string playerName = null)
        {
            var data = GetPlayerData(steamId);
            if (playerName != null) data.PlayerName = playerName;
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                data.SetLevel(type, MAX_LEVEL);
            }
            UpgradeSaveData.Save(_playerUpgrades);
            Plugin.Logger.LogInfo($"[SuitUpgradeManager] Maxed all upgrades for {steamId}");
        }

        /// <summary>
        /// Reset all upgrades for a player (death without body recovery).
        /// </summary>
        public void ResetAllUpgrades(string steamId)
        {
            var data = GetPlayerData(steamId);
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                data.SetLevel(type, 0);
            }
            UpgradeSaveData.Save(_playerUpgrades);
            Plugin.Logger.LogWarning($"[SuitUpgradeManager] RESET all upgrades for {data.PlayerName} ({steamId}) — body not recovered!");
        }

        /// <summary>
        /// Get upgrade data for a player, creating if needed.
        /// </summary>
        public PlayerUpgradeData GetPlayerData(string steamId)
        {
            if (!_playerUpgrades.ContainsKey(steamId))
            {
                _playerUpgrades[steamId] = new PlayerUpgradeData();
            }
            return _playerUpgrades[steamId];
        }

        /// <summary>
        /// Load all upgrade data from save file.
        /// </summary>
        public void LoadData()
        {
            _playerUpgrades = UpgradeSaveData.Load();
            Plugin.Logger.LogInfo($"[SuitUpgradeManager] Loaded upgrade data for {_playerUpgrades.Count} players");
        }

        /// <summary>
        /// Save current data.
        /// </summary>
        public void SaveData()
        {
            UpgradeSaveData.Save(_playerUpgrades);
        }

        /// <summary>
        /// Get all player upgrades (for network sync).
        /// </summary>
        public Dictionary<string, PlayerUpgradeData> GetAllData()
        {
            return _playerUpgrades;
        }

        /// <summary>
        /// Set all player upgrades (for network sync on client).
        /// </summary>
        public void SetAllData(Dictionary<string, PlayerUpgradeData> data)
        {
            _playerUpgrades = data;
        }

        // ============================================================
        //  STAT APPLICATION
        // ============================================================

        /// <summary>
        /// Get the sprint speed multiplier for a player.
        /// Returns 1.0 for no bonus, 1.35 for max sprint upgrade.
        /// </summary>
        public float GetSprintMultiplier(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Sprint);
            if (level <= 0) return 1.0f;
            return 1.0f + SprintBonuses[level - 1];
        }

        /// <summary>
        /// Get the damage multiplier for a player.
        /// Returns 1.0 for no bonus, 0.75 for max armor upgrade.
        /// </summary>
        public float GetDamageMultiplier(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Armor);
            if (level <= 0) return 1.0f;
            return 1.0f - ArmorBonuses[level - 1];
        }

        /// <summary>
        /// Get the jump force multiplier for a player.
        /// Returns 1.0 for no bonus, 1.5 for max jump upgrade.
        /// </summary>
        public float GetJumpMultiplier(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Jump);
            if (level <= 0) return 1.0f;
            return 1.0f + JumpBonuses[level - 1];
        }

        /// <summary>
        /// Get the carry weight reduction multiplier for a player.
        /// Returns 1.0 for no bonus, 0.65 for max carry upgrade (35% less weight penalty).
        /// </summary>
        public float GetCarryMultiplier(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Carry);
            if (level <= 0) return 1.0f;
            return 1.0f - CarryBonuses[level - 1];
        }

        /// <summary>
        /// Get the stamina recovery multiplier for a player.
        /// Returns 1.0 for no bonus, 1.5 for max stamina upgrade.
        /// </summary>
        public float GetStaminaMultiplier(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Stamina);
            if (level <= 0) return 1.0f;
            return 1.0f + StaminaBonuses[level - 1];
        }

        /// <summary>
        /// Get the max health for a player.
        /// Returns 100 for no bonus, 160 for max health upgrade.
        /// </summary>
        public int GetMaxHealth(string steamId)
        {
            int level = GetPlayerData(steamId).GetLevel(UpgradeType.Health);
            if (level <= 0) return 100;
            return HealthValues[level - 1];
        }

        // ============================================================
        //  TERMINAL UI
        // ============================================================

        /// <summary>
        /// Generate the upgrades display string for the terminal.
        /// </summary>
        public string GetUpgradesDisplay(string steamId, int currentCredits)
        {
            var data = GetPlayerData(steamId);
            string name = string.IsNullOrEmpty(data.PlayerName) ? "Unknown" : data.PlayerName;

            string display = "\n";
            display += "SUIT UPGRADE SYSTEM\n";
            display += "========================\n";
            display += $"Employee: {name}\n\n";

            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                int level = data.GetLevel(type);
                string bar = new string('>', level) + new string('-', MAX_LEVEL - level);
                string typeName = type.ToString().ToUpper().PadRight(8);

                string nextStr;
                if (level >= MAX_LEVEL)
                {
                    nextStr = "MAX";
                }
                else
                {
                    int nextPrice = GetPrice(type, level + 1);
                    nextStr = $"Next: {nextPrice}cr";
                }

                string bonusStr = GetBonusDescription(type, level);

                display += $"  {typeName} [{bar}] Lv.{level}  {nextStr}\n";
                if (level > 0)
                {
                    display += $"           {bonusStr}\n";
                }
            }

            display += $"\nTotal invested: {data.GetTotalInvested()} credits\n";
            display += $"Balance: {currentCredits} credits\n\n";
            display += "Use 'upgrade [type]' to purchase.\n\n";

            return display;
        }

        /// <summary>
        /// Get a human-readable description of the current bonus for an upgrade.
        /// </summary>
        private string GetBonusDescription(UpgradeType type, int level)
        {
            if (level <= 0) return "";

            switch (type)
            {
                case UpgradeType.Sprint:
                    return $"+{(int)(SprintBonuses[level - 1] * 100)}% speed";
                case UpgradeType.Armor:
                    return $"-{(int)(ArmorBonuses[level - 1] * 100)}% damage";
                case UpgradeType.Jump:
                    return $"+{(int)(JumpBonuses[level - 1] * 100)}% jump height";
                case UpgradeType.Carry:
                    return $"-{(int)(CarryBonuses[level - 1] * 100)}% weight penalty";
                case UpgradeType.Stamina:
                    return $"+{(int)(StaminaBonuses[level - 1] * 100)}% recovery";
                case UpgradeType.Health:
                    return $"{HealthValues[level - 1]} HP max";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Parse an upgrade type from string input.
        /// </summary>
        public static UpgradeType? ParseUpgradeType(string input)
        {
            switch (input.ToLower().Trim())
            {
                case "sprint":
                case "speed":
                case "run":
                    return UpgradeType.Sprint;
                case "armor":
                case "defence":
                case "defense":
                case "def":
                    return UpgradeType.Armor;
                case "jump":
                case "leap":
                    return UpgradeType.Jump;
                case "carry":
                case "weight":
                case "lift":
                    return UpgradeType.Carry;
                case "stamina":
                case "stam":
                case "endurance":
                    return UpgradeType.Stamina;
                case "health":
                case "hp":
                case "life":
                    return UpgradeType.Health;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Get the Steam ID string for a player controller.
        /// </summary>
        public static string GetSteamId(GameNetcodeStuff.PlayerControllerB player)
        {
            if (player == null) return null;

            try
            {
                // Try to get Steam ID from the player's Steam data
                if (player.playerSteamId != 0)
                {
                    return player.playerSteamId.ToString();
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[SuitUpgradeManager] Error getting Steam ID: {ex.Message}");
            }

            // Fallback: use player client ID
            return $"local_{player.playerClientId}";
        }
    }
}
