using System;
using UnityEngine;

namespace com.basicGrowl.Services
{
    internal sealed class SeekerOverlayDraw : MonoBehaviour
    {
        private const int SegmentCount = 96;
        private const string PreferredCameraName = "cockpitRenderer";
        private static readonly bool EnableSkipLogs = false;
        private const float CameraSelectionRefreshIntervalSeconds = 0.25f;
        private const float DebugHudRefreshIntervalSeconds = 0.10f;
        private const float RenderDirectionSmoothTimeSeconds = 0.045f;
        private const int DebugHudLineCount = 12;
        private static Material _lineMaterial;
        private static Vector2[] _ringUnitPoints;

        private SeekerToneServices _seekerToneServices;

        public float ThicknessPx = 2f;
        public Color RingColor = new Color(0.08f, 1f, 0.08f, 0.9f);
        public float MinRadiusPx = 4f;
        public float MaxRadiusPx = 400f;
        public float FixedRadiusPx = 64f;
        public bool DebugHudText = false;

        private bool _lastDrawSuccess;
        private string _lastSkipReason = "<none>";
        private string _lastCameraName = "<none>";
        private string _lastCallbackSource = "<none>";
        private int _lastCameraId;
        private Vector2 _lastCenterPx;
        private float _lastRadiusPx;

        private bool _loggedFirstCallback;
        private bool _loggedFirstDraw;
        private string _lastSkipLogged = "";
        private float _nextSkipLogTime;
        private int _cameraSelectionFrame = -1;
        private Camera _cachedPreferredCamera;
        private Camera _cachedTargetCamera;
        private float _nextCameraSelectionRefreshTime;
        private bool _hasRenderDirCamera;
        private Vector3 _renderDirCamera = Vector3.forward;
        private int _renderDirCameraId;
        private float _lastRenderDirTime;
        private readonly string[] _debugHudLines = new string[DebugHudLineCount];
        private float _nextDebugHudRefreshTime;

        public void Bind(SeekerToneServices seekerToneServices)
        {
            _seekerToneServices = seekerToneServices;
        }

        private void OnEnable()
        {
            EnsureMaterial();
            _nextCameraSelectionRefreshTime = 0f;
            Plugin.Log?.LogInfo("[Overlay] Enabled. Draw: OnGUI repaint");
        }

        private void OnDisable()
        {
            Plugin.Log?.LogInfo("[Overlay] Disabled.");
        }

        private void DrawOverlayGui()
        {
            _lastDrawSuccess = false;
            _lastCallbackSource = "OnGUI";

            if (_seekerToneServices == null || !_seekerToneServices.IsOverlayActive)
                return;

            if (_lineMaterial == null)
            {
                SetSkipReason("missing material/service");
                return;
            }

            Camera cam = ResolveProjectionCamera();
            if (cam == null)
                return;

            _lastCameraName = cam.name;
            _lastCameraId = cam.GetInstanceID();

            if (!_loggedFirstCallback)
            {
                Plugin.Log?.LogInfo($"[Overlay] First projection camera cam='{cam.name}' type={cam.cameraType}");
                _loggedFirstCallback = true;
            }

            if (!ShouldUseProjectionCamera(cam))
                return;

            if (!_seekerToneServices.TryGetOverlayState(out var _, out var seekerDirWorld))
            {
                SetSkipReason("overlay state hidden");
                return;
            }

            if (!TryProjectRing(cam, seekerDirWorld, out var centerX, out var centerY, out var radiusPx, out var reason))
            {
                SetSkipReason(reason);
                return;
            }

            DrawRing(centerX, centerY, radiusPx);
            _lastDrawSuccess = true;
            _lastSkipReason = "drawn";
            _lastCenterPx = new Vector2(centerX, centerY);
            _lastRadiusPx = radiusPx;

            if (!_loggedFirstDraw)
            {
                Plugin.Log?.LogInfo($"[Overlay] First draw success cam='{cam.name}' source={_lastCallbackSource} center=({centerX:F1},{centerY:F1}) radius={radiusPx:F1}");
                _loggedFirstDraw = true;
            }
        }

