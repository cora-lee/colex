using System.Collections.Generic;
using System.Linq;
using Mammoth.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Create Demo Scene. Builds a 3-lane test track in a stylized,
    /// low-poly pastel-ice look (soft blue/white/pink palette, faceted shading,
    /// jittered iceberg props) inspired by cozy mobile ice-world art, with original
    /// shapes and no characters or branding from any existing game.
    /// </summary>
    public static class MammothDemoSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MammothDemo.unity";

        // Pastel ice palette. Floor/berg tones are kept clearly darker than the sky
        // so the ground reads as ground instead of washing out to the same white as
        // the background under direct light.
        static readonly Color SkyTop = new Color(0.56f, 0.76f, 0.95f);
        static readonly Color SkyHorizon = new Color(0.80f, 0.90f, 0.97f);
        static readonly Color Ice1 = new Color(0.56f, 0.76f, 0.88f);
        static readonly Color Ice2 = new Color(0.42f, 0.65f, 0.82f);
        static readonly Color Ice3 = new Color(0.32f, 0.55f, 0.76f);
        static readonly Color SnowWhite = new Color(0.95f, 0.97f, 1f);
        static readonly Color BlushPink = new Color(0.98f, 0.82f, 0.86f);
        static readonly Color MammothFur = new Color(0.90f, 0.78f, 0.62f);
        static readonly Color MammothFurShade = new Color(0.78f, 0.63f, 0.47f);
        static readonly Color TuskColor = new Color(0.98f, 0.96f, 0.9f);
        static readonly Color MountainRock = new Color(0.30f, 0.40f, 0.50f);
        static readonly Color IceSlick = new Color(0.80f, 0.97f, 1f);
        static readonly Color MudColor = new Color(0.42f, 0.32f, 0.22f);

        // Soft pastel mountain palette (dusty mauve/lavender body, warm cream
        // snow tips) in place of the old dark blue-grey rock, so angular peaks
        // read as gentle folded-paper shapes instead of harsh ice crags.
        static readonly Color MountainPastel1 = new Color(0.72f, 0.66f, 0.78f);
        static readonly Color MountainPastel2 = new Color(0.80f, 0.70f, 0.72f);
        static readonly Color SnowCream = new Color(0.97f, 0.94f, 0.90f);

        // Low-poly pom-pom tree + cloud palette.
        static readonly Color TreeTrunk = new Color(0.55f, 0.42f, 0.30f);
        static readonly Color[] TreeFoliage =
        {
            new Color(0.62f, 0.80f, 0.70f),
            new Color(0.70f, 0.85f, 0.78f),
            new Color(0.98f, 0.82f, 0.86f),
            new Color(0.95f, 0.88f, 0.70f),
        };
        static readonly Color CloudWhite = new Color(0.99f, 0.98f, 0.97f);

        [MenuItem("Mammoth/Create Demo Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cfg = MammothSettingsAsset.Load(true);
            float lane = cfg.lane_width;

            SetupSkyAndFog();
            BuildIceFloor(lane);
            BuildIcebergField(lane);
            BuildMountains(lane);
            BuildTrees(lane);
            BuildClouds(lane);

            var mammoth = BuildMammoth();
            var grabPoint = new GameObject("Grab Origin").transform;
            grabPoint.SetParent(mammoth.transform, false);
            grabPoint.localPosition = new Vector3(0, 1.1f, 1.6f);

            // Systems
            var systems = new GameObject("Mammoth Systems");
            var receiver = systems.AddComponent<MammothStateReceiver>();
            var controller = mammoth.AddComponent<MammothController>();
            controller.receiver = receiver;
            controller.grabOrigin = grabPoint;
            controller.terrain = BuildTerrainZones(lane);
            var hud = systems.AddComponent<MammothDebugHud>();
            hud.receiver = receiver;
            hud.controller = controller;
            var camPreview = systems.AddComponent<MammothCameraPreview>();
            camPreview.receiver = receiver;

            SetupLighting();

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0, 4, -8);
                var follow = cam.gameObject.AddComponent<MammothFollowCamera>();
                follow.target = mammoth.transform;
                cam.backgroundColor = SkyHorizon;
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            BuildRewards(lane, cfg);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Selection.activeGameObject = mammoth;
            Debug.Log("[Mammoth] Demo scene saved to " + ScenePath + ". Press Play. Open Web/index.html (see Web/README.md) for camera control, or use the keyboard.");
        }

        static void SetupSkyAndFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogStartDistance = 18f;
            RenderSettings.fogEndDistance = 90f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(SkyTop, SnowWhite, 0.2f);
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
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 0.75f;
            sun.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
        }

        static void BuildIceFloor(float lane)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ice Floor";
            ground.transform.position = new Vector3(0f, -0.5f, 250f);
            ground.transform.localScale = new Vector3(lane * 3f + 2f, 1f, 520f);
            FlatTint(ground, Ice1);

            for (int i = -1; i <= 1; i += 2)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "Lane Divider";
                Object.DestroyImmediate(line.GetComponent<Collider>());
                line.transform.position = new Vector3(i * lane * .5f, 0.01f, 250f);
                line.transform.localScale = new Vector3(.08f, .02f, 520f);
                FlatTint(line, SnowWhite);
            }

            // Scattered flat ice-crystal facets embedded in the floor for texture.
            var rng = new System.Random(3);
            for (int i = 0; i < 60; i++)
            {
                var facet = GameObject.CreatePrimitive(PrimitiveType.Quad);
                facet.name = "Ice Facet";
                Object.DestroyImmediate(facet.GetComponent<Collider>());
                float x = ((float)rng.NextDouble() * 2f - 1f) * (lane * 1.4f);
                float z = (float)rng.NextDouble() * 500f;
                facet.transform.position = new Vector3(x, 0.011f, z);
                facet.transform.rotation = Quaternion.Euler(90f, (float)rng.NextDouble() * 360f, 0f);
                float s = Mathf.Lerp(0.4f, 1.1f, (float)rng.NextDouble());
                facet.transform.localScale = new Vector3(s, s, 1f);
                FlatTint(facet, Color.Lerp(Ice2, SnowWhite, (float)rng.NextDouble()));
            }
        }

        static void BuildIcebergField(float lane)
        {
            var field = new GameObject("Icebergs");
            var rng = new System.Random(11);
            for (int i = 0; i < 26; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * (lane * 1.9f + (float)rng.NextDouble() * lane * 1.6f);
                float z = (float)rng.NextDouble() * 500f;
                float scale = Mathf.Lerp(1.4f, 4.2f, (float)rng.NextDouble());

                var go = new GameObject("Iceberg");
                go.transform.SetParent(field.transform);
                go.transform.position = new Vector3(x, scale * 0.35f - 0.4f, z);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = Vector3.one * scale;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 97 + 5, jitter: 0.32f, subdivisions: 1);
                mr.sharedMaterial = MakeMaterial(Color.Lerp(Ice3, SnowWhite, (float)rng.NextDouble() * 0.6f));
            }

            // A few soft blush-pink accent crystals for the cozy palette, like the reference art.
            for (int i = 0; i < 8; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * (lane * 2.4f + (float)rng.NextDouble() * lane);
                float z = 40f + (float)rng.NextDouble() * 440f;
                var go = new GameObject("Accent Crystal");
                go.transform.SetParent(field.transform);
                go.transform.position = new Vector3(x, 0.6f, z);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                float scale = Mathf.Lerp(0.5f, 1.1f, (float)rng.NextDouble());
                go.transform.localScale = Vector3.one * scale;
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 53 + 2, jitter: 0.22f, subdivisions: 0);
                mr.sharedMaterial = MakeMaterial(BlushPink);
            }
        }

        // A handful of fixed tint blends so MakeMaterial's cache is reused across
        // hundreds of mountains instead of baking a unique .mat asset per peak.
        static readonly float[] MountainTints = { 0f, 0.12f, 0.22f, 0.35f };

        /// <summary>A continuous mountain wall flanking both sides of the track,
        /// built as two overlapping rows (near/low, far/tall) per side so there are
        /// no sky gaps between peaks, closing the "empty valley" look. Snow caps are
        /// separate small props (not scaled children) so they keep a rounded,
        /// cap-like shape instead of stretching with the mountain's tall,
        /// non-uniform scale.</summary>
        static void BuildMountains(float lane)
        {
            var field = new GameObject("Mountains");
            int seed = 0;
            foreach (int side in new[] { -1, 1 })
            {
                // Pulled well back from the lane, and kept low, so the valley stays
                // open and the track ahead is never blocked from the follow camera.
                BuildMountainRow(field, side, lane * 9f, step: 11f, heightMin: 6f, heightMax: 12f, baseMin: 5f, baseMax: 8f, ref seed);
                BuildMountainRow(field, side, lane * 14f, step: 15f, heightMin: 11f, heightMax: 22f, baseMin: 7f, baseMax: 12f, ref seed);
            }
        }

        static void BuildMountainRow(GameObject field, int side, float rowDistance, float step,
            float heightMin, float heightMax, float baseMin, float baseMax, ref int seed)
        {
            var rng = new System.Random(seed * 97 + 29);
            float z = -10f;
            while (z < 515f)
            {
                float x = side * (rowDistance + ((float)rng.NextDouble() - 0.5f) * step * 1.6f);
                float zPos = z + ((float)rng.NextDouble() - 0.5f) * step * 0.6f;
                float heightScale = Mathf.Lerp(heightMin, heightMax, (float)rng.NextDouble());
                float baseScale = Mathf.Lerp(baseMin, baseMax, (float)rng.NextDouble());
                // Flat-base mountain mesh sits at local y=0, so position.y just needs
                // a tiny sink below the floor (y=0) to guarantee no sliver of gap
                // shows between the rock and the ground, however the camera angles.
                var basePos = new Vector3(x, -0.15f, zPos);

                var go = new GameObject("Mountain");
                go.transform.SetParent(field.transform);
                go.transform.position = basePos;
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                go.transform.localScale = new Vector3(baseScale, heightScale, baseScale);

                int peaks = rng.NextDouble() < 0.35 ? 3 : 2;
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = LowPolyMeshUtil.BuildMountainRidge(seed * 71 + 13, peaks: peaks, baseJitter: 0.08f, snowLine: 0.55f);
                var rockColor = Color.Lerp(MountainPastel1, MountainPastel2, MountainTints[rng.Next(MountainTints.Length)]);
                mr.sharedMaterials = new[] { MakeMaterial(rockColor), MakeMaterial(SnowCream) };

                seed++;
                z += step;
            }
        }

        /// <summary>Low-poly "pom-pom" trees scattered near the mountain bases
        /// (trunk + 2-3 stacked round foliage balls, pastel tints), matching the
        /// cozy low-poly forest look of the reference art.</summary>
        static void BuildTrees(float lane)
        {
            var group = new GameObject("Trees");
            var rng = new System.Random(211);
            for (int i = 0; i < 50; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * (lane * (2.2f + (float)rng.NextDouble() * 2.5f));
                float z = (float)rng.NextDouble() * 500f;
                float scale = Mathf.Lerp(0.7f, 1.5f, (float)rng.NextDouble());

                var tree = new GameObject("Tree");
                tree.transform.SetParent(group.transform, false);
                tree.transform.position = new Vector3(x, 0f, z);
                tree.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                tree.transform.localScale = Vector3.one * scale;

                var trunk = Part(tree, PrimitiveType.Cylinder, "Trunk",
                    new Vector3(0, 0.35f, 0), new Vector3(0.12f, 0.35f, 0.12f), TreeTrunk);

                int balls = rng.Next(2, 4);
                float y = 0.62f;
                Color foliage = TreeFoliage[rng.Next(TreeFoliage.Length)];
                for (int b = 0; b < balls; b++)
                {
                    float ballScale = Mathf.Lerp(0.55f, 0.32f, b / (float)(balls - 1 == 0 ? 1 : balls - 1));
                    var ball = new GameObject("Foliage");
                    ball.transform.SetParent(tree.transform, false);
                    ball.transform.localPosition = new Vector3(0, y, 0);
                    ball.transform.localScale = Vector3.one * ballScale;
                    var fmf = ball.AddComponent<MeshFilter>();
                    var fmr = ball.AddComponent<MeshRenderer>();
                    fmf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 17 + b * 5 + 3, jitter: 0.12f, subdivisions: 1);
                    fmr.sharedMaterial = MakeMaterial(foliage);
                    y += ballScale * 0.85f;
                }
            }
        }

        /// <summary>A few soft clouds drifting high over the track — clusters of
        /// 2-3 overlapping flattened ice-chunk puffs in warm white.</summary>
        static void BuildClouds(float lane)
        {
            var group = new GameObject("Clouds");
            var rng = new System.Random(733);
            for (int i = 0; i < 18; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = side * (lane * (1.5f + (float)rng.NextDouble() * 5f));
                float y = Mathf.Lerp(9f, 16f, (float)rng.NextDouble());
                float z = (float)rng.NextDouble() * 520f;
                float scale = Mathf.Lerp(1.6f, 3.2f, (float)rng.NextDouble());

                var cloud = new GameObject("Cloud");
                cloud.transform.SetParent(group.transform, false);
                cloud.transform.position = new Vector3(x, y, z);
                cloud.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                cloud.transform.localScale = Vector3.one * scale;

                int puffs = rng.Next(2, 4);
                for (int p = 0; p < puffs; p++)
                {
                    var puff = new GameObject("Puff");
                    puff.transform.SetParent(cloud.transform, false);
                    float px = (p - (puffs - 1) / 2f) * 0.6f;
                    puff.transform.localPosition = new Vector3(px, (float)rng.NextDouble() * 0.15f, 0);
                    puff.transform.localScale = new Vector3(0.75f, 0.45f, 0.65f) * Mathf.Lerp(0.8f, 1.1f, (float)rng.NextDouble());
                    var pmf = puff.AddComponent<MeshFilter>();
                    var pmr = puff.AddComponent<MeshRenderer>();
                    pmf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 41 + p * 7 + 1, jitter: 0.14f, subdivisions: 1);
                    pmr.sharedMaterial = MakeMaterial(CloudWhite);
                }
            }
        }

        /// <summary>Alternating slick-ice (sideways slide) and mud (slowdown) patches
        /// down the lane, for a more varied downhill run. Purely a distance lookup
        /// in MammothController, no physics triggers needed.</summary>
        static MammothTerrainSegment[] BuildTerrainZones(float lane)
        {
            var group = new GameObject("Terrain Zones");
            var segs = new List<MammothTerrainSegment>();
            const float fadeLen = 3f;
            const int fadeSteps = 4;
            float z = 50f;
            bool ice = true;
            while (z < 480f)
            {
                const float len = 20f;
                var type = ice ? MammothTerrainType.Ice : MammothTerrainType.Mud;
                segs.Add(new MammothTerrainSegment { startZ = z, endZ = z + len, type = type });

                Color patchColor = ice ? IceSlick : MudColor;

                // Lead-in and lead-out fade strips ease the floor color into the
                // patch color over a short run, so the patch reads as a gradual
                // change in the ice rather than a sticker dropped with a hard edge.
                BuildTerrainFade(group, lane, z - fadeLen, fadeLen, fadeSteps, Ice1, patchColor);

                var patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                patch.name = ice ? "Ice Slick" : "Mud Patch";
                Object.DestroyImmediate(patch.GetComponent<Collider>());
                patch.transform.SetParent(group.transform);
                patch.transform.position = new Vector3(0f, 0.016f, z + len * 0.5f);
                patch.transform.localScale = new Vector3(lane * 3f + 1.6f, 0.02f, len);
                patch.GetComponent<Renderer>().sharedMaterial = ice ? MakeSlickMaterial(IceSlick) : MakeMaterial(MudColor);

                BuildTerrainFade(group, lane, z + len, fadeLen, fadeSteps, patchColor, Ice1);

                z += len + 25f;
                ice = !ice;
            }
            return segs.ToArray();
        }

        /// <summary>A short run of narrow, stepped-color strips between fromColor and
        /// toColor, flush with the floor. Cheap stand-in for a soft alpha gradient
        /// using only opaque low-poly materials (matches the rest of the art style).</summary>
        static void BuildTerrainFade(GameObject parent, float lane, float startZ, float fadeLen, int steps, Color fromColor, Color toColor)
        {
            float stepLen = fadeLen / steps;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Terrain Fade";
                Object.DestroyImmediate(strip.GetComponent<Collider>());
                strip.transform.SetParent(parent.transform);
                strip.transform.position = new Vector3(0f, 0.015f, startZ + stepLen * (i + 0.5f));
                // Slightly overlap neighboring strips so no sliver of bare floor shows through.
                strip.transform.localScale = new Vector3(lane * 3f + 1.6f, 0.018f, stepLen + 0.08f);
                strip.GetComponent<Renderer>().sharedMaterial = MakeMaterial(Color.Lerp(fromColor, toColor, t));
            }
        }

        static GameObject BuildMammoth()
        {
            var mammoth = new GameObject("Mammoth");
            mammoth.transform.position = Vector3.zero;

            var body = Part(mammoth, PrimitiveType.Capsule, "Body", new Vector3(0, 1.1f, 0), new Vector3(1.3f, .9f, 1.3f), MammothFur, flat: true);
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Part(mammoth, PrimitiveType.Sphere, "Head", new Vector3(0, 1.6f, 1.05f), Vector3.one * .9f, MammothFur, flat: true);
            var trunk = Part(mammoth, PrimitiveType.Cylinder, "Trunk", new Vector3(0, 1.05f, 1.45f), new Vector3(.22f, .45f, .22f), MammothFurShade, flat: true);
            trunk.transform.localRotation = Quaternion.Euler(20, 0, 0);
            Part(mammoth, PrimitiveType.Cylinder, "Tusk L", new Vector3(-.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), TuskColor, flat: true).transform.localRotation = Quaternion.Euler(60, 0, 0);
            Part(mammoth, PrimitiveType.Cylinder, "Tusk R", new Vector3(.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), TuskColor, flat: true).transform.localRotation = Quaternion.Euler(60, 0, 0);
            foreach (var p in new[] { new Vector3(-.4f, .35f, .5f), new Vector3(.4f, .35f, .5f), new Vector3(-.4f, .35f, -.5f), new Vector3(.4f, .35f, -.5f) })
                Part(mammoth, PrimitiveType.Cylinder, "Leg", p, new Vector3(.32f, .35f, .32f), MammothFurShade, flat: true);

            return mammoth;
        }

        static readonly Color AcornNutGround = new Color(0.90f, 0.70f, 0.35f);
        static readonly Color AcornCapGround = new Color(0.46f, 0.30f, 0.16f);
        static readonly Color AcornNutAir = new Color(0.80f, 0.92f, 0.98f);
        static readonly Color AcornCapAir = new Color(0.40f, 0.62f, 0.80f);

        /// <summary>
        /// Reward pickups are low-poly acorns: a rounded nut body plus a separate,
        /// flattened cap on top. Ground rewards (reachable without jumping, worth
        /// 1 point) use a warm gold-and-brown acorn palette. Air rewards (only
        /// reachable by jumping, worth 3 points) use a frosty icy-blue palette
        /// instead, so the two are told apart by color/material at a glance, not
        /// just by height.
        /// </summary>
        static void BuildRewards(float lane, MammothSettings cfg)
        {
            var rewards = new GameObject("Rewards");
            var rng = new System.Random(7);
            for (int i = 0; i < 40; i++)
            {
                int l = rng.Next(-1, 2);
                bool air = i % 3 == 2;
                float y = air ? 1.1f + cfg.jump_height * .9f : 1.1f;
                var r = new GameObject(air ? "Reward (air)" : "Reward");
                r.transform.SetParent(rewards.transform);
                r.transform.position = new Vector3(l * lane, y, 15f + i * 12f);
                r.transform.localScale = Vector3.one * (air ? .62f : .55f);

                var nut = new GameObject("Acorn Nut");
                nut.transform.SetParent(r.transform, false);
                nut.transform.localScale = new Vector3(0.9f, 1.15f, 0.9f);
                var nmf = nut.AddComponent<MeshFilter>();
                var nmr = nut.AddComponent<MeshRenderer>();
                nmf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 31 + 9, jitter: 0.14f, subdivisions: 1);
                nmr.sharedMaterial = MakeMaterial(air ? AcornNutAir : AcornNutGround);

                var cap = new GameObject("Acorn Cap");
                cap.transform.SetParent(r.transform, false);
                cap.transform.localPosition = new Vector3(0, 0.62f, 0);
                cap.transform.localScale = new Vector3(1.08f, 0.5f, 1.08f);
                var cmf = cap.AddComponent<MeshFilter>();
                var cmr = cap.AddComponent<MeshRenderer>();
                cmf.sharedMesh = LowPolyMeshUtil.BuildIceChunk(i * 31 + 20, jitter: 0.1f, subdivisions: 0);
                cmr.sharedMaterial = MakeMaterial(air ? AcornCapAir : AcornCapGround);

                r.AddComponent<MammothReward>().points = air ? 3 : 1;
            }
        }

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

        static void FlatTint(GameObject g, Color c, bool flatShade = true)
        {
            if (flatShade) LowPolyMeshUtil.ApplyFlatShading(g);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = MakeMaterial(c);
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

        static readonly Dictionary<Color, Material> SlickMaterials = new Dictionary<Color, Material>();

        /// <summary>A glossier material for the slick-ice patches, kept separate from
        /// MakeMaterial's cache (and its flatter default smoothness) by name.</summary>
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
    }
}
