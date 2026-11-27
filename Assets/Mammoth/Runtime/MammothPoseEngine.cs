using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;

namespace Mammoth
{
    /// <summary>
    /// One detected person's keypoints for the current frame, already mirrored
    /// (x = 1 - rawX) and normalized to 0..1, in the same convention
    /// Web/mammoth-mapping.js uses for MediaPipe landmarks. Keypoint order
    /// matches YOLOv8-pose's COCO layout:
    /// 0 nose, 5 left shoulder, 6 right shoulder, 9 left wrist, 10 right wrist
    /// (the only five this game ever reads — see KpNose/KpLShoulder/etc below).
    /// </summary>
    struct PoseDetection
    {
        public float score;
        public float cx, cy, w, h; // normalized box, mirrored x
        public float[] kpX, kpY, kpConf; // length 17, mirrored x
    }

    /// <summary>One zone's (left/center/right) derived signal for this frame,
    /// mirroring the shape of Web/mammoth-mapping.js's observe() per-zone value.
    /// A null entry means "nobody usably tracked in this zone right now"
    /// (nobody there, more than one person there, or tracking too unreliable).</summary>
    public struct ZoneObservation
    {
        public float height;
        public float leftHeight, rightHeight;
        public bool handsValid, jumpValid, clapValid;
        public float handDistance;
    }

    /// <summary>
    /// Captures the Mac's webcam directly in Unity and runs a YOLOv8n-pose
    /// model (Assets/Mammoth/Models/yolov8n-pose.onnx, exported at a fixed
    /// 256x256 input, no built-in NMS) through Sentis/Inference Engine each
    /// frame, so gesture recognition no longer needs a separate browser page.
    ///
    /// This intentionally mirrors Web/mammoth-mapping.js's observe() function
    /// as closely as possible, just reading YOLO's 17 COCO keypoints (nose=0,
    /// left/right shoulder=5/6, left/right wrist=9/10) instead of MediaPipe's
    /// 33 BlazePose landmarks (nose=0, shoulders=11/12, wrists=15/16) — the
    /// zone-bucketing and per-zone math are otherwise identical.
    /// </summary>
    public class MammothPoseEngine : MonoBehaviour
    {
        public ModelAsset modelAsset;
        [Tooltip("Index into WebCamTexture.devices. -1 picks the first available camera.")]
        public int cameraDeviceIndex = -1;
        public int inputSize = 256;
        [Tooltip("Box/person confidence threshold, separate from the per-keypoint " +
            "confidence threshold in settings.json (cfg.confidence).")]
        public float detectionConfidence = 0.3f;
        [Tooltip("Boxes with IoU above this are treated as the same person when " +
            "picking the top 3 detections.")]
        public float nmsIou = 0.5f;
        public int maxPeople = 3;

        const int KpNose = 0, KpLShoulder = 5, KpRShoulder = 6, KpLWrist = 9, KpRWrist = 10;
        const int NumKeypoints = 17;

        WebCamTexture webCam;
        Model runtimeModel;
        Worker worker;
        Tensor<float> inputTensor;
        readonly List<PoseDetection> detections = new List<PoseDetection>();

        public bool CameraReady => webCam != null && webCam.width > 16;
        public string Status { get; private set; } = "starting camera...";

        void OnEnable()
        {
            StartCamera();
            if (modelAsset != null)
            {
                runtimeModel = ModelLoader.Load(modelAsset);
                worker = new Worker(runtimeModel, BackendType.GPUCompute);
            }
            else
            {
                Status = "no model assigned";
                Debug.LogError("[Mammoth] MammothPoseEngine has no modelAsset assigned.");
            }
        }

        void OnDisable()
        {
            if (webCam != null && webCam.isPlaying) webCam.Stop();
            worker?.Dispose();
            worker = null;
            inputTensor?.Dispose();
            inputTensor = null;
        }

