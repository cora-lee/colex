using System;

namespace Mammoth
{
    /// <summary>
    /// Lane transition and jump arc math from UNITY_HANDOFF.md, in test-world units.
    /// Multiply results by the agreed world-to-Unity scale exactly once.
    /// </summary>
    public class MammothMotion
    {
        int lane;
        float laneFrom, laneTo;
        double laneStart = double.NegativeInfinity;
        double jumpAt = double.NegativeInfinity;

        public int Lane => lane;

        static float Smoothstep(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Lateral offset for the given lane, eased with smoothstep over lane_time.</summary>
        public float Lateral(int targetLane, double now, MammothSettings cfg)
        {
            float current = LateralAt(now, cfg);
            if (targetLane != lane)
            {
                laneFrom = current;
                laneTo = targetLane * cfg.lane_width;
                laneStart = now;
                lane = targetLane;
            }
            return LateralAt(now, cfg);
        }

        float LateralAt(double now, MammothSettings cfg)
        {
            if (double.IsNegativeInfinity(laneStart)) return lane * cfg.lane_width;
            float t = (float)((now - laneStart) / cfg.lane_time);
            return laneFrom + (laneTo - laneFrom) * Smoothstep(t);
        }

        public void StartJump(double now) => jumpAt = now;

        public bool Airborne(double now, MammothSettings cfg)
        {
            double phase = (now - jumpAt) / cfg.jump_duration;
            return phase >= 0 && phase <= 1;
        }

        /// <summary>4 * phase * (1 - phase) * jump_height while airborne, else 0.</summary>
        public float JumpOffset(double now, MammothSettings cfg)
        {
            double phase = (now - jumpAt) / cfg.jump_duration;
            if (phase < 0 || phase > 1) return 0f;
            return (float)(4 * phase * (1 - phase)) * cfg.jump_height;
        }
    }
}
