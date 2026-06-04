using System;
using System.Collections.Generic;
using System.IO;

namespace ShipCommander.Systems
{
    /// <summary>
    /// Handles saving and loading player upgrade data to/from JSON files.
    /// Save files are stored in BepInEx/config/ and keyed by save slot.
    /// </summary>
    public static class UpgradeSaveData
    {
        private const string FILE_PREFIX = "LethalMalfunctions_Upgrades";

        /// <summary>
        /// Get the save file path for the current save slot.
        /// </summary>
        private static string GetSavePath()
        {
            // Determine current save slot
            int saveSlot = 0;
            try
            {
                if (GameNetworkManager.Instance != null)
                {
                    saveSlot = GameNetworkManager.Instance.saveFileNum;
                }
            }
            catch { }

            string configDir = BepInEx.Paths.ConfigPath;
            return Path.Combine(configDir, $"{FILE_PREFIX}_Save{saveSlot + 1}.json");
        }

        /// <summary>
        /// Save all player upgrade data to JSON.
        /// </summary>
        public static void Save(Dictionary<string, PlayerUpgradeData> data)
        {
            try
            {
                string path = GetSavePath();
                string json = SerializeToJson(data);
                File.WriteAllText(path, json);
                Plugin.Logger.LogInfo($"[UpgradeSaveData] Saved upgrade data to {path}");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[UpgradeSaveData] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// Load all player upgrade data from JSON.
        /// </summary>
        public static Dictionary<string, PlayerUpgradeData> Load()
        {
            try
            {
                string path = GetSavePath();
                if (!File.Exists(path))
                {
                    Plugin.Logger.LogInfo($"[UpgradeSaveData] No save file found at {path}, starting fresh.");
                    return new Dictionary<string, PlayerUpgradeData>();
                }

                string json = File.ReadAllText(path);
                var data = DeserializeFromJson(json);
                Plugin.Logger.LogInfo($"[UpgradeSaveData] Loaded upgrade data from {path} ({data.Count} players)");
                return data;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[UpgradeSaveData] Failed to load: {ex.Message}");
                return new Dictionary<string, PlayerUpgradeData>();
            }
        }

        /// <summary>
        /// Simple JSON serializer for upgrade data (no external dependencies needed).
        /// </summary>
        private static string SerializeToJson(Dictionary<string, PlayerUpgradeData> data)
        {
            var lines = new List<string>();
            lines.Add("{");

            int playerIndex = 0;
            foreach (var kvp in data)
            {
                string steamId = kvp.Key;
                var playerData = kvp.Value;

                lines.Add($"  \"{steamId}\": {{");
                lines.Add($"    \"PlayerName\": \"{EscapeJson(playerData.PlayerName)}\",");

                int upgradeIndex = 0;
                var upgradeTypes = (UpgradeType[])Enum.GetValues(typeof(UpgradeType));
                foreach (UpgradeType type in upgradeTypes)
                {
                    int level = playerData.GetLevel(type);
                    string comma = (upgradeIndex < upgradeTypes.Length - 1) ? "," : "";
                    lines.Add($"    \"{type}\": {level}{comma}");
                    upgradeIndex++;
                }

                string playerComma = (playerIndex < data.Count - 1) ? "," : "";
                lines.Add($"  }}{playerComma}");
                playerIndex++;
            }

            lines.Add("}");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Simple JSON deserializer for upgrade data.
        /// </summary>
        private static Dictionary<string, PlayerUpgradeData> DeserializeFromJson(string json)
        {
            var result = new Dictionary<string, PlayerUpgradeData>();

            // Remove whitespace and braces
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            // Find each player block
            string currentSteamId = null;
            PlayerUpgradeData currentData = null;
            bool insidePlayerBlock = false;

            string[] lines = json.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim().TrimEnd(',');
                if (string.IsNullOrEmpty(line)) continue;

                // Detect start of player block: "steamId": {
                if (!insidePlayerBlock && line.Contains("\": {"))
                {
                    int quoteStart = line.IndexOf('"') + 1;
                    int quoteEnd = line.IndexOf('"', quoteStart);
                    if (quoteStart > 0 && quoteEnd > quoteStart)
                    {
                        currentSteamId = line.Substring(quoteStart, quoteEnd - quoteStart);
                        currentData = new PlayerUpgradeData();
                        insidePlayerBlock = true;
                    }
                    continue;
                }

                // Detect end of player block
                if (insidePlayerBlock && line.StartsWith("}"))
                {
                    if (currentSteamId != null && currentData != null)
                    {
                        result[currentSteamId] = currentData;
                    }
                    currentSteamId = null;
                    currentData = null;
                    insidePlayerBlock = false;
                    continue;
                }

                // Parse key-value pairs inside player block
                if (insidePlayerBlock && currentData != null && line.Contains(":"))
                {
                    // Split on first ':'
                    int colonIndex = line.IndexOf(':');
                    string key = line.Substring(0, colonIndex).Trim().Trim('"');
                    string value = line.Substring(colonIndex + 1).Trim().TrimEnd(',').Trim('"');

                    if (key == "PlayerName")
                    {
                        currentData.PlayerName = UnescapeJson(value);
                    }
                    else
                    {
                        // Try to parse as UpgradeType
                        if (Enum.TryParse<UpgradeType>(key, out UpgradeType upgradeType))
                        {
                            if (int.TryParse(value, out int level))
                            {
                                currentData.SetLevel(upgradeType, level);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string UnescapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        /// <summary>
        /// Serialize upgrade data to a compact network-friendly string.
        /// Format: "steamId1:Sprint=2,Armor=1,Jump=0|steamId2:Sprint=1,Armor=0"
        /// </summary>
        public static string SerializeForNetwork(Dictionary<string, PlayerUpgradeData> data)
        {
            var parts = new List<string>();
            foreach (var kvp in data)
            {
                var upgrades = new List<string>();
                upgrades.Add($"Name={EscapeJson(kvp.Value.PlayerName)}");
                foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
                {
                    upgrades.Add($"{type}={kvp.Value.GetLevel(type)}");
                }
                parts.Add($"{kvp.Key}:{string.Join(",", upgrades)}");
            }
            return string.Join("|", parts);
        }

        /// <summary>
        /// Deserialize upgrade data from a compact network string.
        /// </summary>
        public static Dictionary<string, PlayerUpgradeData> DeserializeFromNetwork(string networkString)
        {
            var result = new Dictionary<string, PlayerUpgradeData>();
            if (string.IsNullOrEmpty(networkString)) return result;

            string[] players = networkString.Split('|');
            foreach (string playerStr in players)
            {
                if (string.IsNullOrEmpty(playerStr)) continue;

                int colonIndex = playerStr.IndexOf(':');
                if (colonIndex < 0) continue;

                string steamId = playerStr.Substring(0, colonIndex);
                string upgradesStr = playerStr.Substring(colonIndex + 1);

                var data = new PlayerUpgradeData();
                string[] pairs = upgradesStr.Split(',');
                foreach (string pair in pairs)
                {
                    string[] kv = pair.Split('=');
                    if (kv.Length != 2) continue;

                    if (kv[0] == "Name")
                    {
                        data.PlayerName = UnescapeJson(kv[1]);
                    }
                    else if (Enum.TryParse<UpgradeType>(kv[0], out UpgradeType type))
                    {
                        if (int.TryParse(kv[1], out int level))
                        {
                            data.SetLevel(type, level);
                        }
                    }
                }
                result[steamId] = data;
            }
            return result;
        }
    }
}
