using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using com.basicGrowl.Seeker;
using NuclearOption;

namespace com.basicGrowl.Services
{
    internal sealed partial class SeekerToneServices
    {
        private readonly Plugin _plugin;

        // ----------------------------
        // Tick / time
        // ----------------------------
        private float _nextTick;
        private float _lastTickTime;
        private float TickInterval = 0.05f; // 20 Hz (slew + audio feels responsive)

        // ----------------------------
        // Weapon/profile
        // ----------------------------
        private SeekerSoundProfile _activeProfile;
        private string _activePrefabName;
        private Missile _activeMissileForRange;
        private Aircraft _activeAircraftSpawn;
        private GameObject _cachedWeaponPrefab;
        private bool _cachedWeaponIsIr;
        private SeekerSoundProfile _cachedWeaponProfile;
        private Missile _cachedWeaponMissile;
        private string _cachedWeaponPrefabName;
        private float _activeWeaponStaticMaxRangeMeters;
        private string _inactiveStateKey;
        private int _profileRegistryVersion = -1;
        private float _nextRangeCalcTime;
        private bool _hasCachedShotRange;
        private float _cachedShotRangeMinMeters;
        private float _cachedShotRangeMaxMeters;
        private float _cachedRangeDistMeters;
        private float _cachedRangeLaunchSpeed;
        private float _cachedRangeLaunchAltitude;
        private float _cachedRangeTargetAltitude;
        private float _cachedRangeTargetRelativeSpeed;

        // ----------------------------
        // Target / seeker geometry
        // ----------------------------
        private Aircraft _target;
        private bool _hasTrackingAimPoint;
        private Vector3 _trackingAimPointWorld;

        private bool _hasSeekerDir;
        private Vector3 _seekerDirLocal = Vector3.forward; // aircraft-local (forward = Vector3.forward)
        private bool _hasValidHeatLock;

        private bool _inFrontCone;
        private bool _inFov;
        private bool _hasLos;

        // Smooth lock strength so audio doesn't chatter on terrain edges
        private float _lockStrength;
        private float _lockVel;
        private float LockSmoothTime = 0.08f;

        // ----------------------------
        // Audio
        // ----------------------------
        private readonly AudioSource _srcCaged;
        private readonly AudioSource _srcUncaged;
        private readonly AudioSource _srcEnvA;
        private readonly AudioSource _srcEnvB;

        private readonly Dictionary<string, AudioClip> _clipCache =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _loading =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _missing =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _audioLoadGeneration;
        private bool _disposed;

        private string _cagedClipFolder;
        private string _cagedClipFile;
        private string _cagedClipKey;
        private AudioClip _cagedClipCached;
        private string _uncagedClipFolder;
        private string _uncagedClipFile;
        private string _uncagedClipKey;
        private AudioClip _uncagedClipCached;
        private string _envClipFolder;
        private string _envClipFile;
        private string _envClipKey;
        private AudioClip _envClipCached;

        // ----------------------------
        // Seeker tuning (geometry)
        // ----------------------------
        private float NarrowFovDeg = 10f;   // full angle
        private float FrontConeDeg = 150f;  // full angle, "front only"
        private float SlewRateDegPerSec = 100f;
        private static readonly int LosLayerMask = ResolveLosLayerMask();
        private float NarrowFovHalfDeg = 5f;
        private float FrontConeHalfDeg = 75f;
        private float NarrowFovCos = Mathf.Cos(5f * Mathf.Deg2Rad);
        private float FrontConeCos = Mathf.Cos(75f * Mathf.Deg2Rad);

        // Tracking factor from physical seeker-line angular error (no time-delay filter).
        private float TrackErrorMinDeg = 2f;
        private float TrackErrorMaxDeg = 18f;

        // ----------------------------
        // Heat -> pitch mapping
        // ----------------------------
        // Heat sensitivity for pitch mapping.
        // Heat values at/below Min map to weak pitch, at/above Max map to strong pitch.
        // Sensitivity shapes response inside that range.
        private float HeatPitchMin = 2.0f;
        private float HeatPitchMax = 11.0f;
        private float HeatSensitivity = 2.25f;
        private float HeatCurvePower = 1.10f;
        private float HeatValidOnThreshold = 1.10f;
        private float HeatValidOffThreshold = 0.95f;

