using UnityEditor;
using UnityEngine;

namespace Mammoth.EditorTools
{
    /// <summary>
    /// Menu: Mammoth > Preview Route > ... Puts the mammoth and the main camera
    /// at a point on the route in edit mode, framed like the in-game follow
    /// camera, so the scenery can be checked without playing through.
    /// </summary>
    public static class MammothRoutePreview
    {
        [MenuItem("Mammoth/Preview Route/Start (z 0)")] static void P0() => Place(0f, 0f);
        [MenuItem("Mammoth/Preview Route/High acorns (z 66)")] static void P1() => Place(66f, -1f);
        [MenuItem("Mammoth/Preview Route/Steep uphill (z 170)")] static void P2() => Place(170f, 0f);
        [MenuItem("Mammoth/Preview Route/Summit (z 255)")] static void P3() => Place(255f, 0f);
        [MenuItem("Mammoth/Preview Route/Snow to grass (z 325)")] static void P4() => Place(325f, 1f);
        [MenuItem("Mammoth/Preview Route/Downhill (z 420)")] static void P5() => Place(420f, 0f);
        [MenuItem("Mammoth/Preview Route/Campsite (z 498)")] static void P6() => Place(498f, 0f);

        static void Place(float z, float x)
        {
            var mammoth = Object.FindFirstObjectByType<MammothController>();
            var cam = Camera.main;
            if (mammoth == null || cam == null) { Debug.LogWarning("[Mammoth] Build the demo scene first."); return; }
            float g = MammothRoute.Height(z);
            mammoth.transform.position = new Vector3(x, g, z);
            mammoth.transform.rotation = Quaternion.Euler(-Mathf.Atan(MammothRoute.Slope(z)) * Mathf.Rad2Deg, 0f, 0f);
            float cz = z - 8.5f;
            float cg = Mathf.Max(MammothRoute.Height(cz), g - 1.5f);
            cam.transform.position = new Vector3(x * 0.6f, cg + 4.2f, cz);
            cam.transform.LookAt(new Vector3(x * 0.6f, MammothRoute.Height(z + 6f) + 1f, z + 6f));
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }
}
