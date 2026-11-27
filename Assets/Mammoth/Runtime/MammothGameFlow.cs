using System.Collections.Generic;
using UnityEngine;

namespace Mammoth
{
    public enum MammothFlowState
    {
        Home, Rules, PracticeSelect, Setup, Tutorial, Practice, Ready, Countdown, Playing, Paused, Ending, Results
    }

    public enum MammothPracticeRole { Speed, Direction, JumpGrab }

    /// <summary>
    /// The user flow from the design doc ("The Last Bag of Acorns"):
    /// Home → Rules → three players in frame → calibration → team tutorial →
    /// Ready → 3 2 1 → uphill → summit → downhill → campsite ending → results.
    /// Role practice, pause and tracking loss are side paths. One operator
    /// clicks the menus with the mouse; players only use the four gestures.
    /// The camera is not started on the home screen, only when setup begins.
    /// IMGUI only, so no Canvas/UI package is needed.
    /// </summary>
    public class MammothGameFlow : MonoBehaviour
    {
        public MammothStateReceiver receiver;
        public MammothPoseEngine poseEngine;
        public MammothController controller;
        public MammothFollowCamera followCamera;
        public MammothCameraPreview cameraPreview;
        public MammothDebugHud debugHud;
        [Tooltip("Template for tutorial/practice acorns (a disabled acorn in the scene).")]
        public GameObject practiceAcornTemplate;
        public int totalAcorns = 20;

        public MammothFlowState State { get; private set; } = MammothFlowState.Home;

        // ---- session ----
        bool keyboardMode;
        bool tutorialDoneThisSession;
        bool rulesLeadToSetup;
        bool setupForPractice;
        MammothPracticeRole practiceRole;

        // ---- tutorial ----
        int tutorialStep;
        bool tutLowSpeed, tutHighSpeed, tutLeft, tutRight;
        int tutJumpBase;
        float tutorialDoneAt = -1f;
        GameObject practiceAcorn;
        float practiceRespawnAt = -1f;
        int practiceSpawnCount;

        // ---- countdown / pause / ending ----
        float countdownStart;
        bool countdownIsResume;
        bool trackingPause;
        float trackingOkSince = -1f;
        float endingStart;

        readonly List<(string text, Color color, float until)> toasts = new List<(string, Color, float)>();

        // ---- styling ----
        Font font;
        Texture2D roundTex, circleTex, whiteTex;
        GUIStyle panelStyle, labelStyle, titleStyle, bigStyle, buttonStyle, smallStyle, centerStyle;
        float uiScale = 1f;
        Vector2 view;

        static readonly Color Ink = new Color(0.27f, 0.21f, 0.30f);
        static readonly Color InkSoft = new Color(0.45f, 0.39f, 0.48f);
        static readonly Color Cream = new Color(1f, 0.985f, 0.96f, 0.96f);
        static readonly Color Acorn = new Color(0.66f, 0.45f, 0.26f);
        static readonly Color Mint = new Color(0.47f, 0.78f, 0.66f);
        static readonly Color Sky = new Color(0.52f, 0.71f, 0.93f);
        static readonly Color Rose = new Color(0.93f, 0.58f, 0.66f);
        static readonly Color Lilac = new Color(0.69f, 0.62f, 0.90f);
        static readonly Color Butter = new Color(0.98f, 0.82f, 0.45f);
        static readonly Color Warn = new Color(0.93f, 0.45f, 0.42f);

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            if (poseEngine == null) poseEngine = FindFirstObjectByType<MammothPoseEngine>();
            if (controller == null) controller = FindFirstObjectByType<MammothController>();
            if (followCamera == null) followCamera = FindFirstObjectByType<MammothFollowCamera>();
            if (cameraPreview == null) cameraPreview = FindFirstObjectByType<MammothCameraPreview>();
            if (debugHud == null) debugHud = FindFirstObjectByType<MammothDebugHud>();
            if (debugHud != null) debugHud.show = false;
            if (controller != null)
            {
                controller.OnCollected += HandleCollected;
                controller.OnEmptyGrab += HandleEmptyGrab;
            }
            EnterHome();
        }

        void OnDestroy()
        {
            if (controller == null) return;
            controller.OnCollected -= HandleCollected;
            controller.OnEmptyGrab -= HandleEmptyGrab;
        }

        // =====================================================================
        // State transitions
        // =====================================================================

        void SetControl(bool move, bool steer, bool actions, bool clock = true, bool hold = false)
        {
            if (controller == null) return;
            controller.moveForward = move;
            controller.allowSteer = steer;
            controller.acceptActions = actions;
            controller.clockRunning = clock;
            controller.holdPosition = hold;
        }

        void SetCamera(MammothCameraMode mode)
        {
            if (followCamera != null) followCamera.mode = mode;
        }

        void EnterHome()
        {
            State = MammothFlowState.Home;
            StopCamera();
            DestroyPracticeAcorn();
            if (controller != null) controller.ResetRun();
            SetControl(false, false, false);
            SetCamera(MammothCameraMode.Showcase);
            if (followCamera != null) followCamera.Snap();
        }

        void EnterRules(bool leadToSetup)
        {
            rulesLeadToSetup = leadToSetup;
            State = MammothFlowState.Rules;
        }

        void EnterSetup(bool forPractice)
        {
            setupForPractice = forPractice;
            State = MammothFlowState.Setup;
            SetControl(false, false, false);
            SetCamera(MammothCameraMode.Showcase);
            if (!keyboardMode) StartCamera();
        }

        void StartCamera()
        {
            if (receiver == null) return;
            if (controller != null) controller.useKeyboard = false;
            if (!receiver.enabled) receiver.enabled = true;
        }

