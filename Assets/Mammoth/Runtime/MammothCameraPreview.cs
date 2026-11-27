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
    ///
    /// The bottom-right corner is a fixed anchor (margin from the screen edges);
    /// the player can resize the box by dragging its top-left corner with the
    /// mouse, which only changes widthFraction (height follows the fixed
    /// aspect ratio automatically).
    /// </summary>
    public class MammothCameraPreview : MonoBehaviour
    {
        public MammothStateReceiver receiver;
        public bool show = true;

        [Tooltip("Width of the preview box as a fraction of the screen width, so it " +
            "scales with the window instead of staying a fixed pixel size that can " +
            "overflow or shrink to nothing on a different resolution or aspect ratio.")]
        [Range(MinWidthFraction, MaxWidthFraction)]
        public float widthFraction = 0.18f;

        [Tooltip("Margin from the screen edges, as a fraction of screen height.")]
        [Range(0.005f, 0.05f)]
        public float marginFraction = 0.02f;

        const float MinWidthFraction = 0.08f;
        const float MaxWidthFraction = 0.4f;

        // Matches the browser's downscaled thumbnail (220x165), so the box keeps
        // the feed's real proportions instead of stretching it to a different shape.
        const float AspectRatio = 220f / 165f;

        // Size of the draggable grip square at the top-left corner, in pixels.
        const float HandleSize = 20f;

        Texture2D texture;
        int lastVersion = -1;
        Texture2D borderTex;
        Texture2D handleTex;

        bool dragging;
        Vector2 dragStartMouse;
        float dragStartWidthFraction;

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

            // Flat-color textures for the border and the resize grip, drawn behind/
            // over the camera texture. Avoids GUI.Box: Unity's default skin renders
            // its own dark panel fill, which on top of an also-dark camera frame
            // reads as two stacked black boxes instead of one framed preview.
            borderTex = MakeFlatTex(new Color(1f, 1f, 1f, 0.85f));
            handleTex = MakeFlatTex(new Color(1f, 1f, 1f, 0.95f));
        }

        static Texture2D MakeFlatTex(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            return tex;
        }

        void Update()
        {
            if (receiver != null)
            {
                int v = receiver.FrameVersion;
                if (v != lastVersion)
                {
                    byte[] bytes = receiver.LatestFrameJpeg;
                    // LoadImage resizes the Texture2D itself to match the JPEG.
                    if (bytes != null && bytes.Length > 0 && texture.LoadImage(bytes, false)) lastVersion = v;
                }
            }

            HandleResizeDrag();
        }

        Rect GetBoxRect()
        {
            float w = Screen.width * widthFraction;
            float h = w / AspectRatio;
            float margin = Screen.height * marginFraction;
            float x = Screen.width - w - margin;
            float y = Screen.height - h - margin;
            return new Rect(x, y, w, h);
        }

        static Rect GetHandleRect(Rect box)
        {
            return new Rect(box.x - HandleSize * 0.5f, box.y - HandleSize * 0.5f, HandleSize, HandleSize);
        }

        // Input.mousePosition has its origin at the bottom-left of the screen;
        // GUI/OnGUI rects have theirs at the top-left, so the y axis needs flipping.
        static Vector2 MouseToGuiSpace() => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        void HandleResizeDrag()
        {
            if (!show || lastVersion < 0) return;

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mouse = MouseToGuiSpace();
                if (GetHandleRect(GetBoxRect()).Contains(mouse))
                {
                    dragging = true;
                    dragStartMouse = mouse;
                    dragStartWidthFraction = widthFraction;
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                dragging = false;
            }

            if (dragging && Input.GetMouseButton(0))
            {
                Vector2 mouse = MouseToGuiSpace();
                // The box's bottom-right corner is a fixed anchor, so dragging the
                // top-left handle further left/up grows the box; dragging it
                // toward the anchor shrinks it. Horizontal movement alone decides
                // the size — height always follows from the aspect ratio.
                float deltaX = dragStartMouse.x - mouse.x;
                float deltaFraction = deltaX / Screen.width;
                widthFraction = Mathf.Clamp(dragStartWidthFraction + deltaFraction, MinWidthFraction, MaxWidthFraction);
            }
        }

        void OnGUI()
        {
            if (!show || texture == null || lastVersion < 0) return;

            Rect box = GetBoxRect();
            const float border = 2f;
            GUI.DrawTexture(new Rect(box.x - border, box.y - border, box.width + border * 2f, box.height + border * 2f), borderTex);
            // The browser already mirrors the feed for the player standing in
            // front of the camera, so draw it exactly as received.
            GUI.DrawTexture(box, texture, ScaleMode.ScaleToFit);

            // Small grip square at the top-left corner, so the resize handle is
            // discoverable instead of being an invisible hit area.
            GUI.DrawTexture(GetHandleRect(box), handleTex);
        }
    }
}
