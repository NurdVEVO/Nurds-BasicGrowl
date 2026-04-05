using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace com.basicGrowl.Seeker
{
    internal static class SeekerProfileRegistry
    {
        private const string LegacyExternalProfileFolderName = "SoundOfTheGrowlers";
        private static readonly TimeSpan HotReloadScanInterval = TimeSpan.FromSeconds(1.0);

        private static readonly Dictionary<string, SeekerSoundProfile> _byPrefab =
            new Dictionary<string, SeekerSoundProfile>(StringComparer.Ordinal);
        private static DateTime _nextHotReloadScanUtc = DateTime.MinValue;
        private static string _lastProfilesFingerprint = string.Empty;

        public static void Register(SeekerSoundProfile profile, bool overwriteExisting = false)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(profile.PrefabName))
                throw new ArgumentException("Profile PrefabName is required.", nameof(profile));

            string prefabName = profile.PrefabName.Trim();
            profile.PrefabName = prefabName;

            if (!overwriteExisting && _byPrefab.ContainsKey(prefabName))
                return;

            _byPrefab[prefabName] = profile;
        }

        public static bool TryGetByPrefab(string prefabName, out SeekerSoundProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(prefabName)) return false;
            return _byPrefab.TryGetValue(prefabName.Trim(), out profile);
        }

        public static bool TryReloadIfChanged()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextHotReloadScanUtc)
                return false;

            _nextHotReloadScanUtc = now + HotReloadScanInterval;

            string primaryDirectory = GetPrimaryProfileDirectory();
            string legacyDirectory = Path.Combine(primaryDirectory, LegacyExternalProfileFolderName);
            string fingerprint = BuildProfilesFingerprint(primaryDirectory, legacyDirectory);
            if (string.Equals(fingerprint, _lastProfilesFingerprint, StringComparison.Ordinal))
                return false;

            RegisterAll();
            Plugin.Log.LogInfo("[SeekerProfiles] Text profile changes detected; reloaded.");
            return true;
        }

        public static void RegisterAll()
        {
            _byPrefab.Clear();

            string profileDirectory = GetPrimaryProfileDirectory();
            EnsureDirectory(profileDirectory);

            int externalLoaded = RegisterProfilesFromTextFiles(profileDirectory, overwriteExisting: true);
            string legacyDirectory = Path.Combine(profileDirectory, LegacyExternalProfileFolderName);
            if (Directory.Exists(legacyDirectory))
                externalLoaded += RegisterProfilesFromTextFiles(legacyDirectory, overwriteExisting: false);

            int internalAdded = RegisterInternalProfiles();
            int filesCreated = EnsureEditableProfileFiles(profileDirectory);
            _lastProfilesFingerprint = BuildProfilesFingerprint(profileDirectory, legacyDirectory);
            _nextHotReloadScanUtc = DateTime.UtcNow + HotReloadScanInterval;

            Plugin.Log.LogInfo(
                $"Seeker profiles ready: externalLoaded={externalLoaded}, internalAdded={internalAdded}, total={_byPrefab.Count}, textFilesCreated={filesCreated}.");
        }

        private static string GetPrimaryProfileDirectory()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(SeekerProfileRegistry).Assembly.Location);
            return string.IsNullOrEmpty(assemblyDir) ? "." : assemblyDir;
        }

        private static void EnsureDirectory(string directoryPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(directoryPath))
                    Directory.CreateDirectory(directoryPath);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to create seeker profile folder '{directoryPath}': {e.Message}");
            }
        }

        private static int RegisterProfilesFromTextFiles(string directoryPath, bool overwriteExisting)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            string[] files = Directory.GetFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int loaded = 0;
            for (int i = 0; i < files.Length; i++)
            {
                if (!TryReadProfileFromTextFile(files[i], out var profile))
                    continue;

                Register(profile, overwriteExisting);
                loaded++;
            }

            return loaded;
        }

        private static bool TryReadProfileFromTextFile(string filePath, out SeekerSoundProfile profile)
        {
            profile = null;
            var values = ParseKeyValueTextFile(filePath);
            if (values.Count == 0) return false;
            if (!values.ContainsKey("PrefabName") || !values.ContainsKey("CagedFile")) return false;

            var d = new SeekerSoundProfile();
            profile = new SeekerSoundProfile
            {
                PrefabName = GetString(values, "PrefabName", d.PrefabName),
                WeaponName = GetString(values, "WeaponName", d.WeaponName),
                FolderName = GetString(values, "FolderName", d.FolderName),
                FlareReject = GetBool(values, "FlareReject", d.FlareReject),
                CagedFile = GetString(values, "CagedFile", d.CagedFile),
                UnCagedFile = GetString(values, "UnCagedFile", GetString(values, "UncagedFile", d.UnCagedFile)),
                EnvSky = GetString(values, "EnvSky", d.EnvSky),
                EnvGnd = GetString(values, "EnvGnd", d.EnvGnd),
                SeekerWeakPitch = GetFloat(values, "SeekerWeakPitch", d.SeekerWeakPitch),
                SeekerStrongPitch = GetFloat(values, "SeekerStrongPitch", d.SeekerStrongPitch),
                HeatPitchMin = GetFloat(values, "HeatPitchMin", d.HeatPitchMin),
                HeatPitchMax = GetFloat(values, "HeatPitchMax", d.HeatPitchMax),
                HeatSensitivity = GetFloat(values, "HeatSensitivity", d.HeatSensitivity),
                HeatCurvePower = GetFloat(values, "HeatCurvePower", d.HeatCurvePower),
                HeatValidOnThreshold = GetFloat(values, "HeatValidOnThreshold", d.HeatValidOnThreshold),
                HeatValidOffThreshold = GetFloat(values, "HeatValidOffThreshold", d.HeatValidOffThreshold),
                TickInterval = GetFloat(values, "TickInterval", d.TickInterval),
                LockSmoothTime = GetFloat(values, "LockSmoothTime", d.LockSmoothTime),
                NarrowFovDeg = GetFloat(values, "NarrowFovDeg", d.NarrowFovDeg),
                FrontConeDeg = GetFloat(values, "FrontConeDeg", d.FrontConeDeg),
                SlewRateDegPerSec = GetFloat(values, "SlewRateDegPerSec", d.SlewRateDegPerSec),
                TrackErrorMinDeg = GetFloat(values, "TrackErrorMinDeg", d.TrackErrorMinDeg),
                TrackErrorMaxDeg = GetFloat(values, "TrackErrorMaxDeg", d.TrackErrorMaxDeg),
                BaseCagedVolume = GetFloat(values, "BaseCagedVolume", d.BaseCagedVolume),
                LockedCagedVolume = GetFloat(values, "LockedCagedVolume", d.LockedCagedVolume),
                LockUncagedMaxVolume = GetFloat(values, "LockUncagedMaxVolume", d.LockUncagedMaxVolume),
                EnvOverlayMaxVolume = GetFloat(values, "EnvOverlayMaxVolume", d.EnvOverlayMaxVolume),
                EnvSwitchFadeTime = GetFloat(values, "EnvSwitchFadeTime", d.EnvSwitchFadeTime),
                FlarePulseFadeInSeconds = GetFloat(values, "FlarePulseFadeInSeconds", d.FlarePulseFadeInSeconds),
                FlarePulseFadeOutSeconds = GetFloat(values, "FlarePulseFadeOutSeconds", d.FlarePulseFadeOutSeconds),
                EnvProbeDistanceMeters = GetFloat(values, "EnvProbeDistanceMeters", d.EnvProbeDistanceMeters),
                StrictHeatRangeGateWhenUnavailable = GetBool(values, "StrictHeatRangeGateWhenUnavailable", d.StrictHeatRangeGateWhenUnavailable),
                RangeCalcIntervalSeconds = GetFloat(values, "RangeCalcIntervalSeconds", d.RangeCalcIntervalSeconds),
                RangeRecalcDistThresholdMeters = GetFloat(values, "RangeRecalcDistThresholdMeters", d.RangeRecalcDistThresholdMeters),
                RangeRecalcSpeedThreshold = GetFloat(values, "RangeRecalcSpeedThreshold", d.RangeRecalcSpeedThreshold),
                RangeRecalcAltitudeThresholdMeters = GetFloat(values, "RangeRecalcAltitudeThresholdMeters", d.RangeRecalcAltitudeThresholdMeters),
                RangeRecalcRelativeSpeedThreshold = GetFloat(values, "RangeRecalcRelativeSpeedThreshold", d.RangeRecalcRelativeSpeedThreshold)
            };

            ApplyProfileValidation(filePath, profile, d);
            return !string.IsNullOrWhiteSpace(profile.PrefabName) && !string.IsNullOrWhiteSpace(profile.CagedFile);
        }

        private static Dictionary<string, string> ParseKeyValueTextFile(string filePath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(filePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = (lines[i] ?? string.Empty).Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("//") || trimmed.StartsWith(";")) continue;
                int idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;
                string key = trimmed.Substring(0, idx).Trim();
                string val = trimmed.Substring(idx + 1).Trim();
                if (val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\""))
                    val = val.Substring(1, val.Length - 2);
                map[key] = val;
            }

            return map;
        }

        private static string GetString(IDictionary<string, string> values, string key, string fallback)
        {
            if (!values.TryGetValue(key, out var raw))
                return fallback;

            return (raw ?? fallback)?.Trim();
        }

        private static float GetFloat(IDictionary<string, string> values, string key, float fallback)
        {
            if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return fallback;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            return fallback;
        }

        private static bool GetBool(IDictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                return fallback;
            if (bool.TryParse(raw, out var b))
                return b;
            return fallback;
        }

        private static int RegisterInternalProfiles()
        {
            var asm = typeof(SeekerProfileRegistry).Assembly;
            var profileTypes = asm.GetTypes()
                .Where(t => t.Namespace == "com.basicGrowl.Seeker.Profiles")
                .Select(t => t.GetMethod("Register", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m != null)
                .ToList();

            int before = _byPrefab.Count;
            for (int i = 0; i < profileTypes.Count; i++)
            {
                try { profileTypes[i].Invoke(null, null); } catch { }
            }

            return _byPrefab.Count - before;
        }

        private static int EnsureEditableProfileFiles(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return 0;

            int created = 0;
            foreach (var profile in _byPrefab.Values.Where(p => p != null && !string.IsNullOrWhiteSpace(p.PrefabName)))
            {
                string path = Path.Combine(directoryPath, profile.PrefabName + ".txt");
                if (File.Exists(path)) continue;
                try
                {
                    File.WriteAllText(path, BuildProfileText(profile), Encoding.UTF8);
                    created++;
                }
                catch { }
            }

            return created;
        }

        private static string BuildProfilesFingerprint(string primaryDirectory, string legacyDirectory)
        {
            var sb = new StringBuilder(2048);
            AppendDirectoryFingerprint(sb, primaryDirectory);
            AppendDirectoryFingerprint(sb, legacyDirectory);
            return sb.ToString();
        }

        private static void AppendDirectoryFingerprint(StringBuilder sb, string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                sb.Append("missing:").Append(directoryPath ?? "<null>").Append("|");
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directoryPath, "*.txt", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                sb.Append("error:").Append(directoryPath).Append("|");
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    var info = new FileInfo(files[i]);
                    sb.Append(info.Name)
                        .Append(":")
                        .Append(info.Length)
                        .Append(":")
                        .Append(info.LastWriteTimeUtc.Ticks)
                        .Append("|");
                }
                catch
                {
                    sb.Append(Path.GetFileName(files[i])).Append(":err|");
                }
            }
        }

        private static void ApplyProfileValidation(string filePath, SeekerSoundProfile p, SeekerSoundProfile d)
        {
            string fileName = Path.GetFileName(filePath);
            if (p == null || d == null) return;

            p.PrefabName = (p.PrefabName ?? string.Empty).Trim();
            p.CagedFile = (p.CagedFile ?? string.Empty).Trim();
            p.FolderName = string.IsNullOrWhiteSpace(p.FolderName) ? d.FolderName : p.FolderName.Trim();

            p.SeekerWeakPitch = ClampWithLog(fileName, "SeekerWeakPitch", p.SeekerWeakPitch, d.SeekerWeakPitch, 0.1f, 3f);
            p.SeekerStrongPitch = ClampWithLog(fileName, "SeekerStrongPitch", p.SeekerStrongPitch, d.SeekerStrongPitch, 0.1f, 3f);
            if (p.SeekerStrongPitch < p.SeekerWeakPitch) p.SeekerStrongPitch = p.SeekerWeakPitch;

            p.HeatPitchMin = ClampWithLog(fileName, "HeatPitchMin", p.HeatPitchMin, d.HeatPitchMin, 0f, 100f);
            p.HeatPitchMax = ClampWithLog(fileName, "HeatPitchMax", p.HeatPitchMax, d.HeatPitchMax, 0f, 200f);
            if (p.HeatPitchMax <= p.HeatPitchMin) p.HeatPitchMax = p.HeatPitchMin + 0.01f;
            p.HeatSensitivity = ClampWithLog(fileName, "HeatSensitivity", p.HeatSensitivity, d.HeatSensitivity, 0.01f, 10f);
            p.HeatCurvePower = ClampWithLog(fileName, "HeatCurvePower", p.HeatCurvePower, d.HeatCurvePower, 0.1f, 6f);
            p.HeatValidOnThreshold = ClampWithLog(fileName, "HeatValidOnThreshold", p.HeatValidOnThreshold, d.HeatValidOnThreshold, 0f, 100f);
            p.HeatValidOffThreshold = ClampWithLog(fileName, "HeatValidOffThreshold", p.HeatValidOffThreshold, d.HeatValidOffThreshold, 0f, 100f);
            if (p.HeatValidOffThreshold > p.HeatValidOnThreshold) p.HeatValidOffThreshold = p.HeatValidOnThreshold;

            p.TickInterval = ClampWithLog(fileName, "TickInterval", p.TickInterval, d.TickInterval, 0.01f, 0.5f);
            p.LockSmoothTime = ClampWithLog(fileName, "LockSmoothTime", p.LockSmoothTime, d.LockSmoothTime, 0f, 1f);
            p.NarrowFovDeg = ClampWithLog(fileName, "NarrowFovDeg", p.NarrowFovDeg, d.NarrowFovDeg, 0.5f, 160f);
            p.FrontConeDeg = ClampWithLog(fileName, "FrontConeDeg", p.FrontConeDeg, d.FrontConeDeg, 1f, 179.5f);
            p.SlewRateDegPerSec = ClampWithLog(fileName, "SlewRateDegPerSec", p.SlewRateDegPerSec, d.SlewRateDegPerSec, 1f, 900f);
            p.TrackErrorMinDeg = ClampWithLog(fileName, "TrackErrorMinDeg", p.TrackErrorMinDeg, d.TrackErrorMinDeg, 0.1f, 179f);
            p.TrackErrorMaxDeg = ClampWithLog(fileName, "TrackErrorMaxDeg", p.TrackErrorMaxDeg, d.TrackErrorMaxDeg, 0.2f, 179f);
            if (p.TrackErrorMaxDeg <= p.TrackErrorMinDeg) p.TrackErrorMaxDeg = p.TrackErrorMinDeg + 0.1f;

            p.BaseCagedVolume = ClampWithLog(fileName, "BaseCagedVolume", p.BaseCagedVolume, d.BaseCagedVolume, 0f, 1f);
            p.LockedCagedVolume = ClampWithLog(fileName, "LockedCagedVolume", p.LockedCagedVolume, d.LockedCagedVolume, 0f, 1f);
            p.LockUncagedMaxVolume = ClampWithLog(fileName, "LockUncagedMaxVolume", p.LockUncagedMaxVolume, d.LockUncagedMaxVolume, 0f, 1f);
            p.EnvOverlayMaxVolume = ClampWithLog(fileName, "EnvOverlayMaxVolume", p.EnvOverlayMaxVolume, d.EnvOverlayMaxVolume, 0f, 1f);
            p.EnvSwitchFadeTime = ClampWithLog(fileName, "EnvSwitchFadeTime", p.EnvSwitchFadeTime, d.EnvSwitchFadeTime, 0f, 2f);
            p.FlarePulseFadeInSeconds = ClampWithLog(fileName, "FlarePulseFadeInSeconds", p.FlarePulseFadeInSeconds, d.FlarePulseFadeInSeconds, 0f, 8f);
            p.FlarePulseFadeOutSeconds = ClampWithLog(fileName, "FlarePulseFadeOutSeconds", p.FlarePulseFadeOutSeconds, d.FlarePulseFadeOutSeconds, 0f, 8f);
            p.EnvProbeDistanceMeters = ClampWithLog(fileName, "EnvProbeDistanceMeters", p.EnvProbeDistanceMeters, d.EnvProbeDistanceMeters, 50f, 250000f);

            p.RangeCalcIntervalSeconds = ClampWithLog(fileName, "RangeCalcIntervalSeconds", p.RangeCalcIntervalSeconds, d.RangeCalcIntervalSeconds, 0.01f, 2f);
            p.RangeRecalcDistThresholdMeters = ClampWithLog(fileName, "RangeRecalcDistThresholdMeters", p.RangeRecalcDistThresholdMeters, d.RangeRecalcDistThresholdMeters, 1f, 10000f);
            p.RangeRecalcSpeedThreshold = ClampWithLog(fileName, "RangeRecalcSpeedThreshold", p.RangeRecalcSpeedThreshold, d.RangeRecalcSpeedThreshold, 0.1f, 1000f);
            p.RangeRecalcAltitudeThresholdMeters = ClampWithLog(fileName, "RangeRecalcAltitudeThresholdMeters", p.RangeRecalcAltitudeThresholdMeters, d.RangeRecalcAltitudeThresholdMeters, 1f, 10000f);
            p.RangeRecalcRelativeSpeedThreshold = ClampWithLog(fileName, "RangeRecalcRelativeSpeedThreshold", p.RangeRecalcRelativeSpeedThreshold, d.RangeRecalcRelativeSpeedThreshold, 0.1f, 1000f);
        }

        private static float ClampWithLog(string fileName, string fieldName, float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Plugin.Log.LogWarning($"[SeekerProfiles] '{fileName}' invalid {fieldName}; using fallback {fallback:0.###}.");
                return fallback;
            }

            float clamped = value < min ? min : (value > max ? max : value);
            if (clamped != value)
            {
                Plugin.Log.LogWarning($"[SeekerProfiles] '{fileName}' clamped {fieldName}: {value:0.###} -> {clamped:0.###} ({min:0.###}-{max:0.###}).");
            }

            return clamped;
        }

        private static string BuildProfileText(SeekerSoundProfile p)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("# BasicGrowl seeker profile");
            sb.AppendLine("# Loaded from same folder as com.basicGrowl.dll");
            sb.AppendLine("# Edit values, save file, and the mod will reload it automatically.");
            sb.AppendLine("# Format: Key=Value");
            sb.AppendLine();

            AppendField(sb, "PrefabName", p.PrefabName, "Internal missile ID. Must match the game's prefab name exactly (required).");
            AppendField(sb, "WeaponName", p.WeaponName, "Display name for logs/debug only.");
            AppendField(sb, "FolderName", p.FolderName, "Folder containing wav files. Relative to the plugin DLL.");
            AppendField(sb, "FlareReject", B(p.FlareReject), "true = flares do not drive heat pitch. false = flares can affect pitch.");
            AppendField(sb, "CagedFile", p.CagedFile, "Main caged growl wav file (required).");
            AppendField(sb, "UnCagedFile", p.UnCagedFile, "Uncaged growl wav file (optional).");
            AppendField(sb, "EnvSky", p.EnvSky, "Sky ambience wav file used when appropriate (optional).");
            AppendField(sb, "EnvGnd", p.EnvGnd, "Ground ambience wav file used when appropriate (optional).");

            AppendField(sb, "SeekerWeakPitch", F(p.SeekerWeakPitch), "Lowest pitch used when heat is weak.");
            AppendField(sb, "SeekerStrongPitch", F(p.SeekerStrongPitch), "Highest pitch used when heat is strong.");
            AppendField(sb, "HeatPitchMin", F(p.HeatPitchMin), "Heat level where pitch starts ramping from weak.");
            AppendField(sb, "HeatPitchMax", F(p.HeatPitchMax), "Heat level where pitch reaches strong.");
            AppendField(sb, "HeatSensitivity", F(p.HeatSensitivity), "How aggressively pitch reacts to heat changes.");
            AppendField(sb, "HeatCurvePower", F(p.HeatCurvePower), "Extra shape on pitch response. 1.0 is milder.");
            AppendField(sb, "HeatValidOnThreshold", F(p.HeatValidOnThreshold), "Heat must be at or above this to engage heat lock.");
            AppendField(sb, "HeatValidOffThreshold", F(p.HeatValidOffThreshold), "Heat must drop below this to disengage heat lock.");

            AppendField(sb, "TickInterval", F(p.TickInterval), "Update frequency in seconds. Lower = snappier, higher = lighter CPU.");
            AppendField(sb, "LockSmoothTime", F(p.LockSmoothTime), "Lock strength smoothing time. Higher = slower/smoother transitions.");
            AppendField(sb, "NarrowFovDeg", F(p.NarrowFovDeg), "Narrow tracking field-of-view angle (degrees).");
            AppendField(sb, "FrontConeDeg", F(p.FrontConeDeg), "Maximum front search cone angle (degrees).");
            AppendField(sb, "SlewRateDegPerSec", F(p.SlewRateDegPerSec), "How fast the seeker can turn (degrees per second).");
            AppendField(sb, "TrackErrorMinDeg", F(p.TrackErrorMinDeg), "Angular error where tracking quality is treated as best.");
            AppendField(sb, "TrackErrorMaxDeg", F(p.TrackErrorMaxDeg), "Angular error where tracking quality falls to zero.");

            AppendField(sb, "BaseCagedVolume", F(p.BaseCagedVolume), "Base caged volume when not locked (0 to 1).");
            AppendField(sb, "LockedCagedVolume", F(p.LockedCagedVolume), "Caged volume contribution near lock (0 to 1).");
            AppendField(sb, "LockUncagedMaxVolume", F(p.LockUncagedMaxVolume), "Maximum uncaged volume at strong lock (0 to 1).");
            AppendField(sb, "EnvOverlayMaxVolume", F(p.EnvOverlayMaxVolume), "Maximum environment overlay volume (0 to 1).");
            AppendField(sb, "EnvSwitchFadeTime", F(p.EnvSwitchFadeTime), "Crossfade time when switching environment clips (seconds).");
            AppendField(sb, "FlarePulseFadeInSeconds", F(p.FlarePulseFadeInSeconds), "Flare pulse fade-in duration (seconds).");
            AppendField(sb, "FlarePulseFadeOutSeconds", F(p.FlarePulseFadeOutSeconds), "Flare pulse fade-out duration (seconds).");
            AppendField(sb, "EnvProbeDistanceMeters", F(p.EnvProbeDistanceMeters), "How far to check for ground/sky environment context (meters).");

            AppendField(sb, "StrictHeatRangeGateWhenUnavailable", B(p.StrictHeatRangeGateWhenUnavailable), "If true, heat tone stays off when dynamic range cannot be computed.");
            AppendField(sb, "RangeCalcIntervalSeconds", F(p.RangeCalcIntervalSeconds), "Minimum time between dynamic range calculations.");
            AppendField(sb, "RangeRecalcDistThresholdMeters", F(p.RangeRecalcDistThresholdMeters), "Distance change that forces range recalculation.");
            AppendField(sb, "RangeRecalcSpeedThreshold", F(p.RangeRecalcSpeedThreshold), "Launcher speed change that forces range recalculation.");
            AppendField(sb, "RangeRecalcAltitudeThresholdMeters", F(p.RangeRecalcAltitudeThresholdMeters), "Altitude change that forces range recalculation.");
            AppendField(sb, "RangeRecalcRelativeSpeedThreshold", F(p.RangeRecalcRelativeSpeedThreshold), "Relative speed change that forces range recalculation.");
            return sb.ToString();
        }

        private static void AppendField(StringBuilder sb, string key, string value, string description)
        {
            sb.AppendLine("# " + (description ?? string.Empty));
            sb.AppendLine(key + "=" + (value ?? string.Empty));
            sb.AppendLine();
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string B(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
