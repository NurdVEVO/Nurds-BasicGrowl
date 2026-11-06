using NuclearOption;
using UnityEngine;

namespace com.basicGrowl.Services
{
    internal sealed partial class SeekerToneServices
    {
        // ----------------------------
        // Debug
        // ----------------------------
        private void MaybeLogStateTransitions(
            bool hasTarget,
            string targetName,
            bool targetTrackedByHq,
            bool withinHeatRange)
        {
            if (!EnableStateTransitionLog) return;

            if (!_hasLoggedStateSnapshot)
            {
                _hasLoggedStateSnapshot = true;
                _lastLoggedHasTarget = hasTarget;
                _lastLoggedTargetTrackedByHq = targetTrackedByHq;
                _lastLoggedInFrontCone = _inFrontCone;
                _lastLoggedInFov = _inFov;
                _lastLoggedHasLos = _hasLos;
                _lastLoggedWithinHeatRange = withinHeatRange;
                _lastLoggedTargetName = targetName ?? "<none>";

                Plugin.Log.LogInfo(
                    $"[SeekerState] init target={_lastLoggedTargetName} hasTarget={hasTarget} hqTrack={targetTrackedByHq} front={_inFrontCone} fov={_inFov} los={_hasLos} inRange={withinHeatRange}");
                return;
            }

            if (hasTarget != _lastLoggedHasTarget || !string.Equals(targetName, _lastLoggedTargetName, System.StringComparison.Ordinal))
                Plugin.Log.LogInfo($"[SeekerState] target changed: hasTarget={_lastLoggedHasTarget}->{hasTarget} name='{_lastLoggedTargetName}'->'{targetName}'");

            if (targetTrackedByHq != _lastLoggedTargetTrackedByHq)
                Plugin.Log.LogInfo($"[SeekerState] HQ track changed: {_lastLoggedTargetTrackedByHq}->{targetTrackedByHq}");

            if (_inFrontCone != _lastLoggedInFrontCone)
                Plugin.Log.LogInfo($"[SeekerState] front cone changed: {_lastLoggedInFrontCone}->{_inFrontCone}");

            if (_inFov != _lastLoggedInFov)
                Plugin.Log.LogInfo($"[SeekerState] FOV changed: {_lastLoggedInFov}->{_inFov}");

            if (_hasLos != _lastLoggedHasLos)
                Plugin.Log.LogInfo($"[SeekerState] LOS changed: {_lastLoggedHasLos}->{_hasLos}");

            if (withinHeatRange != _lastLoggedWithinHeatRange)
                Plugin.Log.LogInfo($"[SeekerState] heat range gate changed: {_lastLoggedWithinHeatRange}->{withinHeatRange}");

            _lastLoggedHasTarget = hasTarget;
            _lastLoggedTargetTrackedByHq = targetTrackedByHq;
            _lastLoggedInFrontCone = _inFrontCone;
            _lastLoggedInFov = _inFov;
            _lastLoggedHasLos = _hasLos;
            _lastLoggedWithinHeatRange = withinHeatRange;
            _lastLoggedTargetName = targetName ?? "<none>";
        }