        // Always-on tone mix while IR weapon is selected.
        private float BaseCagedVolume = 0.70f;
        private float LockedCagedVolume = 0.35f;
        private float LockUncagedMaxVolume = 0.85f;
        private float EnvOverlayMaxVolume = 0.45f;
        private float EnvSwitchFadeTime = 0.20f;
        private float FlarePulseFadeInSeconds = 0.4f;
        private float FlarePulseFadeOutSeconds = 0.4f;
        private float EnvProbeDistanceMeters = 50000f;
        private bool StrictHeatRangeGateWhenUnavailable = true;
        private float RangeCalcIntervalSeconds = 0.20f;
        private float RangeRecalcDistThresholdMeters = 150f;
        private float RangeRecalcSpeedThreshold = 8f;
        private float RangeRecalcAltitudeThresholdMeters = 60f;
        private float RangeRecalcRelativeSpeedThreshold = 12f;

        // ----------------------------
        // Reflection: Unit has a private List<IRSource> IRSources
        // ----------------------------
        private static readonly FieldInfo IRSourcesField =
            typeof(Unit).GetField("IRSources", BindingFlags.NonPublic | BindingFlags.Instance);

        private static int ResolveLosLayerMask()
        {
            int mask = LayerMask.GetMask("Statics");
            return mask != 0 ? mask : (1 << 6);
        }

        // Debug
        private static readonly bool DebugDraw = false;
        private bool EnableStateTransitionLog => _plugin != null && _plugin.EnableSeekerStateTransitions;
        private bool EnableTickDiagnosticsLog => _plugin != null && _plugin.EnableSeekerTickDiagnostics;
        private float _nextStatusLog;
        private bool _overlayVisible;
        private Vector3 _overlayOriginWorld;
        private Vector3 _overlaySeekerDirWorld = Vector3.forward;
        private bool _debugHasTarget;
        private bool _debugTargetTrackedByHq;
        private float _debugTrackFactor;
        private float _debugHeat;
        private float _debugHeat01;
        private float _debugPitch;
        private bool _debugFlareDetected;
        private float _debugFlareHeat;
        private float _debugFlarePulse01;
        private float _debugFlarePeriodSeconds;
        private string _debugEnvFileWanted = "<none>";
        private float _debugCagedTargetVol;
        private float _debugUncagedTargetVol;
        private float _debugEnvTargetVol;
        private float _flarePulse01;
        private bool _flarePulseRising = true;
        private bool _hasLoggedStateSnapshot;
        private bool _lastLoggedHasTarget;
        private bool _lastLoggedTargetTrackedByHq;
        private bool _lastLoggedInFrontCone;
        private bool _lastLoggedInFov;
        private bool _lastLoggedHasLos;
        private bool _lastLoggedWithinHeatRange;
        private string _lastLoggedTargetName = "<none>";

        public SeekerToneServices(Plugin plugin)
        {
            _plugin = plugin;

            _srcCaged = plugin.gameObject.AddComponent<AudioSource>();
            _srcCaged.playOnAwake = false;
            _srcCaged.loop = true;
            _srcCaged.spatialBlend = 0f;

            _srcUncaged = plugin.gameObject.AddComponent<AudioSource>();
            _srcUncaged.playOnAwake = false;
            _srcUncaged.loop = true;
            _srcUncaged.spatialBlend = 0f;

            _srcEnvA = plugin.gameObject.AddComponent<AudioSource>();
            _srcEnvA.playOnAwake = false;
            _srcEnvA.loop = true;
            _srcEnvA.spatialBlend = 0f;

            _srcEnvB = plugin.gameObject.AddComponent<AudioSource>();
            _srcEnvB.playOnAwake = false;
            _srcEnvB.loop = true;
            _srcEnvB.spatialBlend = 0f;

            ApplyRuntimeTuning(new SeekerSoundProfile());
        }

        internal bool TryGetOverlayState(out Vector3 originWorld, out Vector3 seekerDirWorld)
        {
            originWorld = _overlayOriginWorld;
            seekerDirWorld = _overlaySeekerDirWorld;

            if (_activeAircraftSpawn != null && _hasSeekerDir)
            {
                originWorld = _activeAircraftSpawn.transform.position;
                seekerDirWorld = _activeAircraftSpawn.transform.TransformDirection(_seekerDirLocal).normalized;
            }

            return IsOverlayActive;
        }

        internal bool IsOverlayActive => _overlayVisible && _activeProfile != null;

        internal void RefreshProfiles()
        {
            _profileRegistryVersion = SeekerProfileRegistry.Version;
            ResetWeaponCache();
            ClearActiveProfile();
            ResetSeekerState(keepDir: true);
            StopTone();
            ReleaseAudioClips();
        }

