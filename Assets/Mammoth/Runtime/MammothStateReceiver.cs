using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Mammoth
{
    /// <summary>
    /// Integration option B: polls the Python lab at http://127.0.0.1:8765/state
    /// and exposes movement values plus one-shot OnJump / OnGrab events.
    /// The lab must be running on the same computer (it binds to loopback only).
    /// </summary>
    public class MammothStateReceiver : MonoBehaviour
    {
        [Tooltip("Python lab endpoint. Loopback only.")]
        public string url = "http://127.0.0.1:8765/state";

        [Tooltip("Requests per second.")]
        [Range(5, 60)] public float pollRate = 30f;

        [Tooltip("Abort a request and treat tracking as lost after this many seconds without a fresh state.")]
        public float receiveTimeout = 0.5f;

        [Tooltip("Team game: only accept events when the lab is in 'team' mode.")]
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

        void OnEnable()
        {
            Tracker.OnJump += LogJump;
            Tracker.OnGrab += LogGrab;
            Tracker.OnSessionChanged += LogSession;
            StartCoroutine(PollLoop());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            Tracker.OnJump -= LogJump;
            Tracker.OnGrab -= LogGrab;
            Tracker.OnSessionChanged -= LogSession;
            Tracker.Reset();
        }

        void LogJump() { if (logEvents) Debug.Log("[Mammoth] jump"); }
        void LogGrab() { if (logEvents) Debug.Log("[Mammoth] grab"); }
        void LogSession(string id) { if (logEvents) Debug.Log("[Mammoth] new session " + id + " (counter baseline reset)"); }

        IEnumerator PollLoop()
        {
            while (true)
            {
                Tracker.ReceiveTimeout = receiveTimeout;
                Tracker.RequireTeamMode = requireTeamMode;
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
                        // The lab sends null for untracked numbers; JsonUtility has no nullable floats,
                        // so map them to 0 and rely on the *Tracked / *Ready flags.
                        string json = NullNumbers.Replace(req.downloadHandler.text, ":0");
                        try { s = JsonUtility.FromJson<MammothState>(json); }
                        catch (System.Exception e) { Status = "bad JSON: " + e.Message; }

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
    }
}
