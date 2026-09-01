using UnityEngine;
using UnityEngine.UIElements;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// The half of the tutorial's pointing that works when you cannot see the thing: a screen arrow
    /// saying which way it is.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> An arrow over a machine you are not looking at is worth nothing,
    /// and the lab's corridor turns — so for most of the walk between two rooms the marker in
    /// <see cref="TutorialMarker"/> is behind a wall or off the side of the screen. This is what is
    /// left to say at that moment, and it is the half a player actually needs.
    /// </para>
    ///
    /// <para>
    /// <b>Two placements, one arrow.</b> Off the side of the screen, or behind the player, it sits on
    /// a ring inset from the panel edge and points outward: turn this way. On screen but occluded, it
    /// sits just above where the target would be and points down at it: it is through there. It is
    /// never up at the same time as the world marker and never away at the same time — between them
    /// there is always exactly one thing on screen while an objective has a target.
    /// </para>
    ///
    /// <para>
    /// <b>It never covers the crosshair or the prompt.</b> The occluded placement is pushed out of a
    /// <see cref="CentreKeepOut"/> disc around screen centre, which clears the crosshair, the prompt
    /// line and the hold bar underneath it. §2.6 makes the crosshair the one thing that tells a player
    /// what they are about to act on; a navigation aid may not sit on top of it. The element ignores
    /// picking, so it cannot swallow input either.
    /// </para>
    ///
    /// <para>
    /// <b>Not a verdict colour (hard rule 4).</b> <see cref="SignalPalette.Accent"/>, matching the
    /// world marker and the card's "next" row. Drawn over a dark backing so it survives a pale wall,
    /// since unlike a severity it has no glyph or word to fall back on — it is one shape whose whole
    /// meaning is which way it points.
    /// </para>
    ///
    /// <para>
    /// Geometry through <c>Painter2D</c> rather than a background image, for §2.1's reason: there are
    /// no textures in this project outside the generated screen surfaces, and an arrow is three points.
    /// </para>
    /// </summary>
    public sealed class TutorialCompass
    {
        /// <summary>Pixels between the arrow's ring and the panel edge.</summary>
        public const float EdgeInset = 64f;

        /// <summary>
        /// Pixels of clear space kept around screen centre. Sized off the HUD: the crosshair opens to
        /// 14 px, the prompt sits 22 px under it and the hold bar 8 px under that, so nothing inside
        /// this radius is free.
        /// </summary>
        public const float CentreKeepOut = 110f;

        /// <summary>Tip to base, in pixels.</summary>
        public const float ArrowLength = 26f;

        public const float ArrowHalfWidth = 13f;

        /// <summary>Pixels of stem behind the head, so it reads as an arrow rather than a triangle.</summary>
        public const float StemLength = 12f;

        public const float StemHalfWidth = 3.5f;

        /// <summary>How far the dark backing extends past the arrow, in pixels.</summary>
        public const float OutlineWidth = 2.5f;

        private Vector2 at;
        private float angle;
        private bool drawing;

        public TutorialCompass()
        {
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Root.style.position = Position.Absolute;
            Root.style.left = 0;
            Root.style.right = 0;
            Root.style.top = 0;
            Root.style.bottom = 0;
            Root.style.display = DisplayStyle.None;
            Root.generateVisualContent += Paint;
        }

        /// <summary>The tree to parent. Full-screen, transparent and inert.</summary>
        public VisualElement Root { get; }

        /// <summary>
        /// Point at <paramref name="worldPoint"/>, or draw nothing.
        /// <para>
        /// <paramref name="onScreen"/> is <see cref="TutorialMarker.OnScreen"/> rather than something
        /// recomputed here, so the two can never disagree about where the boundary between "arrow in
        /// the room" and "arrow on the edge" is — a disagreement would show as both drawing at once,
        /// or neither.
        /// </para>
        /// </summary>
        public void Refresh(bool show, Vector3 worldPoint, bool onScreen, Camera eye)
        {
            if (!show || eye == null) { Hide(); return; }

            var rect = Root.contentRect;
            if (float.IsNaN(rect.width) || rect.width < 1f || rect.height < 1f) { Hide(); return; }

            var viewport = eye.WorldToViewportPoint(worldPoint);
            bool behind = viewport.z <= 0f;

            var centre = new Vector2(rect.width * 0.5f, rect.height * 0.5f);

            // Panel space counts y downwards and the viewport counts it up, hence the flip. Derived
            // from the element's own rect rather than from Screen.width, so a scaled panel puts the
            // arrow where the player sees the target rather than where the pixels are.
            var projected = new Vector2(viewport.x * rect.width, (1f - viewport.y) * rect.height);

            // A point behind the camera projects to the opposite side of the screen from where it
            // actually is. Mirroring through the centre is what makes "turn left" mean left.
            if (behind) projected = centre * 2f - projected;

            var direction = projected - centre;
            if (direction.sqrMagnitude < 1e-4f) direction = new Vector2(0f, -1f);
            direction.Normalize();

            Vector2 placed;
            float facing;

            if (behind || !onScreen)
            {
                placed = centre + direction * EdgeDistance(direction, rect);
                facing = Mathf.Atan2(direction.y, direction.x);
            }
            else
            {
                var above = projected;
                if ((above - centre).magnitude < CentreKeepOut) above = centre + direction * CentreKeepOut;

                placed = ClampInside(above - new Vector2(0f, ArrowLength), rect);

                // Straight down, in a space whose y grows downwards.
                facing = Mathf.PI * 0.5f;
            }

            // Repainted on a change rather than every frame. It changes whenever the player turns,
            // which is often — but standing still is the common case and costs nothing.
            bool moved = !drawing ||
                         (placed - at).sqrMagnitude > 0.25f ||
                         Mathf.Abs(Mathf.DeltaAngle(facing * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) > 0.4f;

            at = placed;
            angle = facing;
            drawing = true;

            Root.style.display = DisplayStyle.Flex;
            if (moved) Root.MarkDirtyRepaint();
        }

        private void Hide()
        {
            if (!drawing) return;

            drawing = false;
            Root.style.display = DisplayStyle.None;
            Root.MarkDirtyRepaint();
        }

        /// <summary>Distance from centre to the inset rectangle's boundary along a unit direction.</summary>
        private static float EdgeDistance(Vector2 direction, Rect rect)
        {
            float halfX = Mathf.Max(1f, rect.width * 0.5f - EdgeInset);
            float halfY = Mathf.Max(1f, rect.height * 0.5f - EdgeInset);

            float alongX = Mathf.Abs(direction.x) > 1e-4f ? halfX / Mathf.Abs(direction.x) : float.MaxValue;
            float alongY = Mathf.Abs(direction.y) > 1e-4f ? halfY / Mathf.Abs(direction.y) : float.MaxValue;

            return Mathf.Min(alongX, alongY);
        }

        private static Vector2 ClampInside(Vector2 point, Rect rect) => new(
            Mathf.Clamp(point.x, EdgeInset, Mathf.Max(EdgeInset, rect.width - EdgeInset)),
            Mathf.Clamp(point.y, EdgeInset, Mathf.Max(EdgeInset, rect.height - EdgeInset)));

        private void Paint(MeshGenerationContext context)
        {
            if (!drawing) return;

            var forward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-forward.y, forward.x);

            var painter = context.painter2D;

            // Dark first, accent over it. The arrow carries its meaning in a direction rather than in
            // a word, so it has to stay legible against a pale wall as well as a dark one.
            Stroke(painter, forward, side, OutlineWidth, SignalPalette.Panel);
            Stroke(painter, forward, side, 0f, SignalPalette.Accent);
        }

        private void Stroke(Painter2D painter, Vector2 forward, Vector2 side, float grow, Color colour)
        {
            float length = ArrowLength + grow * 2f;
            float halfWidth = ArrowHalfWidth + grow;
            var tip = at + forward * (length * 0.5f);
            var back = at - forward * (length * 0.5f);

            painter.fillColor = colour;

            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(back + side * halfWidth);
            painter.LineTo(back - side * halfWidth);
            painter.ClosePath();
            painter.Fill();

            float stem = StemLength + grow;
            float stemHalf = StemHalfWidth + grow;
            var tail = back - forward * stem;

            painter.BeginPath();
            painter.MoveTo(back + side * stemHalf);
            painter.LineTo(tail + side * stemHalf);
            painter.LineTo(tail - side * stemHalf);
            painter.LineTo(back - side * stemHalf);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
