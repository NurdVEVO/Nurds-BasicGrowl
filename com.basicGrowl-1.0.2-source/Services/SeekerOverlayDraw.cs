using System;
using NuclearOption.UIStyleSystem;
using UnityEngine;
using UnityEngine.UI;

namespace com.basicGrowl.Services
{
    internal sealed class SeekerOverlayDraw : MonoBehaviour
    {
        private const string PreferredCameraName = "cockpitRenderer";
        private static readonly bool EnableSkipLogs = false;
        private const float CameraSelectionRefreshIntervalSeconds = 0.25f;
        private const float DebugHudRefreshIntervalSeconds = 0.10f;
        private const float RenderDirectionSmoothTimeSeconds = 0.045f;
        private const int DebugHudLineCount = 12;

        private SeekerToneServices _seekerToneServices;
        private SeekerOverlayGraphic _hudGraphic;
        private RectTransform _hudRoot;

        public float ThicknessPx = 2f;
        public Color RingColor = new Color(0f, 1f, 0f, 1f);
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
            _nextCameraSelectionRefreshTime = 0f;
            Plugin.Log?.LogInfo("[Overlay] Enabled. Draw: HUD canvas");
        }

        private void OnDisable()
        {
            DestroyHudGraphic();
            Plugin.Log?.LogInfo("[Overlay] Disabled.");
        }

        private void OnDestroy()
        {
            DestroyHudGraphic();
        }

        private void LateUpdate()
        {
            DrawHudOverlay();
        }

        private void DrawHudOverlay()
        {
            _lastDrawSuccess = false;
            _lastCallbackSource = "LateUpdate/HUDCanvas";

            if (_seekerToneServices == null || !_seekerToneServices.IsOverlayActive)
            {
                HideHudGraphic();
                return;
            }

            if (!EnsureHudGraphic())
            {
                SetSkipReason("HUD canvas unavailable");
                return;
            }

            Camera cam = ResolveProjectionCamera();
            if (cam == null)
            {
                HideHudGraphic();
                return;
            }

            _lastCameraName = cam.name;
            _lastCameraId = cam.GetInstanceID();

            if (!_loggedFirstCallback)
            {
                Plugin.Log?.LogInfo($"[Overlay] First projection camera cam='{cam.name}' type={cam.cameraType}");
                _loggedFirstCallback = true;
            }

            if (!ShouldUseProjectionCamera(cam))
            {
                HideHudGraphic();
                return;
            }

            if (!_seekerToneServices.TryGetOverlayState(out var _, out var seekerDirWorld))
            {
                HideHudGraphic();
                SetSkipReason("overlay state hidden");
                return;
            }

            if (!TryProjectRing(cam, seekerDirWorld, out var centerX, out var centerY, out var radiusPx, out var reason))
            {
                HideHudGraphic();
                SetSkipReason(reason);
                return;
            }

            if (!TrySetHudRing(new Vector2(centerX, centerY), radiusPx, ResolveHudColor()))
            {
                HideHudGraphic();
                SetSkipReason("HUD coordinate conversion failed");
                return;
            }

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
            centerY = screenY;
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

        private bool EnsureHudGraphic()
        {
            CombatHUD hud = CombatHUD.i;
            RectTransform hudRoot = hud != null ? hud.transform as RectTransform : null;
            if (hudRoot == null)
                return false;

            if (_hudGraphic != null && _hudRoot == hudRoot)
            {
                _hudGraphic.gameObject.layer = hud.gameObject.layer;
                return true;
            }

            DestroyHudGraphic();

            var ringObject = new GameObject(
                "BasicGrowlSeekerCircle",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(SeekerOverlayGraphic));
            ringObject.layer = hud.gameObject.layer;

            var rect = (RectTransform)ringObject.transform;
            rect.SetParent(hudRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            _hudGraphic = ringObject.GetComponent<SeekerOverlayGraphic>();
            _hudGraphic.raycastTarget = false;
            _hudGraphic.enabled = false;
            _hudRoot = hudRoot;

            Plugin.Log?.LogInfo($"[Overlay] Attached seeker circle to HUDCanvas layer={ringObject.layer} ('{LayerMask.LayerToName(ringObject.layer)}').");
            return true;
        }

        private bool TrySetHudRing(Vector2 centerScreen, float radiusPx, Color color)
        {
            if (_hudGraphic == null)
                return false;

            RectTransform rect = _hudGraphic.rectTransform;
            Canvas canvas = _hudGraphic.canvas;
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, centerScreen, uiCamera, out var centerLocal))
                return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect,
                    centerScreen + new Vector2(radiusPx, 0f),
                    uiCamera,
                    out var radiusPointLocal))
                return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect,
                    centerScreen + new Vector2(Mathf.Max(0f, ThicknessPx), 0f),
                    uiCamera,
                    out var thicknessPointLocal))
                return false;

            float radiusLocal = Vector2.Distance(centerLocal, radiusPointLocal);
            float thicknessLocal = Vector2.Distance(centerLocal, thicknessPointLocal);
            _hudGraphic.SetRing(centerLocal, radiusLocal, thicknessLocal, color);
            return true;
        }

        private Color ResolveHudColor()
        {
            try
            {
                if (ThemeManager.Active != null && ThemeManager.Active.ColorTheme != null)
                    return ThemeManager.Active.ColorTheme.AllClear;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[Overlay] HUD theme color unavailable: {ex.Message}");
            }

            return RingColor;
        }

        private void HideHudGraphic()
        {
            if (_hudGraphic != null)
                _hudGraphic.SetVisible(false);
        }

        private void DestroyHudGraphic()
        {
            if (_hudGraphic != null)
            {
                GameObject ringObject = _hudGraphic.gameObject;
                _hudGraphic = null;
                if (ringObject != null)
                    Destroy(ringObject);
            }

            _hudRoot = null;
        }

        private void OnGUI()
        {
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

    }

    internal sealed class SeekerOverlayGraphic : Graphic
    {
        private const int SegmentCount = 96;
        private Vector2 _center;
        private float _radius;
        private float _thickness;
        private bool _visible;

        internal void SetRing(Vector2 center, float radius, float thickness, Color ringColor)
        {
            _center = center;
            _radius = Mathf.Max(0f, radius);
            _thickness = Mathf.Max(0f, thickness);
            color = ringColor;
            _visible = true;
            enabled = true;
            SetVerticesDirty();
        }

        internal void SetVisible(bool visible)
        {
            if (_visible == visible && enabled == visible)
                return;

            _visible = visible;
            enabled = visible;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (!_visible || _radius <= 0f || _thickness <= 0f)
                return;

            float outer = _radius + _thickness * 0.5f;
            float inner = Mathf.Max(0f, _radius - _thickness * 0.5f);
            Color32 vertexColor = color;

            for (int i = 0; i < SegmentCount; i++)
            {
                float a0 = (float)i / SegmentCount * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / SegmentCount * Mathf.PI * 2f;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                int firstVertex = vertexHelper.currentVertCount;
                vertexHelper.AddVert(_center + d0 * outer, vertexColor, Vector2.zero);
                vertexHelper.AddVert(_center + d1 * outer, vertexColor, Vector2.zero);
                vertexHelper.AddVert(_center + d1 * inner, vertexColor, Vector2.zero);
                vertexHelper.AddVert(_center + d0 * inner, vertexColor, Vector2.zero);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
            }
        }
    }
}
