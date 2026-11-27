using System.Collections.Generic;
using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// A scored acorn. It is never collected by touching or passing it: only a
    /// valid trunk grab (decided by MammothController) collects it, once.
    /// </summary>
    public class MammothReward : MonoBehaviour
    {
        public static readonly List<MammothReward> Active = new List<MammothReward>();

        [Tooltip("Unique id; each acorn scores at most once per run.")]
        public int id;
        public int points = 10;
        [Tooltip("High acorns need a jump before the grab.")]
        public bool high;
        [Tooltip("Practice acorns (tutorial) never count toward the run score.")]
        public bool practice;
        [Tooltip("Spin speed in degrees per second, for visibility only.")]
        public float spin = 70f;

        /// <summary>Set by the controller each frame when the trunk could reach it.</summary>
        [System.NonSerialized] public bool reachable;

        Vector3 baseScale;
        Vector3 basePos;
        float bobPhase;

        void Awake()
        {
            baseScale = transform.localScale;
            basePos = transform.position;
            bobPhase = id * 0.73f;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public void ResetForRun()
        {
            transform.position = basePos;
            transform.localScale = baseScale;
            gameObject.SetActive(true);
        }

        void Update()
        {
            transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.World);
            float bob = Mathf.Sin(Time.time * 2.2f + bobPhase) * 0.08f;
            transform.position = basePos + new Vector3(0f, bob, 0f);
            // Reach outline from the doc, done as a gentle pulse in size so it
            // needs no extra shader: the acorn swells while the trunk can reach it.
            float pulse = reachable ? 1.25f + Mathf.Sin(Time.time * 10f) * 0.08f : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale * pulse, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        /// <summary>Called by the controller after it decides the grab succeeded.</summary>
        public int Collect()
        {
            reachable = false;
            gameObject.SetActive(false);
            return points;
        }
    }
}
