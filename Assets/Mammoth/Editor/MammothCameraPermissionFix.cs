using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Fix Camera Permission (restart Unity).
    /// When Unity is opened from Unity Hub, macOS can attribute the camera
    /// request to the Hub and refuse it silently, so Unity never appears in
    /// System Settings > Privacy &amp; Security > Camera. This saves everything,
    /// clears Unity's camera permission record (tccutil), then relaunches this
    /// same Unity editor directly (not through the Hub) on this project. After
    /// it reopens, pressing Play asks for camera access with the system prompt.
    /// </summary>
    public static class MammothCameraPermissionFix
    {
        [MenuItem("Mammoth/Fix Camera Permission (restart Unity)", false, 200)]
        static void Run()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                UnityEngine.Debug.LogWarning("[Mammoth] This fix is for macOS only.");
                return;
            }
            if (EditorApplication.isPlaying)
            {
                UnityEngine.Debug.LogWarning("[Mammoth] Stop Play first, then run Fix Camera Permission again.");
                return;
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            Shell("/usr/bin/tccutil", "reset Camera com.unity3d.UnityEditor5.x");
            Shell("/usr/bin/tccutil", "reset Camera com.unity3d.unityhub");

            string app = EditorApplication.applicationPath;           // .../Unity.app
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            // Detached relaunch through LaunchServices ("open"), after this editor quits.
            string cmd = $"sleep 4; /usr/bin/open -n -a '{app}' --args -projectPath '{project}'";
            var psi = new ProcessStartInfo("/usr/bin/nohup", $"/bin/sh -c \"{cmd}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            Process.Start(psi);
            UnityEngine.Debug.Log("[Mammoth] Restarting Unity directly to request camera access...");
            EditorApplication.Exit(0);
        }

        static void Shell(string file, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                p.WaitForExit(5000);
                UnityEngine.Debug.Log("[Mammoth] " + file + " " + args + ": " + p.StandardOutput.ReadToEnd().Trim() + " " + p.StandardError.ReadToEnd().Trim());
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[Mammoth] " + file + " failed: " + e.Message);
            }
        }
    }
}
