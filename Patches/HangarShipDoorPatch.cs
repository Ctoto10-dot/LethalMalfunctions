using HarmonyLib;
using ShipCommander.Systems;
using UnityEngine;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(HangarShipDoor))]
    public class HangarShipDoorPatch
    {
        private static System.Collections.Generic.Dictionary<HangarShipDoor, bool> _wasDoorForcedClosed = new System.Collections.Generic.Dictionary<HangarShipDoor, bool>();
        private static float _savedDoorPower = 1f;
        private static bool _wasDoorForcedOpen = false;

        [HarmonyPatch("SetDoorOpen")]
        [HarmonyPrefix]
        private static bool PreventDoorOpen(HangarShipDoor __instance)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem == null) return true;

            bool isDoorsSwitchOn = eventSystem.ShipBreakerBoxInstance == null || eventSystem.ShipBreakerBoxInstance.isSwitchOn[1];
            bool isDoorsJammed = eventSystem.BrokenSystems.Contains(ShipEventType.DoorsJam);

            // If the breaker switch is ON and the doors are jammed, block opening them!
            if (isDoorsSwitchOn && isDoorsJammed)
            {
                Plugin.Logger.LogInfo("[HangarShipDoorPatch] SetDoorOpen blocked: doors are jammed and breaker switch is ON.");
                return false; // Block execution (button is clickable but does not work)
            }

            return true; // Allow execution
        }

        [HarmonyPatch("Update")]
        [HarmonyPrefix]
        private static void SaveDoorPower(HangarShipDoor __instance)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem == null) return;

            bool isDoorsSwitchOn = eventSystem.ShipBreakerBoxInstance == null || eventSystem.ShipBreakerBoxInstance.isSwitchOn[1];
            if (isDoorsSwitchOn)
            {
                // Only save the active power level if the switch is ON
                _savedDoorPower = __instance.doorPower;
            }
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        private static void ForceDoorCycle(HangarShipDoor __instance)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem == null) return;

            bool isDoorsSwitchOn = eventSystem.ShipBreakerBoxInstance == null || eventSystem.ShipBreakerBoxInstance.isSwitchOn[1];
            bool isLanded = StartOfRound.Instance != null && StartOfRound.Instance.shipHasLanded;

            if (!isDoorsSwitchOn)
            {
                // Override doorPower to the saved power level to freeze its charge display
                __instance.doorPower = _savedDoorPower;

                // Disable control panel buttons
                __instance.SetDoorButtonsEnabled(false);

                if (isLanded)
                {
                    // Check if door is closed in animator
                    bool isClosed = false;
                    if (__instance.shipDoorsAnimator != null)
                    {
                        try { isClosed = __instance.shipDoorsAnimator.GetBool("Closed"); } catch {}
                    }

                    // Force doors open if they are closed or not already forced open
                    if (isClosed || !_wasDoorForcedOpen)
                    {
                        _wasDoorForcedOpen = true;
                        // Temporarily enable buttons so SetDoorOpen and PlayDoorAnimation can execute
                        __instance.SetDoorButtonsEnabled(true);
                        __instance.SetDoorOpen();
                        __instance.PlayDoorAnimation(false);
                        __instance.SetDoorButtonsEnabled(false);
                        Plugin.Logger.LogInfo($"[HangarShipDoorPatch] Doors switch flipped OFF: forcing doors open and freezing power to {_savedDoorPower:F2}.");
                    }
                }
                else
                {
                    // If not landed (space/transit), we stop forcing doors open
                    if (_wasDoorForcedOpen)
                    {
                        _wasDoorForcedOpen = false;
                        Plugin.Logger.LogInfo("[HangarShipDoorPatch] Ship is not landed. Stop forcing doors open.");
                    }
                }
            }
            else
            {
                bool isDoorsJammed = eventSystem.BrokenSystems.Contains(ShipEventType.DoorsJam);

                if (_wasDoorForcedOpen)
                {
                    _wasDoorForcedOpen = false;
                    
                    // Restore the frozen power level
                    __instance.doorPower = _savedDoorPower;

                    // Enable/disable buttons depending on whether doors are currently jammed
                    __instance.SetDoorButtonsEnabled(!isDoorsJammed);

                    if (isDoorsJammed)
                    {
                        // Slam the door closed again and resume drain
                        __instance.SetDoorButtonsEnabled(true);
                        __instance.SetDoorClosed();
                        __instance.PlayDoorAnimation(true);
                        __instance.SetDoorButtonsEnabled(false);
                        Plugin.Logger.LogInfo("[HangarShipDoorPatch] Doors switch flipped ON while jammed: slamming doors closed and resuming drain.");
                    }
                    else
                    {
                        Plugin.Logger.LogInfo($"[HangarShipDoorPatch] Doors switch flipped ON: restoring normal door control buttons and power to {_savedDoorPower:F2}.");
                    }
                }
                else
                {
                    // Ensure buttonsEnabled is in sync with the jammed state when Switch 1 is ON
                    if (__instance.buttonsEnabled == isDoorsJammed)
                    {
                        __instance.SetDoorButtonsEnabled(!isDoorsJammed);
                    }
                }

                // If the door is jammed, make sure it is closed and draining (if power > 0.0f)
                if (isDoorsJammed)
                {
                    if (__instance.doorPower > 0.0f)
                    {
                        bool isClosed = false;
                        if (__instance.shipDoorsAnimator != null)
                        {
                            try { isClosed = __instance.shipDoorsAnimator.GetBool("Closed"); } catch {}
                        }

                        if (!isClosed)
                        {
                            // If somehow the door is open while jammed and switch is ON, force it closed
                            __instance.SetDoorButtonsEnabled(true);
                            __instance.SetDoorClosed();
                            __instance.PlayDoorAnimation(true);
                            __instance.SetDoorButtonsEnabled(false);
                            Plugin.Logger.LogInfo("[HangarShipDoorPatch] DoorsJam: Forcing doors closed to resume drain.");
                        }
                    }
                }
            }

            // Sync the interactability of physical buttons on the panel:
            // - If Switch 1 is OFF, they have no power (un-interactable).
            // - If Switch 1 is ON, they are clickable (even if jammed, green clicks but won't open door).
            GameObject panel = GameObject.Find("HangarDoorButtonPanel");
            if (panel != null)
            {
                InteractTrigger[] triggers = panel.GetComponentsInChildren<InteractTrigger>(true);
                foreach (var t in triggers)
                {
                    t.interactable = isDoorsSwitchOn;
                }
            }
        }
    }
}
