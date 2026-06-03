using HarmonyLib;
using ShipCommander.Systems;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(ManualCameraRenderer))]
    public class ManualCameraRendererPatch
    {
        private static bool IsRadarOffline()
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem == null) return false;

            if (eventSystem.BrokenSystems.Contains(ShipEventType.RadarInterference) || eventSystem.ShockerOverloadActive)
            {
                return true;
            }

            if (eventSystem.ShipBreakerBoxInstance != null && !eventSystem.ShipBreakerBoxInstance.isSwitchOn[3])
            {
                return true;
            }

            return false;
        }

        [HarmonyPatch("SwitchScreenButton")]
        [HarmonyPrefix]
        private static bool PreventScreenTurnOn()
        {
            if (IsRadarOffline())
            {
                return false;
            }
            return true;
        }

        [HarmonyPatch("SwitchRadarTargetClientRpc")]
        [HarmonyPrefix]
        private static bool PreventRadarSwitch()
        {
            if (IsRadarOffline())
            {
                return false;
            }
            return true;
        }
    }
}
