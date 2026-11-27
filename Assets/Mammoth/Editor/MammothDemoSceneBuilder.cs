using System.Collections.Generic;
using System.Linq;
using Mammoth.Art;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Create Demo Scene. Builds "The Last Bag of Acorns" route
    /// from the design doc as one continuous mountain: a snowy uphill, a short
    /// flat summit, a downhill where the snow thins into grass, and a campsite.
    /// Side bands are kept strictly apart so nothing overlaps: road (|x| &lt; 4),
    /// road-edge markers, icebergs/rocks (5 to 10.2), stacked-sphere pastel
    /// trees (11.2 to 21), mountains (24+). Acorns sit on one smooth line on the
    /// road, so nothing hides them and they can be reached on a natural path.
    /// </summary>
    public static partial class MammothDemoSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MammothDemo.unity";
        const float SnowEndZ = 330f;

        static readonly Color SkyHorizon = new Color(0.80f, 0.89f, 0.98f);
        static readonly Color SkyTop = new Color(0.60f, 0.76f, 0.96f);
        static readonly Color SnowGround = new Color(0.70f, 0.77f, 0.89f);
        static readonly Color GrassGround = new Color(0.70f, 0.86f, 0.72f);
        static readonly Color RoadSnow = new Color(0.60f, 0.74f, 0.92f);
        static readonly Color RoadEarth = new Color(0.86f, 0.74f, 0.58f);
        static readonly Color SnowWhite = new Color(0.88f, 0.91f, 0.97f);
        static readonly Color IceSlick = new Color(0.80f, 0.97f, 1f);
        static readonly Color MudColor = new Color(0.42f, 0.32f, 0.22f);
        static readonly Color Ice2 = new Color(0.50f, 0.70f, 0.90f);
        static readonly Color Ice3 = new Color(0.40f, 0.62f, 0.86f);
        static readonly Color RockLilac = new Color(0.74f, 0.70f, 0.82f);
        static readonly Color Pebble = new Color(0.88f, 0.86f, 0.90f);
        static readonly Color MammothFur = new Color(0.90f, 0.78f, 0.62f);
        static readonly Color MammothFurShade = new Color(0.78f, 0.63f, 0.47f);
        static readonly Color TuskColor = new Color(0.98f, 0.96f, 0.9f);
        static readonly Color SquirrelFur = new Color(0.86f, 0.54f, 0.30f);
        static readonly Color SquirrelTail = new Color(0.94f, 0.66f, 0.40f);
        static readonly Color SquirrelBelly = new Color(0.99f, 0.90f, 0.78f);
        static readonly Color BagColor = new Color(0.86f, 0.76f, 0.60f);
        static readonly Color Dark = new Color(0.22f, 0.16f, 0.18f);
        static readonly Color MountainPastel1 = new Color(0.82f, 0.66f, 0.74f);
        static readonly Color MountainPastel2 = new Color(0.86f, 0.74f, 0.70f);
        static readonly Color MountainPastel3 = new Color(0.74f, 0.72f, 0.88f);
        static readonly Color SnowCream = new Color(0.92f, 0.90f, 0.92f);
        static readonly Color CloudWhite = new Color(0.99f, 0.99f, 1f);
        static readonly Color TrunkPale = new Color(0.95f, 0.92f, 0.90f);
        static readonly Color Wood = new Color(0.62f, 0.44f, 0.30f);
        static readonly Color SignBoard = new Color(0.98f, 0.92f, 0.80f);

        // Stacked-sphere tree palettes, like the reference art: soft pastel
        // pom-pom towers. Frosty tones on the snowy uphill, spring tones once
        // the snow thins on the downhill.
        static readonly Color[] FrostTrees =
        {
            new Color(0.72f, 0.84f, 0.98f), new Color(0.82f, 0.79f, 0.97f),
            new Color(0.75f, 0.92f, 0.93f), new Color(0.97f, 0.86f, 0.93f),
        };
        static readonly Color[] SpringTrees =
        {
            new Color(0.66f, 0.88f, 0.76f), new Color(0.97f, 0.76f, 0.84f),
            new Color(0.99f, 0.90f, 0.72f), new Color(0.82f, 0.79f, 0.97f),
            new Color(0.72f, 0.84f, 0.98f),
        };

        static readonly Color AcornNutGround = new Color(0.90f, 0.70f, 0.35f);
        static readonly Color AcornCapGround = new Color(0.46f, 0.30f, 0.16f);
        static readonly Color AcornNutAir = new Color(0.80f, 0.92f, 0.98f);
        static readonly Color AcornCapAir = new Color(0.40f, 0.62f, 0.80f);

        static readonly List<Vector2> placedTrees = new List<Vector2>();

        [MenuItem("Mammoth/Create Demo Scene")]
        public static void Create()
        {
            V2 = false;
            Build();
        }

        static void Build()
        {
            Materials.Clear(); SlickMaterials.Clear(); GlossMaterials.Clear(); FlatPrimitive.Clear();
            acornNutMesh = null; acornCapMesh = null; polyBalls[0] = polyBalls[1] = polyBalls[2] = null;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cfg = MammothSettingsAsset.Load(true);

            SetupSkyAndFog();
            BuildGround();
            BuildRoad();
            BuildIcebergsAndRocks();
            BuildMountains();
            BuildTrees();
            BuildClouds();
            BuildLandmarks();
            BuildCampsite();

            Transform squirrel, trunkPivot;
            var mammoth = V2 ? BuildMammothV2(out squirrel, out trunkPivot) : BuildMammoth(out squirrel, out trunkPivot);
            var grabPoint = new GameObject("Grab Origin").transform;
            grabPoint.SetParent(mammoth.transform, false);
            grabPoint.localPosition = new Vector3(0, 1.1f, 1.6f);

            // Systems
            var systems = new GameObject("Mammoth Systems");
            var receiver = systems.AddComponent<MammothStateReceiver>();
            var poseEngine = systems.AddComponent<MammothPoseEngine>();
            poseEngine.modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Mammoth/Models/yolov8n-pose.onnx");
            if (poseEngine.modelAsset == null)
                Debug.LogError("[Mammoth] yolov8n-pose.onnx not found at Assets/Mammoth/Models/ - in-Unity camera mode will not run.");
            receiver.poseEngine = poseEngine;
            receiver.mode = MammothSourceMode.InUnityCamera;
            // Doc: do not start the camera on the home screen. The game flow
            // enables these when the operator starts camera setup.
            receiver.enabled = false;
            poseEngine.enabled = false;

            var controller = mammoth.AddComponent<MammothController>();
            controller.receiver = receiver;
            controller.grabOrigin = grabPoint;
            controller.squirrel = squirrel;
            controller.trunk = trunkPivot;
            controller.terrain = BuildTerrainZones();
            var riderComp = mammoth.GetComponent<MammothRider>();
            if (riderComp != null) { riderComp.controller = controller; controller.trunkRaiseAngle = 150f; }
            var hud = systems.AddComponent<MammothDebugHud>();
            hud.receiver = receiver;
            hud.controller = controller;
            hud.show = false;
            var camPreview = systems.AddComponent<MammothCameraPreview>();
            camPreview.receiver = receiver;
            camPreview.show = false;

            SetupLighting();
            if (V2) SetupLightingV2();

            MammothFollowCamera follow = null;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(-5f, 3f, -5f);
                cam.farClipPlane = 400f;
                cam.fieldOfView = 58f;
                follow = cam.gameObject.AddComponent<MammothFollowCamera>();
                follow.target = mammoth.transform;
                follow.controller = controller;
                follow.mode = MammothCameraMode.Showcase;
                cam.backgroundColor = SkyHorizon;
                cam.clearFlags = V2 ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            }

            BuildRewards(cfg, out var practiceTemplate);

            var flow = systems.AddComponent<MammothGameFlow>();
            flow.receiver = receiver;
            flow.poseEngine = poseEngine;
            flow.controller = controller;
            flow.followCamera = follow;
            flow.cameraPreview = camPreview;
            flow.debugHud = hud;
            flow.practiceAcornTemplate = practiceTemplate;

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ActiveScenePath);
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == ActiveScenePath);
            list.Insert(0, new EditorBuildSettingsScene(ActiveScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Selection.activeGameObject = mammoth;
            Debug.Log("[Mammoth] Scene saved to " + ActiveScenePath + ". Press Play: the home screen opens first.");
        }

        // =====================================================================
        // Terrain
        // =====================================================================

        /// <summary>Valley floor beside the road rises gently away from it.</summary>
        static float Valley(float ax) => ax < 6f ? 0f : 0.045f * Mathf.Pow(ax - 6f, 1.35f);

        static float GroundY(float x, float z) => MammothRoute.Height(z) + Valley(Mathf.Abs(x));

        static float Noise(int xi, int zi)
        {
            unchecked
            {
                int h = xi * 374761393 + zi * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f * 2f - 1f;
            }
        }

        static readonly float[] GroundXs =
        {
            -110f, -84f, -66f, -54f, -45f, -38f, -32f, -27f, -22.5f, -18.5f, -15f, -12f, -9.5f, -7.2f, -5.2f, -4.2f,
            4.2f, 5.2f, 7.2f, 9.5f, 12f, 15f, 18.5f, 22.5f, 27f, 32f, 38f, 45f, 54f, 66f, 84f, 110f,
        };

        static void BuildGround()
        {
            var group = new GameObject("Ground");
            BuildGroundStrip(group, "Snow Ground", -40f, SnowEndZ, SnowGround);
            BuildGroundStrip(group, "Grass Ground", SnowEndZ, 580f, GrassGround);

            // Snow thins into grass: grass tufts on the last snow, then
            // lingering snow patches on the first grass, instead of a hard line.
            var rng = new System.Random(19);
            for (int i = 0; i < 90; i++)
            {
                bool grassOnSnow = i < 40;
                float z = grassOnSnow ? Mathf.Lerp(270f, SnowEndZ, (float)rng.NextDouble()) : Mathf.Lerp(SnowEndZ, 430f, Mathf.Pow((float)rng.NextDouble(), 1.6f));
                int side = rng.NextDouble() < 0.5 ? -1 : 1;
                float x = side * Mathf.Lerp(4.8f, 40f, (float)rng.NextDouble());
                float s = Mathf.Lerp(1.2f, 3.4f, (float)rng.NextDouble());
                var patch = new GameObject(grassOnSnow ? "Grass Patch" : "Snow Patch");
                patch.transform.SetParent(group.transform);
                patch.transform.position = new Vector3(x, GroundY(x, z) + 0.06f, z);
                patch.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                patch.transform.localScale = new Vector3(s, 0.08f, s * Mathf.Lerp(0.6f, 1f, (float)rng.NextDouble()));
                patch.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 13 + 4, jitter: 0.2f, subdivisions: 1);
                patch.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(grassOnSnow ? GrassGround : SnowGround);
            }
        }

        static void BuildGroundStrip(GameObject parent, string name, float z0, float z1, Color color)
        {
            const float dz = 3f;
            int rows = Mathf.RoundToInt((z1 - z0) / dz) + 1;
            int cols = GroundXs.Length;
            var verts = new Vector3[rows * cols];
            for (int r = 0; r < rows; r++)
            {
                float z = z0 + r * dz;
                int zi = Mathf.RoundToInt(z / dz);
                for (int c = 0; c < cols; c++)
                {
                    float x = GroundXs[c];
                    float jitter = Mathf.Abs(x) > 5f ? Noise(c, zi) * Mathf.Min(0.45f, 0.03f * Mathf.Abs(x)) : 0f;
                    verts[r * cols + c] = new Vector3(x, GroundY(x, z) + jitter - 0.02f, z);
                }
            }
            var tris = new List<int>();
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    tris.AddRange(new[] { a, d, b, b, d, e });
                }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            var flat = LowPolyMeshUtil.Flatten(mesh);
            SaveMesh(flat, name);

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<MeshFilter>().sharedMesh = flat;
            go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);
        }

        static void BuildRoad()
        {
            var group = new GameObject("Road");
            BuildRoadStrip(group, "Road Snow", -40f, SnowEndZ, RoadSnow);
            BuildRoadStrip(group, "Road Earth", SnowEndZ, 580f, RoadEarth);
        }

        static void BuildRoadStrip(GameObject parent, string name, float z0, float z1, Color color)
        {
            const float dz = 2f;
            float[] xs = { -MammothRoute.RoadHalfWidth, -1.3f, 1.3f, MammothRoute.RoadHalfWidth };
            int rows = Mathf.RoundToInt((z1 - z0) / dz) + 1;
            var verts = new Vector3[rows * xs.Length];
            for (int r = 0; r < rows; r++)
            {
                float z = z0 + r * dz;
                for (int c = 0; c < xs.Length; c++)
                    verts[r * xs.Length + c] = new Vector3(xs[c], MammothRoute.Height(z) + 0.04f, z);
            }
            var tris = new List<int>();
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < xs.Length - 1; c++)
                {
                    int a = r * xs.Length + c, b = a + 1, d = a + xs.Length, e = d + 1;
                    tris.AddRange(new[] { a, d, b, b, d, e });
                }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh, name);
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);
        }

        /// <summary>Slick-ice (sideways slide) and mud (slowdown) patches on the
        /// road, alternating like before. None on the summit or at camp.</summary>
        static readonly (float z, MammothTerrainType type)[] TerrainPatches =
        {
            (50f, MammothTerrainType.Ice), (95f, MammothTerrainType.Mud),
            (140f, MammothTerrainType.Ice), (185f, MammothTerrainType.Mud),
            (300f, MammothTerrainType.Ice), (345f, MammothTerrainType.Mud),
            (390f, MammothTerrainType.Ice), (435f, MammothTerrainType.Mud),
        };

        static MammothTerrainSegment[] BuildTerrainZones()
        {
            var group = new GameObject("Terrain Zones");
            var segs = new List<MammothTerrainSegment>();
            const float len = 20f;
            int seed = 0;
            foreach (var t in TerrainPatches)
            {
                bool ice = t.type == MammothTerrainType.Ice;
                segs.Add(new MammothTerrainSegment { startZ = t.z, endZ = t.z + len, type = t.type });
                Color road = t.z < SnowEndZ ? RoadSnow : RoadEarth;
                Color patchColor = ice ? IceSlick : MudColor;
                var patchMat = ice ? MakeSlickMaterial(IceSlick) : MakeMaterial(MudColor);
                float cz = t.z + len * 0.5f;
                // Soft rim first (slightly bigger, blended), then the patch on top.
                BlobPatch(group, "Terrain Rim", cz, len * 0.5f + 1.2f, 4.6f, 0.065f, seed, MakeMaterial(Color.Lerp(road, patchColor, 0.45f)));
                BlobPatch(group, ice ? "Ice Slick" : "Mud Patch", cz, len * 0.5f, 4.3f, 0.07f, seed, patchMat);
                // A few small splats spilling out past the front and back edges.
                var rng = new System.Random(900 + seed);
                for (int i = 0; i < 4; i++)
                {
                    float sz = (i % 2 == 0 ? t.z - 1.5f : t.z + len + 1.5f) + (float)(rng.NextDouble() - 0.5) * 3f;
                    float sx = (float)(rng.NextDouble() * 2 - 1) * 2.8f;
                    float r = 0.6f + (float)rng.NextDouble() * 0.9f;
                    BlobPatch(group, "Terrain Splat", sz, r, r * 1.3f, 0.068f, seed * 7 + i + 1, patchMat, sx);
                }
                seed++;
            }
            return segs.ToArray();
        }

        /// <summary>An organic puddle shape on the road: wavy outline from a few
        /// sine harmonics, built as rings so it follows the slope. Clipped to
        /// the road width.</summary>
        static void BlobPatch(GameObject parent, string name, float cz, float rz, float rx, float lift, int seed, Material mat, float cx = 0f)
        {
            const int segs = 72, rings = 6;
            var rng = new System.Random(seed * 31 + 5);
            var ph = new float[4];
            for (int i = 0; i < ph.Length; i++) ph[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
            float half = MammothRoute.RoadHalfWidth - 0.05f;
            var verts = new List<Vector3> { new Vector3(cx, MammothRoute.Height(cz) + lift, cz) };
            for (int r = 1; r <= rings; r++)
            {
                float f = r / (float)rings;
                for (int k = 0; k < segs; k++)
                {
                    float a = k / (float)segs * Mathf.PI * 2f;
                    float wob = 1f + 0.10f * Mathf.Sin(2f * a + ph[0]) + 0.07f * Mathf.Sin(3f * a + ph[1])
                                   + 0.05f * Mathf.Sin(5f * a + ph[2]) + 0.03f * Mathf.Sin(8f * a + ph[3]);
                    float x = Mathf.Clamp(cx + Mathf.Cos(a) * rx * wob * f, -half, half);
                    float z = cz + Mathf.Sin(a) * rz * wob * f;
                    verts.Add(new Vector3(x, MammothRoute.Height(z) + lift, z));
                }
            }
            var tris = new List<int>();
            for (int k = 0; k < segs; k++)
                tris.AddRange(new[] { 0, 1 + (k + 1) % segs, 1 + k });
            for (int r = 1; r < rings; r++)
            {
                int i0 = 1 + (r - 1) * segs, i1 = 1 + r * segs;
                for (int k = 0; k < segs; k++)
                {
                    int k1 = (k + 1) % segs;
                    tris.AddRange(new[] { i0 + k, i0 + k1, i1 + k, i0 + k1, i1 + k1, i1 + k });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                for (int i = 0; i < tris.Count; i += 3) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A flat strip across the road width that follows the slope.</summary>
        static void RoadPatch(GameObject parent, string name, float z0, float z1, float lift, Material mat)
        {
            float half = MammothRoute.RoadHalfWidth - 0.05f;
            int rows = Mathf.Max(2, Mathf.CeilToInt((z1 - z0) / 1f) + 1);
            var verts = new Vector3[rows * 2];
            for (int r = 0; r < rows; r++)
            {
                float z = Mathf.Lerp(z0, z1, r / (float)(rows - 1));
                float y = MammothRoute.Height(z) + lift;
                verts[r * 2] = new Vector3(-half, y, z);
                verts[r * 2 + 1] = new Vector3(half, y, z);
            }
            var tris = new List<int>();
            for (int r = 0; r < rows - 1; r++)
            {
                int a = r * 2, b = a + 1, d = a + 2, e = a + 3;
                tris.AddRange(new[] { a, d, b, b, d, e });
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static readonly Dictionary<Color, Material> SlickMaterials = new Dictionary<Color, Material>();

        static Material MakeSlickMaterial(Color c)
        {
            if (SlickMaterials.TryGetValue(c, out var m) && m != null) return m;
            string name = "Mammoth Slick " + ColorUtility.ToHtmlStringRGB(c);
            string path = "Assets/Mammoth/Materials/" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader) { name = name, color = c };
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.85f);
                System.IO.Directory.CreateDirectory("Assets/Mammoth/Materials");
                AssetDatabase.CreateAsset(m, path);
            }
            SlickMaterials[c] = m;
            return m;
        }

        /// <summary>Soft snow mounds (pebbles on grass) lining both road edges, so
        /// the route reads clearly without hard painted lines.</summary>
        static void BuildRoadEdges()
        {
            var group = new GameObject("Road Edges");
            var rng = new System.Random(41);
            int i = 0;
            for (float z = -36f; z < 575f; z += 2.4f)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    bool snow = z < SnowEndZ;
                    float x = side * (MammothRoute.RoadHalfWidth + 0.35f + (float)rng.NextDouble() * 0.25f);
                    float zz = z + (float)rng.NextDouble() * 0.8f;
                    float s = Mathf.Lerp(0.32f, 0.55f, (float)rng.NextDouble());
                    var go = new GameObject(snow ? "Snow Mound" : "Pebble");
                    go.transform.SetParent(group.transform);
                    go.transform.position = new Vector3(x, MammothRoute.Height(zz) + s * 0.2f, zz);
                    go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.localScale = new Vector3(s * 1.3f, s * 0.7f, s);
                    go.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(i++ * 7 + 3, jitter: 0.18f, subdivisions: 1);
                    go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(snow ? SnowWhite : Pebble);
                }
            }
        }

        /// <summary>Icebergs on the snowy part, lilac rocks once it turns to
        /// grass, always fully off the road (inner edge at x ≥ 5) and inside a
        /// band that ends before the tree band starts.</summary>
        static readonly List<Vector3> placedIcebergs = new List<Vector3>();   // x, z, radius
        static readonly List<Vector3> placedMountains = new List<Vector3>();  // x, z, base radius

        /// <summary>Acorn line x at distance z (piecewise linear between acorns).</summary>
        static float PathX(float z)
        {
            if (z <= AcornPath[0].z) return 0f;
            for (int i = 1; i < AcornPath.Length; i++)
                if (z <= AcornPath[i].z)
                {
                    var a = AcornPath[i - 1]; var b = AcornPath[i];
                    return Mathf.Lerp(a.x, b.x, (z - a.z) / (b.z - a.z));
                }
            return 0f;
        }

        /// <summary>True when an iceberg at (x, z) with footprint r neither sits
        /// on an acorn nor stands just in front of one (where it would hide it
        /// from the follow camera or box it in). Between acorns icebergs may cut
        /// into the road freely, like the original layout.</summary>
        static bool KeepsAcornsClear(float x, float z, float r)
        {
            foreach (var a in AcornPath)
            {
                // High acorns need a clear run-up for the jump, so keep a longer
                // and wider stretch in front of them free.
                float ahead = a.high ? 16f : 10f;
                float margin = a.high ? 2.4f : 1.8f;
                if (z + r < a.z - ahead || z - r > a.z + 3f) continue;
                if (Mathf.Abs(x - a.x) - r < margin) return false;
            }
            return true;
        }

        /// <summary>Doc: the route must never force an unavoidable collision. Keep
        /// at least a 2.4-wide gap on the road wherever icebergs face each other.</summary>
        static bool LeavesGap(float x, float z, float r)
        {
            float inner = Mathf.Abs(x) - r;           // distance from road center to its inner edge
            if (MammothRoute.RoadHalfWidth + inner < 2.4f) return false;
            foreach (var p in placedIcebergs)
            {
                if (Mathf.Sign(p.x) == Mathf.Sign(x)) continue;
                if (Mathf.Abs(p.y - z) > p.z + r + 1.5f) continue;
                float otherInner = Mathf.Abs(p.x) - p.z;
                if (Mathf.Min(inner, MammothRoute.RoadHalfWidth) + Mathf.Min(otherInner, MammothRoute.RoadHalfWidth) < 2.4f) return false;
            }
            return true;
        }

        /// <summary>Icebergs like the original: mixed sizes, close to the road on
        /// both sides and cutting into it as obstacles to steer around. Acorns
        /// stay unblocked and there is always a way through. None on the summit
        /// (doc: no penalty obstacles there).</summary>
        static void BuildIcebergsAndRocks()
        {
            var group = new GameObject("Icebergs");
            placedIcebergs.Clear();
            var rng = new System.Random(11);
            int made = 0;
            for (int i = 0; i < 600 && made < 46; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float z = Mathf.Lerp(14f, MammothRoute.CampStartZ - 8f, (float)rng.NextDouble()); // start area stays open for the home view
                if (z > MammothRoute.SummitStartZ - 6f && z < MammothRoute.SummitEndZ + 6f) continue;
                float scale = Mathf.Lerp(1.4f, 4.2f, (float)rng.NextDouble());
                if (z > SnowEndZ) scale *= 0.85f; // retreating ice downhill
                float x = side * (3.8f + (float)rng.NextDouble() * 3.2f);
                float r = scale * 0.85f;
                // Clearance uses the iceberg's visible size (the jittered mesh
                // reaches ~1.3x its scale), not its smaller collision footprint.
                if (!KeepsAcornsClear(x, z, scale * 1.3f) || !LeavesGap(x, z, r)) continue;
                bool crowded = false;
                foreach (var p in placedIcebergs)
                    if (new Vector2(p.x - x, p.y - z).magnitude < (p.z + r) * 0.6f) { crowded = true; break; }
                if (crowded) continue;

                var go = new GameObject("Iceberg");
                go.transform.SetParent(group.transform);
                go.transform.position = new Vector3(x, GroundY(x, z) + scale * 0.35f - 0.4f, z);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(made * 97 + 5, jitter: 0.32f, subdivisions: 1);
                go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(Color.Lerp(Ice3, SnowWhite, (float)rng.NextDouble() * 0.6f));
                if (Mathf.Abs(x) - r < MammothRoute.RoadHalfWidth) go.AddComponent<MammothObstacle>().radius = r;
                placedIcebergs.Add(new Vector3(x, z, r));
                made++;
            }
            Debug.Log("[Mammoth] Icebergs placed: " + made);

            var pink = new Color(0.98f, 0.82f, 0.86f);
            for (int k = 0; k < 8; k++)
            {
                int side = k % 2 == 0 ? -1 : 1;
                float z = 40f + (float)rng.NextDouble() * 440f;
                float x = side * (4.8f + (float)rng.NextDouble() * 2f);
                float sc = Mathf.Lerp(0.5f, 1.1f, (float)rng.NextDouble());
                if (!KeepsAcornsClear(x, z, sc * 1.3f)) continue;
                var go = new GameObject("Accent Crystal");
                go.transform.SetParent(group.transform);
                go.transform.position = new Vector3(x, GroundY(x, z) + 0.6f, z);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = Vector3.one * sc;
                go.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(k * 53 + 2, jitter: 0.22f, subdivisions: 0);
                go.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(pink);
                placedIcebergs.Add(new Vector3(x, z, sc));
            }
        }

        static readonly float[] MountainTints = { 0f, 0.33f, 0.66f, 1f };

        static void BuildMountains()
        {
            var field = new GameObject("Mountains");
            int seed = 0;
            placedMountains.Clear();
            foreach (int side in new[] { -1, 1 })
            {
                BuildMountainRow(field, side, 18f, 6f, step: 11f, heightMin: 8f, heightMax: 13f, baseMin: 4.5f, baseMax: 6.5f, ref seed);
                BuildMountainRow(field, side, 28f, 8f, step: 15f, heightMin: 13f, heightMax: 19f, baseMin: 7f, baseMax: 10f, ref seed);
            }
        }

        static void BuildMountainRow(GameObject field, int side, float rowDistance, float xJitter, float step,
            float heightMin, float heightMax, float baseMin, float baseMax, ref int seed)
        {
            var rng = new System.Random(seed * 97 + 29);
            for (float z = -30f; z < 600f; z += step)
            {
                float x = side * (rowDistance + ((float)rng.NextDouble() - 0.5f) * 2f * xJitter);
                float zPos = z + ((float)rng.NextDouble() - 0.5f) * step * 0.5f;
                float heightScale = Mathf.Lerp(heightMin, heightMax, (float)rng.NextDouble());
                float baseScale = Mathf.Lerp(baseMin, baseMax, (float)rng.NextDouble());
                // Keep the campsite (tents, fire) clear of mountain bases.
                if (zPos > 495f && zPos < 545f && Mathf.Abs(x) - baseScale < 11.5f)
                    x = side * (11.5f + baseScale);

                var go = new GameObject("Mountain");
                go.transform.SetParent(field.transform);
                go.transform.position = new Vector3(x, GroundY(x, zPos) - 1.2f, zPos);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = new Vector3(baseScale, heightScale, baseScale);
                placedMountains.Add(new Vector3(x, zPos, baseScale));

                int peaks = rng.NextDouble() < 0.12 ? 3 : 2;
                go.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildMountainRidge(seed * 71 + 13, peaks: peaks, baseJitter: 0.05f, snowLine: 0.55f);
                var a = Color.Lerp(MountainPastel1, MountainPastel2, MountainTints[rng.Next(MountainTints.Length)]);
                var rock = Color.Lerp(a, MountainPastel3, MountainTints[rng.Next(MountainTints.Length)] * 0.6f);
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { MakeMaterial(rock), MakeMaterial(SnowCream) };
                seed++;
            }
        }

        // =====================================================================
        // Trees: stacked smooth spheres on thin pale trunks (reference art)
        // =====================================================================

        static void BuildTrees()
        {
            var group = new GameObject("Trees");
            placedTrees.Clear();
            var rng = new System.Random(211);
            foreach (int side in new[] { -1, 1 })
            {
                float z = -24f + (float)rng.NextDouble() * 6f;
                while (z < 575f)
                {
                    bool spring = z > SnowEndZ - 20f;
                    // Clusters of varied heights, a little denser on the green downhill.
                    int count = rng.Next(1, spring ? 4 : 3);
                    float cx = side * Mathf.Lerp(8.5f, 11.5f, (float)rng.NextDouble());
                    for (int t = 0; t < count; t++)
                    {
                        for (int attempt = 0; attempt < 8; attempt++)
                        {
                            float x = cx + ((float)rng.NextDouble() - 0.5f) * 1.2f;
                            float zz = z + ((float)rng.NextDouble() - 0.5f) * 5.2f;
                            float ax = Mathf.Abs(x);
                            if (ax < 7.5f || ax > 12.5f) continue;
                            if (!ClearOfScenery(x, zz)) continue;
                            if (zz > MammothRoute.SummitStartZ && zz < MammothRoute.SummitEndZ && rng.NextDouble() < 0.6) break;
                            if (!FarFromTrees(x, zz, 1.7f)) continue;
                            var palette = (zz > SnowEndZ - 20f + ((float)rng.NextDouble() - 0.5f) * 40f) ? SpringTrees : FrostTrees;
                            BuildTree(group.transform, new Vector3(x, GroundY(x, zz) - 0.15f, zz),
                                Mathf.Lerp(0.85f, 1.45f, (float)rng.NextDouble()), palette[rng.Next(palette.Length)], rng);
                            placedTrees.Add(new Vector2(x, zz));
                            break;
                        }
                    }
                    z += Mathf.Lerp(22f, 34f, (float)rng.NextDouble());
                }
            }
        }

        static bool ClearOfScenery(float x, float z)
        {
            foreach (var p in placedIcebergs)
                if (new Vector2(p.x - x, p.y - z).magnitude < p.z + 0.9f) return false;
            foreach (var m in placedMountains)
                if (new Vector2(m.x - x, m.y - z).magnitude < m.z * 1.05f + 0.6f) return false;
            return true;
        }

        static bool FarFromTrees(float x, float z, float minDist)
        {
            float d2 = minDist * minDist;
            foreach (var p in placedTrees)
                if ((p.x - x) * (p.x - x) + (p.y - z) * (p.y - z) < d2) return false;
            return true;
        }

        static void BuildTree(Transform parent, Vector3 basePos, float size, Color color, System.Random rng)
        {
            var tree = new GameObject("Tree");
            tree.transform.SetParent(parent, false);
            tree.transform.position = basePos;
            tree.transform.localScale = Vector3.one * size;

            float trunkH = Mathf.Lerp(0.55f, 0.9f, (float)rng.NextDouble());
            Part(tree, PrimitiveType.Cylinder, "Trunk", new Vector3(0, trunkH * 0.5f + 0.1f, 0), new Vector3(0.09f, trunkH * 0.5f + 0.1f, 0.09f), TrunkPale, flat: false);

            int balls = rng.Next(3, 6);
            float r = Mathf.Lerp(0.42f, 0.52f, (float)rng.NextDouble());
            float y = trunkH + r * 0.75f;
            for (int b = 0; b < balls; b++)
            {
                // Each ball a touch lighter than the one below, for a soft gradient.
                var c = Color.Lerp(color, Color.white, b * 0.05f);
                float wobble = ((float)rng.NextDouble() - 0.5f) * 0.05f;
                Part(tree, PrimitiveType.Sphere, "Ball", new Vector3(wobble, y, -wobble), Vector3.one * (r * 2f), c, flat: false);
                float next = r * Mathf.Lerp(0.82f, 0.9f, (float)rng.NextDouble());
                y += (r + next) * 0.72f;
                r = next;
            }
        }

        static void BuildClouds()
        {
            var group = new GameObject("Clouds");
            var rng = new System.Random(733);
            for (int i = 0; i < 26; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * Mathf.Lerp(8f, 26f, (float)rng.NextDouble());
                float z = (float)rng.NextDouble() * 580f;
                float y = GroundY(x, z) + Mathf.Lerp(9f, 16f, (float)rng.NextDouble());
                float scale = Mathf.Lerp(1.8f, 3.6f, (float)rng.NextDouble());

                var cloud = new GameObject("Cloud");
                cloud.transform.SetParent(group.transform, false);
                cloud.transform.position = new Vector3(x, y, z);
                cloud.transform.localScale = Vector3.one * scale;

                int puffs = rng.Next(3, 5);
                for (int p = 0; p < puffs; p++)
                {
                    var puff = new GameObject("Puff");
                    puff.transform.SetParent(cloud.transform, false);
                    float px = (p - (puffs - 1) / 2f) * 0.5f;
                    float big = p == puffs / 2 ? 1.25f : 1f;
                    puff.transform.localPosition = new Vector3(px, (big - 1f) * 0.3f, 0);
                    puff.transform.localScale = new Vector3(0.75f, 0.62f, 0.7f) * big * Mathf.Lerp(0.85f, 1.1f, (float)rng.NextDouble());
                    puff.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 41 + p * 7 + 1, jitter: 0.04f, subdivisions: 3, flatShaded: false);
                    puff.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(CloudWhite);
                }
            }
        }

        // =====================================================================
        // Landmarks: summit flags and signs, finish arch, campsite
        // =====================================================================

        static void BuildLandmarks()
        {
            var group = new GameObject("Landmarks");
            // No roadside text signs: the on-screen prompts carry the route story.

            // Prayer-flag style pennants strung along the flat summit.
            Color[] flagCols = { new Color(0.97f, 0.62f, 0.62f), new Color(0.99f, 0.85f, 0.5f), new Color(0.6f, 0.85f, 0.7f), new Color(0.6f, 0.75f, 0.97f), new Color(0.8f, 0.7f, 0.95f) };
            int f = 0;
            for (float z = MammothRoute.SummitStartZ + 2f; z < MammothRoute.SummitEndZ - 2f; z += 4f)
                foreach (int side in new[] { -1, 1 })
                {
                    float x = side * 5.2f;
                    var pole = Part(group, PrimitiveType.Cylinder, "Flag Pole", new Vector3(x, MammothRoute.Height(z) + 1.1f, z), new Vector3(0.06f, 1.1f, 0.06f), Wood, flat: false);
                    var flag = Part(group, PrimitiveType.Cube, "Flag", new Vector3(x - side * 0.32f, MammothRoute.Height(z) + 1.95f, z), new Vector3(0.6f, 0.36f, 0.03f), flagCols[f++ % flagCols.Length], flat: true);
                    flag.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                }

            // Finish arch at the campsite entrance.
            float fz = MammothRoute.ArchZ;
            float fy = MammothRoute.Height(fz);
            Part(group, PrimitiveType.Cylinder, "Arch Post L", new Vector3(-4.7f, fy + 2.1f, fz), new Vector3(0.22f, 2.1f, 0.22f), Wood, flat: false);
            Part(group, PrimitiveType.Cylinder, "Arch Post R", new Vector3(4.7f, fy + 2.1f, fz), new Vector3(0.22f, 2.1f, 0.22f), Wood, flat: false);
            Part(group, PrimitiveType.Cube, "Arch Banner", new Vector3(0f, fy + 3.85f, fz), new Vector3(9.8f, 0.9f, 0.14f), SignBoard, flat: true);
            Text(group.transform, "Campsite", new Vector3(0f, fy + 3.85f, fz - 0.09f), 0.22f, 64);
            // Readable from inside the camp too (the ending camera looks back at it).
            var back = Text(group.transform, "Welcome", new Vector3(0f, fy + 3.85f, fz + 0.09f), 0.22f, 64);
            back.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            for (int i = 0; i < 7; i++)
            {
                float x = -4.2f + i * 1.4f;
                var flag = Part(group, PrimitiveType.Cube, "Bunting", new Vector3(x, fy + 3.2f, fz - 0.05f), new Vector3(0.5f, 0.42f, 0.03f), flagCols[i % flagCols.Length], flat: true);
                flag.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
        }

        static void Sign(Transform parent, Vector3 pos, string text)
        {
            float y = GroundY(pos.x, pos.z);
            Part(parent.gameObject, PrimitiveType.Cylinder, "Sign Post", new Vector3(pos.x, y + 0.8f, pos.z), new Vector3(0.1f, 0.8f, 0.1f), Wood, flat: false);
            Part(parent.gameObject, PrimitiveType.Cube, "Sign Board", new Vector3(pos.x, y + 1.6f, pos.z), new Vector3(1.9f, 0.9f, 0.1f), SignBoard, flat: true);
            Text(parent, text, new Vector3(pos.x, y + 1.6f, pos.z - 0.07f), 0.09f, 64);
        }

        static GameObject Text(Transform parent, string text, Vector3 pos, float charSize, int fontSize)
        {
            var go = new GameObject("Text " + text.Replace('\n', ' '));
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = charSize;
            tm.fontSize = fontSize;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.42f, 0.28f, 0.2f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return go;
        }

        static void BuildCampsite()
        {
            var group = new GameObject("Campsite");
            var tentCols = new[] { new Color(0.97f, 0.64f, 0.58f), new Color(0.99f, 0.84f, 0.52f), new Color(0.62f, 0.78f, 0.98f) };
            var tents = new[] { new Vector3(-8f, 0f, 514f), new Vector3(7.5f, 0f, 520f), new Vector3(-9.5f, 0f, 529f) };
            for (int i = 0; i < tents.Length; i++)
            {
                var p = tents[i];
                var tent = new GameObject("Tent");
                tent.transform.SetParent(group.transform);
                tent.transform.position = new Vector3(p.x, GroundY(p.x, p.z) - 0.05f, p.z);
                tent.transform.rotation = Quaternion.Euler(0f, 45f + i * 20f, 0f);
                tent.transform.localScale = new Vector3(2.2f, 2.3f, 2.2f);
                tent.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildMountainPeak(500 + i, baseJitter: 0f, sides: 4, tiers: 0);
                tent.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(tentCols[i]);
            }

            // Campfire beside the road with log seats.
            var fire = new Vector3(3.2f, MammothRoute.Height(518f), 518f);
            for (int i = 0; i < 3; i++)
            {
                var log = Part(group, PrimitiveType.Cylinder, "Fire Log", fire + new Vector3(0f, 0.12f, 0f), new Vector3(0.14f, 0.45f, 0.14f), Wood, flat: true);
                log.transform.localRotation = Quaternion.Euler(80f, i * 60f, 0f);
            }
            var flame = new GameObject("Flame");
            flame.transform.SetParent(group.transform);
            flame.transform.position = fire + new Vector3(0f, 0.45f, 0f);
            flame.transform.localScale = new Vector3(0.38f, 0.6f, 0.38f);
            flame.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(77, jitter: 0.15f, subdivisions: 1);
            flame.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.62f, 0.32f));
            var core = new GameObject("Flame Core");
            core.transform.SetParent(flame.transform, false);
            core.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            core.transform.localScale = Vector3.one * 0.62f;
            core.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(78, jitter: 0.12f, subdivisions: 1);
            core.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.88f, 0.45f));
            foreach (var off in new[] { new Vector3(1.4f, 0f, 0.4f), new Vector3(-0.4f, 0f, 1.5f) })
            {
                var seat = Part(group, PrimitiveType.Cylinder, "Log Seat", fire + off + new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.5f, 0.3f), Wood, flat: true);
                seat.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // A couple of static neighbours waiting at camp.
            Bunny(group.transform, new Vector3(6.2f, 0f, 512f), 200f);
            Bunny(group.transform, new Vector3(-6f, 0f, 522f), 150f);

            // Extra spring trees framing the campsite.
            var rng = new System.Random(905);
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * Mathf.Lerp(10.5f, 12.5f, (float)rng.NextDouble());
                float z = Mathf.Lerp(506f, 560f, (float)rng.NextDouble());
                if (!FarFromTrees(x, z, 1.8f) || !ClearOfScenery(x, z)) continue;
                BuildTree(group.transform, new Vector3(x, GroundY(x, z) - 0.15f, z), Mathf.Lerp(1f, 1.5f, (float)rng.NextDouble()), SpringTrees[rng.Next(SpringTrees.Length)], rng);
                placedTrees.Add(new Vector2(x, z));
            }
        }

        static void Bunny(Transform parent, Vector3 p, float yaw)
        {
            var b = new GameObject("Bunny");
            b.transform.SetParent(parent);
            b.transform.position = new Vector3(p.x, GroundY(p.x, p.z), p.z);
            b.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var white = new Color(0.98f, 0.97f, 0.96f);
            Part(b, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.45f, 0.6f), white);
            Part(b, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.62f, 0.22f), Vector3.one * 0.34f, white);
            Part(b, PrimitiveType.Capsule, "Ear L", new Vector3(-0.07f, 0.9f, 0.2f), new Vector3(0.08f, 0.16f, 0.06f), new Color(0.99f, 0.86f, 0.9f));
            Part(b, PrimitiveType.Capsule, "Ear R", new Vector3(0.07f, 0.9f, 0.2f), new Vector3(0.08f, 0.16f, 0.06f), new Color(0.99f, 0.86f, 0.9f));
            Part(b, PrimitiveType.Sphere, "Tail", new Vector3(0f, 0.32f, -0.3f), Vector3.one * 0.16f, white);
        }

        // =====================================================================
        // Sky, light, mammoth + squirrel, acorns
        // =====================================================================

        static void SetupSkyAndFog()
        {
            if (V2) { SetupNaturalSky(); return; }
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.50f, 0.56f, 0.70f);
        }

        static void SetupLighting()
        {
            var sun = GameObject.Find("Directional Light");
            if (sun == null)
            {
                sun = new GameObject("Directional Light");
                sun.AddComponent<Light>().type = LightType.Directional;
            }
            var light = sun.GetComponent<Light>();
            light.color = new Color(1f, 0.94f, 0.86f);
            light.intensity = 0.9f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        }

        static GameObject BuildMammoth(out Transform squirrel, out Transform trunkPivot)
        {
            var mammoth = new GameObject("Mammoth");
            mammoth.transform.position = new Vector3(0f, MammothRoute.Height(0f), 0f);

            var body = Part(mammoth, PrimitiveType.Capsule, "Body", new Vector3(0, 1.1f, 0), new Vector3(1.3f, .9f, 1.3f), MammothFur, flat: true);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Part(mammoth, PrimitiveType.Sphere, "Head", new Vector3(0, 1.6f, 1.05f), Vector3.one * .9f, MammothFur, flat: true);

            var pivot = new GameObject("Trunk Pivot");
            pivot.transform.SetParent(mammoth.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.42f, 1.42f);
            var trunk = Part(pivot, PrimitiveType.Cylinder, "Trunk", new Vector3(0f, -0.38f, 0.1f), new Vector3(.22f, .45f, .22f), MammothFurShade, flat: true);
            trunk.transform.localRotation = Quaternion.Euler(20, 0, 0);
            trunkPivot = pivot.transform;

            Part(mammoth, PrimitiveType.Cylinder, "Tusk L", new Vector3(-.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), TuskColor, flat: true).transform.localRotation = Quaternion.Euler(60, 0, 0);
            Part(mammoth, PrimitiveType.Cylinder, "Tusk R", new Vector3(.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), TuskColor, flat: true).transform.localRotation = Quaternion.Euler(60, 0, 0);
            foreach (var p in new[] { new Vector3(-.4f, .35f, .5f), new Vector3(.4f, .35f, .5f), new Vector3(-.4f, .35f, -.5f), new Vector3(.4f, .35f, -.5f) })
                Part(mammoth, PrimitiveType.Cylinder, "Leg", p, new Vector3(.32f, .35f, .32f), MammothFurShade, flat: true);

            // The squirrel rides on the head holding the (empty) food bag; it
            // leans with steering and wobbles for feedback.
            var sq = new GameObject("Squirrel");
            sq.transform.SetParent(mammoth.transform, false);
            sq.transform.localPosition = new Vector3(0f, 1.98f, 0.98f);
            Part(sq, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.17f, 0f), new Vector3(0.28f, 0.32f, 0.26f), SquirrelFur, flat: false);
            Part(sq, PrimitiveType.Sphere, "Belly", new Vector3(0f, 0.15f, 0.07f), new Vector3(0.18f, 0.22f, 0.14f), SquirrelBelly, flat: false);
            Part(sq, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.39f, 0.04f), Vector3.one * 0.22f, SquirrelFur, flat: false);
            Part(sq, PrimitiveType.Sphere, "Ear L", new Vector3(-0.07f, 0.5f, 0.02f), new Vector3(0.06f, 0.09f, 0.04f), SquirrelFur, flat: false);
            Part(sq, PrimitiveType.Sphere, "Ear R", new Vector3(0.07f, 0.5f, 0.02f), new Vector3(0.06f, 0.09f, 0.04f), SquirrelFur, flat: false);
            Part(sq, PrimitiveType.Sphere, "Eye L", new Vector3(-0.05f, 0.41f, 0.14f), Vector3.one * 0.035f, Dark, flat: false);
            Part(sq, PrimitiveType.Sphere, "Eye R", new Vector3(0.05f, 0.41f, 0.14f), Vector3.one * 0.035f, Dark, flat: false);
            Part(sq, PrimitiveType.Sphere, "Tail 1", new Vector3(0f, 0.2f, -0.17f), Vector3.one * 0.2f, SquirrelTail, flat: false);
            Part(sq, PrimitiveType.Sphere, "Tail 2", new Vector3(0f, 0.38f, -0.25f), Vector3.one * 0.24f, SquirrelTail, flat: false);
            Part(sq, PrimitiveType.Sphere, "Tail 3", new Vector3(0f, 0.56f, -0.2f), Vector3.one * 0.22f, SquirrelTail, flat: false);
            Part(sq, PrimitiveType.Sphere, "Food Bag", new Vector3(0.19f, 0.1f, 0.02f), new Vector3(0.16f, 0.18f, 0.16f), BagColor, flat: true);
            squirrel = sq.transform;

            return mammoth;
        }

        /// <summary>
        /// Doc layout: 20 scored acorns worth 10 each. Uphill 7 low + 3 high,
        /// none on the summit, downhill 7 low + 3 high. They follow one smooth
        /// weaving line on the road (no sharp jumps between neighbours), high
        /// ones only on gentle ground, an easy centered one near the finish.
        /// </summary>
        static readonly (float z, float x, bool high)[] AcornPath =
        {
            (26f, 0f, false), (42f, -1.4f, false), (58f, -2.4f, false),
            (74f, -1.0f, true), (90f, 1.0f, true), (104f, 2.4f, true),
            (134f, 1.6f, false), (162f, 0f, false), (190f, -1.6f, false), (218f, -0.6f, false),
            (300f, 0.8f, false), (318f, 2.2f, false), (336f, 1.2f, false),
            (356f, -0.6f, true), (374f, -2.2f, true),
            (396f, -1.4f, false), (416f, 0.2f, false), (438f, 1.8f, true), (462f, 0.8f, false), (484f, 0f, false),
        };

        static void BuildRewards(MammothSettings cfg, out GameObject practiceTemplate)
        {
            var rewards = new GameObject("Rewards");
            for (int i = 0; i < AcornPath.Length; i++)
            {
                var a = AcornPath[i];
                float y = MammothRoute.Height(a.z) + 1.1f + (a.high ? 1.4f : 0f);
                var r = BuildAcorn(rewards.transform, a.high ? "Acorn (high)" : "Acorn", new Vector3(a.x, y, a.z), a.high, i);
                var reward = r.AddComponent<MammothReward>();
                reward.id = i + 1;
                reward.points = 10;
                reward.high = a.high;
            }

            practiceTemplate = BuildAcorn(rewards.transform, "Practice Acorn Template", new Vector3(0f, -20f, -60f), false, 99);
            var pr = practiceTemplate.AddComponent<MammothReward>();
            pr.practice = true;
            pr.points = 0;
            practiceTemplate.SetActive(false);
        }

        static GameObject BuildAcorn(Transform parent, string name, Vector3 pos, bool air, int i)
        {
            if (V2) return BuildAcornV2(parent, name, pos, air);
            var r = new GameObject(name);
            r.transform.SetParent(parent);
            r.transform.position = pos;
            r.transform.localScale = Vector3.one * (air ? .62f : .58f);

            // Rounded, chunky low-poly nut body (not a pointed cone).
            var nut = new GameObject("Acorn Nut");
            nut.transform.SetParent(r.transform, false);
            nut.transform.localPosition = new Vector3(0, -0.5f, 0);
            nut.transform.localScale = new Vector3(0.74f, 0.55f, 0.74f);
            nut.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 31 + 9, jitter: 0.14f, subdivisions: 1);
            nut.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(AcornNutGround);

            // Round beret-style cap, wider than the nut so it overlaps the rim.
            var cap = new GameObject("Acorn Cap");
            cap.transform.SetParent(r.transform, false);
            cap.transform.localPosition = new Vector3(0, 0.15f, 0);
            cap.transform.localScale = new Vector3(0.88f, 0.35f, 0.88f);
            cap.AddComponent<MeshFilter>().sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 31 + 20, jitter: 0.1f, subdivisions: 1);
            cap.AddComponent<MeshRenderer>().sharedMaterial = MakeMaterial(AcornCapGround);

            // Small angled stem on top.
            var stem = Part(r.gameObject, PrimitiveType.Cylinder, "Acorn Stem",
                new Vector3(0.09f, 0.44f, 0f), new Vector3(0.045f, 0.12f, 0.045f),
                AcornCapGround, flat: true);
            stem.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            return r;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color c, bool flat = false)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            FlatTint(g, c, flat);
            return g;
        }

        static readonly Dictionary<PrimitiveType, Mesh> FlatPrimitive = new Dictionary<PrimitiveType, Mesh>();

        static void FlatTint(GameObject g, Color c, bool flatShade = true)
        {
            if (flatShade)
            {
                // Share one flattened mesh per primitive type instead of a new
                // mesh per part, so hundreds of props don't bloat the scene.
                var mf = g.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var key = PrimitiveKey(mf.sharedMesh.name);
                    if (!FlatPrimitive.TryGetValue(key, out var m) || m == null)
                    {
                        m = LowPolyMeshUtil.Flatten(mf.sharedMesh);
                        SaveMesh(m, "Flat " + key);
                        FlatPrimitive[key] = m;
                    }
                    mf.sharedMesh = m;
                }
            }
            g.GetComponent<Renderer>().sharedMaterial = MakeMaterial(c);
        }

        static PrimitiveType PrimitiveKey(string meshName)
        {
            if (meshName.Contains("Cylinder")) return PrimitiveType.Cylinder;
            if (meshName.Contains("Capsule")) return PrimitiveType.Capsule;
            if (meshName.Contains("Cube")) return PrimitiveType.Cube;
            if (meshName.Contains("Quad")) return PrimitiveType.Quad;
            if (meshName.Contains("Plane")) return PrimitiveType.Plane;
            return PrimitiveType.Sphere;
        }

        /// <summary>Generated meshes are saved as assets so the saved scene keeps them.</summary>
        static void SaveMesh(Mesh mesh, string name)
        {
            System.IO.Directory.CreateDirectory("Assets/Mammoth/Generated");
            // The new scene keeps its own mesh assets so rebuilding one scene
            // never breaks the other.
            if (V2) name = "Adventure " + name;
            string path = "Assets/Mammoth/Generated/" + name.Replace('/', '_') + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        static Material MakeMaterial(Color c)
        {
            if (Materials.TryGetValue(c, out var m) && m != null) return m;
            string name = "Mammoth " + ColorUtility.ToHtmlStringRGB(c);
            string path = "Assets/Mammoth/Materials/" + name + ".mat";

            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader) { name = name, color = c };
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
                System.IO.Directory.CreateDirectory("Assets/Mammoth/Materials");
                AssetDatabase.CreateAsset(m, path);
            }
            Materials[c] = m;
            return m;
        }
    }
}
