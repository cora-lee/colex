using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Moves the mammoth from receiver data.
    /// - forward: travelSpeed from the lab (already smoothed, do not smooth again)
    /// - lateral: lane * lane_width, smoothstep over lane_time
    /// - vertical: local parabolic arc started by each jump event (jump_height applied once)
    /// - grab: on each grab event, Unity checks for a reward inside grabRange
    /// </summary>
    public class MammothController : MonoBehaviour
    {
        public MammothStateReceiver receiver;

        [Tooltip("Unity units per test-world unit. Agree on this with the mapping owner and recheck speed, lane spacing and jump height in the scene.")]
        public float worldScale = 1f;

        [Tooltip("Optional: where the grab reach is measured from (e.g. the trunk). Defaults to this transform.")]
        public Transform grabOrigin;

        [Tooltip("Reward reach in Unity units. The lab only reports a grab gesture; collection is decided here.")]
        public float grabRange = 1.5f;

        [Tooltip("Allow keyboard testing without the lab: W/S speed, A/D lanes, Space jump, E grab.")]
        public bool keyboardFallback = true;

        [Tooltip("Stretches of track (by forward distance) that slow the mammoth down or make it slide. Set by the scene builder.")]
        public MammothTerrainSegment[] terrain = System.Array.Empty<MammothTerrainSegment>();
        [Tooltip("Travel speed multiplier while inside a Mud segment.")]
        public float mudSpeedFactor = 0.45f;
        [Tooltip("Lateral slide amplitude (Unity units) while inside an Ice segment.")]
        public float iceSlideAmplitude = 0.55f;
        [Tooltip("How fast the ice slide oscillates.")]
        public float iceSlideFrequency = 1.7f;

        public int Score { get; private set; }
        public int Jumps { get; private set; }
        public int GrabAttempts { get; private set; }
        public float LastGrabTime { get; private set; } = -100f;
        public bool LastGrabHit { get; private set; }
        public MammothTerrainType CurrentTerrain { get; private set; }

        MammothSettings cfg;
        readonly MammothMotion motion = new MammothMotion();
        Vector3 origin;
        float distance;
        int keyLane;
        float keySpeed;

        void Start()
        {
            cfg = MammothSettingsAsset.Load();
            origin = transform.position;
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            if (receiver != null)
            {
                receiver.Tracker.OnJump += HandleJump;
                receiver.Tracker.OnGrab += HandleGrab;
            }
            else Debug.LogWarning("[Mammoth] No MammothStateReceiver in scene; keyboard only.");
        }

        void OnDestroy()
        {
            if (receiver == null) return;
            receiver.Tracker.OnJump -= HandleJump;
            receiver.Tracker.OnGrab -= HandleGrab;
        }

        bool UsingLab => receiver != null && receiver.ControlAllowed;

        void Update()
        {
            double now = Time.timeAsDouble;
            int lane;
            float speed;

            if (UsingLab)
            {
                lane = receiver.Lane;
                speed = receiver.TravelSpeed;
            }
            else if (keyboardFallback)
            {
                ReadKeyboard();
                lane = keyLane;
                speed = keySpeed;
            }
            else
            {
                lane = motion.Lane;
                speed = 0f;
            }

            CurrentTerrain = SampleTerrain(distance);
            float speedFactor = CurrentTerrain == MammothTerrainType.Mud ? mudSpeedFactor : 1f;
            distance += speed * speedFactor * worldScale * Time.deltaTime;

            float x = motion.Lateral(lane, now, cfg) * worldScale;
            if (CurrentTerrain == MammothTerrainType.Ice)
                x += Mathf.Sin((float)now * iceSlideFrequency + distance * 0.05f) * iceSlideAmplitude;
            float y = motion.JumpOffset(now, cfg) * worldScale;
            transform.position = origin + new Vector3(x, y, distance);
        }

        MammothTerrainType SampleTerrain(float z)
        {
            for (int i = 0; i < terrain.Length; i++)
                if (terrain[i].Contains(z)) return terrain[i].type;
            return MammothTerrainType.Normal;
        }

        void ReadKeyboard()
        {
            float target = MammothKeys.Held(KeyCode.W) ? cfg.max_speed : MammothKeys.Held(KeyCode.S) ? 0f : cfg.min_speed;
            float alpha = 1f - Mathf.Exp(-Mathf.Min(Time.deltaTime, .2f) / cfg.smooth);
            keySpeed += (target - keySpeed) * alpha;
            if (MammothKeys.Down(KeyCode.A)) keyLane = Mathf.Max(-1, keyLane - 1);
            if (MammothKeys.Down(KeyCode.D)) keyLane = Mathf.Min(1, keyLane + 1);
            if (MammothKeys.Down(KeyCode.Space)) HandleJump();
            if (MammothKeys.Down(KeyCode.E)) HandleGrab();
        }

        void HandleJump()
        {
            Jumps++;
            motion.StartJump(Time.timeAsDouble);
        }

        void HandleGrab()
        {
            GrabAttempts++;
            LastGrabTime = Time.time;
            Vector3 from = (grabOrigin != null ? grabOrigin : transform).position;

            MammothReward best = null;
            float bestDist = float.MaxValue;
            foreach (var r in MammothReward.Active)
            {
                float d = Vector3.Distance(from, r.transform.position);
                if (d <= grabRange && d < bestDist) { best = r; bestDist = d; }
            }

            LastGrabHit = best != null;
            if (best != null) Score += best.Collect();
        }

        public bool Airborne => cfg != null && motion.Airborne(Time.timeAsDouble, cfg);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .8f, .2f, .6f);
            Gizmos.DrawWireSphere((grabOrigin != null ? grabOrigin : transform).position, grabRange);
        }
    }
}
