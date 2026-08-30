using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Setting the selected item down wherever you are looking.
    /// <para>
    /// Every other way of emptying a hand needs a fixture that agrees to take the item — a rack, an
    /// instrument, the desk. §5.5 makes the cost of moving things around a room the whole skill
    /// ceiling, and that only works if the room is somewhere you can actually leave things. Without
    /// this, an item nothing in the lab will accept — a manual, a blank slip, a bottle beside a busy
    /// instrument — occupies one of three slots for the rest of the run.
    /// </para>
    /// <para>
    /// <b>It goes through the host like everything else.</b> The existing <c>PutDown</c> command is the
    /// door: it moves a vial to <c>SampleLocation.OnSurface</c>, releases a slip's paper to the same
    /// surface, and hands a bottle back to <c>SolventStore</c>. The surface it names is a
    /// <see cref="DropSpot"/>, whose id carries the coordinates, so what the host records is a place
    /// every other client can find. With four players in the room, two of them reaching for the same
    /// dropped vial is then a race the host settles exactly as it settles the delivery crate.
    /// </para>
    /// <para>
    /// A static helper rather than a method on <see cref="PlayerInteractor"/> because the whole thing
    /// is a query against the room plus one command; the interactor supplies the aim it already casts
    /// this frame and owns nothing else here.
    /// </para>
    /// </summary>
    public static class ItemDrop
    {
        /// <summary>
        /// How far off vertical a face may be and still count as somewhere to rest an object. A wall
        /// is not a shelf, and a vial balanced on a 60° chamfer would read as a bug.
        /// </summary>
        private const float MinUpwardness = 0.5f;

        /// <summary>How far below the boots to look for a floor when nothing aimed at will do.</summary>
        private const float FloorProbe = 4f;

        /// <summary>Where the fallback lands: a stride in front, so you can see what you put down.</summary>
        private const float FootReach = 0.55f;

        /// <summary>Lifts the resting point clear of the surface it was measured against.</summary>
        private const float Clearance = 0.02f;

        /// <summary>
        /// Radius of the sanity probe. Deliberately about the size of one prop rather than the size of
        /// the largest one: this exists to catch a point inside a wall or inside an instrument, not to
        /// pack a shelf, and a fussier test would refuse ordinary drops beside things already there.
        /// </summary>
        private const float FootprintRadius = 0.05f;

        /// <summary>
        /// Where the item would land, or why it cannot.
        /// <para>
        /// Two answers, in order: the surface you are looking at, then the floor at your feet. Aiming
        /// is first because it is the one the player controls — putting a vial on the bench in front of
        /// you is the common case and dropping it on the floor instead would be the game ignoring a
        /// deliberate choice.
        /// </para>
        /// <paramref name="refusal"/> is a sentence for the player and is never null on false.
        /// </summary>
        public static bool TryResolve(PlayerInteractor player, out Vector3 position, out string refusal)
        {
            position = default;
            refusal = null;

            if (player == null)
            {
                refusal = PromptStrings.DropNoPlayer.Text;
                return false;
            }

            int mask = player.Mask;

            // The ray the interactor already cast this frame, rather than one rebuilt here: a second
            // ray would aim at something subtly different from what the crosshair is on.
            var ray = player.LastRay;
            bool aimed = ray.direction.sqrMagnitude > 0.0001f;

            if (aimed &&
                Physics.Raycast(ray, out var hit, player.Range, mask, QueryTriggerInteraction.Ignore) &&
                !player.IsSelf(hit.collider) &&
                hit.normal.y >= MinUpwardness)
            {
                var rest = hit.point + hit.normal * Clearance;
                if (Fits(rest, mask))
                {
                    position = rest;
                    return true;
                }

                refusal = PromptStrings.DropNoRoom.Text;
                return false;
            }

            var feet = player.transform.position;
            var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;

            // In front of the boots first, then straight down. The second is not a duplicate: a player
            // standing with their nose to a wall has nothing in front of them and still has a floor.
            if (TryFloor(player, feet + forward * FootReach, mask, out position) ||
                TryFloor(player, feet, mask, out position))
            {
                return true;
            }

            refusal = aimed
                ? PromptStrings.DropNowhere.Text
                : PromptStrings.DropNothingUnderfoot.Text;
            return false;
        }

        private static bool TryFloor(PlayerInteractor player, Vector3 origin, int mask,
                                     out Vector3 position)
        {
            position = default;

            // Started above the boots rather than at them, so a bench the player is standing against
            // is found before the floor under it — the nearest flat surface is the one they meant.
            var from = origin + Vector3.up * 1.2f;

            if (!Physics.Raycast(from, Vector3.down, out var floor, FloorProbe, mask,
                                 QueryTriggerInteraction.Ignore)) return false;
            if (player.IsSelf(floor.collider) || floor.normal.y < MinUpwardness) return false;

            var rest = floor.point + Vector3.up * Clearance;
            if (!Fits(rest, mask)) return false;

            position = rest;
            return true;
        }

        /// <summary>
        /// Is there actually room at this point, or is it inside something? The probe sits clear of the
        /// surface the point was measured against, so the shelf itself never counts as an obstruction.
        /// </summary>
        private static bool Fits(Vector3 position, int mask) =>
            !Physics.CheckSphere(position + Vector3.up * (FootprintRadius + Clearance),
                                 FootprintRadius, mask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// Set the selected item down. Ask first, move the prop only if the answer was yes — the
        /// pattern <see cref="PlayerInteractor.Take"/> uses in the other direction.
        /// <para>
        /// The spot is not built until the host has agreed, because building one for a refused request
        /// would leave an empty transform in the room for every drop the lab turned down.
        /// </para>
        /// </summary>
        public static void Attempt(PlayerInteractor player)
        {
            if (player == null) return;

            // Unity's ==, not a null pattern: a prop destroyed out from under the hand — a slip filed
            // by somebody else, a spent vial — is a live C# reference and reads as empty hands.
            if (player.Carried == null)
            {
                player.Say(PromptStrings.DropHandsEmpty.Text);
                return;
            }

            if (!TryResolve(player, out var position, out string refusal))
            {
                player.Say(refusal);
                return;
            }

            string surfaceId = DropSpot.IdFor(position);

            // Slot -1: "this container, no particular hole". The established form for a location whose
            // slot nobody chose — a disconnected player's vial goes back to the rack the same way — and
            // the one PropSockets already knows to resolve by leaving a prop where it found it.
            LabCommands.Attempt(player, LabCommand.PutDown(surfaceId, -1), _ =>
            {
                // Whatever the host just emptied out of the selected hand, rather than what was in it
                // when the request left: on a client those are the same object unless the player
                // changed slots mid-flight, and in that case the host acted on the new selection.
                var placed = player.ReleaseCarried();
                if (placed == null) return;

                var spot = DropSpot.Resolve(surfaceId);
                if (spot == null)
                {
                    // Cannot happen for an id this method just built, but a prop left parented to a
                    // hand it is no longer in would be invisible and unreachable, so say so rather
                    // than lose it silently.
                    Debug.LogError($"[ItemDrop] Could not build a drop spot for '{surfaceId}'.", player);
                    player.TryCarry(placed);
                    return;
                }

                spot.TryPlace(placed);
                player.Say(PromptStrings.ItemSetDown.Format(("item", placed.DisplayName)), 2f);
            });
        }
    }
}