        void StopCamera()
        {
            if (receiver != null && receiver.enabled) receiver.enabled = false;
            if (poseEngine != null && poseEngine.enabled) poseEngine.enabled = false;
        }

        void UseKeyboard(bool on)
        {
            keyboardMode = on;
            if (controller != null) controller.useKeyboard = on;
            if (on) StopCamera();
            else StartCamera();
        }

        void AfterSetup()
        {
            if (setupForPractice) { State = MammothFlowState.PracticeSelect; return; }
            if (tutorialDoneThisSession) EnterReady();
            else EnterTutorial();
        }

        void EnterTutorial()
        {
            State = MammothFlowState.Tutorial;
            tutorialStep = 0;
            tutLowSpeed = tutHighSpeed = tutLeft = tutRight = false;
            tutorialDoneAt = -1f;
            if (controller != null) { controller.ResetRun(); controller.Recenter(); }
            SetControl(true, false, true, true, hold: true);
            SetCamera(MammothCameraMode.Follow);
            if (followCamera != null) followCamera.Snap();
        }

        void EnterPractice(MammothPracticeRole role)
        {
            practiceRole = role;
            State = MammothFlowState.Practice;
            practiceSpawnCount = 0;
            if (controller != null) { controller.ResetRun(); controller.Recenter(); }
            SetControl(true, role == MammothPracticeRole.Direction, role == MammothPracticeRole.JumpGrab, true, hold: true);
            SetCamera(MammothCameraMode.Follow);
            if (followCamera != null) followCamera.Snap();
            DestroyPracticeAcorn();
            if (role == MammothPracticeRole.JumpGrab) SpawnPracticeAcorn(false);
        }

        void EnterReady()
        {
            State = MammothFlowState.Ready;
            DestroyPracticeAcorn();
            if (controller != null) { controller.ResetRun(); }
            SetControl(false, false, false);
            SetCamera(MammothCameraMode.Follow);
            if (followCamera != null) followCamera.Snap();
        }

        void EnterCountdown(bool resume)
        {
            countdownIsResume = resume;
            countdownStart = Time.unscaledTime;
            State = MammothFlowState.Countdown;
            if (!resume && controller != null) controller.ResetRun();
            // Doc: during countdown, do not move, score or queue jumps and grabs.
            SetControl(false, false, false, clock: !resume ? true : false);
            SetCamera(MammothCameraMode.Follow);
        }

        void EnterPlaying()
        {
            State = MammothFlowState.Playing;
            SetControl(true, true, true);
        }

        void EnterPause(bool tracking)
        {
            trackingPause = tracking;
            trackingOkSince = -1f;
            State = MammothFlowState.Paused;
            // Freeze game time, position, jump and score.
            SetControl(false, false, false, clock: false);
        }

        void EnterEnding()
        {
            State = MammothFlowState.Ending;
            endingStart = Time.unscaledTime;
            SetControl(false, false, false);
            if (controller != null)
            {
                controller.SetTrunkRaised(true);
                controller.Wobble(1.2f);
            }
            SetCamera(MammothCameraMode.Celebrate);
        }

        // =====================================================================
        // Per-frame logic
        // =====================================================================

        void Update()
        {
            if (MammothKeys.Down(KeyCode.F1) && debugHud != null) debugHud.show = !debugHud.show;
            if (cameraPreview != null)
                cameraPreview.show = false; // the setup screen draws its own live preview

            switch (State)
            {
                case MammothFlowState.Tutorial: UpdateTutorial(); break;
                case MammothFlowState.Practice: UpdatePractice(); break;
                case MammothFlowState.Countdown: UpdateCountdown(); break;
                case MammothFlowState.Playing: UpdatePlaying(); break;
                case MammothFlowState.Paused: UpdatePaused(); break;
                case MammothFlowState.Ending:
                    if (Time.unscaledTime - endingStart > 8.5f) State = MammothFlowState.Results;
                    break;
            }
            toasts.RemoveAll(t => Time.unscaledTime > t.until);
        }

        bool TrackingOk => keyboardMode || (receiver != null && receiver.ControlAllowed);

        void UpdateTutorial()
        {
            if (controller == null) return;
            switch (tutorialStep)
            {
                case 0:
                    if (controller.Throttle < 0.25f) tutLowSpeed = true;
                    if (controller.Throttle > 0.75f) tutHighSpeed = true;
                    if (tutLowSpeed && tutHighSpeed) NextTutorialStep();
                    break;
                case 1:
                    if (controller.Lateral < -2.2f) tutLeft = true;
                    if (controller.Lateral > 2.2f) tutRight = true;
                    if (tutLeft && tutRight) NextTutorialStep();
                    break;
                case 2:
                    if (controller.Jumps > tutJumpBase && !controller.Airborne) NextTutorialStep();
                    break;
                case 3:
                    if (practiceAcorn == null && practiceRespawnAt < 0f) SpawnPracticeAcorn(false);
                    if (practiceRespawnAt > 0f && Time.unscaledTime > practiceRespawnAt) { practiceRespawnAt = -1f; SpawnPracticeAcorn(false); }
                    break;
                case 4:
                    if (Time.unscaledTime - tutorialDoneAt > 2.2f)
                    {
                        tutorialDoneThisSession = true;
                        EnterReady();
                    }
                    break;
            }
        }

        void NextTutorialStep()
        {
            tutorialStep++;
            AddToast("Nice!", Mint, 1.4f);
            if (controller == null) return;
            if (tutorialStep == 1) SetControl(true, true, true, true, hold: true);
            if (tutorialStep == 2) { controller.Recenter(); tutJumpBase = controller.Jumps; SetControl(true, false, true, true, hold: true); }
            if (tutorialStep == 3) { practiceRespawnAt = -1f; SpawnPracticeAcorn(false); }
            if (tutorialStep == 4) { tutorialDoneAt = Time.unscaledTime; DestroyPracticeAcorn(); SetControl(false, false, false); }
        }