        internal void GetDebugState(
            out bool overlayVisible,
            out bool hasTarget,
            out bool targetTrackedByHq,
            out bool inFrontCone,
            out bool inFov,
            out bool hasLos,
            out float lockStrength,
            out float trackFactor,
            out float heat,
            out float heat01,
            out float pitch,
            out bool flareDetected,
            out float flareHeat,
            out float flarePulse01,
            out float flarePeriodSeconds,
            out string profileName)
        {
            overlayVisible = _overlayVisible;
            hasTarget = _debugHasTarget;
            targetTrackedByHq = _debugTargetTrackedByHq;
            inFrontCone = _inFrontCone;
            inFov = _inFov;
            hasLos = _hasLos;
            lockStrength = _lockStrength;
            trackFactor = _debugTrackFactor;
            heat = _debugHeat;
            heat01 = _debugHeat01;
            pitch = _debugPitch;
            flareDetected = _debugFlareDetected;
            flareHeat = _debugFlareHeat;
            flarePulse01 = _debugFlarePulse01;
            flarePeriodSeconds = _debugFlarePeriodSeconds;
            profileName = _activeProfile != null ? _activeProfile.PrefabName : "<none>";
        }

        internal void GetAudioDebugState(
            out string envFileWanted,
            out float cagedTargetVol,
            out float uncagedTargetVol,
            out float envTargetVol,
            out string cagedState,
            out string uncagedState,
            out string envAState,
            out string envBState)
        {
            envFileWanted = _debugEnvFileWanted;
            cagedTargetVol = _debugCagedTargetVol;
            uncagedTargetVol = _debugUncagedTargetVol;
            envTargetVol = _debugEnvTargetVol;
            cagedState = FormatAudioSourceState(_srcCaged);
            uncagedState = FormatAudioSourceState(_srcUncaged);
            envAState = FormatAudioSourceState(_srcEnvA);
            envBState = FormatAudioSourceState(_srcEnvB);
        }

        public void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;

            float now = Time.unscaledTime;
            float dt = (_lastTickTime > 0f) ? Mathf.Clamp(now - _lastTickTime, 0.0f, 0.2f) : TickInterval;
            _lastTickTime = now;
            _nextTick = now + TickInterval;

            // HUD + local aircraft gate
            if (SceneSingleton<CombatHUD>.i == null ||
                !GameManager.GetLocalAircraft(out var ac) || ac == null)
            {
                EnterInactiveState("no-aircraft", resetWeaponCache: true, clearProfile: true, clearAircraft: true);
                return;
            }

            if (!ReferenceEquals(ac, _activeAircraftSpawn))
            {
                _activeAircraftSpawn = ac;
                ResetWeaponCache();
                ClearActiveProfile();
                ResetSeekerState();
                StopTone();
            }

            var hud = SceneSingleton<CombatHUD>.i;

            // Weapon station + ammo gate
            var ws = hud.GetWeaponStation();
            if (ws == null || ws.WeaponInfo == null || ws.Ammo <= 0)
            {
                EnterInactiveState("no-weapon", keepDir: true);
                return;
            }

            var prefab = ws.WeaponInfo.weaponPrefab;
            if (prefab == null)
            {
                EnterInactiveState("no-prefab", keepDir: true);
                return;
            }

            _activeWeaponStaticMaxRangeMeters = ws.WeaponInfo.targetRequirements.maxRange;

            if (_profileRegistryVersion != SeekerProfileRegistry.Version)
            {
                _profileRegistryVersion = SeekerProfileRegistry.Version;
                ResetWeaponCache();
                ClearActiveProfile();
                StopTone();
            }

            if (!ReferenceEquals(prefab, _cachedWeaponPrefab))
                ResolveWeaponPrefab(prefab);

            // IR weapon gate
            if (!_cachedWeaponIsIr)
            {
                EnterInactiveState("non-ir:" + _cachedWeaponPrefabName, keepDir: true, clearProfile: true);
                return;
            }

            // Resolve profile by prefab name
            var prefabName = _cachedWeaponPrefabName ?? "";
            if (!string.Equals(prefabName, _activePrefabName, StringComparison.Ordinal) || _activeProfile == null)
            {
                if (_cachedWeaponProfile == null)
                {
                    EnterInactiveState("missing-profile:" + prefabName, keepDir: true, clearProfile: true);
                    return;
                }

                _inactiveStateKey = null;

                _activePrefabName = prefabName;
                _activeProfile = _cachedWeaponProfile;
                ApplyRuntimeTuning(_activeProfile);
                _activeMissileForRange = _cachedWeaponMissile;
                InvalidateRangeCache();
                ClearActiveClipSlots();
                PrimeProfileClips(_activeProfile);

                Plugin.Log.LogInfo($"Active seeker profile: '{_activeProfile.PrefabName}' ({_activeProfile.WeaponName})");
                if (_activeMissileForRange != null)
                    Plugin.Log.LogInfo("[Seeker] Per-shot heat range gate enabled (CalcRange).");
                else
                    Plugin.Log.LogWarning("[Seeker] Per-shot heat range gate unavailable (no missile component found).");
                StopTone(); // hard reset on profile switch
            }

