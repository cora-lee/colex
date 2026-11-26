using System;

namespace Mammoth
{
    /// <summary>
    /// Turns /state snapshots into movement values and one-shot jump/grab events,
    /// following the receiver rules in UNITY_HANDOFF.md:
    /// - events come from increases in jumps/grabs within the SAME sessionId
    /// - a new session (or first connect) only sets a baseline, history is never replayed
    /// - states with an older or equal `updated` value are ignored
    /// - events are blocked when not running/calibrated, session metadata is missing,
    ///   or (team game) mode is not "team"
    /// - no fresh state within ReceiveTimeout => connection treated as lost
    /// Pure C#, no UnityEngine dependency, so it can be unit tested outside Unity.
    /// </summary>
    public class MammothEventTracker
    {
        public const int ExpectedProtocol = 3;

        public double ReceiveTimeout = 0.5;
        public bool RequireTeamMode = true;

        public event Action OnJump;
        public event Action OnGrab;
        public event Action<string> OnSessionChanged;

        public MammothState Latest { get; private set; }
        public string SessionId { get; private set; }
        public string LastRejectReason { get; private set; } = "no data yet";

        int baseJumps, baseGrabs;
        double lastUpdated = double.NegativeInfinity;
        double lastAcceptedLocal = double.NegativeInfinity;

        /// <summary>True while a fresh, usable state exists.</summary>
        public bool Connected(double localNow) =>
            Latest != null && localNow - lastAcceptedLocal <= ReceiveTimeout;

        public bool ControlAllowed(double localNow) =>
            Connected(localNow) && Gate(Latest) == null;

        /// <summary>Throttle-smoothed travel speed (units/s); 0 when control is not allowed.</summary>
        public float TravelSpeed(double localNow) => ControlAllowed(localNow) ? Latest.travelSpeed : 0f;

        /// <summary>Current lane (-1..1). Keeps the last known lane while disconnected.</summary>
        public int Lane => Latest == null ? 0 : Math.Max(-1, Math.Min(1, Latest.lane));

        string Gate(MammothState s)
        {
            if (string.IsNullOrEmpty(s.sessionId) || string.IsNullOrEmpty(s.mode)) return "session metadata missing";
            if (!s.running) return "lab not running";
            if (!s.calibrated) return "not calibrated";
            if (RequireTeamMode && s.mode != "team") return "mode is '" + s.mode + "', team game requires 'team'";
            return null;
        }

        /// <summary>Feed one parsed /state. localNow is the Unity-side clock (seconds).</summary>
        /// <returns>true if the state was accepted.</returns>
        public bool Push(MammothState s, double localNow)
        {
            if (s == null) { LastRejectReason = "empty response"; return false; }
            if (s.protocolVersion != ExpectedProtocol)
            {
                LastRejectReason = "protocolVersion " + s.protocolVersion + " (expected " + ExpectedProtocol + ")";
                return false;
            }

            bool newSession = s.sessionId != SessionId;
            if (!newSession && s.updated <= lastUpdated)
            {
                LastRejectReason = "stale state ignored";
                return false;
            }

            Latest = s;
            lastUpdated = s.updated;
            lastAcceptedLocal = localNow;

            if (newSession)
            {
                SessionId = s.sessionId;
                baseJumps = s.jumps;
                baseGrabs = s.grabs;
                OnSessionChanged?.Invoke(SessionId);
                LastRejectReason = Gate(s) ?? "";
                return true;
            }

            string block = Gate(s);
            LastRejectReason = block ?? "";

            // Counters should only rise within a session; if not, re-baseline.
            int newJumps = s.jumps - baseJumps;
            int newGrabs = s.grabs - baseGrabs;
            baseJumps = s.jumps;
            baseGrabs = s.grabs;

            if (block == null)
            {
                for (int i = 0; i < newJumps; i++) OnJump?.Invoke();
                for (int i = 0; i < newGrabs; i++) OnGrab?.Invoke();
            }
            return true;
        }

        /// <summary>Forget the session so the next state sets a fresh baseline.</summary>
        public void Reset()
        {
            Latest = null;
            SessionId = null;
            lastUpdated = double.NegativeInfinity;
            lastAcceptedLocal = double.NegativeInfinity;
            LastRejectReason = "reset";
        }
    }
}
