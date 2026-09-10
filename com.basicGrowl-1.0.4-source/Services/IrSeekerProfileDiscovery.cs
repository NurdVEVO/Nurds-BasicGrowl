using System;
using System.Collections.Generic;
using System.Linq;
using com.basicGrowl.Seeker;
using UnityEngine;

namespace com.basicGrowl.Services
{
    internal sealed class IrSeekerProfileDiscovery : MonoBehaviour
    {
        private const float InitialStartupDelaySeconds = 5f;
        private const float RegistryProbeIntervalSeconds = 1f;
        private const int StableMissingProbeCount = 2;

        private readonly List<DiscoveredMissile> _pending = new List<DiscoveredMissile>();
        private readonly HashSet<string> _suppressedAutomaticPrefabs =
            new HashSet<string>(StringComparer.Ordinal);
        private Action _refreshProfiles;
        private float _nextStartupProbeAt;
        private string _lastMissingSignature = string.Empty;
        private int _stableMissingProbes;
        private bool _dialogVisible;
        private bool _confirmationDialog;
        private string _dialogTitle = string.Empty;
        private string _dialogMessage = string.Empty;
        private Vector2 _scrollPosition;
        private Rect _windowRect;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;

        public void Initialize(Action refreshProfiles)
        {
            _refreshProfiles = refreshProfiles;
            _nextStartupProbeAt = Time.realtimeSinceStartup + InitialStartupDelaySeconds;
        }

        public void ScanFromConfigurator()
        {
            if (_dialogVisible)
                return;

            if (!TryFindMissingProfiles(out var missing, out var unavailableReason))
            {
                ShowInformation("IR missile scan", unavailableReason);
                return;
            }

            if (missing.Count == 0)
            {
                ShowInformation("IR missile scan", "No IR missiles without seeker profiles were found.");
                return;
            }

            ShowConfirmation(missing);
        }

        private void Update()
        {
            if (_dialogVisible || Time.realtimeSinceStartup < _nextStartupProbeAt)
                return;

            _nextStartupProbeAt = Time.realtimeSinceStartup + RegistryProbeIntervalSeconds;
            if (!TryFindMissingProfiles(out var missing, out _))
            {
                ResetAutomaticCandidate();
                return;
            }

            missing.RemoveAll(missile => _suppressedAutomaticPrefabs.Contains(missile.PrefabName));
            if (missing.Count == 0)
            {
                ResetAutomaticCandidate();
                return;
            }

            string signature = BuildMissingSignature(missing);
            if (!string.Equals(signature, _lastMissingSignature, StringComparison.Ordinal))
            {
                _lastMissingSignature = signature;
                _stableMissingProbes = 1;
                return;
            }

            _stableMissingProbes++;
            if (_stableMissingProbes < StableMissingProbeCount)
                return;

            ShowConfirmation(missing);
        }

        private static string BuildMissingSignature(IEnumerable<DiscoveredMissile> missing)
        {
            return string.Join("\n", missing.Select(missile => missile.PrefabName).ToArray());
        }

        private void ResetAutomaticCandidate()
        {
            _lastMissingSignature = string.Empty;
            _stableMissingProbes = 0;
        }

        private static bool TryFindMissingProfiles(
            out List<DiscoveredMissile> missing,
            out string unavailableReason)
        {
            missing = new List<DiscoveredMissile>();
            unavailableReason = string.Empty;

            var weaponLookup = Encyclopedia.WeaponLookup;
            if (weaponLookup == null || weaponLookup.Count == 0)
            {
                unavailableReason = "The game's weapon registry is not ready yet. Try again after reaching the main menu.";
                return false;
            }

            var byPrefab = new Dictionary<string, DiscoveredMissile>(StringComparer.Ordinal);
            var inspectedPrefabs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in weaponLookup)
            {
                WeaponMount mount = pair.Value;
                WeaponInfo info = mount?.info;
                GameObject prefab = info?.weaponPrefab;
                if (prefab == null)
                    continue;

                string prefabName = (prefab.name ?? string.Empty).Trim();
                if (prefabName.Length == 0 || !inspectedPrefabs.Add(prefabName))
                    continue;

                if (SeekerProfileRegistry.TryGetByPrefab(prefabName, out _))
                    continue;

                bool hasIrSeeker;
                try
                {
                    hasIrSeeker = prefab.GetComponentInChildren<IRSeeker>(true) != null;
                }
                catch
                {
                    hasIrSeeker = false;
                }

                if (!hasIrSeeker)
                    continue;

                string displayName = string.IsNullOrWhiteSpace(info.weaponName)
                    ? prefabName
                    : info.weaponName.Trim();
                byPrefab.Add(prefabName, new DiscoveredMissile(prefabName, displayName));
            }

