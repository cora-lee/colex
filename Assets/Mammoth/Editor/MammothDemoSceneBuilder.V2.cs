using System.Collections.Generic;
using Mammoth.Art;
using UnityEditor;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Create Adventure Scene (new file). Same route as the
    /// demo scene, saved to its own scene file, with the higher-quality art:
    /// a natural gradient sky, a rounder mammoth that walks, a squirrel that
    /// steers by pulling locks of the mammoth's hair, and a proper acorn.
    /// </summary>
    public static partial class MammothDemoSceneBuilder
    {
        const string ScenePathV2 = "Assets/Scenes/MammothAdventure.unity";
        static bool V2;
        static string ActiveScenePath => V2 ? ScenePathV2 : ScenePath;

        static readonly Color SkyHaze = new Color(0.84f, 0.91f, 0.97f);
        static readonly Color HairDark = new Color(0.55f, 0.38f, 0.26f);
        static readonly Color FootDark = new Color(0.60f, 0.46f, 0.36f);
        static readonly Color EarInner = new Color(0.96f, 0.74f, 0.72f);
        static readonly Color Blush = new Color(0.97f, 0.68f, 0.66f);
        static readonly Color AcornNutV2 = new Color(0.86f, 0.58f, 0.25f);
        static readonly Color AcornNutLight = new Color(0.95f, 0.76f, 0.42f);
        static readonly Color AcornCapV2 = new Color(0.43f, 0.27f, 0.15f);
        static readonly Color LeafGreen = new Color(0.52f, 0.74f, 0.42f);

        [MenuItem("Mammoth/Create Adventure Scene (new file)")]
        public static void CreateV2()
        {
            V2 = true;
            try { Build(); }
            finally { V2 = false; }
        }

        // ------------------------------------------------------------------
        // Sky
        // ------------------------------------------------------------------
        static void SetupNaturalSky()
        {
            const string path = "Assets/Mammoth/Materials/Mammoth Sky.mat";
            var shader = Shader.Find("Mammoth/GradientSky");
            if (shader == null) { Debug.LogError("[Mammoth] Mammoth/GradientSky shader missing."); return; }
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(shader) { name = "Mammoth Sky" };
                System.IO.Directory.CreateDirectory("Assets/Mammoth/Materials");
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.shader = shader;
            sky.SetColor("_Top", new Color(0.27f, 0.53f, 0.88f));
            sky.SetColor("_Mid", new Color(0.52f, 0.75f, 0.96f));
            sky.SetColor("_Horizon", SkyHaze);
            sky.SetColor("_Ground", SkyHaze);
            sky.SetFloat("_MidHeight", 0.3f);
            sky.SetFloat("_SunGlow", 0.3f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHaze;
            RenderSettings.fogStartDistance = 38f;
            RenderSettings.fogEndDistance = 170f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            // Kept at the old flat-ambient brightness so the snow does not blow out.
            RenderSettings.ambientSkyColor = new Color(0.47f, 0.55f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.55f, 0.68f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.50f, 0.62f);
        }

        static void SetupLightingV2()
        {
            var sun = GameObject.Find("Directional Light");
            if (sun == null) return;
            var light = sun.GetComponent<Light>();
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 0.88f;
            light.shadowStrength = 0.55f;
            RenderSettings.sun = light;
        }

        // ------------------------------------------------------------------
        // Mammoth + squirrel
        // ------------------------------------------------------------------
        static GameObject Pivot(GameObject parent, string name, Vector3 pos)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            return g;
        }

        static GameObject Ball(GameObject parent, string name, Vector3 pos, Vector3 scale, Color c, Vector3? euler = null, PrimitiveType type = PrimitiveType.Sphere)
        {
            var g = Part(parent, type, name, pos, scale, c, flat: false);
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            return g;
        }

        static readonly Color HideGrey = new Color(0.52f, 0.47f, 0.48f);
        static readonly Color HideDark = new Color(0.46f, 0.41f, 0.42f);
        static readonly Color FaceTan = new Color(0.84f, 0.68f, 0.54f);
        static readonly Color Ivory = new Color(0.98f, 0.9f, 0.76f);
        static readonly Color LockDark = new Color(0.34f, 0.28f, 0.30f);
        static readonly Mesh[] polyBalls = new Mesh[3];

        /// <summary>Clean faceted ball with regular facets (size = full extents).
        /// detail 0 is an octahedron, 1 has 32 faces, 2 has 128.</summary>
        static GameObject Poly(GameObject parent, string name, Vector3 pos, Vector3 size, Color c, Vector3? euler = null, int detail = 1)
        {
            if (polyBalls[detail] == null)
            {
                polyBalls[detail] = LowPolyMeshUtil.BuildIceChunk(1, jitter: 0f, subdivisions: detail);
                polyBalls[detail].name = "Poly Ball " + detail;
                SaveMesh(polyBalls[detail], polyBalls[detail].name);
            }
            return MeshPart(parent, name, polyBalls[detail], pos, size * 0.5f, c, euler);
        }

        static GameObject MeshPart(GameObject parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Color c, Vector3? euler = null)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(c);
            return g;
        }

        struct Ring
        {
            public Vector3 c, ax, ay;
            public Ring(Vector3 c, Vector3 ax, Vector3 ay) { this.c = c; this.ax = ax; this.ay = ay; }
        }

        /// <summary>Sculpted low-poly solid: a skin stretched over a row of
        /// cross-sections, flat-shaded so it reads as large clean planes.
        /// power 2 = round section, higher = boxier.</summary>
        static Mesh Loft(string name, Ring[] rings, int sides, float power = 2f)
        {
            var verts = new List<Vector3>();
            foreach (var r in rings)
                for (int k = 0; k < sides; k++)
                {
                    float a = (k + 0.5f) / sides * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), sy = Mathf.Sin(a);
                    float px = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), 2f / power);
                    float py = Mathf.Sign(sy) * Mathf.Pow(Mathf.Abs(sy), 2f / power);
                    verts.Add(r.c + r.ax * px + r.ay * py);
                }
            int start = verts.Count; verts.Add(rings[0].c);
            int end = verts.Count; verts.Add(rings[rings.Length - 1].c);
            var tris = new List<int>();
            for (int i = 0; i < rings.Length - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    int a = i * sides + k, b = (i + 1) * sides + k, c = i * sides + k1, d = (i + 1) * sides + k1;
                    tris.AddRange(new[] { a, b, c, c, b, d });
                }
            int last = (rings.Length - 1) * sides;
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                tris.AddRange(new[] { start, k, k1, end, last + k1, last + k });
            }
            // Make every face point outward whatever way the rings were listed.
            float outward = 0f;
            for (int i = 0; i < (rings.Length - 1) * sides * 6; i += 3)
            {
                Vector3 p0 = verts[tris[i]], p1 = verts[tris[i + 1]], p2 = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                int ring = Mathf.Min(tris[i] / sides, rings.Length - 1);
                outward += Vector3.Dot(n, (p0 + p1 + p2) / 3f - rings[ring].c);
            }
            if (outward < 0f)
                for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var flat = LowPolyMeshUtil.Flatten(mesh);
            flat.name = name;
            SaveMesh(flat, name);
            return flat;
        }

        /// <summary>Tapering faceted tube along a curve (trunk, tusks, tails).</summary>
        static Mesh Tube(string name, Vector3[] pts, float[] radii, int sides)
        {
            var rings = new Ring[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 t = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 bx = (Vector3.right - Vector3.Dot(Vector3.right, t) * t).normalized;
                Vector3 n = Vector3.Cross(t, bx);
                rings[i] = new Ring(pts[i], bx * radii[i], n * radii[i]);
            }
            return Loft(name, rings, sides);
        }

        static Ring Slice(float z, float cy, float halfW, float halfH) => new Ring(new Vector3(0f, cy, z), Vector3.right * halfW, Vector3.up * halfH);
        static Ring Layer(float y, float cz, float halfW, float halfD) => new Ring(new Vector3(0f, y, cz), Vector3.right * halfW, Vector3.forward * halfD);

        /// <summary>An original sculpted low-poly woolly mammoth: tall shoulders,
        /// a back that slopes to the rump, a domed head, a long trunk and big
        /// curling tusks, built from large clean planes. A squirrel rides the
        /// dome and steers by two locks of hair.</summary>
        static GameObject BuildMammothV2(out Transform squirrel, out Transform trunkPivot)
        {
            var mammoth = new GameObject("Mammoth");
            mammoth.transform.position = new Vector3(0f, MammothRoute.Height(0f), 0f);
            var rider = mammoth.AddComponent<MammothRider>();
            var rig = Pivot(mammoth, "Rig", Vector3.zero);
            rider.rig = rig.transform;

            // Body: tall at the shoulders, back sloping down to a lower rump.
            var body = Pivot(rig, "Body", Vector3.zero);
            MeshPart(body, "Torso", Loft("Mammoth Torso", new[]
            {
                Slice(-1.42f, 1.52f, 0.10f, 0.14f),
                Slice(-1.34f, 1.50f, 0.42f, 0.52f),
                Slice(-1.16f, 1.50f, 0.68f, 0.74f),
                Slice(-0.82f, 1.56f, 0.80f, 0.86f),
                Slice(-0.32f, 1.70f, 0.86f, 0.98f),
                Slice(0.16f, 1.86f, 0.88f, 1.12f),
                Slice(0.56f, 1.96f, 0.86f, 1.2f),
                Slice(0.92f, 1.98f, 0.76f, 1.08f),
                Slice(1.16f, 2.0f, 0.56f, 0.82f),
                Slice(1.3f, 2.02f, 0.26f, 0.4f),
            }, 8, 2.5f), Vector3.zero, Vector3.one, HideGrey);
            rider.body = body.transform;

            // Short thin tail with a small tuft.
            var tail = Pivot(body, "Tail", new Vector3(0f, 1.9f, -1.32f));
            MeshPart(tail, "Tail", Tube("Mammoth Tail", new[]
            {
                new Vector3(0f, 0.04f, 0.1f), new Vector3(0f, -0.04f, -0.1f), new Vector3(0f, -0.3f, -0.18f), new Vector3(0f, -0.58f, -0.18f),
            }, new[] { 0.08f, 0.065f, 0.05f, 0.045f }, 5), Vector3.zero, Vector3.one, HideDark);
            Poly(tail, "Tail Tuft", new Vector3(0f, -0.68f, -0.18f), new Vector3(0.16f, 0.26f, 0.14f), HideDark, null, 0);
            rider.tail = tail.transform;

            // Straight, thick column legs with a slightly flared foot and a pale
            // toenail band at the bottom (FL, FR, BL, BR).
            var legMesh = Loft("Mammoth Leg", new[]
            {
                new Ring(new Vector3(0f, -1.05f, 0.02f), Vector3.right * 0.37f, Vector3.forward * 0.38f),
                new Ring(new Vector3(0f, -0.9f, 0.01f), Vector3.right * 0.33f, Vector3.forward * 0.34f),
                new Ring(new Vector3(0f, -0.5f, 0f), Vector3.right * 0.33f, Vector3.forward * 0.34f),
                new Ring(new Vector3(0f, 0.0f, 0f), Vector3.right * 0.4f, Vector3.forward * 0.42f),
                new Ring(new Vector3(0f, 0.7f, 0f), Vector3.right * 0.36f, Vector3.forward * 0.38f),
            }, 8);
            var nailMesh = Loft("Mammoth Toenails", new[]
            {
                new Ring(new Vector3(0f, -1.06f, 0.03f), Vector3.right * 0.395f, Vector3.forward * 0.405f),
                new Ring(new Vector3(0f, -0.96f, 0.025f), Vector3.right * 0.375f, Vector3.forward * 0.385f),
            }, 8);
            var hips = new[] { new Vector3(-0.48f, 1.05f, 0.56f), new Vector3(0.48f, 1.05f, 0.56f), new Vector3(-0.46f, 1.05f, -0.86f), new Vector3(0.46f, 1.05f, -0.86f) };
            for (int i = 0; i < 4; i++)
            {
                var hip = Pivot(rig, "Leg " + i, hips[i]);
                MeshPart(hip, "Leg", legMesh, Vector3.zero, Vector3.one, HideDark);
                MeshPart(hip, "Toenails", nailMesh, Vector3.zero, Vector3.one, FaceTan);
                rider.legs[i] = hip.transform;
            }

            // Head: a big grey wool crest that rises above the shoulders, with a
            // long tan face that narrows straight into the trunk.
            var head = Pivot(rig, "Head Pivot", new Vector3(0f, 2.3f, 1.08f));
            rider.head = head.transform;
            MeshPart(head, "Crest", Loft("Mammoth Crest", new[]
            {
                Layer(-0.3f, -0.12f, 0.5f, 0.56f),
                Layer(0.3f, -0.08f, 0.56f, 0.6f),
                Layer(0.78f, 0.0f, 0.46f, 0.5f),
                Layer(1.02f, 0.04f, 0.3f, 0.32f),
                Layer(1.1f, 0.06f, 0.12f, 0.12f),
            }, 8, 2.4f), Vector3.zero, Vector3.one, HideGrey);
            MeshPart(head, "Face", Loft("Mammoth Face", new[]
            {
                Layer(0.82f, 0.24f, 0.3f, 0.24f),
                Layer(0.6f, 0.34f, 0.5f, 0.36f),
                Layer(0.24f, 0.44f, 0.54f, 0.4f),
                Layer(-0.12f, 0.5f, 0.44f, 0.34f),
                Layer(-0.44f, 0.56f, 0.3f, 0.26f),
                Layer(-0.62f, 0.58f, 0.25f, 0.22f),
            }, 8, 2.4f), Vector3.zero, Vector3.one, FaceTan);
            for (int s = -1; s <= 1; s += 2)
            {
                string sn = s < 0 ? " L" : " R";
                // small tan ear just behind the eye
                var ear = Pivot(head, "Ear" + sn, new Vector3(s * 0.52f, 0.46f, 0.12f));
                Poly(ear, "Ear Flap", new Vector3(s * 0.06f, -0.04f, -0.02f), new Vector3(0.14f, 0.42f, 0.34f), FaceTan, new Vector3(0f, s * -18f, 0f), 1);
                rider.ears[s < 0 ? 0 : 1] = ear.transform;
                // calm closed eye: a short dark slit
                Poly(head, "Eye" + sn, new Vector3(s * 0.43f, 0.5f, 0.6f), new Vector3(0.2f, 0.03f, 0.05f), Dark, new Vector3(0f, s * 38f, s * -8f), 0);

                // Big tusks: from under the face, down and out, then sweeping
                // forward and curling up and inward.
                var tusk = Tube("Mammoth Tusk" + sn, new[]
                {
                    new Vector3(s * 0.26f, -0.4f, 0.66f), new Vector3(s * 0.36f, -0.78f, 0.86f), new Vector3(s * 0.52f, -1.08f, 1.24f),
                    new Vector3(s * 0.7f, -1.06f, 1.72f), new Vector3(s * 0.74f, -0.74f, 2.08f), new Vector3(s * 0.6f, -0.36f, 2.2f),
                    new Vector3(s * 0.42f, -0.16f, 2.08f),
                }, new[] { 0.2f, 0.19f, 0.17f, 0.14f, 0.105f, 0.068f, 0.022f }, 6);
                MeshPart(head, "Tusk" + sn, tusk, Vector3.zero, Vector3.one, Ivory);
            }

            // Trunk carries on from the bottom of the face, same width, same tan.
            var pivot = Pivot(head, "Trunk Pivot", new Vector3(0f, -0.6f, 0.58f));
            var trunkMesh = Tube("Mammoth Trunk", new[]
            {
                new Vector3(0f, 0.04f, 0f), new Vector3(0f, -0.36f, 0.04f), new Vector3(0f, -0.76f, 0.04f),
                new Vector3(0f, -1.12f, 0.02f), new Vector3(0f, -1.36f, 0.1f), new Vector3(0f, -1.46f, 0.28f),
            }, new[] { 0.25f, 0.21f, 0.18f, 0.15f, 0.13f, 0.11f }, 8);
            MeshPart(pivot, "Trunk", trunkMesh, Vector3.zero, Vector3.one, FaceTan);
            trunkPivot = pivot.transform;

            // Squirrel rider: a few clean shapes, big enough to read from behind.
            var sq = Pivot(head, "Squirrel", new Vector3(0f, 1.06f, 0.04f));
            sq.transform.localScale = Vector3.one * 1.5f;
            MeshPart(sq, "Body", Loft("Squirrel Body", new[]
            {
                Layer(0.0f, 0f, 0.07f, 0.07f), Layer(0.06f, 0f, 0.14f, 0.13f), Layer(0.2f, 0.01f, 0.15f, 0.13f),
                Layer(0.31f, 0.02f, 0.1f, 0.09f), Layer(0.36f, 0.03f, 0.05f, 0.05f),
            }, 6), Vector3.zero, Vector3.one, SquirrelFur);
            Poly(sq, "Belly", new Vector3(0f, 0.17f, 0.085f), new Vector3(0.17f, 0.24f, 0.1f), SquirrelBelly, null, 0);
            Poly(sq, "Head", new Vector3(0f, 0.43f, 0.05f), new Vector3(0.26f, 0.23f, 0.25f), SquirrelFur, null, 1);
            Poly(sq, "Muzzle", new Vector3(0f, 0.395f, 0.15f), new Vector3(0.13f, 0.1f, 0.12f), SquirrelBelly, null, 0);
            Poly(sq, "Nose", new Vector3(0f, 0.405f, 0.21f), Vector3.one * 0.04f, Dark, null, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                string sn = s < 0 ? " L" : " R";
                Poly(sq, "Ear" + sn, new Vector3(s * 0.085f, 0.56f, 0.02f), new Vector3(0.09f, 0.17f, 0.06f), SquirrelFur, new Vector3(0f, 0f, -s * 14f), 0);
                Poly(sq, "Eye" + sn, new Vector3(s * 0.085f, 0.455f, 0.14f), Vector3.one * 0.045f, Dark, null, 0);
                Poly(sq, "Foot" + sn, new Vector3(s * 0.1f, 0.03f, 0.11f), new Vector3(0.09f, 0.06f, 0.18f), SquirrelFur, null, 0);
            }
            MeshPart(sq, "Tail", Tube("Squirrel Tail", new[]
            {
                new Vector3(0f, 0.05f, -0.1f), new Vector3(0f, 0.12f, -0.28f), new Vector3(0f, 0.34f, -0.37f),
                new Vector3(0f, 0.58f, -0.31f), new Vector3(0f, 0.72f, -0.16f), new Vector3(0f, 0.74f, -0.04f),
            }, new[] { 0.045f, 0.1f, 0.14f, 0.13f, 0.08f, 0.02f }, 6), Vector3.zero, Vector3.one, SquirrelTail);
            rider.bag = Poly(sq, "Food Bag", new Vector3(0.17f, 0.1f, -0.06f), new Vector3(0.15f, 0.18f, 0.15f), BagColor, null, 1).transform;
            squirrel = sq.transform;
            rider.squirrel = squirrel;
            rider.restHand = new Vector3(0.133f, 0.1f, 0.2f);
            rider.pullHand = new Vector3(0.27f, 0.4f, -0.02f);
            rider.trophyPoint = new Vector3(0f, 5.6f, 1.0f);

            // Hair reins: a lock on each side of the dome, held in each paw.
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                string sn = s < 0 ? " L" : " R";
                rider.shoulders[i] = Pivot(sq, "Shoulder" + sn, new Vector3(s * 0.12f, 0.28f, 0.05f)).transform;
                rider.hairRoots[i] = Pivot(head, "Hair Root" + sn, new Vector3(s * 0.22f, 0.88f, 0.34f)).transform;
                Poly(head, "Hair Root Tuft" + sn, new Vector3(s * 0.22f, 0.9f, 0.34f), new Vector3(0.16f, 0.2f, 0.16f), LockDark, null, 0);
                rider.locks[i] = Poly(head, "Hair Lock" + sn, Vector3.zero, Vector3.one * 0.08f, LockDark, null, 0).transform;
                rider.arms[i] = Poly(head, "Squirrel Arm" + sn, Vector3.zero, Vector3.one * 0.07f, SquirrelFur, null, 0).transform;
                var hand = Poly(head, "Squirrel Paw" + sn, Vector3.zero, Vector3.one * 0.14f, SquirrelFur, null, 1);
                Poly(hand, "Hair End", new Vector3(0f, 1.3f, 0f), new Vector3(1.2f, 2.4f, 1.2f), LockDark, null, 0);
                rider.hands[i] = hand.transform;
            }
            return mammoth;
        }

        // ------------------------------------------------------------------
        // Acorn
        // ------------------------------------------------------------------
        static Mesh acornNutMesh, acornCapMesh;

        static GameObject BuildAcornV2(Transform parent, string name, Vector3 pos, bool air)
        {
            if (acornNutMesh == null)
            {
                acornNutMesh = Lathe("Acorn Nut V2", new[]
                {
                    new Vector2(0f, -0.70f), new Vector2(0.035f, -0.665f), new Vector2(0.07f, -0.62f), new Vector2(0.15f, -0.55f),
                    new Vector2(0.24f, -0.44f), new Vector2(0.31f, -0.29f), new Vector2(0.345f, -0.12f),
                    new Vector2(0.35f, 0.02f), new Vector2(0.33f, 0.12f), new Vector2(0f, 0.14f),
                }, 28);
                SaveMesh(acornNutMesh, acornNutMesh.name);
                // Faceted cap: reads as the scaly cup against the smooth, shiny nut.
                var cap = Lathe("Acorn Cap V2", new[]
                {
                    new Vector2(0f, 0.06f), new Vector2(0.33f, 0.05f), new Vector2(0.385f, 0.02f), new Vector2(0.41f, 0.07f),
                    new Vector2(0.40f, 0.15f), new Vector2(0.355f, 0.25f), new Vector2(0.27f, 0.34f),
                    new Vector2(0.15f, 0.40f), new Vector2(0f, 0.42f),
                }, 14);
                acornCapMesh = LowPolyMeshUtil.Flatten(cap);
                acornCapMesh.name = "Acorn Cap V2";
                SaveMesh(acornCapMesh, acornCapMesh.name);
            }

            var r = new GameObject(name);
            r.transform.SetParent(parent);
            r.transform.position = pos;
            r.transform.localScale = Vector3.one * (air ? 0.78f : 0.74f);
            var art = Pivot(r, "Acorn Art", new Vector3(0f, 0.1f, 0f));
            art.transform.localRotation = Quaternion.Euler(0f, 0f, 12f); // jaunty tilt

            var nut = new GameObject("Acorn Nut");
            nut.transform.SetParent(art.transform, false);
            nut.AddComponent<MeshFilter>().sharedMesh = acornNutMesh;
            nut.AddComponent<MeshRenderer>().sharedMaterial = MakeGlossMaterial(AcornNutV2, 0.62f);
            // soft lighter cheek so the nut does not read as one flat brown
            Ball(art, "Acorn Shine", new Vector3(-0.13f, -0.16f, 0.2f), new Vector3(0.2f, 0.34f, 0.14f), AcornNutLight, new Vector3(0f, -30f, 10f))
                .GetComponent<Renderer>().sharedMaterial = MakeGlossMaterial(AcornNutLight, 0.62f);

            var capGo = new GameObject("Acorn Cap");
            capGo.transform.SetParent(art.transform, false);
            capGo.AddComponent<MeshFilter>().sharedMesh = acornCapMesh;
            capGo.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(AcornCapV2);

            Ball(art, "Acorn Stem", new Vector3(0.02f, 0.47f, 0f), new Vector3(0.06f, 0.08f, 0.06f), AcornCapV2, new Vector3(0f, 0f, -16f), PrimitiveType.Capsule);
            Ball(art, "Acorn Stem Tip", new Vector3(0.045f, 0.545f, 0f), Vector3.one * 0.075f, AcornCapV2);
            Ball(art, "Acorn Leaf", new Vector3(0.2f, 0.47f, 0f), new Vector3(0.34f, 0.035f, 0.17f), LeafGreen, new Vector3(0f, 0f, 22f));
            return r;
        }

        /// <summary>Surface of revolution from a (radius, height) profile listed
        /// bottom to top. Smooth-shaded, with clean poles.</summary>
        static Mesh Lathe(string name, Vector2[] prof, int segs)
        {
            var verts = new List<Vector3>();
            foreach (var p in prof)
                for (int k = 0; k < segs; k++)
                {
                    float a = k / (float)segs * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x));
                }
            var tris = new List<int>();
            for (int j = 0; j < prof.Length - 1; j++)
                for (int k = 0; k < segs; k++)
                {
                    int k1 = (k + 1) % segs;
                    int a = j * segs + k, b = (j + 1) * segs + k, c = j * segs + k1, d = (j + 1) * segs + k1;
                    if (prof[j].x > 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
                    if (prof[j + 1].x > 0f) { tris.Add(c); tris.Add(b); tris.Add(d); }
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            for (int j = 0; j < prof.Length; j++)
            {
                if (prof[j].x > 0f) continue;
                Vector3 n = j == 0 ? Vector3.down : Vector3.up;
                for (int k = 0; k < segs; k++) normals[j * segs + k] = n;
            }
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly Dictionary<string, Material> GlossMaterials = new Dictionary<string, Material>();

        static Material MakeGlossMaterial(Color c, float smoothness)
        {
            string name = "Mammoth Gloss " + ColorUtility.ToHtmlStringRGB(c);
            if (GlossMaterials.TryGetValue(name, out var m) && m != null) return m;
            string path = "Assets/Mammoth/Materials/" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader) { name = name, color = c };
                System.IO.Directory.CreateDirectory("Assets/Mammoth/Materials");
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(m);
            GlossMaterials[name] = m;
            return m;
        }
    }
}
