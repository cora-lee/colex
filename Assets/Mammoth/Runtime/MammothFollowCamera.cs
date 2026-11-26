using UnityEngine;

namespace Mammoth
{
    /// <summary>Simple chase camera: follows target forward and lateral motion, ignores jump height.</summary>
    public class MammothFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 4f, -8f);
        public float lateralFollow = 0.5f;
        public float smoothing = 8f;

        float baseY;

        void Start() { if (target != null) baseY = target.position.y; }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 p = target.position;
            Vector3 goal = new Vector3(p.x * lateralFollow, baseY, p.z) + offset;
            transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.LookAt(new Vector3(p.x * lateralFollow, baseY + 1f, p.z + 6f));
        }
    }
}
