using System.Collections.Generic;
using Residue.Data;
using UnityEngine;

namespace Residue.Chemistry
{
    /// <summary>
    /// Turns ground truth into a number the player sees (§5.2). Everything that makes the reading
    /// wrong lives here: residue carried over from the previous sample, contamination on the vial
    /// itself, instrument noise, and calibration drift.
    /// <para>
    /// <b>Server only.</b> A client must never call this — see §3.1, "never let a client compute a test result".
    /// </para>
    /// </summary>
    public static class MeasurementPipeline
    {
        /// <summary>
        /// Run a real sample. Consumes volume, charges consumables, and leaves residue behind.
        /// Returns null if the sample lacks the volume for this instrument.
        /// </summary>
        public static TestResult Run(
            SampleState state,
            SampleGroundTruth truth,
            MachineRuntimeState machine,
            int day,
            ref Rng rng)
        {
            var def = machine.Def;
            if (state == null || truth == null || def == null) return null;
            if (!state.HasVolumeFor(def)) return null;

            var result = new TestResult
            {
                MachineId = def.Id,
                DayRun = day,
                MachineRunIndex = machine.RunIndex,
                VolumeConsumedMl = def.SampleVolumeMl,
                Cost = def.CostPerRun
            };

            foreach (var element in def.Measures)
            {
                if (element == null) continue;

                // The gear-spalling trap (§4.3): the debris is in the vial, but the plasma cannot
                // see particles that large. The element is simply absent from the report.
                if (def.IsBlindTo(element.Id)) continue;

                float presented = truth.GetPresented(element.Id);
                float carried = machine.GetResidue(element.Id) * MachineRuntimeState.ResidueTransferRate;
                float raw = presented + carried;

                float noise = rng.NextGaussian(0f, Mathf.Abs(raw) * def.BaseNoisePercent);
                float measured = (raw + noise) * (1f + machine.DriftPercent);

                result.Values[element.Id] = Mathf.Max(0f, measured);
            }

            state.VolumeMl = Mathf.Max(0f, state.VolumeMl - def.SampleVolumeMl);

            // Deliberately NOT added to state.Results. The instrument produces a reading; it does not
            // file it. A result reaches the sample's history only when the player carries the
            // printout to the terminal, which is what stops the numbers teleporting across the room
            // and makes the lab a place rather than a menu.
            DepositResidue(truth, machine);
            machine.RegisterRun();

            return result;
        }

        /// <summary>
        /// Push a solvent blank through the instrument. Reads residue directly, so a careful player
        /// can prove the machine is clean before trusting a borderline result. Costs machine time and
        /// consumables but no sample volume — and deliberately does NOT clean the machine.
        /// </summary>
        public static TestResult RunBlank(MachineRuntimeState machine, int day, ref Rng rng)
        {
            var def = machine.Def;
            if (def == null) return null;

            var result = new TestResult
            {
                MachineId = def.Id,
                DayRun = day,
                MachineRunIndex = machine.RunIndex,
                VolumeConsumedMl = 0f,
                Cost = def.CostPerRun,
                IsBlank = true
            };

            foreach (var element in def.Measures)
            {
                if (element == null || def.IsBlindTo(element.Id)) continue;

                float carried = machine.GetResidue(element.Id) * MachineRuntimeState.ResidueTransferRate;
                float noise = rng.NextGaussian(0f, Mathf.Abs(carried) * def.BaseNoisePercent);
                result.Values[element.Id] = Mathf.Max(0f, (carried + noise) * (1f + machine.DriftPercent));
            }

            machine.RegisterRun();
            return result;
        }

        /// <summary>
        /// Push a certified reference standard through the instrument (§5.3). The values going in are
        /// known, so whatever comes back out is the instrument's error.
        /// <para>
        /// Takes exactly the same path as a real sample — residue, noise and drift all apply. A
        /// standard that dodged them would measure nothing an actual run suffers from, and the fact
        /// that an unflushed instrument fails its own check is the §5.2 blank earning its keep rather
        /// than a flaw here.
        /// </para>
        /// Consumes no sample volume: the ampoule is the consumable, and the caller charges it.
        /// </summary>
        public static TestResult RunReference(
            ReferenceStandard standard,
            MachineRuntimeState machine,
            int day,
            ref Rng rng)
        {
            var def = machine?.Def;
            if (standard == null || def == null) return null;

            var result = new TestResult
            {
                MachineId = def.Id,
                DayRun = day,
                MachineRunIndex = machine.RunIndex,
                VolumeConsumedMl = 0f,
                Cost = def.CostPerRun,
                IsReference = true
            };

            foreach (var element in def.Measures)
            {
                if (element == null || def.IsBlindTo(element.Id)) continue;
                if (!standard.TryGet(element.Id, out float certified)) continue;

                float carried = machine.GetResidue(element.Id) * MachineRuntimeState.ResidueTransferRate;
                float raw = certified + carried;

                float noise = rng.NextGaussian(0f, Mathf.Abs(raw) * def.BaseNoisePercent);
                result.Values[element.Id] = Mathf.Max(0f, (raw + noise) * (1f + machine.DriftPercent));
            }

            DepositStandardResidue(standard, machine);
            machine.RegisterRun();

            return result;
        }

        /// <summary>
        /// Everything that went through the machine leaves a fraction behind — including elements the
        /// instrument cannot measure, because physical residue does not care what the detector can see.
        /// </summary>
        private static void DepositResidue(SampleGroundTruth truth, MachineRuntimeState machine)
        {
            float carryover = machine.Def.ContaminationCarryoverPercent;
            if (carryover <= 0f) return;

            foreach (var kv in truth.TrueValues)
            {
                float deposited = (kv.Value + truth.GetContamination(kv.Key)) * carryover;
                if (deposited <= 0f) continue;
                machine.Residue.TryGetValue(kv.Key, out float existing);
                machine.Residue[kv.Key] = existing + deposited;
            }
        }

        /// <summary>
        /// A standard is an oil too, so it leaves its own trace behind. Certified values are healthy
        /// baselines, so this is a small deposit — but it is why a check run is followed by a flush.
        /// </summary>
        private static void DepositStandardResidue(ReferenceStandard standard, MachineRuntimeState machine)
        {
            float carryover = machine.Def.ContaminationCarryoverPercent;
            if (carryover <= 0f) return;

            foreach (var kv in standard.Certified)
            {
                float deposited = kv.Value * carryover;
                if (deposited <= 0f) continue;
                machine.Residue.TryGetValue(kv.Key, out float existing);
                machine.Residue[kv.Key] = existing + deposited;
            }
        }

        /// <summary>
        /// Mark every result a machine produced during its current drift episode as suspect.
        /// Called after a reference sample reveals drift, to build the "re-open these" list (§5.3).
        /// </summary>
        public static int FlagSuspectResults(
            IEnumerable<SampleState> samples,
            MachineRuntimeState machine,
            float revealedDrift,
            float suspicionThreshold = CalibrationCheck.Tolerance)
        {
            if (Mathf.Abs(revealedDrift) < suspicionThreshold) return 0;

            int flagged = 0;
            foreach (var sample in samples)
            {
                foreach (var r in sample.Results)
                {
                    if (r.MachineId != machine.Def.Id) continue;
                    if (r.MachineRunIndex < machine.DriftStartedAtRunIndex) continue;
                    if (r.Suspect) continue;
                    r.Suspect = true;
                    flagged++;
                }
            }
            return flagged;
        }
    }
}
