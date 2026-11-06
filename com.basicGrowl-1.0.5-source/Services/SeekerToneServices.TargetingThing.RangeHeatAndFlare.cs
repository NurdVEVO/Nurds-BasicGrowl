using System;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption;

namespace com.basicGrowl.Services
{
    internal sealed partial class SeekerToneServices
    {
        // ----------------------------
        // Heat signature from IRSource intensity
        // ----------------------------
        private static Missile ResolveActiveMissileForRange(GameObject weaponPrefab)
        {
            if (weaponPrefab == null) return null;

            try
            {
                var missile = weaponPrefab.GetComponentInChildren<Missile>(true);
                return missile;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetCurrentShotRangeMeters(
            Aircraft launcher,
            Aircraft target,
            float targetDistMeters,
            out float shotRangeMinMeters,
            out float shotRangeMaxMeters)
        {
            shotRangeMinMeters = -1f;
            shotRangeMaxMeters = -1f;
            if (_activeMissileForRange == null || launcher == null || target == null) return false;

            float launchSpeed = GetUnitSpeed(launcher);
            float launchAltitude = launcher.transform.position.y;
            float targetAltitude = target.transform.position.y;
            float targetRelativeSpeed = GetRelativeTargetSpeedAlongLos(launcher, target, targetDistMeters);

            if (_hasCachedShotRange)
            {
                bool cacheExpired = Time.unscaledTime >= _nextRangeCalcTime;
                bool rangeInputsChanged = IsRangeInputDeltaLarge(
                    targetDistMeters,
                    launchSpeed,
                    launchAltitude,
                    targetAltitude,
                    targetRelativeSpeed);

                if (!cacheExpired && !rangeInputsChanged)
                {
                    shotRangeMinMeters = _cachedShotRangeMinMeters;
                    shotRangeMaxMeters = _cachedShotRangeMaxMeters;
                    return true;
                }
            }

            try
            {
                float noEscapeDistance;
                float dynamicMax = _activeMissileForRange.CalcRange(
                    launchSpeed,
                    launchAltitude,
                    targetAltitude,
                    targetDistMeters,
                    targetRelativeSpeed,
                    out noEscapeDistance);

                // True min range should come from missile min-range model, not no-escape distance.
                float min = _activeMissileForRange.GetMinRange(launchSpeed);
                if (float.IsNaN(min) || float.IsInfinity(min) || min < 0f)
                    min = 0f;

                float max = dynamicMax;
                if (float.IsNaN(max) || float.IsInfinity(max) || max <= 1f)
                {
                    // Fallback to the selected weapon's static targeting range.
                    max = _activeWeaponStaticMaxRangeMeters;
                }

                if (float.IsNaN(max) || float.IsInfinity(max) || max <= 1f)
                {
                    _nextRangeCalcTime = Time.unscaledTime + RangeCalcIntervalSeconds;
                    return false;
                }

                if (min > max)
                    min = max;

                shotRangeMinMeters = min;
                shotRangeMaxMeters = max;

                _hasCachedShotRange = true;
                _cachedShotRangeMinMeters = min;
                _cachedShotRangeMaxMeters = max;
                _cachedRangeDistMeters = targetDistMeters;
                _cachedRangeLaunchSpeed = launchSpeed;
                _cachedRangeLaunchAltitude = launchAltitude;
                _cachedRangeTargetAltitude = targetAltitude;
                _cachedRangeTargetRelativeSpeed = targetRelativeSpeed;
                _nextRangeCalcTime = Time.unscaledTime + RangeCalcIntervalSeconds;
                return true;
            }
            catch
            {
                _nextRangeCalcTime = Time.unscaledTime + RangeCalcIntervalSeconds;
                return false;
            }
        }

        private static float GetUnitSpeed(Unit unit)
        {
            if (unit == null || unit.rb == null) return 0f;
            return unit.rb.velocity.magnitude;
        }

        private static float GetRelativeTargetSpeedAlongLos(Aircraft launcher, Aircraft target, float targetDistMeters)
        {
            if (launcher == null || target == null || targetDistMeters <= 0.001f) return 0f;

            Vector3 launcherVel = launcher.rb != null ? launcher.rb.velocity : Vector3.zero;
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 losDir = (target.transform.position - launcher.transform.position) / targetDistMeters;

            // Positive = opening, negative = closing.
            return Vector3.Dot(targetVel - launcherVel, losDir);
        }

        private bool IsRangeInputDeltaLarge(
            float targetDistMeters,
            float launchSpeed,
            float launchAltitude,
            float targetAltitude,
            float targetRelativeSpeed)
        {
            if (Mathf.Abs(targetDistMeters - _cachedRangeDistMeters) >= RangeRecalcDistThresholdMeters)
                return true;

            if (Mathf.Abs(launchSpeed - _cachedRangeLaunchSpeed) >= RangeRecalcSpeedThreshold)
                return true;

            if (Mathf.Abs(launchAltitude - _cachedRangeLaunchAltitude) >= RangeRecalcAltitudeThresholdMeters)
                return true;

            if (Mathf.Abs(targetAltitude - _cachedRangeTargetAltitude) >= RangeRecalcAltitudeThresholdMeters)
                return true;

            if (Mathf.Abs(targetRelativeSpeed - _cachedRangeTargetRelativeSpeed) >= RangeRecalcRelativeSpeedThreshold)
                return true;

            return false;
        }

        private void InvalidateRangeCache()
        {
            _hasCachedShotRange = false;
            _cachedShotRangeMinMeters = 0f;
            _cachedShotRangeMaxMeters = 0f;
            _cachedRangeDistMeters = 0f;
            _cachedRangeLaunchSpeed = 0f;
            _cachedRangeLaunchAltitude = 0f;
            _cachedRangeTargetAltitude = 0f;
            _cachedRangeTargetRelativeSpeed = 0f;
            _nextRangeCalcTime = 0f;
        }

        private bool TryGetEnvOverlayFile(Aircraft ac, Vector3 origin, bool forceSky, out string envFile)
        {
            envFile = null;
            if (ac == null || _activeProfile == null) return false;
            if (!_hasSeekerDir) return false;

            if (forceSky)
            {
                if (string.IsNullOrEmpty(_activeProfile.EnvSky)) return false;
                envFile = _activeProfile.EnvSky;
                return true;
            }

            Vector3 seekerDirWorld = ac.transform.TransformDirection(_seekerDirLocal).normalized;
            bool groundInView = Physics.Raycast(origin, seekerDirWorld, EnvProbeDistanceMeters, LosLayerMask);

            string candidate = groundInView ? _activeProfile.EnvGnd : _activeProfile.EnvSky;
            if (string.IsNullOrEmpty(candidate)) return false;

            envFile = candidate;
            return true;
        }

        private bool TryGetHqTrackingSnapshot(
            Aircraft ownAircraft,
            Unit target,
            out bool targetTracked,
            out bool hasTrackingEntry,
            out bool trackingObserved,
            out Vector3 trackingPosition,
            out bool hasTrackingPosition)
        {
            targetTracked = false;
            hasTrackingEntry = false;
            trackingObserved = false;
            trackingPosition = Vector3.zero;
            hasTrackingPosition = false;

            if (ownAircraft == null || target == null)
                return false;

            var ownHq = GetUnitHqSafe(ownAircraft);
            if (ownHq == null)
                return false;

            try
            {
                targetTracked = ownHq.IsTargetBeingTracked(target);
            }
            catch
            {
                targetTracked = false;
            }

            // Use game-native HQ APIs for target tracking snapshots.
            // This avoids fragile reflection against internal tracking dictionaries.
            try
            {
                if (ownHq.TryGetKnownPosition(target, out var knownPosition))
                {
                    hasTrackingEntry = true;
                    hasTrackingPosition = TryConvertKnownPositionToLocal(knownPosition, out trackingPosition);
                    if (!hasTrackingPosition)
                        trackingPosition = Vector3.zero;
                }
            }
            catch
            {
                // Keep defaults and try fallback API below.
            }

            if (!hasTrackingPosition)
            {
                try
                {
                    var knownPosition = ownHq.GetKnownPosition(target);
                    if (knownPosition.HasValue)
                    {
                        hasTrackingEntry = true;
                        hasTrackingPosition = TryConvertKnownPositionToLocal(knownPosition.Value, out trackingPosition);
                        if (!hasTrackingPosition)
                            trackingPosition = Vector3.zero;
                    }
                }
                catch
                {
                    // Keep defaults.
                }
            }

            // "Observed" here follows HQ's own tracked-state decision.
            trackingObserved = targetTracked;

            return true;
        }

        private static bool TryConvertKnownPositionToLocal(GlobalPosition knownPosition, out Vector3 localPosition)
        {
            localPosition = Vector3.zero;

            try
            {
                // Nuclear Option uses GlobalPosition + floating-origin transforms.
                // ToLocalPosition maps tracked coordinates into the current local world space.
                localPosition = GlobalPositionExtensions.ToLocalPosition(knownPosition);

                if (!float.IsNaN(localPosition.x) && !float.IsNaN(localPosition.y) && !float.IsNaN(localPosition.z) &&
                    !float.IsInfinity(localPosition.x) && !float.IsInfinity(localPosition.y) && !float.IsInfinity(localPosition.z))
                {
                    return true;
                }
            }
            catch
            {
                // Fall back below.
            }

            // Fallback path for API differences.
            return TryConvertToVector3(knownPosition, out localPosition);
        }

        private static bool TryConvertToVector3(object value, out Vector3 result)
        {
            result = Vector3.zero;
            if (value == null)
                return false;

            if (value is Vector3 vec)
            {
                result = vec;
                return true;
            }

            try
            {
                var valueType = value.GetType();

                // Nuclear Option TrackingInfo.GetPosition() returns GlobalPosition.
                // Prefer its AsVector3() converter when present.
                var asVector3Method = valueType.GetMethod(
                    "AsVector3",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                if (asVector3Method != null && asVector3Method.ReturnType == typeof(Vector3))
                {
                    var converted = asVector3Method.Invoke(value, null);
                    if (converted is Vector3 convertedVec)
                    {
                        result = convertedVec;
                        return true;
                    }
                }

                // Fallback for structs that expose x/y/z fields.
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var fx = valueType.GetField("x", flags);
                var fy = valueType.GetField("y", flags);
                var fz = valueType.GetField("z", flags);

                if (fx != null && fy != null && fz != null)
                {
                    float x = Convert.ToSingle(fx.GetValue(value));
                    float y = Convert.ToSingle(fy.GetValue(value));
                    float z = Convert.ToSingle(fz.GetValue(value));
                    result = new Vector3(x, y, z);
                    return true;
                }
            }
            catch
            {
                // Keep defaults.
            }

            return false;
        }

        private static FactionHQ GetUnitHqSafe(Unit unit)
        {
            if (unit == null) return null;

            try
            {
                return unit.NetworkHQ;
            }
            catch
            {
                return null;
            }
        }

        private void SampleTargetHeatAndFlare(
            Aircraft ac,
            Vector3 origin,
            Unit target,
            out float targetNonFlareHeat,
            out bool targetFlaringInFov,
            out float targetFlareHeat)
        {
            targetNonFlareHeat = 0f;
            targetFlaringInFov = false;
            targetFlareHeat = 0f;
            if (target == null || !target.HasIRSignature()) return;

            bool canEvaluateFlareInFov = (ac != null) && (IRSourcesField != null);

            if (IRSourcesField != null)
            {
                try
                {
                    var list = IRSourcesField.GetValue(target) as List<IRSource>;
                    if (list != null && list.Count > 0)
                    {
                        float bestNonFlare = -1f;
                        GlobalPosition targetGlobalPosition = target.GlobalPosition();

                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var s = list[i];
                            if (s == null || s.transform == null)
                            {
                                list.RemoveAt(i);
                                continue;
                            }

                            if (FastMath.OutOfRange(s.transform.GlobalPosition(), targetGlobalPosition, 100f))
                            {
                                list.RemoveAt(i);
                                continue;
                            }

                            float inten = s.intensity;
                            if (!s.flare && inten > bestNonFlare)
                                bestNonFlare = inten;

                            if (!canEvaluateFlareInFov) continue;
                            if (!s.flare) continue;
                            if (inten <= 0.001f) continue;

                            Vector3 toSrcWorld = s.transform.position - origin;
                            float srcDist = toSrcWorld.magnitude;
                            if (srcDist <= 0.001f) continue;

                            Vector3 dirToSrcLocal = ac.transform.InverseTransformDirection(toSrcWorld / srcDist).normalized;
                            if (Vector3.Dot(Vector3.forward, dirToSrcLocal) < FrontConeCos) continue;
                            if (Vector3.Dot(_seekerDirLocal, dirToSrcLocal) < NarrowFovCos) continue;
                            if (Physics.Linecast(origin, s.transform.position, LosLayerMask)) continue;

                            targetFlaringInFov = true;
                            targetFlareHeat += inten;
                        }

                        targetNonFlareHeat = (bestNonFlare >= 0f) ? bestNonFlare : 0f;
                        return;
                    }
                }
                catch
                {
                    // Keep defaults.
                }
            }

            // Fallback: random source
            var src = target.GetIRSource();
            if (src == null || src.flare) return;
            targetNonFlareHeat = src.intensity;
        }

        private float GetFlarePulse01(bool flarePulseActive, float dt)
        {
            float safeDt = Mathf.Max(0f, dt);
            float riseStep = (FlarePulseFadeInSeconds <= 0.001f) ? 1f : (safeDt / FlarePulseFadeInSeconds);
            float fallStep = (FlarePulseFadeOutSeconds <= 0.001f) ? 1f : (safeDt / FlarePulseFadeOutSeconds);

            if (flarePulseActive)
            {
                if (_flarePulseRising)
                {
                    _flarePulse01 = Mathf.Clamp01(_flarePulse01 + riseStep);
                    if (_flarePulse01 >= 0.999f)
                        _flarePulseRising = false;
                }
                else
                {
                    _flarePulse01 = Mathf.Clamp01(_flarePulse01 - fallStep);
                    if (_flarePulse01 <= 0.001f)
                        _flarePulseRising = true;
                }
            }
            else
            {
                // Smooth release when flaring condition drops.
                _flarePulseRising = true;
                _flarePulse01 = Mathf.Clamp01(_flarePulse01 - fallStep);
            }

            return _flarePulse01;
        }
    }
}
