using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Moves the mammoth along the mountain route (MammothRoute).
    /// - forward: travelSpeed from the mapping (already smoothed), times the
    ///   route's slope multiplier
    /// - lateral: continuous steering. The direction player's hand-height
    ///   difference (right minus left) works like a tilt: tilt further, slide
    ///   sideways faster; level hands, hold the current line. No fixed lanes.
    /// - vertical: road height at the current distance plus a local jump arc
    ///   started by each new jump event (jump_height applied once)
    /// - grab: each new grab event opens a short window in which Unity checks a
    ///   3D reach box in front of the trunk and collects at most one acorn
    /// Movement and actions are gated by MammothGameFlow (menus, countdown,
    /// pause and results never move or score).
    /// </summary>
    public class MammothController : MonoBehaviour
    {
        public MammothStateReceiver receiver;

        [Tooltip("Unity units per test-world unit. Agree on this with the mapping owner and recheck speed, steering and jump height in the scene.")]
        public float worldScale = 1f;

        [Tooltip("Where the grab reach is measured from (the trunk tip).")]
        public Transform grabOrigin;

        [Tooltip("Visual parts animated by the controller (optional).")]
        public Transform squirrel;
        public Transform trunk;

        [Header("Input source")]
        [Tooltip("Keyboard test mode: W/S speed, A/D steer, Space jump, E grab. Set by the game flow.")]
        public bool useKeyboard = true;
        [Tooltip("Kept for older scenes; the game flow decides the input source now.")]
        public bool keyboardFallback = true;

        [Header("Steering (tilt)")]
        [Tooltip("Hand-height difference below this is treated as level (no sideways motion).")]
        public float steerDeadzone = 0.2f;
        [Tooltip("Hand-height difference that gives full sideways speed.")]
        public float steerFull = 1.1f;
        [Tooltip("Sideways speed at full tilt, Unity units per second.")]
        public float steerSpeed = 4.5f;
        [Tooltip("How quickly sideways speed follows the tilt (higher = snappier).")]
        public float steerResponse = 7f;

        [Header("Grab (trunk reach box, Unity units)")]
        public float grabWindow = 0.18f;
        public float reachForward = 1.7f;
        public float reachBack = 0.7f;
        public float reachLateral = 0.95f;
        public float reachVertical = 0.65f;

        [Header("Set by MammothGameFlow")]
        public bool moveForward;
        public bool allowSteer;
        public bool acceptActions;
        [Tooltip("Advances jump and grab timing; frozen while paused.")]
        public bool clockRunning = true;
        [Tooltip("Speed multiplier for the tutorial/practice area, where the mammoth stays put.")]
        public bool holdPosition;

        public int Score { get; private set; }
        public int Acorns { get; private set; }
        public int Collisions { get; private set; }
        public int Jumps { get; private set; }
        public int GrabAttempts { get; private set; }
        public float LastGrabTime { get; private set; } = -100f;
        public bool LastGrabHit { get; private set; }
        public float Distance => distance;
        public float Lateral => lateral;
        /// <summary>Forward input in test-world units/s, before the slope multiplier.</summary>
        public float InputSpeed { get; private set; }
        /// <summary>Actual forward speed this frame, test-world units/s.</summary>
        public float ActualSpeed { get; private set; }
        /// <summary>0..1 throttle, for gauges.</summary>
        public float Throttle { get; private set; }
        /// <summary>-1..1 current tilt input after the dead zone.</summary>
        public float Steer { get; private set; }
        public bool Airborne => cfg != null && motion.Airborne(clock, cfg);
        public float GroundY => MammothRoute.Height(distance);

        public event Action<MammothReward> OnCollected;
        public event Action OnEmptyGrab;
        public event Action OnJumped;

        MammothSettings cfg;
        MammothMotion motion = new MammothMotion();
        readonly HashSet<int> collectedIds = new HashSet<int>();
        float distance;
        float lateral;
        float lateralVel;
        float keySpeed;
        double clock;
        double grabOpenUntil = -1;
        bool grabOpen;
        float trunkRaise;
        bool trunkRaised;
        float wobbleUntil = -1f;
        Quaternion trunkBaseRot;

        void Awake()
        {
            cfg = MammothSettingsAsset.Load();
            keySpeed = cfg.min_speed;
            if (trunk != null) trunkBaseRot = trunk.localRotation;
        }

        void Start()
        {
            if (receiver == null) receiver = FindFirstObjectByType<MammothStateReceiver>();
            if (receiver != null)
            {
                receiver.Tracker.OnJump += HandleJump;
                receiver.Tracker.OnGrab += HandleGrab;
            }
            ApplyPose(0f);
        }

        void OnDestroy()
        {
            if (receiver == null) return;
            receiver.Tracker.OnJump -= HandleJump;
            receiver.Tracker.OnGrab -= HandleGrab;
        }

        /// <summary>Doc: at the start, reset route progress, score, collision count
        /// and collected IDs, and place the elephant at the center.</summary>
        public void ResetRun()
        {
            distance = MammothRoute.StartZ;
            lateral = 0f;
            lateralVel = 0f;
            Score = 0; Acorns = 0; Collisions = 0; Jumps = 0; GrabAttempts = 0;
            collectedIds.Clear();
            motion = new MammothMotion();
            grabOpen = false;
            keySpeed = cfg.min_speed;
            SetTrunkRaised(false);
            foreach (var r in FindObjectsByType<MammothReward>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!r.practice) r.ResetForRun();
            ApplyPose(0f);
        }

        /// <summary>Centers the mammoth sideways (tutorial start).</summary>
        public void Recenter() { lateral = 0f; lateralVel = 0f; }

        public void SetTrunkRaised(bool raised) => trunkRaised = raised;

        void Update()
        {
            float dt = Time.deltaTime;
            if (clockRunning) clock += dt;

            ReadInput(out float speedIn, out float steerIn, out float throttle);
            InputSpeed = speedIn;
            Throttle = throttle;
            Steer = steerIn;

            float targetVel = allowSteer ? steerIn * steerSpeed : 0f;
            lateralVel += (targetVel - lateralVel) * (1f - Mathf.Exp(-steerResponse * dt));
            if (!clockRunning) lateralVel = 0f;
            lateral = Mathf.Clamp(lateral + lateralVel * dt * worldScale, -MammothRoute.SteerLimit, MammothRoute.SteerLimit);

            float mult = MammothRoute.SpeedMultiplier(distance);
            ActualSpeed = (moveForward && !holdPosition) ? speedIn * mult : 0f;
            distance += ActualSpeed * worldScale * dt;

            UpdateReach();
            UpdateGrabWindow();
            ApplyPose(dt);
        }

        void ReadInput(out float speed, out float steer, out float throttle)
        {
            if (useKeyboard)
            {
                float target = MammothKeys.Held(KeyCode.W) ? cfg.max_speed
                             : MammothKeys.Held(KeyCode.S) ? cfg.min_speed
                             : Mathf.Lerp(cfg.min_speed, cfg.max_speed, 0.4f);
                float alpha = 1f - Mathf.Exp(-Mathf.Min(Time.deltaTime, .2f) / cfg.smooth);
                keySpeed += (target - keySpeed) * alpha;
                speed = keySpeed;
                throttle = Mathf.InverseLerp(cfg.min_speed, cfg.max_speed, keySpeed);
                steer = (MammothKeys.Held(KeyCode.D) ? 1f : 0f) - (MammothKeys.Held(KeyCode.A) ? 1f : 0f);
                if (MammothKeys.Down(KeyCode.Space)) HandleJump();
                if (MammothKeys.Down(KeyCode.E)) HandleGrab();
                return;
            }

            speed = 0f; steer = 0f; throttle = 0f;
            if (receiver == null || !receiver.ControlAllowed) return;
            var s = receiver.State;
            speed = receiver.TravelSpeed;
            throttle = s != null ? Mathf.Clamp01(s.speed) : 0f;
            if (s != null && s.visible != null && s.visible.Length > 1 && s.visible[1])
            {
                // Screen-region left/right as the mapping reports it (no extra mirroring).
                float diff = s.rightHand - s.leftHand;
                float mag = Mathf.InverseLerp(steerDeadzone, steerFull, Mathf.Abs(diff));
                steer = Mathf.Sign(diff) * mag * mag * (3f - 2f * mag);
            }
        }

        void HandleJump()
        {
            if (!acceptActions || cfg == null) return;
            if (motion.Airborne(clock, cfg)) return; // doc: reject, never queue
            Jumps++;
            motion.StartJump(clock);
            OnJumped?.Invoke();
        }

        void HandleGrab()
        {
            if (!acceptActions) return;
            GrabAttempts++;
            LastGrabTime = Time.time;
            grabOpen = true;
            grabOpenUntil = clock + grabWindow;
            if (TryCollect()) return;
        }

        void UpdateGrabWindow()
        {
            if (!grabOpen) return;
            if (TryCollect()) return;
            if (clock >= grabOpenUntil)
            {
                grabOpen = false;
                LastGrabHit = false;
                OnEmptyGrab?.Invoke();
            }
        }

        bool TryCollect()
        {
            var best = NearestReachable();
            if (best == null) return false;
            grabOpen = false;
            LastGrabHit = true;
            if (!best.practice)
            {
                if (collectedIds.Contains(best.id)) return true;
                collectedIds.Add(best.id);
                Score += best.Collect();
                Acorns++;
            }
            else best.Collect();
            OnCollected?.Invoke(best);
            return true;
        }

        Vector3 GrabFrom => (grabOrigin != null ? grabOrigin : transform).position;

        bool InReach(MammothReward r, Vector3 from)
        {
            Vector3 d = r.transform.position - from;
            return d.z >= -reachBack && d.z <= reachForward
                && Mathf.Abs(d.x) <= reachLateral
                && Mathf.Abs(d.y) <= reachVertical;
        }

        MammothReward NearestReachable()
        {
            Vector3 from = GrabFrom;
            MammothReward best = null;
            float bestDist = float.MaxValue;
            foreach (var r in MammothReward.Active)
            {
                if (!InReach(r, from)) continue;
                float d = (r.transform.position - from).sqrMagnitude;
                if (d < bestDist) { best = r; bestDist = d; }
            }
            return best;
        }

        void UpdateReach()
        {
            Vector3 from = GrabFrom;
            foreach (var r in MammothReward.Active)
                r.reachable = acceptActions && InReach(r, from);
        }

        void ApplyPose(float dt)
        {
            float jumpY = cfg != null ? motion.JumpOffset(clock, cfg) * worldScale : 0f;
            float ground = MammothRoute.Height(distance);
            transform.position = new Vector3(lateral, ground + jumpY, distance);

            float pitch = -Mathf.Atan(MammothRoute.Slope(distance)) * Mathf.Rad2Deg;
            float yaw = Mathf.Clamp(lateralVel * 5f, -22f, 22f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            if (squirrel != null)
            {
                float wobble = Time.time < wobbleUntil ? Mathf.Sin(Time.time * 30f) * 12f : 0f;
                float lean = -Steer * 20f;
                squirrel.localRotation = Quaternion.Slerp(squirrel.localRotation,
                    Quaternion.Euler(0f, 0f, lean + wobble), 1f - Mathf.Exp(-10f * Mathf.Max(dt, 0.0001f)));
            }

            if (trunk != null)
            {
                trunkRaise = Mathf.MoveTowards(trunkRaise, trunkRaised ? 1f : 0f, dt * 1.5f);
                trunk.localRotation = trunkBaseRot * Quaternion.Euler(-110f * trunkRaise, 0f, 0f);
            }
        }

        /// <summary>Squirrel wobble used for feedback (collisions, celebrations).</summary>
        public void Wobble(float seconds = 0.6f) => wobbleUntil = Time.time + seconds;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .8f, .2f, .6f);
            Vector3 from = GrabFrom;
            Vector3 center = from + new Vector3(0f, 0f, (reachForward - reachBack) * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(reachLateral * 2f, reachVertical * 2f, reachForward + reachBack));
        }
    }
}
