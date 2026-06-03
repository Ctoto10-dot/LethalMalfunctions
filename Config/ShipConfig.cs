using BepInEx.Configuration;

namespace ShipCommander.Config
{
    /// <summary>
    /// All configurable settings for ShipCommander.
    /// Players can edit these in BepInEx/config/ShipCommander.cfg
    /// </summary>
    public static class ShipConfig
    {
        // --- Power System ---
        public static ConfigEntry<float> MaxPower;
        public static ConfigEntry<float> PowerRegenRate;
        public static ConfigEntry<float> RadarDrain;
        public static ConfigEntry<float> LightsDrain;
        public static ConfigEntry<float> DoorsDrain;
        public static ConfigEntry<float> CommsDrain;

        // --- Events ---
        public static ConfigEntry<bool> EnableEvents;
        public static ConfigEntry<float> EventMinInterval;
        public static ConfigEntry<float> EventMaxInterval;
        public static ConfigEntry<float> EventReactionTime;

        // --- Progression ---
        public static ConfigEntry<bool> EnableProgression;
        public static ConfigEntry<float> HardMoonMultiplier;

        public static void Init(ConfigFile config)
        {
            // Power System
            MaxPower = config.Bind("Power", "MaxPower", 100f,
                "Maximum ship power capacity");
            PowerRegenRate = config.Bind("Power", "RegenRate", 0.5f,
                "Power regeneration per second from the generator");
            RadarDrain = config.Bind("Power", "RadarDrain", 2f,
                "Power drain per second when radar is active");
            LightsDrain = config.Bind("Power", "LightsDrain", 1f,
                "Power drain per second when lights are on");
            DoorsDrain = config.Bind("Power", "DoorsDrain", 1f,
                "Power drain per second when automatic doors are powered");
            CommsDrain = config.Bind("Power", "CommsDrain", 1.5f,
                "Power drain per second when communications are active");

            // Events
            EnableEvents = config.Bind("Events", "EnableEvents", true,
                "Enable random ship events during missions");
            EventMinInterval = config.Bind("Events", "MinInterval", 180f,
                "Minimum seconds between random events (base)");
            EventMaxInterval = config.Bind("Events", "MaxInterval", 420f,
                "Maximum seconds between random events (base)");
            EventReactionTime = config.Bind("Events", "ReactionTime", 8f,
                "Seconds the navigator has to react to an event before consequences");

            // Progression
            EnableProgression = config.Bind("Progression", "EnableProgression", true,
                "Enable difficulty progression (events happen more often as days go by)");
            HardMoonMultiplier = config.Bind("Progression", "HardMoonMultiplier", 0.7f,
                "Multiplier for event intervals on hard moons (Rend, Dine, Titan). Lower means faster events.");
        }
    }
}
