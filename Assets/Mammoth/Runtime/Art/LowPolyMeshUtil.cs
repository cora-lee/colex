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

        /// <summary>
        /// Builds a low-poly mountain peak: a flat, jittered-radius base ring at
        /// local y=0 (so it sits flush with the ground with no gap underneath),
        /// then a stack of intermediate rings tapering up to a single apex at
        /// local y=1. The radius profile eases out slowly at first (quadratic),
        /// so the mound stays wide and round through the lower-mid tiers and only
        /// narrows sharply near the top — a plump snow-pile silhouette. Per-vertex
        /// jitter is kept light: enough to add a handful of facets that catch
        /// light unevenly like packed snow, not so much that the panels buckle
        /// into a wrinkled/crumpled look.
        /// </summary>
        public static Mesh BuildMountainPeak(int seed, float baseJitter = 0.3f, int sides = 8, int tiers = 2)
        {
            var rng = new System.Random(seed);
            var verts = new System.Collections.Generic.List<Vector3> { Vector3.zero }; // [0] base center
            var tris = new System.Collections.Generic.List<int>();

            int prevRingStart = -1;
            for (int t = 0; t <= tiers; t++)
            {
                float tFrac = t / (float)(tiers + 1); // 0 at the base ring, <1 at the last ring before the apex point
                // Eased rather than linear so the silhouette bulges through the
                // middle tiers instead of tapering as a straight-sided pyramid.
                float y = Mathf.Clamp01(Mathf.Pow(tFrac, 0.8f) + (t == 0 ? 0f : ((float)rng.NextDouble() * 2f - 1f) * 0.03f));
                // Quadratic ease keeps the radius close to full width through the
                // lower-mid tiers (plump/round), then falls off faster near the
                // top, instead of a straight linear taper (which read as "wrinkled"
                // once combined with per-vertex jitter).
                float radius = Mathf.Lerp(1f, 0.15f, tFrac * tFrac);

                int ringStart = verts.Count;
                for (int i = 0; i < sides; i++)
                {
                    float angle = (i / (float)sides) * Mathf.PI * 2f;
                    float r = radius * (1f + ((float)rng.NextDouble() * 2f - 1f) * baseJitter * (t == 0 ? 0.55f : 0.22f));
                    verts.Add(new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r));
                }

                if (t == 0)
                {
                    for (int i = 0; i < sides; i++)
                    {
                        int a = ringStart + i;
                        int b = ringStart + (i + 1) % sides;
                        tris.Add(0); tris.Add(a); tris.Add(b); // base cap, downward normal
                    }
                }
                else
                {
                    for (int i = 0; i < sides; i++)
                    {
                        int a0 = prevRingStart + i, b0 = prevRingStart + (i + 1) % sides;
                        int a1 = ringStart + i, b1 = ringStart + (i + 1) % sides;
                        tris.Add(a0); tris.Add(a1); tris.Add(b1);
                        tris.Add(a0); tris.Add(b1); tris.Add(b0);
                    }
                }

                prevRingStart = ringStart;
            }

            float apexX = ((float)rng.NextDouble() * 2f - 1f) * 0.06f;
            float apexZ = ((float)rng.NextDouble() * 2f - 1f) * 0.06f;
            float apexY = 1f + ((float)rng.NextDouble() * 2f - 1f) * 0.04f;
            int apexIdx = verts.Count;
            verts.Add(new Vector3(apexX, apexY, apexZ));
            for (int i = 0; i < sides; i++)
            {
                int a = prevRingStart + i;
                int b = prevRingStart + (i + 1) % sides;
                tris.Add(a); tris.Add(apexIdx); tris.Add(b);
            }

            var mesh = new Mesh { name = "MountainPeak" };
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return Flatten(mesh);
        }

        /// <summary>
        /// Builds a jagged low-poly "mountain ridge": a cluster of 2-3 angular
        /// pyramidal peaks sharing one base ring, each peak split at a snow-line
        /// into a lower rock frustum (submesh 0) and an upper snow-tip cone
        /// (submesh 1). Deliberately built from a handful of large, flat planes
        /// with only mild per-vertex noise on the base ring — not heavy per-vertex
        /// jitter — so the silhouette reads as clean folded-paper facets (like
        /// stylized low-poly mountain illustrations) rather than a crumpled blob.
        /// Assign a rock-tone material to submesh 0 and a snow-tone material to
        /// submesh 1 on the MeshRenderer to get the two-tone look with perfect
        /// alignment (no separate floating snow-cap prop needed).
        /// </summary>
        public static Mesh BuildMountainRidge(int seed, int peaks = 2, float baseJitter = 0.08f, float snowLine = 0.55f)
        {
            var rng = new System.Random(seed);
            int sides = peaks * 3;

            var baseVerts = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = (i / (float)sides) * Mathf.PI * 2f;
                float r = 1f + ((float)rng.NextDouble() * 2f - 1f) * baseJitter;
                baseVerts[i] = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
            }

            var apexPos = new Vector3[peaks];
            for (int p = 0; p < peaks; p++)
            {
                float midAngle = ((p * 3 + 1) / (float)sides) * Mathf.PI * 2f;
                float height = Mathf.Lerp(0.72f, 1f, (float)rng.NextDouble());
                float lean = Mathf.Lerp(0f, 0.22f, (float)rng.NextDouble());
                apexPos[p] = new Vector3(Mathf.Cos(midAngle) * lean, height, Mathf.Sin(midAngle) * lean);
            }

            var rockTris = new System.Collections.Generic.List<Vector3>();
            var snowTris = new System.Collections.Generic.List<Vector3>();
            Vector3 center = Vector3.zero;

            for (int p = 0; p < peaks; p++)
            {
                Vector3 apex = apexPos[p];
                var v = new Vector3[4];
                var s = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    v[k] = baseVerts[(p * 3 + k) % sides];
                    s[k] = Vector3.Lerp(v[k], apex, snowLine);
                }

                // Base cap (downward normal) — hidden at ground level, rock-colored.
                rockTris.Add(center); rockTris.Add(v[0]); rockTris.Add(v[1]);
                rockTris.Add(center); rockTris.Add(v[1]); rockTris.Add(v[2]);
                rockTris.Add(center); rockTris.Add(v[2]); rockTris.Add(v[3]);

                for (int k = 0; k < 3; k++)
                {
                    // Rock frustum quad (base ring -> snow-line ring).
                    rockTris.Add(v[k]); rockTris.Add(s[k]); rockTris.Add(s[k + 1]);
                    rockTris.Add(v[k]); rockTris.Add(s[k + 1]); rockTris.Add(v[k + 1]);
                    // Snow cone (snow-line ring -> apex).
                    snowTris.Add(s[k]); snowTris.Add(apex); snowTris.Add(s[k + 1]);
                }
            }

            int rockCount = rockTris.Count;
            int snowCount = snowTris.Count;
            var allVerts = new Vector3[rockCount + snowCount];
            var allNormals = new Vector3[rockCount + snowCount];
            for (int i = 0; i < rockCount; i += 3)
            {
                Vector3 a = rockTris[i], b = rockTris[i + 1], c = rockTris[i + 2];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                allVerts[i] = a; allVerts[i + 1] = b; allVerts[i + 2] = c;
                allNormals[i] = n; allNormals[i + 1] = n; allNormals[i + 2] = n;
            }
            for (int i = 0; i < snowCount; i += 3)
            {
                Vector3 a = snowTris[i], b = snowTris[i + 1], c = snowTris[i + 2];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int o = rockCount + i;
                allVerts[o] = a; allVerts[o + 1] = b; allVerts[o + 2] = c;
                allNormals[o] = n; allNormals[o + 1] = n; allNormals[o + 2] = n;
            }

            var rockIdx = new int[rockCount];
            for (int i = 0; i < rockCount; i++) rockIdx[i] = i;
            var snowIdx = new int[snowCount];
            for (int i = 0; i < snowCount; i++) snowIdx[i] = rockCount + i;

            var mesh = new Mesh { name = "MountainRidge" };
            mesh.vertices = allVerts;
            mesh.normals = allNormals;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(rockIdx, 0);
            mesh.SetTriangles(snowIdx, 1);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
