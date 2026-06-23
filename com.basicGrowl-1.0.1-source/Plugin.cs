using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace com.basicGrowl
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.basicGrowl";
        public const string Name = "Basic growl";
        public const string Version = "1.0.1";

        internal static ManualLogSource Log;
        private Services.SeekerToneServices _seekerTones;
        private Services.SeekerOverlayDraw _seekerOverlay;
        private ConfigEntry<int> _cfgSeekerVolumePercent;
        private ConfigEntry<bool> _cfgEnableSeekerDiagnostics;
        private ConfigEntry<bool> _cfgEnableSeekerStateTransitions;
        private ConfigEntry<bool> _cfgEnableSeekerTickDiagnostics;
        private ConfigEntry<string> _cfgToggleVerboseLoggingButton;
        private ConfigEntry<string> _cfgRefreshProfilesButton;

        internal float SeekerVolume01 => Mathf.Clamp01((_cfgSeekerVolumePercent?.Value ?? 100) / 100f);
        internal bool EnableSeekerDiagnostics => _cfgEnableSeekerDiagnostics?.Value ?? false;
        internal bool EnableSeekerStateTransitions => EnableSeekerDiagnostics && (_cfgEnableSeekerStateTransitions?.Value ?? false);
        internal bool EnableSeekerTickDiagnostics => EnableSeekerDiagnostics && (_cfgEnableSeekerTickDiagnostics?.Value ?? false);

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{Name} loaded");

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
                false,
                "Enable verbose seeker diagnostic logs.");

            _cfgEnableSeekerStateTransitions = Config.Bind(
                "Debug",
                "EnableSeekerStateTransitions",
                false,
                "Enable seeker transition logs (target/HQ/FOV/LOS/range state changes).");

            _cfgEnableSeekerTickDiagnostics = Config.Bind(
                "Debug",
                "EnableSeekerTickDiagnostics",
                false,
                "Enable periodic seeker telemetry logs (SeekerDiag entries).");

            _cfgToggleVerboseLoggingButton = Config.Bind(
                "Actions",
                "Toggle Verbose Logging",
                string.Empty,
                CreateButtonDescription(
                    "Configuration Manager button: toggles all verbose seeker logging settings.",
                    "Toggle Verbose Logging",
                    ToggleVerboseLogging));

            _cfgRefreshProfilesButton = Config.Bind(
                "Actions",
                "Refresh Profiles",
                string.Empty,
                CreateButtonDescription(
                    "Configuration Manager button: reloads seeker profile text files from disk.",
                    "Refresh Profiles",
                    RefreshProfiles));

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

        private void ToggleVerboseLogging()
        {
            bool enable = !EnableSeekerDiagnostics;
            _cfgEnableSeekerDiagnostics.Value = enable;
            _cfgEnableSeekerStateTransitions.Value = enable;
            _cfgEnableSeekerTickDiagnostics.Value = enable;
            Log.LogInfo($"Verbose seeker logging {(enable ? "enabled" : "disabled")}.");
        }

        private void RefreshProfiles()
        {
            Seeker.SeekerProfileRegistry.RegisterAll();
            _seekerTones?.RefreshProfiles();
            Log.LogInfo("Seeker profiles refreshed.");
        }

        private static ConfigDescription CreateButtonDescription(string description, string buttonText, Action onClick)
        {
            object tag = TryCreateConfigurationManagerButton(buttonText, onClick);
            return tag != null
                ? new ConfigDescription(description, null, tag)
                : new ConfigDescription(description);
        }

        private static object TryCreateConfigurationManagerButton(string buttonText, Action onClick)
        {
            Type attributeType = FindConfigurationManagerAttributesType();
            if (attributeType == null)
                return null;

            try
            {
                object attributes = Activator.CreateInstance(attributeType);
                SetBoolProperty(attributeType, attributes, "HideDefaultButton", true);
                SetBoolProperty(attributeType, attributes, "HideSettingName", true);
                SetBoolProperty(attributeType, attributes, "ReadOnly", true);

                var drawerProperty = attributeType.GetProperty("CustomDrawer", BindingFlags.Public | BindingFlags.Instance);
                if (drawerProperty == null || !drawerProperty.CanWrite)
                    return attributes;

                Action<ConfigEntryBase> drawer = _ =>
                {
                    if (GUILayout.Button(buttonText))
                        onClick?.Invoke();
                };

                drawerProperty.SetValue(attributes, drawer, null);
                return attributes;
            }
            catch (Exception e)
            {
                Log?.LogWarning($"Configuration Manager button setup failed for '{buttonText}': {e.Message}");
                return null;
            }
        }

        private static Type FindConfigurationManagerAttributesType()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType("ConfigurationManagerAttributes", throwOnError: false);
                if (type != null)
                    return type;

                type = assemblies[i].GetType("ConfigurationManager.ConfigurationManagerAttributes", throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void SetBoolProperty(Type type, object target, string propertyName, bool value)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                property.SetValue(target, value, null);
        }
    }
}
