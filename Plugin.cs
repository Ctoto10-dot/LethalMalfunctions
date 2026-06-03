using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ShipCommander.Config;
using ShipCommander.Systems;
using ShipCommander.Patches;
using UnityEngine;

namespace ShipCommander
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("atomic.terminalapi", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("LethalNetworkAPI", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private static Plugin _instance;
        public static Plugin Instance
        {
            get => _instance;
            private set => _instance = value;
        }
        internal static new ManualLogSource Logger;

        private readonly Harmony _harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        // Core systems
        private ShipEventSystem _eventSystem;
        public ShipEventSystem EventSystem
        {
            get
            {
                if (_eventSystem == null)
                {
                    _eventSystem = new ShipEventSystem();
                }
                return _eventSystem;
            }
        }

        private void Awake()
        {
            Instance = this;
            Logger = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.PLUGIN_NAME);
            Logger.LogInfo("[Plugin] Awake called. Instance set.");

            // Initialize config
            ShipConfig.Init(Config);
            
            // Ensure system is created
            var initEvents = EventSystem;

            // Register networking
            Networking.ShipCommanderNetwork.Initialize();

            // Register terminal commands and HUD
            _harmony.PatchAll(typeof(StartOfRoundPatch));
            _harmony.PatchAll(typeof(RoundManagerPatch));
            _harmony.PatchAll(typeof(TerminalPatch));
            _harmony.PatchAll(typeof(WalkieTalkiePatch));
            _harmony.PatchAll(typeof(HangarShipDoorPatch));
            _harmony.PatchAll(typeof(ManualCameraRendererPatch));
            _harmony.PatchAll(typeof(PlayerControllerBPatch));

            Logger.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded successfully!");
        }

        /// <summary>
        /// Called by StartOfRoundPatch when a new game session begins.
        /// </summary>
        public void InitializeSystems()
        {
            // Reset systems for new round
            EventSystem.ResetForNewRound();

            Logger.LogInfo("All systems initialized for round.");
        }

        /// <summary>
        /// Called when leaving a moon or ending the game.
        /// </summary>
        public void ShutdownSystems()
        {
            Logger.LogInfo("Shutting down ShipCommander systems...");

            EventSystem?.StopAllEvents();
        }

        // Removed OnDestroy to prevent erroneous singleton clearance

        /// <summary>
        /// Checks if the local player is currently on the ship.
        /// </summary>
        public static bool IsLocalPlayerOnShip()
        {
            var localPlayer = StartOfRound.Instance?.localPlayerController;
            if (localPlayer == null) return false;
            return localPlayer.isInHangarShipRoom && !localPlayer.isPlayerDead;
        }

    }
}
