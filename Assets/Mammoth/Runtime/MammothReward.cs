using System.Collections.Generic;
using UnityEngine;

namespace Mammoth
{
    /// <summary>A collectible the mammoth can grab when it is within MammothController.grabRange.</summary>
    public class MammothReward : MonoBehaviour
    {
        public static readonly List<MammothReward> Active = new List<MammothReward>();

        public int points = 1;
        [Tooltip("Spin speed in degrees per second, for visibility only.")]
        public float spin = 90f;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Update() => transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.World);

        /// <summary>Called by the controller after it decides the grab succeeded.</summary>
        public int Collect()
        {
            gameObject.SetActive(false);
            return points;
        }
    }
}
