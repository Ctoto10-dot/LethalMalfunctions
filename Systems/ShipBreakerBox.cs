using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ShipCommander.Networking;

namespace ShipCommander.Systems
{
    public class ShipBreakerBox : MonoBehaviour
    {
        public InteractTrigger[] switchTriggers;
        public bool[] isSwitchOn;
        public bool isBroken = false;
        public bool IsRebooting { get; private set; }
        private ShipEventSystem _eventSystem;
        private AudioSource _rebootAudioSource;

        public void Initialize(ShipEventSystem eventSystem, InteractTrigger[] triggers)
        {
            _eventSystem = eventSystem;
            switchTriggers = triggers;
            isSwitchOn = new bool[triggers.Length];

            _rebootAudioSource = gameObject.AddComponent<AudioSource>();
            _rebootAudioSource.spatialBlend = 1.0f; // 3D sound
            _rebootAudioSource.spatialize = false;
            _rebootAudioSource.maxDistance = 15f;
            _rebootAudioSource.volume = 0.8f;
            _rebootAudioSource.playOnAwake = false;
            _rebootAudioSource.loop = false;

            for (int i = 0; i < triggers.Length; i++)
            {
                isSwitchOn[i] = true;
                int index = i; // local copy for closure

                // Add our custom listener to the existing onInteract event to preserve vanilla animations/sounds
                triggers[i].onInteract.AddListener((player) =>
                {
                    OnSwitchInteracted(index, player);
                });
                
                // Set default state: initially ON, so hoverTip should be "Turn OFF"
                triggers[i].interactable = true;
                triggers[i].hoverTip = "Turn OFF";
                triggers[i].disabledHoverTip = "";
            }
        }

        public void BreakRandomSwitches(int count)
        {
            if (switchTriggers == null || switchTriggers.Length == 0) return;
            
            isBroken = true;
            
            List<int> availableIndices = new List<int>();
            for (int i = 0; i < switchTriggers.Length; i++)
            {
                if (isSwitchOn[i]) availableIndices.Add(i);
            }

            // Randomize and pick 'count' switches to flip
            for (int i = 0; i < availableIndices.Count; i++)
            {
                int temp = availableIndices[i];
                int randomIndex = UnityEngine.Random.Range(i, availableIndices.Count);
                availableIndices[i] = availableIndices[randomIndex];
                availableIndices[randomIndex] = temp;
            }

            int flipped = 0;
            foreach (int index in availableIndices)
            {
                if (flipped >= count) break;
                SetSwitchState(index, false, false);
                flipped++;
            }
            
            Plugin.Logger.LogInfo($"[ShipBreakerBox] Flipped {flipped} switches OFF.");
        }

