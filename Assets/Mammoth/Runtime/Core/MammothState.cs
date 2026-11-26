using System;

namespace Mammoth
{
    /// <summary>
    /// One /state response from the Python lab (protocolVersion 3).
    /// Nullable JSON numbers (leftHand, jumpHandHeight, ...) arrive as 0 through JsonUtility,
    /// so use the matching *Tracked / directionReady flags before reading them.
    /// </summary>
    [Serializable]
    public class MammothState
    {
        public int protocolVersion;
        public string sessionId;
        public string mode;
        public int[] required;
        public bool running;
        public bool calibrated;
        public bool calibrating;
        public bool[] visible;

        public float speed;        // throttle 0..1
        public float travelSpeed;  // already smoothed, test-world units/s
        public int lane;           // -1, 0, 1

        public float leftHand;
        public float rightHand;
        public bool directionReady;

        public bool jumpTracked;
        public float jumpHandHeight;
        public bool jumpReady;
        public int jumps;
        public float jumpHeight;   // normalized 0..1, multiply by jump_height once

        public bool clapTracked;
        public float handDistance;
        public bool grabReady;
        public int grabs;
        public bool grabActive;    // 0.5 s UI flash only, never use as an event

        public string[] events;
        public string @event;
        public double updated;     // server monotonic clock
    }
}
