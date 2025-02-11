using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RandomHornMod
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class RandomHornMod : BaseUnityPlugin
    {
        private const string ModGUID = "HenDGS.RandomHorn";
        private const string ModName = "Lethal Company Random Horn";
        private const string ModVersion = "1.0.0";

        private readonly Harmony _harmony = new Harmony(ModGUID);
        internal static BepInEx.Logging.ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{ModGUID} has loaded successfully.");
            _harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(NoisemakerProp), "ItemActivate")]
    class AudioPatch
    {
        [HarmonyPrefix]
        private static void Prefix(NoisemakerProp __instance)
        {
            if (__instance == null || __instance.noiseSFX == null || __instance.noiseSFX.Length == 0)
                return;

            string randomSoundPath = ReadMp3Files.GetRandomMp3FilePath("Music");
            if (!string.IsNullOrEmpty(randomSoundPath))
            {
                Patches.LoadAudioClipAsync(randomSoundPath, clip =>
                {
                    if (clip != null)
                    {
                        __instance.noiseSFX[0] = clip;
                        __instance.noiseSFXFar[0] = clip;
                    }
                });
            }
        }
    }

    static class ReadMp3Files
    {
        private static readonly List<string> CachedMp3Files = new List<string>();
        private static bool _isLoaded = false;

        private static readonly BepInEx.Logging.ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RandomHorn");

        public static List<string> ReadMp3FilesInDirectory(string path)
        {
            if (_isLoaded) return CachedMp3Files;

            string musicFolderPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            CachedMp3Files.Clear();

            if (Directory.Exists(musicFolderPath))
            {
                CachedMp3Files.AddRange(Directory.GetFiles(musicFolderPath, "*.mp3"));
                _isLoaded = true;
            }
            else
            {
                Log.LogWarning($"Music folder not found: {musicFolderPath}");
            }

            return CachedMp3Files;
        }

        public static string GetRandomMp3FilePath(string path)
        {
            List<string> mp3Files = ReadMp3FilesInDirectory(path);
            if (mp3Files.Count == 0) return null;

            System.Random rand = new System.Random();
            return mp3Files[rand.Next(mp3Files.Count)];
        }
    }

    static class Patches
    {
        private const float MaxAudioLength = 5.0f;
        
        private static readonly Dictionary<string, AudioClip> AudioClipCache = new Dictionary<string, AudioClip>();

        private static readonly BepInEx.Logging.ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RandomHorn");

        public static async void LoadAudioClipAsync(string filepath, Action<AudioClip> callback)
        {
            if (AudioClipCache.TryGetValue(filepath, out AudioClip cachedClip))
            {
                callback?.Invoke(cachedClip);
                return;
            }

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(filepath, AudioType.MPEG))
            {
                request.SendWebRequest();
                while (!request.isDone)
                {
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Log.LogError($"Error loading sound: {filepath} - {request.error}");
                    callback?.Invoke(null);
                    return;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || clip.loadState != AudioDataLoadState.Loaded)
                {
                    Log.LogError($"Failed to load audio clip: {filepath}");
                    callback?.Invoke(null);
                    return;
                }

                clip.name = Path.GetFileName(filepath);
                
                if (clip.length > MaxAudioLength)
                {
                    Log.LogWarning(
                        $"Audio clip {clip.name} exceeds max length ({clip.length}s). Trimming to {MaxAudioLength}s.");
                    clip = TrimAudioClip(clip, MaxAudioLength);
                }

                AudioClipCache[filepath] = clip;
                callback?.Invoke(clip);
            }
        }

        private static AudioClip TrimAudioClip(AudioClip clip, float maxLength)
        {
            int samplesToKeep = Mathf.FloorToInt(clip.frequency * maxLength);
            float[] newSamples = new float[samplesToKeep * clip.channels];

            clip.GetData(newSamples, 0);

            AudioClip trimmedClip = AudioClip.Create(clip.name + "_trimmed", samplesToKeep, clip.channels,
                clip.frequency, false);
            trimmedClip.SetData(newSamples, 0);

            return trimmedClip;
        }
    }
}