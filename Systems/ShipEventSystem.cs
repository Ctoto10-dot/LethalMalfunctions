using System.Collections;
using System.Collections.Generic;
using ShipCommander.Config;
using ShipCommander.UI;
using UnityEngine;
using GameNetcodeStuff;

namespace ShipCommander.Systems
{
    public enum ShipEventType
    {
        RadarInterference,
        LightsFailure,
        DoorsJam,
        CommsFailure,
        TerminalGlitch
    }

    public class ShipEventSystem
    {
        private bool _eventsActive = false;
        private Coroutine _eventLoopCoroutine;

        private void DumpObject(GameObject obj, int depth)
        {
            if (obj == null) return;
            string indent = new string(' ', depth * 2);
            string components = "";
            var allComps = obj.GetComponents<Component>();
            if (allComps != null)
            {
                foreach (var comp in allComps)
                {
                    if (comp != null) components += comp.GetType().Name + ", ";
                }
            }
            Plugin.Logger.LogInfo($"{indent}[DUMP] {obj.name} | localPos: {obj.transform.localPosition} | comps: {components}");
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                DumpObject(obj.transform.GetChild(i).gameObject, depth + 1);
            }
        }

        // Track all currently broken systems
        public HashSet<ShipEventType> BrokenSystems { get; private set; }
        public bool ShockerUsedToday { get; private set; }
        public bool ShockerOverloadActive { get; private set; }
        public bool NeedsBootSequence { get; set; } = false;
        public bool IsBooting { get; private set; } = false;
        public string CurrentBootText { get; set; } = "";

        public InteractTrigger CommsAntennaTrigger { get; private set; }
        public GameObject CommsAntennaObject { get; private set; }
        public GameObject CommsAntennaVisualCube { get; private set; }
        public AudioSource RepairLoopAudioSource { get; private set; }
        public AudioSource ElectricityAudioSource { get; private set; }
        public static AudioClip RepairLoopClip { get; private set; }
        public static AudioClip RepairCompleteClip { get; private set; }
        public static AudioClip ElectricityClip { get; private set; }
        private static bool _audioLoadingStarted = false;
        private UnityEngine.Coroutine _blinkCoroutine;
        private UnityEngine.Coroutine _sparkCoroutine;

        private static Queue<string> _glitchMessageQueue = new Queue<string>();
        private static Coroutine _glitchCoroutine;

        private static readonly Vector2[] GlitchPositions = new Vector2[]
        {
            new Vector2(-220f, 110f),
            new Vector2(220f, 110f),
            new Vector2(-240f, 0f),
            new Vector2(240f, 0f),
            new Vector2(-180f, -110f),
            new Vector2(180f, -110f)
        };

        private readonly Dictionary<ShipEventType, IShipEvent> _events;

        public ShipEventSystem()
        {
            BrokenSystems = new HashSet<ShipEventType>();
            _events = new Dictionary<ShipEventType, IShipEvent>
            {
                { ShipEventType.RadarInterference, new RadarInterferenceEvent(this) },
                { ShipEventType.LightsFailure, new LightsFailureEvent(this) },
                { ShipEventType.DoorsJam, new DoorsJamEvent(this) },
                { ShipEventType.CommsFailure, new CommsFailureEvent(this) },
                { ShipEventType.TerminalGlitch, new TerminalGlitchEvent(this) }
            };
        }

        public void SetupCommsAntenna()
        {
            if (CommsAntennaTrigger != null || GameObject.Find("CommsAntennaRepair") != null) return;
            LoadRepairAudioClip();

            GameObject lightSwitchObj = GameObject.Find("LightSwitchContainer");
            if (lightSwitchObj != null && StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
            {
                Transform shipTransform = StartOfRound.Instance.elevatorTransform;
                GameObject antennaObj = UnityEngine.Object.Instantiate(lightSwitchObj, shipTransform);
                antennaObj.name = "CommsAntennaRepair";
                
                // Destroy interfering scripts that reset the position (like AutoParentToShip)
                var autoParent = antennaObj.GetComponent<AutoParentToShip>();
                if (autoParent != null) UnityEngine.Object.Destroy(autoParent);
                
                var netObj = antennaObj.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null) UnityEngine.Object.Destroy(netObj);

                // Place the antenna object on the roof in local coordinates centered on the rotating radar dish
                // X = -0.6f, Y = 7.0f (raised floating level), Z = -6.6f
                antennaObj.transform.localPosition = new Vector3(-0.6f, 7.0f, -6.6f);
                Plugin.Logger.LogInfo($"[ShipCommander] Antenna Obj localPosition set next to radar: {antennaObj.transform.localPosition}");
                
                // Create a highly visible white cube
                GameObject visualCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visualCube.transform.SetParent(antennaObj.transform, false);
                visualCube.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f); // 0.6 meters
                UnityEngine.Object.Destroy(visualCube.GetComponent<Collider>()); // Prevent physics issues
                CommsAntennaVisualCube = visualCube;
                
                var cubeRenderer = visualCube.GetComponent<Renderer>();
                if (cubeRenderer != null)
                {
                    cubeRenderer.material.color = Color.red;
                }

                // Hide the original light switch meshes so only our white cube is visible
                var renderers = antennaObj.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (r.gameObject != visualCube)
                    {
                        r.enabled = false;
                    }
                }

                // Setup the looping audio source for the hammer repair sound
                RepairLoopAudioSource = antennaObj.AddComponent<AudioSource>();
                RepairLoopAudioSource.loop = true;
                RepairLoopAudioSource.playOnAwake = false;
                RepairLoopAudioSource.spatialBlend = 1.0f; // 3D sound
                RepairLoopAudioSource.maxDistance = 15f;
                RepairLoopAudioSource.volume = 0.8f;

                // Setup the looping audio source for the passive electricity sound
                ElectricityAudioSource = antennaObj.AddComponent<AudioSource>();
                ElectricityAudioSource.loop = true;
                ElectricityAudioSource.playOnAwake = false;
                ElectricityAudioSource.spatialBlend = 1.0f; // 3D sound
                ElectricityAudioSource.maxDistance = 15f;
                ElectricityAudioSource.volume = 0.6f; // Moderate volume
                if (ElectricityClip != null)
                {
                    ElectricityAudioSource.clip = ElectricityClip;
                    ElectricityAudioSource.Play();
                }

