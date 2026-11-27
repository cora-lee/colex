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
        // Browser camera: Chrome runs the camera + gesture mapping (Web/index.html)
        // and posts state to Unity. Used when macOS won't give Unity the camera.
        bool browserMode;
        Texture2D browserTex;
        int browserFrameVersion = -1;
        bool tutorialDoneThisSession;
        bool rulesLeadToSetup;
        bool setupForPractice;
        MammothPracticeRole practiceRole;

        // ---- tutorial ----
        int tutorialStep;
        bool tutLowSpeed, tutHighSpeed, tutLeft, tutRight;
        int tutJumpBase;
        float tutorialDoneAt = -1f;
        float tutorialStepAt;
        GameObject practiceAcorn;
        float practiceRespawnAt = -1f;
        int practiceSpawnCount;

        // ---- countdown / pause / ending ----
        float countdownStart;
        bool countdownIsResume;
        bool trackingPause;
        float trackingOkSince = -1f;
        float endingStart;

        // Per-role recognition flashes shown above the camera preview columns
        // (0 speed, 1 direction, 2 jump + trunk), straight from the tracker so
        // they work on every screen, not only during play.
        readonly string[] flashText = new string[3];
        readonly Color[] flashColor = new Color[3];
        readonly float[] flashUntil = new float[3];
        int lastSteerDir, lastSpeedBand = -1;

        void Flash(int role, string text, Color c)
        {
            flashText[role] = text; flashColor[role] = c; flashUntil[role] = Time.unscaledTime + 1.1f;
        }

        void UpdateRecognitionFlashes()
        {
            var s = receiver != null && receiver.enabled ? receiver.State : null;
            if (s == null) return;
            bool dirVisible = s.visible != null && s.visible.Length > 1 && s.visible[1];
            int dir = 0;
            if (dirVisible)
            {
                float diff = s.rightHand - s.leftHand;
                if (Mathf.Abs(diff) > 0.5f) dir = diff > 0 ? 1 : -1;
            }
            if (dir != 0 && dir != lastSteerDir) Flash(1, dir < 0 ? "Left!" : "Right!", Sky);
            lastSteerDir = dir;

            bool speedVisible = s.visible != null && s.visible.Length > 0 && s.visible[0];
            int band = !speedVisible ? -1 : s.speed > 0.7f ? 2 : s.speed < 0.25f ? 0 : 1;
            if (lastSpeedBand >= 0 && band != lastSpeedBand)
            {
                if (band == 2) Flash(0, "Fast!", Rose);
                else if (band == 0) Flash(0, "Slow", Rose);
            }
            lastSpeedBand = band;
        }

        readonly List<(string text, Color color, float until)> toasts = new List<(string, Color, float)>();

        // ---- styling ----
        Font font;
        Texture2D roundTex, circleTex, whiteTex, bigCircleTex;
        float goStart = -10f;
        GUIStyle pillStyle;
        GUIStyle panelStyle, labelStyle, titleStyle, bigStyle, buttonStyle, smallStyle, centerStyle;
        float uiScale = 1f;
        Vector2 view;

        static readonly Color Ink = new Color(0.27f, 0.21f, 0.30f);
        static readonly Color InkSoft = new Color(0.45f, 0.39f, 0.48f);
        static readonly Color Cream = new Color(1f, 0.985f, 0.96f, 1f);
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
            if (receiver != null)
            {
                receiver.Tracker.OnJump += FlashJump;
                receiver.Tracker.OnGrab += FlashClap;
            }
            if (controller != null)
            {
                controller.OnCollected += HandleCollected;
                controller.OnEmptyGrab += HandleEmptyGrab;
                controller.OnCollision += HandleCollision;
            }
            EnterHome();
        }

        void FlashJump() => Flash(2, "Jump!", Mint);
        void FlashClap() => Flash(2, "Clap!", Butter);

        void OnDestroy()
        {
            if (receiver != null)
            {
                receiver.Tracker.OnJump -= FlashJump;
                receiver.Tracker.OnGrab -= FlashClap;
            }
            if (controller == null) return;
            controller.OnCollected -= HandleCollected;
            controller.OnEmptyGrab -= HandleEmptyGrab;
            controller.OnCollision -= HandleCollision;
        }

        void HandleCollision()
        {
            if (State == MammothFlowState.Playing) AddToast("Bump! Back on your feet...", Warn, 1.4f);
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
            if (!keyboardMode) StartCamera();
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
            var want = browserMode ? MammothSourceMode.Listen : MammothSourceMode.InUnityCamera;
            if (receiver.mode != want)
            {
                receiver.enabled = false;
                receiver.mode = want;
            }
            if (browserMode && poseEngine != null && poseEngine.enabled) poseEngine.enabled = false;
            if (!receiver.enabled) receiver.enabled = true;
        }

        /// <summary>Switch to Chrome's camera: start the local page server (in the
        /// Editor) and open the camera page; Unity listens for its state.</summary>
        void UseBrowserCamera()
        {
            browserMode = true;
            keyboardMode = false;
            StartCamera();
#if UNITY_EDITOR
            var server = System.Type.GetType("Mammoth.MammothWebServer, Assembly-CSharp-Editor");
            server?.GetMethod("Start")?.Invoke(null, null);
#endif
            Application.OpenURL("http://localhost:8080/Web/index.html");
        }

        float CameraAspect()
        {
            if (CameraImage(out var t, out _) && t.height > 0) return Mathf.Clamp((float)t.width / t.height, 1f, 2.4f);
            return 16f / 9f;
        }

        /// <summary>Largest rect of the given aspect centered inside area.</summary>
        static Rect Fit(Rect area, float aspect)
        {
            float w = area.width, h = w / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            return new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
        }

        /// <summary>Pop-up badges ("Jump!", "Left!", ...) above each player column.</summary>
        void DrawRecognitionBadges(Rect image, float yTop, int fontSize)
        {
            float zw = image.width / 3f;
            for (int i = 0; i < 3; i++)
            {
                float left = flashUntil[i] - Time.unscaledTime;
                if (left <= 0f || string.IsNullOrEmpty(flashText[i])) continue;
                float a = Mathf.Clamp01(left / 0.3f);
                float pop = 1f + Mathf.Clamp01((1.1f - left) / 0.12f) * 0f + Mathf.Clamp01(1f - (1.1f - left) / 0.12f) * 0.25f;
                float bw = Mathf.Min(zw - 6f, 96f) * pop, bh = (fontSize + 12f) * pop;
                var r = new Rect(image.x + i * zw + (zw - bw) * 0.5f, yTop - bh * 0.5f, bw, bh);
                var c = flashColor[i]; c.a = a;
                Panel(r, c);
                Label(r, flashText[i], centerStyle.WithSize(fontSize).Bold(), new Color(1f, 1f, 1f, a));
            }
        }

        bool CameraImage(out Texture tex, out Rect uv)
        {
            if (browserMode)
            {
                tex = browserTex; uv = new Rect(0f, 0f, 1f, 1f); // page already sends a mirrored image
                return browserTex != null && browserFrameVersion >= 0;
            }
            tex = poseEngine != null ? poseEngine.CameraTexture : null;
            bool vflip = poseEngine != null && poseEngine.CameraVerticallyMirrored;
            uv = vflip ? new Rect(1f, 1f, -1f, -1f) : new Rect(1f, 0f, -1f, 1f);
            return tex != null && poseEngine.CameraReady;
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
            tutorialStepAt = Time.unscaledTime;
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
            if (!resume) runRecorded = false;
            // Doc: during countdown, do not move, score or queue jumps and grabs.
            SetControl(false, false, false, clock: !resume ? true : false);
            SetCamera(MammothCameraMode.Follow);
        }

        void EnterPlaying()
        {
            State = MammothFlowState.Playing;
            goStart = Time.unscaledTime;
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
            RecordRun();
            SetControl(false, false, false);
            if (controller != null)
            {
                controller.SetTrunkRaised(true);
                controller.Wobble(1.2f);
            }
            SetCamera(MammothCameraMode.Celebrate);
        }

        /// <summary>Editor-only UI check: jump straight to a screen while playing.
        /// Gameplay screens use keyboard mode so tracking loss can't pause them.</summary>
        public void DebugPreview(MammothFlowState s)
        {
            switch (s)
            {
                case MammothFlowState.Home: EnterHome(); break;
                case MammothFlowState.Rules: EnterRules(true); break;
                case MammothFlowState.PracticeSelect: State = MammothFlowState.PracticeSelect; break;
                case MammothFlowState.Setup: UseKeyboard(false); EnterSetup(false); break;
                case MammothFlowState.Tutorial: UseKeyboard(true); EnterTutorial(); break;
                case MammothFlowState.Ready: UseKeyboard(true); EnterReady(); break;
                case MammothFlowState.Countdown: UseKeyboard(true); EnterCountdown(false); break;
                case MammothFlowState.Playing:
                    UseKeyboard(true); EnterCountdown(false); EnterPlaying();
                    controller.DebugJumpTo(52f); AddToast("+10", Mint, 3f); break;
                case MammothFlowState.Paused: UseKeyboard(true); EnterCountdown(false); EnterPlaying(); controller.DebugJumpTo(120f); EnterPause(false); break;
                case MammothFlowState.Ending: UseKeyboard(true); EnterCountdown(false); EnterPlaying(); controller.DebugJumpTo(MammothRoute.FinishZ); EnterEnding(); break;
                case MammothFlowState.Results: UseKeyboard(true); EnterCountdown(false); EnterPlaying(); controller.DebugJumpTo(MammothRoute.FinishZ); EnterEnding(); State = MammothFlowState.Results; break;
            }
        }

        // =====================================================================
        // Per-frame logic
        // =====================================================================

        void Update()
        {
            if (MammothKeys.Down(KeyCode.F1) && debugHud != null) debugHud.show = !debugHud.show;
            UpdateRecognitionFlashes();
            if (browserMode && receiver != null && receiver.FrameVersion != browserFrameVersion)
            {
                var bytes = receiver.LatestFrameJpeg;
                if (bytes != null && bytes.Length > 0)
                {
                    if (browserTex == null) browserTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (browserTex.LoadImage(bytes, false)) browserFrameVersion = receiver.FrameVersion;
                }
            }
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
            // Let the controller settle first, so a reading left over from the
            // previous screen can't tick a target before anyone moves.
            if (Time.unscaledTime - tutorialStepAt < 0.5f && tutorialStep < 4) return;
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
            tutorialStepAt = Time.unscaledTime;
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
            AddToast("Acorn!", Mint, 1.0f);
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
            bigCircleTex = MakeRounded(256, 128, true);
            whiteTex = Texture2D.whiteTexture;

            panelStyle = new GUIStyle { normal = { background = roundTex }, border = new RectOffset(24, 24, 24, 24) };
            pillStyle = new GUIStyle { normal = { background = MakeRounded(14, 7) }, border = new RectOffset(7, 7, 7, 7) };
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

        static Texture2D MakeRounded(int size, int radius, bool mips = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mips) { wrapMode = TextureWrapMode.Clamp, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear };
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

        /// <summary>Pill-shaped fill (9-sliced, so the ends stay round at any
        /// length) in the current GUI.color.</summary>
        void Pill(Rect r) => GUI.Box(r, GUIContent.none, pillStyle);

        /// <summary>Round slider knob with a white ring, centered on t (0..1) of the bar.</summary>
        void Knob(Rect bar, float t)
        {
            var old = GUI.color;
            float cx = bar.x + bar.width * Mathf.Clamp01(t), cy = bar.y + bar.height * 0.5f;
            GUI.color = new Color(0.3f, 0.2f, 0.35f, 0.18f);
            GUI.DrawTexture(new Rect(cx - 13f, cy - 11f, 26f, 26f), circleTex);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cx - 13f, cy - 13f, 26f, 26f), circleTex);
            GUI.color = Acorn;
            GUI.DrawTexture(new Rect(cx - 9f, cy - 9f, 18f, 18f), circleTex);
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
                case MammothFlowState.Countdown: DrawHighAcornTags(); DrawPlayHud(); DrawCountdown(); break;
                case MammothFlowState.Playing: DrawHighAcornTags(); DrawPlayHud(); DrawGo(); break;
                case MammothFlowState.Paused: DrawPlayHud(); DrawPaused(); break;
                case MammothFlowState.Ending: DrawEnding(); break;
                case MammothFlowState.Results: DrawResults(); break;
            }
            bool previewHere = State == MammothFlowState.Home || State == MammothFlowState.Tutorial || State == MammothFlowState.Practice
                || State == MammothFlowState.Ready || State == MammothFlowState.Countdown || State == MammothFlowState.Playing || State == MammothFlowState.Paused;
            if (previewHere && !keyboardMode) DrawCameraPreview();
            if (State == MammothFlowState.Playing || State == MammothFlowState.Tutorial || State == MammothFlowState.Practice || State == MammothFlowState.Setup) DrawToasts();
            GUI.matrix = Matrix4x4.identity;
        }

        float CenterX(float w) => (view.x - w) * 0.5f;

        /// <summary>Small mirrored live camera preview in the bottom-left corner,
        /// with the three player regions and whether each role is tracked.</summary>
        void DrawCameraPreview()
        {
            const float w = 252f;
            float h = (w - 12f) / CameraAspect() + 12f;
            var box = new Rect(20f, view.y - h - 20f, w, h);
            Panel(box, new Color(0.2f, 0.18f, 0.25f, 0.92f));
            var inner = new Rect(box.x + 6f, box.y + 6f, w - 12f, h - 12f);
            if (!CameraImage(out var camTex, out var camUv))
            {
                string st = browserMode ? "Waiting for the Chrome camera page..." : (poseEngine != null ? poseEngine.Status : "camera off");
                if (st.Contains("denied")) st = "Camera permission is off for Unity.";
                Label(inner, st, centerStyle.WithSize(14), new Color(1f, 1f, 1f, 0.85f));
                return;
            }
            GUI.DrawTextureWithTexCoords(inner, camTex, camUv);
            DrawSkeletons(inner, 2.2f);
            DrawPlayerZones(inner, false);
            DrawRecognitionBadges(inner, box.y - 20f, 15);
        }

        // ---------------- Body tracking overlay ----------------
        static readonly Color TrackLime = new Color(0.72f, 0.98f, 0.42f);
        static readonly Color PlayerBlue = new Color(0.10f, 0.36f, 0.86f);
        static readonly int[] Bones =
        {
            5, 6, 5, 7, 7, 9, 6, 8, 8, 10, 5, 11, 6, 12, 11, 12, 11, 13, 13, 15, 12, 14, 14, 16,
        };
        readonly Vector2[] kpBuf = new Vector2[17];
        readonly float[] kpConfBuf = new float[17];
        Material lineMat;

        /// <summary>Draws every tracked person's skeleton over the camera image,
        /// in the same mirrored view the players see.</summary>
        void DrawSkeletons(Rect image, float thickness)
        {
            if (Event.current.type != EventType.Repaint || poseEngine == null || browserMode) return;
            if (lineMat == null)
            {
                lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                lineMat.SetInt("_Cull", 0);
            }
            int n = poseEngine.PersonCount;
            if (n == 0) return;
            lineMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.QUADS);
            for (int p = 0; p < n; p++)
            {
                if (!poseEngine.GetPerson(p, kpBuf, kpConfBuf)) continue;
                for (int b = 0; b < Bones.Length; b += 2)
                    if (kpConfBuf[Bones[b]] > 0.3f && kpConfBuf[Bones[b + 1]] > 0.3f)
                        Seg(image, kpBuf[Bones[b]], kpBuf[Bones[b + 1]], thickness);
                // neck: shoulder midpoint up to the nose
                if (kpConfBuf[0] > 0.3f && kpConfBuf[5] > 0.3f && kpConfBuf[6] > 0.3f)
                    Seg(image, (kpBuf[5] + kpBuf[6]) * 0.5f, kpBuf[0], thickness);
                for (int k = 0; k < 17; k++)
                    if (kpConfBuf[k] > 0.3f && k != 1 && k != 2 && k != 3 && k != 4)
                        Seg(image, kpBuf[k] + new Vector2(-0.004f, 0f), kpBuf[k] + new Vector2(0.004f, 0f), thickness * 1.9f);
            }
            GL.End();
            GL.PopMatrix();
        }

        void Seg(Rect image, Vector2 a, Vector2 b, float thickness)
        {
            if (a.x < 0f || a.x > 1f || b.x < 0f || b.x > 1f || a.y < 0f || a.y > 1f || b.y < 0f || b.y > 1f) return;
            Vector2 pa = ToPixels(new Vector2(image.x + a.x * image.width, image.y + a.y * image.height));
            Vector2 pb = ToPixels(new Vector2(image.x + b.x * image.width, image.y + b.y * image.height));
            Vector2 d = pb - pa;
            float len = d.magnitude;
            if (len < 0.01f) d = Vector2.right; else d /= len;
            Vector2 nrm = new Vector2(-d.y, d.x) * (thickness * uiScale * 0.5f);
            GL.Color(TrackLime);
            GL.Vertex3(pa.x + nrm.x, pa.y + nrm.y, 0f);
            GL.Vertex3(pb.x + nrm.x, pb.y + nrm.y, 0f);
            GL.Vertex3(pb.x - nrm.x, pb.y - nrm.y, 0f);
            GL.Vertex3(pa.x - nrm.x, pa.y - nrm.y, 0f);
        }

        /// <summary>GUI point (virtual 720p space) to GL pixel space (origin bottom-left).</summary>
        Vector2 ToPixels(Vector2 gui)
        {
            Vector3 sp = GUI.matrix.MultiplyPoint3x4(new Vector3(gui.x, gui.y, 0f));
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        /// <summary>Role regions in the style of the mapping page: a blue
        /// PLAYER badge on top, a dotted divider and a Tracked chip at the bottom.</summary>
        void DrawPlayerZones(Rect image, bool big)
        {
            var s = receiver != null ? receiver.State : null;
            string[] roles = { "SPEED", "DIRECTION", "JUMP + GRAB" };
            float zw = image.width / 3f;
            var old = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                bool ok = s != null && s.visible != null && s.visible.Length > i && s.visible[i];
                var zr = new Rect(image.x + i * zw, image.y, zw, image.height);
                if (i > 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.7f);
                    for (float y = zr.y; y < zr.yMax; y += big ? 9f : 6f)
                        GUI.DrawTexture(new Rect(zr.x - 1f, y, 2f, big ? 5f : 3f), whiteTex);
                }
                if (big)
                {
                    var badge = new Rect(zr.x + 8f, zr.y + 8f, Mathf.Min(zw - 16f, 150f), 46f);
                    GUI.color = PlayerBlue; GUI.DrawTexture(badge, roundTex);
                    GUI.color = old;
                    Label(new Rect(badge.x + 10f, badge.y + 4f, badge.width - 12f, 16f), "PLAYER 0" + (i + 1), labelStyle.WithSize(11).Bold(), new Color(1f, 1f, 1f, 0.85f));
                    Label(new Rect(badge.x + 10f, badge.y + 19f, badge.width - 12f, 24f), roles[i], labelStyle.WithSize(17).Bold(), Color.white);
                }
                else
                {
                    var tagR = new Rect(zr.x + 3f, zr.y + 3f, zw - 6f, 18f);
                    GUI.color = PlayerBlue; Pill(tagR);
                    GUI.color = old;
                    Label(tagR, roles[i], centerStyle.WithSize(10).Bold(), Color.white);
                }
                float cw = big ? 92f : 58f, chh = big ? 26f : 17f;
                var chip = new Rect(zr.x + (zw - cw) * 0.5f, zr.yMax - chh - (big ? 10f : 4f), cw, chh);
                GUI.color = ok ? new Color(0.80f, 0.93f, 1f, 0.95f) : new Color(1f, 0.82f, 0.82f, 0.9f);
                Pill(chip);
                GUI.color = old;
                Label(chip, ok ? "Tracked" : "No one", centerStyle.WithSize(big ? 14 : 10).Bold(), ok ? PlayerBlue : new Color(0.7f, 0.25f, 0.3f));
            }
            GUI.color = old;
        }

        // ---------------- Home ----------------
        void DrawHome()
        {
            float w = 470f;
            var card = new Rect(48f, 56f, w, 470f);
            Panel(card, Cream);
            Label(new Rect(card.x + 20f, card.y + 34f, w - 40f, 120f), "The Last Bag\nof Acorns", titleStyle, Acorn);
            Label(new Rect(card.x + 30f, card.y + 160f, w - 60f, 60f), "Over the mountain together.", centerStyle, InkSoft);
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
                "•  Steer around icebergs: a bump sits the mammoth down and costs time.\n" +
                "•  Clap when an acorn swells near the trunk to collect it.\n" +
                "•  High acorns need a jump first, then a clap in the air.\n" +
                "•  <b>Fastest team to camp tops the ranking.</b>";
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

            var area = new Rect(card.x + 40f, card.y + 96f, 520f, 374f);
            var imageRect = Fit(new Rect(area.x + 8f, area.y + 8f, area.width - 16f, area.height - 16f), CameraAspect());
            var preview = new Rect(imageRect.x - 8f, imageRect.y - 8f, imageRect.width + 16f, imageRect.height + 16f);
            Panel(preview, new Color(0.2f, 0.18f, 0.25f, 1f), false);
            bool camReady = CameraImage(out var tex, out var texUv);
            if (camReady)
            {
                // Mirrored preview: left on screen is the player's left region.
                GUI.DrawTextureWithTexCoords(imageRect, tex, texUv);
            }
            else
                Label(new Rect(preview.x, preview.y + preview.height * 0.5f - 40f, preview.width, 50f), "No camera image yet", centerStyle.WithSize(20).Bold(), new Color(1f, 1f, 1f, 0.75f));

            var s = receiver != null ? receiver.State : null;
            string[] roles = { "Speed", "Direction", "Jump + Trunk" };
            if (camReady)
            {
                DrawSkeletons(imageRect, 4f);
                DrawPlayerZones(imageRect, true);
            }

            DrawRecognitionBadges(imageRect, imageRect.y + 24f, 18);

            // Status column
            float sx = area.xMax + 30f, sw = card.xMax - 40f - sx;
            string status;
            Color statusColor = InkSoft;
            bool calibrated = receiver != null && receiver.ControlAllowed;
            string camStatus = poseEngine != null ? poseEngine.Status : "no pose engine";
            if (browserMode && !calibrated && (receiver == null || !receiver.Connected))
            {
                status = "<b>Using Chrome's camera.</b>\nOn the page that just opened, press Start camera and allow access. Keep that tab open.";
                statusColor = Ink;
            }
            else if (!camReady && !browserMode)
            {
                status = camStatus;
                if (camStatus.Contains("denied"))
                {
                    status = "macOS is not giving Unity the camera.\nUse Chrome's camera instead: it already has permission.";
                    statusColor = Warn;
                }
            }
            else
            {
                // Calibration, step by step: 1 everyone in their region,
                // 2 hands down and apart, 3 hold still for 3 seconds.
                bool solo = receiver != null && receiver.SoloMode;
                var miss = new List<string>();
                bool allIn = s != null && s.visible != null && s.visible.Length >= 3;
                if (allIn) for (int i = 0; i < 3; i++) if (!s.visible[i]) { miss.Add(roles[i]); allIn = false; }
                if (s == null || s.visible == null) miss.AddRange(roles);
                bool anyIn = false;
                if (s != null && s.visible != null) foreach (bool v in s.visible) anyIn |= v;
                float prog = s != null ? s.calibrationProgress : 0f;
                if (calibrated) prog = 1f;
                status = null;
                float y = card.y + 92f;
                Label(new Rect(sx, y, sw, 28f), "Calibration", labelStyle.WithSize(20).Bold(), Acorn); y += 34f;
                if (solo)
                    CalibStep(sx, ref y, sw, "1  Stand in one region", anyIn || calibrated, anyIn || calibrated ? null : "Pick Speed, Direction or Jump + Trunk");
                else
                    CalibStep(sx, ref y, sw, "1  Everyone in their region", allIn || calibrated, allIn || calibrated ? null : "Missing: " + string.Join(", ", miss));
                CalibStep(sx, ref y, sw, "2  Hands down and apart", calibrated || prog > 0f, null);
                CalibStep(sx, ref y, sw, "3  Hold still for 3 seconds", calibrated, null);
                var bar = new Rect(sx, y + 6f, sw, 14f);
                var oldc = GUI.color;
                GUI.color = new Color(0.88f, 0.86f, 0.9f); Pill(bar);
                if (prog > 0.02f) { GUI.color = calibrated ? Mint : Acorn; Pill(new Rect(bar.x, bar.y, Mathf.Max(14f, bar.width * prog), bar.height)); }
                GUI.color = oldc;
                Label(new Rect(sx, y + 26f, sw, 24f), calibrated ? "Calibrated. Keep your hands down." : (prog > 0f ? "Calibrating... hold still" : (solo ? "Waiting for you" : "Waiting for all three players")), smallStyle, calibrated ? Mint : InkSoft);
            }
            if (status != null) Label(new Rect(sx, card.y + 90f, sw, 200f), status, labelStyle.WithSize(19), statusColor);

            if (Button(new Rect(card.x + w - 260f, card.y + h - 76f, 220f, 52f), "Continue", Acorn, calibrated)) AfterSetup();
            if (Button(new Rect(card.x + 40f, card.y + h - 76f, 150f, 52f), "Back", Lilac)) EnterHome();
            if (Button(new Rect(card.x + 210f, card.y + h - 76f, 230f, 52f), "Keyboard test mode", Sky)) UseKeyboard(true);
            if (!browserMode && !camReady && Button(new Rect(sx, card.y + 330f, sw, 54f), "Use Chrome camera", Mint)) UseBrowserCamera();
            else if (browserMode && !calibrated && Button(new Rect(sx, card.y + 330f, sw, 54f), "Reopen camera page", Mint)) UseBrowserCamera();
            if (!browserMode && camReady && !calibrated && receiver != null)
            {
                bool soloOn = receiver.SoloMode;
                if (Button(new Rect(sx, card.y + 330f, sw, 50f), soloOn ? "Solo play: on" : "Play solo", soloOn ? Mint : Acorn))
                {
                    receiver.SetSolo(!soloOn);
                    AddToast(soloOn ? "Waiting for all three players" : "Solo: stand in one region. Keyboard covers the rest.", Butter, 2.8f);
                }
                if (Button(new Rect(sx, card.y + 390f, sw, 44f), "Skip calibration (test)", Lilac))
                {
                    receiver.ForceCalibrate();
                    AddToast("Calibration skipped: keyboard covers empty roles", Butter, 2.5f);
                }
            }
        }

        void CalibStep(float x, ref float y, float w, string text, bool done, string note)
        {
            Circle(new Rect(x, y + 3f, 18f, 18f), done ? Mint : new Color(0.85f, 0.83f, 0.88f));
            Label(new Rect(x + 28f, y, w - 28f, 26f), text, labelStyle.WithSize(17), done ? Ink : InkSoft);
            y += 28f;
            if (note != null) { Label(new Rect(x + 28f, y - 4f, w - 28f, 40f), note, smallStyle.WithSize(14), Warn); y += 36f; }
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
            if (Button(new Rect(view.x - 400f, view.y - 76f, 196f, 50f), "Skip tutorial", Acorn))
            {
                DestroyPracticeAcorn();
                tutorialDoneThisSession = true;
                EnterReady();
                return;
            }
            if (Button(new Rect(view.x - 570f, view.y - 76f, 156f, 50f), "Skip step", Sky)) NextTutorialStep();
        }

        void DrawSpeedGauge(Rect r, bool targets)
        {
            Panel(r, Cream);
            var bar = new Rect(r.x + 24f, r.y + 40f, r.width - 48f, 16f);
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.9f); Pill(bar);
            if (targets)
            {
                GUI.color = tutLowSpeed ? Mint : new Color(Rose.r, Rose.g, Rose.b, 0.6f);
                Pill(new Rect(bar.x, bar.y, bar.width * 0.25f, bar.height));
                GUI.color = tutHighSpeed ? Mint : new Color(Rose.r, Rose.g, Rose.b, 0.6f);
                Pill(new Rect(bar.x + bar.width * 0.75f, bar.y, bar.width * 0.25f, bar.height));
            }
            float t = controller != null ? controller.Throttle : 0f;
            Knob(bar, t);
            GUI.color = old;
            Label(new Rect(r.x + 24f, r.y + 8f, 200f, 26f), "Speed", labelStyle.WithSize(16).Bold());
            if (targets) Label(new Rect(r.x + 24f, r.y + 8f, r.width - 48f, 26f), (tutLowSpeed ? "slow: done   " : "slow   ") + (tutHighSpeed ? "fast: done" : "fast"), smallStyle.Right());
        }

        void DrawSteerMeter(Rect r, bool targets)
        {
            Panel(r, Cream);
            var bar = new Rect(r.x + 24f, r.y + 40f, r.width - 48f, 16f);
            var old = GUI.color;
            GUI.color = new Color(0.88f, 0.86f, 0.9f); Pill(bar);
            if (targets)
            {
                GUI.color = tutLeft ? Mint : new Color(Sky.r, Sky.g, Sky.b, 0.6f);
                Pill(new Rect(bar.x, bar.y, bar.width * 0.15f, bar.height));
                GUI.color = tutRight ? Mint : new Color(Sky.r, Sky.g, Sky.b, 0.6f);
                Pill(new Rect(bar.x + bar.width * 0.85f, bar.y, bar.width * 0.15f, bar.height));
            }
            float lat = controller != null ? Mathf.InverseLerp(-MammothRoute.SteerLimit, MammothRoute.SteerLimit, controller.Lateral) : 0.5f;
            Knob(bar, lat);
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

        static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            const float c = 1.9f;
            return 1f + (c + 1f) * t * t * t + c * t * t;
        }

        /// <summary>Big round badge: soft shadow, white ring, colored disc, label.</summary>
        /// <summary>Circle badge whose text scales with the circle (fontRatio =
        /// font size / circle size), so the pop-in animation never leaves the
        /// text bigger than, or off-center from, the circle.</summary>
        void BigBadge(Vector2 center, float size, Color fill, string text, float fontRatio, float alpha)
        {
            int fontSize = Mathf.Max(8, Mathf.RoundToInt(size * fontRatio));
            var old = GUI.color;
            var r = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            GUI.color = new Color(0.25f, 0.18f, 0.32f, 0.22f * alpha);
            GUI.DrawTexture(new Rect(r.x + 4f, r.y + 12f, r.width, r.height), bigCircleTex);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, bigCircleTex);
            float ring = size * 0.07f;
            GUI.color = new Color(fill.r, fill.g, fill.b, alpha);
            GUI.DrawTexture(new Rect(r.x + ring, r.y + ring, r.width - ring * 2f, r.height - ring * 2f), bigCircleTex);
            GUI.color = old;
            var shadowStyle = bigStyle.WithSize(fontSize);
            float th = Mathf.Max(size, fontSize * 1.6f);
            Vector2 ink = InkCenterOffset(text, shadowStyle);
            var tr = new Rect(center.x - th * 0.5f - ink.x, center.y - th * 0.5f - ink.y, th, th);
            Label(new Rect(tr.x, tr.y + 4f, tr.width, tr.height), text, shadowStyle, new Color(0.3f, 0.2f, 0.3f, 0.25f * alpha));
            Label(tr, text, shadowStyle, new Color(1f, 1f, 1f, alpha));
        }

        /// <summary>How far the drawn glyph ink sits from the rect center when a
        /// MiddleCenter label is laid out, measured from the real glyph
        /// bounds (side bearings, ascent), so the caller can cancel it and put
        /// the ink exactly on the badge center.</summary>
        Vector2 InkCenterOffset(string text, GUIStyle style)
        {
            var f = style.font != null ? style.font : font;
            if (f == null || string.IsNullOrEmpty(text)) return Vector2.zero;
            int fs = style.fontSize;
            var fst = style.fontStyle;
            f.RequestCharactersInTexture(text, fs, fst);
            float pen = 0f, minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (char ch in text)
            {
                if (!f.GetCharacterInfo(ch, out var ci, fs, fst)) continue;
                minX = Mathf.Min(minX, pen + ci.minX); maxX = Mathf.Max(maxX, pen + ci.maxX);
                minY = Mathf.Min(minY, ci.minY); maxY = Mathf.Max(maxY, ci.maxY);
                pen += ci.advance;
            }
            if (minX > maxX) return Vector2.zero;
            float ox = (minX + maxX) * 0.5f - pen * 0.5f;
            float lineH = style.CalcSize(new GUIContent(text)).y;
            float asc = f.fontSize > 0 && f.ascent > 0 ? f.ascent * fs / f.fontSize : fs * 0.8f;
            // line box is centered in the rect; baseline sits asc below its top
            float oy = -lineH * 0.5f + asc - (minY + maxY) * 0.5f;
            // IMGUI's text backend lays glyphs out slightly up and left of what
            // the Font metrics predict; constant measured on screen.
            ox -= fs * inkBiasX;
            oy -= fs * inkBiasY;
            return new Vector2(ox, oy);
        }
        const float inkBiasX = 0.07f, inkBiasY = 0f;

        void DrawCountdown()
        {
            float t = Time.unscaledTime - countdownStart;
            int n = Mathf.Clamp(3 - Mathf.FloorToInt(t), 1, 3);
            float local = t - Mathf.Floor(t);
            Color[] fills = { Mint, Butter, Rose }; // 1, 2, 3
            float size = 210f * Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(local / 0.35f));
            float alpha = 1f - Mathf.Clamp01((local - 0.82f) / 0.18f);
            var c = new Vector2(view.x * 0.5f, view.y * 0.30f);

            // gentle dim so the badge reads over the bright snow
            var old = GUI.color;
            GUI.color = new Color(0.2f, 0.16f, 0.3f, 0.12f);
            GUI.DrawTexture(new Rect(0f, 0f, view.x, view.y), whiteTex);
            GUI.color = old;

            BigBadge(c, size * 0.82f, fills[n - 1], n.ToString(), 0.476f, alpha);

            // progress dots under the badge
            for (int i = 0; i < 3; i++)
            {
                bool lit = i < 4 - n;
                Circle(new Rect(c.x - 34f + i * 26f, c.y + 112f, 16f, 16f), lit ? Color.white : new Color(1f, 1f, 1f, 0.4f));
            }
            var cap = new Rect(c.x - 100f, c.y + 140f, 200f, 38f);
            Panel(cap, new Color(1f, 1f, 1f, 0.92f));
            Label(cap, countdownIsResume ? "Resuming" : "Get ready", centerStyle.WithSize(19).Bold(), Acorn);
        }

        void DrawGo()
        {
            float t = Time.unscaledTime - goStart;
            if (t > 0.9f) return;
            float size = 230f * Mathf.LerpUnclamped(0.6f, 1f, EaseOutBack(t / 0.3f));
            float alpha = 1f - Mathf.Clamp01((t - 0.6f) / 0.3f);
            BigBadge(new Vector2(view.x * 0.5f, view.y * 0.30f), size * 0.82f, Acorn, "Go!", 0.31f, alpha);
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
            GUI.color = new Color(0.88f, 0.86f, 0.9f); Pill(bar);
            var cfgMax = 12f;
            GUI.color = new Color(Mint.r, Mint.g, Mint.b, 0.75f);
            Pill(new Rect(bar.x + bar.width * (lo / cfgMax), bar.y, bar.width * ((hi - lo) / cfgMax), bar.height));
            float sp = controller.ActualSpeed;
            Knob(bar, Mathf.Clamp01(sp / cfgMax));
            GUI.color = old;
            Label(new Rect(left.x + 22f, left.y + 84f, 260f, 24f), "speed " + sp.ToString("0.0") + "   suggested " + lo.ToString("0") + "-" + hi.ToString("0"), smallStyle);

            // Right card: just the acorn count, with an acorn icon, centered.
            string count = controller.Acorns + " / " + totalAcorns;
            var countStyle = labelStyle.WithSize(30).Bold();
            float textW = countStyle.CalcSize(new GUIContent(count)).x;
            float iconW = 34f, gap = 12f, padX = 24f;
            float cardW = padX * 2f + iconW + gap + textW;
            var right = new Rect(view.x - cardW - 24f, 22f, cardW, 70f);
            Panel(right, Cream);
            float cx = right.x + padX;
            DrawAcornIcon(new Rect(cx, right.y + (right.height - 40f) * 0.5f, iconW, 40f));
            Label(new Rect(cx + iconW + gap, right.y, textW + 4f, right.height), count, countStyle.Mid());

            // Run clock under the acorn card: the time is what ranks the team.
            string clockText = FormatTime(controller.RunTime);
            var timeStyle = labelStyle.WithSize(26).Bold();
            float timeW = Mathf.Max(cardW, 150f);
            var timeCard = new Rect(view.x - timeW - 24f, right.yMax + 10f, timeW, 54f);
            Panel(timeCard, controller.Sitting ? Warn : new Color(0.33f, 0.26f, 0.38f, 0.9f));
            Label(timeCard, clockText, new GUIStyle(timeStyle) { alignment = TextAnchor.MiddleCenter }, Color.white);

            // Terrain badge under the left card
            if (controller.CurrentTerrain != MammothTerrainType.Normal)
            {
                bool ice = controller.CurrentTerrain == MammothTerrainType.Ice;
                var tb = new Rect(24f, 150f, 300f, 40f);
                Panel(tb, ice ? new Color(0.55f, 0.82f, 0.95f, 0.95f) : new Color(0.62f, 0.47f, 0.33f, 0.95f));
                Label(tb, ice ? "Slick ice: you'll slide!" : "Mud: slow going", centerStyle.WithSize(17).Bold(), Color.white);
            }

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

        static readonly Color AcornNutUI = new Color(0.90f, 0.70f, 0.35f);
        static readonly Color AcornCapUI = new Color(0.46f, 0.30f, 0.16f);

        /// <summary>Small flat acorn: rounded nut, wide cap, little stem.</summary>
        void DrawAcornIcon(Rect r)
        {
            var old = GUI.color;
            float w = r.width;
            GUI.color = AcornNutUI;
            GUI.DrawTexture(new Rect(r.x + w * 0.14f, r.y + r.height * 0.30f, w * 0.72f, r.height * 0.68f), bigCircleTex);
            GUI.color = AcornCapUI;
            Pill(new Rect(r.x, r.y + r.height * 0.18f, w, r.height * 0.30f));
            GUI.DrawTexture(new Rect(r.x + w * 0.46f, r.y, w * 0.1f, r.height * 0.24f), whiteTex);
            GUI.color = old;
        }

        /// <summary>"JUMP" tag floating above every high acorn ahead, so players
        /// know which ones need a jump before the clap.</summary>
        void DrawHighAcornTags()
        {
            var cam = Camera.main;
            if (cam == null || controller == null) return;
            if (State == MammothFlowState.Countdown || Time.unscaledTime - goStart < 0.9f) return;
            // Only the next high acorn ahead, so tags never pile up on each other.
            MammothReward next = null;
            float best = float.MaxValue;
            foreach (var r in MammothReward.Active)
            {
                if (!r.high || r.practice) continue;
                float d = r.transform.position.z - controller.Distance;
                if (d > -0.5f && d < 34f && d < best) { best = d; next = r; }
            }
            if (next == null) return;
            Vector3 sp = cam.WorldToScreenPoint(next.transform.position + Vector3.up * 0.7f);
            if (sp.z <= 0f) return;
            float gx = sp.x / uiScale, gy = (Screen.height - sp.y) / uiScale;
            float sc = Mathf.Lerp(0.75f, 1.05f, Mathf.InverseLerp(34f, 6f, best));
            float a = Mathf.Clamp01(Mathf.InverseLerp(34f, 28f, best));
            float bob = Mathf.Sin(Time.unscaledTime * 4f) * 3f;
            float w = 70f * sc, h = 26f * sc;
            var rect = new Rect(gx - w * 0.5f, gy - h - 12f * sc + bob, w, h);
            var old = GUI.color;
            // pointer: a short stem and a dot down to the acorn
            float stemW = 3f * sc;
            GUI.color = new Color(Sky.r, Sky.g, Sky.b, a);
            GUI.DrawTexture(new Rect(gx - stemW * 0.5f, rect.yMax - 2f, stemW, 9f * sc), whiteTex);
            float dot = 8f * sc;
            GUI.DrawTexture(new Rect(gx - dot * 0.5f, rect.yMax + 7f * sc - dot * 0.5f, dot, dot), circleTex);
            GUI.color = new Color(1f, 1f, 1f, a);
            Pill(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f));
            GUI.color = new Color(Sky.r, Sky.g, Sky.b, a);
            Pill(rect);
            GUI.color = old;
            Label(rect, "JUMP", centerStyle.WithSize(Mathf.RoundToInt(14 * sc)).Bold(), new Color(1f, 1f, 1f, a));
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
                Label(new Rect(card.x + 40f, card.y + 84f, w - 80f, 60f), "Take a breath. Your position, time and acorns are saved.", centerStyle.WithSize(19), InkSoft);
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

        // ---------------- Time ranking (kept on this computer) ----------------
        const string RankKey = "mammoth.besttimes.v1";
        const int RankKeep = 5;
        readonly List<Vector2> ranking = new List<Vector2>(); // x = seconds, y = acorns
        int lastRank = -1;       // index in ranking of the run just finished, -1 if not ranked
        bool runRecorded;

        static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            float sec = seconds - m * 60f;
            return m + ":" + sec.ToString("00.0");
        }

        void LoadRanking()
        {
            ranking.Clear();
            foreach (var part in PlayerPrefs.GetString(RankKey, "").Split(';'))
            {
                var f = part.Split('|');
                if (f.Length == 2 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t) && int.TryParse(f[1], out int a))
                    ranking.Add(new Vector2(t, a));
            }
        }

        /// <summary>Adds the finished run to the best-times list (fastest first,
        /// more acorns wins a tie) and remembers where it landed.</summary>
        void RecordRun()
        {
            if (runRecorded || controller == null) return;
            runRecorded = true;
            lastRank = -1;
            LoadRanking();
            float time = controller.RunTime;
            if (time < 5f) return; // editor previews jump straight here
            var entry = new Vector2(time, controller.Acorns);
            int at = ranking.FindIndex(e => time < e.x || (Mathf.Approximately(time, e.x) && entry.y > e.y));
            if (at < 0) at = ranking.Count;
            ranking.Insert(at, entry);
            if (ranking.Count > RankKeep) ranking.RemoveRange(RankKeep, ranking.Count - RankKeep);
            lastRank = at < RankKeep ? at : -1;
            var sb = new System.Text.StringBuilder();
            foreach (var e in ranking)
                sb.Append(e.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append((int)e.y).Append(';');
            PlayerPrefs.SetString(RankKey, sb.ToString());
            PlayerPrefs.Save();
        }

        void DrawResults()
        {
            float w = 600f, h = 610f;
            var card = new Rect(CenterX(w), 54f, w, h);
            Panel(card, Cream);
            Label(new Rect(card.x, card.y + 20f, w, 50f), "We made it to camp", titleStyle.WithSize(36), Acorn);
            int acorns = controller != null ? controller.Acorns : 0;
            int hits = controller != null ? controller.Collisions : 0;
            float time = controller != null ? controller.RunTime : 0f;

            // The time is the headline: it decides the rank.
            Label(new Rect(card.x, card.y + 74f, w, 64f), FormatTime(time), titleStyle.WithSize(56), Ink);
            string rankLine = lastRank == 0 ? "New best time!" : lastRank > 0 ? "Rank #" + (lastRank + 1) + " on this computer" : "Not in the top " + RankKeep + " yet";
            var badge = new Rect(card.x + w * 0.5f - 150f, card.y + 142f, 300f, 34f);
            Panel(badge, lastRank == 0 ? Butter : new Color(0.9f, 0.88f, 0.92f), false);
            Label(badge, rankLine, centerStyle.WithSize(17).Bold(), Ink);

            float y = card.y + 190f;
            Row(card, ref y, "Acorns", acorns + " / " + totalAcorns + (acorns >= totalAcorns ? "  (all!)" : ""));
            Row(card, ref y, "Bumps", hits.ToString());

            // Best times
            y += 2f;
            Label(new Rect(card.x + 80f, y, 300f, 28f), "Best times", labelStyle.WithSize(18).Bold(), InkSoft);
            y += 32f;
            for (int i = 0; i < RankKeep; i++)
            {
                var row = new Rect(card.x + 70f, y, w - 140f, 34f);
                bool mine = i == lastRank;
                if (mine) { var old = GUI.color; GUI.color = new Color(Mint.r, Mint.g, Mint.b, 0.45f); Pill(row); GUI.color = old; }
                Label(new Rect(row.x + 14f, row.y, 40f, row.height), (i + 1) + ".", labelStyle.WithSize(19).Bold().Mid(), mine ? Ink : InkSoft);
                if (i < ranking.Count)
                {
                    Label(new Rect(row.x + 56f, row.y, 160f, row.height), FormatTime(ranking[i].x), labelStyle.WithSize(20).Bold().Mid(), Ink);
                    Label(new Rect(row.x, row.y, row.width - 14f, row.height), (int)ranking[i].y + " acorns", new GUIStyle(labelStyle.WithSize(17)) { alignment = TextAnchor.MiddleRight }, InkSoft);
                }
                else Label(new Rect(row.x + 56f, row.y, 160f, row.height), "-", labelStyle.WithSize(20).Mid(), InkSoft);
                y += 36f;
            }

            if (Button(new Rect(card.x + 60f, card.y + h - 78f, 220f, 56f), "Play again", Acorn)) EnterReady();
            if (Button(new Rect(card.x + w - 280f, card.y + h - 78f, 220f, 56f), "Home", Lilac)) EnterHome();
        }

        void Row(Rect card, ref float y, string name, string value)
        {
            Label(new Rect(card.x + 80f, y, 200f, 40f), name, labelStyle.WithSize(22), InkSoft);
            Label(new Rect(card.x + 80f, y, card.width - 160f, 40f), value, labelStyle.WithSize(26).Bold().Right());
            y += 52f;
        }

        /// <summary>Short feedback ("+10", "Almost...", "Bump!") near the top
        /// center, just under the route prompt, newest on top.</summary>
        void DrawToasts()
        {
            float y = 104f;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float left = t.until - Time.unscaledTime;
                float a = Mathf.Clamp01(left / 0.25f);
                float w = Mathf.Max(150f, t.text.Length * 11.5f + 56f);
                var r = new Rect(CenterX(w), y, w, 44f);
                var c = t.color; c.a *= a;
                Panel(r, c);
                Label(r, t.text, centerStyle.WithSize(19).Bold(), new Color(1f, 1f, 1f, a));
                y += 52f;
            }
        }
    }

    static class MammothGuiStyleExt
    {
        public static GUIStyle WithSize(this GUIStyle s, int size) => new GUIStyle(s) { fontSize = size };
        public static GUIStyle Bold(this GUIStyle s) => new GUIStyle(s) { fontStyle = FontStyle.Bold };
        public static GUIStyle Right(this GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.MiddleRight };
        public static GUIStyle Mid(this GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.MiddleLeft };
    }
}
