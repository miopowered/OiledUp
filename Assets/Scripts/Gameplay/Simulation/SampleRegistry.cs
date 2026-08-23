using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The host's vault for every sample in the run.
    /// <para>
    /// <b>Ground truth never leaves this object.</b> The truth map is private and there is no
    /// accessor for it outside the editor. Anything that genuinely needs the real chemistry —
    /// running a machine, resolving a verdict — is a <i>method here</i>, so the answer is computed
    /// inside the vault and only the player-facing result comes back out.
    /// </para>
    /// This is stronger than "remember not to send it": there is no expression a caller can write
    /// that yields a <see cref="SampleGroundTruth"/> in a shipped build, so no RPC signature can
    /// accidentally close over one.
    /// </summary>
    public sealed class SampleRegistry
    {
        private readonly Dictionary<SampleId, SampleState> states = new();
        private readonly Dictionary<SampleId, SampleGroundTruth> truths = new();
        private readonly List<PendingConsequence> pending = new();

        public IReadOnlyCollection<SampleState> All => states.Values;
        public int Count => states.Count;

        /// <summary>Verdicts filed but not yet resolved, soonest first.</summary>
        public IReadOnlyList<PendingConsequence> Pending => pending;

        public void Add(GeneratedSample generated)
        {
            if (generated?.State == null || generated.Truth == null) return;
            states[generated.State.Id] = generated.State;
            truths[generated.Truth.Id] = generated.Truth;
        }

        public bool TryGet(SampleId id, out SampleState state) => states.TryGetValue(id, out state);

        public SampleState Get(SampleId id) => states.TryGetValue(id, out var s) ? s : null;

        // -- Operations that need the truth, and therefore live here ---------------------------------

        /// <summary>
        /// Run a loaded sample through an instrument. Returns null if the sample is unknown or has
        /// too little volume left. The caller receives a <see cref="TestResult"/> — measured values
        /// only, already polluted by residue, noise and drift.
        /// </summary>
        public TestResult RunMachine(SampleId id, MachineInstance machine, int day, ref Rng rng)
        {
            if (!states.TryGetValue(id, out var state)) return null;
            if (!truths.TryGetValue(id, out var truth)) return null;
            return MeasurementPipeline.Run(state, truth, machine.Runtime, day, ref rng);
        }

        /// <summary>
        /// File a verdict. The consequence is queued for <c>day + daysToFailure</c> rather than
        /// resolved now — §5.4 is explicit that the cost lands days later, which is what makes a
        /// wrong call feel like something you did rather than something the game told you.
        /// </summary>
        public bool FileVerdict(SampleId id, Verdict verdict, RootCauseDef rootCause, int day)
        {
            if (!states.TryGetValue(id, out var state)) return false;
            if (state.FiledVerdict.HasValue) return false;
            if (!truths.TryGetValue(id, out var truth)) return false;

            state.FiledVerdict = verdict;
            state.FiledRootCause = rootCause;
            state.FiledOnDay = day;
            state.Location = SampleLocation.Archived();

            // A healthy unit has no failure clock, so its verdict settles on the next day's paperwork.
            int delay = truth.PrimaryFault != null ? truth.PrimaryFault.DaysToFailure : 1;

            pending.Add(new PendingConsequence
            {
                Sample = id,
                ResolveOnDay = day + delay
            });
            pending.Sort((a, b) => a.ResolveOnDay.CompareTo(b.ResolveOnDay));
            return true;
        }

        /// <summary>
        /// Resolve everything due on or before <paramref name="day"/>. Truth is read here and
        /// discarded; callers get scored reports.
        /// </summary>
        public List<ConsequenceReport> ResolveDue(int day, EconomyTuning tuning)
        {
            var reports = new List<ConsequenceReport>();

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (p.ResolveOnDay > day) continue;

                if (states.TryGetValue(p.Sample, out var state) &&
                    truths.TryGetValue(p.Sample, out var truth))
                {
                    reports.Add(ConsequenceResolver.Resolve(state, truth, tuning));
                    state.ConsequenceResolved = true;
                }

                pending.RemoveAt(i);
            }

            reports.Reverse(); // restore chronological order after the reverse iteration
            return reports;
        }

        // -- Resampling -------------------------------------------------------------------------------

        private readonly List<SampleId> pendingRequeue = new();

        /// <summary>Mark a unit as kept in service and due for another draw next cycle.</summary>
        public void QueueRequeue(SampleId id)
        {
            if (!pendingRequeue.Contains(id)) pendingRequeue.Add(id);
        }

        /// <summary>Drain the requeue list. Returns a copy so the caller can generate while iterating.</summary>
        public List<SampleId> TakePendingRequeues()
        {
            var taken = new List<SampleId>(pendingRequeue);
            pendingRequeue.Clear();
            return taken;
        }

        /// <summary>
        /// Build the next draw from a unit the player filed MONITOR on.
        /// <para>
        /// Lives here because it needs ground truth: the same fault has to come back further along,
        /// not a fresh roll. A re-draw that re-rolled the fault would make MONITOR a coin flip
        /// rather than a decision to watch something specific.
        /// </para>
        /// </summary>
        public GeneratedSample BuildRequeue(SampleId original, SampleGenerator generator, int day, ref Rng rng)
        {
            if (generator == null) return null;
            if (!states.TryGetValue(original, out var state)) return null;
            if (!truths.TryGetValue(original, out var truth)) return null;

            var fault = truth.PrimaryFault;
            if (fault == null) return null;

            float previous = truth.FaultSeverities.Count > 0 ? truth.FaultSeverities[0] : 0.5f;

            var request = GenerationRequest.Default(state.Profile, state.EquipmentTag, day);
            request.ForcedFault = fault;
            request.ForcedSeverity01 = Mathf.Clamp01(previous + rng.Range(0.18f, 0.32f));
            request.CascadeChance = 0f;
            request.HoursSinceOilChange = state.HoursSinceOilChange + state.Profile.DefaultOilChangeHours * 0.12f;

            var generated = generator.Generate(request, ref rng);
            if (generated == null) return null;

            generated.State.ResampleOf = original;
            generated.State.FieldTechNote =
                $"Resample. Previously reported MONITOR on day {state.FiledOnDay}; unit kept in service.";
            return generated;
        }

        /// <summary>
        /// True if the sample is genuinely faulty. Exposed only so the end-of-day report can explain
        /// what was actually wrong <i>after</i> the verdict has resolved — never before.
        /// </summary>
        public bool TryDescribeResolvedFault(SampleId id, out string faultName, out RootCauseDef cause)
        {
            faultName = null;
            cause = null;

            if (!states.TryGetValue(id, out var state) || !state.ConsequenceResolved) return false;
            if (!truths.TryGetValue(id, out var truth)) return false;

            var fault = truth.PrimaryFault;
            if (fault == null) return false;

            faultName = fault.DisplayName;
            cause = fault.RootCause;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only escape hatch for the ground-truth dump tool (issue #3) and tests.
        /// Compiled out of player builds entirely, so it cannot leak at runtime.
        /// </summary>
        public SampleGroundTruth PeekTruthForDebugging(SampleId id)
            => truths.TryGetValue(id, out var t) ? t : null;
#endif
    }

    /// <summary>A verdict awaiting its day of reckoning.</summary>
    public struct PendingConsequence
    {
        public SampleId Sample;
        public int ResolveOnDay;
    }
}
