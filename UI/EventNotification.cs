using ShipCommander.Systems;

namespace ShipCommander.UI
{
    public static class EventNotification
    {
        public static void ShowEventAlert(ShipEventType eventType)
        {
            string title;
            string body;

            switch (eventType)
            {
                case ShipEventType.RadarInterference:
                    title = "RADAR INTERFERENCE";
                    body = "Signal lost. Screen offline.";
                    break;

                case ShipEventType.LightsFailure:
                    title = "LIGHTS FAILURE";
                    body = "Cabin lights short-circuited!";
                    break;

                case ShipEventType.DoorsJam:
                    title = "DOORS JAMMED";
                    body = "Hydraulics failed. Doors locked.";
                    break;

                case ShipEventType.CommsFailure:
                    title = "COMMS FAILURE";
                    body = "Transmitter offline. Signal blocked.";
                    break;

                case ShipEventType.TerminalGlitch:
                    title = "TERMINAL GLITCH";
                    body = "Buffer overflow. Command lockout.";
                    break;

                default:
                    title = "SYSTEM EVENT";
                    body = "An unknown event occurred.";
                    break;
            }

            try
            {
                Plugin.Instance?.EventSystem?.QueueGlitchMessage($"{title}\n{body}");
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"[EventNotification] Error queuing glitch message: {ex}");
            }

            Plugin.Logger.LogInfo($"[EventNotification] {title}: {body}");
        }
    }
}