        private bool ShouldUseProjectionCamera(Camera cam)
        {
            if (cam == null)
            {
                SetSkipReason("camera null");
                return false;
            }

            if (cam.cameraType != CameraType.Game)
            {
                SetSkipReason("camera type filtered");
                return false;
            }

            bool hasMode = TryIsCockpitModeAvailable(out var isCockpitMode);
            if (hasMode && !isCockpitMode)
            {
                SetSkipReason("not cockpit camera mode");
                return false;
            }

            if (IsPreferredCamera(cam))
            {
                _cachedPreferredCamera = cam;
                _cachedTargetCamera = cam;
                _nextCameraSelectionRefreshTime = Time.unscaledTime + CameraSelectionRefreshIntervalSeconds;
                return true;
            }

            RefreshCameraSelectionForFrame();

            var preferredCam = _cachedPreferredCamera;
            var targetCam = _cachedTargetCamera;
            if (targetCam == null)
            {
                SetSkipReason("no preferred/main camera available");
                return false;
            }

            if (cam != targetCam)
            {
                if (preferredCam != null)
                    SetSkipReason("not preferred camera");
                else if (hasMode)
                    SetSkipReason("cockpit mode fallback main camera");
                else
                    SetSkipReason("not fallback main camera");

                return false;
            }

            return true;
        }

        private Camera ResolveProjectionCamera()
        {
            RefreshCameraSelectionForFrame();
            Camera cam = _cachedPreferredCamera != null ? _cachedPreferredCamera : _cachedTargetCamera;
            if (cam == null)
                SetSkipReason("no preferred/main camera available");
            return cam;
        }

        private void RefreshCameraSelectionForFrame()
        {
            int frame = Time.frameCount;
            if (_cameraSelectionFrame == frame)
                return;

            _cameraSelectionFrame = frame;

            bool hasCachedTargetCamera = _cachedTargetCamera != null;
            bool refreshDue = Time.unscaledTime >= _nextCameraSelectionRefreshTime;
            if (hasCachedTargetCamera && !refreshDue)
                return;

            _cachedPreferredCamera = FindPreferredCamera();
            _cachedTargetCamera = _cachedPreferredCamera ?? Camera.main;
            _nextCameraSelectionRefreshTime = Time.unscaledTime + CameraSelectionRefreshIntervalSeconds;
        }

        private static bool TryIsCockpitModeAvailable(out bool isCockpitMode)
        {
            try
            {
                isCockpitMode = CameraStateManager.cameraMode == CameraMode.cockpit;
                return true;
            }
            catch
            {
                isCockpitMode = false;
                return false;
            }
        }

        private static Camera FindPreferredCamera()
        {
            var cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null) continue;
                if (IsPreferredCamera(c))
                    return c;
            }