            _inactiveStateKey = null;

            // Target policy:
            // - use the HUD's primary selected target entry
            // - if that selected target is not an aircraft, keep base caged tone
            var targets = hud.GetTargetList();
            Aircraft targetAircraft = FindSelectedAircraftTarget(targets);

            // Update current target reference
            if (!ReferenceEquals(targetAircraft, _target))
            {
                _target = targetAircraft;
                _hasTrackingAimPoint = false;
                _hasValidHeatLock = false;
                _hasLos = false;
                _inFov = false;
                _inFrontCone = false;

                // New target should reacquire smoothly from low lock.
                if (_target != null)
                {
                    _lockStrength = 0f;
                    Plugin.Log.LogInfo($"[Seeker] Target: {_target.name}");
                }
                else
                {
                    Plugin.Log.LogInfo("[Seeker] Target: <none>");
                }

                _lockVel = 0f;
                InvalidateRangeCache();
            }

            // Initialize seeker direction if needed
            if (!_hasSeekerDir)
            {
                _seekerDirLocal = Vector3.forward;
                _hasSeekerDir = true;
            }

            // Seeker origin (simple for now)
            Vector3 origin = ac.transform.position;
            float dist = -1f;
            float heat = 0f;
            float trackFactor = 0f;
            bool flarePulseCandidate = false;
            float targetFlareHeat = 0f;
            float targetNonFlareHeat = 0f;
            bool hasTarget = _target != null;
            bool targetTrackedByHq = false;
            bool hasTrackingEntry = false;
            bool trackingObserved = false;
            bool usingTrackingAimPoint = false;
            float trackingDeltaMeters = -1f;
            string targetName = hasTarget ? _target.name : "<none>";
            float shotRangeMinMeters = -1f;
            float shotRangeMaxMeters = -1f;
            bool hasShotRange = false;
            bool withinHeatRange = false;
            float heatRaw = 0f;
            bool hasValidHeatSource = false;
            bool flareRejectEnabled = _activeProfile == null || _activeProfile.FlareReject;