            missing = byPrefab.Values
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.PrefabName, StringComparer.Ordinal)
                .ToList();
            return true;
        }

        private void ShowConfirmation(List<DiscoveredMissile> missing)
        {
            _pending.Clear();
            _pending.AddRange(missing);
            _confirmationDialog = true;
            _dialogTitle = "New IR missiles found";
            _dialogMessage = $"Do you want to add {_pending.Count} seeker profile{(_pending.Count == 1 ? string.Empty : "s")} to the folder?";
            OpenDialog();
            Plugin.Log.LogInfo($"[IR discovery] Prompting for consent to create {_pending.Count} seeker profile(s).");
        }

        private void ShowInformation(string title, string message)
        {
            _pending.Clear();
            _confirmationDialog = false;
            _dialogTitle = title ?? string.Empty;
            _dialogMessage = message ?? string.Empty;
            OpenDialog();
        }

        private void OpenDialog()
        {
            _scrollPosition = Vector2.zero;
            _windowRect = new Rect(
                Mathf.Max(12f, (Screen.width - 560f) * 0.5f),
                Mathf.Max(12f, (Screen.height - 360f) * 0.5f),
                Mathf.Min(560f, Screen.width - 24f),
                Mathf.Min(360f, Screen.height - 24f));

            if (!_dialogVisible)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _dialogVisible = true;
        }

        private void OnGUI()
        {
            if (!_dialogVisible)
                return;

            GUI.depth = -10000;
            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            int windowId = GetInstanceID();
            _windowRect = GUI.ModalWindow(windowId, _windowRect, DrawDialog, _dialogTitle);
        }

        private void DrawDialog(int windowId)
        {
            GUILayout.Space(8f);
            GUILayout.Label(_dialogMessage);

            if (_confirmationDialog && _pending.Count > 0)
            {
                GUILayout.Space(8f);
                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(210f));
                for (int i = 0; i < _pending.Count; i++)
                {
                    DiscoveredMissile missile = _pending[i];
                    GUILayout.Label($"• {missile.DisplayName}  [{missile.PrefabName}]");
                }
                GUILayout.EndScrollView();
                GUILayout.Label("Generated profiles use the embedded default seeker audio and remain fully editable.");
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_confirmationDialog)
            {
                if (GUILayout.Button("Yes", GUILayout.Width(120f), GUILayout.Height(32f)))
                    AcceptProfiles();
                GUILayout.Space(12f);
                if (GUILayout.Button("No", GUILayout.Width(120f), GUILayout.Height(32f)))
                    DeclineProfiles();
            }
            else if (GUILayout.Button("OK", GUILayout.Width(120f), GUILayout.Height(32f)))
            {
                CloseDialog();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void AcceptProfiles()
        {
            SuppressPendingAutomaticPrompts();
            var profiles = _pending.Select(CreateDefaultProfile).ToList();
            int requested = profiles.Count;
            int created = SeekerProfileRegistry.CreateEditableProfileFiles(profiles, out var failures);

            if (created > 0)
                _refreshProfiles?.Invoke();

            Plugin.Log.LogInfo($"[IR discovery] User accepted profile creation: requested={requested}, created={created}, failed={failures.Count}.");
            string result = $"Created {created} seeker profile file{(created == 1 ? string.Empty : "s")}.";
            if (failures.Count > 0)
                result += " Could not create: " + string.Join(", ", failures.ToArray()) + ". Check the log for details.";

            ShowInformation("IR missile profiles", result);
        }

        private static SeekerSoundProfile CreateDefaultProfile(DiscoveredMissile missile)
        {
            return new SeekerSoundProfile
            {
                PrefabName = missile.PrefabName,
                WeaponName = missile.DisplayName,
                FolderName = "SeekerNoises",
                FlareReject = true,
                CagedFile = "Aim9Caged.wav",
                UnCagedFile = "Aim9UnCaged.wav",
                EnvSky = "Aim9EnvSky.wav",
                EnvGnd = "Aim9EnvGnd.wav"
            };
        }

        private void DeclineProfiles()
        {
            SuppressPendingAutomaticPrompts();
            Plugin.Log.LogInfo($"[IR discovery] User declined creation of {_pending.Count} seeker profile(s).");
            CloseDialog();
        }

        private void SuppressPendingAutomaticPrompts()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                string prefabName = _pending[i]?.PrefabName;
                if (!string.IsNullOrWhiteSpace(prefabName))
                    _suppressedAutomaticPrefabs.Add(prefabName);
            }

            ResetAutomaticCandidate();
        }

        private void CloseDialog()
        {
            _dialogVisible = false;
            _confirmationDialog = false;
            _pending.Clear();
            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
        }

        private void OnDisable()
        {
            if (_dialogVisible)
                CloseDialog();
        }

        private sealed class DiscoveredMissile
        {
            public readonly string PrefabName;
            public readonly string DisplayName;

            public DiscoveredMissile(string prefabName, string displayName)
            {
                PrefabName = prefabName;
                DisplayName = displayName;
            }
        }
    }
}
