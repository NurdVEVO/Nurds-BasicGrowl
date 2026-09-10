namespace com.basicGrowl.Seeker
{
    internal sealed class SeekerSoundProfile
    {
        public string PrefabName;            // e.g. "AAM1"
        public string WeaponName;            // optional display name
        public string FolderName = "SeekerNoises";
        public bool FlareReject = true;      // true = ignore flare heat for seeker pitch

        // Audio clips
        public string CagedFile;             // e.g. "Aim9Caged.wav"
        public string UnCagedFile;           // e.g. "Aim9UnCaged.wav"
        public string EnvSky;                // e.g. "Aim9EnvSky.wav"
        public string EnvGnd;                // e.g. "Aim9EnvGnd.wav"

        // Pitch mapping
        public float SeekerWeakPitch = 0.5f;
        public float SeekerStrongPitch = 1.0f;
        public float HeatPitchMin = 2.0f;
        public float HeatPitchMax = 11.0f;
        public float HeatSensitivity = 2.25f;
        public float HeatCurvePower = 1.10f;
        public float HeatValidOnThreshold = 1.10f;
        public float HeatValidOffThreshold = 0.95f;

        // Tick + lock behavior
        public float TickInterval = 0.05f;     // seconds
        public float LockSmoothTime = 0.08f;   // seconds

        // Seeker geometry/tracking
        public float NarrowFovDeg = 10f;       // full angle
        public float FrontConeDeg = 150f;      // full angle
        public float SlewRateDegPerSec = 100f;
        public float TrackErrorMinDeg = 2f;
        public float TrackErrorMaxDeg = 18f;

        // Audio mix behavior
        public float BaseCagedVolume = 0.70f;
        public float LockedCagedVolume = 0.35f;
        public float LockUncagedMaxVolume = 0.85f;
        public float EnvOverlayMaxVolume = 0.45f;
        public float EnvSwitchFadeTime = 0.20f;

        // Flare pulse behavior
        public float FlarePulseFadeInSeconds = 0.4f;
        public float FlarePulseFadeOutSeconds = 0.4f;

        // Environment probe
        public float EnvProbeDistanceMeters = 50000f;

        // Range-gate behavior
        public bool StrictHeatRangeGateWhenUnavailable = true;
        public float RangeCalcIntervalSeconds = 0.20f;
        public float RangeRecalcDistThresholdMeters = 150f;
        public float RangeRecalcSpeedThreshold = 8f;
        public float RangeRecalcAltitudeThresholdMeters = 60f;
        public float RangeRecalcRelativeSpeedThreshold = 12f;
    }
}
