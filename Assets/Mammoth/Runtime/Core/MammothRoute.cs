using System;

namespace Mammoth
{
    public enum MammothSection { Uphill, Summit, Downhill, Campsite }

    /// <summary>
    /// The single mountain route from the design doc: a gentle then steeper
    /// uphill, a short flat summit, a long downhill and a flat campsite at the
    /// end. Distance along the route is world z; height is world y of the road
    /// surface. Pure math so the scene builder and runtime share one profile.
    /// </summary>
    public static class MammothRoute
    {
        public const float StartZ = 0f;
        public const float SummitStartZ = 240f;
        public const float SummitEndZ = 280f;
        public const float CampStartZ = 500f;
        public const float ArchZ = 502f;
        /// <summary>The run ends a little past the arch, inside the campsite by the fire.</summary>
        public const float FinishZ = 512f;
        public const float EndZ = 540f;

        /// <summary>Half width of the walkable road; the mammoth's center stays inside.</summary>
        public const float RoadHalfWidth = 4f;
        public const float SteerLimit = 3.1f;

        public const float GentleRise = 5f;
        public const float SteepRise = 15f;
        public const float DownhillDrop = 18f;

        static float Smooth(float a, float b, float z)
        {
            float t = (z - a) / (b - a);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Road surface height at distance z.</summary>
        public static float Height(float z)
        {
            return GentleRise * Smooth(0f, 120f, z)
                 + SteepRise * Smooth(105f, 240f, z)
                 - DownhillDrop * Smooth(280f, 495f, z);
        }

        public static float Slope(float z) => (Height(z + 0.5f) - Height(z - 0.5f));

        public static MammothSection Section(float z)
        {
            if (z < SummitStartZ) return MammothSection.Uphill;
            if (z < SummitEndZ) return MammothSection.Summit;
            if (z < CampStartZ) return MammothSection.Downhill;
            return MammothSection.Campsite;
        }

        /// <summary>Doc proposal: 0.65 on the steeper uphill, 1 elsewhere (no extra downhill boost).</summary>
        public static float SpeedMultiplier(float z) => (z > 120f && z < 236f) ? 0.65f : 1f;

        /// <summary>Suggested speed band (test-world units/s) shown on the HUD.</summary>
        public static void SuggestedSpeed(float z, out float low, out float high)
        {
            switch (Section(z))
            {
                case MammothSection.Uphill: low = 7f; high = 12f; break;
                case MammothSection.Summit: low = 3f; high = 7f; break;
                case MammothSection.Downhill: low = 3f; high = 8f; break;
                default: low = 0f; high = 4f; break;
            }
        }

        public static string SectionName(MammothSection s)
        {
            switch (s)
            {
                case MammothSection.Uphill: return "Uphill";
                case MammothSection.Summit: return "Summit";
                case MammothSection.Downhill: return "Downhill";
                default: return "Campsite";
            }
        }

        /// <summary>Story/warning line for the current distance, or null. Warnings
        /// appear well before the change so there are at least 2 s of travel to react.</summary>
        public static string Prompt(float z)
        {
            if (z < 22f) return "The old path has changed. Let's take our food over the mountain.";
            if (z > 75f && z < 118f) return "Uphill ahead. Raise your hands to build speed.";
            if (z > 236f && z < 282f) return "Downhill ahead. Slow down first.";
            if (z > 470f && z < CampStartZ) return "The campsite is just ahead.";
            return null;
        }
    }
}
