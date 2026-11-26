using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>Menu: Mammoth > Create Demo Scene. Builds a 3-lane test track with placeholder art.</summary>
    public static class MammothDemoSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/MammothDemo.unity";

        [MenuItem("Mammoth/Create Demo Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cfg = MammothSettingsAsset.Load(true);
            float lane = cfg.lane_width;

            // Track
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Track";
            ground.transform.position = new Vector3(0f, -0.5f, 250f);
            ground.transform.localScale = new Vector3(lane * 3f + 2f, 1f, 520f);
            Tint(ground, new Color(.55f, .5f, .42f));

            for (int i = -1; i <= 1; i += 2)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "Lane Divider";
                Object.DestroyImmediate(line.GetComponent<Collider>());
                line.transform.position = new Vector3(i * lane * .5f, 0.01f, 250f);
                line.transform.localScale = new Vector3(.08f, .02f, 520f);
                Tint(line, new Color(.95f, .95f, .9f));
            }

            // Mammoth placeholder
            var mammoth = new GameObject("Mammoth");
            mammoth.transform.position = Vector3.zero;
            var body = Part(mammoth, PrimitiveType.Capsule, "Body", new Vector3(0, 1.1f, 0), new Vector3(1.3f, .9f, 1.3f), new Color(.45f, .3f, .2f));
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Part(mammoth, PrimitiveType.Sphere, "Head", new Vector3(0, 1.6f, 1.05f), Vector3.one * .9f, new Color(.42f, .28f, .18f));
            var trunk = Part(mammoth, PrimitiveType.Cylinder, "Trunk", new Vector3(0, 1.05f, 1.45f), new Vector3(.22f, .45f, .22f), new Color(.4f, .27f, .17f));
            trunk.transform.localRotation = Quaternion.Euler(20, 0, 0);
            Part(mammoth, PrimitiveType.Cylinder, "Tusk L", new Vector3(-.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), new Color(.95f, .92f, .82f)).transform.localRotation = Quaternion.Euler(60, 0, 0);
            Part(mammoth, PrimitiveType.Cylinder, "Tusk R", new Vector3(.28f, 1.2f, 1.4f), new Vector3(.08f, .3f, .08f), new Color(.95f, .92f, .82f)).transform.localRotation = Quaternion.Euler(60, 0, 0);
            foreach (var p in new[] { new Vector3(-.4f, .35f, .5f), new Vector3(.4f, .35f, .5f), new Vector3(-.4f, .35f, -.5f), new Vector3(.4f, .35f, -.5f) })
                Part(mammoth, PrimitiveType.Cylinder, "Leg", p, new Vector3(.32f, .35f, .32f), new Color(.38f, .25f, .16f));

            var grabPoint = new GameObject("Grab Origin").transform;
            grabPoint.SetParent(mammoth.transform, false);
            grabPoint.localPosition = new Vector3(0, 1.1f, 1.6f);

            // Systems
            var systems = new GameObject("Mammoth Systems");
            var receiver = systems.AddComponent<MammothStateReceiver>();
            var controller = mammoth.AddComponent<MammothController>();
            controller.receiver = receiver;
            controller.grabOrigin = grabPoint;
            var hud = systems.AddComponent<MammothDebugHud>();
            hud.receiver = receiver;
            hud.controller = controller;

            // Camera
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0, 4, -8);
                var follow = cam.gameObject.AddComponent<MammothFollowCamera>();
                follow.target = mammoth.transform;
                cam.backgroundColor = new Color(.75f, .85f, .95f);
            }

            // Rewards: some on the ground, some in the air (jump + clap to grab)
            var rewards = new GameObject("Rewards");
            var rng = new System.Random(7);
            for (int i = 0; i < 40; i++)
            {
                int l = rng.Next(-1, 2);
                bool air = i % 3 == 2;
                float y = air ? 1.1f + cfg.jump_height * .9f : 1.1f;
                var r = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                r.name = air ? "Reward (air)" : "Reward";
                Object.DestroyImmediate(r.GetComponent<Collider>());
                r.transform.SetParent(rewards.transform);
                r.transform.position = new Vector3(l * lane, y, 15f + i * 12f);
                r.transform.localScale = Vector3.one * .6f;
                Tint(r, air ? new Color(.35f, .75f, 1f) : new Color(1f, .78f, .2f));
                r.AddComponent<MammothReward>().points = air ? 3 : 1;
            }

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Selection.activeGameObject = mammoth;
            Debug.Log("[Mammoth] Demo scene saved to " + ScenePath + ". Press Play. Start the Python lab for gesture control, or use the keyboard.");
        }

        static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color c)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            Tint(g, c);
            return g;
        }

        static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        static void Tint(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>();
            if (!Materials.TryGetValue(c, out var m) || m == null)
            {
                m = new Material(r.sharedMaterial) { color = c, name = "Mammoth " + ColorUtility.ToHtmlStringRGB(c) };
                System.IO.Directory.CreateDirectory("Assets/Mammoth/Materials");
                AssetDatabase.CreateAsset(m, "Assets/Mammoth/Materials/" + m.name + ".mat");
                Materials[c] = m;
            }
            r.sharedMaterial = m;
        }
    }
}