            if (hasTarget)
            {
                TryGetHqTrackingSnapshot(
                    ac,
                    _target,
                    out targetTrackedByHq,
                    out hasTrackingEntry,
                    out trackingObserved,
                    out var snapshotAimWorld,
                    out var hasSnapshotAimWorld);

                // Keep steering on last known HQ position.
                // Tracking "observed" can drop to false while the HQ snapshot remains valid.
                if (hasTrackingEntry && hasSnapshotAimWorld)
                {
                    _hasTrackingAimPoint = true;
                    _trackingAimPointWorld = snapshotAimWorld;
                }

                usingTrackingAimPoint = _hasTrackingAimPoint;
                if (usingTrackingAimPoint)
                {
                    // Direction/distance to HQ tracking aim point (never live target position).
                    Vector3 toTargetWorld = (_trackingAimPointWorld - origin);
                    dist = toTargetWorld.magnitude;

                    // Compute lock gating (front cone + narrow FOV + LOS) against HQ tracking aim point.
                    ComputeGeometryAndLos(ac, origin, _trackingAimPointWorld, toTargetWorld, dist, dt);

                    bool lockNow = (_inFrontCone && _inFov && _hasLos);
                    _lockStrength = Mathf.SmoothDamp(_lockStrength, lockNow ? 1f : 0f, ref _lockVel, LockSmoothTime, Mathf.Infinity, dt);

                    // Physical seeker tracking factor from angular error between seeker line and HQ track line.
                    trackFactor = GetTrackFactor(ac, toTargetWorld, dist);

                    hasShotRange = TryGetCurrentShotRangeMeters(ac, _target, dist, out shotRangeMinMeters, out shotRangeMaxMeters);
                    withinHeatRange = ResolveWithinHeatRange(hasShotRange, dist, shotRangeMinMeters, shotRangeMaxMeters);

                    try
                    {
                        trackingDeltaMeters = Vector3.Distance(_target.transform.position, _trackingAimPointWorld);
                    }
                    catch
                    {
                        trackingDeltaMeters = -1f;
                    }
                }
                else
                {
                    // No HQ tracking aim available yet: seeker cannot steer to live target position.
                    SlewTowardLocal(Vector3.forward, dt);
                    _inFrontCone = false;
                    _inFov = false;
                    _hasLos = false;
                    _lockStrength = Mathf.SmoothDamp(_lockStrength, 0f, ref _lockVel, LockSmoothTime, Mathf.Infinity, dt);
                }

                // Flare pulse trigger:
                // - target flares visible in seeker FOV
                // - flare heat exceeds target non-flare heat
                if (targetTrackedByHq && usingTrackingAimPoint && _hasLos && withinHeatRange)
                {
                    SampleTargetHeatAndFlare(
                        ac,
                        origin,
                        _target,
                        out targetNonFlareHeat,
                        out var targetFlaringInFov,
                        out targetFlareHeat);
                    flarePulseCandidate = flareRejectEnabled && targetFlaringInFov && (targetFlareHeat > targetNonFlareHeat);
                }
                else
                {
                    targetFlareHeat = 0f;
                    targetNonFlareHeat = 0f;
                    flarePulseCandidate = false;
                }

                // Heat follows physical tracking and respects both LOS + per-shot range.
                // FlareReject=true: flares do not drive seeker pitch.
                // FlareReject=false: flare heat can drive seeker pitch.
                float heatCandidate = flareRejectEnabled
                    ? targetNonFlareHeat
                    : Mathf.Max(targetNonFlareHeat, targetFlareHeat);

                float sampledHeatRaw = (targetTrackedByHq && _hasLos && withinHeatRange && usingTrackingAimPoint)
                    ? heatCandidate
                    : 0f;

                // Heat validity gate:
                // - engage above 1.10
                // - release below 0.95
                float heatGateThreshold = _hasValidHeatLock ? HeatValidOffThreshold : HeatValidOnThreshold;
                hasValidHeatSource = sampledHeatRaw >= heatGateThreshold;
                _hasValidHeatLock = hasValidHeatSource;

                heatRaw = hasValidHeatSource ? sampledHeatRaw : 0f;
                heat = heatRaw * trackFactor;

                if (usingTrackingAimPoint)
                    DebugViz(origin, ac, _trackingAimPointWorld);
            }
            else
            {
                // No target: smoothly return seeker toward boresight and keep base caged tone playing.
                SlewTowardLocal(Vector3.forward, dt);
                _inFrontCone = false;
                _inFov = false;
                _hasLos = false;
                _lockStrength = Mathf.SmoothDamp(_lockStrength, 0f, ref _lockVel, LockSmoothTime, Mathf.Infinity, dt);
                _hasTrackingAimPoint = false;
                _hasValidHeatLock = false;
            }

            _overlayVisible = true;
            _overlayOriginWorld = origin;
            _overlaySeekerDirWorld = ac.transform.TransformDirection(_seekerDirLocal).normalized;

            // Heat -> [0..1] with adjustable sensitivity in the profile heat range.
            float heat01Linear = Mathf.InverseLerp(HeatPitchMin, HeatPitchMax, heat);
            float heatResponse = 1f - Mathf.Exp(-HeatSensitivity * heat01Linear);
            float heatResponseDen = 1f - Mathf.Exp(-HeatSensitivity);
            float heat01 = (heatResponseDen > 0.0001f) ? (heatResponse / heatResponseDen) : heat01Linear;
            heat01 = Mathf.Pow(Mathf.Clamp01(heat01), HeatCurvePower);
            float cagedPitch = Mathf.Lerp(_activeProfile.SeekerWeakPitch, _activeProfile.SeekerStrongPitch, heat01);

            // Blend by lock only. Use equal-power crossfade and snap the top end so
            // caged cannot bleed when uncaged is effectively full.
            bool hasUncaged = !string.IsNullOrEmpty(_activeProfile.UnCagedFile);
            float rangeGate = (withinHeatRange && hasValidHeatSource) ? 1f : 0f;
            float uncagedBlend = hasUncaged ? Mathf.Clamp01(_lockStrength * rangeGate) : 0f;
            if (uncagedBlend > 0.995f) uncagedBlend = 1f;

            float cagedFade = Mathf.Cos(uncagedBlend * Mathf.PI * 0.5f);
            float uncagedFade = Mathf.Sin(uncagedBlend * Mathf.PI * 0.5f);

            float uncagedVol = uncagedFade * LockUncagedMaxVolume;
            float cagedBaseVol = Mathf.Lerp(BaseCagedVolume, LockedCagedVolume, uncagedBlend);
            float cagedVol = cagedBaseVol * cagedFade;
            if (!flarePulseCandidate && uncagedBlend >= 1f) cagedVol = 0f;

