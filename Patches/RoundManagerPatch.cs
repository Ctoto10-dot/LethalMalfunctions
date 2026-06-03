using HarmonyLib;

namespace ShipCommander.Patches
{
    /// <summary>
    /// Patches RoundManager to reset ShipCommander systems when a new level loads.
    /// </summary>
    [HarmonyPatch(typeof(RoundManager))]
    internal class RoundManagerPatch
    {
        /// <summary>
        /// Reset systems when a new level is loaded.
        /// </summary>
        [HarmonyPatch("LoadNewLevel")]
        [HarmonyPostfix]
        static void OnLoadNewLevel()
        {
            Plugin.Logger.LogInfo("New level loading — resetting ShipCommander systems.");

            Plugin.Instance?.EventSystem?.ResetForNewRound();
        }
    }
}