        void UpdatePractice()
        {
            if (practiceRole != MammothPracticeRole.JumpGrab) return;
            if (practiceRespawnAt > 0f && Time.unscaledTime > practiceRespawnAt)
            {
                practiceRespawnAt = -1f;
                SpawnPracticeAcorn(practiceSpawnCount % 2 == 1);
            }
        }

        void UpdateCountdown()
        {
            if (!TrackingOk) { EnterPause(true); return; }
            if (Time.unscaledTime - countdownStart >= 3f) EnterPlaying();
        }

        void UpdatePlaying()
        {
            if (!TrackingOk) { EnterPause(true); return; }
            if (MammothKeys.Down(KeyCode.Escape) || MammothKeys.Down(KeyCode.P)) { EnterPause(false); return; }
            if (controller != null && controller.Distance >= MammothRoute.FinishZ) EnterEnding();
        }

        void UpdatePaused()
        {
            if (TrackingOk)
            {
                if (trackingOkSince < 0f) trackingOkSince = Time.unscaledTime;
            }
            else trackingOkSince = -1f;
        }

        bool ReadyToContinue => !trackingPause || keyboardMode || (trackingOkSince > 0f && Time.unscaledTime - trackingOkSince >= 3f);

        // =====================================================================
        // Acorn feedback and practice acorns
        // =====================================================================

        void HandleCollected(MammothReward r)
        {
            if (r.practice)
            {
                AddToast("Got it!", Mint, 1.3f);
                practiceSpawnCount++;
                if (State == MammothFlowState.Tutorial && tutorialStep == 3) { NextTutorialStep(); return; }
                Destroy(r.gameObject, 0.05f);
                practiceAcorn = null;
                practiceRespawnAt = Time.unscaledTime + 1.0f;
                return;
            }
            AddToast("+" + r.points, Mint, 1.2f);
        }

        void HandleEmptyGrab()
        {
            if (State == MammothFlowState.Playing || State == MammothFlowState.Tutorial || State == MammothFlowState.Practice)
                AddToast("Almost. Get a little closer.", Butter, 1.4f);
        }

        void SpawnPracticeAcorn(bool high)
        {
            DestroyPracticeAcorn();
            if (practiceAcornTemplate == null || controller == null || controller.grabOrigin == null) return;
            Vector3 p = controller.grabOrigin.position + new Vector3(0f, 0f, 0.55f);
            p.x = controller.Lateral;
            p.y = controller.GroundY + 1.1f + (high ? 1.4f : 0f);
            practiceAcorn = Instantiate(practiceAcornTemplate, p, Quaternion.identity);
            practiceAcorn.name = high ? "Practice Acorn (high)" : "Practice Acorn";
            var r = practiceAcorn.GetComponent<MammothReward>();
            if (r != null) { r.practice = true; r.high = high; r.id = -1000 - practiceSpawnCount; }
            practiceAcorn.SetActive(true);
        }

        void DestroyPracticeAcorn()
        {
            if (practiceAcorn != null) Destroy(practiceAcorn);
            practiceAcorn = null;
        }

        void AddToast(string text, Color color, float seconds)
        {
            toasts.Add((text, color, Time.unscaledTime + seconds));
            if (toasts.Count > 3) toasts.RemoveAt(0);
        }

        // =====================================================================
        // Drawing
        // =====================================================================

