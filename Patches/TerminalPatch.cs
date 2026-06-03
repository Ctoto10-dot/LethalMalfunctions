using HarmonyLib;
using ShipCommander.Systems;
using ShipCommander.Networking;

namespace ShipCommander.Patches
{
    [HarmonyPatch(typeof(Terminal))]
    public class TerminalPatch
    {
        [HarmonyPatch("ParsePlayerSentence")]
        [HarmonyPrefix]
        private static bool ParsePlayerSentencePrefix(Terminal __instance, ref TerminalNode __result)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem != null)
            {
                if (eventSystem.IsBooting)
                {
                    __instance.currentNode = null;
                    __result = CreateNode(eventSystem.CurrentBootText);
                    return false;
                }
                if (eventSystem.BrokenSystems.Contains(ShipEventType.TerminalGlitch))
                {
                    __instance.currentNode = null;
                    __result = CreateNode(
                        "\n[SYSTEM CRITICAL - FATAL ERROR]\n\n" +
                        "CORRUPT SECTOR DETECTED IN ROOT BUFFER.\n" +
                        "TERMINAL COMMAND INTERPRETER CORRUPTED.\n\n" +
                        "TO RESTORE SYSTEM INTEGRITY:\n" +
                        "  1. GO TO BREAKER BOX ON THE SHIP WALL.\n" +
                        "  2. FLIP TERMINAL SWITCH (SWITCH 5) OFF.\n" +
                        "  3. WAIT 2 SECONDS, THEN FLIP IT BACK ON.\n\n" +
                        "Type any character to refresh diagnostics..."
                    );
                    return false;
                }
            }

