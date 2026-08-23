using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Editor.Art
{
    /// <summary>
    /// Builds flat-shaded, untextured geometry that samples the palette atlas.
    /// <para>
    /// For this art style, authoring a prop in C# is usually faster than sourcing one: the style
    /// contract (§2.1) forbids textures and smoothing, so a prop <i>is</i> a handful of boxes and
    /// cylinders with their UVs pinned to palette texels. A dial is a cylinder, not a texture.
    /// </para>
    /// Every face gets its own vertices so normals stay hard without relying on import settings.
    /// </summary>
    public static class ProcMesh
    {
        /// <summary>Accumulates faces across several primitives into one mesh.</summary>
        public sealed class Builder
        {
            private readonly List<Vector3> verts = new();
            private readonly List<Vector3> normals = new();
            private readonly List<Vector2> uvs = new();
            private readonly List<int> tris = new();

            public Builder Box(Vector3 centre, Vector3 size, PaletteUv.Family family, int step)
            {
                Vector2 uv = PaletteUv.TexelCenter(family, step);
                Vector3 h = size * 0.5f;

                // (normal, right, up) per face; each face is its own quad so the normal stays hard.
                AddQuad(centre + Vector3.forward * h.z, Vector3.forward, Vector3.right * h.x, Vector3.up * h.y, uv);
                AddQuad(centre + Vector3.back * h.z, Vector3.back, Vector3.left * h.x, Vector3.up * h.y, uv);
                AddQuad(centre + Vector3.right * h.x, Vector3.right, Vector3.back * h.z, Vector3.up * h.y, uv);
                AddQuad(centre + Vector3.left * h.x, Vector3.left, Vector3.forward * h.z, Vector3.up * h.y, uv);
                // Invariant for every face: cross(right, up) must point ALONG the normal, or the
                // quad comes out wound backwards. The top and bottom are the two that are easy to
                // get wrong, because the in-plane "up" axis is a Z direction rather than +Y.
                AddQuad(centre + Vector3.up * h.y, Vector3.up, Vector3.right * h.x, Vector3.back * h.z, uv);
                AddQuad(centre + Vector3.down * h.y, Vector3.down, Vector3.right * h.x, Vector3.forward * h.z, uv);
                return this;
            }

            /// <summary>
            /// A cylinder along +Y. Sides above 12 are the one case §2.1 allows smoothing on, but we
            /// keep them hard anyway — at these poly counts the facets are the look.
            /// </summary>
            public Builder Cylinder(Vector3 baseCentre, float radius, float height, int sides,
                                    PaletteUv.Family family, int step)
            {
                sides = Mathf.Max(3, sides);
                Vector2 uv = PaletteUv.TexelCenter(family, step);
                Vector3 top = baseCentre + Vector3.up * height;

                for (int i = 0; i < sides; i++)
                {
                    float a0 = i / (float)sides * Mathf.PI * 2f;
                    float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;

                    Vector3 r0 = new(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                    Vector3 r1 = new(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);

                    Vector3 sideNormal = Vector3.Normalize((r0 + r1) * 0.5f);
                    AddTri(baseCentre + r0, baseCentre + r1, top + r1, sideNormal, uv);
                    AddTri(baseCentre + r0, top + r1, top + r0, sideNormal, uv);

                    AddTri(top, top + r0, top + r1, Vector3.up, uv);
                    AddTri(baseCentre, baseCentre + r1, baseCentre + r0, Vector3.down, uv);
                }
                return this;
            }

            /// <summary>A hollow room: floor, ceiling and four walls, built inward-facing.</summary>
            public Builder Room(Vector3 innerSize, float wallThickness, PaletteUv.Family family, int step)
            {
                float w = innerSize.x, h = innerSize.y, d = innerSize.z;
                float t = wallThickness;

                Box(new Vector3(0f, -t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f), family, step);
                Box(new Vector3(0f, h + t * 0.5f, 0f), new Vector3(w + t * 2f, t, d + t * 2f), family, step + 2);
                Box(new Vector3(0f, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + t * 2f, h, t), family, step + 1);
                Box(new Vector3(0f, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + t * 2f, h, t), family, step + 1);
                Box(new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), family, step + 1);
                Box(new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d), family, step + 1);
                return this;
            }

            private void AddQuad(Vector3 centre, Vector3 normal, Vector3 right, Vector3 up, Vector2 uv)
            {
                int b = verts.Count;
                verts.Add(centre - right - up);
                verts.Add(centre + right - up);
                verts.Add(centre + right + up);
                verts.Add(centre - right + up);

                for (int i = 0; i < 4; i++) { normals.Add(normal); uvs.Add(uv); }

                // Unity front-faces are CLOCKWISE viewed from the front. With verts ordered
                // BL, BR, TR, TL that is (0,1,2) and (0,2,3). The reverse of this silently produces
                // boxes whose near faces are culled, so the mesh looks hollow rather than broken —
                // which is why it survived a screenshot review. Validate() below now catches it.
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            private void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 uv)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                for (int k = 0; k < 3; k++) { normals.Add(normal); uvs.Add(uv); }
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            }

            /// <summary>
            /// Triangles whose winding disagrees with their own vertex normal.
            /// <para>
            /// For a correctly wound face, <c>cross(b-a, c-a)</c> points along the normal. An
            /// inverted face still renders — you just see through it — so this class of bug is
            /// invisible in a still image of a closed shape and needs an actual check.
            /// </para>
            /// </summary>
            public int CountInvertedFaces()
            {
                int bad = 0;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                    Vector3 geometric = Vector3.Cross(b - a, c - a);
                    if (Vector3.Dot(geometric, normals[tris[t]]) <= 0f) bad++;
                }
                return bad;
            }

            public Mesh ToMesh(string name)
            {
                int inverted = CountInvertedFaces();
                if (inverted > 0)
                {
                    Debug.LogError(
                        $"[ProcMesh] '{name}' has {inverted} of {TriangleCount} triangles wound against " +
                        "their normal. Those faces will be culled and the shape will look hollow.");
                }

                var mesh = new Mesh { name = name };
                if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            public int TriangleCount => tris.Count / 3;
        }

        /// <summary>
        /// A single facing quad with real 0..1 UVs, for surfaces that carry an actual texture.
        /// <para>
        /// Everything else in this project maps every vertex to one palette texel, which is exactly
        /// wrong for an instrument screen: the display texture would sample a single colour. Screens
        /// are the one place the style contract's no-textures rule does not apply, because the
        /// texture IS the readout.
        /// </para>
        /// </summary>
        public static Mesh ScreenQuad(string name, float width, float height)
        {
            var mesh = new Mesh { name = name };
            float w = width * 0.5f, h = height * 0.5f;

            mesh.SetVertices(new List<Vector3>
            {
                new(-w, -h, 0f), new(w, -h, 0f), new(w, h, 0f), new(-w, h, 0f)
            });
            // Winding (0,1,2),(0,2,3) over BL,BR,TR,TL gives a geometric normal of +Z, so the quad
            // faces +Z and its vertex normals must agree or it lights as if turned away.
            mesh.SetNormals(new List<Vector3>
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward
            });
            // U runs right-to-left in world space. A camera looking down -Z has its right vector at
            // -X, so world +X lands on the LEFT of the screen; mapping u=1 to x=+w mirrors the
            // texture, and text comes out backwards. V needs no flip: v=1 is world +h, which is up.
            mesh.SetUVs(0, new List<Vector2>
            {
                new(1f, 0f), new(0f, 0f), new(0f, 1f), new(1f, 1f)
            });
            mesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Box(string name, Vector3 size, PaletteUv.Family family, int step) =>
            new Builder().Box(Vector3.zero, size, family, step).ToMesh(name);

        public static Mesh Cylinder(string name, float radius, float height, int sides,
                                    PaletteUv.Family family, int step) =>
            new Builder().Cylinder(Vector3.zero, radius, height, sides, family, step).ToMesh(name);
    }
}
