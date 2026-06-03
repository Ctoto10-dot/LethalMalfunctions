using System.Text;
using ShipCommander.Systems;
using TerminalApi;
using TerminalApi.Classes;

namespace ShipCommander.Commands
{
    public static class TerminalCommands
    {
        public static void RegisterCommands()
        {
            // Help Command
            TerminalApi.TerminalApi.AddCommand("sc", new CommandInfo()
            {
                DisplayTextSupplier = () =>
                {
                    return "=== ShipCommander ===\n\nAvailable commands:\n> status - View ship systems status\n> repair [system] - Repair a broken system\n\n";
                },
                Category = "Other",
                Description = "ShipCommander help menu."
            });

            // Status Command
            TerminalApi.TerminalApi.AddCommand("status", new CommandInfo()
            {
                DisplayTextSupplier = () =>
                {
                    var events = Plugin.Instance?.EventSystem;
                    if (events == null) return "Systems not initialized.\n\n";

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("=== SHIP SYSTEMS STATUS ===");
                    
                    sb.AppendLine(events.BrokenSystems.Contains(ShipEventType.LightsFailure) ? "  Lights: [BROKEN]" : "  Lights: [OK]");
                    sb.AppendLine(events.BrokenSystems.Contains(ShipEventType.DoorsJam) ? "  Doors: [BROKEN]" : "  Doors: [OK]");
                    sb.AppendLine(events.BrokenSystems.Contains(ShipEventType.CommsFailure) ? "  Comms: [BROKEN]" : "  Comms: [OK]");
                    
                    if (events.BrokenSystems.Contains(ShipEventType.RadarInterference))
                    {
                        string codes = events.CalibrationCodes != null ? string.Join(", ", events.CalibrationCodes) : "N/A";
                        sb.AppendLine($"  Radar: [BROKEN] - CALIBRATION REQUIRED: {codes}");
                    }
                    else
                    {
                        sb.AppendLine("  Radar: [OK]");
                    }
                    
                    sb.AppendLine("\nTo repair a system, type 'repair [system]'.\nTo calibrate radar, type the code.");
                    return sb.ToString();
                },
                Category = "Other",
                Description = "Check ShipCommander systems status."
            });

            // Mult-word commands (repair and event) are now handled by TerminalPatch
            // to avoid TerminalKeyword duplication errors in TerminalApi.
            
            Plugin.Logger.LogInfo("Terminal commands registered via TerminalApi and TerminalPatch!");
        }
    }
}
