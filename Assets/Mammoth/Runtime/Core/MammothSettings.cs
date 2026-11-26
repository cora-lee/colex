using System;
using System.Collections.Generic;

namespace Mammoth
{
    /// <summary>
    /// Mirrors settings.json from the mapping handoff. Field names must stay identical
    /// to the JSON keys so JsonUtility can fill them. Values are test-world units.
    /// </summary>
    [Serializable]
    public class MammothSettings
    {
        public float confidence = 0.55f;
        public float margin = 0.025f;
        public float calibration = 3f;
        public float speed_low = -0.8f;
        public float speed_high = 1f;
        public float smooth = 0.3f;
        public float min_speed = 2f;
        public float max_speed = 12f;
        public float hand_trigger = 0.15f;
        public float hand_reset = -0.15f;
        public float lane_width = 2f;
        public float lane_time = 0.25f;
        public float jump_hand_trigger = 0.8f;
        public float jump_hand_reset = -0.15f;
        public float clap_close = 0.35f;
        public float clap_open = 0.9f;
        public float clap_cooldown = 0.35f;
        public float jump_duration = 0.9f;
        public float jump_height = 1.5f;

        // Ranges copied from settings.py SCHEMA (minimum, maximum).
        static readonly (string key, float min, float max)[] Ranges =
        {
            ("confidence", .1f, .99f), ("margin", 0f, .12f), ("calibration", 1f, 15f),
            ("speed_low", -3f, 3f), ("speed_high", -3f, 3f), ("smooth", .02f, 3f),
            ("min_speed", 0f, 30f), ("max_speed", 0f, 60f),
            ("hand_trigger", -.5f, 2f), ("hand_reset", -2f, 1f),
            ("lane_width", .5f, 5f), ("lane_time", .05f, 2f),
            ("jump_hand_trigger", .2f, 2f), ("jump_hand_reset", -2f, .5f),
            ("clap_close", .1f, 1.2f), ("clap_open", .3f, 2f), ("clap_cooldown", .1f, 2f),
            ("jump_duration", .2f, 3f), ("jump_height", .1f, 5f),
        };

        public float Get(string key)
        {
            switch (key)
            {
                case "confidence": return confidence;
                case "margin": return margin;
                case "calibration": return calibration;
                case "speed_low": return speed_low;
                case "speed_high": return speed_high;
                case "smooth": return smooth;
                case "min_speed": return min_speed;
                case "max_speed": return max_speed;
                case "hand_trigger": return hand_trigger;
                case "hand_reset": return hand_reset;
                case "lane_width": return lane_width;
                case "lane_time": return lane_time;
                case "jump_hand_trigger": return jump_hand_trigger;
                case "jump_hand_reset": return jump_hand_reset;
                case "clap_close": return clap_close;
                case "clap_open": return clap_open;
                case "clap_cooldown": return clap_cooldown;
                case "jump_duration": return jump_duration;
                case "jump_height": return jump_height;
                default: throw new ArgumentException("Unknown setting " + key);
            }
        }

        /// <summary>Same checks as settings.validate(). Returns an empty list when valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (var r in Ranges)
            {
                float v = Get(r.key);
                if (float.IsNaN(v) || float.IsInfinity(v) || v < r.min - 1e-6f || v > r.max + 1e-6f)
                    errors.Add(r.key + " must be a finite number between " + r.min + " and " + r.max + ".");
            }
            Pair(errors, "speed_low", "speed_high");
            Pair(errors, "min_speed", "max_speed");
            Pair(errors, "hand_reset", "hand_trigger");
            Pair(errors, "jump_hand_reset", "jump_hand_trigger");
            Pair(errors, "clap_close", "clap_open");
            return errors;
        }

        void Pair(List<string> errors, string low, string high)
        {
            if (Get(low) >= Get(high)) errors.Add(low + " must be smaller than " + high + ".");
        }
    }
}