        void StartCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Status = "no camera found on this Mac";
                Debug.LogError("[Mammoth] " + Status);
                return;
            }
            int idx = (cameraDeviceIndex >= 0 && cameraDeviceIndex < devices.Length) ? cameraDeviceIndex : 0;
            webCam = new WebCamTexture(devices[idx].name, 960, 720, 30);
            webCam.Play();
            Status = "waiting for camera permission...";
        }

        /// <summary>
        /// Runs one inference step (if the camera has a fresh frame) and returns
        /// the three zone observations, same shape/semantics as the JS observe().
        /// Returns false (all-null zones) when the camera or model isn't ready yet.
        /// </summary>
        public bool TryObserve(MammothSettings cfg, out ZoneObservation?[] zones)
        {
            zones = new ZoneObservation?[3];
            if (webCam == null || !webCam.isPlaying || webCam.width <= 16 || worker == null) return false;
            if (!webCam.didUpdateThisFrame) return true; // no new frame yet; keep last answer blank-but-ready

            Status = "ok";
            RunInference();
            BuildZones(cfg, zones);
            return true;
        }

        void RunInference()
        {
            if (inputTensor == null)
                inputTensor = new Tensor<float>(new TensorShape(1, 3, inputSize, inputSize));

            // NCHW, RGB, 0..1 — matches the (1,3,256,256) BCHW input the model
            // was exported with, and Ultralytics' 0..1 (not 0..255) normalization.
            //
            // CoordOrigin matters here: some cameras/platforms hand WebCamTexture
            // a vertically-flipped frame (webCam.videoVerticallyMirrored), which
            // Unity doc'd as not safe to cache — query it fresh every frame. We
            // want the tensor's row 0 to always be the TOP of the real-world
            // image (matching the y-down convention mammoth-mapping.js/signals.py
            // assume for every height/jump calculation), so flip our requested
            // coordinate origin to compensate whenever the raw frame is mirrored.
            var origin = webCam.videoVerticallyMirrored ? CoordOrigin.BottomLeft : CoordOrigin.TopLeft;
            TextureConverter.ToTensor(webCam, inputTensor, new TextureTransform().SetCoordOrigin(origin));

            worker.Schedule(inputTensor);
            detections.Clear();
            var peeked = worker.PeekOutput() as Tensor<float>;
            if (peeked == null) return;
            using var output = peeked.ReadbackAndClone() as Tensor<float>;
            if (output == null) return;

            // Shape (1, 56, N): channel-major, so for channel c, anchor a the
            // flat index is c * N + a (batch is always 0 here).
            var shape = output.shape;
            int anchors = shape[2];
            var data = output.DownloadToArray();

            float Get(int c, int a) => data[c * anchors + a];

            var raw = new List<PoseDetection>(64);
            for (int a = 0; a < anchors; a++)
            {
                float score = Get(4, a);
                if (score < detectionConfidence) continue;

                float cx = Get(0, a) / inputSize, cy = Get(1, a) / inputSize;
                float w = Get(2, a) / inputSize, h = Get(3, a) / inputSize;

                var kpX = new float[NumKeypoints];
                var kpY = new float[NumKeypoints];
                var kpConf = new float[NumKeypoints];
                for (int k = 0; k < NumKeypoints; k++)
                {
                    kpX[k] = 1f - Get(5 + 3 * k, a) / inputSize; // mirror x
                    kpY[k] = Get(6 + 3 * k, a) / inputSize;
                    kpConf[k] = Get(7 + 3 * k, a);
                }

                raw.Add(new PoseDetection { score = score, cx = 1f - cx, cy = cy, w = w, h = h, kpX = kpX, kpY = kpY, kpConf = kpConf });
            }

            // Greedy NMS by score, keep up to maxPeople non-overlapping boxes.
            raw.Sort((p, q) => q.score.CompareTo(p.score));
            foreach (var d in raw)
            {
                if (detections.Count >= maxPeople) break;
                bool overlaps = false;
                foreach (var kept in detections)
                {
                    if (Iou(d, kept) > nmsIou) { overlaps = true; break; }
                }
                if (!overlaps) detections.Add(d);
            }
        }

        static float Iou(PoseDetection a, PoseDetection b)
        {
            float ax1 = a.cx - a.w / 2, ax2 = a.cx + a.w / 2, ay1 = a.cy - a.h / 2, ay2 = a.cy + a.h / 2;
            float bx1 = b.cx - b.w / 2, bx2 = b.cx + b.w / 2, by1 = b.cy - b.h / 2, by2 = b.cy + b.h / 2;
            float ix1 = Mathf.Max(ax1, bx1), iy1 = Mathf.Max(ay1, by1);
            float ix2 = Mathf.Min(ax2, bx2), iy2 = Mathf.Min(ay2, by2);
            float iw = Mathf.Max(0, ix2 - ix1), ih = Mathf.Max(0, iy2 - iy1);
            float inter = iw * ih;
            float union = a.w * a.h + b.w * b.h - inter;
            return union <= 0 ? 0 : inter / union;
        }

        bool Good(PoseDetection d, int[] ids, float confidence)
        {
            foreach (int i in ids)
            {
                if (d.kpConf[i] < confidence) return false;
                if (d.kpX[i] < 0 || d.kpX[i] > 1 || d.kpY[i] < 0 || d.kpY[i] > 1) return false;
            }
            return true;
        }

        void BuildZones(MammothSettings cfg, ZoneObservation?[] zones)
        {
            // One list per zone, length = how many people landed there this
            // frame; a zone only resolves to a single ZoneObservation when
            // exactly one person was assigned to it (same rule as observe()).
            var lists = new List<ZoneObservation?>[3] { new(), new(), new() };

            foreach (var d in detections)
            {
                if (!Good(d, new[] { KpLShoulder, KpRShoulder }, cfg.confidence)) continue;
                float x = (d.kpX[KpLShoulder] + d.kpX[KpRShoulder]) / 2f;
                if (!(x >= 0 && x < 1)) continue;
                int zone = Mathf.Min(2, (int)(x * 3));
                if (Mathf.Min(Mathf.Abs(x - 1f / 3f), Mathf.Abs(x - 2f / 3f)) < cfg.margin) continue;

                float sy = (d.kpY[KpLShoulder] + d.kpY[KpRShoulder]) / 2f;
                float width = Mathf.Max(Mathf.Abs(d.kpX[KpLShoulder] - d.kpX[KpRShoulder]), 0.035f);
                bool hands = Good(d, new[] { KpLWrist, KpRWrist }, cfg.confidence);

                int left = d.kpX[KpLShoulder] < d.kpX[KpRShoulder] ? KpLWrist : KpRWrist;
                int right = left == KpLWrist ? KpRWrist : KpLWrist;
                int ls = left == KpLWrist ? KpLShoulder : KpRShoulder;
                int rs = left == KpLWrist ? KpRShoulder : KpLShoulder;

                var value = new ZoneObservation
                {
                    height = hands ? (sy - (d.kpY[KpLWrist] + d.kpY[KpRWrist]) / 2f) / width : 0,
                    leftHeight = hands ? (d.kpY[ls] - d.kpY[left]) / width : 0,
                    rightHeight = hands ? (d.kpY[rs] - d.kpY[right]) / width : 0,
                    handsValid = hands,
                    jumpValid = hands,
                    clapValid = hands,
                    handDistance = hands
                        ? Vector2.Distance(new Vector2(d.kpX[KpLWrist], d.kpY[KpLWrist]), new Vector2(d.kpX[KpRWrist], d.kpY[KpRWrist])) / width
                        : 0,
                };

                bool validForZone = zone == 0 ? Good(d, new[] { KpNose, KpLWrist, KpRWrist }, cfg.confidence) : hands;
                lists[zone].Add(validForZone ? value : (ZoneObservation?)null);
            }

            for (int z = 0; z < 3; z++)
                zones[z] = lists[z].Count == 1 ? lists[z][0] : null;
        }
    }
}