            return null;
        }

        private static bool IsPreferredCamera(Camera cam)
        {
            return cam != null && string.Equals(cam.name, PreferredCameraName, StringComparison.OrdinalIgnoreCase);
        }

        private void SetSkipReason(string reason)
        {
            _lastSkipReason = reason;
            if (!EnableSkipLogs) return;

            if (Plugin.Log == null)
                return;

            bool reasonChanged = !string.Equals(reason, _lastSkipLogged, StringComparison.Ordinal);
            bool timeElapsed = Time.unscaledTime >= _nextSkipLogTime;

            if (!reasonChanged && !timeElapsed)
                return;

            _lastSkipLogged = reason;
            _nextSkipLogTime = Time.unscaledTime + 1.0f;
            Plugin.Log.LogInfo($"[Overlay] Skip: {reason} cam='{_lastCameraName}' id={_lastCameraId} source={_lastCallbackSource}");
        }

        private bool TryProjectRing(
            Camera cam,
            Vector3 seekerDirWorld,
            out float centerX,
            out float centerY,
            out float radiusPx,
            out string reason)
        {
            centerX = 0f;
            centerY = 0f;
            radiusPx = 0f;
            reason = "<none>";

            if (seekerDirWorld.sqrMagnitude <= 0.00001f)
            {
                reason = "seeker dir too small";
                return false;
            }

            if (FixedRadiusPx <= 0.01f)
            {
                reason = "fixed radius too small";
                return false;
            }

            seekerDirWorld.Normalize();

            // Treat the reticle as a virtual direction bone in the render camera's local space.
            // This avoids cockpit/camera parallax from any arbitrary world-space pivot point.
            Vector3 dirCamera = GetSmoothedCameraDirection(cam, seekerDirWorld);
            if (dirCamera.z <= 0.0001f)
            {
                reason = "center behind camera";
                return false;
            }

            float tanHalfY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (tanHalfY <= 0.0001f)
            {
                reason = "camera fov invalid";
                return false;
            }

            Rect pixelRect = cam.pixelRect;
            float tanHalfX = tanHalfY * Mathf.Max(0.0001f, cam.aspect);
            float viewportX = 0.5f + 0.5f * ((dirCamera.x / dirCamera.z) / tanHalfX);
            float viewportY = 0.5f + 0.5f * ((dirCamera.y / dirCamera.z) / tanHalfY);

            centerX = pixelRect.x + viewportX * pixelRect.width;
            float screenY = pixelRect.y + viewportY * pixelRect.height;
            centerY = Screen.height - screenY;
            radiusPx = Mathf.Clamp(FixedRadiusPx, MinRadiusPx, MaxRadiusPx);
            return true;
        }

        private Vector3 GetSmoothedCameraDirection(Camera cam, Vector3 seekerDirWorld)
        {
            Vector3 targetDirCamera = cam.transform.InverseTransformDirection(seekerDirWorld).normalized;
            float now = Time.unscaledTime;
            float dt = _lastRenderDirTime > 0f ? Mathf.Clamp(now - _lastRenderDirTime, 0f, 0.1f) : 0f;
            _lastRenderDirTime = now;

            int camId = cam.GetInstanceID();
            if (!_hasRenderDirCamera || _renderDirCameraId != camId)
            {
                _renderDirCamera = targetDirCamera;
                _renderDirCameraId = camId;
                _hasRenderDirCamera = true;
                return _renderDirCamera;
            }

            float t = RenderDirectionSmoothTimeSeconds <= 0.0001f
                ? 1f
                : 1f - Mathf.Exp(-dt / RenderDirectionSmoothTimeSeconds);

            _renderDirCamera = Vector3.Slerp(_renderDirCamera, targetDirCamera, Mathf.Clamp01(t)).normalized;
            return _renderDirCamera;
        }

        private void DrawRing(float centerX, float centerY, float radiusPx)
        {
            float outer = Mathf.Max(0f, radiusPx + ThicknessPx * 0.5f);
            float inner = Mathf.Max(0f, radiusPx - ThicknessPx * 0.5f);

            GL.PushMatrix();
            _lineMaterial.SetPass(0);
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
            GL.Begin(GL.TRIANGLE_STRIP);
            GL.Color(RingColor);

            var points = _ringUnitPoints;
            for (int i = 0; i < points.Length; i++)
            {
                float cs = points[i].x;
                float sn = points[i].y;

                GL.Vertex3(centerX + cs * outer, centerY + sn * outer, 0f);
                GL.Vertex3(centerX + cs * inner, centerY + sn * inner, 0f);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e != null && e.type == EventType.Repaint)
                DrawOverlayGui();

            if (!DebugHudText) return;

            if (Time.unscaledTime >= _nextDebugHudRefreshTime)
            {
                RefreshDebugHudSnapshot();
                _nextDebugHudRefreshTime = Time.unscaledTime + DebugHudRefreshIntervalSeconds;
            }

            float x = 12f;
            float y = 12f;
            float w = 1200f;
            float h = 420f;

            GUI.Box(new Rect(x, y, w, h), GUIContent.none);

            float row = y + 8f;
            float rowStep = 18f;

            for (int i = 0; i < _debugHudLines.Length; i++)
            {
                string line = _debugHudLines[i];
                if (!string.IsNullOrEmpty(line))
                {
                    GUI.Label(new Rect(x + 10f, row, w - 20f, rowStep), line);
                }

                row += rowStep;
            }
        }

        private void RefreshDebugHudSnapshot()
        {
            _debugHudLines[0] = "Overlay Debug";
            _debugHudLines[1] = $"Overlay draw={_lastDrawSuccess} skip=({_lastSkipReason}) src={_lastCallbackSource}";
            _debugHudLines[2] = $"Cam='{_lastCameraName}' id={_lastCameraId} center=({_lastCenterPx.x:F1},{_lastCenterPx.y:F1}) radius={_lastRadiusPx:F1}px";

            if (_seekerToneServices == null)
            {
                _debugHudLines[3] = "Seeker service: <null>";
                for (int i = 4; i < _debugHudLines.Length; i++)
                    _debugHudLines[i] = "<n/a>";
                return;
            }

            _seekerToneServices.GetDebugState(
                out var overlayVisible,
                out var hasTarget,
                out var targetTrackedByHq,
                out var inFrontCone,
                out var inFov,
                out var hasLos,
                out var lockStrength,
                out var trackFactor,
                out var heat,
                out var heat01,
                out var pitch,
                out var flareDetected,
                out var flareHeat,
                out var flarePulse01,
                out var flarePeriodSeconds,
                out var profileName);

            _debugHudLines[3] = $"Profile={profileName} overlayVisible={overlayVisible} target={hasTarget} hqTrack={targetTrackedByHq}";
            _debugHudLines[4] = $"front={inFrontCone} fov={inFov} los={hasLos} lock={lockStrength:F2} trk={trackFactor:F2}";
            _debugHudLines[5] = $"heat={heat:F2} heat01={heat01:F2} pitch={pitch:F2}";
            _debugHudLines[6] = $"flarePulse={flareDetected} flareHeat={flareHeat:F2} pulse01={flarePulse01:F2} period={flarePeriodSeconds:F2}s";

            _seekerToneServices.GetAudioDebugState(
                out var envFileWanted,
                out var cagedTargetVol,
                out var uncagedTargetVol,
                out var envTargetVol,
                out var cagedState,
                out var uncagedState,
                out var envAState,
                out var envBState);

            _debugHudLines[7] = $"TargetVol caged={cagedTargetVol:F2} uncaged={uncagedTargetVol:F2} env={envTargetVol:F2} envFile={envFileWanted}";
            _debugHudLines[8] = $"Caged:   {cagedState}";
            _debugHudLines[9] = $"Uncaged: {uncagedState}";
            _debugHudLines[10] = $"EnvA:    {envAState}";
            _debugHudLines[11] = $"EnvB:    {envBState}";
        }

        private static void EnsureMaterial()
        {
            EnsureRingUnitPoints();
            if (_lineMaterial != null) return;

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;

            _lineMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMaterial.renderQueue = 5000;

            _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMaterial.SetInt("_ZWrite", 0);
            _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private static void EnsureRingUnitPoints()
        {
            if (_ringUnitPoints != null) return;

            _ringUnitPoints = new Vector2[SegmentCount + 1];
            for (int i = 0; i < _ringUnitPoints.Length; i++)
            {
                float t = (float)i / SegmentCount;
                float a = t * Mathf.PI * 2f;
                _ringUnitPoints[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
        }
    }
}

