using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Draws what the interaction raycast actually sees: every nearby collider as a wireframe, the
    /// ray itself, and the ordered list of everything it passes through.
    /// <para>
    /// Built because "I have to aim at empty space to select things" is a symptom with several
    /// possible causes — a collider offset from its mesh, a larger collider in front of a smaller
    /// one, a MeshCollider whose shape does not match what it looks like, or an object with no
    /// <see cref="Interactable"/> above it swallowing the hit. Guessing between those from a
    /// description wastes more time than showing them does.
    /// </para>
    /// Toggled with F3. Costs a <c>RaycastAll</c> per frame while on, so it defaults to off.
    /// </summary>
    public sealed class InteractionDebug : MonoBehaviour
    {
        /// <summary>Read by <see cref="PlayerInteractor"/> to decide whether to gather all hits.</summary>
        public static bool Enabled { get; private set; }

        [SerializeField] private PlayerInteractor interactor;

        [Tooltip("Colliders within this radius of the player are outlined.")]
        [SerializeField] private float outlineRadius = 7f;

        [Tooltip("Skip wireframing MeshColliders above this triangle count.")]
        [SerializeField] private int meshWireframeLimit = 1200;

        // Debug colours deliberately outside the game palette. Magenta and cyan never appear in the
        // lab, so nothing here can be mistaken for a verdict, an instrument state or a real surface.
        private static readonly Color Idle = new(0.15f, 0.75f, 0.85f, 0.35f);
        private static readonly Color Targeted = new(1f, 1f, 1f, 0.95f);
        private static readonly Color RayColour = new(1f, 0.15f, 0.9f, 0.9f);
        private static readonly Color BlockerColour = new(1f, 0.55f, 0.0f, 0.8f);
        private static readonly Color NoInteractable = new(0.55f, 0.55f, 0.60f, 0.45f);

        private Material lines;
        private readonly List<Collider> nearby = new();

        private void Awake()
        {
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;

            lines = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            lines.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            lines.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            lines.SetInt("_Cull", (int)CullMode.Off);
            lines.SetInt("_ZWrite", 0);

            // Draw through geometry. A hitbox hiding inside a machine is exactly the case we are
            // trying to see, so depth-testing the overlay would hide the bug being hunted.
            lines.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        private void OnDestroy()
        {
            if (lines != null) Destroy(lines);
            Enabled = false;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
                Enabled = !Enabled;
        }

        // -- Drawing ---------------------------------------------------------------------------------

        private void OnRenderObject()
        {
            if (!Enabled || lines == null || interactor == null) return;

            RefreshNearby();

            lines.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            var targetedColliders = TargetedColliders();

            foreach (var c in nearby)
            {
                if (c == null) continue;

                bool isTarget = targetedColliders.Contains(c);
                bool hasInteractable = c.GetComponentInParent<Interactable>() != null;

                Color colour = isTarget ? Targeted : hasInteractable ? Idle : NoInteractable;
                DrawCollider(c, colour);
            }

            DrawRay();

            GL.End();
            GL.PopMatrix();
        }

        private HashSet<Collider> TargetedColliders()
        {
            var set = new HashSet<Collider>();
            if (interactor.Target == null) return set;

            foreach (var c in interactor.Target.GetComponentsInChildren<Collider>(true)) set.Add(c);
            return set;
        }

        private void RefreshNearby()
        {
            nearby.Clear();
            foreach (var c in Physics.OverlapSphere(transform.position, outlineRadius, interactor.Mask,
                         QueryTriggerInteraction.Collide))
            {
                // The room shell would drown everything else in wireframe.
                if (c is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.triangles.Length / 3 > meshWireframeLimit)
                    continue;
                nearby.Add(c);
            }
        }

        private void DrawRay()
        {
            var ray = interactor.LastRay;
            float end = interactor.LastHadHit ? interactor.LastHit.distance : interactor.Range;

            GL.Color(RayColour);
            Line(ray.origin, ray.origin + ray.direction * end);

            if (!interactor.LastHadHit) return;

            // A cross at the exact impact point: if this sits away from the visible surface, the
            // collider and the mesh disagree.
            Vector3 p = interactor.LastHit.point;
            const float s = 0.035f;
            Line(p + Vector3.left * s, p + Vector3.right * s);
            Line(p + Vector3.up * s, p + Vector3.down * s);
            Line(p + Vector3.forward * s, p + Vector3.back * s);

            // Anything the ray passed through before the thing you wanted is a blocker.
            var hits = interactor.LastAllHits;
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].collider == interactor.LastHit.collider) break;
                GL.Color(BlockerColour);
                DrawCollider(hits[i].collider, BlockerColour);
            }
        }

        private void DrawCollider(Collider c, Color colour)
        {
            GL.Color(colour);

            switch (c)
            {
                case BoxCollider box:
                    DrawLocalBox(box.transform, box.center, box.size);
                    break;

                case CapsuleCollider capsule:
                {
                    var size = new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f);
                    DrawLocalBox(capsule.transform, capsule.center, size);
                    break;
                }

                case SphereCollider sphere:
                    DrawLocalBox(sphere.transform, sphere.center, Vector3.one * sphere.radius * 2f);
                    break;

                case MeshCollider mesh when mesh.sharedMesh != null:
                    // The mesh IS the hitbox, so draw the real triangles rather than the bounds.
                    // Bounds would look plausible while hiding the actual mismatch.
                    DrawMeshWireframe(mesh.transform, mesh.sharedMesh);
                    break;

                default:
                    DrawWorldBounds(c.bounds);
                    break;
            }
        }

        private void DrawLocalBox(Transform t, Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var corners = new Vector3[8];
            int i = 0;

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                corners[i++] = t.TransformPoint(centre + new Vector3(h.x * sx, h.y * sy, h.z * sz));

            // corners are ordered by (x, y, z) sign bits, so xor of index bits gives edges.
            for (int a = 0; a < 8; a++)
            {
                for (int bit = 1; bit <= 4; bit <<= 1)
                {
                    int b = a ^ bit;
                    if (b > a) Line(corners[a], corners[b]);
                }
            }
        }

        private void DrawWorldBounds(Bounds b)
        {
            Vector3 c = b.center, h = b.extents;
            var corners = new Vector3[8];
            int i = 0;

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                corners[i++] = c + new Vector3(h.x * sx, h.y * sy, h.z * sz);

            for (int a = 0; a < 8; a++)
            {
                for (int bit = 1; bit <= 4; bit <<= 1)
                {
                    int b2 = a ^ bit;
                    if (b2 > a) Line(corners[a], corners[b2]);
                }
            }
        }

        private void DrawMeshWireframe(Transform t, Mesh mesh)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(verts[tris[i]]);
                Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 c = t.TransformPoint(verts[tris[i + 2]]);
                Line(a, b);
                Line(b, c);
                Line(c, a);
            }
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        // -- Readout ---------------------------------------------------------------------------------

        /// <summary>
        /// The ordered list of what the ray passes through, for the HUD. This is the answer to
        /// "why did aiming here not select the thing I was aiming at".
        /// </summary>
        public string BuildReadout()
        {
            if (interactor == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"INTERACTION DEBUG   F3 toggles   range {interactor.Range:F2} m");

            var hits = interactor.LastAllHits;
            if (hits.Count == 0)
            {
                sb.AppendLine("  ray hits nothing");
            }
            else
            {
                for (int i = 0; i < hits.Count && i < 6; i++)
                {
                    var h = hits[i];
                    var owner = h.collider.GetComponentInParent<Interactable>();
                    string marker = i == 0 ? ">" : " ";

                    sb.AppendLine(
                        $" {marker} {h.distance:F2}m  {h.collider.GetType().Name,-16} " +
                        $"{h.collider.gameObject.name,-16} -> " +
                        (owner != null ? owner.GetType().Name : "NO INTERACTABLE"));
                }
            }

            sb.Append(interactor.Target != null
                ? $"  target: {interactor.Target.GetType().Name} on {interactor.Target.name}"
                : "  target: none");

            return sb.ToString();
        }
    }
}
