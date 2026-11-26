using UnityEngine;

namespace Mammoth.Art
{
    /// <summary>
    /// Turns a smooth-shaded mesh into a faceted "low-poly" look by duplicating
    /// vertices per triangle and assigning flat (face) normals. Used for the
    /// stylized, pastel-ice art direction instead of smooth default primitives.
    /// </summary>
    public static class LowPolyMeshUtil
    {
        public static Mesh Flatten(Mesh source)
        {
            var src = Object.Instantiate(source);
            var verts = src.vertices;
            var tris = src.triangles;
            var uvs = src.uv;
            bool hasUv = uvs != null && uvs.Length == verts.Length;

            var outVerts = new Vector3[tris.Length];
            var outNormals = new Vector3[tris.Length];
            var outUv = new Vector2[tris.Length];
            var outTris = new int[tris.Length];

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[i + k];
                    outVerts[i + k] = verts[vi];
                    outNormals[i + k] = n;
                    outUv[i + k] = hasUv ? uvs[vi] : Vector2.zero;
                    outTris[i + k] = i + k;
                }
            }

            var mesh = new Mesh { name = source.name + " (LowPoly)" };
            mesh.vertices = outVerts;
            mesh.normals = outNormals;
            mesh.uv = outUv;
            mesh.triangles = outTris;
            mesh.RecalculateBounds();
            Object.DestroyImmediate(src);
            return mesh;
        }

        /// <summary>Replaces a renderer's mesh with a flat-shaded copy in place.</summary>
        public static void ApplyFlatShading(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            mf.sharedMesh = Flatten(mf.sharedMesh);
        }

        /// <summary>
        /// Builds a jittered low-poly "ice chunk" mesh by subdividing an octahedron
        /// and displacing vertices along their normal, then flat-shading the result.
        /// </summary>
        public static Mesh BuildIceChunk(int seed, float jitter = 0.28f, int subdivisions = 1)
        {
            var rng = new System.Random(seed);
            var verts = new System.Collections.Generic.List<Vector3>
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            var tris = new System.Collections.Generic.List<int>
            {
                0,4,3, 0,3,5, 0,5,2, 0,2,4,
                1,3,4, 1,5,3, 1,2,5, 1,4,2
            };

            var cache = new System.Collections.Generic.Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                var m = ((verts[a] + verts[b]) * 0.5f).normalized;
                verts.Add(m);
                idx = verts.Count - 1;
                cache[key] = idx;
                return idx;
            }

            for (int s = 0; s < subdivisions; s++)
            {
                var next = new System.Collections.Generic.List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = next;
                cache.Clear();
            }

            var outVerts = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                float d = 1f + ((float)rng.NextDouble() * 2f - 1f) * jitter;
                outVerts[i] = verts[i] * d;
            }

            var mesh = new Mesh { name = "IceChunk" };
            mesh.vertices = outVerts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Flatten(mesh);
        }
    }
}
