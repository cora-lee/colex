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

        [Tooltip("Width of the preview box as a fraction of the screen width, so it " +
            "scales with the window instead of staying a fixed pixel size that can " +
            "overflow or shrink to nothing on a different resolution or aspect ratio.")]
        [Range(0.08f, 0.35f)]
        public float widthFraction = 0.18f;

        [Tooltip("Margin from the screen edges, as a fraction of screen height.")]
        [Range(0.005f, 0.05f)]
        public float marginFraction = 0.02f;

        // Matches the browser's downscaled thumbnail (220x165), so the box keeps
        // the feed's real proportions instead of stretching it to a different shape.
        const float AspectRatio = 220f / 165f;

        Texture2D texture;
        int lastVersion = -1;
        Texture2D borderTex;

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

            // A single flat-color texture for a thin border, drawn behind the
            // camera texture. Avoids GUI.Box: Unity's default skin renders its
            // own dark panel fill, which on top of an also-dark camera frame
            // reads as two stacked black boxes instead of one framed preview.
            borderTex = new Texture2D(1, 1);
            borderTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.85f));
            borderTex.Apply();
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

            float w = Screen.width * widthFraction;
            float h = w / AspectRatio;
            float margin = Screen.height * marginFraction;

            float x = Screen.width - w - margin;
            float y = Screen.height - h - margin;

            const float border = 2f;
            GUI.DrawTexture(new Rect(x - border, y - border, w + border * 2f, h + border * 2f), borderTex);
            // The browser already mirrors the feed for the player standing in
            // front of the camera, so draw it exactly as received.
            GUI.DrawTexture(new Rect(x, y, w, h), texture, ScaleMode.ScaleToFit);
        }
    }
}
