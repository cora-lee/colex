using System.Collections.Generic;
using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// An obstacle on or beside the road (icebergs). MammothController checks
    /// the mammoth against its footprint: one hit deducts points once, gives a
    /// short slowdown and lets the mammoth pass through, so the route can never
    /// become blocked.
    /// </summary>
    public class MammothObstacle : MonoBehaviour
    {
        public static readonly List<MammothObstacle> Active = new List<MammothObstacle>();

        [Tooltip("Footprint radius on the ground (x/z), Unity units.")]
        public float radius = 1f;

        [Tooltip("Spans the whole road (a fallen log): the only way past is to jump. Uses halfDepth along the road, ignores x.")]
        public bool jumpOver;
        [Tooltip("For jumpOver obstacles: half its thickness along the road.")]
        public float halfDepth = 0.45f;

        [System.NonSerialized] public bool hitThisPass;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);
    }
}
