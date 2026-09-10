using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace com.basicGrowl.Services
{
    internal sealed partial class SeekerToneServices
    {
        private static string FormatAudioSourceState(AudioSource src)
        {
            if (src == null) return "<null>";
            string clip = src.clip != null ? src.clip.name : "<none>";
            return $"clip={clip} play={src.isPlaying} vol={src.volume:F2} pitch={src.pitch:F2}";
        }

        private void PrimeProfileClips(com.basicGrowl.Seeker.SeekerSoundProfile profile)
        {
            if (profile == null)
                return;

            GetOrLoadClip(profile.FolderName, profile.CagedFile);
            GetOrLoadClip(profile.FolderName, profile.UnCagedFile);
            GetOrLoadClip(profile.FolderName, profile.EnvSky);
            GetOrLoadClip(profile.FolderName, profile.EnvGnd);
        }

        private void ApplyTone(
            string folder,
            string cagedFile,
            string uncagedFile,
            string envFile,
            float pitch,
            float cagedVol,
            float uncagedVol,
            float envVol,
            bool bypassEnvCagedDuck,
            float dt)
        {
            // CAGED required
            if (string.IsNullOrEmpty(cagedFile))
            {
                StopTone();
                return;
            }

            float masterVolume = GetMasterVolume01();

            var cagedClip = GetOrLoadClip(
                folder,
                cagedFile,
                ref _cagedClipFolder,
                ref _cagedClipFile,
                ref _cagedClipKey,
                ref _cagedClipCached);
            if (cagedClip != null)
            {
                if (_srcCaged.clip != cagedClip) _srcCaged.clip = cagedClip;
                _srcCaged.pitch = pitch;

                // Keep caged source running; volume controls audibility.
                if (!_srcCaged.isPlaying) _srcCaged.Play();
            }
            else
            {
                // Keep processing uncaged/env even while caged is loading/missing.
                if (_srcCaged.isPlaying) _srcCaged.Stop();
                _srcCaged.clip = null;
                _srcCaged.volume = 0f;
            }

            // UNCAGED optional
            if (string.IsNullOrEmpty(uncagedFile))
            {
                if (_srcUncaged.isPlaying) _srcUncaged.Stop();
                _srcUncaged.clip = null;
            }
            else
            {
                var uncagedClip = GetOrLoadClip(
                    folder,
                    uncagedFile,
                    ref _uncagedClipFolder,
                    ref _uncagedClipFile,
                    ref _uncagedClipKey,
                    ref _uncagedClipCached);
                if (uncagedClip == null)
                {
                    // Requested uncaged clip is missing/loading: do not leave stale clip running.
                    _srcUncaged.volume = 0f;
                    if (_srcUncaged.isPlaying) _srcUncaged.Stop();
                    _srcUncaged.clip = null;
                }
                else
                {
                    if (_srcUncaged.clip != uncagedClip) _srcUncaged.clip = uncagedClip;
                    _srcUncaged.pitch = 1f;
                    _srcUncaged.volume = Mathf.Clamp01(uncagedVol * masterVolume);

                    if (_srcUncaged.volume > 0.001f)
                    {
                        if (!_srcUncaged.isPlaying) _srcUncaged.Play();
                    }
                    else
                    {
                        if (_srcUncaged.isPlaying) _srcUncaged.Stop();
                        _srcUncaged.clip = null;
                    }
                }
            }

            ApplyEnvTone(folder, envFile, pitch, envVol * masterVolume, dt);

            // Attenuate caged by the live env mix (after env fades are applied this tick).
            float outCagedVol;
            if (cagedClip == null)
            {
                outCagedVol = 0f;
            }
            else if (bypassEnvCagedDuck)
            {
                outCagedVol = Mathf.Clamp01(cagedVol);
            }
            else
            {
                float liveEnvBlend = GetLiveEnvBlend01();
                outCagedVol = Mathf.Clamp01(cagedVol * (1f - liveEnvBlend));
            }

            _srcCaged.volume = Mathf.Clamp01(outCagedVol * masterVolume);
        }

        private void ApplyEnvTone(string folder, string envFile, float pitch, float envVol, float dt)
        {
            float vol = Mathf.Clamp01(envVol);
            float safeDt = Mathf.Max(0.001f, dt);
            float step = (EnvSwitchFadeTime <= 0.001f) ? 1f : (safeDt / EnvSwitchFadeTime);

            if (string.IsNullOrEmpty(envFile) || vol <= 0.001f)
            {
                FadeEnvSource(_srcEnvA, 0f, pitch, step, clearWhenSilent: true);
                FadeEnvSource(_srcEnvB, 0f, pitch, step, clearWhenSilent: true);
                return;
            }

            var envClip = GetOrLoadClip(
                folder,
                envFile,
                ref _envClipFolder,
                ref _envClipFile,
                ref _envClipKey,
                ref _envClipCached);
            if (envClip == null)
            {
                // Requested env clip is missing/loading: fade out current env clips instead of holding stale audio.
                FadeEnvSource(_srcEnvA, 0f, pitch, step, clearWhenSilent: true);
                FadeEnvSource(_srcEnvB, 0f, pitch, step, clearWhenSilent: true);
                return;
            }

            bool aIsTarget = _srcEnvA.clip == envClip;
            bool bIsTarget = _srcEnvB.clip == envClip;

            if (!aIsTarget && !bIsTarget)
            {
                // Assign new env clip to the quieter source so old clip can fade out.
                var assign = (_srcEnvA.volume <= _srcEnvB.volume) ? _srcEnvA : _srcEnvB;
                assign.clip = envClip;
                assign.pitch = pitch;
                if (!assign.isPlaying) assign.Play();

                aIsTarget = assign == _srcEnvA;
                bIsTarget = assign == _srcEnvB;
            }

            FadeEnvSource(_srcEnvA, aIsTarget ? vol : 0f, pitch, step, clearWhenSilent: !aIsTarget);
            FadeEnvSource(_srcEnvB, bIsTarget ? vol : 0f, pitch, step, clearWhenSilent: !bIsTarget);
        }

        private float GetLiveEnvBlend01()
        {
            float liveEnvVol = GetLiveSourceVolume(_srcEnvA) + GetLiveSourceVolume(_srcEnvB);
            float masterVolume = GetMasterVolume01();
            float denom = Mathf.Max(0.0001f, EnvOverlayMaxVolume * masterVolume);
            return Mathf.Clamp01(liveEnvVol / denom);
        }

        private float GetMasterVolume01()
        {
            if (_plugin == null) return 1f;
            return Mathf.Clamp01(_plugin.SeekerVolume01);
        }

        private static float GetLiveSourceVolume(AudioSource src)
        {
            if (src == null || src.clip == null) return 0f;
            if (!src.isPlaying && src.volume <= 0.001f) return 0f;
            return Mathf.Clamp01(src.volume);
        }

        private static void FadeEnvSource(AudioSource src, float targetVol, float pitch, float step, bool clearWhenSilent)
        {
            if (src == null) return;

            src.pitch = pitch;
            src.volume = Mathf.Clamp01(Mathf.MoveTowards(src.volume, targetVol, step));

            if (src.volume > 0.001f || targetVol > 0.001f)
            {
                if (!src.isPlaying && src.clip != null) src.Play();
                return;
            }

            if (src.isPlaying) src.Stop();
            if (clearWhenSilent) src.clip = null;
        }

        private void StopTone()
        {
            ResetAndStopSource(_srcCaged);
            ResetAndStopSource(_srcUncaged);
            ResetAndStopSource(_srcEnvA);
            ResetAndStopSource(_srcEnvB);
        }

        private void ClearActiveClipSlots()
        {
            ClearClipSlot(ref _cagedClipFolder, ref _cagedClipFile, ref _cagedClipKey, ref _cagedClipCached);
            ClearClipSlot(ref _uncagedClipFolder, ref _uncagedClipFile, ref _uncagedClipKey, ref _uncagedClipCached);
            ClearClipSlot(ref _envClipFolder, ref _envClipFile, ref _envClipKey, ref _envClipCached);
        }

        private static void ClearClipSlot(ref string folder, ref string fileName, ref string key, ref AudioClip clip)
        {
            folder = null;
            fileName = null;
            key = null;
            clip = null;
        }

        private static void ResetAndStopSource(AudioSource src)
        {
            if (src == null) return;

            if (src.isPlaying) src.Stop();
            src.clip = null;
            src.pitch = 1f;
            src.volume = 0f;
        }

        private AudioClip GetOrLoadClip(string folder, string fileName)
        {
            string unusedFolder = null;
            string unusedFile = null;
            string unusedKey = null;
            AudioClip unusedClip = null;
            return GetOrLoadClip(folder, fileName, ref unusedFolder, ref unusedFile, ref unusedKey, ref unusedClip);
        }

        private AudioClip GetOrLoadClip(
            string folder,
            string fileName,
            ref string cachedFolder,
            ref string cachedFile,
            ref string cachedKey,
            ref AudioClip cachedClip)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            folder = string.IsNullOrEmpty(folder) ? "SeekerNoises" : folder;
            if (!string.Equals(cachedFolder, folder, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cachedFile, fileName, StringComparison.OrdinalIgnoreCase))
            {
                cachedFolder = folder;
                cachedFile = fileName;
                cachedKey = folder + "/" + fileName;
                cachedClip = null;
            }

            var key = cachedKey;

            if (cachedClip != null)
                return cachedClip;

            if (_missing.Contains(key)) return null;

            if (_clipCache.TryGetValue(key, out var clip) && clip != null)
            {
                cachedClip = clip;
                return clip;
            }

            if (_loading.Contains(key))
                return null;

            string dllDir = Path.GetDirectoryName(_plugin.Info.Location);
            string fullPath = Path.Combine(dllDir ?? ".", folder, fileName);
            if (!File.Exists(fullPath))
            {
                if (TryLoadEmbeddedClip(folder, fileName, key, out clip))
                {
                    cachedClip = clip;
                    return clip;
                }

                Plugin.Log.LogWarning($"Missing seeker sound: {fullPath}");
                _missing.Add(key);
                return null;
            }

            _loading.Add(key);
            int generation = _audioLoadGeneration;
            _plugin.StartCoroutine(LoadWav(folder, fileName, key, generation));
            return null;
        }

        private IEnumerator LoadWav(string folder, string fileName, string cacheKey, int generation)
        {
            string dllDir = Path.GetDirectoryName(_plugin.Info.Location);
            string fullPath = Path.Combine(dllDir ?? ".", folder, fileName);
            var uri = new Uri(fullPath).AbsoluteUri;

            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (generation == _audioLoadGeneration)
                    {
                        _loading.Remove(cacheKey);
                        if (!TryLoadEmbeddedClip(folder, fileName, cacheKey, out var embeddedClip))
                        {
                            Plugin.Log.LogWarning($"Failed to load WAV '{fileName}': {req.error}");
                            _missing.Add(cacheKey);
                        }
                    }

                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(req);
                clip.name = fileName;

                if (_disposed || generation != _audioLoadGeneration)
                {
                    UnityEngine.Object.Destroy(clip);
                    yield break;
                }

                _clipCache[cacheKey] = clip;
                _loading.Remove(cacheKey);
            }
        }

        private bool TryLoadEmbeddedClip(string folder, string fileName, string cacheKey, out AudioClip clip)
        {
            clip = null;
            if (_disposed || !EmbeddedAudioClipLoader.TryCreateClip(folder, fileName, out clip) || clip == null)
                return false;

            _clipCache[cacheKey] = clip;
            _missing.Remove(cacheKey);
            return true;
        }

        private void ReleaseAudioClips()
        {
            _audioLoadGeneration++;
            StopTone();
            ClearActiveClipSlots();

            foreach (var pair in _clipCache)
            {
                if (pair.Value != null)
                    UnityEngine.Object.Destroy(pair.Value);
            }

            _clipCache.Clear();
            _loading.Clear();
            _missing.Clear();
        }

        internal void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseAudioClips();
        }
    }
}
