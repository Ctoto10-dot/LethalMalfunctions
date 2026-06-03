using GameNetcodeStuff;
using HarmonyLib;
using ShipCommander.Systems;
using UnityEngine;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerBPatch
    {
        public static bool EnableCoordinateLogging = false; // Set to true to enable copy-pasteable coordinate logging

        private static Vector3 _lastLoggedPos = Vector3.zero;
        private static float _logTimer = 0f;

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void OnUpdate(PlayerControllerB __instance)
        {
            try
            {
                if (__instance != StartOfRound.Instance?.localPlayerController) return;

                // Log player coordinates relative to the ship (elevatorTransform) if debug logging is enabled
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

                // Check and enforce wrench requirement for repairing comms antenna
                UpdateAntennaTrigger(__instance, events);
            }
            catch (System.Exception)
            {
                // Silent catch to not spam Update
            }
        }

        private static void UpdateAntennaTrigger(PlayerControllerB player, ShipEventSystem events)
        {
            if (events.CommsAntennaTrigger == null) return;

            bool isCommsBroken = events.BrokenSystems.Contains(ShipEventType.CommsFailure);
            if (!isCommsBroken) return;

            // Antenna is always interactable when comms are broken
            events.CommsAntennaTrigger.interactable = true;
        }

        private static void UpdateShipLights(ShipEventSystem events)
        {
            var shipLights = StartOfRound.Instance?.shipRoomLights;
            if (shipLights == null) return;

            // Lights should be controlled by the vanilla switch, but forced OFF if the breaker switch is OFF or lights are broken
            bool isBreakerOn = events.ShipBreakerBoxInstance == null || events.ShipBreakerBoxInstance.isSwitchOn[0];
            bool isBroken = events.BrokenSystems.Contains(ShipEventType.LightsFailure);
            
            bool targetState = shipLights.areLightsOn && isBreakerOn && !isBroken;

            if (shipLights.gameObject.activeSelf != targetState)
            {
                shipLights.gameObject.SetActive(targetState);
            }
        }
    }
}
