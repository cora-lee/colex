using System;

namespace Mammoth
{
    /// <summary>
    /// Direct C# port of the Mapping class in Web/mammoth-mapping.js (itself a
    /// port of Handoff/signals.py), so the exact same calibration, speed
    /// smoothing, lane-steer, jump and grab/clap logic runs in-process here
    /// instead of in a browser page that POSTs JSON over loopback HTTP.
    /// Pure C#, no UnityEngine dependency (besides the settings/state types),
    /// so the state machine itself stays unit-testable like MammothEventTracker.
    /// </summary>
    public class MammothGestureMapping
    {
        readonly MammothSettings cfg;

        string sessionId;
        bool calibrating;
        double? firstSampleAt;
        bool neutralSet;

        float speed, travelSpeed;
        int lane;
        bool directionReady;

        bool jumpReady;
        int jumpCount;
        double jumpAt = -100;

        bool grabReady;
        int grabCount;
        double grabAt = -100;

        double? lastNow;

        public MammothGestureMapping(MammothSettings settings)
        {
            cfg = settings;
            Reset();
        }

        public void Reset()
        {
            sessionId = Guid.NewGuid().ToString("N");
            neutralSet = false;
            firstSampleAt = null;
            calibrating = true;
            speed = 0; travelSpeed = 0;
            lane = 0;
            directionReady = false; jumpReady = false; grabReady = false;
            jumpAt = -100; grabAt = -100;
            jumpCount = 0; grabCount = 0;
            lastNow = null;
        }

        static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        /// <summary>obs[0]=left(speed), obs[1]=center(steer), obs[2]=right(jump/grab),
        /// matching Web/mammoth-mapping.js's [a, b, c] destructuring.</summary>
        /// <summary>Test shortcut: treat the session as calibrated now, so a single
        /// tester can play with whichever roles the camera sees.</summary>
        public void ForceCalibrate() { neutralSet = true; calibrating = false; }

        /// <summary>Solo play: calibration starts as soon as anyone is in view,
        /// instead of waiting for all three regions.</summary>
        public bool soloMode;

        public MammothState Update(ZoneObservation?[] obs, double now)
        {
            var a = obs[0]; var b = obs[1]; var c = obs[2];
            double dt = lastNow.HasValue ? Math.Min(0.2, Math.Max(0, now - lastNow.Value)) : 0.03;
            lastNow = now;

            bool ready = soloMode
                ? (a.HasValue || b.HasValue || c.HasValue)
                : a.HasValue && b.HasValue && c.HasValue && c.Value.jumpValid && c.Value.clapValid;

            if (calibrating)
            {
                if (ready)
                {
                    if (firstSampleAt == null) firstSampleAt = now;
                    if (now - firstSampleAt.Value >= cfg.calibration) { neutralSet = true; calibrating = false; }
                }
                else
                {
                    firstSampleAt = null;
                }
            }
            bool active = neutralSet;

            float target = (active && a.HasValue) ? Clamp01((a.Value.height - cfg.speed_low) / (cfg.speed_high - cfg.speed_low)) : 0f;
            float alpha = 1f - (float)Math.Exp(-dt / cfg.smooth);
            speed += (target - speed) * alpha;
            float travelTarget = (active && a.HasValue) ? cfg.min_speed + speed * (cfg.max_speed - cfg.min_speed) : 0f;
            travelSpeed += (travelTarget - travelSpeed) * alpha;

            string lastEvent = "";

            if (!active || !b.HasValue) directionReady = false;
            else if (Math.Max(b.Value.leftHeight, b.Value.rightHeight) <= cfg.hand_reset) directionReady = true;
            else if (directionReady)
            {
                bool left = b.Value.leftHeight >= cfg.hand_trigger, right = b.Value.rightHeight >= cfg.hand_trigger;
                if (left && right) directionReady = false;
                else if (left || right)
                {
                    lane = Math.Max(-1, Math.Min(1, lane + (left ? -1 : 1)));
                    directionReady = false;
                    lastEvent = left ? "left" : "right";
                }
            }

            // jumpTracked/clapTracked/handDistance (the REPORTED fields) reflect
            // whether zone C itself is being read cleanly, independent of
            // calibration; only the jumpReady/grabReady STATE MACHINES require
            // `active`, matching mammoth-mapping.js's jumpHands/clapValid split.
            bool jumpTrackedField = c.HasValue && c.Value.jumpValid;
            bool jumpGateOk = active && jumpTrackedField;
            float jumpHandHeight = 0f;
            if (!jumpGateOk) jumpReady = false;
            else
            {
                float lh = c.Value.leftHeight, rh = c.Value.rightHeight;
                jumpHandHeight = Math.Min(lh, rh);
                if (now - jumpAt >= cfg.jump_duration)
                {
                    if (Math.Max(lh, rh) <= cfg.jump_hand_reset) jumpReady = true;
                    else if (jumpReady && jumpHandHeight >= cfg.jump_hand_trigger)
                    {
                        jumpAt = now; jumpCount++; jumpReady = false; lastEvent = "jump";
                    }
                }
            }

            bool clapTrackedField = c.HasValue && c.Value.clapValid;
            float handDistance = clapTrackedField ? c.Value.handDistance : 0f;
            if (!active || !clapTrackedField) grabReady = false;
            else if (handDistance >= cfg.clap_open) grabReady = true;
            else if (handDistance <= cfg.clap_close && grabReady && now - grabAt >= cfg.clap_cooldown)
            {
                grabAt = now; grabCount++; grabReady = false; lastEvent = "grab";
            }

            double phase = (now - jumpAt) / cfg.jump_duration;
            float jumpHeight = (phase >= 0 && phase <= 1) ? Math.Max(0f, 4f * (float)phase * (1f - (float)phase)) : 0f;

            return new MammothState
            {
                protocolVersion = 3,
                sessionId = sessionId,
                mode = "team",
                required = new[] { 0, 1, 2 },
                running = true,
                calibrated = active,
                calibrating = calibrating,
                calibrationProgress = active ? 1f : (firstSampleAt.HasValue ? (float)Math.Min(1.0, (now - firstSampleAt.Value) / cfg.calibration) : 0f),
                visible = new[] { a.HasValue, b.HasValue, c.HasValue },
                speed = (float)Math.Round(speed * 1000f) / 1000f,
                travelSpeed = travelSpeed,
                leftHand = b.HasValue ? b.Value.leftHeight : 0f,
                rightHand = b.HasValue ? b.Value.rightHeight : 0f,
                tilt = b.HasValue ? b.Value.tilt : 0f,
                directionReady = directionReady,
                lane = lane,
                jumpTracked = jumpTrackedField,
                jumpHandHeight = jumpHandHeight,
                jumpReady = jumpReady,
                jumps = jumpCount,
                jumpHeight = jumpHeight,
                clapTracked = clapTrackedField,
                handDistance = handDistance,
                grabReady = grabReady,
                grabs = grabCount,
                grabActive = active && now - grabAt < 0.5,
                events = string.IsNullOrEmpty(lastEvent) ? Array.Empty<string>() : new[] { lastEvent },
                @event = lastEvent,
                updated = now,
            };
        }
    }
}
