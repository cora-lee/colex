using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Brings the mammoth and its squirrel rider to life. The squirrel holds
    /// two locks of the mammoth's head hair like reins: steering left yanks
    /// the left lock up and back, the head follows the pull, and the same on
    /// the right. Also walks the legs with the actual speed, tucks them in a
    /// jump, flaps the ears and wags the tail. Purely visual.
    /// </summary>
    public class MammothRider : MonoBehaviour
    {
        public MammothController controller;
        public Transform squirrel;
        public Transform head;

        [Header("Hair reins (left, right)")]
        public Transform[] shoulders = new Transform[2];
        public Transform[] hairRoots = new Transform[2];
        public Transform[] hands = new Transform[2];
        public Transform[] arms = new Transform[2];
        public Transform[] locks = new Transform[2];
        [Tooltip("Hand position in squirrel space (right side; mirrored for the left).")]
        public Vector3 restHand = new Vector3(0.125f, 0.09f, 0.175f);
        public Vector3 pullHand = new Vector3(0.26f, 0.38f, -0.04f);
        public float armWidth = 0.09f;
        public float lockWidth = 0.1f;
        public float headYaw = 17f;
        public float headRoll = 9f;

        [Header("Body")]
        public Transform[] legs = new Transform[4]; // FL, FR, BL, BR pivots
        public Transform[] ears = new Transform[2];
        public Transform tail;
        public Transform body;
        [Tooltip("Everything that flops down when the mammoth sits after a bump.")]
        public Transform rig;
        [Tooltip("Where a caught acorn is held up, in mammoth space, so it shows from behind.")]
        public Vector3 trophyPoint = new Vector3(0f, 4.6f, 0.9f);
        public Transform bag;
        public float strideRate = 0.95f;
        public float strideAngle = 26f;

        [Tooltip("Editor preview only: forces the steering pose. NaN = follow the controller.")]
        [System.NonSerialized] public float debugSteer = float.NaN;

        float steer, phase, speedBlend;
        Quaternion headBase;
        Quaternion[] earBase = new Quaternion[2];
        Quaternion tailBase;
        Vector3 bodyBase;

        void Awake()
        {
            if (head != null) headBase = head.localRotation;
            for (int i = 0; i < 2; i++) if (ears[i] != null) earBase[i] = ears[i].localRotation;
            if (tail != null) tailBase = tail.localRotation;
            if (body != null) bodyBase = body.localPosition;
        }

        void Start()
        {
            if (controller != null) controller.OnCollected += ShowCatch;
        }

        void OnDestroy()
        {
            if (controller != null) controller.OnCollected -= ShowCatch;
        }

        Transform trophy;
        Vector3 trophyFrom, trophyScale;
        float trophyStart = -10f;
        float sit;

        /// <summary>Holds the caught acorn high above the head for a beat, then
        /// drops it into the squirrel's bag, so a catch is obvious from behind.</summary>
        void ShowCatch(MammothReward r)
        {
            if (r == null) return;
            if (trophy != null) Destroy(trophy.gameObject);
            var copy = Instantiate(r.gameObject, r.transform.position, r.transform.rotation, transform);
            var reward = copy.GetComponent<MammothReward>();
            if (reward != null) DestroyImmediate(reward);
            copy.name = "Caught Acorn";
            copy.SetActive(true);
            trophy = copy.transform;
            trophyFrom = trophy.localPosition;
            trophyScale = Vector3.one * 0.74f;
            trophyStart = Time.time;
        }

        /// <summary>Editor preview only: play the catch with the nearest acorn ahead.</summary>
        public void DebugCatch()
        {
            MammothReward best = null;
            foreach (var r in MammothReward.Active)
                if (r.transform.position.z > transform.position.z && (best == null || r.transform.position.z < best.transform.position.z)) best = r;
            ShowCatch(best);
        }

        void UpdateTrophy()
        {
            if (trophy == null) return;
            float t = Time.time - trophyStart;
            const float rise = 0.22f, hold = 0.6f, drop = 0.28f;
            Vector3 bagPos = bag != null ? transform.InverseTransformPoint(bag.position) : trophyPoint;
            if (t < rise)
            {
                float f = t / rise;
                float e = 1f - (1f - f) * (1f - f);
                trophy.localPosition = Vector3.Lerp(trophyFrom, trophyPoint, e);
                trophy.localScale = trophyScale * Mathf.Lerp(1f, 1.9f, e);
            }
            else if (t < rise + hold)
            {
                float f = (t - rise) / hold;
                trophy.localPosition = trophyPoint + Vector3.up * (Mathf.Sin(f * Mathf.PI) * 0.18f);
                trophy.localScale = trophyScale * (1.9f + Mathf.Sin(f * Mathf.PI * 3f) * 0.12f);
            }
            else if (t < rise + hold + drop)
            {
                float f = (t - rise - hold) / drop;
                trophy.localPosition = Vector3.Lerp(trophyPoint, bagPos, f * f);
                trophy.localScale = trophyScale * Mathf.Lerp(1.9f, 0.15f, f);
            }
            else
            {
                Destroy(trophy.gameObject);
                trophy = null;
                return;
            }
            trophy.Rotate(0f, 540f * Time.deltaTime, 0f, Space.World);
        }

        void LateUpdate()
        {
            UpdateTrophy();
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float target = controller != null ? Mathf.Clamp(controller.Steer, -1f, 1f) : 0f;
            if (!float.IsNaN(debugSteer)) target = debugSteer;
            steer = Mathf.Lerp(steer, target, 1f - Mathf.Exp(-12f * dt));

            if (head != null)
                head.localRotation = headBase * Quaternion.Euler(0f, steer * headYaw, -steer * headRoll);

            // little tugs while a lock is being pulled, so it reads as yanking
            float tug = Mathf.Sin(Time.time * 16f) * 0.5f + 0.5f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                float pull = Mathf.Clamp01(steer * side);
                float slack = Mathf.Clamp01(-steer * side);
                float t = Mathf.Clamp01(pull * (0.86f + 0.14f * tug));
                Vector3 local = Vector3.LerpUnclamped(restHand, pullHand, t);
                local.z += slack * 0.05f;      // the other paw gives slack
                local.y -= slack * 0.03f;
                local.x *= side;
                if (squirrel == null || hands[i] == null) continue;
                Vector3 hand = squirrel.TransformPoint(local);
                hands[i].position = hand;
                if (shoulders[i] != null && arms[i] != null) Stretch(arms[i], shoulders[i].position, hand, armWidth);
                if (hairRoots[i] != null && locks[i] != null)
                    Stretch(locks[i], hairRoots[i].position, hand, lockWidth * Mathf.Lerp(1f, 0.62f, pull));
            }

            // walk cycle
            float speed = controller != null ? controller.ActualSpeed : 0f;
            bool air = controller != null && controller.Airborne;
            speedBlend = Mathf.Lerp(speedBlend, Mathf.Clamp01(speed / 4f), 1f - Mathf.Exp(-6f * dt));
            phase += speed * strideRate * dt;
            for (int i = 0; i < legs.Length; i++)
            {
                if (legs[i] == null) continue;
                bool diagonalA = i == 0 || i == 3;
                float a = Mathf.Sin(phase + (diagonalA ? 0f : Mathf.PI)) * strideAngle * speedBlend;
                if (air) a = i < 2 ? -38f : 34f; // legs stretched out mid-jump
                if (sit > 0.01f) a = Mathf.Lerp(a, i < 2 ? -62f : -84f, sit); // legs out in front, sitting
                legs[i].localRotation = Quaternion.Slerp(legs[i].localRotation, Quaternion.Euler(a, 0f, 0f), 1f - Mathf.Exp(-18f * dt));
            }
            float sitTarget = controller != null ? controller.SitAmount : 0f;
            sit = Mathf.Lerp(sit, sitTarget, 1f - Mathf.Exp(-16f * dt));
            if (rig != null)
            {
                float shake = sit > 0.5f ? Mathf.Sin(Time.time * 26f) * 1.5f : 0f; // dazed wobble
                rig.localPosition = new Vector3(0f, -0.5f * sit, -0.1f * sit);
                rig.localRotation = Quaternion.Euler(-15f * sit, 0f, shake * sit);
            }
            if (body != null)
                body.localPosition = bodyBase + Vector3.up * (Mathf.Abs(Mathf.Sin(phase)) * 0.045f * speedBlend);

            for (int i = 0; i < 2; i++)
            {
                if (ears[i] == null) continue;
                float side = i == 0 ? -1f : 1f;
                float flap = Mathf.Sin(Time.time * 2.3f + i) * 5f + Mathf.Sin(phase * 2f) * 6f * speedBlend + (air ? 22f : 0f);
                ears[i].localRotation = earBase[i] * Quaternion.Euler(0f, side * flap, 0f);
            }
            if (tail != null)
                tail.localRotation = tailBase * Quaternion.Euler(Mathf.Sin(phase * 2f) * 6f * speedBlend, 0f, Mathf.Sin(Time.time * 3.1f) * 14f);
        }

        /// <summary>Lays a unit sphere between two world points as a soft limb.</summary>
        static void Stretch(Transform t, Vector3 a, Vector3 b, float width)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return;
            t.position = (a + b) * 0.5f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            // limb meshes have radius 1, so half the size gives the wanted extents
            t.localScale = new Vector3(width, len + width * 0.6f, width) * 0.5f;
        }
    }
}