                InteractTrigger trigger = antennaObj.GetComponentInChildren<InteractTrigger>();
                if (trigger != null)
                {
                    trigger.hoverTip = "Repair Antenna";
                    trigger.disabledHoverTip = "";
                    trigger.interactable = false;
                    trigger.timeToHold = 3.0f;
                    trigger.holdInteraction = true;

                    // Bind starting of hold interaction
                    if (trigger.onInteractEarly == null) trigger.onInteractEarly = new InteractEvent();
                    trigger.onInteractEarly.AddListener((player) => {
                        if (RepairLoopClip != null && RepairLoopAudioSource != null && BrokenSystems.Contains(ShipEventType.CommsFailure))
                        {
                            RepairLoopAudioSource.clip = RepairLoopClip;
                            RepairLoopAudioSource.Play();
                            Plugin.Logger.LogInfo("[ShipCommander] Started playing hammer knock audio.");
                        }
                    });

                    // Bind letting go / interruption of hold interaction
                    if (trigger.onStopInteract == null) trigger.onStopInteract = new InteractEvent();
                    trigger.onStopInteract.AddListener((player) => {
                        if (RepairLoopAudioSource != null)
                        {
                            RepairLoopAudioSource.Stop();
                            Plugin.Logger.LogInfo("[ShipCommander] Stopped playing hammer knock audio.");
                        }
                    });

                    trigger.onInteract = new InteractEvent();
                    
                    trigger.onInteract.AddListener((player) => {
                        if (BrokenSystems.Contains(ShipEventType.CommsFailure))
                        {
                            bool isCommsSwitchOn = ShipBreakerBoxInstance == null || ShipBreakerBoxInstance.isSwitchOn[2];
                            if (isCommsSwitchOn)
                            {
                                // Shock the player!
                                if (player != null)
                                {
                                    player.DamagePlayer(25, true, true, CauseOfDeath.Electrocution);
                                }
                                if (ElectricityAudioSource != null && ShipEventSystem.ElectricityClip != null)
                                {
                                    ElectricityAudioSource.PlayOneShot(ShipEventSystem.ElectricityClip, 1.2f);
                                }
                                if (RepairLoopAudioSource != null)
                                {
                                    RepairLoopAudioSource.Stop();
                                }
                                if (HUDManager.Instance != null)
                                {
                                    HUDManager.Instance.DisplayTip("ELECTROCUTION WARNING", "Turn OFF the Comms breaker switch before repairing!", true);
                                }
                                // Re-enable interaction trigger so they can try again
                                trigger.interactable = true;
                                return;
                            }

                            Plugin.Logger.LogInfo("Antenna manually repaired by player!");
                            
                            // Immediately disable trigger and stop audio loop locally to prevent double interactions during sync delay
                            trigger.interactable = false;
                            if (RepairLoopAudioSource != null)
                            {
                                RepairLoopAudioSource.Stop();
                            }
                            
                            if (Unity.Netcode.NetworkManager.Singleton != null && (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost))
                            {
                                RepairSystem(ShipEventType.CommsFailure);
                                Networking.ShipCommanderNetwork.RepairMessage.SendClients((int)ShipEventType.CommsFailure);
                            }
                            else if (Unity.Netcode.NetworkManager.Singleton != null)
                            {
                                Networking.ShipCommanderNetwork.RepairMessage.SendServer((int)ShipEventType.CommsFailure);
                            }
                            else 
                            {
                                RepairSystem(ShipEventType.CommsFailure);
                            }
                        }
                    });

                    // Make the interaction collider larger (3.5m x 3.5m x 3.5m) so it can be reached easily around the radar
                    BoxCollider collider = antennaObj.GetComponentInChildren<BoxCollider>();
                    if (collider != null)
                    {
                        collider.size = new Vector3(3.5f, 3.5f, 3.5f);
                        collider.center = Vector3.zero;
                    }

                    CommsAntennaTrigger = trigger;
                    CommsAntennaObject = antennaObj;
                    CommsAntennaObject.SetActive(false);
                    Plugin.Logger.LogInfo("Comms Antenna trigger setup successfully.");
                }
            }
        }

        public ShipBreakerBox ShipBreakerBoxInstance { get; private set; }

        public void SetupBreakerBox()
        {
            if (ShipBreakerBoxInstance != null) return;
            if (StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StartCoroutine(TrySetupBreakerBoxCoroutine());
            }
        }

        private IEnumerator TrySetupBreakerBoxCoroutine()
        {
            BreakerBox originalBox = null;
            while (originalBox == null)
            {
                BreakerBox[] originalBoxes = Resources.FindObjectsOfTypeAll<BreakerBox>();
                if (originalBoxes != null && originalBoxes.Length > 0)
                {
                    originalBox = originalBoxes[0];
                }
                else
                {
                    yield return new WaitForSeconds(2f);
                }
            }

            if (ShipBreakerBoxInstance != null) yield break; // Already setup

            if (StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
            {
                Transform shipTransform = StartOfRound.Instance.elevatorTransform;
                GameObject boxObj = UnityEngine.Object.Instantiate(originalBox.gameObject, shipTransform);
                boxObj.name = "ShipBreakerBox";

                // Position it on the wall inside the ship (near the terminal or door)
                boxObj.transform.localPosition = new Vector3(5.75f, 2.7f, -3.35f);
                // Turn it 180 degrees as requested by the user
                boxObj.transform.localRotation = Quaternion.Euler(originalBox.transform.localRotation.eulerAngles.x, 180f, originalBox.transform.localRotation.eulerAngles.z);

                // Strip the original BreakerBox script so it doesn't mess with facility lighting
                var oldScript = boxObj.GetComponent<BreakerBox>();
                if (oldScript != null) UnityEngine.Object.Destroy(oldScript);

                // Silence any default looping ambient sounds on the breaker box prefab
                AudioSource[] boxAudios = boxObj.GetComponentsInChildren<AudioSource>(true);
                foreach (var src in boxAudios)
                {
                    if (src != null && src.loop)
                    {
                        src.Stop();
                        src.loop = false;
                        src.playOnAwake = false;
                    }
                }

                var netObj = boxObj.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null) UnityEngine.Object.Destroy(netObj);

                // Find the switch triggers but EXCLUDE the door/panel trigger
                var allTriggers = boxObj.GetComponentsInChildren<InteractTrigger>();
                var validSwitches = new System.Collections.Generic.List<InteractTrigger>();
                foreach (var t in allTriggers)
                {
                    string tName = t.gameObject.name.ToLower();
                    if (tName.Contains("door") || tName.Contains("panel") || t.hoverTip.ToLower().Contains("open") || t.hoverTip.ToLower().Contains("close"))
                    {
                        // Leave the door trigger intact so players can open the box!
                        continue;
                    }
                    validSwitches.Add(t);
                }

                // Sort the valid switches alphabetically by their GameObject name so they correspond exactly to BreakerSwitch1 (0), BreakerSwitch2 (1), etc.
                validSwitches.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name, System.StringComparison.Ordinal));

                // Add our custom script
                ShipBreakerBoxInstance = boxObj.AddComponent<ShipBreakerBox>();
                ShipBreakerBoxInstance.Initialize(this, validSwitches.ToArray());

                Plugin.Logger.LogInfo("[ShipCommander] Ship Breaker Box spawned and setup successfully.");
            }
        }

        // Radar calibration state
        public string[] CalibrationCodes { get; private set; }
        public int CalibrationProgress { get; private set; }
        public bool NeedsCalibration { get; private set; }

        public void StartBlinking()
        {
            StopBlinking();
            if (StartOfRound.Instance != null && CommsAntennaVisualCube != null)
            {
                _blinkCoroutine = StartOfRound.Instance.StartCoroutine(BlinkAntennaCube());
            }
        }

        public void StopBlinking()
        {
            if (_blinkCoroutine != null && StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StopCoroutine(_blinkCoroutine);
                _blinkCoroutine = null;
            }
        }

        private IEnumerator BlinkAntennaCube()
        {
            if (CommsAntennaVisualCube == null) yield break;
            var cubeRenderer = CommsAntennaVisualCube.GetComponent<Renderer>();
            if (cubeRenderer == null) yield break;

            var normalColor = Color.red;
            var blinkColor = new Color(1f, 0.5f, 0f); // Orange

            while (BrokenSystems.Contains(ShipEventType.CommsFailure))
            {
                cubeRenderer.material.color = blinkColor;
                yield return new WaitForSeconds(0.5f);
                cubeRenderer.material.color = normalColor;
                yield return new WaitForSeconds(0.5f);
            }
            
            cubeRenderer.material.color = normalColor;
        }

        public void StartSparking()
        {
            StopSparking();
            if (StartOfRound.Instance != null && CommsAntennaObject != null)
            {
                _sparkCoroutine = StartOfRound.Instance.StartCoroutine(SparkLoop());
            }
        }

        public void StopSparking()
        {
            if (_sparkCoroutine != null && StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StopCoroutine(_sparkCoroutine);
                _sparkCoroutine = null;
            }
        }

        private IEnumerator SparkLoop()
        {
            while (BrokenSystems.Contains(ShipEventType.CommsFailure))
            {
                float delay = UnityEngine.Random.Range(0.6f, 1.5f);
                yield return new WaitForSeconds(delay);

                if (CommsAntennaObject != null && CommsAntennaObject.activeSelf)
                {
                    SpawnSparks(CommsAntennaObject.transform.position, 10);
                }
            }
        }

        public void SpawnSparks(Vector3 position, int count)
        {
            try
            {
                GameObject sparkObj = new GameObject("AntennaSparks");
                sparkObj.transform.position = position;
                
                ParticleSystem ps = sparkObj.AddComponent<ParticleSystem>();
                
                var main = ps.main;
                // Bright yellow/orange spark colors
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.2f, 1f), new Color(1f, 0.6f, 0f, 1f));
                main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f); // Tiny, sharp sparks (1.5 to 5 cm)
                main.startSpeed = new ParticleSystem.MinMaxCurve(3.0f, 7.0f); // High-speed eject
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                main.gravityModifier = 0.4f; // Gentle downward gravity arc
                main.duration = 0.5f;
                main.loop = false;
                
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
                
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.1f;
                
                ParticleSystemRenderer psr = sparkObj.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.renderMode = ParticleSystemRenderMode.Mesh;
                    
                    // Create a temporary cube to extract its mesh
                    GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
                    UnityEngine.Object.Destroy(tempCube);
                    
                    psr.mesh = cubeMesh;

                    // Find a working material on the ship and make a glowing yellow variant
                    Material baseMaterial = null;
                    GameObject buttonPanel = GameObject.Find("HangarDoorButtonPanel");
                    if (buttonPanel != null)
                    {
                        var meshRenderers = buttonPanel.GetComponentsInChildren<MeshRenderer>();
                        foreach (var mr in meshRenderers)
                        {
                            if (mr.sharedMaterial != null && (mr.gameObject.name.Contains("red") || mr.gameObject.name.Contains("button") || mr.gameObject.name.Contains("Button")))
                            {
                                baseMaterial = mr.sharedMaterial;
                                break;
                            }
                        }
                    }

                    if (baseMaterial == null)
                    {
                        GameObject lightSwitchObj = GameObject.Find("LightSwitchContainer");
                        if (lightSwitchObj != null)
                        {
                            var r = lightSwitchObj.GetComponentInChildren<Renderer>();
                            if (r != null)
                            {
                                baseMaterial = r.sharedMaterial;
                            }
                        }
                    }

                    if (baseMaterial != null)
                    {
                        // Create a glowing yellow variant of the material
                        Material yellowMat = new Material(baseMaterial);
                        Color yellowColor = new Color(1f, 0.9f, 0.1f, 1f);
                        if (yellowMat.HasProperty("_BaseColor")) yellowMat.SetColor("_BaseColor", yellowColor);
                        if (yellowMat.HasProperty("_Color")) yellowMat.SetColor("_Color", yellowColor);
                        if (yellowMat.HasProperty("_EmissiveColor")) yellowMat.SetColor("_EmissiveColor", yellowColor * 3.0f); // Make it glow bright yellow
                        if (yellowMat.HasProperty("_EmissionColor")) yellowMat.SetColor("_EmissionColor", yellowColor * 3.0f);
                        yellowMat.EnableKeyword("_EMISSION");
                        psr.sharedMaterial = yellowMat;
                    }
                }

                ps.Play();
                UnityEngine.Object.Destroy(sparkObj, 1.0f);
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogError($"Error spawning sparks: {ex}");
            }
        }

        public IEnumerator DelayedAntennaDisable()
        {
            StopBlinking();
            StopSparking();

            // Stop the loop repair sound
            if (RepairLoopAudioSource != null)
            {
                RepairLoopAudioSource.Stop();
            }

            // Stop the electricity sound
            if (ElectricityAudioSource != null)
            {
                ElectricityAudioSource.Stop();
            }

            // Spawn a big blast of electrical sparks upon repair completion
            if (CommsAntennaObject != null)
            {
                SpawnSparks(CommsAntennaObject.transform.position, 40);
            }

            if (CommsAntennaVisualCube != null)
            {
                var cubeRenderer = CommsAntennaVisualCube.GetComponent<Renderer>();
                if (cubeRenderer != null)
                {
                    cubeRenderer.material.color = Color.green;
                }
            }

            if (CommsAntennaTrigger != null)
            {
                CommsAntennaTrigger.interactable = false;
            }

            // Play the repair complete audio clip instead of the vanilla light switch click sound
            if (RepairLoopAudioSource != null && RepairCompleteClip != null)
            {
                RepairLoopAudioSource.PlayOneShot(RepairCompleteClip);
                Plugin.Logger.LogInfo("[ShipCommander] Played repair complete audio clip.");
            }

            // Trigger a beautiful HUD notification + sound clip
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.DisplayTip("SYSTEM REPAIRED", "Communications antenna is back online!", false);
            }

            yield return new WaitForSeconds(1.5f);

            if (CommsAntennaObject != null)
            {
                CommsAntennaObject.SetActive(false);
            }
        }

        public void ResetForNewRound()
        {
            if (BrokenSystems == null) BrokenSystems = new HashSet<ShipEventType>();
            
            StopAllEvents();
            StopBlinking();
            StopSparking();
            BrokenSystems.Clear();
            NeedsCalibration = false;
            CalibrationProgress = 0;
            ShockerUsedToday = false;
            _glitchCoroutine = null;
            _glitchMessageQueue.Clear();

            if (CommsAntennaObject != null)
            {
                try { UnityEngine.Object.Destroy(CommsAntennaObject); } catch {}
            }
            CommsAntennaObject = null;
            CommsAntennaTrigger = null;
            CommsAntennaVisualCube = null;

            ItemCharger charger = UnityEngine.Object.FindObjectOfType<ItemCharger>();
            if (charger != null)
            {
                InteractTrigger trigger = charger.GetComponentInChildren<InteractTrigger>();
                if (trigger != null) trigger.interactable = true;
            }
            RepairLoopAudioSource = null;
            ElectricityAudioSource = null;

            SetupBreakerBox();
            if (ShipBreakerBoxInstance != null)
            {
                ShipBreakerBoxInstance.ResetAllSwitches();
            }

            if (ShipConfig.EnableEvents.Value)
            {
                // Only host or server should run the event loop (or offline/singleplayer)
                if (Unity.Netcode.NetworkManager.Singleton == null || Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost)
                {
                    _eventsActive = true;
                    if (StartOfRound.Instance != null)
                    {
                        _eventLoopCoroutine = StartOfRound.Instance.StartCoroutine(EventLoop());
                        Plugin.Logger.LogInfo("Ship event system activated (Host/Server).");
                    }
                }
                else
                {
                    Plugin.Logger.LogInfo("Ship event system standby (Client).");
                }
            }
        }

        public static void LoadRepairAudioClip()
        {
            if (_audioLoadingStarted) return;
            _audioLoadingStarted = true;

            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string pluginDir = System.IO.Path.GetDirectoryName(dllPath);
            string loopPath = System.IO.Path.Combine(pluginDir, "hammer-knock-on-metal.mp3");
            string completePath = System.IO.Path.Combine(pluginDir, "solid-confident-knock-with-a-hammer.mp3");
            string electricityPath = System.IO.Path.Combine(pluginDir, "neiz_esten-elektricheskiy-razryadz_uk.mp3");

            if (StartOfRound.Instance != null)
            {
                if (System.IO.File.Exists(loopPath))
                {
                    StartOfRound.Instance.StartCoroutine(LoadAudioCoroutine(loopPath, 0));
                }
                else
                {
                    Plugin.Logger.LogWarning($"[ShipCommander] Loop repair audio file not found at: {loopPath}");
                }

                if (System.IO.File.Exists(completePath))
                {
                    StartOfRound.Instance.StartCoroutine(LoadAudioCoroutine(completePath, 1));
                }
                else
                {
                    Plugin.Logger.LogWarning($"[ShipCommander] Complete repair audio file not found at: {completePath}");
                }

                if (System.IO.File.Exists(electricityPath))
                {
                    StartOfRound.Instance.StartCoroutine(LoadAudioCoroutine(electricityPath, 2));
                }
                else
                {
                    Plugin.Logger.LogWarning($"[ShipCommander] Electricity audio file not found at: {electricityPath}");
                }
            }
        }

        private static IEnumerator LoadAudioCoroutine(string path, int clipType)
        {
            string url = "file://" + path.Replace("\\", "/");
            Plugin.Logger.LogInfo($"[ShipCommander] Loading audio from: {url}");
            using (UnityEngine.Networking.UnityWebRequest uwr = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                yield return uwr.SendWebRequest();
                if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(uwr);
                    if (clipType == 0)
                    {
                        RepairLoopClip = clip;
                        Plugin.Logger.LogInfo("[ShipCommander] Successfully loaded repair loop audio clip!");
                    }
                    else if (clipType == 1)
                    {
                        RepairCompleteClip = clip;
                        Plugin.Logger.LogInfo("[ShipCommander] Successfully loaded repair complete audio clip!");
                    }
                    else if (clipType == 2)
                    {
                        ElectricityClip = clip;
                        Plugin.Logger.LogInfo("[ShipCommander] Successfully loaded electricity audio clip!");
                        // Play immediately if the event is currently active and the audio source exists
                        var system = Plugin.Instance?.EventSystem;
                        if (system != null && system.ElectricityAudioSource != null && system.BrokenSystems.Contains(ShipEventType.CommsFailure))
                        {
                            system.ElectricityAudioSource.clip = clip;
                            system.ElectricityAudioSource.Play();
                        }
                    }
                }
                else
                {
                    Plugin.Logger.LogError($"[ShipCommander] Failed to load audio clip from {path}: {uwr.error}");
                }
            }
        }

        public void StopAllEvents()
        {
            _eventsActive = false;

            if (_eventLoopCoroutine != null && StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StopCoroutine(_eventLoopCoroutine);
                _eventLoopCoroutine = null;
            }

            BrokenSystems.Clear();
            NeedsCalibration = false;

            if (CommsAntennaTrigger != null)
            {
                CommsAntennaTrigger.interactable = false;
                CommsAntennaObject?.SetActive(false);
            }
        }

        private IEnumerator EventLoop()
        {
            float initialDelay = ShipConfig.EventMinInterval.Value;
            yield return new WaitForSeconds(initialDelay);

            while (_eventsActive)
            {
                float minInterval = ShipConfig.EventMinInterval.Value;
                float maxInterval = ShipConfig.EventMaxInterval.Value;
                int eventsToTrigger = 1;

                if (ShipConfig.EnableProgression.Value && StartOfRound.Instance != null)
                {
                    int daysSpent = StartOfRound.Instance.gameStats.daysSpent;
                    
                    // Progression logic: 
                    // Days 1-2: Normal
                    // Days 3-4: 25% faster
                    // Days 5+: 50% faster, 25% chance of 2 events at once
                    if (daysSpent >= 4) // Day 5+ (daysSpent is 0-indexed initially but increments)
                    {
                        minInterval *= 0.5f;
                        maxInterval *= 0.5f;
                        if (UnityEngine.Random.value < 0.25f) eventsToTrigger = 2;
                    }
                    else if (daysSpent >= 2) // Day 3-4
                    {
                        minInterval *= 0.75f;
                        maxInterval *= 0.75f;
                    }

                    // Hard moon multiplier (Rend, Dine, Titan)
                    string planetName = StartOfRound.Instance.currentLevel?.PlanetName?.ToLower() ?? "";
                    if (planetName.Contains("rend") || planetName.Contains("dine") || planetName.Contains("titan"))
                    {
                        minInterval *= ShipConfig.HardMoonMultiplier.Value;
                        maxInterval *= ShipConfig.HardMoonMultiplier.Value;
                    }
                    
                    // Cap minimums so it doesn't get ridiculously fast (min 1 minute, max 2 minutes)
                    minInterval = Mathf.Max(minInterval, 60f); 
                    maxInterval = Mathf.Max(maxInterval, 120f);
                }

                float interval = UnityEngine.Random.Range(minInterval, maxInterval);
                yield return new WaitForSeconds(interval);

                if (StartOfRound.Instance != null && StartOfRound.Instance.shipHasLanded)
                {
                    for (int i = 0; i < eventsToTrigger; i++)
                    {
                        TriggerRandomEvent();
                    }
                }
            }
        }

        private void TriggerRandomEvent()
        {
            ShipEventType[] possibleEvents = {
                ShipEventType.RadarInterference,
                ShipEventType.LightsFailure,
                ShipEventType.DoorsJam,
                ShipEventType.CommsFailure,
                ShipEventType.TerminalGlitch
            };

            // Pick a random system that isn't already broken and is powered ON
            var availableEvents = new List<ShipEventType>();
            foreach (var evt in possibleEvents)
            {
                if (!BrokenSystems.Contains(evt))
                {
                    if (evt == ShipEventType.LightsFailure)
                    {
                        var shipLights = StartOfRound.Instance?.shipRoomLights;
                        if (shipLights != null && !shipLights.areLightsOn) continue; // Skip lights failure event if ship lights are OFF
                        if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[0]) continue; // Skip lights failure event if Switch 0 is OFF
                    }
                    if (evt == ShipEventType.DoorsJam)
                    {
                        if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[1]) continue; // Skip doors jam if Switch 1 is OFF
                    }
                    if (evt == ShipEventType.CommsFailure)
                    {
                        if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[2]) continue; // Skip comms failure if Switch 2 is OFF
                    }
                    if (evt == ShipEventType.RadarInterference)
                    {
                        if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[3]) continue; // Skip radar interference if Switch 3 is OFF
                    }
                    if (evt == ShipEventType.TerminalGlitch)
                    {
                        if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[4]) continue; // Skip terminal glitch if Switch 4 is OFF
                    }
                    availableEvents.Add(evt);
                }
            }

            if (availableEvents.Count == 0) return; // Everything is already broken

            ShipEventType eventType = availableEvents[UnityEngine.Random.Range(0, availableEvents.Count)];
            
            string code = null;
            if (eventType == ShipEventType.RadarInterference)
            {
                code = GenerateCalibrationCode();
            }

            // Trigger the event locally on host
            TriggerEvent(eventType, code);

            // Broadcast the breakdown event to all clients
            if (Unity.Netcode.NetworkManager.Singleton != null)
            {
                string networkMsg = eventType.ToString();
                if (!string.IsNullOrEmpty(code))
                {
                    networkMsg += ":" + code;
                }
                Networking.ShipCommanderNetwork.BreakdownMessage.SendClients(networkMsg);
            }
        }

        public void TriggerEvent(ShipEventType eventType, string radarCode = null, bool suppressStandardAlert = false)
        {
            if (BrokenSystems.Contains(eventType)) return; // Already broken

            // Validate that the system has power before breaking it
            if (eventType == ShipEventType.LightsFailure)
            {
                var shipLights = StartOfRound.Instance?.shipRoomLights;
                if (shipLights != null && !shipLights.areLightsOn) return;
                if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[0]) return;
            }
            if (eventType == ShipEventType.DoorsJam)
            {
                if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[1]) return;
            }
            if (eventType == ShipEventType.CommsFailure)
            {
                if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[2]) return;
            }
            if (eventType == ShipEventType.RadarInterference)
            {
                if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[3]) return;
            }
            if (eventType == ShipEventType.TerminalGlitch)
            {
                if (ShipBreakerBoxInstance != null && !ShipBreakerBoxInstance.isSwitchOn[4]) return;
            }

            BrokenSystems.Add(eventType);
            Plugin.Logger.LogWarning($"SHIP EVENT TRIGGERED: {eventType}!");
            
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            if (terminal != null)
            {
                terminal.screenText.text += "\n\n[WARNING] DETECTED ANOMALY IN SHIP SYSTEMS...\n\n";
            }

            if (_events.TryGetValue(eventType, out var ev))
            {
                ev.Trigger(radarCode);
            }

            // Hook the BreakerBox for specific events
            if (ShipBreakerBoxInstance != null)
            {
                if (eventType == ShipEventType.LightsFailure)
                {
                    ShipBreakerBoxInstance.SetSwitchState(0, false, true);
                }
                else if (eventType == ShipEventType.RadarInterference)
                {
                    ShipBreakerBoxInstance.SetSwitchState(3, false, true);
                }
            }

            if (!suppressStandardAlert)
            {
                EventNotification.ShowEventAlert(eventType);
            }
        }

        public bool TryCalibrationCode(string code)
        {
            if (!NeedsCalibration || CalibrationCodes == null) return false;

            if (CalibrationProgress < CalibrationCodes.Length &&
                code.ToUpper() == CalibrationCodes[CalibrationProgress].ToUpper())
            {
                CalibrationProgress++;
                Plugin.Logger.LogInfo($"Calibration code {CalibrationProgress}/{CalibrationCodes.Length} accepted!");

                if (CalibrationProgress >= CalibrationCodes.Length)
                {
                    NeedsCalibration = false;
                    Plugin.Logger.LogInfo("Radar calibration complete!");
                    return true;
                }
                return true;
            }
            return false;
        }

        public void RepairSystem(ShipEventType type)
        {
            if (!BrokenSystems.Contains(type)) return;
            
            BrokenSystems.Remove(type);
            Plugin.Logger.LogInfo($"{type} repaired successfully.");

            if (_events.TryGetValue(type, out var ev))
            {
                ev.Repair();
            }

            // Explicitly flip the respective breaker switch back ON when a system is repaired
            if (type == ShipEventType.DoorsJam)
            {
                ShipBreakerBoxInstance?.SetSwitchState(1, true, true);
            }
            else if (type == ShipEventType.LightsFailure)
            {
                ShipBreakerBoxInstance?.SetSwitchState(0, true, true);
            }
        }

        public void SetRadarCalibrationState(bool needsCalibration, int progress, string[] codes)
        {
            NeedsCalibration = needsCalibration;
            CalibrationProgress = progress;
            CalibrationCodes = codes;
        }

        public string GenerateCalibrationCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] code = new char[4];
            for (int i = 0; i < 4; i++)
            {
                code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            }
            return new string(code);
        }

        public void TriggerShocker()
        {


            Plugin.Logger.LogWarning("SHOCKER ACTIVATED! Overloading systems...");

            if (ShipBreakerBoxInstance != null)
            {
                for (int i = 0; i < ShipBreakerBoxInstance.isSwitchOn.Length; i++)
                {
                    ShipBreakerBoxInstance.SetSwitchState(i, false, true);
                }
                ShipBreakerBoxInstance.StartRebootOverload(ElectricityClip);
            }

            if (StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
            {
                // Play loud sound (reusing electricity clip for now)
                if (ElectricityAudioSource != null && ElectricityClip != null)
                {
                    ElectricityAudioSource.PlayOneShot(ElectricityClip, 1.5f);
                }

                // Disable item charger temporarily (30 seconds)
                if (StartOfRound.Instance != null)
                {
                    StartOfRound.Instance.StartCoroutine(TemporarilyDisableCharger(30f));
                }

                // Drain all batteries
                GrabbableObject[] allItems = UnityEngine.Object.FindObjectsOfType<GrabbableObject>();
                foreach (var item in allItems)
                {
                    if (item != null && item.itemProperties != null && item.itemProperties.requiresBattery && item.insertedBattery != null)
                    {
                        item.insertedBattery.empty = true;
                        item.insertedBattery.charge = 0f;
                        if (item.isBeingUsed)
                        {
                            item.isBeingUsed = false;
                            try { item.UseItemOnClient(false); } catch {}
                        }
                    }
                }

                // Spawn big sparks around the ship
                SpawnSparks(StartOfRound.Instance.elevatorTransform.position + new Vector3(0, 5f, 0), 100);

                // Stun all nearby enemies
                EnemyAI[] enemies = UnityEngine.Object.FindObjectsOfType<EnemyAI>();
                foreach (var enemy in enemies)
                {
                    if (enemy != null && !enemy.isEnemyDead && Vector3.Distance(enemy.transform.position, StartOfRound.Instance.elevatorTransform.position) < 25f)
                    {
                        Plugin.Logger.LogInfo($"Stunning enemy: {enemy.enemyType.enemyName}");
                        enemy.SetEnemyStunned(true, 15f, null);
                    }
                }

                // Break all ship systems without triggering the small corner alerts
                TriggerEvent(ShipEventType.LightsFailure, null, true);
                TriggerEvent(ShipEventType.DoorsJam, null, true);
                TriggerEvent(ShipEventType.CommsFailure, null, true);
                TriggerEvent(ShipEventType.RadarInterference, GenerateCalibrationCode(), true);

                // Queue glitch messages only for shocker overload
                QueueGlitchMessage("[CRITICAL FAILURE]\nPOWER GRID OFFLINE", true);
                QueueGlitchMessage("[CRITICAL FAILURE]\nDOOR HYDRAULICS JAMMED", true);
                QueueGlitchMessage("[CRITICAL FAILURE]\nCOMMS ANTENNA DESTROYED", true);
                QueueGlitchMessage("[CRITICAL FAILURE]\nRADAR CALIBRATION LOST", true);

                // Lock the engine
                StartOfRound.Instance.StartCoroutine(LockEngineRoutine());

                // Trigger local visual/audio contusion effect if player is near or inside the ship
                if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.localPlayerController != null)
                {
                    float dist = Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, StartOfRound.Instance.elevatorTransform.position);
                    if (dist < 30f || GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
                    {
                        StartOfRound.Instance.StartCoroutine(ScreenFlashRoutine());
                    }
                }
            }
        }

        private IEnumerator ScreenFlashRoutine()
        {
            // Trigger vanilla ear ringing effect
            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.earsRingingTimer = 4f;
            }

            // Create a temporary fullscreen white flash canvas
            GameObject flashObj = new GameObject("ShockerFlashCanvas");
            UnityEngine.Canvas canvas = flashObj.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;
            
            UnityEngine.UI.Image img = flashObj.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1f, 1f, 1f, 1f); // Solid white

            yield return new WaitForSeconds(0.1f);

            // Fade out over 3 seconds
            float duration = 3.0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                img.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }

            UnityEngine.Object.Destroy(flashObj);
        }

        private IEnumerator LockEngineRoutine()
        {
            StartMatchLever lever = UnityEngine.Object.FindObjectOfType<StartMatchLever>();
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();

            InteractTrigger terminalTrigger = null;
            if (terminal != null)
            {
                try { terminal.QuitTerminal(); } catch { Plugin.Logger.LogWarning("Could not call QuitTerminal"); }
                terminalTrigger = terminal.gameObject.GetComponent<InteractTrigger>();
                if (terminalTrigger == null) terminalTrigger = terminal.gameObject.GetComponentInChildren<InteractTrigger>();
            }

            if (lever != null && lever.triggerScript != null)
            {
                lever.triggerScript.interactable = false;
                lever.triggerScript.disabledHoverTip = "ENGINE REBOOTING...";
            }

            if (terminalTrigger != null)
            {
                terminalTrigger.interactable = false;
                terminalTrigger.disabledHoverTip = "REBOOTING...";
            }
                
            QueueGlitchMessage("SYSTEMS REBOOTING\nMain power overloaded. Wait 15 seconds.", true);

            yield return new WaitForSeconds(15f);

            if (lever != null && lever.triggerScript != null)
            {
                lever.triggerScript.interactable = true;
                lever.triggerScript.disabledHoverTip = "";
            }

            if (terminalTrigger != null)
            {
                terminalTrigger.interactable = true;
                terminalTrigger.disabledHoverTip = "";
            }

            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.DisplayTip("SYSTEMS ONLINE", "Reboot successful. Ready for takeoff.", false);
            }
        }

        public void QueueGlitchMessage(string message, bool isShocker = false)
        {
            Plugin.Logger.LogWarning($"[ShipCommander] QueueGlitchMessage called: {message}");
            _glitchMessageQueue.Enqueue(message + (isShocker ? "|shocker" : "|normal"));
            
            if (_glitchCoroutine == null)
            {
                Plugin.Logger.LogWarning($"[ShipCommander] _glitchCoroutine is null. HUDManager.Instance != null: {HUDManager.Instance != null}");
                if (HUDManager.Instance != null)
                {
                    _glitchCoroutine = HUDManager.Instance.StartCoroutine(ProcessGlitchQueue());
                    Plugin.Logger.LogWarning($"[ShipCommander] Started ProcessGlitchQueue coroutine via HUDManager.");
                }
                else if (StartOfRound.Instance != null)
                {
                    _glitchCoroutine = StartOfRound.Instance.StartCoroutine(ProcessGlitchQueue());
                    Plugin.Logger.LogWarning($"[ShipCommander] Started ProcessGlitchQueue coroutine via StartOfRound.");
                }
            }
            else
            {
                Plugin.Logger.LogWarning($"[ShipCommander] _glitchCoroutine is already running.");
            }
        }

        private IEnumerator ProcessGlitchQueue()
        {
            Plugin.Logger.LogWarning("[ShipCommander] Processing Glitch UI Queue START...");
            UnityEngine.Transform uiParent = null;
            GameObject customCanvasObj = null;

            if (HUDManager.Instance != null && HUDManager.Instance.HUDContainer != null)
            {
                uiParent = HUDManager.Instance.HUDContainer.transform.parent;
            }
            else
            {
                Plugin.Logger.LogWarning("HUDContainer not found, creating temporary canvas for glitch warnings.");
                customCanvasObj = new GameObject("GlitchCanvas");
                UnityEngine.Canvas canvas = customCanvasObj.AddComponent<UnityEngine.Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10000;
                uiParent = customCanvasObj.transform;
            }

            // Create a randomized copy of positions to avoid overlapping for shocker messages
            List<Vector2> availablePositions = new List<Vector2>(GlitchPositions);
            for (int i = 0; i < availablePositions.Count; i++)
            {
                int tempIndex = UnityEngine.Random.Range(i, availablePositions.Count);
                Vector2 temp = availablePositions[i];
                availablePositions[i] = availablePositions[tempIndex];
                availablePositions[tempIndex] = temp;
            }

            int posIndex = 0;

            while (_glitchMessageQueue.Count > 0)
            {
                string rawMsg = _glitchMessageQueue.Dequeue();
                bool isShockerMsg = rawMsg.EndsWith("|shocker");
                string msg = rawMsg.Substring(0, rawMsg.Length - (isShockerMsg ? 8 : 7));

                string[] parts = msg.Split('\n');
                string titleText = parts[0];
                string bodyText = parts.Length > 1 ? parts[1] : "";

                Vector2 screenPos;
                if (isShockerMsg)
                {
                    screenPos = availablePositions[posIndex % availablePositions.Count];
                    posIndex++;
                }
                else
                {
                    // Bottom-left positioning for normal breakdowns
                    screenPos = new Vector2(-270f, -190f);
                }

                try
                {
                    if (!isShockerMsg && HUDManager.Instance != null)
                    {
                        HUDManager.Instance.DisplayTip(titleText, bodyText, true);
                    }
                    else
                    {
                        CreateCustomGlitchNotification(titleText, bodyText, uiParent, screenPos, !isShockerMsg);
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogError($"Error creating glitch warning: {ex}");
                }

                yield return new UnityEngine.WaitForSeconds(0.7f);
            }

            _glitchCoroutine = null;
            if (customCanvasObj != null)
            {
                StartOfRound.Instance.StartCoroutine(CleanupGlitchCanvas(customCanvasObj, 5.0f));
            }
        }

        private void CreateCustomGlitchNotification(string titleText, string bodyText, UnityEngine.Transform parent, Vector2 screenPos, bool isNormal)
        {
            GameObject panelObj = new GameObject("CustomGlitchAlert");
panelObj.transform.SetParent(parent, false);

            UnityEngine.CanvasGroup cg = panelObj.AddComponent<UnityEngine.CanvasGroup>();
            cg.alpha = 1f;

            RectTransform rt = panelObj.AddComponent<RectTransform>();
            rt.SetParent(parent, false);

            // Compact size, fitted to text without extra spacing
            rt.sizeDelta = new Vector2(320f, 70f);

            if (isNormal)
            {
                rt.anchorMin = new UnityEngine.Vector2(1f, 0.5f);
                rt.anchorMax = new UnityEngine.Vector2(1f, 0.5f);
                rt.pivot = new UnityEngine.Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-20f, screenPos.y); // Use screenPos.y for vertical stacking
            }
            else
            {
                // Shocker concept: CHAOS ON THE SCREEN
                rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);
                rt.pivot = new UnityEngine.Vector2(0.5f, 0.5f);
                
                float rx = UnityEngine.Random.Range(-200f, 200f);
                float ry = UnityEngine.Random.Range(-120f, 120f);
                rt.anchoredPosition = new Vector2(rx, ry);
            }

            // Add background image
            UnityEngine.UI.Image bgImage = panelObj.AddComponent<UnityEngine.UI.Image>();
            
            // Retrieve vanilla sprite and material if possible for CRT/distortion shader consistency
            Sprite originalSprite = null;
            Material originalBgMaterial = null;
            if (HUDManager.Instance != null && HUDManager.Instance.tipsPanelAnimator != null)
            {
                UnityEngine.UI.Image[] srcImgs = HUDManager.Instance.tipsPanelAnimator.gameObject.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                foreach (var img in srcImgs)
                {
                    if (img != null && img.sprite != null && (img.gameObject.name.Contains("BG") || img.gameObject.name.Contains("Panel")))
                    {
                        originalSprite = img.sprite;
                        originalBgMaterial = img.material;
                        break;
                    }
                }
                if (originalSprite == null && srcImgs.Length > 0 && srcImgs[0] != null)
                {
                    originalSprite = srcImgs[0].sprite;
                    originalBgMaterial = srcImgs[0].material;
                }
            }

            TMPro.TMP_FontAsset fontAsset = null;
            Material fontMaterial = null;
            if (HUDManager.Instance != null)
            {
                if (HUDManager.Instance.weightCounter != null)
                {
                    fontAsset = HUDManager.Instance.weightCounter.font;
                    fontMaterial = HUDManager.Instance.weightCounter.fontSharedMaterial;
                }
                else if (HUDManager.Instance.tipsPanelHeader != null)
                {
                    fontAsset = HUDManager.Instance.tipsPanelHeader.font;
                    fontMaterial = HUDManager.Instance.tipsPanelHeader.fontSharedMaterial;
                }
            }

            if (originalSprite != null)
            {
                bgImage.sprite = originalSprite;
                bgImage.type = UnityEngine.UI.Image.Type.Sliced;
                if (originalBgMaterial != null)
                {
                    bgImage.material = originalBgMaterial; // Retain HUD/CRT curvature distortion shader
                }
            }

            // Opaque dark-red warning background (alpha = 0.85f for proper stacking layers but allowing CRT distortion to be visible)
            bgImage.color = new Color(0.75f, 0.02f, 0.02f, 0.85f);



            // Title Header Text
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(panelObj.transform, false);
            RectTransform headerRt = headerObj.AddComponent<RectTransform>();
            headerRt.anchorMin = new UnityEngine.Vector2(0f, 0.5f);
            headerRt.anchorMax = new UnityEngine.Vector2(1f, 0.95f);
            headerRt.offsetMin = new Vector2(10f, 0f);
            headerRt.offsetMax = new Vector2(-10f, -5f);

            TMPro.TextMeshProUGUI headerText = headerObj.AddComponent<TMPro.TextMeshProUGUI>();
            if (fontAsset != null) headerText.font = fontAsset;
            if (fontMaterial != null) headerText.fontSharedMaterial = fontMaterial; // Inherits HUD curvature/rendering effects
            headerText.text = titleText;
            headerText.fontSize = 17f;
            headerText.color = Color.white;
            headerText.alignment = TMPro.TextAlignmentOptions.Center;

            // Body Detail Text
            GameObject bodyObj = new GameObject("Body");
            bodyObj.transform.SetParent(panelObj.transform, false);
            RectTransform bodyRt = bodyObj.AddComponent<RectTransform>();
            bodyRt.anchorMin = new UnityEngine.Vector2(0f, 0.05f);
            bodyRt.anchorMax = new UnityEngine.Vector2(1f, 0.5f);
            bodyRt.offsetMin = new Vector2(10f, 5f);
            bodyRt.offsetMax = new Vector2(-10f, 0f);

            TMPro.TextMeshProUGUI bodyTextComp = bodyObj.AddComponent<TMPro.TextMeshProUGUI>();
            if (fontAsset != null) bodyTextComp.font = fontAsset;
            if (fontMaterial != null) bodyTextComp.fontSharedMaterial = fontMaterial; // Inherits HUD curvature/rendering effects
            bodyTextComp.text = bodyText;
            bodyTextComp.fontSize = 12f;
            bodyTextComp.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            bodyTextComp.alignment = TMPro.TextAlignmentOptions.Center;

            // Fade out and destroy
            if (StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StartCoroutine(FadeOutGlitchClone(cg, 4.0f));
            }
        }

        private IEnumerator FadeOutGlitchClone(UnityEngine.CanvasGroup cg, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += UnityEngine.Time.deltaTime;
                if (cg == null) yield break;
                cg.alpha = UnityEngine.Mathf.Lerp(1f, 0f, elapsed / duration);
                yield return null;
            }
            if (cg != null) UnityEngine.Object.Destroy(cg.gameObject);
        }

        private IEnumerator CleanupGlitchCanvas(GameObject canvasObj, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (canvasObj != null) UnityEngine.Object.Destroy(canvasObj);
        }

        private IEnumerator TemporarilyDisableCharger(float duration)
        {
            ShockerOverloadActive = true;

            // 1. Disable charger
            ItemCharger charger = UnityEngine.Object.FindObjectOfType<ItemCharger>();
            InteractTrigger chargerTrigger = null;
            Collider chargerCollider = null;
            if (charger != null)
            {
                chargerTrigger = charger.GetComponentInChildren<InteractTrigger>();
                if (chargerTrigger != null)
                {
                    chargerCollider = chargerTrigger.GetComponent<Collider>();
                }

                charger.enabled = false;
                if (chargerTrigger != null)
                {
                    chargerTrigger.interactable = false;
                    chargerTrigger.enabled = false;
                }
                if (chargerCollider != null)
                {
                    chargerCollider.enabled = false;
                }
            }

            // 2. Disable Map Camera Screen safely
            ManualCameraRenderer mapScreen = null;
            if (StartOfRound.Instance != null) mapScreen = StartOfRound.Instance.mapScreen;
            if (mapScreen == null && TimeOfDay.Instance != null && TimeOfDay.Instance.playersManager != null)
            {
                mapScreen = TimeOfDay.Instance.playersManager.mapScreen;
            }

            if (mapScreen != null)
            {
                try { mapScreen.SwitchScreenOn(false); } catch {}
            }

            // 3. Disable quota and deadline monitor texts
            if (StartOfRound.Instance != null)
            {
                if (StartOfRound.Instance.profitQuotaMonitorText != null)
                {
                    StartOfRound.Instance.profitQuotaMonitorText.enabled = false;
                }
                if (StartOfRound.Instance.deadlineMonitorText != null)
                {
                    StartOfRound.Instance.deadlineMonitorText.enabled = false;
                }
            }

            // 4. Close and disable terminal screen / trigger
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            InteractTrigger terminalTrigger = null;
            if (terminal != null)
            {
                if (terminal.terminalInUse)
                {
                    try { terminal.QuitTerminal(true); } catch {}
                }
                if (terminal.screenText != null)
                {
                    terminal.screenText.enabled = false;
                }
                terminalTrigger = terminal.GetComponent<InteractTrigger>();
                if (terminalTrigger == null) terminalTrigger = terminal.GetComponentInChildren<InteractTrigger>();

                if (terminalTrigger != null)
                {
                    terminalTrigger.interactable = false;
                    terminalTrigger.enabled = false;
                }
            }

            // 5. Wait for overload duration
            yield return new UnityEngine.WaitForSeconds(duration);

            // 6. Restore everything
            ShockerOverloadActive = false;

            if (charger != null) charger.enabled = true;
            if (chargerTrigger != null)
            {
                chargerTrigger.enabled = true;
                chargerTrigger.interactable = true;
            }
            if (chargerCollider != null) chargerCollider.enabled = true;

            if (mapScreen != null)
            {
                try { mapScreen.SwitchScreenOn(true); } catch {}
            }

            if (StartOfRound.Instance != null)
            {
                if (StartOfRound.Instance.profitQuotaMonitorText != null)
                {
                    StartOfRound.Instance.profitQuotaMonitorText.enabled = true;
                }
                if (StartOfRound.Instance.deadlineMonitorText != null)
                {
                    StartOfRound.Instance.deadlineMonitorText.enabled = true;
                }
            }

            if (terminal != null)
            {
                if (terminal.screenText != null)
                {
                    terminal.screenText.enabled = true;
                }
            }
            if (terminalTrigger != null)
            {
                terminalTrigger.enabled = true;
                terminalTrigger.interactable = true;
            }
        }

        public IEnumerator BootTerminalRoutine(Terminal terminal)
        {
            IsBooting = true;
            NeedsBootSequence = false;

            // Sync with other clients so they don't boot again
            if (Unity.Netcode.NetworkManager.Singleton != null)
            {
                if (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost)
                {
                    Networking.ShipCommanderNetwork.TerminalBootedMessage.SendClients(true);
                }
                else
                {
                    Networking.ShipCommanderNetwork.TerminalBootedMessage.SendServer(true);
                }
            }

            string[] phases = new string[]
            {
                "\n\n\n\nSHIPSUPPORT BIOS v4.02\n" +
                "Copyright (C) 2088-2104, Halden Corp.\n\n" +
                "Checking RAM... 640KB OK\n",

                "\n\n\n\nSHIPSUPPORT BIOS v4.02\n" +
                "Copyright (C) 2088-2104, Halden Corp.\n\n" +
                "Checking RAM... 640KB OK\n" +
                "Initializing disk controller...\n" +
                "Loading OS: ShipOS v2.10...\n",

                "\n\n\n\nSHIPSUPPORT BIOS v4.02\n" +
                "Copyright (C) 2088-2104, Halden Corp.\n\n" +
                "Checking RAM... 640KB OK\n" +
                "Initializing disk controller...\n" +
                "Loading OS: ShipOS v2.10...\n" +
                "[■□□□□□□□□□] 10%\n",

                "\n\n\n\nSHIPSUPPORT BIOS v4.02\n" +
                "Copyright (C) 2088-2104, Halden Corp.\n\n" +
                "Checking RAM... 640KB OK\n" +
                "Initializing disk controller...\n" +
                "Loading OS: ShipOS v2.10...\n" +
                "[■■■■■□□□□□] 50%\n",

                "\n\n\n\nSHIPSUPPORT BIOS v4.02\n" +
                "Copyright (C) 2088-2104, Halden Corp.\n\n" +
                "Checking RAM... 640KB OK\n" +
                "Initializing disk controller...\n" +
                "Loading OS: ShipOS v2.10...\n" +
                "[■■■■■■■■■■] 100%\n\n" +
                "Loading complete. Booting..."
            };

            for (int i = 0; i < phases.Length; i++)
            {
                CurrentBootText = phases[i];
                if (terminal != null)
                {
                    try
                    {
                        TerminalNode phaseNode = UnityEngine.ScriptableObject.CreateInstance<TerminalNode>();
                        phaseNode.displayText = CurrentBootText;
                        phaseNode.clearPreviousText = true;
                        terminal.LoadNewNode(phaseNode);
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Logger.LogError($"Error loading boot phase node: {ex}");
                    }
                }
                yield return new UnityEngine.WaitForSeconds(1.0f);
            }

            IsBooting = false;

            if (terminal != null)
            {
                // Force load welcome node
                if (terminal.terminalNodes != null && terminal.terminalNodes.specialNodes != null && terminal.terminalNodes.specialNodes.Count > 1)
                {
                    terminal.LoadNewNode(terminal.terminalNodes.specialNodes[1]);
                }
            }
        }
    }

    public interface IShipEvent
    {
        ShipEventType Type { get; }
        void Trigger(string arg);
        void Repair();
    }

    public class RadarInterferenceEvent : IShipEvent
    {
        private readonly ShipEventSystem _system;
        public ShipEventType Type => ShipEventType.RadarInterference;

        public RadarInterferenceEvent(ShipEventSystem system) => _system = system;

        public void Trigger(string arg)
        {
            var codes = new string[1];
            codes[0] = string.IsNullOrEmpty(arg) ? _system.GenerateCalibrationCode() : arg;
            _system.SetRadarCalibrationState(true, 0, codes);

            if (TimeOfDay.Instance?.playersManager?.mapScreen != null)
            {
                TimeOfDay.Instance.playersManager.mapScreen.SwitchScreenOn(false);
            }
        }

        public void Repair()
        {
            _system.SetRadarCalibrationState(false, 0, null);
            if (TimeOfDay.Instance?.playersManager?.mapScreen != null)
            {
                TimeOfDay.Instance.playersManager.mapScreen.SwitchScreenOn(true);
            }
        }
    }

    public class LightsFailureEvent : IShipEvent
    {
        private readonly ShipEventSystem _system;
        public ShipEventType Type => ShipEventType.LightsFailure;

        public LightsFailureEvent(ShipEventSystem system) => _system = system;

        public void Trigger(string arg)
        {
            Plugin.Logger.LogInfo("[ShipEventSystem] Executing LightsFailure logic...");
        }

        public void Repair()
        {
        }
    }

    public class DoorsJamEvent : IShipEvent
    {
        private readonly ShipEventSystem _system;
        public ShipEventType Type => ShipEventType.DoorsJam;

        public DoorsJamEvent(ShipEventSystem system) => _system = system;

        public void Trigger(string arg)
        {
            HangarShipDoor door = UnityEngine.Object.FindObjectOfType<HangarShipDoor>();
            if (door != null)
            {
                door.SetDoorClosed();
                door.PlayDoorAnimation(true);
            }
        }

        public void Repair()
        {
        }
    }

    public class CommsFailureEvent : IShipEvent
    {
        private readonly ShipEventSystem _system;
        public ShipEventType Type => ShipEventType.CommsFailure;

        public CommsFailureEvent(ShipEventSystem system) => _system = system;

        public void Trigger(string arg)
        {
            _system.SetupCommsAntenna();
            if (_system.CommsAntennaTrigger != null)
            {
                _system.CommsAntennaObject?.SetActive(true);
                _system.CommsAntennaTrigger.interactable = true;
                _system.StartBlinking();

                // Only start sparks and electricity sound loop if the Comms switch is ON
                bool isCommsSwitchOn = _system.ShipBreakerBoxInstance == null || _system.ShipBreakerBoxInstance.isSwitchOn[2];
                if (isCommsSwitchOn)
                {
                    _system.StartSparking();
                    if (_system.ElectricityAudioSource != null && ShipEventSystem.ElectricityClip != null && !_system.ElectricityAudioSource.isPlaying)
                    {
                        _system.ElectricityAudioSource.clip = ShipEventSystem.ElectricityClip;
                        _system.ElectricityAudioSource.Play();
                    }
                }
            }
        }

        public void Repair()
        {
            if (_system.CommsAntennaTrigger != null && StartOfRound.Instance != null)
            {
                StartOfRound.Instance.StartCoroutine(_system.DelayedAntennaDisable());
            }
            else if (_system.CommsAntennaTrigger != null)
            {
                _system.StopBlinking();
                _system.CommsAntennaTrigger.interactable = false;
                _system.CommsAntennaObject?.SetActive(false);
            }
        }
    }

    public class TerminalGlitchEvent : IShipEvent
    {
        private readonly ShipEventSystem _system;
        public ShipEventType Type => ShipEventType.TerminalGlitch;

        public TerminalGlitchEvent(ShipEventSystem system) => _system = system;

        public void Trigger(string arg)
        {
            Plugin.Logger.LogInfo("[ShipEventSystem] Executing TerminalGlitch logic...");
            // Force players out of terminal if they are using it
            Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
            if (terminal != null && terminal.terminalInUse)
            {
                try { terminal.QuitTerminal(true); } catch {}
            }
        }

        public void Repair()
        {
            Plugin.Logger.LogInfo("[ShipEventSystem] TerminalGlitch repaired.");
        }
    }

    public class BootHelper : MonoBehaviour
    {
        public static BootHelper Instance;
        private void Awake()
        {
            Instance = this;
        }
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
