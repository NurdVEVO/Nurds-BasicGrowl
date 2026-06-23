using UnityEngine;
using NuclearOption;

namespace com.basicGrowl.Services
{
    internal sealed partial class SeekerToneServices
    {
        private void ComputeGeometryAndLos(Aircraft ac, Vector3 origin, Vector3 targetPos, Vector3 toTargetWorld, float dist, float dt)
        {
            if (dist < 1f)
            {
                // Extremely close: treat as visible and align
                Vector3 dirLocalClose = ac.transform.InverseTransformDirection(toTargetWorld.normalized);
                SlewTowardLocal(dirLocalClose, dt);
                _inFrontCone = true;
                _inFov = true;
                _hasLos = true;
                return;
            }

            Vector3 dirToTargetWorld = toTargetWorld / dist;
            Vector3 dirToTargetLocal = ac.transform.InverseTransformDirection(dirToTargetWorld).normalized;

            // Front cone gate (LOCAL)
            _inFrontCone = Vector3.Dot(Vector3.forward, dirToTargetLocal) >= FrontConeCos;

            // Always steer toward selected target. If target is outside front cone,
            // clamp steering to cone edge instead of snapping to boresight.
            Vector3 desiredSeekerLocal = ClampToFrontConeLocal(dirToTargetLocal);
            SlewTowardLocal(desiredSeekerLocal, dt);

            // Narrow FOV around seeker line (used for lock gating only)
            _inFov = _inFrontCone && (Vector3.Dot(_seekerDirLocal, dirToTargetLocal) >= NarrowFovCos);

            if (_inFrontCone)
            {
                // Inside cone: true LOS to target.
                _hasLos = !Physics.Linecast(origin, targetPos, LosLayerMask);
                return;
            }

            // Outside cone: keep attempting LOS along the clamped seeker direction.
            Vector3 seekerDirWorld = ac.transform.TransformDirection(_seekerDirLocal).normalized;
            Vector3 barrierPoint = origin + (seekerDirWorld * dist);
            _hasLos = !Physics.Linecast(origin, barrierPoint, LosLayerMask);
        }

        private Vector3 ClampToFrontConeLocal(Vector3 dirToTargetLocal)
        {
            dirToTargetLocal.Normalize();
            float angleFromForward = Vector3.Angle(Vector3.forward, dirToTargetLocal);
            if (angleFromForward <= FrontConeHalfDeg)
                return dirToTargetLocal;

            float maxRad = FrontConeHalfDeg * Mathf.Deg2Rad;
            return Vector3.RotateTowards(Vector3.forward, dirToTargetLocal, maxRad, 0f).normalized;
        }

        private float GetTrackFactor(Aircraft ac, Vector3 toTargetWorld, float dist)
        {
            if (ac == null || dist <= 0.001f) return 0f;

            Vector3 dirToTargetWorld = toTargetWorld / dist;
            Vector3 dirToTargetLocal = ac.transform.InverseTransformDirection(dirToTargetWorld).normalized;
            float errDeg = Vector3.Angle(_seekerDirLocal, dirToTargetLocal);

            return Mathf.Clamp01(Mathf.InverseLerp(TrackErrorMaxDeg, TrackErrorMinDeg, errDeg));
        }

        // ----------------------------
        // Seeker slew helpers
        // ----------------------------
        private void SlewTowardLocal(Vector3 desiredDirLocal, float dt)
        {
            desiredDirLocal.Normalize();
            float maxRad = (SlewRateDegPerSec * Mathf.Deg2Rad) * dt;
            _seekerDirLocal = Vector3.RotateTowards(_seekerDirLocal, desiredDirLocal, maxRad, 0f).normalized;
        }

        private void DebugViz(Vector3 origin, Aircraft ac, Vector3 targetPos)
        {
            if (!DebugDraw) return;

            float dur = TickInterval * 1.2f;
            Vector3 seekerDirWorld = ac.transform.TransformDirection(_seekerDirLocal).normalized;

            Debug.DrawLine(origin, origin + ac.transform.forward * 2000f, Color.cyan, dur);
            Debug.DrawLine(origin, origin + seekerDirWorld * 5000f, Color.white, dur);
            Debug.DrawLine(origin, targetPos, _hasLos ? Color.green : Color.red, dur);
        }
    }
}