        void EnsureStyles()
        {
            if (panelStyle != null) return;
            font = PickFont();
            roundTex = MakeRounded(64, 22);
            circleTex = MakeRounded(64, 32);
            whiteTex = Texture2D.whiteTexture;

            panelStyle = new GUIStyle { normal = { background = roundTex }, border = new RectOffset(24, 24, 24, 24) };
            labelStyle = new GUIStyle { font = font, fontSize = 20, wordWrap = true, richText = true, normal = { textColor = Ink } };
            smallStyle = new GUIStyle(labelStyle) { fontSize = 16, normal = { textColor = InkSoft } };
            titleStyle = new GUIStyle(labelStyle) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigStyle = new GUIStyle(labelStyle) { fontSize = 120, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            centerStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            buttonStyle = new GUIStyle(labelStyle) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        static Font PickFont()
        {
            // Only ask for a font the OS actually has; a missing name logs a
            // warning every frame and drops the whole style back to defaults.
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var name in new[] { "Avenir Next", "Avenir", "Helvetica Neue", "Arial" })
                if (installed.Contains(name)) return Font.CreateDynamicFontFromOSFont(name, 28);
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static Texture2D MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        void Panel(Rect r, Color c, bool shadow = true)
        {
            var old = GUI.color;
            if (shadow)
            {
                GUI.color = new Color(0.25f, 0.2f, 0.35f, 0.16f * c.a);
                GUI.Box(new Rect(r.x, r.y + 6f, r.width, r.height), GUIContent.none, panelStyle);
            }
            GUI.color = c;
            GUI.Box(r, GUIContent.none, panelStyle);
            GUI.color = old;
        }

        void Circle(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, circleTex);
            GUI.color = old;
        }

        bool Button(Rect r, string label, Color c, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color fill = enabled ? (hover ? Color.Lerp(c, Color.white, 0.18f) : c) : new Color(0.78f, 0.76f, 0.8f);
            Panel(hover ? new Rect(r.x, r.y - 2f, r.width, r.height) : r, fill);
            GUI.Label(hover ? new Rect(r.x, r.y - 2f, r.width, r.height) : r, label, buttonStyle);
            if (!enabled) return false;
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void Label(Rect r, string text, GUIStyle style, Color? color = null)
        {
            var s = style;
            if (color.HasValue) { s = new GUIStyle(style); s.normal.textColor = color.Value; }
            GUI.Label(r, text, s);
        }

        void OnGUI()
        {
            EnsureStyles();
            uiScale = Screen.height / 720f;
            view = new Vector2(Screen.width / uiScale, 720f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));

            switch (State)
            {
                case MammothFlowState.Home: DrawHome(); break;
                case MammothFlowState.Rules: DrawRules(); break;
                case MammothFlowState.PracticeSelect: DrawPracticeSelect(); break;
                case MammothFlowState.Setup: DrawSetup(); break;
                case MammothFlowState.Tutorial: DrawTutorial(); break;
                case MammothFlowState.Practice: DrawPractice(); break;
                case MammothFlowState.Ready: DrawReady(); break;
                case MammothFlowState.Countdown: DrawPlayHud(); DrawCountdown(); break;
                case MammothFlowState.Playing: DrawPlayHud(); break;
                case MammothFlowState.Paused: DrawPlayHud(); DrawPaused(); break;
                case MammothFlowState.Ending: DrawEnding(); break;
                case MammothFlowState.Results: DrawResults(); break;
            }
            DrawToasts();
            GUI.matrix = Matrix4x4.identity;
        }

        float CenterX(float w) => (view.x - w) * 0.5f;

        // ---------------- Home ----------------
        void DrawHome()
        {
            float w = 470f;
            var card = new Rect(48f, 90f, w, 470f);
            Panel(card, Cream);
            Label(new Rect(card.x + 20f, card.y + 34f, w - 40f, 120f), "The Last Bag\nof Acorns", titleStyle, Acorn);
            Label(new Rect(card.x + 30f, card.y + 160f, w - 60f, 60f), "Three players. One elephant. Over the mountain together.", centerStyle, InkSoft);
            float bx = card.x + 50f, bw = w - 100f;
            if (Button(new Rect(bx, card.y + 240f, bw, 58f), "Start team adventure", Acorn)) EnterRules(true);
            if (Button(new Rect(bx, card.y + 312f, bw, 52f), "Practice controls", Mint)) State = MammothFlowState.PracticeSelect;
            if (Button(new Rect(bx, card.y + 378f, bw, 52f), "How to play", Sky)) EnterRules(false);
        }

        // ---------------- Rules ----------------
        void DrawRules()
        {
            float w = 860f, h = 540f;
            var card = new Rect(CenterX(w), 80f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 22f, w, 56f), "How we travel together", titleStyle, Acorn);

            string[] names = { "Speed", "Direction", "Jump + Trunk" };
            string[] where = { "Left player", "Center player", "Right player" };
            string[] how = {
                "Raise both hands to go faster, lower them to slow down.",
                "Tilt: raise one hand higher than the other to steer that way.",
                "Raise both hands to jump. Clap to swing the trunk.",
            };
            Color[] cols = { Rose, Sky, Mint };
            float colW = (w - 80f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = card.x + 40f + i * colW;
                Circle(new Rect(x + colW * 0.5f - 46f, card.y + 100f, 92f, 92f), cols[i]);
                Label(new Rect(x + colW * 0.5f - 46f, card.y + 100f, 92f, 92f), (i + 1).ToString(), bigStyle.WithSize(44));
                Label(new Rect(x, card.y + 204f, colW, 30f), names[i], centerStyle.WithSize(22).Bold());
                Label(new Rect(x, card.y + 232f, colW, 24f), where[i], centerStyle.WithSize(15), InkSoft);
                Label(new Rect(x + 12f, card.y + 258f, colW - 24f, 70f), how[i], centerStyle.WithSize(16), InkSoft);
            }

            string rules =
                "•  The route moves forward on its own.\n" +
                "•  Steer around rocks and toward acorns.\n" +
                "•  Clap when an acorn swells near the trunk to collect it.\n" +
                "•  High acorns need a jump first, then a clap in the air.\n" +
                "•  <b>Speed up uphill; watch your speed downhill.</b>";
            Label(new Rect(card.x + 90f, card.y + 340f, w - 180f, 140f), rules, labelStyle.WithSize(18));

            if (rulesLeadToSetup)
            {
                if (Button(new Rect(card.x + w - 260f, card.y + h - 76f, 220f, 52f), "Continue", Acorn)) EnterSetup(false);
                if (Button(new Rect(card.x + 40f, card.y + h - 76f, 150f, 52f), "Back", Lilac)) EnterHome();
            }
            else if (Button(new Rect(card.x + w * 0.5f - 90f, card.y + h - 76f, 180f, 52f), "Back", Lilac)) EnterHome();
        }

