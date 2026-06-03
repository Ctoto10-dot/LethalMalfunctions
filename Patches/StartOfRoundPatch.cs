using HarmonyLib;

namespace ShipCommander.Patches
{
    /// <summary>
    /// Patches StartOfRound to initialize and shutdown ShipCommander systems
    /// at the appropriate lifecycle points.
    /// </summary>
    [HarmonyPatch(typeof(StartOfRound))]
    internal class StartOfRoundPatch
    {
        /// <summary>
        /// When the round starts, initialize all ShipCommander systems.
        /// </summary>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void OnStartOfRound()
        {
            Plugin.Logger.LogInfo("StartOfRound detected — initializing ShipCommander.");
            Plugin.Instance?.InitializeSystems();
        }

        /// <summary>
        /// When the ship lands on a moon, activate the event system.
        /// </summary>
        [HarmonyPatch("ShipHasLeft")]
        [HarmonyPostfix]
        static void OnShipLeft()
        {
            Plugin.Logger.LogInfo("Ship has left — shutting down event system.");
            Plugin.Instance?.ShutdownSystems();
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
    }
}
