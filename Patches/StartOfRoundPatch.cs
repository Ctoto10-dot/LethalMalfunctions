using HarmonyLib;
using ShipCommander.Systems;
using ShipCommander.Networking;
using UnityEngine;

namespace ShipCommander.Patches
{
    /// <summary>
    /// Patches StartOfRound to initialize and shutdown ShipCommander systems
    /// at the appropriate lifecycle points, and handle suit upgrade body recovery.
    /// </summary>
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        /// <summary>
        /// When the round starts, initialize all ShipCommander systems and load upgrades.
        /// </summary>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void OnStartOfRound()
        {
            Plugin.Logger.LogInfo("StartOfRound detected — initializing ShipCommander.");
            Plugin.Instance?.InitializeSystems();

            // Load upgrade data (host only — clients receive via network sync)
            if (Unity.Netcode.NetworkManager.Singleton != null && 
                (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
            {
                SuitUpgradeManager.Instance.LoadData();

                // Apply upgrades to all connected players
                if (StartOfRound.Instance != null)
                {
                    foreach (var player in StartOfRound.Instance.allPlayerScripts)
                    {
                        if (player != null && player.isPlayerControlled)
                        {
                            string steamId = SuitUpgradeManager.GetSteamId(player);
                            if (steamId != null)
                            {
                                var data = SuitUpgradeManager.Instance.GetPlayerData(steamId);
                                data.PlayerName = player.playerUsername;
                                ShipCommanderNetwork.ApplyUpgradesToPlayer(steamId);
                            }
                        }
                    }

                    // Send full sync to all clients
                    ShipCommanderNetwork.SendFullSync();
                }
            }
        }

        /// <summary>
        /// When the ship leaves a moon, check dead players' bodies for upgrade recovery.
        /// </summary>
        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPostfix]
        static void OnShipLeft()
        {
            Plugin.Logger.LogInfo("Ship has left — shutting down event system.");
            Plugin.Instance?.ShutdownSystems();

            // Check dead bodies for upgrade recovery (host only)
            if (Unity.Netcode.NetworkManager.Singleton != null && 
                (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
            {
                CheckDeadBodiesForUpgrades();
            }
        }

        /// <summary>
        /// When the game ends, clean up.
        /// </summary>
        [HarmonyPatch("EndOfGame")]
        [HarmonyPostfix]
        static void OnEndOfGame()
        {
            Plugin.Logger.LogInfo("End of game — cleaning up ShipCommander.");
            Plugin.Instance?.ShutdownSystems();
        }

        /// <summary>
        /// When a player is revived/respawned, reapply their upgrades.
        /// </summary>
        [HarmonyPatch("ReviveDeadPlayers")]
        [HarmonyPostfix]
        static void OnReviveDeadPlayers()
        {
            Plugin.Logger.LogInfo("Players revived — reapplying suit upgrades.");

            if (StartOfRound.Instance == null) return;

            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player != null && player.isPlayerControlled)
                {
                    string steamId = SuitUpgradeManager.GetSteamId(player);
                    if (steamId == null) continue;

                    // Apply max health upgrade
                    int maxHealth = SuitUpgradeManager.Instance.GetMaxHealth(steamId);
                    if (maxHealth > 100)
                    {
                        player.health = maxHealth;
                        Plugin.Logger.LogInfo($"[Upgrades] Set {player.playerUsername} health to {maxHealth} (Health upgrade)");
                    }

                    // Reapply jump force
                    ShipCommanderNetwork.ApplyUpgradesToPlayer(steamId);
                }
            }

            // Send full sync after respawn
            if (Unity.Netcode.NetworkManager.Singleton != null && 
                (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
            {
                ShipCommanderNetwork.SendFullSync();
            }
        }

        /// <summary>
        /// Check all dead players. If their body is NOT on the ship, reset their upgrades.
        /// If their body IS on the ship, keep their upgrades.
        /// </summary>
        private static void CheckDeadBodiesForUpgrades()
        {
            if (StartOfRound.Instance == null) return;

            var allBodies = UnityEngine.Object.FindObjectsOfType<DeadBodyInfo>();
            Plugin.Logger.LogInfo($"[Upgrades] Checking {allBodies.Length} dead bodies for upgrade recovery...");

            foreach (var player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled) continue;

                string steamId = SuitUpgradeManager.GetSteamId(player);
                if (steamId == null) continue;

                var upgradeData = SuitUpgradeManager.Instance.GetPlayerData(steamId);

                // Check if player has any upgrades worth saving
                bool hasUpgrades = false;
                foreach (UpgradeType type in System.Enum.GetValues(typeof(UpgradeType)))
                {
                    if (upgradeData.GetLevel(type) > 0)
                    {
                        hasUpgrades = true;
                        break;
                    }
                }

                if (!hasUpgrades) continue; // No upgrades to lose

                // If player is dead, check if their body made it to the ship
                if (player.isPlayerDead)
                {
                    bool bodyOnShip = false;

                    foreach (var body in allBodies)
                    {
                        if (body == null) continue;

                        // Match body to player
                        if (body.playerObjectId == (int)player.playerClientId)
                        {
                            // Check if body is in the ship
                            if (body.isInShip || (StartOfRound.Instance.elevatorTransform != null &&
                                Vector3.Distance(body.transform.position, StartOfRound.Instance.elevatorTransform.position) < 20f))
                            {
                                bodyOnShip = true;
                            }
                            break;
                        }
                    }

                    if (bodyOnShip)
                    {
                        Plugin.Logger.LogInfo($"[Upgrades] {player.playerUsername}'s body was recovered! Upgrades preserved.");
                    }
                    else
                    {
                        Plugin.Logger.LogWarning($"[Upgrades] {player.playerUsername}'s body was NOT recovered! Resetting all upgrades.");
                        SuitUpgradeManager.Instance.ResetAllUpgrades(steamId);

                        // Notify all clients
                        if (Unity.Netcode.NetworkManager.Singleton != null)
                        {
                            ShipCommanderNetwork.UpgradeResetMessage.SendClients(steamId);
                        }
                    }
                }
            }
        }
    }
}
