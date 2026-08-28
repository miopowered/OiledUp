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
        public bool FileVerdict(SampleId id, Verdict verdict, RootCauseDef rootCause, int day) =>
            FileVerdict(id, verdict, rootCause, day, out _);

        /// <inheritdoc cref="FileVerdict(SampleId,Verdict,RootCauseDef,int)"/>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool FileVerdict(SampleId id, Verdict verdict, RootCauseDef rootCause, int day,
                                out string refusal)
        {
            refusal = null;
            if (!states.TryGetValue(id, out var state)) { refusal = "No such sample."; return false; }
            if (!truths.TryGetValue(id, out var truth)) { refusal = "No such sample."; return false; }

            if (!SampleLifecycle.TryArchive(state, verdict, rootCause, day, out refusal)) return false;

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
        /// <param name="settleEverything">
        /// Ignore the due dates and settle the whole queue. Only for the end of a run, where there
        /// is no later day to resolve on and anything left pending would simply never be answered.
        /// </param>
        public List<ConsequenceReport> ResolveDue(int day, EconomyTuning tuning,
                                                  bool settleEverything = false)
        {
            var reports = new List<ConsequenceReport>();

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (!settleEverything && p.ResolveOnDay > day) continue;

                if (states.TryGetValue(p.Sample, out var state) &&
                    truths.TryGetValue(p.Sample, out var truth) &&
                    SampleLifecycle.TryResolve(state, out _))
                {
                    reports.Add(ConsequenceResolver.Resolve(state, truth, tuning));
                }

                pending.RemoveAt(i);
            }

            reports.Reverse(); // restore chronological order after the reverse iteration
            return reports;
        }

        // -- Calibration drift (§5.3) ------------------------------------------------------------------

        /// <summary>
        /// Mark every run this instrument produced inside the drift episode that has just been
        /// revealed, and count what it reaches.
        /// <para>
        /// Lives here because the registry owns the records. Nothing about the suspicion needs ground
        /// truth — it is entirely a statement about the instrument — but the write lands on filed,
        /// player-facing results, and those have exactly one owner.
        /// </para>
        /// <b>Call this before <see cref="MachineRuntimeState.Calibrate"/>.</b> Calibrating moves the
        /// start of the drift window to now, which would empty the very window this is built from.
        /// </summary>
        public CalibrationOutcome FlagDriftSuspects(MachineRuntimeState machine, float revealedDrift, int day)
        {
            int flagged = MeasurementPipeline.FlagSuspectResults(states.Values, machine, revealedDrift);

            int touched = 0;
            int archived = 0;

            if (flagged > 0)
            {
                foreach (var state in states.Values)
                {
                    if (!RanInsideDriftWindow(state, machine)) continue;
                    touched++;
                    if (state.FiledVerdict.HasValue) archived++;
                }
            }

            return new CalibrationOutcome(day, revealedDrift, flagged, touched, archived);
        }

        private static bool RanInsideDriftWindow(SampleState state, MachineRuntimeState machine)
        {
            foreach (var result in state.Results)
            {
                if (result.MachineId != machine.Def.Id) continue;
                if (result.MachineRunIndex < machine.DriftStartedAtRunIndex) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Records with a verdict on file and at least one reading an instrument took while it was
        /// drifting. This is §5.3's "show the affected archived samples" — the list the player has to
        /// look at after finding out the machine was wrong.
        /// </summary>
        public List<SampleState> SuspectArchive()
        {
            var suspect = new List<SampleState>();

            foreach (var state in states.Values)
            {
                if (state.Stage != SampleStage.Archived) continue;

                foreach (var result in state.Results)
                {
                    if (!result.Suspect) continue;
                    suspect.Add(state);
                    break;
                }
            }

            suspect.Sort((a, b) => a.Id.CompareTo(b.Id));
            return suspect;
        }

        /// <summary>
        /// Withdraw a verdict filed on numbers the instrument got wrong, so the sample can be run
        /// again (§5.3).
        /// <para>
        /// <paramref name="requiredVolumeMl"/> is the smallest draw that could repeat one of the
        /// suspect tests. Refusing when the vial cannot cover it is the mechanic rather than an edge
        /// case: the oil is gone, the reading can never be checked, and the player is left holding a
        /// verdict they now know was filed on a lie. §5.3 calls that escalating pressure. Softening
        /// it into "re-open anyway" would make volume free, and volume is the resource the whole §4.5
        /// test-ordering decision is about.
        /// </para>
        /// The queued consequence is cancelled along with the verdict. A record that could be re-filed
        /// while its first call was still on the way to landing would pay out twice.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool ReopenForRetest(SampleId id, float requiredVolumeMl, out string refusal)
        {
            refusal = null;
            if (!states.TryGetValue(id, out var state)) { refusal = "No such sample."; return false; }

            if (state.Stage == SampleStage.Archived)
            {
                if (float.IsInfinity(requiredVolumeMl))
                {
                    refusal = $"{state.RecordTag}: no instrument on the bench can repeat the suspect tests.";
                    return false;
                }

                if (state.VolumeMl < requiredVolumeMl)
                {
                    refusal =
                        $"{state.RecordTag} has {state.VolumeMl:F1} ml left and the cheapest suspect " +
                        $"test needs {requiredVolumeMl:F0} ml. There is nothing left to check it with — " +
                        "the verdict stands on numbers you now know were wrong.";
                    return false;
                }
            }

            if (!SampleLifecycle.TryReopen(state, out refusal)) return false;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Sample == id) pending.RemoveAt(i);
            }

            return true;
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

        // -- Saving and loading (#49) ------------------------------------------------------------------
        //
        // The vault serialises itself. Nothing here hands a SampleGroundTruth out or takes one in:
        // the record types crossing this boundary are internal to Residue.Gameplay, and the
        // conversion happens on this side of the wall in both directions. That keeps the promise at
        // the top of this file intact — there is still no expression a caller outside this assembly
        // can write that yields a truth object.

        /// <summary>Flatten every sample's truth into records. See the note above.</summary>
        internal void CaptureTruths(List<RunSnapshot.TruthRecord> into)
        {
            if (into == null) return;

            foreach (var pair in truths)
            {
                var truth = pair.Value;
                var record = new RunSnapshot.TruthRecord { Id = truth.Id.Value };

                foreach (var fault in truth.ActualFaults) record.FaultIds.Add(fault != null ? fault.Id : null);
                foreach (var severity in truth.FaultSeverities) record.Severities.Add(severity);

                foreach (var kv in truth.TrueValues)
                    record.TrueValues.Add(new RunSnapshot.Reading { ElementId = kv.Key, Value = kv.Value });

                foreach (var kv in truth.Contamination)
                    record.Contamination.Add(new RunSnapshot.Reading { ElementId = kv.Key, Value = kv.Value });

                into.Add(record);
            }

            into.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        /// <summary>
        /// Put one saved sample back in the vault, truth and all.
        /// <para>
        /// The faults arrive already resolved against the live catalog, because refusing an id that
        /// no longer exists is the loader's job and it has to refuse the <i>whole</i> run rather than
        /// this one sample — a sample whose fault silently vanished reads as healthy, and would
        /// resolve as "no fault found" against true values that still carry the signature. That is
        /// the chemistry lying, which is the one thing it may never do.
        /// </para>
        /// </summary>
        internal void RestoreSample(SampleState state, IReadOnlyList<FaultDef> faults,
                                    IReadOnlyList<float> severities,
                                    IReadOnlyList<RunSnapshot.Reading> trueValues,
                                    IReadOnlyList<RunSnapshot.Reading> contamination)
        {
            if (state == null) return;

            var truth = new SampleGroundTruth { Id = state.Id };

            if (faults != null)
            {
                for (int i = 0; i < faults.Count; i++)
                {
                    if (faults[i] == null) continue;
                    truth.ActualFaults.Add(faults[i]);
                    truth.FaultSeverities.Add(severities != null && i < severities.Count ? severities[i] : 0.5f);
                }
            }

            if (trueValues != null)
            {
                foreach (var reading in trueValues) truth.TrueValues[reading.ElementId] = reading.Value;
            }

            if (contamination != null)
            {
                foreach (var reading in contamination) truth.Contamination[reading.ElementId] = reading.Value;
            }

            states[state.Id] = state;
            truths[state.Id] = truth;
        }

        /// <summary>Re-queue a verdict that had not come due when the run was saved.</summary>
        internal void RestorePending(SampleId sample, int resolveOnDay)
        {
            pending.Add(new PendingConsequence { Sample = sample, ResolveOnDay = resolveOnDay });
            pending.Sort((a, b) => a.ResolveOnDay.CompareTo(b.ResolveOnDay));
        }

        /// <summary>Units the player filed MONITOR on, waiting for the next <c>BeginDay</c> to re-draw.</summary>
        internal IReadOnlyList<SampleId> PendingRequeues => pendingRequeue;

        /// <inheritdoc cref="PendingRequeues"/>
        internal void RestoreRequeue(SampleId sample) => QueueRequeue(sample);

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
