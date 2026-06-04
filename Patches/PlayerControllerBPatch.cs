using GameNetcodeStuff;
using HarmonyLib;
using ShipCommander.Systems;
using ShipCommander.Networking;
using UnityEngine;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerBPatch
    {
        public static bool EnableCoordinateLogging = false;

        private static Vector3 _lastLoggedPos = Vector3.zero;
        private static float _logTimer = 0f;

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void OnUpdate(PlayerControllerB __instance)
        {
            try
            {
                if (__instance != StartOfRound.Instance?.localPlayerController) return;

                // Log player coordinates relative to the ship if debug logging is enabled
                if (EnableCoordinateLogging && StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
                {
                    _logTimer += Time.deltaTime;
                    Vector3 localPos = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(__instance.transform.position);
                    float distanceMoved = Vector3.Distance(localPos, _lastLoggedPos);

                    if (_logTimer >= 1.5f || distanceMoved > 0.1f)
                    {
                        _logTimer = 0f;
                        _lastLoggedPos = localPos;
                        string cmdX = localPos.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        string cmdY = localPos.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        string cmdZ = localPos.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        Plugin.Logger.LogWarning($"[COORDS] box {cmdX} {cmdY} {cmdZ}");
                    }
                }

                var events = Plugin.Instance?.EventSystem;
                if (events == null || events.BrokenSystems == null) return;

                if (__instance.isInHangarShipRoom)
                {
                    UpdateShipLights(events);
                }

                UpdateAntennaTrigger(__instance, events);

                // Apply stamina recovery modifier from upgrades
                ApplyStaminaUpgrade(__instance);
            }
            catch (System.Exception)
            {
                // Silent catch to not spam Update
            }
        }

        /// <summary>
        /// Apply stamina recovery bonus from suit upgrades.
        /// Increases the rate at which sprint meter refills when NOT sprinting.
        /// </summary>
        private static void ApplyStaminaUpgrade(PlayerControllerB player)
        {
            if (player.isSprinting) return;

            string steamId = SuitUpgradeManager.GetSteamId(player);
            if (steamId == null) return;

            float staminaMultiplier = SuitUpgradeManager.Instance.GetStaminaMultiplier(steamId);
            if (staminaMultiplier <= 1.0f) return;

            // Add extra stamina recovery per frame
            float extraRecovery = Time.deltaTime * (staminaMultiplier - 1.0f) * 0.08f;
            player.sprintMeter = Mathf.Clamp01(player.sprintMeter + extraRecovery);
        }

        private static void UpdateAntennaTrigger(PlayerControllerB player, ShipEventSystem events)
        {
            if (events.CommsAntennaTrigger == null) return;

            bool isCommsBroken = events.BrokenSystems.Contains(ShipEventType.CommsFailure);
            if (!isCommsBroken) return;

            events.CommsAntennaTrigger.interactable = true;
        }

        private static void UpdateShipLights(ShipEventSystem events)
        {
            var shipLights = StartOfRound.Instance?.shipRoomLights;
            if (shipLights == null) return;

            bool isBreakerOn = events.ShipBreakerBoxInstance == null || events.ShipBreakerBoxInstance.isSwitchOn[0];
            bool isBroken = events.BrokenSystems.Contains(ShipEventType.LightsFailure);
            
            bool targetState = shipLights.areLightsOn && isBreakerOn && !isBroken;

            if (shipLights.gameObject.activeSelf != targetState)
            {
                shipLights.gameObject.SetActive(targetState);
            }
        }

        // ============================================================
        //  DAMAGE REDUCTION (Armor upgrade)
        // ============================================================

        [HarmonyPatch("DamagePlayer")]
        [HarmonyPrefix]
        static void DamagePlayerPrefix(PlayerControllerB __instance, ref int damageNumber)
        {
            try
            {
                string steamId = SuitUpgradeManager.GetSteamId(__instance);
                if (steamId == null) return;

                float damageMultiplier = SuitUpgradeManager.Instance.GetDamageMultiplier(steamId);
                if (damageMultiplier < 1.0f)
                {
                    int originalDamage = damageNumber;
                    damageNumber = Mathf.Max(1, Mathf.RoundToInt(damageNumber * damageMultiplier));
                    Plugin.Logger.LogInfo($"[Armor] Reduced damage from {originalDamage} to {damageNumber} for {__instance.playerUsername}");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"[PlayerControllerBPatch] DamagePlayer error: {ex.Message}");
            }
        }

        // ============================================================
        //  CARRY WEIGHT REDUCTION (Carry upgrade)
        // ============================================================

        [HarmonyPatch("GrabObjectClientRpc")]
        [HarmonyPostfix]
        static void GrabObjectPostfix(PlayerControllerB __instance)
        {
            try
            {
                string steamId = SuitUpgradeManager.GetSteamId(__instance);
                if (steamId == null) return;

                float carryMultiplier = SuitUpgradeManager.Instance.GetCarryMultiplier(steamId);
                if (carryMultiplier < 1.0f)
                {
                    float totalWeight = 1.0f;
                    foreach (var item in __instance.ItemSlots)
                    {
                        if (item != null)
                        {
                            float itemWeight = item.itemProperties.weight - 1.0f;
                            totalWeight += itemWeight * carryMultiplier;
                        }
                    }
                    __instance.carryWeight = totalWeight;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"[PlayerControllerBPatch] GrabObject error: {ex.Message}");
            }
        }

        // ============================================================
        //  SPRINT SPEED (Sprint upgrade) 
        // ============================================================

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void SprintSpeedPostfix(PlayerControllerB __instance)
        {
            try
            {
                if (!__instance.isSprinting) return;

                string steamId = SuitUpgradeManager.GetSteamId(__instance);
                if (steamId == null) return;

                float sprintMultiplier = SuitUpgradeManager.Instance.GetSprintMultiplier(steamId);
                if (sprintMultiplier > 1.0f)
                {
                    // Boost the movement speed while sprinting
                    __instance.movementSpeed = __instance.movementSpeed * sprintMultiplier;
                }
            }
            catch (System.Exception)
            {
                // Silent
            }
        }
    }
}
