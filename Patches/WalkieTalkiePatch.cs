using HarmonyLib;
using ShipCommander.Systems;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(WalkieTalkie))]
    public class WalkieTalkiePatch
    {
        [HarmonyPatch("ItemActivate")]
        [HarmonyPrefix]
        private static bool PreventWalkieTalkieUse(WalkieTalkie __instance)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem == null) return true;

            bool isCommsSwitchOn = eventSystem.ShipBreakerBoxInstance == null || eventSystem.ShipBreakerBoxInstance.isSwitchOn[2];

            if (eventSystem.BrokenSystems.Contains(ShipEventType.CommsFailure) || !isCommsSwitchOn)
            {
                if (__instance.playerHeldBy != null)
                {
                    HUDManager.Instance.DisplayTip("COMMS OFFLINE", "Walkie-Talkie is not receiving signal!");
                }
                return false; // Block execution
            }
            return true;
        }
    }
}
