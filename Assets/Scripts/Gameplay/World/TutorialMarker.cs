using System.Collections.Generic;
using Residue.Data;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The arrow hanging in the air over whatever <see cref="TutorialTargets"/> is pointing at: a
    /// downward chevron, bobbing, yawed to keep one face towards the eye.
    ///
    /// <para>
    /// A plain class owning a <see cref="GameObject"/>, the way <see cref="TutorialCard"/> is a plain
    /// class owning a subtree, so <see cref="LabHud"/> stays a component that draws a crosshair rather
    /// than one that also knows how an arrow is built.
    /// </para>
    ///
    /// <para>
    /// <b>Geometry, not a sprite.</b> §2.1 allows no textures outside the instrument screens, and this
    /// project's own note is that a dial is a cylinder rather than a texture. Eighteen triangles
    /// generated here at runtime rather than authored in <c>LabSceneBuilder</c>, for two reasons: a
    /// tutorial-only object baked into the lab scene would sit in the scene file of every real run,
    /// which is precisely what "nothing may appear outside a tutorial" argues against; and
    /// <c>ProcMesh</c> is editor-only, so a runtime component could not have called it anyway.
    /// </para>
    ///
    /// <para>
    /// <b>Not a verdict colour (hard rule 4).</b> A quest marker is the exact thing that reaches for
    /// green, and palette row 4 is reserved. It is <see cref="SignalPalette.Accent"/> — the same
    /// coolant-family teal <see cref="TutorialCard"/> already draws whichever objective is next in, so
    /// the arrow in the room and the row on the card are visibly the same statement.
    /// </para>
    ///
    /// <para>
    /// <b>It does not draw through walls.</b> The mesh is depth-tested like any other geometry, so a
    /// wall hides it. That is a choice rather than a limitation: an unlit arrow punching through the
    /// lab's shell reads as a rendering fault on flat-shaded untextured geometry — the same objection
    /// §2.6 raises against outlines — and it would be visible from everywhere at once in a building
    /// whose whole navigational interest is that the corridor turns. The occluded case is not dropped,
    /// it is handed to <see cref="TutorialCompass"/>, which is the half that can actually say "turn
    /// left". <see cref="Visible"/> and the compass are exact complements: whenever there is a target,
    /// exactly one of the two is on screen.
    /// </para>
    ///
    /// <para>
    /// <b>It cannot be interacted with.</b> No collider is ever added, and the object sits on
    /// <see cref="PlayerInteractor.IgnoreRaycastLayer"/> — belt and braces, so a future mask that
    /// forgets to exclude it still cannot put an arrow between the player and the machine it is
    /// pointing at.
    /// </para>
    /// </summary>
    public sealed class TutorialMarker
    {
        /// <summary>Metres the arrow travels, peak to trough.</summary>
        public const float BobMetres = 0.07f;

        /// <summary>Seconds for one full bob. Slow enough to read as breathing rather than as a blink.</summary>
        public const float BobSeconds = 1.8f;

        /// <summary>
        /// Fraction of the viewport treated as "not really on screen". A target three pixels inside
        /// the edge is one the player has to hunt for, so the compass keeps it.
        /// </summary>
        public const float EdgeMargin = 0.06f;

        /// <summary>
        /// How much wider that margin is on the way in than on the way out. Without it a target
        /// hovering exactly on the boundary swaps the world arrow for the screen arrow every frame the
        /// player's head moves, which is the same flicker <see cref="OcclusionHold"/> exists to stop —
        /// in the other axis.
        /// </summary>
        public const float EdgeHysteresis = 0.02f;

        /// <summary>
        /// Seconds an occlusion answer has to hold before it is believed. Walking past a door frame
        /// flickers the line of sight several times a second, and an arrow that swapped with the
        /// compass on every one of those would be unreadable.
        /// </summary>
        public const float OcclusionHold = 0.15f;

        /// <summary>
        /// Metres in front of the marker that do not count as being in the way. See
        /// <see cref="UpdateOcclusion"/>: everything that close is the marked object, its neighbours
        /// on the same bench, or the fascia the arrow is floating off.
        /// </summary>
        public const float SelfSkin = 0.5f;

        /// <summary>
        /// What can hide the target. Everything except the layers that are either the player's own
        /// body, the things in their hands, or not physical at all — the same exclusions
        /// <see cref="PlayerInteractor"/> makes for the interaction ray, and for the same reason: a
        /// ray that starts inside the player's own capsule otherwise hits it immediately.
        /// </summary>
        public static int OccluderMask =>
            ~((1 << PlayerInteractor.IgnoreRaycastLayer) |
              (1 << ThirdPersonView.PlayerBodyLayer) |
              (1 << HeldItemCamera.HeldItemLayer));

        private GameObject marker;
        private Transform pivot;
        private Mesh mesh;
        private Material material;

        private Transform anchor;
        private float anchorTop;

        private bool occluded;
        private float occlusionSince = -1f;

        /// <summary>Is the tutorial pointing at anything at all?</summary>
        public bool HasTarget { get; private set; }

        /// <summary>Where the arrow's tip sits, before the bob. Meaningless unless <see cref="HasTarget"/>.</summary>
        public Vector3 Point { get; private set; }

        /// <summary>Is the target inside the viewport, far enough from the edge to be found by eye?</summary>
        public bool OnScreen { get; private set; }

        /// <summary>Is something between the eye and the target?</summary>
        public bool Occluded => occluded;

        /// <summary>
        /// Is the arrow itself being drawn? False whenever <see cref="TutorialCompass"/> should be
        /// drawing instead — the two never appear together and never both stay away.
        /// </summary>
        public bool Visible => HasTarget && OnScreen && !occluded;

        /// <summary>
        /// Point at this, or at nothing. <paramref name="show"/> false is the whole of "switched off":
        /// no target is looked at, the object is hidden, and the next call starts clean.
        /// </summary>
        public void Refresh(in TutorialTarget target, Camera eye, bool show)
        {
            HasTarget = show && eye != null && target.Exists;

            if (!HasTarget)
            {
                anchor = null;
                OnScreen = false;
                occluded = false;
                occlusionSince = -1f;
                if (marker != null) marker.SetActive(false);
                return;
            }

            if (!ReferenceEquals(target.Anchor, anchor)) Measure(target.Anchor);

            Point = target.Anchor.position + Vector3.up * (anchorTop + target.Clearance);

            var viewport = eye.WorldToViewportPoint(Point);
            float margin = OnScreen ? EdgeMargin - EdgeHysteresis : EdgeMargin + EdgeHysteresis;

            OnScreen = viewport.z > 0f &&
                       viewport.x > margin && viewport.x < 1f - margin &&
                       viewport.y > margin && viewport.y < 1f - margin;

            UpdateOcclusion(eye);

            Build();
            marker.SetActive(Visible);
            if (!Visible) return;

            // Unscaled, so the arrow keeps breathing behind a pause menu rather than freezing into
            // something that looks broken.
            float bob = Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / BobSeconds)) * (BobMetres * 0.5f);
            pivot.position = Point + Vector3.up * bob;

            // Yaw only. The chevron is authored pointing down and must keep pointing down — a marker
            // that tilted to face a camera looking from above would stop indicating anything.
            var flat = eye.transform.position - pivot.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f) pivot.rotation = Quaternion.LookRotation(flat.normalized);
        }

        /// <summary>
        /// How tall the marked thing is, measured once per target rather than per frame. Renderers
        /// rather than colliders, because what the arrow has to clear is what the player can see —
        /// and a <see cref="MachineActionButton"/>'s collider is often larger than the button.
        /// </summary>
        private void Measure(Transform target)
        {
            anchor = target;
            anchorTop = 0f;
            if (target == null) return;

            renderers.Clear();
            target.GetComponentsInChildren(true, renderers);

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null) continue;
                anchorTop = Mathf.Max(anchorTop, renderers[i].bounds.max.y - target.position.y);
            }
        }

        private readonly List<Renderer> renderers = new();

        private void UpdateOcclusion(Camera eye)
        {
            var from = eye.transform.position;
            var to = Point;

            // Anything within SelfSkin of the arrow is the thing being marked, or the bench it is
            // standing on, and is not an occluder. Distance rather than a hierarchy test because the
            // marked object is not always the one in the way: an action button's arrow hangs 16 cm off
            // the instrument's own fascia, and that instrument is a different transform from the
            // button. A wall three metres out still blocks.
            float span = Vector3.Distance(from, to);
            bool blocked = Physics.Linecast(from, to, out var hit, OccluderMask,
                                            QueryTriggerInteraction.Ignore) &&
                           hit.distance < span - SelfSkin;

            if (blocked == occluded)
            {
                occlusionSince = -1f;
                return;
            }

            if (occlusionSince < 0f) occlusionSince = Time.unscaledTime;
            if (Time.unscaledTime - occlusionSince < OcclusionHold) return;

            occluded = blocked;
            occlusionSince = -1f;
        }

        /// <summary>Give back the object, the mesh and the material. Called from <see cref="LabHud"/>.</summary>
        public void Dispose()
        {
            if (marker != null) Object.Destroy(marker);
            if (mesh != null) Object.Destroy(mesh);
            if (material != null) Object.Destroy(material);

            marker = null;
            pivot = null;
            mesh = null;
            material = null;
        }

        // -- The object --------------------------------------------------------------------------------

        private void Build()
        {
            if (marker != null) return;

            mesh = BuildMesh();
            material = BuildMaterial();

            marker = new GameObject("TutorialMarker")
            {
                // Never written into a scene or a prefab. This exists for the length of one tutorial
                // run and belongs to the HUD that made it.
                hideFlags = HideFlags.DontSave,
                layer = PlayerInteractor.IgnoreRaycastLayer
            };

            pivot = marker.transform;
            marker.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = marker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // No shadow either way: an annotation that cast one would be a thing in the room, and a
            // marker over an instrument would put a chevron-shaped shadow on the bench beside it.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// Unlit, and drawn from both sides.
        /// <para>
        /// Unlit because this is an annotation rather than a fixture: it must read identically in the
        /// lit sample room and in the dim corridor, and a marker that went dark in the one place
        /// navigation is hard would be worse than useless. Two-sided (<c>_Cull</c> off, which the URP
        /// unlit shader exposes) because the geometry below is generated without an Editor to look at
        /// it, and a winding mistake should cost a slightly odd silhouette rather than an invisible
        /// arrow nobody can diagnose from a log.
        /// </para>
        /// </summary>
        private static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            var created = new Material(shader)
            {
                name = "TutorialMarkerMaterial",
                hideFlags = HideFlags.HideAndDontSave,
                color = SignalPalette.Accent
            };

            if (created.HasProperty("_BaseColor")) created.SetColor("_BaseColor", SignalPalette.Accent);
            if (created.HasProperty("_Cull")) created.SetFloat("_Cull", (float)CullMode.Off);

            return created;
        }

        // Authored in final mesh-local coordinates with the tip at the origin, so the transform's
        // position is exactly the point being marked and the yaw above is the only rotation involved.
        private const float HeadHeight = 0.30f;
        private const float HeadHalfWidth = 0.13f;
        private const float ShaftHeight = 0.22f;
        private const float ShaftHalfWidth = 0.045f;

        /// <summary>
        /// A four-sided chevron with a square shaft: eighteen triangles, hard normals, one palette
        /// colour. Flat-shaded by construction — every triangle carries its own three vertices, so no
        /// normal is ever shared across a crease.
        /// </summary>
        private static Mesh BuildMesh()
        {
            var vertices = new List<Vector3>(54);
            var normals = new List<Vector3>(54);
            var uvs = new List<Vector2>(54);
            var triangles = new List<int>(54);

            var tip = Vector3.zero;

            var a = new Vector3(-HeadHalfWidth, HeadHeight, -HeadHalfWidth);
            var b = new Vector3(HeadHalfWidth, HeadHeight, -HeadHalfWidth);
            var c = new Vector3(HeadHalfWidth, HeadHeight, HeadHalfWidth);
            var d = new Vector3(-HeadHalfWidth, HeadHeight, HeadHalfWidth);

            // Sides of the head, then its top face.
            Face(vertices, normals, uvs, triangles, tip, b, a);
            Face(vertices, normals, uvs, triangles, tip, c, b);
            Face(vertices, normals, uvs, triangles, tip, d, c);
            Face(vertices, normals, uvs, triangles, tip, a, d);
            Quad(vertices, normals, uvs, triangles, a, b, c, d);

            float top = HeadHeight + ShaftHeight;

            var s0 = new Vector3(-ShaftHalfWidth, HeadHeight, -ShaftHalfWidth);
            var s1 = new Vector3(ShaftHalfWidth, HeadHeight, -ShaftHalfWidth);
            var s2 = new Vector3(ShaftHalfWidth, HeadHeight, ShaftHalfWidth);
            var s3 = new Vector3(-ShaftHalfWidth, HeadHeight, ShaftHalfWidth);

            var t0 = new Vector3(-ShaftHalfWidth, top, -ShaftHalfWidth);
            var t1 = new Vector3(ShaftHalfWidth, top, -ShaftHalfWidth);
            var t2 = new Vector3(ShaftHalfWidth, top, ShaftHalfWidth);
            var t3 = new Vector3(-ShaftHalfWidth, top, ShaftHalfWidth);

            Quad(vertices, normals, uvs, triangles, s0, s1, t1, t0);
            Quad(vertices, normals, uvs, triangles, s1, s2, t2, t1);
            Quad(vertices, normals, uvs, triangles, s2, s3, t3, t2);
            Quad(vertices, normals, uvs, triangles, s3, s0, t0, t3);
            Quad(vertices, normals, uvs, triangles, t0, t1, t2, t3);

            var built = new Mesh { name = "TutorialMarkerMesh", hideFlags = HideFlags.HideAndDontSave };
            built.SetVertices(vertices);
            built.SetNormals(normals);
            built.SetUVs(0, uvs);
            built.SetTriangles(triangles, 0);
            built.RecalculateBounds();
            return built;
        }

        /// <summary>
        /// One flat triangle. The normal is derived from the winding rather than passed in, so the two
        /// can never disagree — the failure <c>ProcMesh</c> logs an error for is made unrepresentable
        /// here instead of detected.
        /// </summary>
        private static void Face(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
                                 List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
        {
            // Cross(c - a, b - a), not the other way round: Unity winds front faces clockwise as seen
            // from outside, in a left-handed space, so that ordering is the one that points outwards.
            var normal = Vector3.Cross(c - a, b - a).normalized;

            // Addressed through PaletteUv rather than by raw numbers (§2.2), at the coolant family the
            // accent belongs to. The runtime material below is flat and untextured — this is
            // annotation, not a fixture, and it must read the same in the lit sample room and the dim
            // corridor — so the coordinate is unused today. It is here so that geometry which may one
            // day be handed the palette material is already addressing it correctly, rather than
            // carrying zeroes that would sample whatever sits at texel (0,0).
            var uv = PaletteUv.TexelCenter(PaletteUv.Family.Coolant, PaletteUv.Light);

            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }

        private static void Quad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
                                 List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Face(vertices, normals, uvs, triangles, a, b, c);
            Face(vertices, normals, uvs, triangles, a, c, d);
        }
    }
}