        // ---------------- Practice select ----------------
        void DrawPracticeSelect()
        {
            float w = 520f, h = 470f;
            var card = new Rect(CenterX(w), 110f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 24f, w, 56f), "Practice controls", titleStyle.WithSize(38), Acorn);
            Label(new Rect(card.x + 40f, card.y + 82f, w - 80f, 40f), "Each player can try their own role. Practice is not scored.", centerStyle.WithSize(16), InkSoft);
            bool ready = keyboardMode || (receiver != null && receiver.enabled && receiver.ControlAllowed);
            float bx = card.x + 60f, bw = w - 120f;
            if (!ready)
            {
                if (Button(new Rect(bx, card.y + 150f, bw, 58f), "Set up camera first", Acorn)) EnterSetup(true);
            }
            else
            {
                if (Button(new Rect(bx, card.y + 140f, bw, 54f), "Speed practice", Rose)) EnterPractice(MammothPracticeRole.Speed);
                if (Button(new Rect(bx, card.y + 206f, bw, 54f), "Direction practice", Sky)) EnterPractice(MammothPracticeRole.Direction);
                if (Button(new Rect(bx, card.y + 272f, bw, 54f), "Jump and grab practice", Mint)) EnterPractice(MammothPracticeRole.JumpGrab);
            }
            if (Button(new Rect(card.x + w * 0.5f - 80f, card.y + h - 80f, 160f, 50f), "Home", Lilac)) EnterHome();
        }

        // ---------------- Setup / calibration ----------------
        void DrawSetup()
        {
            float w = 900f, h = 580f;
            var card = new Rect(CenterX(w), 60f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 18f, w, 50f), "Camera setup", titleStyle.WithSize(36), Acorn);

            if (keyboardMode)
            {
                Label(new Rect(card.x + 60f, card.y + 110f, w - 120f, 200f),
                    "<b>Keyboard test mode</b>\n\nW / S  speed up / slow down\nA / D  steer left / right\nSpace  jump\nE  trunk grab\n\nUse this to test the route without three players.", labelStyle.WithSize(20));
                if (Button(new Rect(card.x + w - 260f, card.y + h - 76f, 220f, 52f), "Continue", Acorn)) AfterSetup();
                if (Button(new Rect(card.x + 40f, card.y + h - 76f, 240f, 52f), "Use camera instead", Sky)) UseKeyboard(false);
                return;
            }

            var preview = new Rect(card.x + 40f, card.y + 80f, 520f, 390f);
            Panel(preview, new Color(0.2f, 0.18f, 0.25f, 1f), false);
            var tex = poseEngine != null ? poseEngine.CameraTexture : null;
            bool camReady = poseEngine != null && poseEngine.CameraReady;
            if (tex != null && camReady)
            {
                var inner = new Rect(preview.x + 8f, preview.y + 8f, preview.width - 16f, preview.height - 16f);
                bool vflip = poseEngine.CameraVerticallyMirrored;
                // Mirrored preview: left on screen is the player's left region.
                GUI.DrawTextureWithTexCoords(inner, tex, vflip ? new Rect(1f, 1f, -1f, -1f) : new Rect(1f, 0f, -1f, 1f));
            }

            var s = receiver != null ? receiver.State : null;
            string[] roles = { "Speed", "Direction", "Jump + Trunk" };
            float zw = (preview.width - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                bool ok = s != null && s.visible != null && s.visible.Length > i && s.visible[i];
                var zr = new Rect(preview.x + 8f + i * zw, preview.y + 8f, zw, preview.height - 16f);
                var old = GUI.color;
                GUI.color = ok ? new Color(0.45f, 0.9f, 0.6f, 0.9f) : new Color(1f, 0.55f, 0.55f, 0.75f);
                GUI.DrawTexture(new Rect(zr.x, zr.yMax - 40f, zr.width - 4f, 40f), whiteTex);
                if (i > 0) GUI.DrawTexture(new Rect(zr.x - 1f, zr.y, 3f, zr.height), whiteTex);
                GUI.color = old;
                Label(new Rect(zr.x, zr.yMax - 40f, zr.width - 4f, 40f), roles[i] + (ok ? "  OK" : ""), centerStyle.WithSize(17).Bold(), Ink);
            }

            // Status column
            float sx = preview.xMax + 30f, sw = card.xMax - 40f - sx;
            string status;
            Color statusColor = InkSoft;
            bool calibrated = receiver != null && receiver.ControlAllowed;
            string camStatus = poseEngine != null ? poseEngine.Status : "no pose engine";
            if (!camReady)
            {
                status = camStatus;
                if (camStatus.Contains("denied"))
                {
                    status = "Camera permission is off for Unity.\nTurn it on in System Settings > Privacy & Security > Camera, then press Play again.";
                    statusColor = Warn;
                }
            }
            else if (calibrated) { status = "<b>Calibrated.</b>\nLower your hands and keep them apart."; statusColor = Ink; }
            else
            {
                status = "Stand in your regions. Lower your hands and keep them apart.";
                if (s != null && s.visible != null)
                {
                    var missing = new List<string>();
                    for (int i = 0; i < 3 && i < s.visible.Length; i++) if (!s.visible[i]) missing.Add(roles[i]);
                    if (missing.Count > 0) status += "\n\nMissing: " + string.Join(", ", missing);
                    else status += "\n\nHold still for 3 seconds...";
                }
            }
            Label(new Rect(sx, card.y + 90f, sw, 300f), status, labelStyle.WithSize(19), statusColor);

            if (Button(new Rect(card.x + w - 260f, card.y + h - 76f, 220f, 52f), "Continue", Acorn, calibrated)) AfterSetup();
            if (Button(new Rect(card.x + 40f, card.y + h - 76f, 150f, 52f), "Back", Lilac)) EnterHome();
            if (Button(new Rect(card.x + 210f, card.y + h - 76f, 250f, 52f), "Keyboard test mode", Sky)) UseKeyboard(true);
        }

        // ---------------- Tutorial ----------------
        void DrawTutorial()
        {
            string[] titles = { "1  Speed", "2  Steer", "3  Jump", "4  Trunk" };
            string[] body =
            {
                "Speed player: lower your hands to go slow, then raise them to go fast.",
                "Direction player: tilt left, then tilt right.",
                "Jump player: raise both hands together.",
                "Clap near an acorn to collect it with the trunk.",
            };
            string[] keys = { "Keyboard: hold S, then hold W", "Keyboard: hold A, then hold D", "Keyboard: Space", "Keyboard: E" };

            float w = 640f;
            var card = new Rect(CenterX(w), 28f, w, 170f);
            Panel(card, Cream);
            Label(new Rect(card.x + 28f, card.y + 14f, 300f, 30f), "Team tutorial", labelStyle.WithSize(16), InkSoft);
            for (int i = 0; i < 4; i++)
            {
                var dot = new Rect(card.x + w - 28f - (4 - i) * 30f, card.y + 18f, 18f, 18f);
                Circle(dot, i < tutorialStep ? Mint : (i == tutorialStep ? Acorn : new Color(0.85f, 0.83f, 0.88f)));
            }

            if (tutorialStep >= 4)
            {
                Label(new Rect(card.x, card.y + 46f, w, 100f), "Tutorial complete!\nGet ready for the mountain.", titleStyle.WithSize(30), Acorn);
                return;
            }

            Label(new Rect(card.x + 28f, card.y + 40f, w - 56f, 36f), titles[tutorialStep], labelStyle.WithSize(28).Bold(), Acorn);
            Label(new Rect(card.x + 28f, card.y + 80f, w - 56f, 50f), body[tutorialStep], labelStyle.WithSize(19));
            if (keyboardMode) Label(new Rect(card.x + 28f, card.y + 136f, w - 56f, 24f), keys[tutorialStep], smallStyle);

            if (tutorialStep == 0) DrawSpeedGauge(new Rect(CenterX(520f), 214f, 520f, 74f), true);
            if (tutorialStep == 1) DrawSteerMeter(new Rect(CenterX(520f), 214f, 520f, 74f), true);

            if (Button(new Rect(view.x - 190f, view.y - 76f, 160f, 50f), "Home", Lilac)) EnterHome();
        }

        void DrawSpeedGauge(Rect r, bool targets)
        {
            Panel(r, Cream);
            var bar = new Rect(r.x + 24f, r.y + 40f, r.width - 48f, 16f);
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.9f); GUI.DrawTexture(bar, roundTex);
            if (targets)
            {
                GUI.color = tutLowSpeed ? Mint : new Color(Rose.r, Rose.g, Rose.b, 0.6f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * 0.25f, bar.height), roundTex);
                GUI.color = tutHighSpeed ? Mint : new Color(Rose.r, Rose.g, Rose.b, 0.6f);
                GUI.DrawTexture(new Rect(bar.x + bar.width * 0.75f, bar.y, bar.width * 0.25f, bar.height), roundTex);
            }
            float t = controller != null ? controller.Throttle : 0f;
            GUI.color = Acorn;
            GUI.DrawTexture(new Rect(bar.x + bar.width * t - 11f, bar.y - 5f, 22f, 26f), roundTex);
            GUI.color = old;
            Label(new Rect(r.x + 24f, r.y + 8f, 200f, 26f), "Speed", labelStyle.WithSize(16).Bold());
            if (targets) Label(new Rect(r.x + 24f, r.y + 8f, r.width - 48f, 26f), (tutLowSpeed ? "slow: done   " : "slow   ") + (tutHighSpeed ? "fast: done" : "fast"), smallStyle.Right());
        }

        void DrawSteerMeter(Rect r, bool targets)
        {
            Panel(r, Cream);
            var bar = new Rect(r.x + 24f, r.y + 40f, r.width - 48f, 16f);
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.9f); GUI.DrawTexture(bar, roundTex);
            if (targets)
            {
                GUI.color = tutLeft ? Mint : new Color(Sky.r, Sky.g, Sky.b, 0.6f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * 0.15f, bar.height), roundTex);
                GUI.color = tutRight ? Mint : new Color(Sky.r, Sky.g, Sky.b, 0.6f);
                GUI.DrawTexture(new Rect(bar.x + bar.width * 0.85f, bar.y, bar.width * 0.15f, bar.height), roundTex);
            }
            float lat = controller != null ? Mathf.InverseLerp(-MammothRoute.SteerLimit, MammothRoute.SteerLimit, controller.Lateral) : 0.5f;
            GUI.color = Acorn;
            GUI.DrawTexture(new Rect(bar.x + bar.width * lat - 11f, bar.y - 5f, 22f, 26f), roundTex);
            GUI.color = old;
            Label(new Rect(r.x + 24f, r.y + 8f, 200f, 26f), "Tilt", labelStyle.WithSize(16).Bold());
            if (targets) Label(new Rect(r.x + 24f, r.y + 8f, r.width - 48f, 26f), (tutLeft ? "left: done   " : "left   ") + (tutRight ? "right: done" : "right"), smallStyle.Right());
        }

        // ---------------- Practice ----------------
        void DrawPractice()
        {
            string[] titles = { "Speed practice", "Direction practice", "Jump and grab practice" };
            string[] body =
            {
                "Raise both hands to speed up, lower them to slow down. Even the lowest input still moves.",
                "Raise one hand higher than the other to tilt and steer. Level hands keep the line.",
                "Clap near the acorn to collect it. Every second acorn is high: jump first, then clap in the air.",
            };
            float w = 640f;
            var card = new Rect(CenterX(w), 28f, w, 140f);
            Panel(card, Cream);
            Label(new Rect(card.x + 28f, card.y + 16f, w - 56f, 36f), titles[(int)practiceRole], labelStyle.WithSize(26).Bold(), Acorn);
            Label(new Rect(card.x + 28f, card.y + 58f, w - 56f, 70f), body[(int)practiceRole], labelStyle.WithSize(18));
            if (practiceRole == MammothPracticeRole.Speed)
            {
                DrawSpeedGauge(new Rect(CenterX(520f), 184f, 520f, 74f), false);
                float sp = controller != null ? controller.InputSpeed : 0f;
                Label(new Rect(CenterX(520f), 262f, 520f, 30f), sp.ToString("0.0") + " units/s", centerStyle.WithSize(18).Bold());
            }
            if (practiceRole == MammothPracticeRole.Direction) DrawSteerMeter(new Rect(CenterX(520f), 184f, 520f, 74f), false);
            if (Button(new Rect(view.x - 190f, view.y - 76f, 160f, 50f), "Back", Lilac))
            {
                DestroyPracticeAcorn();
                if (controller != null) controller.ResetRun();
                SetControl(false, false, false);
                SetCamera(MammothCameraMode.Showcase);
                State = MammothFlowState.PracticeSelect;
            }
        }

        // ---------------- Ready / countdown ----------------
        void DrawReady()
        {
            float w = 600f, h = 250f;
            var card = new Rect(CenterX(w), 200f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 26f, w, 50f), "Ready?", titleStyle.WithSize(40), Acorn);
            Label(new Rect(card.x + 40f, card.y + 86f, w - 80f, 60f), "When everyone is ready, lower your hands and keep them apart.", centerStyle.WithSize(19), InkSoft);
            bool ok = TrackingOk;
            if (!ok) Label(new Rect(card.x + 40f, card.y + 140f, w - 80f, 30f), "Waiting for all three players...", centerStyle.WithSize(16), Warn);
            if (Button(new Rect(card.x + w * 0.5f - 110f, card.y + h - 76f, 220f, 54f), "Ready", Acorn, ok)) EnterCountdown(false);
            if (Button(new Rect(view.x - 190f, view.y - 76f, 160f, 50f), "Home", Lilac)) EnterHome();
        }

        void DrawCountdown()
        {
            int n = 3 - Mathf.FloorToInt(Time.unscaledTime - countdownStart);
            if (n < 1) n = 1;
            float frac = (Time.unscaledTime - countdownStart) % 1f;
            float size = Mathf.Lerp(230f, 190f, frac);
            var r = new Rect(view.x * 0.5f - size * 0.5f, view.y * 0.45f - size * 0.5f, size, size);
            Circle(new Rect(r.x, r.y + 8f, r.width, r.height), new Color(0.3f, 0.2f, 0.35f, 0.18f));
            Circle(r, new Color(Acorn.r, Acorn.g, Acorn.b, 0.95f));
            Label(r, n.ToString(), bigStyle);
            if (countdownIsResume) Label(new Rect(0f, r.yMax + 16f, view.x, 30f), "Resuming...", centerStyle.WithSize(20).Bold(), Color.white);
        }

        // ---------------- In-game HUD ----------------
        void DrawPlayHud()
        {
            if (controller == null) return;
            float z = controller.Distance;
            var section = MammothRoute.Section(z);

            // Left card: section + speed with the suggested range
            var left = new Rect(24f, 22f, 300f, 118f);
            Panel(left, Cream);
            Label(new Rect(left.x + 22f, left.y + 12f, 260f, 30f), MammothRoute.SectionName(section), labelStyle.WithSize(24).Bold(), Acorn);
            MammothRoute.SuggestedSpeed(z, out float lo, out float hi);
            var bar = new Rect(left.x + 22f, left.y + 62f, 256f, 14f);
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.9f); GUI.DrawTexture(bar, roundTex);
            var cfgMax = 12f;
            GUI.color = new Color(Mint.r, Mint.g, Mint.b, 0.75f);
            GUI.DrawTexture(new Rect(bar.x + bar.width * (lo / cfgMax), bar.y, bar.width * ((hi - lo) / cfgMax), bar.height), roundTex);
            float sp = controller.ActualSpeed;
            GUI.color = Acorn;
            GUI.DrawTexture(new Rect(bar.x + bar.width * Mathf.Clamp01(sp / cfgMax) - 9f, bar.y - 5f, 18f, 24f), roundTex);
            GUI.color = old;
            Label(new Rect(left.x + 22f, left.y + 84f, 260f, 24f), "speed " + sp.ToString("0.0") + "   suggested " + lo.ToString("0") + "-" + hi.ToString("0"), smallStyle);

            // Right card: acorns + shared score + tracking
            var right = new Rect(view.x - 284f, 22f, 260f, 118f);
            Panel(right, Cream);
            Circle(new Rect(right.x + 20f, right.y + 18f, 40f, 40f), Acorn);
            Label(new Rect(right.x + 72f, right.y + 14f, 180f, 44f), controller.Acorns + " / " + totalAcorns, labelStyle.WithSize(30).Bold());
            Label(new Rect(right.x + 22f, right.y + 64f, 220f, 26f), "score " + controller.Score, labelStyle.WithSize(18), InkSoft);
            if (!keyboardMode)
            {
                var s = receiver != null ? receiver.State : null;
                string[] r = { "S", "D", "J" };
                for (int i = 0; i < 3; i++)
                {
                    bool ok = s != null && s.visible != null && s.visible.Length > i && s.visible[i] && receiver.ControlAllowed;
                    var dr = new Rect(right.x + 150f + i * 32f, right.y + 64f, 26f, 26f);
                    Circle(dr, ok ? Mint : Warn);
                    Label(dr, r[i], centerStyle.WithSize(13).Bold(), Color.white);
                }
            }
            else Label(new Rect(right.x + 140f, right.y + 64f, 110f, 26f), "keyboard", smallStyle.Right());

            // Next necessary prompt
            if (State == MammothFlowState.Playing)
            {
                string prompt = MammothRoute.Prompt(z) ?? HighAcornPrompt(z);
                if (prompt != null)
                {
                    float pw = Mathf.Min(720f, view.x - 680f);
                    if (pw < 360f) pw = Mathf.Min(720f, view.x - 60f);
                    var pr = new Rect(CenterX(pw), pw > view.x - 680f ? 156f : 30f, pw, 62f);
                    Panel(pr, new Color(0.33f, 0.26f, 0.38f, 0.82f), false);
                    Label(pr, prompt, centerStyle.WithSize(19).Bold(), Color.white);
                }
                if (Button(new Rect(view.x - 140f, view.y - 66f, 116f, 44f), "Pause", Lilac)) EnterPause(false);
            }
        }

        string HighAcornPrompt(float z)
        {
            foreach (var r in MammothReward.Active)
            {
                if (!r.high || r.practice) continue;
                float dz = r.transform.position.z - z;
                if (dz > 6f && dz < 32f) return "High acorn ahead: raise both hands to jump, then clap in the air.";
            }
            return null;
        }

        void DrawPaused()
        {
            float w = 620f, h = 290f;
            var card = new Rect(CenterX(w), 190f, w, h);
            Panel(card, Cream);
            if (trackingPause)
            {
                bool restored = TrackingOk;
                Label(new Rect(card.x, card.y + 24f, w, 44f), restored ? "Tracking restored" : "Paused", titleStyle.WithSize(34), Acorn);
                string msg = restored
                    ? "Lower and separate your hands, then get ready."
                    : "A player is out of view. The game is paused.";
                var s = receiver != null ? receiver.State : null;
                if (!restored && s != null && s.visible != null)
                {
                    string[] roles = { "Speed", "Direction", "Jump + Trunk" };
                    var missing = new List<string>();
                    for (int i = 0; i < 3 && i < s.visible.Length; i++) if (!s.visible[i]) missing.Add(roles[i]);
                    if (missing.Count > 0) msg += "\nMissing: " + string.Join(", ", missing);
                }
                Label(new Rect(card.x + 40f, card.y + 84f, w - 80f, 80f), msg, centerStyle.WithSize(19), InkSoft);
            }
            else
            {
                Label(new Rect(card.x, card.y + 24f, w, 44f), "Paused", titleStyle.WithSize(34), Acorn);
                Label(new Rect(card.x + 40f, card.y + 84f, w - 80f, 60f), "Take a breath. Your position, score and acorns are saved.", centerStyle.WithSize(19), InkSoft);
            }
            if (Button(new Rect(card.x + w - 250f, card.y + h - 80f, 210f, 54f), "Continue", Acorn, ReadyToContinue)) EnterCountdown(true);
            if (Button(new Rect(card.x + 40f, card.y + h - 80f, 150f, 54f), "Home", Lilac)) EnterHome();
        }

        // ---------------- Ending / results ----------------
        void DrawEnding()
        {
            float t = Time.unscaledTime - endingStart;
            int acorns = controller != null ? controller.Acorns : 0;
            string line = acorns > 0 ? "We brought our food to a new home." : "Everyone arrived safely. Next time, let's bring more food.";
            float a1 = Mathf.Clamp01((t - 1f) / 0.8f);
            float a2 = Mathf.Clamp01((t - 4.2f) / 0.8f);
            var pr = new Rect(CenterX(820f), view.y - 210f, 820f, 140f);
            Panel(pr, new Color(Cream.r, Cream.g, Cream.b, 0.94f * Mathf.Max(a1, 0.001f)));
            Label(new Rect(pr.x, pr.y + 22f, pr.width, 44f), line, titleStyle.WithSize(30), new Color(Acorn.r, Acorn.g, Acorn.b, a1));
            Label(new Rect(pr.x, pr.y + 80f, pr.width, 36f), "When a home changes, more than one life is affected.", centerStyle.WithSize(20), new Color(InkSoft.r, InkSoft.g, InkSoft.b, a2));
            if (t > 2f && Button(new Rect(view.x - 160f, view.y - 66f, 136f, 44f), "Skip", Lilac)) State = MammothFlowState.Results;
        }

        void DrawResults()
        {
            float w = 560f, h = 470f;
            var card = new Rect(CenterX(w), 100f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 24f, w, 50f), "We made it to camp", titleStyle.WithSize(36), Acorn);
            int acorns = controller != null ? controller.Acorns : 0;
            int score = controller != null ? controller.Score : 0;
            int hits = controller != null ? controller.Collisions : 0;
            if (acorns >= totalAcorns)
            {
                var badge = new Rect(card.x + w * 0.5f - 110f, card.y + 84f, 220f, 36f);
                Panel(badge, Butter, false);
                Label(badge, "Full collection!", centerStyle.WithSize(18).Bold(), Ink);
            }
            float y = card.y + 140f;
            Row(card, ref y, "Acorns", acorns + " / " + totalAcorns);
            Row(card, ref y, "Score", score.ToString());
            Row(card, ref y, "Collisions", hits.ToString());
            if (Button(new Rect(card.x + 50f, card.y + h - 90f, 210f, 58f), "Play again", Acorn)) EnterReady();
            if (Button(new Rect(card.x + w - 260f, card.y + h - 90f, 210f, 58f), "Home", Lilac)) EnterHome();
        }

        void Row(Rect card, ref float y, string name, string value)
        {
            Label(new Rect(card.x + 80f, y, 200f, 40f), name, labelStyle.WithSize(22), InkSoft);
            Label(new Rect(card.x + 80f, y, card.width - 160f, 40f), value, labelStyle.WithSize(26).Bold().Right());
            y += 52f;
        }

        void DrawToasts()
        {
            float y = view.y * 0.62f;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float w = Mathf.Max(160f, t.text.Length * 12f + 60f);
                var r = new Rect(CenterX(w), y, w, 50f);
                Panel(r, t.color);
                Label(r, t.text, centerStyle.WithSize(21).Bold(), Color.white);
                y -= 60f;
            }
        }
    }

    static class MammothGuiStyleExt
    {
        public static GUIStyle WithSize(this GUIStyle s, int size) => new GUIStyle(s) { fontSize = size };
        public static GUIStyle Bold(this GUIStyle s) => new GUIStyle(s) { fontStyle = FontStyle.Bold };
        public static GUIStyle Right(this GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.MiddleRight };
    }
}