        public void StartRebootOverload(AudioClip clip)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(RebootRoutine(clip));
            }
        }

        private IEnumerator RebootRoutine(AudioClip clip)
        {
            IsRebooting = true;

            if (_rebootAudioSource != null && clip != null)
            {
                _rebootAudioSource.clip = clip;
                _rebootAudioSource.Play();
            }

            float duration = 15f;
            float elapsed = 0f;
            float sparkInterval = 1.666f;
            float nextSparkTime = 0f;

            while (elapsed < duration)
            {
                if (elapsed >= nextSparkTime)
                {
                    if (_eventSystem != null)
                    {
                        _eventSystem.SpawnSparks(transform.position + new Vector3(0, 0.5f, 0), 10);
                    }
                    nextSparkTime += sparkInterval;
                }

                yield return null;
                elapsed += Time.deltaTime;
            }

            IsRebooting = false;
        }

        private void OnSwitchInteracted(int index, GameNetcodeStuff.PlayerControllerB player)
        {
            if (IsRebooting)
            {
                // Shock the player!
                if (player != null)
                {
                    player.DamagePlayer(25, true, true, CauseOfDeath.Electrocution);
                }

                if (_eventSystem != null && ShipEventSystem.ElectricityClip != null)
                {
                    if (_rebootAudioSource != null)
                    {
                        _rebootAudioSource.PlayOneShot(ShipEventSystem.ElectricityClip, 0.8f);
                    }
                }

                // Spawn sparks locally on the switch!
                if (index >= 0 && index < switchTriggers.Length && _eventSystem != null)
                {
                    var trigger = switchTriggers[index];
                    _eventSystem.SpawnSparks(trigger.transform.position, 15);
                }

                if (HUDManager.Instance != null)
                {
                    HUDManager.Instance.DisplayTip("ELECTROCUTION WARNING", "Breaker box is overloaded! Wait for system reboot.", true);
                }

                // Force the switch visual position back to OFF!
                SetSwitchState(index, false, false);
                return;
            }

            bool newState = !isSwitchOn[index];

            // Locally update: pass false for playSound since vanilla interaction triggers the click sound automatically
            SetSwitchState(index, newState, false);

            // Sync via network
            if (Unity.Netcode.NetworkManager.Singleton != null)
            {
                int encoded = index * 2 + (newState ? 1 : 0);
                if (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost)
                {
                    ShipCommanderNetwork.BreakerSwitchMessage.SendClients(encoded);
                }
                else
                {
                    ShipCommanderNetwork.BreakerSwitchMessage.SendServer(encoded);
                }
            }
        }

        public void SetSwitchState(int index, bool isOn, bool playSound)
        {
            if (index < 0 || index >= switchTriggers.Length) return;

            isSwitchOn[index] = isOn;

            // Visual update
            var trigger = switchTriggers[index];
            trigger.interactable = true; // Always interactable to allow toggling back and forth!
            trigger.hoverTip = isOn ? "Turn OFF" : "Turn ON";
            
            // Animate switch handle using the trigger's AnimatedObjectTrigger
            var animTrigger = trigger.GetComponent<AnimatedObjectTrigger>();
            if (animTrigger != null)
            {
                Animator animA = null;
                Animator animB = null;
                string animString = null;
                try
                {
                    var fields = typeof(AnimatedObjectTrigger).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        if (f.Name == "triggerAnimator")
                        {
                            animA = (Animator)f.GetValue(animTrigger);
                        }
                        else if (f.Name == "triggerAnimatorB")
                        {
                            animB = (Animator)f.GetValue(animTrigger);
                        }
                        else if (f.Name == "animationString")
                        {
                            animString = (string)f.GetValue(animTrigger);
                        }
                        else if (f.Name == "boolValue")
                        {
                            f.SetValue(animTrigger, isOn);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"[ShipBreakerBox] Reflection error finding animator fields: {ex.Message}");
                }

                if (!string.IsNullOrEmpty(animString))
                {
                    if (animA != null) animA.SetBool(animString, isOn);
                    if (animB != null) animB.SetBool(animString, isOn);
                }
            }
            else
            {
                // Fallback to parent animator
                Animator anim = trigger.GetComponentInParent<Animator>();
                if (anim != null)
                {
                    anim.SetBool("turnedOn", isOn);
                }
            }

            if (playSound)
            {
                // Find AudioSource dynamically using reflection to find any AudioSource field on the trigger
                AudioSource source = null;
                try
                {
                    var fields = typeof(InteractTrigger).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var field in fields)
                    {
                        if (field.FieldType == typeof(AudioSource))
                        {
                            source = (AudioSource)field.GetValue(trigger);
                            if (source != null) break;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.Logger.LogWarning($"[ShipBreakerBox] Reflection error finding AudioSource: {ex.Message}");
                }

                if (source == null) source = trigger.GetComponent<AudioSource>();
                if (source == null) source = trigger.GetComponentInParent<AudioSource>();

                if (source != null)
                {
                    if (source.clip != null)
                    {
                        source.PlayOneShot(source.clip);
                    }
                    else
                    {
                        source.Play();
                    }
                }
            }

            // Turn off sparks and electricity sound if the Comms switch is flipped OFF
            if (index == 2)
            {
                if (!isOn)
                {
                    _eventSystem.StopSparking();
                    if (_eventSystem.ElectricityAudioSource != null)
                    {
                        _eventSystem.ElectricityAudioSource.Stop();
                    }
                }
                else
                {
                    // Start sparks and electricity loop again if Comms are still broken
                    if (_eventSystem.BrokenSystems.Contains(ShipEventType.CommsFailure))
                    {
                        _eventSystem.StartSparking();
                        if (_eventSystem.ElectricityAudioSource != null && ShipEventSystem.ElectricityClip != null && !_eventSystem.ElectricityAudioSource.isPlaying)
                        {
                            _eventSystem.ElectricityAudioSource.clip = ShipEventSystem.ElectricityClip;
                            _eventSystem.ElectricityAudioSource.Play();
                        }
                    }
                }
            }

            // Turn the radar screen off or on depending on the radar switch state
            if (index == 3)
            {
                if (TimeOfDay.Instance?.playersManager?.mapScreen != null)
                {
                    if (!isOn)
                    {
                        TimeOfDay.Instance.playersManager.mapScreen.SwitchScreenOn(false);
                    }
                    else
                    {
                        // Only turn screen back on if radar isn't actively broken by interference or shocker
                        if (!_eventSystem.BrokenSystems.Contains(ShipEventType.RadarInterference) && !_eventSystem.ShockerOverloadActive)
                        {
                            TimeOfDay.Instance.playersManager.mapScreen.SwitchScreenOn(true);
                        }
                    }
                }
            }

            // Terminal power control (Switch 4)
            if (index == 4)
            {
                Terminal terminal = UnityEngine.Object.FindObjectOfType<Terminal>();
                InteractTrigger terminalTrigger = null;
                if (terminal != null)
                {
                    terminalTrigger = terminal.GetComponent<InteractTrigger>();
                    if (terminalTrigger == null) terminalTrigger = terminal.GetComponentInChildren<InteractTrigger>();
                }

                if (!isOn)
                {
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
                    }
                    if (terminalTrigger != null)
                    {
                        terminalTrigger.interactable = false;
                        terminalTrigger.enabled = false;
                    }
                }
                else
                {
                    if (terminal != null && terminal.screenText != null)
                    {
                        terminal.screenText.enabled = true;
                    }
                    if (terminalTrigger != null)
                    {
                        terminalTrigger.enabled = true;
                        terminalTrigger.interactable = true;
                    }

                    _eventSystem.NeedsBootSequence = true;

                    // Repair TerminalGlitch if it's active
                    if (_eventSystem.BrokenSystems.Contains(ShipEventType.TerminalGlitch))
                    {
                        _eventSystem.RepairSystem(ShipEventType.TerminalGlitch);
                        if (Unity.Netcode.NetworkManager.Singleton != null)
                        {
                            if (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost)
                                ShipCommanderNetwork.RepairMessage.SendClients((int)ShipEventType.TerminalGlitch);
                            else
                                ShipCommanderNetwork.RepairMessage.SendServer((int)ShipEventType.TerminalGlitch);
                        }
                    }
                }
            }

            CheckIfRepaired();
        }

        public void ResetAllSwitches()
        {
            if (switchTriggers == null) return;
            isBroken = false;
            for (int i = 0; i < switchTriggers.Length; i++)
            {
                SetSwitchState(i, true, false);
            }
            Plugin.Logger.LogInfo("[ShipBreakerBox] Resetted all switches to ON state.");
        }

        private void CheckIfRepaired()
        {
            // Lights (Switch 0) turned ON repairs LightsFailure
            if (isSwitchOn[0] && _eventSystem.BrokenSystems.Contains(ShipEventType.LightsFailure))
            {
                _eventSystem.RepairSystem(ShipEventType.LightsFailure);
                if (Unity.Netcode.NetworkManager.Singleton != null)
                {
                    if (Unity.Netcode.NetworkManager.Singleton.IsServer || Unity.Netcode.NetworkManager.Singleton.IsHost)
                        ShipCommanderNetwork.RepairMessage.SendClients((int)ShipEventType.LightsFailure);
                    else
                        ShipCommanderNetwork.RepairMessage.SendServer((int)ShipEventType.LightsFailure);
                }
            }

            // Doors are now repaired exclusively via the terminal repair doors command

            bool anySystemBroken = false;
            for (int i = 0; i < isSwitchOn.Length; i++)
            {
                if (!isSwitchOn[i]) anySystemBroken = true;
            }
            isBroken = anySystemBroken;
        }
    }
}