            string input = "";
            if (__instance.textAdded > 0 && __instance.textAdded <= __instance.screenText.text.Length)
            {
                input = __instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded);
            }
            else
            {
                string[] lines = __instance.screenText.text.Split('\n');
                input = lines[lines.Length - 1];
            }
            
            input = input.Trim().ToLower();
            string[] words = input.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0) return true;
            string verb = words[0];

            if (verb == "status")
            {
                var events = Plugin.Instance?.EventSystem;
                if (events == null) return true;

                string statusStr = "SHIP SYSTEMS DIAGNOSTICS\n------------------------\n\n";

                statusStr += $"LIGHTS:   {(events.BrokenSystems.Contains(ShipEventType.LightsFailure) ? "[ FAIL ]" : "[ OK ]")}\n";
                statusStr += $"DOORS:    {(events.BrokenSystems.Contains(ShipEventType.DoorsJam) ? "[ FAIL ]" : "[ OK ]")}\n";
                
                bool radarBroken = events.BrokenSystems.Contains(ShipEventType.RadarInterference);
                statusStr += $"RADAR:    {(radarBroken ? "[ FAIL ]" : "[ OK ]")}\n";
                if (radarBroken && events.CalibrationCodes != null && events.CalibrationCodes.Length > 0)
                {
                    statusStr += $"  -> Calibration Codes required: {string.Join(", ", events.CalibrationCodes)}\n";
                }
                
                statusStr += $"COMMS:    {(events.BrokenSystems.Contains(ShipEventType.CommsFailure) ? "[ FAIL ]" : "[ OK ]")}\n";
                statusStr += $"TERMINAL: {(events.BrokenSystems.Contains(ShipEventType.TerminalGlitch) ? "[ FAIL ]" : "[ OK ]")}\n";

                statusStr += "\nUse 'repair [system]' to fix malfunctioning components.\n\n";

                __instance.currentNode = null; // Bypass CONFIRM or DENY lock
                __result = CreateNode(statusStr);
                return false;
            }

            if (verb == "lm" || verb == "malfunctions")
            {
                __instance.currentNode = null;
                __result = CreateNode(
@"
LETHAL MALFUNCTIONS v1.0
========================

Available commands:
> status - View ship systems status
> repair [system] - Repair a broken system
> shocker - Activate defense grid

"
                );
                return false;
            }

            if (verb == "shocker" || verb == "defense")
            {
                var events = Plugin.Instance?.EventSystem;
                if (events == null) return true;

                if (!StartOfRound.Instance.shipHasLanded)
                {
                    __instance.currentNode = null;
                    __result = CreateNode("Error: Defense system can only be used when landed.\n\n");
                    return false;
                }



                if (events.BrokenSystems.Count > 0)
                {
                    __instance.currentNode = null;
                    __result = CreateNode("DEFENSE SYSTEM ERROR: Cannot activate. Fix all broken ship systems first.\n\n");
                    return false;
                }

                if (events.ShipBreakerBoxInstance != null)
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
                        __instance.currentNode = null;
                        __result = CreateNode("DEFENSE SYSTEM ERROR: Cannot activate. All breaker switches must be turned ON.\n\n");
                        return false;
                    }
                }

                if (Unity.Netcode.NetworkManager.Singleton != null && (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
                {
                    events.TriggerShocker();
                    ShipCommanderNetwork.ShockerMessage.SendClients(true);
                }
                else if (Unity.Netcode.NetworkManager.Singleton != null)
                {
                    ShipCommanderNetwork.ShockerMessage.SendServer(true);
                }
                else 
                {
                    events.TriggerShocker();
                }

                __instance.currentNode = null;
                __result = CreateNode("DEFENSE SYSTEM ACTIVATED!\nWARNING: OVERLOADING MAIN POWER CORE...\n\n");
                return false;
            }

            /*
            if (verb == "coords")
            {
                var player = StartOfRound.Instance.localPlayerController;
                if (player != null && StartOfRound.Instance.elevatorTransform != null)
                {
                    UnityEngine.Vector3 localPos = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(player.transform.position);
                    
                    // Send to in-game chat so it's guaranteed to be seen!
                    if (HUDManager.Instance != null)
                    {
                        HUDManager.Instance.AddTextToChatOnServer($"<color=green>COORDS: X={localPos.x:F2} Y={localPos.y:F2} Z={localPos.z:F2}</color>");
                    }

                    __instance.currentNode = null;
                    __result = CreateNode($"YOUR EXACT LOCAL POSITION:\nX: {localPos.x:F3}\nY: {localPos.y:F3}\nZ: {localPos.z:F3}\n\n");
                    return false;
                }
            }

            if (verb == "box")
            {
                var box = Plugin.Instance.EventSystem?.ShipBreakerBoxInstance;
                if (box != null)
                {
                    if (words.Length == 2 && words[1] == "current")
                    {
                        var player = StartOfRound.Instance.localPlayerController;
                        if (player != null && StartOfRound.Instance.elevatorTransform != null)
                        {
                            UnityEngine.Vector3 localPos = StartOfRound.Instance.elevatorTransform.InverseTransformPoint(player.transform.position);
                            // Push the box slightly forward based on where the player is looking
                            box.transform.localPosition = localPos;
                            __instance.currentNode = null;
                            __result = CreateNode($"Box moved to your current position: X={localPos.x:F2}, Y={localPos.y:F2}, Z={localPos.z:F2}\n\n");
                            return false;
                        }
                    }
                    else if (words.Length >= 4)
                    {
                        if (float.TryParse(words[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(words[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                            float.TryParse(words[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float z))
                        {
                            box.transform.localPosition = new UnityEngine.Vector3(x, y, z);
                            __instance.currentNode = null;
                            __result = CreateNode($"Box moved to X={x}, Y={y}, Z={z}\n\n");
                            return false;
                        }
                    }
                    else if (words.Length == 3 && words[1] == "rot")
                    {
                        if (float.TryParse(words[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float rotY))
                        {
                            var euler = box.transform.localRotation.eulerAngles;
                            box.transform.localRotation = UnityEngine.Quaternion.Euler(euler.x, rotY, euler.z);
                            __instance.currentNode = null;
                            __result = CreateNode($"Box rotated to Y={rotY} degrees.\n\n");
                            return false;
                        }
                    }
                }
            }
            */

            if (words.Length < 2) return true; // Other commands need 2 words

            string noun = words[1];

            Plugin.Logger.LogInfo($"[TerminalPatch] Parsed verb: '{verb}', noun: '{noun}'");

            if (verb == "repair" || verb == "event")
            {
                ShipEventType? eventType = null;
                string systemName = noun;

                switch (noun)
                {
                    case "lights": eventType = ShipEventType.LightsFailure; break;
                    case "doors": eventType = ShipEventType.DoorsJam; break;
                    case "comms": eventType = ShipEventType.CommsFailure; break;
                    case "radar": eventType = ShipEventType.RadarInterference; break;
                    case "terminal":
                    case "glitch": eventType = ShipEventType.TerminalGlitch; systemName = "terminal"; break;
                }

                if (eventType.HasValue)
                {
                    var events = Plugin.Instance?.EventSystem;
                    if (events == null)
                    {
                        Plugin.Logger.LogWarning($"[TerminalPatch] ERROR: events is null! Plugin.Instance is null? {Plugin.Instance == null}");
                        return true;
                    }

                    if (verb == "repair")
                    {
                        if (!events.BrokenSystems.Contains(eventType.Value))
                        {
                            __instance.currentNode = null;
                            __result = CreateNode($"System {systemName} is already functioning normally.\n\n");
                            return false;
                        }

                        if (eventType.Value == ShipEventType.RadarInterference)
                        {
                            if (events.ShipBreakerBoxInstance != null && !events.ShipBreakerBoxInstance.isSwitchOn[3])
                            {
                                __instance.currentNode = null;
                                __result = CreateNode("ERROR: RADAR POWER OFFLINE. Flip the Radar breaker switch back ON before calibrating.\n\n");
                                return false;
                            }

                            if (words.Length < 3)
                            {
                                __instance.currentNode = null;
                                __result = CreateNode($"ERROR: MISSING CALIBRATION CODE.\n\n");
                                return false;
                            }
                            
                            string providedCode = words[2].ToUpper();
                            if (!events.TryCalibrationCode(providedCode))
                            {
                                __instance.currentNode = null;
                                __result = CreateNode($"ERROR: INCORRECT CALIBRATION CODE.\n\n");
                                return false;
                            }
                        }

                        if (eventType.Value == ShipEventType.CommsFailure)
                        {
                            __instance.currentNode = null;
                            __result = CreateNode($"MANUAL REPAIR REQUIRED: Antenna on ship roof is unresponsive.\n\n");
                            return false;
                        }

                        if (eventType.Value == ShipEventType.LightsFailure)
                        {
                            __instance.currentNode = null;
                            __result = CreateNode($"MANUAL REPAIR REQUIRED: Breakers tripped. Use the Breaker Box on the ship wall.\n\n");
                            return false;
                        }

                        if (eventType.Value == ShipEventType.TerminalGlitch)
                        {
                            __instance.currentNode = null;
                            __result = CreateNode($"MANUAL REPAIR REQUIRED: Reboot terminal by cycling the Terminal breaker switch (Switch 5) OFF and ON.\n\n");
                            return false;
                        }

                        Plugin.Logger.LogInfo($"[TerminalPatch] Sending repair request for {eventType.Value}");
                        if (Unity.Netcode.NetworkManager.Singleton != null && (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
                        {
                            events.RepairSystem(eventType.Value);
                            ShipCommanderNetwork.RepairMessage.SendClients((int)eventType.Value);
                        }
                        else if (Unity.Netcode.NetworkManager.Singleton != null)
                        {
                            ShipCommanderNetwork.RepairMessage.SendServer((int)eventType.Value);
                        }
                        else 
                        {
                            events.RepairSystem(eventType.Value); // Offline fallback
                        }
                        
                        __instance.currentNode = null;
                        __result = CreateNode($"Repairing {systemName}... Please wait.\n\n");
                        return false;
                    }
                    else if (verb == "event")
                    {
                        if (eventType.Value == ShipEventType.LightsFailure)
                        {
                            var shipLights = StartOfRound.Instance?.shipRoomLights;
                            if (shipLights != null && !shipLights.areLightsOn)
                            {
                                __instance.currentNode = null;
                                __result = CreateNode("[ERROR] Cannot break lights while they are turned OFF.\n\n");
                                return false;
                            }
                        }

                        Plugin.Logger.LogInfo($"[TerminalPatch] Requesting breakdown for {eventType.Value}");
                        
                        if (Unity.Netcode.NetworkManager.Singleton != null && (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
                        {
                            // Server/Host generates and triggers directly
                            string code = (eventType.Value == ShipEventType.RadarInterference) ? events.GenerateCalibrationCode() : null;
                            events.TriggerEvent(eventType.Value, code);
                            
                            string networkMsg = eventType.Value.ToString();
                            if (!string.IsNullOrEmpty(code)) networkMsg += ":" + code;
                            ShipCommanderNetwork.BreakdownMessage.SendClients(networkMsg);
                        }
                        else if (Unity.Netcode.NetworkManager.Singleton != null)
                        {
                            // Client requests the server to break the system
                            ShipCommanderNetwork.BreakdownMessage.SendServer(eventType.Value.ToString());
                        }
                        else
                        {
                            // Offline fallback
                            string code = (eventType.Value == ShipEventType.RadarInterference) ? events.GenerateCalibrationCode() : null;
                            events.TriggerEvent(eventType.Value, code);
                        }
                        
                        __instance.currentNode = null;
                        __result = CreateNode($"[DEBUG] Requesting breakdown for {systemName}...\n\n");
                        return false;
                    }
                }
            }

            return true;
        }

        private static TerminalNode CreateNode(string text)
        {
            TerminalNode node = UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();
            node.displayText = text;
            node.clearPreviousText = true;
            return node;
        }

        [HarmonyPatch("LoadNewNode")]
        [HarmonyPrefix]
        private static bool LoadNewNodePrefix(Terminal __instance, ref TerminalNode node)
        {
            var eventSystem = Plugin.Instance?.EventSystem;
            if (eventSystem != null)
            {
                if (eventSystem.NeedsBootSequence && !eventSystem.IsBooting)
                {
                    var helper = BootHelper.Instance;
                    if (helper == null)
                    {
                        helper = __instance.gameObject.AddComponent<BootHelper>();
                    }
                    if (helper != null)
                    {
                        helper.StartCoroutine(eventSystem.BootTerminalRoutine(__instance));
                    }
                }

                if (eventSystem.IsBooting)
                {
                    node = CreateNode(eventSystem.CurrentBootText);
                }
                else if (eventSystem.BrokenSystems.Contains(ShipEventType.TerminalGlitch))
                {
                    TerminalNode glitchNode = CreateNode(
                        "\n[SYSTEM CRITICAL - FATAL ERROR]\n\n" +
                        "CORRUPT SECTOR DETECTED IN ROOT BUFFER.\n" +
                        "TERMINAL COMMAND INTERPRETER CORRUPTED.\n\n" +
                        "TO RESTORE SYSTEM INTEGRITY:\n" +
                        "  1. GO TO BREAKER BOX ON THE SHIP WALL.\n" +
                        "  2. FLIP TERMINAL SWITCH (SWITCH 5) OFF.\n" +
                        "  3. WAIT 2 SECONDS, THEN FLIP IT BACK ON.\n\n" +
                        "Type any character to refresh diagnostics..."
                    );
                    node = glitchNode;
                }
            }
            return true;
        }
    }
}