            bool flarePulseActive = flarePulseCandidate && withinHeatRange;
            float flarePulse01 = GetFlarePulse01(flarePulseActive, dt);
            cagedVol = Mathf.Lerp(cagedVol, 1f, flarePulse01);

            float cagedPitchOut = Mathf.Lerp(cagedPitch, 1f, uncagedBlend);

            string envFile = null;
            float envVol = 0f;
            bool targetInSightFov = hasTarget && _inFov && _hasLos;
            bool useEnvOverlay = hasTarget && (!targetInSightFov || !hasValidHeatSource);
            if (useEnvOverlay && TryGetEnvOverlayFile(ac, origin, forceSky: !withinHeatRange, out var envCandidate))
            {
                envFile = envCandidate;
                envVol = Mathf.Clamp01((1f - _lockStrength) * EnvOverlayMaxVolume);
            }

            _debugHasTarget = hasTarget;
            _debugTargetTrackedByHq = targetTrackedByHq;
            _debugTrackFactor = trackFactor;
            _debugHeat = heat;
            _debugHeat01 = heat01;
            _debugPitch = cagedPitchOut;
            _debugFlareDetected = flarePulseActive;
            _debugFlareHeat = targetFlareHeat;
            _debugFlarePulse01 = flarePulse01;
            _debugFlarePeriodSeconds = (flarePulseActive || flarePulse01 > 0.001f)
                ? (FlarePulseFadeInSeconds + FlarePulseFadeOutSeconds)
                : 0f;
            _debugEnvFileWanted = string.IsNullOrEmpty(envFile) ? "<none>" : envFile;
            _debugCagedTargetVol = cagedVol;
            _debugUncagedTargetVol = uncagedVol;
            _debugEnvTargetVol = envVol;

            MaybeLogStateTransitions(hasTarget, targetName, targetTrackedByHq, withinHeatRange);

            ApplyTone(
                folder: _activeProfile.FolderName,
                cagedFile: _activeProfile.CagedFile,
                uncagedFile: _activeProfile.UnCagedFile,
                envFile: envFile,
                pitch: cagedPitchOut,
                cagedVol: cagedVol,
                uncagedVol: uncagedVol,
                envVol: envVol,
                bypassEnvCagedDuck: flarePulseActive,
                dt: dt
            );

            MaybeStatusLog(
                ownAircraft: ac,
                hasTarget: hasTarget,
                targetName: targetName,
                targetTrackedByHq: targetTrackedByHq,
                hasTrackingEntry: hasTrackingEntry,
                trackingObserved: trackingObserved,
                usingTrackingAimPoint: usingTrackingAimPoint,
                trackingDeltaMeters: trackingDeltaMeters,
                distMeters: dist,
                hasShotRange: hasShotRange,
                shotRangeMinMeters: shotRangeMinMeters,
                shotRangeMaxMeters: shotRangeMaxMeters,
                withinHeatRange: withinHeatRange,
                heatRaw: heatRaw,
                hasValidHeatSource: hasValidHeatSource,
                heat: heat,
                heat01: heat01,
                pitch: cagedPitchOut,
                trackFactor: trackFactor,
                targetFlareHeat: targetFlareHeat,
                targetNonFlareHeat: targetNonFlareHeat);
        }

        private void ResetSeekerState(bool keepDir = false)
        {
            _target = null;
            _hasTrackingAimPoint = false;
            _hasValidHeatLock = false;
            _trackingAimPointWorld = Vector3.zero;
            _inFrontCone = false;
            _inFov = false;
            _hasLos = false;
            _overlayVisible = false;

            _lockStrength = 0f;
            _lockVel = 0f;
            _debugHasTarget = false;
            _debugTargetTrackedByHq = false;
            _debugTrackFactor = 0f;
            _debugHeat = 0f;
            _debugHeat01 = 0f;
            _debugPitch = 1f;
            _debugFlareDetected = false;
            _debugFlareHeat = 0f;
            _debugFlarePulse01 = 0f;
            _debugFlarePeriodSeconds = 0f;
            _debugEnvFileWanted = "<none>";
            _debugCagedTargetVol = 0f;
            _debugUncagedTargetVol = 0f;
            _debugEnvTargetVol = 0f;
            _flarePulse01 = 0f;
            _flarePulseRising = true;
            _hasLoggedStateSnapshot = false;
            _lastLoggedTargetName = "<none>";
            InvalidateRangeCache();

            if (!keepDir)
            {
                _hasSeekerDir = false;
                _seekerDirLocal = Vector3.forward;
            }
        }

        private void ClearActiveProfile()
        {
            _activeProfile = null;
            _activePrefabName = null;
            _activeMissileForRange = null;
            _activeWeaponStaticMaxRangeMeters = 0f;
            ClearActiveClipSlots();
            ApplyRuntimeTuning(new SeekerSoundProfile());
            InvalidateRangeCache();
        }

