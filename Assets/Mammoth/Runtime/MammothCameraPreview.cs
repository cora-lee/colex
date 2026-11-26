using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Small camera-preview overlay in the bottom-right corner of the actual
    /// Unity game screen, so players can see their own tracking without the
    /// preview covering the route — matches the design spec's "a small camera
    /// preview may stay visible during play without covering the route."
    ///
    /// The browser page POSTs a downscaled JPEG thumbnail to /frame every ~200ms
    /// (separate from the 30 Hz /state gesture traffic); MammothStateReceiver
    /// stores the latest bytes, and this component just decodes and draws them.
    /// IMGUI, same style as MammothDebugHud, so no Canvas/UI package is needed.
    /// </summary>
    public class MammothCameraPreview : MonoBehaviour
    {
        public MammothStateReceiver receiver;
        public bool show = true;
        [Tooltip("Size in pixels of the preview box in the corner.")]
        public Vector2 size = new Vector2(220, 165);
        [Tooltip("Margin from the screen edges, in pixels.")]
        public float margin = 16f;

        Texture2D texture;
        int lastVersion = -1;
        GUIStyle frameStyle;

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        }

        void Update()
        {
            if (receiver == null) return;
            int v = receiver.FrameVersion;
            if (v == lastVersion) return;

            byte[] bytes = receiver.LatestFrameJpeg;
            if (bytes == null || bytes.Length == 0) return;

            // LoadImage resizes the Texture2D itself to match the JPEG.
            if (texture.LoadImage(bytes, false)) lastVersion = v;
        }

        void OnGUI()
        {
            if (!show || texture == null || lastVersion < 0) return;
            if (frameStyle == null) frameStyle = new GUIStyle(GUI.skin.box);

            float x = Screen.width - size.x - margin;
            float y = Screen.height - size.y - margin;

            GUI.Box(new Rect(x - 4, y - 4, size.x + 8, size.y + 8), GUIContent.none, frameStyle);
            // The browser already mirrors the feed for the player standing in
            // front of the camera, so draw it exactly as received.
            GUI.DrawTexture(new Rect(x, y, size.x, size.y), texture, ScaleMode.ScaleToFit);
        }
    }
}
