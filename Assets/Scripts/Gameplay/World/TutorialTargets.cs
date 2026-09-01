using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Turns "which objective is next" into "which thing in this room". The whole of the tutorial's
    /// in-world pointing is decided here; <see cref="TutorialMarker"/> and
    /// <see cref="TutorialCompass"/> only draw what this hands them.
    ///
    /// <para>
    /// <b>It marks fixtures, never answers (hard rule 1).</b> Every case below picks by
    /// <i>kind</i> — an instrument, the terminal, a carton, the tap — and, where several of a kind
    /// exist, by distance or by publicly visible occupancy (is this instrument empty, is it busy),
    /// which is what the status light and the screen beside it already say out loud. Nothing here can
    /// see a <c>SampleState</c>, a reading or a severity, and none of the collectors below is allowed
    /// to grow a reason like "the one that needs the standard". A marker that singled a bottle out on
    /// its chemistry would teach a player to follow the arrow instead of learning the room, which is
    /// the exact failure hard rule 1 exists to prevent.
    /// </para>
    ///
    /// <para>
    /// <b>It never marks what is already in your hands.</b> An arrow hanging in front of the camera
    /// is noise, and it would be pointing at something the player is demonstrably already holding.
    /// Where the natural target <i>is</i> the held item, the pointer moves to where that item has to
    /// go — the terminal for a printed slip, the tap for an empty bottle — which is the half of the
    /// instruction the player still needs.
    /// </para>
    ///
    /// <para>
    /// <b>Stable rather than nearest.</b> Nearest is only how a target is <i>chosen</i>. Once chosen
    /// it is kept for as long as it is still a candidate for the same step, so walking across the
    /// room does not make the arrow hop between two instruments a metre apart from each other. A
    /// pointer that changes its mind while you walk towards it is worse than one that is slightly
    /// suboptimal.
    /// </para>
    ///
    /// <para>
    /// Resolved against the live scene rather than a list baked at build time: <c>LabSceneBuilder</c>
    /// regenerates the room, and cartons, vials, slips and bottles are all spawned during the day.
    /// The scan is throttled to <see cref="RescanSeconds"/> because a step's answer changes on the
    /// scale of a player walking somewhere, not on the scale of a frame.
    /// </para>
    /// </summary>
    public sealed class TutorialTargets
    {
        /// <summary>
        /// How often the room is re-examined. Half a second is under the reaction time of anybody
        /// walking towards an arrow and two orders of magnitude cheaper than doing it per frame.
        /// </summary>
        public const float RescanSeconds = 0.5f;

        /// <summary>Metres above a fixture's own geometry. Clears eye height on a full-size machine.</summary>
        public const float FixtureClearance = 0.5f;

        /// <summary>Metres above a carried-size prop sitting on a bench.</summary>
        public const float PropClearance = 0.28f;

        /// <summary>Metres above a button on an instrument's fascia. Close, so it reads as "this one".</summary>
        public const float ButtonClearance = 0.16f;

        // The room, as of the last scan. Arrays rather than a query per frame; see RescanSeconds.
        private MachineStation[] machines = System.Array.Empty<MachineStation>();
        private MachineActionButton[] buttons = System.Array.Empty<MachineActionButton>();
        private TerminalStation[] terminals = System.Array.Empty<TerminalStation>();
        private WashStation[] washStations = System.Array.Empty<WashStation>();
        private SolventValve[] valves = System.Array.Empty<SolventValve>();
        private DeliveryBayStation[] bays = System.Array.Empty<DeliveryBayStation>();
        private CartonProp[] cartons = System.Array.Empty<CartonProp>();
        private VialProp[] vials = System.Array.Empty<VialProp>();
        private PrintoutProp[] slips = System.Array.Empty<PrintoutProp>();
        private SolventBottle[] bottles = System.Array.Empty<SolventBottle>();

        private readonly List<Component> candidates = new();

        private float nextScan;
        private bool scanned;

        private TutorialStep stickyStep = TutorialStep.None;
        private Component sticky;

        /// <summary>
        /// Look at the room again on the next resolve, whatever the clock says. The scan is on a
        /// timer, and a caller that has just built or torn down part of the lab — a test, or a day
        /// rolling over — should not have to wait out the interval to be told the truth.
        /// </summary>
        public void Rescan() => scanned = false;

        /// <summary>
        /// What the tutorial is pointing at right now, or <see cref="TutorialTarget.None"/>.
        /// <para>
        /// Reads <see cref="TutorialObjectives.Current"/> itself, and answers nothing at all when it
        /// is null — which is every run that is not the tutorial. That check lives here rather than in
        /// each drawer so there is one place for it to be true, and one place for a test to point at.
        /// </para>
        /// </summary>
        public TutorialTarget ResolveCurrent(Vector3 from, PlayerInventory hands)
        {
            var objectives = TutorialObjectives.Current;
            if (objectives == null)
            {
                Forget();
                return TutorialTarget.None;
            }

            return Resolve(objectives.Next, from, hands);
        }

        /// <summary>
        /// The thing this step points at. <paramref name="hands"/> may be null — a player whose
        /// inventory has not been wired yet simply counts as holding nothing.
        /// </summary>
        public TutorialTarget Resolve(TutorialStep step, Vector3 from, PlayerInventory hands)
        {
            if (step == TutorialStep.None)
            {
                Forget();
                return TutorialTarget.None;
            }

            if (step != stickyStep)
            {
                stickyStep = step;
                sticky = null;
            }

            // Re-examined when the clock says so, and also whenever there is no standing answer —
            // otherwise a step whose target has just been destroyed would point at nothing for up to
            // RescanSeconds while the replacement sat in the room unnoticed.
            //
            // Picking the marked thing up is the third case, and it does not wait for the timer: the
            // prop is parented to the hand socket the instant it is taken, so half a second of stale
            // answer is half a second of arrow hanging in the player's face.
            bool due = !scanned || sticky == null || Time.unscaledTime >= nextScan ||
                       (sticky is Carryable held && Held(held, hands));

            if (due) Repick(step, from, hands);

            if (sticky == null) return TutorialTarget.None;
            return new TutorialTarget(sticky.transform, ClearanceFor(sticky));
        }

        private void Forget()
        {
            stickyStep = TutorialStep.None;
            sticky = null;
        }

        private void Repick(TutorialStep step, Vector3 from, PlayerInventory hands)
        {
            Scan();

            candidates.Clear();
            Collect(step, hands, candidates);

            if (candidates.Count == 0)
            {
                sticky = null;
                return;
            }

            // The standing answer wins whenever it is still one of the answers. Distance only ever
            // breaks a tie that has no incumbent.
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == sticky) return;
            }

            sticky = Nearest(candidates, from);
        }

        private void Scan()
        {
            if (scanned && Time.unscaledTime < nextScan) return;

            scanned = true;
            nextScan = Time.unscaledTime + RescanSeconds;

            machines = All<MachineStation>();
            buttons = All<MachineActionButton>();
            terminals = All<TerminalStation>();
            washStations = All<WashStation>();
            valves = All<SolventValve>();
            bays = All<DeliveryBayStation>();
            cartons = All<CartonProp>();
            vials = All<VialProp>();
            slips = All<PrintoutProp>();
            bottles = All<SolventBottle>();
        }

        /// <summary>
        /// Everything of this kind that is switched on. Inactive excluded because a switched-off
        /// fixture is one this player cannot walk up to, and a replica's props are switched off with
        /// the rest of that avatar.
        /// <para>
        /// The order Unity returns is unspecified and may differ between two scans of an unchanged
        /// room, which is exactly why the pick above is sticky rather than recomputed: an arrow that
        /// followed the array would swap between two equidistant instruments for no reason a player
        /// could see.
        /// </para>
        /// </summary>
        private static T[] All<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);

        // -- What each step points at ------------------------------------------------------------------
        //
        // Read this as the spec it is. Each case falls back down a chain that ends at a fixture which
        // is always in the room, so a step whose ideal target has not been spawned yet still points
        // somewhere honest — "the bay", "the wash station" — rather than going quiet and leaving a
        // first-time player with a card and no room.

        private void Collect(TutorialStep step, PlayerInventory hands, List<Component> into)
        {
            switch (step)
            {
                // A box on the floor of the bay. If the truck has not been in yet, the bay itself,
                // which is where it will be.
                case TutorialStep.TakeACarton:
                    AddLoose(cartons, hands, into);
                    if (into.Count == 0) AddAll(bays, into);
                    return;

                // The tape is cut on the box, and a box in your hands has its colliders off — so this
                // is always a carton standing on something, never the one being carried.
                case TutorialStep.OpenTheCarton:
                    AddLooseCartons(hands, into, CartonState.Sealed);
                    if (into.Count == 0) AddLoose(cartons, hands, into);
                    if (into.Count == 0) AddAll(bays, into);
                    return;

                // A bottle standing in the open, else the open box it is still in.
                case TutorialStep.TakeAVial:
                    AddLoose(vials, hands, into);
                    if (into.Count == 0) AddLooseCartons(hands, into, CartonState.Open);
                    if (into.Count == 0) AddLoose(cartons, hands, into);
                    return;

                // An instrument with nothing in it. Occupancy, not chemistry: which instrument is free
                // is on its own status light, and which test a sample needs stays the player's problem.
                case TutorialStep.LoadAnInstrument:
                    AddMachines(into, MachinePreference.Free);
                    return;

                // The instrument you just loaded. Run is a press on the machine itself, not on one of
                // the four action buttons, so the station is the thing to stand in front of.
                case TutorialStep.StartTheRun:
                    AddMachines(into, MachinePreference.Loaded);
                    return;

                // Nothing to do here, which is the lesson — so it points at the instrument that is
                // busy rather than at a button, and the player learns the day runs without them.
                case TutorialStep.LetARunFinish:
                    AddMachines(into, MachinePreference.Busy);
                    return;

                // The paper first, the desk once the paper is in hand.
                case TutorialStep.FileTheSlip:
                    if (!Holding<PrintoutProp>(hands)) AddLoose(slips, hands, into);
                    if (into.Count == 0) AddAll(terminals, into);
                    return;

                case TutorialStep.FileAVerdict:
                case TutorialStep.EndTheDay:
                    AddAll(terminals, into);
                    return;

                // The tap once you have something to fill, a bottle to pick up before that, and the
                // wash station itself if every bottle is elsewhere.
                case TutorialStep.FillABottle:
                    if (Holding<SolventBottle>(hands)) { AddAll(valves, into); return; }
                    AddLoose(bottles, hands, into);
                    if (into.Count == 0) AddAll(washStations, into);
                    return;

                case TutorialStep.RunABlank:
                    AddButtons(MachineAction.Blank, into);
                    return;

                case TutorialStep.FlushAnInstrument:
                    AddButtons(MachineAction.Clean, into);
                    return;

                case TutorialStep.RunAStandard:
                    AddButtons(MachineAction.Reference, into);
                    return;

                case TutorialStep.Recalibrate:
                    AddButtons(MachineAction.Calibrate, into);
                    return;
            }
        }

        private enum CartonState { Sealed, Open }

        /// <summary>
        /// Which instrument, when there are five of them. All three preferences are facts the
        /// instrument already broadcasts on its own status light and screen — dark, bright-and-still,
        /// pulsing — so nothing is revealed here that a player standing in the room cannot see.
        /// </summary>
        private enum MachinePreference { Free, Loaded, Busy }

        private void AddMachines(List<Component> into, MachinePreference prefer)
        {
            for (int i = 0; i < machines.Length; i++)
            {
                var station = machines[i];
                if (station == null) continue;

                var machine = station.Machine;
                if (machine == null) continue;

                bool wanted = prefer switch
                {
                    MachinePreference.Free => machine.IsEmpty && !machine.IsRunning,
                    MachinePreference.Loaded => !machine.IsEmpty && !machine.IsRunning &&
                                                !machine.HasResultWaiting,
                    _ => machine.IsRunning
                };

                if (wanted) into.Add(station);
            }

            // Nothing matched — either every instrument is busy, or this process has no view of them
            // yet. Point at one anyway: "an instrument is over there" is still the instruction.
            if (into.Count == 0) AddAll(machines, into);
        }

        private void AddButtons(MachineAction action, List<Component> into)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].Action == action) into.Add(buttons[i]);
            }

            if (into.Count == 0) AddAll(machines, into);
        }

        private static void AddAll<T>(T[] source, List<Component> into) where T : Component
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null) into.Add(source[i]);
            }
        }

        /// <summary>
        /// Everything of this kind that is standing in the room rather than in somebody's hands.
        /// See the class remarks: the arrow never points at what the player is already carrying.
        /// </summary>
        private static void AddLoose<T>(T[] source, PlayerInventory hands, List<Component> into)
            where T : Carryable
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null || Held(source[i], hands)) continue;
                into.Add(source[i]);
            }
        }

        /// <summary>
        /// Boxes in the room that are in the state asked for. A box this process has no placement
        /// record for matches neither state — so it is never <i>preferred</i>, and the caller's plain
        /// <see cref="AddLoose{T}"/> fallback is what picks it up.
        /// </summary>
        private void AddLooseCartons(PlayerInventory hands, List<Component> into, CartonState state)
        {
            for (int i = 0; i < cartons.Length; i++)
            {
                var carton = cartons[i];
                if (carton == null || Held(carton, hands)) continue;
                if (!carton.TryState(out var box)) continue;
                if (box.IsSealed != (state == CartonState.Sealed)) continue;

                into.Add(carton);
            }
        }

        private static bool Held(Carryable prop, PlayerInventory hands)
        {
            if (hands == null) return false;

            var slots = hands.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == prop) return true;
            }
            return false;
        }

        private static bool Holding<T>(PlayerInventory hands) where T : Carryable
        {
            if (hands == null) return false;

            var slots = hands.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] is T) return true;
            }
            return false;
        }

        private static Component Nearest(List<Component> from, Vector3 to)
        {
            Component best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < from.Count; i++)
            {
                float sqr = (from[i].transform.position - to).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                best = from[i];
            }

            return best;
        }

        private static float ClearanceFor(Component target) => target switch
        {
            MachineActionButton _ => ButtonClearance,
            Carryable _ => PropClearance,
            _ => FixtureClearance
        };
    }
}
