using LethalNetworkAPI;
using ShipCommander.Systems;
using UnityEngine;

namespace ShipCommander.Networking
{
    public static class ShipCommanderNetwork
    {
        // Define unified network messages
        public static LNetworkMessage<int> RepairMessage = LNetworkMessage<int>.Connect("SC_RepairMessage");
        public static LNetworkMessage<string> BreakdownMessage = LNetworkMessage<string>.Connect("SC_BreakdownMessage");
        public static LNetworkMessage<bool> ShockerMessage = LNetworkMessage<bool>.Connect("SC_ShockerMessage");
        public static LNetworkMessage<int> BreakerSwitchMessage = LNetworkMessage<int>.Connect("SC_BreakerSwitchMessage");
        public static LNetworkMessage<bool> TerminalBootedMessage = LNetworkMessage<bool>.Connect("SC_TerminalBootedMessage");

        private static System.Collections.Generic.Dictionary<ulong, float> _lastBreakdownTime = new System.Collections.Generic.Dictionary<ulong, float>();

        public static void Initialize()
        {
            // Register Server Handlers (When a client asks the server to do something)
            RepairMessage.OnServerReceived += ServerHandleRepair;
            BreakdownMessage.OnServerReceived += ServerHandleBreakdown;
            ShockerMessage.OnServerReceived += ServerHandleShocker;
            BreakerSwitchMessage.OnServerReceived += ServerHandleBreakerSwitch;
            TerminalBootedMessage.OnServerReceived += ServerHandleTerminalBooted;

            // Register Client Handlers (When the server tells clients to do something)
            RepairMessage.OnClientReceived += ClientHandleRepair;
            BreakdownMessage.OnClientReceived += ClientHandleBreakdown;
            ShockerMessage.OnClientReceived += ClientHandleShocker;
            BreakerSwitchMessage.OnClientReceived += ClientHandleBreakerSwitch;
            TerminalBootedMessage.OnClientReceived += ClientHandleTerminalBooted;

            Plugin.Logger.LogInfo("ShipCommanderNetwork initialized with LNetworkMessage API.");
        }

        private static void ServerHandleRepair(int eventTypeInt, ulong clientId)
        {
            Plugin.Logger.LogInfo($"Server received repair request for {(ShipEventType)eventTypeInt}");
            // Apply locally on server/host first
            Plugin.Instance?.EventSystem?.RepairSystem((ShipEventType)eventTypeInt);
            // Broadcast to all clients
            RepairMessage.SendClients(eventTypeInt);
        }

        private static void ServerHandleBreakdown(string message, ulong clientId)
        {
            Plugin.Logger.LogInfo($"Server received breakdown request from client {clientId}: {message}");

            // Server authority validation
            if (StartOfRound.Instance == null || !StartOfRound.Instance.shipHasLanded)
            {
                Plugin.Logger.LogWarning($"Rejected breakdown request from client {clientId}: Game not active (ship not landed).");
                return;
            }

            if (!Config.ShipConfig.EnableEvents.Value)
            {
                Plugin.Logger.LogWarning($"Rejected breakdown request from client {clientId}: Events are disabled in config.");
                return;
            }

            // Rate limit check: 5 seconds cooldown per client
            if (_lastBreakdownTime.TryGetValue(clientId, out var lastTime))
            {
                if (Time.time - lastTime < 5.0f)
                {
                    Plugin.Logger.LogWarning($"Rejected breakdown request from client {clientId}: Rate limit exceeded (cooldown active).");
                    return;
                }
            }
            _lastBreakdownTime[clientId] = Time.time;
            
            // The client sent the event type string (e.g. "RadarInterference")
            if (System.Enum.TryParse<ShipEventType>(message, out var eventType))
            {
                string code = null;
                if (eventType == ShipEventType.RadarInterference)
                {
                    code = Plugin.Instance?.EventSystem?.GenerateCalibrationCode();
                }

                // Apply locally on server/host
                Plugin.Instance?.EventSystem?.TriggerEvent(eventType, code);

                // Broadcast to all clients
                string broadcastMsg = eventType.ToString();
                if (!string.IsNullOrEmpty(code)) broadcastMsg += ":" + code;

                BreakdownMessage.SendClients(broadcastMsg);
            }
        }

        private static void ClientHandleRepair(int eventTypeInt)
        {
            ShipEventType eventType = (ShipEventType)eventTypeInt;
            Plugin.Logger.LogInfo($"Client applying repair for {eventType} from server");
            Plugin.Instance?.EventSystem?.RepairSystem(eventType);
        }

        private static void ClientHandleBreakdown(string message)
        {
            Plugin.Logger.LogInfo($"Client applying breakdown from server: {message}");
            
            string[] parts = message.Split(':');
            if (System.Enum.TryParse<ShipEventType>(parts[0], out var eventType))
            {
                string code = parts.Length > 1 ? parts[1] : null;
                Plugin.Instance?.EventSystem?.TriggerEvent(eventType, code);
            }
        }

        private static void ServerHandleShocker(bool dummy, ulong clientId)
        {
            Plugin.Logger.LogInfo($"Server received Shocker request from client {clientId}");
            if (StartOfRound.Instance == null || !StartOfRound.Instance.shipHasLanded) return;
            
            var events = Plugin.Instance?.EventSystem;
            if (events?.ShipBreakerBoxInstance != null)
            {
                bool allSwitchesOn = true;
                for (int i = 0; i < events.ShipBreakerBoxInstance.isSwitchOn.Length; i++)
                {
                    if (!events.ShipBreakerBoxInstance.isSwitchOn[i])
                    {
                        allSwitchesOn = false;
                        break;
                    }
                }
                if (!allSwitchesOn)
                {
                    Plugin.Logger.LogWarning($"Server rejected shocker request: not all breaker switches are ON.");
                    return;
                }
            }

            Plugin.Instance?.EventSystem?.TriggerShocker();
            ShockerMessage.SendClients(true);
        }

        private static void ClientHandleShocker(bool dummy)
        {
            Plugin.Logger.LogInfo($"Client applying Shocker from server");
            Plugin.Instance?.EventSystem?.TriggerShocker();
        }

        private static void ServerHandleBreakerSwitch(int encoded, ulong clientId)
        {
            int index = encoded / 2;
            bool isOn = (encoded % 2) == 1;
            Plugin.Logger.LogInfo($"Server received breaker switch {index} state {isOn} from client {clientId}");
            Plugin.Instance?.EventSystem?.ShipBreakerBoxInstance?.SetSwitchState(index, isOn, true);
            BreakerSwitchMessage.SendClients(encoded);
        }

        private static void ClientHandleBreakerSwitch(int encoded)
        {
            int index = encoded / 2;
            bool isOn = (encoded % 2) == 1;
            Plugin.Logger.LogInfo($"Client received breaker switch {index} state {isOn} from server");
            Plugin.Instance?.EventSystem?.ShipBreakerBoxInstance?.SetSwitchState(index, isOn, true);
        }
        private static void ServerHandleTerminalBooted(bool dummy, ulong clientId)
        {
            Plugin.Logger.LogInfo($"Server received TerminalBooted sync from client {clientId}");
            var events = Plugin.Instance?.EventSystem;
            if (events != null)
            {
                events.NeedsBootSequence = false;
            }
            TerminalBootedMessage.SendClients(true);
        }

        private static void ClientHandleTerminalBooted(bool dummy)
        {
            Plugin.Logger.LogInfo("Client applying TerminalBooted from server");
            var events = Plugin.Instance?.EventSystem;
            if (events != null)
            {
                events.NeedsBootSequence = false;
            }
        }
    }
}
