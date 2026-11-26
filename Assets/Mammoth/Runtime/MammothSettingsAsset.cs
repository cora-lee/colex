using System.IO;
using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Loads StreamingAssets/MammothMapping/settings.json (and the revision from manifest.json).
    /// Use the same export revision the Python lab is running, otherwise lane spacing and
    /// jump height in Unity will not match what the mapping owner tested.
    /// </summary>
    public static class MammothSettingsAsset
    {
        public const string Folder = "MammothMapping";

        [System.Serializable]
        class Manifest { public string settingsRevision; public string exportedAtUtc; public MammothSettings settings; }

        public static string Revision { get; private set; } = "unknown";

        static MammothSettings cached;

        public static MammothSettings Load(bool forceReload = false)
        {
            if (cached != null && !forceReload) return cached;
            var settings = new MammothSettings();
            string dir = Path.Combine(Application.streamingAssetsPath, Folder);
            string settingsPath = Path.Combine(dir, "settings.json");
            string manifestPath = Path.Combine(dir, "manifest.json");

            try
            {
                if (File.Exists(settingsPath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(settingsPath), settings);
                else
                    Debug.LogWarning("[Mammoth] " + settingsPath + " not found, using defaults.");

                if (File.Exists(manifestPath))
                {
                    var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                    if (m != null && !string.IsNullOrEmpty(m.settingsRevision)) Revision = m.settingsRevision;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Mammoth] Could not read mapping settings: " + e.Message);
            }

            var errors = settings.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError("[Mammoth] settings.json is invalid, using defaults:\n" + string.Join("\n", errors));
                settings = new MammothSettings();
            }
            cached = settings;
            return cached;
        }
    }
}
