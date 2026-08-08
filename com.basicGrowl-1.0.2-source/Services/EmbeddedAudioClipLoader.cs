using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace com.basicGrowl.Services
{
    internal static class EmbeddedAudioClipLoader
    {
        private const string DefaultFolderName = "SeekerNoises";
        private const string ResourcePrefix = "com.basicGrowl.EmbeddedAudio.SeekerNoises.";

        private static readonly Dictionary<string, string> ResourceNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Aim9Caged.wav", ResourcePrefix + "Aim9Caged.imaadpcm" },
                { "Aim9EnvGnd.wav", ResourcePrefix + "Aim9EnvGnd.imaadpcm" },
                { "Aim9EnvSky.wav", ResourcePrefix + "Aim9EnvSky.imaadpcm" },
                { "Aim9PCaged.wav", ResourcePrefix + "Aim9PCaged.imaadpcm" },
                { "Aim9PUnCaged.wav", ResourcePrefix + "Aim9PUnCaged.imaadpcm" },
                { "Aim9UnCaged.wav", ResourcePrefix + "Aim9UnCaged.imaadpcm" },
                { "Aim9UncagedFlared.wav", ResourcePrefix + "Aim9UncagedFlared.imaadpcm" }
            };

        private static readonly int[] ImaAdpcmIndexTable =
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        private static readonly int[] ImaAdpcmStepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
            19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
            5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        internal static bool TryCreateClip(string folder, string fileName, out AudioClip clip)
        {
            clip = null;
            if (!string.Equals(folder, DefaultFolderName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(fileName) ||
                !ResourceNames.TryGetValue(fileName, out var resourceName))
            {
                return false;
            }

            try
            {
                var assembly = typeof(EmbeddedAudioClipLoader).Assembly;
                using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resourceStream == null)
                    {
                        Plugin.Log?.LogWarning(
                            $"[EmbeddedAudio] Missing resource '{resourceName}'. Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
                        return false;
                    }

                    using (var reader = new BinaryReader(resourceStream))
                    {
                        var magic = reader.ReadBytes(4);
                        if (magic.Length != 4 || magic[0] != 0x49 || magic[1] != 0x41 || magic[2] != 0x44 || magic[3] != 0x50)
                        {
                            Plugin.Log?.LogWarning($"[EmbeddedAudio] Resource '{resourceName}' is not an IMA ADPCM payload.");
                            return false;
                        }

                        var sampleRate = reader.ReadInt32();
                        var channels = reader.ReadInt32();
                        var sampleCount = reader.ReadInt32();
                        var predictor = reader.ReadInt16();
                        var stepIndex = reader.ReadByte();
                        reader.ReadByte();

                        if (sampleRate <= 0 || channels != 1 || sampleCount <= 0 || stepIndex >= ImaAdpcmStepTable.Length)
                        {
                            Plugin.Log?.LogWarning(
                                $"[EmbeddedAudio] Invalid metadata in '{resourceName}': samples={sampleCount}, channels={channels}, sampleRate={sampleRate}, stepIndex={stepIndex}.");
                            return false;
                        }

                        var adpcmBytes = reader.ReadBytes((int)(resourceStream.Length - resourceStream.Position));
                        var requiredPayloadBytes = sampleCount / 2;
                        if (adpcmBytes.Length < requiredPayloadBytes)
                        {
                            Plugin.Log?.LogWarning(
                                $"[EmbeddedAudio] Truncated resource '{resourceName}': payloadBytes={adpcmBytes.Length}, required={requiredPayloadBytes}.");
                            return false;
                        }

                        var samples = new float[sampleCount];
                        var sampleIndex = 0;
                        samples[sampleIndex++] = predictor / 32768.0f;

                        for (var i = 0; i < adpcmBytes.Length && sampleIndex < sampleCount; i++)
                        {
                            DecodeImaAdpcmNibble(adpcmBytes[i] & 0x0f, ref predictor, ref stepIndex);
                            samples[sampleIndex++] = predictor / 32768.0f;

                            if (sampleIndex >= sampleCount)
                                break;

                            DecodeImaAdpcmNibble((adpcmBytes[i] >> 4) & 0x0f, ref predictor, ref stepIndex);
                            samples[sampleIndex++] = predictor / 32768.0f;
                        }

                        if (sampleIndex != sampleCount)
                        {
                            Plugin.Log?.LogWarning(
                                $"[EmbeddedAudio] Resource '{resourceName}' decoded {sampleIndex} of {sampleCount} samples.");
                            return false;
                        }

                        clip = AudioClip.Create(fileName, sampleCount, channels, sampleRate, false);
                        clip.SetData(samples, 0);
                        Plugin.Log?.LogInfo(
                            $"[EmbeddedAudio] Loaded '{fileName}': {sampleCount} samples, {sampleRate} Hz, {sampleCount / (float)sampleRate:F2}s, payloadBytes={adpcmBytes.Length}.");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                if (clip != null)
                {
                    UnityEngine.Object.Destroy(clip);
                    clip = null;
                }

                Plugin.Log?.LogWarning($"[EmbeddedAudio] Failed to decode '{fileName}': {e.Message}");
                return false;
            }
        }

        private static void DecodeImaAdpcmNibble(int code, ref short predictor, ref byte stepIndex)
        {
            var clampedIndex = Mathf.Clamp(stepIndex, 0, ImaAdpcmStepTable.Length - 1);
            var step = ImaAdpcmStepTable[clampedIndex];
            var diff = step >> 3;
            if ((code & 4) != 0) diff += step;
            if ((code & 2) != 0) diff += step >> 1;
            if ((code & 1) != 0) diff += step >> 2;

            var nextPredictor = (int)predictor;
            nextPredictor += (code & 8) != 0 ? -diff : diff;

            predictor = (short)Mathf.Clamp(nextPredictor, short.MinValue, short.MaxValue);
            stepIndex = (byte)Mathf.Clamp(
                clampedIndex + ImaAdpcmIndexTable[code & 0x0f],
                0,
                ImaAdpcmStepTable.Length - 1);
        }
    }
}