        private void EnterInactiveState(
            string stateKey,
            bool keepDir = false,
            bool resetWeaponCache = false,
            bool clearProfile = false,
            bool clearAircraft = false)
        {
            if (string.Equals(_inactiveStateKey, stateKey, StringComparison.Ordinal))
                return;

            _inactiveStateKey = stateKey;

            if (clearAircraft)
                _activeAircraftSpawn = null;
            if (resetWeaponCache)
                ResetWeaponCache();
            if (clearProfile)
                ClearActiveProfile();

            ResetSeekerState(keepDir);
            StopTone();
        }

        private void ResetWeaponCache()
        {
            _cachedWeaponPrefab = null;
            _cachedWeaponIsIr = false;
            _cachedWeaponProfile = null;
            _cachedWeaponMissile = null;
            _cachedWeaponPrefabName = null;
        }

        private void ResolveWeaponPrefab(GameObject prefab)
        {
            ResetWeaponCache();
            _cachedWeaponPrefab = prefab;
            if (prefab == null)
                return;

            _cachedWeaponPrefabName = prefab.name ?? string.Empty;

            try
            {
                _cachedWeaponIsIr = prefab.GetComponentInChildren<IRSeeker>(true) != null;
            }
            catch
            {
                _cachedWeaponIsIr = false;
            }

            if (!_cachedWeaponIsIr)
                return;

            SeekerProfileRegistry.TryGetByPrefab(_cachedWeaponPrefabName, out _cachedWeaponProfile);
            _cachedWeaponMissile = ResolveActiveMissileForRange(prefab);
        }

        private bool ResolveWithinHeatRange(bool hasShotRange, float dist, float shotRangeMinMeters, float shotRangeMaxMeters)
        {
            if (!hasShotRange)
                return !StrictHeatRangeGateWhenUnavailable;

            return dist >= shotRangeMinMeters && dist <= shotRangeMaxMeters;
        }

