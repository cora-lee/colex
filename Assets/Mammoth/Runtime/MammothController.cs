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

        [Header("Terrain patches")]
        [Tooltip("Stretches of road (by distance) with mud or slick ice. Set by the scene builder.")]
        public MammothTerrainSegment[] terrain = System.Array.Empty<MammothTerrainSegment>();
        [Tooltip("Travel speed multiplier while inside a Mud segment.")]
        public float mudSpeedFactor = 0.45f;
        [Tooltip("Sideways slide amplitude (Unity units) while on slick ice.")]
        public float iceSlideAmplitude = 0.55f;
        [Tooltip("How fast the ice slide sways.")]
        public float iceSlideFrequency = 1.7f;
        [Tooltip("On ice, how quickly sideways speed follows the tilt (low = slides on, hard to stop).")]
        public float iceSteerResponse = 0.7f;
        [Tooltip("On ice, a small tilt is amplified this much (and the slide is faster).")]
        public float iceSteerGain = 2.6f;
        public float iceSlideSpeed = 7.5f;
        [Tooltip("On ice, extra sideways push that keeps nudging the mammoth back and forth.")]
        public float iceSlidePush = 3.2f;

        [Header("Steering (tilt)")]
        [Tooltip("Hand-height difference below this is treated as level (no sideways motion).")]
        public float steerDeadzone = 0.2f;
        [Tooltip("Shoulder lean (height difference / shoulder width) ignored as noise.")]
        public float tiltDeadzone = 0.05f;
        [Tooltip("Shoulder lean that gives full steering.")]
        public float tiltFull = 0.35f;
        [Tooltip("Hand-height difference that gives full sideways speed.")]
        public float steerFull = 1.1f;
        [Tooltip("Sideways speed at full tilt, Unity units per second.")]
        public float steerSpeed = 4.5f;
        [Tooltip("How quickly sideways speed follows the tilt (higher = snappier).")]
        public float steerResponse = 7f;

        [Header("Grab (trunk reach box, Unity units)")]
        public float grabWindow = 0.3f;
        public float reachForward = 2.4f;
        public float reachBack = 1.0f;
        public float reachLateral = 1.4f;
        public float reachVertical = 0.9f;

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
        public MammothTerrainType CurrentTerrain { get; private set; }
        /// <summary>Sideways position including the ice slide.</summary>
        public float EffectiveLateral => Mathf.Clamp(lateral + iceDrift, -MammothRoute.SteerLimit, MammothRoute.SteerLimit);

        public event Action<MammothReward> OnCollected;
        public event Action OnEmptyGrab;
        public event Action OnJumped;
        public event Action OnCollision;

        [Header("Collisions (doc proposal)")]
        public int collisionPenalty = 5;
        public float collisionSlowFactor = 0.5f;
        public float collisionSlowSeconds = 0.8f;
        public float collisionProtectSeconds = 1f;
        [Tooltip("Mammoth body radius used against obstacle footprints.")]
        public float bodyRadius = 0.55f;
        [Tooltip("A bump makes the mammoth sit down for this long. It costs time, not points.")]
        public float sitSeconds = 1.5f;
        [Tooltip("How far the trunk swings up for a grab, in degrees.")]
        public float trunkRaiseAngle = 110f;

        double slowUntil = -1;
        double protectUntil = -1;
        double sitUntil = -1;
        float grabPoseUntil = -1f;

        /// <summary>Seconds of play in this run (stops while paused or finished).</summary>
        public float RunTime { get; private set; }
        public bool Sitting => clock < sitUntil;
        /// <summary>Editor preview only.</summary>
        public void DebugSit() => sitUntil = clock + sitSeconds;
        /// <summary>0..1 sit pose for the art: quick flop down, slower push back up.</summary>
        public float SitAmount
        {
            get
            {
                if (clock >= sitUntil) return 0f;
                float left = (float)(sitUntil - clock);
                return Mathf.Clamp01(Mathf.Min((sitSeconds - left) / 0.18f, left / 0.4f));
            }
        }

        MammothSettings cfg;
        MammothMotion motion = new MammothMotion();
        readonly HashSet<int> collectedIds = new HashSet<int>();
        float distance;
        float lateral;
        float lateralVel;
        float iceDrift;
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
            iceDrift = 0f;
            Score = 0; Acorns = 0; Collisions = 0; Jumps = 0; GrabAttempts = 0;
            collectedIds.Clear();
            motion = new MammothMotion();
            grabOpen = false;
            slowUntil = -1; protectUntil = -1; sitUntil = -1; grabPoseUntil = -1f;
            RunTime = 0f;
            keySpeed = Mathf.Lerp(cfg.min_speed, cfg.max_speed, 0.4f);
            foreach (var o in MammothObstacle.Active) o.hitThisPass = false;
            SetTrunkRaised(false);
            foreach (var r in FindObjectsByType<MammothReward>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!r.practice) r.ResetForRun();
            ApplyPose(0f);
        }

        /// <summary>Editor UI preview only: jump to a distance on the route.</summary>
        public void DebugJumpTo(float z) { distance = z; ApplyPose(0f); }

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

            CurrentTerrain = SampleTerrain(distance);
            bool onIce = CurrentTerrain == MammothTerrainType.Ice && clockRunning && !holdPosition;

            // On ice the mammoth keeps its sideways momentum (slow to respond,
            // slow to stop) and gets nudged back and forth, so it really slides.
            float targetVel = allowSteer ? steerIn * steerSpeed : 0f;
            if (onIce && allowSteer)
            {
                // A small tilt becomes a big slide; level hands don't stop it
                // (target follows the current drift, so momentum carries on).
                float amp = Mathf.Clamp(steerIn * iceSteerGain, -1f, 1f);
                targetVel = Mathf.Abs(steerIn) > 0.02f ? amp * iceSlideSpeed : lateralVel * 0.97f;
            }
            float resp = onIce ? iceSteerResponse * (Mathf.Abs(steerIn) > 0.02f ? 2.2f : 1f) : steerResponse;
            lateralVel += (targetVel - lateralVel) * (1f - Mathf.Exp(-resp * dt));
            if (onIce) lateralVel += Mathf.Sin((float)clock * iceSlideFrequency + distance * 0.07f) * iceSlidePush * dt;
            if (!clockRunning) lateralVel = 0f;
            float nextLateral = lateral + lateralVel * dt * worldScale;
            if (Mathf.Abs(nextLateral) > MammothRoute.SteerLimit) lateralVel *= -0.25f; // soft bounce off the road edge
            lateral = Mathf.Clamp(nextLateral, -MammothRoute.SteerLimit, MammothRoute.SteerLimit);

            float driftTarget = CurrentTerrain == MammothTerrainType.Ice && clockRunning && !holdPosition
                ? Mathf.Sin((float)clock * iceSlideFrequency + distance * 0.05f) * iceSlideAmplitude : 0f;
            iceDrift += (driftTarget - iceDrift) * (1f - Mathf.Exp(-4f * dt));

            float mult = MammothRoute.SpeedMultiplier(distance);
            if (CurrentTerrain == MammothTerrainType.Mud) mult *= mudSpeedFactor;
            if (clock < slowUntil) mult *= collisionSlowFactor;
            if (Sitting) mult = 0f;
            ActualSpeed = (moveForward && !holdPosition) ? speedIn * mult : 0f;
            if (moveForward && !holdPosition && clockRunning) RunTime += dt;
            distance += ActualSpeed * worldScale * dt;

            if (moveForward && !holdPosition && clockRunning) CheckCollisions();
            UpdateReach();
            UpdateGrabWindow();
            ApplyPose(dt);
        }

        MammothTerrainType SampleTerrain(float z)
        {
            for (int i = 0; i < terrain.Length; i++)
                if (terrain[i].Contains(z)) return terrain[i].type;
            return MammothTerrainType.Normal;
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
                // Lean the shoulders: a little lean steers a little, a big lean
                // steers hard. Positive tilt = leaning toward the screen left.
                float lean = -s.tilt;
                float dz = CurrentTerrain == MammothTerrainType.Ice ? tiltDeadzone * 0.5f : tiltDeadzone; // ice reacts to the slightest lean
                float mag = Mathf.InverseLerp(dz, tiltFull, Mathf.Abs(lean));
                steer = Mathf.Sign(lean) * mag;
            }

            // Keyboard fills in for any role nobody is standing in (solo or
            // short-handed testing): W/S speed, A/D steer, Space jump, E grab.
            if (!keyboardFallback) return;
            bool speedSeen = s != null && s.visible != null && s.visible.Length > 0 && s.visible[0];
            float kTarget = MammothKeys.Held(KeyCode.W) ? cfg.max_speed
                          : MammothKeys.Held(KeyCode.S) ? cfg.min_speed
                          : Mathf.Lerp(cfg.min_speed, cfg.max_speed, 0.4f);
            float a = 1f - Mathf.Exp(-Mathf.Min(Time.deltaTime, .2f) / cfg.smooth);
            keySpeed += (kTarget - keySpeed) * a;
            if (!speedSeen)
            {
                speed = keySpeed;
                throttle = Mathf.InverseLerp(cfg.min_speed, cfg.max_speed, keySpeed);
            }
            float kSteer = (MammothKeys.Held(KeyCode.D) ? 1f : 0f) - (MammothKeys.Held(KeyCode.A) ? 1f : 0f);
            if (kSteer != 0f) steer = kSteer;
            if (MammothKeys.Down(KeyCode.Space)) HandleJump();
            if (MammothKeys.Down(KeyCode.E)) HandleGrab();
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
            grabPoseUntil = Mathf.Max(grabPoseUntil, Time.time + 0.3f);
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
            grabPoseUntil = Time.time + 0.9f;
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

        void CheckCollisions()
        {
            foreach (var o in MammothObstacle.Active)
            {
                float dx = o.transform.position.x - EffectiveLateral;
                float dz = o.transform.position.z - distance;
                bool touching = dx * dx + dz * dz < (o.radius + bodyRadius) * (o.radius + bodyRadius);
                if (!touching) { if (dz < -o.radius - 2f) o.hitThisPass = false; continue; }
                // Continuous contact never deducts again; pass through it.
                if (o.hitThisPass || clock < protectUntil) continue;
                o.hitThisPass = true;
                // No point penalty: the mammoth flops down and the clock keeps running.
                sitUntil = clock + sitSeconds;
                protectUntil = sitUntil + collisionProtectSeconds;
                lateralVel = 0f;
                Collisions++;
                Wobble(0.7f);
                OnCollision?.Invoke();
            }
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
            transform.position = new Vector3(EffectiveLateral, ground + jumpY, distance);

            float pitch = -Mathf.Atan(MammothRoute.Slope(distance)) * Mathf.Rad2Deg;
            float yaw = Mathf.Clamp(lateralVel * 5f, -22f, 22f);
            float roll = 0f;
            if (CurrentTerrain == MammothTerrainType.Ice && clockRunning && !holdPosition)
            {
                // Wobbly footing: body twists and tips while it slides.
                float w = (float)clock * iceSlideFrequency * 1.6f;
                yaw += Mathf.Sin(w) * 9f + lateralVel * 3f;
                roll = Mathf.Sin(w * 1.3f + 1f) * 6f - lateralVel * 2.5f;
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);

            if (squirrel != null)
            {
                float wobble = Time.time < wobbleUntil ? Mathf.Sin(Time.time * 30f) * 12f : 0f;
                float lean = -Steer * 20f;
                squirrel.localRotation = Quaternion.Slerp(squirrel.localRotation,
                    Quaternion.Euler(0f, 0f, lean + wobble), 1f - Mathf.Exp(-10f * Mathf.Max(dt, 0.0001f)));
            }

            if (trunk != null)
            {
                bool up = trunkRaised || Time.time < grabPoseUntil;
                trunkRaise = Mathf.MoveTowards(trunkRaise, up ? 1f : 0f, dt * (trunkRaised ? 1.5f : 6f));
                trunk.localRotation = trunkBaseRot * Quaternion.Euler(-trunkRaiseAngle * trunkRaise, 0f, 0f);
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