        private void MaybeStatusLog(
            Aircraft ownAircraft,
            bool hasTarget,
            string targetName,
            bool targetTrackedByHq,
            bool hasTrackingEntry,
            bool trackingObserved,
            bool usingTrackingAimPoint,
            float trackingDeltaMeters,
            float distMeters,
            bool hasShotRange,
            float shotRangeMinMeters,
            float shotRangeMaxMeters,
            bool withinHeatRange,
            float heatRaw,
            bool hasValidHeatSource,
            float heat,
            float heat01,
            float pitch,
            float trackFactor,
            float targetFlareHeat,
            float targetNonFlareHeat)
        {
            if (!EnableTickDiagnosticsLog) return;
            if (Time.unscaledTime < _nextStatusLog) return;
            _nextStatusLog = Time.unscaledTime + 0.5f;

            string distText = hasTarget && distMeters >= 0f
                ? $"{distMeters / 1000f:F2}km"
                : "<none>";

            string rangeText = hasShotRange
                ? $"{shotRangeMinMeters:F0}-{shotRangeMaxMeters:F0}m ({(withinHeatRange ? "IN" : "OUT")})"
                : "<none>";

            bool hasTrackingSnapshot = hasTrackingEntry || usingTrackingAimPoint;
            bool hasTrackingPosition = usingTrackingAimPoint;
            float trackingLiveDeltaMeters = trackingDeltaMeters;

            Plugin.Log.LogInfo(
                $"[SeekerDiag] target={hasTarget} name='{targetName}' hqTrack={targetTrackedByHq} gates(front={_inFrontCone} fov={_inFov} los={_hasLos}) lock={_lockStrength:F2} trk={trackFactor:F2} dist={distText} range={rangeText} heatRaw={heatRaw:F2} heatValid={hasValidHeatSource} heat={heat:F2} heat01={heat01:F2} pitch={pitch:F2} flareHeat={targetFlareHeat:F2} nonFlareHeat={targetNonFlareHeat:F2}");

            if (hasTarget)
            {
                string hqTrackText = hasTrackingSnapshot
                    ? $"entry={hasTrackingEntry} observed={trackingObserved} hasPos={hasTrackingPosition} delta={trackingLiveDeltaMeters:F1}m"
                    : "entry=<unavailable>";

                Plugin.Log.LogInfo(
                    $"[SeekerDiag] aimSource={(usingTrackingAimPoint ? "HQ_TRACK_POS" : "NO_HQ_TRACK_AIM")} hqTrack=({hqTrackText})");

                // Anti-cheat telemetry:
                // Compare what the seeker is currently aligned to (HQ track aim vs live target transform),
                // and show exactly what is allowed to drive heat/pitch this tick.
                float hqDistMeters = -1f;
                float liveDistMeters = -1f;
                float hqSeekErrDeg = -1f;
                float liveSeekErrDeg = -1f;
                bool hasDirectLosHq = false;
                bool hasDirectLosLive = false;
                bool directLosHq = false;
                bool directLosLive = false;

                if (ownAircraft != null && _target != null)
                {
                    Vector3 origin = ownAircraft.transform.position;

                    try
                    {
                        Vector3 toLiveWorld = _target.transform.position - origin;
                        liveDistMeters = toLiveWorld.magnitude;
                        if (liveDistMeters > 0.001f)
                        {
                            Vector3 liveDirLocal = ownAircraft.transform.InverseTransformDirection(toLiveWorld / liveDistMeters).normalized;
                            liveSeekErrDeg = Vector3.Angle(_seekerDirLocal, liveDirLocal);
                            directLosLive = !Physics.Linecast(origin, _target.transform.position, LosLayerMask);
                            hasDirectLosLive = true;
                        }
                    }
                    catch
                    {
                        // Keep defaults.
                    }

                    if (usingTrackingAimPoint && _hasTrackingAimPoint)
                    {
                        try
                        {
                            Vector3 toHqWorld = _trackingAimPointWorld - origin;
                            hqDistMeters = toHqWorld.magnitude;
                            if (hqDistMeters > 0.001f)
                            {
                                Vector3 hqDirLocal = ownAircraft.transform.InverseTransformDirection(toHqWorld / hqDistMeters).normalized;
                                hqSeekErrDeg = Vector3.Angle(_seekerDirLocal, hqDirLocal);
                                directLosHq = !Physics.Linecast(origin, _trackingAimPointWorld, LosLayerMask);
                                hasDirectLosHq = true;
                            }
                        }
                        catch
                        {
                            // Keep defaults.
                        }
                    }
                }

                bool liveBiasSuspect =
                    (hqSeekErrDeg >= 0f) &&
                    (liveSeekErrDeg >= 0f) &&
                    (liveSeekErrDeg + 1.0f < hqSeekErrDeg);

                string hqErrText = FormatDebugFloat(hqSeekErrDeg, "F2");
                string liveErrText = FormatDebugFloat(liveSeekErrDeg, "F2");
                string hqDistText = FormatDebugFloat(hqDistMeters, "F1");
                string liveDistText = FormatDebugFloat(liveDistMeters, "F1");
                string hqLosText = hasDirectLosHq ? directLosHq.ToString() : "<na>";
                string liveLosText = hasDirectLosLive ? directLosLive.ToString() : "<na>";
                string heatSource = (heatRaw > 0f) ? "TARGET_IR_NONFLARE" : "NONE";

                Plugin.Log.LogInfo(
                    $"[SeekerDiag] heatSource={heatSource} heatGate=(hqTrack={targetTrackedByHq} hqAim={usingTrackingAimPoint} losGate={_hasLos} rangeGate={withinHeatRange} validHeat={hasValidHeatSource} on>={HeatValidOnThreshold:F2}/off<{HeatValidOffThreshold:F2}) seekErrDeg(hq={hqErrText} live={liveErrText}) distM(hq={hqDistText} live={liveDistText}) losDirect(hq={hqLosText} live={liveLosText}) liveBiasSuspect={liveBiasSuspect}");
            }
        }

        private static string FormatDebugFloat(float value, string format)
        {
            return value >= 0f ? value.ToString(format) : "<na>";
        }
    }
}
