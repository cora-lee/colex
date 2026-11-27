using UnityEngine;

namespace Mammoth
{
    public enum MammothCameraMode { Follow, Showcase, Celebrate }

    /// <summary>
    /// Fixed rear elevated follow camera from the design doc. It follows the
    /// road height (not the jump), so jumps read clearly. Showcase is the home
    /// screen framing (mammoth and squirrel with the route climbing behind);
    /// Celebrate slowly circles to the front for the campsite ending.
    /// </summary>
    public class MammothFollowCamera : MonoBehaviour
    {
        public Transform target;
        public MammothController controller;
        public MammothCameraMode mode = MammothCameraMode.Follow;
        public Vector3 offset = new Vector3(0f, 4.2f, -8.5f);
        public float lateralFollow = 0.6f;
        public float smoothing = 6f;

        float celebrateAngle;

        void Start()
        {
            if (controller == null && target != null) controller = target.GetComponent<MammothController>();
            Snap();
        }

        public void Snap()
        {
            if (target == null) return;
            Compute(out var pos, out var look);
            transform.position = pos;
            transform.LookAt(look);
        }

        void Compute(out Vector3 pos, out Vector3 look)
        {
            Vector3 p = target.position;
            float ground = controller != null ? controller.GroundY : p.y;
            switch (mode)
            {
                case MammothCameraMode.Showcase:
                    pos = new Vector3(p.x - 5.2f, ground + 2.6f, p.z - 4.6f);
                    look = new Vector3(p.x + 0.6f, ground + 2.4f, p.z + 7f);
                    break;
                case MammothCameraMode.Celebrate:
                    celebrateAngle = Mathf.MoveTowards(celebrateAngle, 150f, Time.deltaTime * 35f);
                    var dir = Quaternion.Euler(0f, celebrateAngle, 0f) * new Vector3(0f, 0f, -7f);
                    pos = new Vector3(p.x, ground + 3f, p.z) + dir;
                    look = new Vector3(p.x, ground + 1.6f, p.z);
                    break;
                default:
                    celebrateAngle = 0f;
                    float camZ = p.z + offset.z;
                    float camGround = Mathf.Max(MammothRoute.Height(camZ), ground - 1.5f);
                    pos = new Vector3(p.x * lateralFollow, camGround + offset.y, camZ);
                    float aheadZ = p.z + 6f;
                    look = new Vector3(p.x * lateralFollow, MammothRoute.Height(aheadZ) + 1f, aheadZ);
                    break;
            }
        }

        /// <summary>Icebergs cut into the road, so the camera can drift into one.
        /// Lift it over the iceberg instead of rendering from inside it.</summary>
        void KeepOutOfIcebergs()
        {
            Vector3 p = transform.position;
            foreach (var o in MammothObstacle.Active)
            {
                Vector3 c = o.transform.position;
                float R = o.transform.lossyScale.x * 1.3f + 0.5f;
                float dx = p.x - c.x, dz = p.z - c.z;
                float flat2 = dx * dx + dz * dz;
                if (flat2 >= R * R) continue;
                float top = c.y + Mathf.Sqrt(R * R - flat2);
                if (p.y < top) p.y = top;
            }
            transform.position = p;
        }

        void LateUpdate()
        {
            if (target == null) return;
            Compute(out var goal, out var look);
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, goal, k);
            KeepOutOfIcebergs();
            var want = Quaternion.LookRotation(look - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
        }
    }
}
