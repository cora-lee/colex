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
    ///
    /// Clicking anywhere on the box itself (not the resize handle) toggles a
    /// diagnostics popup, drawn natively in Unity, that shows per-zone
    /// recognition status (left/center/right), calibration progress, and the
    /// connection state. All of that already arrives over /state — this just
    /// surfaces it in Unity instead of needing to check the browser page.
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

        // A mouse-down/up pair that moved less than this (in pixels) counts as a
        // click rather than the start of a resize drag.
        const float ClickDragThreshold = 6f;

        Texture2D texture;
        int lastVersion = -1;
        Texture2D borderTex;
        Texture2D handleTex;
        Texture2D panelTex;

        bool dragging;
        Vector2 mouseDownPos;
        float dragStartWidthFraction;
        bool showDiagnostics;

        GUIStyle panelStyle;
        GUIStyle titleStyle;

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

            // Flat-color textures for the border, resize grip, and the diagnostics
            // popup background. Avoids GUI.Box: Unity's default skin renders its
            // own dark panel fill, which on top of an also-dark camera frame reads
            // as two stacked black boxes instead of one framed preview.
            borderTex = MakeFlatTex(new Color(1f, 1f, 1f, 0.85f));
            handleTex = MakeFlatTex(new Color(1f, 1f, 1f, 0.95f));
            panelTex = MakeFlatTex(new Color(0.05f, 0.05f, 0.08f, 0.92f));
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

            HandleMouse();
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

        void HandleMouse()
        {
            if (!show || lastVersion < 0) return;

            Rect box = GetBoxRect();

            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mouse = MouseToGuiSpace();
                mouseDownPos = mouse;
                if (GetHandleRect(box).Contains(mouse))
                {
                    dragging = true;
                    dragStartWidthFraction = widthFraction;
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                Vector2 mouse = MouseToGuiSpace();
                bool wasClick = Vector2.Distance(mouse, mouseDownPos) < ClickDragThreshold;
                if (wasClick && !dragging && box.Contains(mouse))
                {
                    showDiagnostics = !showDiagnostics;
                }
                dragging = false;
            }

            if (dragging && Input.GetMouseButton(0))
            {
                Vector2 mouse = MouseToGuiSpace();
                // The box's bottom-right corner is a fixed anchor, so dragging the
                // top-left handle further left/up grows the box; dragging it
                // toward the anchor shrinks it. Horizontal movement alone decides
                // the size — height always follows from the aspect ratio.
                float deltaX = mouseDownPos.x - mouse.x;
                float deltaFraction = deltaX / Screen.width;
                widthFraction = Mathf.Clamp(dragStartWidthFraction + deltaFraction, MinWidthFraction, MaxWidthFraction);
            }
        }

        void OnGUI()
        {
            if (!show || texture == null || lastVersion < 0) return;

            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = false };
                panelStyle.normal.textColor = Color.white;
                titleStyle = new GUIStyle(panelStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
            }

            Rect box = GetBoxRect();
            const float border = 2f;
            GUI.DrawTexture(new Rect(box.x - border, box.y - border, box.width + border * 2f, box.height + border * 2f), borderTex);
            // The browser already mirrors the feed for the player standing in
            // front of the camera, so draw it exactly as received.
            GUI.DrawTexture(box, texture, ScaleMode.ScaleToFit);

            // Small grip square at the top-left corner, so the resize handle is
            // discoverable instead of being an invisible hit area.
            GUI.DrawTexture(GetHandleRect(box), handleTex);

            if (showDiagnostics) DrawDiagnostics(box);
        }

        void DrawDiagnostics(Rect box)
        {
            const float panelWidth = 260f;
            const float panelHeight = 150f;
            const float gap = 8f;

            // Sits just above the camera box, right-aligned with it, so it never
            // covers the play route even when the box itself is down in the corner.
            float x = Mathf.Min(box.xMax - panelWidth, Screen.width - panelWidth - 4f);
            float y = box.y - panelHeight - gap;
            var panelRect = new Rect(x, Mathf.Max(4f, y), panelWidth, panelHeight);

            GUI.DrawTexture(panelRect, panelTex);

            var inner = new Rect(panelRect.x + 12f, panelRect.y + 10f, panelRect.width - 24f, panelRect.height - 20f);
            GUILayout.BeginArea(inner);
            GUILayout.Label("Camera recognition", titleStyle);

            bool connected = receiver != null && receiver.Connected;
            string connLine = connected ? "<color=#7CFC7C>connected</color>" : "<color=#FF7A7A>not connected</color>";
            GUILayout.Label(connLine, panelStyle);

            var s = receiver != null ? receiver.State : null;
            if (s == null || !connected)
            {
                GUILayout.Label(receiver != null ? receiver.Status : "no receiver", panelStyle);
            }
            else
            {
                GUILayout.Label(ZoneLine("Left", s.visible, 0) + "   " + ZoneLine("Center", s.visible, 1) + "   " + ZoneLine("Right", s.visible, 2), panelStyle);

                string calLine = s.calibrated
                    ? "<color=#7CFC7C>calibrated</color>"
                    : s.calibrating
                        ? "<color=#FFD54A>calibrating…</color>"
                        : "<color=#FF7A7A>not calibrating</color>";
                GUILayout.Label(calLine, panelStyle);
            }

            GUILayout.EndArea();
        }

        static string ZoneLine(string label, bool[] visible, int i)
        {
            bool ok = visible != null && visible.Length > i && visible[i];
            string tag = ok ? "<color=#7CFC7C>OK</color>" : "<color=#FF7A7A>--</color>";
            return label + " " + tag;
        }
    }
}
