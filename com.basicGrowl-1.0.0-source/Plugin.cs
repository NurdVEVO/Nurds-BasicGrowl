using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace com.basicGrowl
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.basicGrowl";
        public const string Name = "Basic growl";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        private Services.SeekerToneServices _seekerTones;
        private Services.SeekerOverlayDraw _seekerOverlay;
        private ConfigEntry<int> _cfgSeekerVolumePercent;
        private ConfigEntry<bool> _cfgEnableSeekerDiagnostics;
        private ConfigEntry<bool> _cfgEnableSeekerStateTransitions;
        private ConfigEntry<bool> _cfgEnableSeekerTickDiagnostics;

        internal float SeekerVolume01 => Mathf.Clamp01((_cfgSeekerVolumePercent?.Value ?? 100) / 100f);
        internal bool EnableSeekerDiagnostics => _cfgEnableSeekerDiagnostics?.Value ?? true;
        internal bool EnableSeekerStateTransitions => EnableSeekerDiagnostics && (_cfgEnableSeekerStateTransitions?.Value ?? true);
        internal bool EnableSeekerTickDiagnostics => EnableSeekerDiagnostics && (_cfgEnableSeekerTickDiagnostics?.Value ?? true);

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{Name} loaded");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            int mine = Harmony.GetAllPatchedMethods()
                .Count(m => Harmony.GetPatchInfo(m)?.Owners?.Contains(Guid) == true);

            Log.LogInfo($"Harmony patched {mine} method(s) for {Guid}");

            _cfgSeekerVolumePercent = Config.Bind(
                "Audio",
                "Growl Volume (Grrrrrrrrrrrrrr)",
                100,
                new ConfigDescription(
                    "Master seeker audio volume percent (0-100). 100 is full current loudness (0 dB).",
                    new AcceptableValueRange<int>(0, 100)));

            _cfgEnableSeekerDiagnostics = Config.Bind(
                "Debug",
                "EnableSeekerDiagnostics",
                true,
                "Enable verbose seeker diagnostic state/telemetry logs.");

            _cfgEnableSeekerStateTransitions = Config.Bind(
                "Debug",
                "EnableSeekerStateTransitions",
                true,
                "Enable seeker transition logs (target/HQ/FOV/LOS/range state changes).");

            _cfgEnableSeekerTickDiagnostics = Config.Bind(
                "Debug",
                "EnableSeekerTickDiagnostics",
                true,
                "Enable periodic seeker telemetry logs (SeekerDiag entries).");

            Seeker.SeekerProfileRegistry.RegisterAll();
            _seekerTones = new Services.SeekerToneServices(this);
            _seekerOverlay = gameObject.AddComponent<Services.SeekerOverlayDraw>();
            _seekerOverlay.Bind(_seekerTones);
            Log.LogInfo("[Overlay] Seeker overlay component attached.");
        }

        private void Update()
        {
            _seekerTones?.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