        private void ApplyRuntimeTuning(SeekerSoundProfile profile)
        {
            if (profile == null) profile = new SeekerSoundProfile();
            string context = string.IsNullOrWhiteSpace(profile.PrefabName) ? "<defaults>" : profile.PrefabName;

            TickInterval = ClampProfileValue(context, "TickInterval", profile.TickInterval, 0.01f, 0.5f, 0.05f);
            LockSmoothTime = ClampProfileValue(context, "LockSmoothTime", profile.LockSmoothTime, 0f, 1f, 0.08f);

            NarrowFovDeg = ClampProfileValue(context, "NarrowFovDeg", profile.NarrowFovDeg, 0.5f, 160f, 10f);
            FrontConeDeg = ClampProfileValue(context, "FrontConeDeg", profile.FrontConeDeg, 1f, 179.5f, 150f);
            SlewRateDegPerSec = ClampProfileValue(context, "SlewRateDegPerSec", profile.SlewRateDegPerSec, 1f, 900f, 100f);

            NarrowFovHalfDeg = NarrowFovDeg * 0.5f;
            FrontConeHalfDeg = FrontConeDeg * 0.5f;
            NarrowFovCos = Mathf.Cos(NarrowFovHalfDeg * Mathf.Deg2Rad);
            FrontConeCos = Mathf.Cos(FrontConeHalfDeg * Mathf.Deg2Rad);

            TrackErrorMinDeg = ClampProfileValue(context, "TrackErrorMinDeg", profile.TrackErrorMinDeg, 0.1f, 179f, 2f);
            TrackErrorMaxDeg = ClampProfileValue(context, "TrackErrorMaxDeg", profile.TrackErrorMaxDeg, 0.2f, 179f, 18f);
            if (TrackErrorMaxDeg <= TrackErrorMinDeg)
            {
                TrackErrorMaxDeg = TrackErrorMinDeg + 0.1f;
                Plugin.Log.LogWarning($"[SeekerProfiles] '{context}' adjusted TrackErrorMaxDeg to {TrackErrorMaxDeg:0.###} (must be > TrackErrorMinDeg).");
            }

            HeatPitchMin = ClampProfileValue(context, "HeatPitchMin", profile.HeatPitchMin, 0f, 100f, 2f);
            HeatPitchMax = ClampProfileValue(context, "HeatPitchMax", profile.HeatPitchMax, 0f, 200f, 11f);
            if (HeatPitchMax <= HeatPitchMin)
            {
                HeatPitchMax = HeatPitchMin + 0.01f;
                Plugin.Log.LogWarning($"[SeekerProfiles] '{context}' adjusted HeatPitchMax to {HeatPitchMax:0.###} (must be > HeatPitchMin).");
            }

            HeatSensitivity = ClampProfileValue(context, "HeatSensitivity", profile.HeatSensitivity, 0.01f, 10f, 2.25f);
            HeatCurvePower = ClampProfileValue(context, "HeatCurvePower", profile.HeatCurvePower, 0.1f, 6f, 1.1f);
            HeatValidOnThreshold = ClampProfileValue(context, "HeatValidOnThreshold", profile.HeatValidOnThreshold, 0f, 100f, 1.1f);
            HeatValidOffThreshold = ClampProfileValue(context, "HeatValidOffThreshold", profile.HeatValidOffThreshold, 0f, 100f, 0.95f);
            if (HeatValidOffThreshold > HeatValidOnThreshold)
            {
                HeatValidOffThreshold = HeatValidOnThreshold;
                Plugin.Log.LogWarning($"[SeekerProfiles] '{context}' adjusted HeatValidOffThreshold to {HeatValidOffThreshold:0.###} (must be <= HeatValidOnThreshold).");
            }

            BaseCagedVolume = ClampProfileValue(context, "BaseCagedVolume", profile.BaseCagedVolume, 0f, 1f, 0.70f);
            LockedCagedVolume = ClampProfileValue(context, "LockedCagedVolume", profile.LockedCagedVolume, 0f, 1f, 0.35f);
            LockUncagedMaxVolume = ClampProfileValue(context, "LockUncagedMaxVolume", profile.LockUncagedMaxVolume, 0f, 1f, 0.85f);
            EnvOverlayMaxVolume = ClampProfileValue(context, "EnvOverlayMaxVolume", profile.EnvOverlayMaxVolume, 0f, 1f, 0.45f);
            EnvSwitchFadeTime = ClampProfileValue(context, "EnvSwitchFadeTime", profile.EnvSwitchFadeTime, 0f, 2f, 0.2f);
            FlarePulseFadeInSeconds = ClampProfileValue(context, "FlarePulseFadeInSeconds", profile.FlarePulseFadeInSeconds, 0f, 8f, 0.4f);
            FlarePulseFadeOutSeconds = ClampProfileValue(context, "FlarePulseFadeOutSeconds", profile.FlarePulseFadeOutSeconds, 0f, 8f, 0.4f);
            EnvProbeDistanceMeters = ClampProfileValue(context, "EnvProbeDistanceMeters", profile.EnvProbeDistanceMeters, 50f, 250000f, 50000f);

            StrictHeatRangeGateWhenUnavailable = profile.StrictHeatRangeGateWhenUnavailable;
            RangeCalcIntervalSeconds = ClampProfileValue(context, "RangeCalcIntervalSeconds", profile.RangeCalcIntervalSeconds, 0.01f, 2f, 0.2f);
            RangeRecalcDistThresholdMeters = ClampProfileValue(context, "RangeRecalcDistThresholdMeters", profile.RangeRecalcDistThresholdMeters, 1f, 10000f, 150f);
            RangeRecalcSpeedThreshold = ClampProfileValue(context, "RangeRecalcSpeedThreshold", profile.RangeRecalcSpeedThreshold, 0.1f, 1000f, 8f);
            RangeRecalcAltitudeThresholdMeters = ClampProfileValue(context, "RangeRecalcAltitudeThresholdMeters", profile.RangeRecalcAltitudeThresholdMeters, 1f, 10000f, 60f);
            RangeRecalcRelativeSpeedThreshold = ClampProfileValue(context, "RangeRecalcRelativeSpeedThreshold", profile.RangeRecalcRelativeSpeedThreshold, 0.1f, 1000f, 12f);
        }

        private static float ClampProfileValue(string profileName, string fieldName, float value, float min, float max, float fallback)
        {
            float original = value;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Plugin.Log.LogWarning($"[SeekerProfiles] '{profileName}' invalid {fieldName}. Using fallback {fallback:0.###}.");
                value = fallback;
            }

            float clamped = Mathf.Clamp(value, min, max);
            if (!Mathf.Approximately(clamped, original))
            {
                Plugin.Log.LogWarning($"[SeekerProfiles] '{profileName}' clamped {fieldName}: {original:0.###} -> {clamped:0.###} (range {min:0.###}-{max:0.###}).");
            }

            return clamped;
        }

        private static Aircraft FindSelectedAircraftTarget(IList targets)
        {
            if (targets == null || targets.Count == 0)
                return null;

            return targets[0] as Aircraft;
        }

    }
}
