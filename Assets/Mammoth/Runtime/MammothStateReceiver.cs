using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mammoth
{
    public enum MammothSourceMode
    {
        [InspectorName("Poll an external lab (Python)")] Poll,
        [InspectorName("Listen for a browser page to push /state")] Listen,
    }

    /// <summary>
    /// Feeds the shared MammothEventTracker from one of two sources:
    ///
    /// Poll (integration option B): polls an external process's /state endpoint
    /// (the Python lab) over HTTP GET.
    ///
    /// Listen: Unity itself hosts the /state endpoint. A browser page running the
    /// camera and the v3 gesture mapping in JavaScript (Web/) POSTs each state as
    /// JSON to this endpoint instead of Unity polling anything. Loopback only.
    /// </summary>
    public class MammothStateReceiver : MonoBehaviour
    {
        public MammothSourceMode mode = MammothSourceMode.Listen;

        [Header("Poll mode")]
        [Tooltip("Python lab endpoint. Loopback only.")]
        public string url = "http://127.0.0.1:8765/state";
        [Tooltip("Requests per second.")]
        [Range(5, 60)] public float pollRate = 30f;

        [Header("Listen mode")]
        [Tooltip("Port Unity listens on for the browser page's POST /state. Loopback only.")]
        public int listenPort = 8765;

        [Header("Common")]
        [Tooltip("Abort a request / treat tracking as lost after this many seconds without a fresh state.")]
        public float receiveTimeout = 0.5f;
        [Tooltip("Team game: only accept events when the source reports mode 'team'.")]
        public bool requireTeamMode = true;
        public bool logEvents = true;

        static readonly System.Text.RegularExpressions.Regex NullNumbers =
            new System.Text.RegularExpressions.Regex(@":\s*null\b");

        public readonly MammothEventTracker Tracker = new MammothEventTracker();

        public MammothState State => Tracker.Latest;
        public bool Connected => Tracker.Connected(Time.unscaledTimeAsDouble);
        public bool ControlAllowed => Tracker.ControlAllowed(Time.unscaledTimeAsDouble);
        public float TravelSpeed => Tracker.TravelSpeed(Time.unscaledTimeAsDouble);
        public int Lane => Tracker.Lane;
        public string Status { get; private set; } = "starting";

        /// <summary>Latest camera-preview JPEG the browser page POSTed to /frame, or
        /// null if none has arrived yet. Bump-free to read: byte[] reference
        /// assignment is atomic, and FrameVersion tells a reader whether it's new.</summary>
        public byte[] LatestFrameJpeg => latestFrame;
        /// <summary>Increments every time a new /frame POST lands, so a consumer
        /// (MammothCameraPreview) only re-decodes the JPEG when it actually changed.</summary>
        public int FrameVersion => frameVersion;

        HttpListener listener;
        readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        volatile string listenError;
        volatile byte[] latestFrame;
        int frameVersion;

        void OnEnable()
        {
            Tracker.OnJump += LogJump;
            Tracker.OnGrab += LogGrab;
            Tracker.OnSessionChanged += LogSession;
            Tracker.ReceiveTimeout = receiveTimeout;
            Tracker.RequireTeamMode = requireTeamMode;

            if (mode == MammothSourceMode.Poll) StartCoroutine(PollLoop());
            else StartListening();
        }

        void OnDisable()
        {
            StopAllCoroutines();
            StopListening();
            Tracker.OnJump -= LogJump;
            Tracker.OnGrab -= LogGrab;
            Tracker.OnSessionChanged -= LogSession;
            Tracker.Reset();
        }

        void Update()
        {
            if (mode != MammothSourceMode.Listen) return;
            Tracker.ReceiveTimeout = receiveTimeout;
            Tracker.RequireTeamMode = requireTeamMode;

            if (listenError != null)
            {
                Status = listenError;
                return;
            }

            // Drain the queue but only act on the newest state this frame.
            string latestJson = null;
            while (incoming.TryDequeue(out var j)) latestJson = j;
            if (latestJson == null)
            {
                if (!Tracker.Connected(Time.unscaledTimeAsDouble))
                    Status = "waiting for the browser page to connect (http://127.0.0.1:" + listenPort + "/state)";
                return;
            }

            string json = NullNumbers.Replace(latestJson, ":0");
            MammothState s = null;
            try { s = JsonUtility.FromJson<MammothState>(json); }
            catch (Exception e) { Status = "bad JSON from browser: " + e.Message; }

            if (s != null)
            {
                Tracker.Push(s, Time.unscaledTimeAsDouble);
                Status = Tracker.LastRejectReason == "" ? "ok" : Tracker.LastRejectReason;
            }
        }

        void LogJump() { if (logEvents) Debug.Log("[Mammoth] jump"); }
        void LogGrab() { if (logEvents) Debug.Log("[Mammoth] grab"); }
        void LogSession(string id) { if (logEvents) Debug.Log("[Mammoth] new session " + id + " (counter baseline reset)"); }

        // ---------------- Poll mode ----------------

        IEnumerator PollLoop()
        {
            while (true)
            {
                float started = Time.unscaledTime;

                using (var req = UnityWebRequest.Get(url))
                {
                    var op = req.SendWebRequest();
                    while (!op.isDone && Time.unscaledTime - started < receiveTimeout) yield return null;

                    if (!op.isDone)
                    {
                        req.Abort();
                        Status = "timeout: is the Python lab running?";
                    }
                    else if (req.result != UnityWebRequest.Result.Success)
                    {
                        Status = "no connection: " + req.error;
                    }
                    else
                    {
                        MammothState s = null;
                        string json = NullNumbers.Replace(req.downloadHandler.text, ":0");
                        try { s = JsonUtility.FromJson<MammothState>(json); }
                        catch (Exception e) { Status = "bad JSON: " + e.Message; }

                        if (s != null)
                        {
                            Tracker.Push(s, Time.unscaledTimeAsDouble);
                            Status = Tracker.LastRejectReason == "" ? "ok" : Tracker.LastRejectReason;
                        }
                    }
                }

                float wait = 1f / Mathf.Max(1f, pollRate) - (Time.unscaledTime - started);
                if (wait > 0) yield return new WaitForSecondsRealtime(wait);
                else yield return null;
            }
        }

        // ---------------- Listen mode ----------------

        void StartListening()
        {
            listenError = null;
            try
            {
                // HttpListener prefixes must end with '/'. Register the root and
                // filter by path in OnContext (accepts "/state" with or without a
                // trailing slash) instead of registering "/state" without one.
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{listenPort}/");
                listener.Start();
                listener.BeginGetContext(OnContext, null);
                Status = "listening on http://127.0.0.1:" + listenPort + "/state";
            }
            catch (Exception e)
            {
                listenError = "could not open port " + listenPort + ": " + e.Message;
                Debug.LogError("[Mammoth] " + listenError);
            }
        }

        void StopListening()
        {
            try { listener?.Stop(); listener?.Close(); } catch { /* already gone */ }
            listener = null;
        }

        void OnContext(IAsyncResult ar)
        {
            HttpListenerContext ctx;
            try
            {
                if (listener == null || !listener.IsListening) return;
                ctx = listener.EndGetContext(ar);
                listener.BeginGetContext(OnContext, null); // keep accepting
            }
            catch { return; } // listener was stopped

            try
            {
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.Close();
                    return;
                }

                string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');

                // Small camera-preview thumbnails (bottom-right corner overlay in
                // Unity) land here as raw JPEG bytes, separate from /state so the
                // 30 Hz gesture JSON never has to carry image data.
                if (path == "/frame")
                {
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        using var ms = new System.IO.MemoryStream();
                        ctx.Request.InputStream.CopyTo(ms);
                        latestFrame = ms.ToArray();
                        System.Threading.Interlocked.Increment(ref frameVersion);
                        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 405;
                    }
                    return;
                }

                if (path != "/state")
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                if (ctx.Request.HttpMethod == "POST")
                {
                    using var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                    string body = reader.ReadToEnd();
                    incoming.Enqueue(body);
                    var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                else if (ctx.Request.HttpMethod == "GET")
                {
                    string json = Tracker.Latest == null ? "{}" : JsonUtility.ToJson(Tracker.Latest);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    ctx.Response.StatusCode = 405;
                }
            }
            catch (Exception e)
            {
                try { ctx.Response.StatusCode = 500; } catch { /* response already gone */ }
                Debug.LogWarning("[Mammoth] request handling error: " + e.Message);
            }
            finally
            {
                try { ctx.Response.Close(); } catch { /* already closed */ }
            }
        }
    }
}
