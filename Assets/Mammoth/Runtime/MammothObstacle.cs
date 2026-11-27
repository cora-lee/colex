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

        [System.NonSerialized] public bool hitThisPass;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);
    }
}
