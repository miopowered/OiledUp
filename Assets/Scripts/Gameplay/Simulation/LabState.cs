using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The whole run, host-side: the day clock, the sample vault, the installed instruments and the
    /// books. Everything a client is allowed to see hangs off here; everything it is not stays
    /// inside <see cref="SampleRegistry"/>.
    /// <para>
    /// Deliberately a plain C# class with no MonoBehaviour and no Unity lifecycle, so the entire
    /// game loop can be stepped in a unit test — and so it becomes the host's owned state at M4
    /// without a rewrite.
    /// </para>
    /// </summary>
    public sealed class LabState
    {
        public int Day { get; private set; }
        public float DaySecondsRemaining { get; private set; }
        public bool DayInProgress { get; private set; }

        public Economy Economy { get; }
        public SampleRegistry Samples { get; } = new();

        /// <summary>
        /// The wash station's drum and the bottles drawing on it (§5.2, §5.5). Separate from
        /// <see cref="Economy"/> because the drum is money and the bottles are geography: the economy
        /// answers "can the lab afford another flush", this answers "is there one within reach of the
        /// instrument you are standing at".
        /// </summary>
        public SolventStore Solvent { get; }

        /// <summary>
        /// Slips printed and not yet filed. Lives on the lab rather than on the instrument that
        /// printed it because a slip outlives the tray it landed in — see <see cref="ResultSlips"/>.
        /// </summary>
        public ResultSlips Slips { get; } = new();

        /// <summary>
        /// The loading bay, the truck and the cartons still standing in it (#30). Separate from
        /// <see cref="Samples"/> because the vault answers "what oil exists" and this answers "what of
        /// it is within reach yet" — a distinction that did not exist while the day's vials simply
        /// appeared in a crate at 09:00.
        /// </summary>
        public DeliveryBay Deliveries { get; }

        public List<MachineInstance> Machines { get; } = new();
        public EconomyTuning Tuning { get; }
        public ContractPlan Plan { get; }
        public ContentCatalog Content { get; }

        /// <summary>
        /// The house certified reference material (§5.3). One blend serves every instrument, and its
        /// values are derived from the same published baselines the manual prints — the player can
        /// look up every figure on the certificate.
        /// </summary>
        public ReferenceStandard Standard { get; }

        /// <summary>Reports from the most recent day end, for the summary screen.</summary>
        public IReadOnlyList<ConsequenceReport> LastReports => lastReports;

        public event Action<MachineInstance, TestResult> RunCompleted;
        public event Action<int> DayStarted;
        public event Action<IReadOnlyList<ConsequenceReport>> DayEnded;

        /// <summary>An instrument has been recalibrated and the archive behind it re-scored (§5.3).</summary>
        public event Action<MachineInstance, CalibrationOutcome> Calibrated;

        /// <summary>
        /// The truck is a short way out (#30). Announced early on purpose: the acceptance criterion is
        /// that the player can <i>choose</i> to finish a run first, and a choice needs notice.
        /// </summary>
        public event Action<DeliveryBay> DeliveryDue;

        /// <summary>Cartons have been set down in the bay and can now be carried in.</summary>
        public event Action<IReadOnlyList<Carton>> DeliveryArrived;

        /// <summary>The bay is full and the rest of the load is staying on the truck.</summary>
        public event Action<DeliveryBay> DeliveryHeld;

        private Rng rng;
        private SampleGenerator generator;
        private readonly Dictionary<string, EquipmentProfileDef> profilesById = new();
        private readonly List<ConsequenceReport> lastReports = new();

        public LabState(ContentCatalog content, ContractPlan plan, int seed, EconomyTuning tuning = null)
        {
            Content = content;
            Plan = plan ?? ContractPlan.Default();
            Tuning = tuning ?? new EconomyTuning();
            Economy = new Economy(Tuning);
            Solvent = new SolventStore(Economy);
            Deliveries = new DeliveryBay(Samples);

            Seed = seed;
            rng = new Rng(seed);
            generator = new SampleGenerator(content.Faults);
            Standard = ReferenceStandard.FromProfiles(content.Profiles);

            foreach (var p in content.Profiles)
            {
                if (p != null) profilesById[p.Id] = p;
            }
        }

        /// <summary>
        /// Multiplier applied to every instrument's run time. 1 is the shipping balance.
        /// Anything else is a testing convenience — see <see cref="MachineInstance.TimeScale"/>.
        /// </summary>
        public float MachineTimeScale
        {
            get => machineTimeScale;
            set
            {
                machineTimeScale = Mathf.Max(0.001f, value);
                foreach (var m in Machines) m.TimeScale = machineTimeScale;
            }
        }

        private float machineTimeScale = 1f;

        /// <summary>Install one instrument. The MVP lab is fixed; §5.5 layout mode replaces this at M5.</summary>
        public MachineInstance Install(MachineDef def, string instanceId = null)
        {
            if (def == null) return null;
            var instance = new MachineInstance(instanceId ?? $"{def.Id}-{Machines.Count}", def)
            {
                TimeScale = machineTimeScale
            };
            Machines.Add(instance);
            return instance;
        }

        public MachineInstance FindMachine(string instanceId)
        {
            foreach (var m in Machines)
            {
                if (m.InstanceId == instanceId) return m;
            }
            return null;
        }

        // -- Day cycle ------------------------------------------------------------------------------

        /// <summary>
        /// The working day has run out. Instruments refuse to <i>start</i> new runs; anything
        /// already running finishes. Samples you did not get to are still your problem — that is
        /// the §6.1 pressure of the queue outpacing your hands.
        /// </summary>
        public bool ShiftOver => DayInProgress && DaySecondsRemaining <= 0f;

        /// <summary>All contracted days are done. Checked after the day ends, not after the next begins.</summary>
        public bool ContractComplete => !DayInProgress && Day >= Plan.Length;

        /// <summary>Run is over: the contract finished, or the money ran out (§1.2).</summary>
        public bool IsRunOver => Economy.IsBankrupt || ContractComplete;

        /// <summary>Starts the next day. Returns false when the run is over, so callers cannot run past the contract.</summary>
        public bool BeginDay()
        {
            if (IsRunOver) return false;

            Day++;
            var plan = Plan.ForDay(Day);
            DaySecondsRemaining = plan.DaySeconds;
            DayInProgress = true;

            // Drift walks a fresh direction each day, so a machine is never predictably biased (§5.3).
            foreach (var m in Machines) m.Runtime.BeginDay(ref rng);

            GenerateArrivals(plan);
            GenerateRequeues();

            // Last, because a re-draw travels in the same box as the rest of that firm's day and a
            // note is only wrong once every vial that will be in the carton is in it (#32).
            SealDeliveries();

            // The paperwork exists from 09:00; the boxes do not. Everything generated above is on a
            // truck until DeliveryBay puts it down — see the type doc there for why the chemistry is
            // still minted at day start while the delivery is not.
            Deliveries.ScheduleArrival(plan.DaySeconds);

            DayStarted?.Invoke(Day);
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (!DayInProgress) return;

            DaySecondsRemaining = Mathf.Max(0f, DaySecondsRemaining - deltaSeconds);

            switch (Deliveries.Tick(deltaSeconds))
            {
                case DeliveryEvent.DueSoon: DeliveryDue?.Invoke(Deliveries); break;
                case DeliveryEvent.Arrived: DeliveryArrived?.Invoke(Deliveries.JustArrived); break;
                case DeliveryEvent.Held: DeliveryHeld?.Invoke(Deliveries); break;
            }

            foreach (var machine in Machines)
            {
                var finished = machine.Tick(deltaSeconds);
                if (finished == RunKind.None) continue;

                if (finished == RunKind.Calibration) { CompleteCalibration(machine); continue; }

                TestResult result = finished switch
                {
                    RunKind.Blank => MeasurementPipeline.RunBlank(machine.Runtime, Day, ref rng),
                    RunKind.Reference => MeasurementPipeline.RunReference(Standard, machine.Runtime, Day, ref rng),
                    _ => Samples.RunMachine(machine.LoadedSample, machine, Day, ref rng)
                };

                if (result == null) continue;

                machine.LastResult = result;
                if (finished == RunKind.Blank)
                {
                    machine.LastBlank = result;
                    machine.LastBlankDay = Day;
                }
                else if (finished == RunKind.Reference)
                {
                    machine.LastCheck = CalibrationCheck.From(Standard, result, machine.Def, Day);

                    // Kept beside the check for the save layer — see MachineInstance.LastCheckRun.
                    machine.LastCheckRun = result;
                }

                Economy.Charge(result.Cost);
                RunCompleted?.Invoke(machine, result);
            }
        }

        // -- Calibration (§5.3) -----------------------------------------------------------------------

        /// <summary>
        /// Push a certified standard through an instrument. Spends an ampoule up front, then behaves
        /// like any other run — including leaving residue, which is why a check is followed by a flush.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryStartReferenceRun(MachineInstance machine, out string refusal)
        {
            refusal = null;
            if (machine == null) { refusal = "No such instrument."; return false; }

            if (machine.IsRunning) { refusal = $"{machine.Def.DisplayName} is busy."; return false; }
            if (!machine.IsEmpty) { refusal = "Take the vial out before running a standard."; return false; }
            if (ShiftOver) { refusal = "Shift over — no new runs."; return false; }

            if (Economy.ReferenceStandards < 1)
            {
                refusal = "No certified standards left. Order more at the terminal.";
                return false;
            }

            if (!machine.TryBeginReference())
            {
                refusal = $"{machine.Def.DisplayName} will not take a standard right now.";
                return false;
            }

            Economy.TryConsumeReferenceStandard();
            return true;
        }

        /// <summary>
        /// Recalibrate against today's certificate.
        /// <para>
        /// Housekeeping rather than analysis, so it is still allowed after the shift ends — same rule
        /// as the flush. It occupies the instrument either way, which is the "costs time" half of
        /// §5.3; <see cref="EconomyTuning.CalibrationCost"/> is the other half.
        /// </para>
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryStartCalibration(MachineInstance machine, out string refusal)
        {
            refusal = null;
            if (machine == null) { refusal = "No such instrument."; return false; }

            if (machine.IsRunning) { refusal = $"{machine.Def.DisplayName} is busy."; return false; }
            if (!machine.IsEmpty) { refusal = "Take the vial out before calibrating."; return false; }

            if (!machine.HasFreshCheck(Day))
            {
                refusal = "Run today's certified standard first — there is nothing to calibrate against.";
                return false;
            }

            if (Economy.Money < Tuning.CalibrationCost)
            {
                refusal = $"A calibration costs £{Tuning.CalibrationCost:N0}, and the account will not cover it.";
                return false;
            }

            if (!machine.TryBeginCalibration(Day))
            {
                refusal = $"{machine.Def.DisplayName} will not start a calibration right now.";
                return false;
            }

            Economy.Charge(Tuning.CalibrationCost);
            return true;
        }

        /// <summary>
        /// Finish a recalibration. The order is load-bearing: the suspicion window is read off the
        /// machine <i>before</i> <see cref="MachineRuntimeState.Calibrate"/> moves its start to now,
        /// which would otherwise leave the retroactive list permanently empty.
        /// </summary>
        private void CompleteCalibration(MachineInstance machine)
        {
            var outcome = Samples.FlagDriftSuspects(machine.Runtime, machine.Runtime.DriftPercent, Day);

            machine.Runtime.Calibrate(Day);
            machine.LastCheck = null;   // consumed — the next calibration needs a fresh standard
            machine.LastCheckRun = null;
            machine.LastCalibration = outcome;

            Calibrated?.Invoke(machine, outcome);
        }

        /// <summary>
        /// Re-open a record filed on suspect numbers, if there is enough oil left to repeat one of the
        /// tests now in doubt (§5.3).
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryReopenSuspect(SampleId id, out string refusal)
        {
            refusal = null;
            if (!Samples.TryGet(id, out var sample)) { refusal = "No such sample."; return false; }

            return Samples.ReopenForRetest(id, SmallestSuspectDraw(sample), out refusal);
        }

        /// <summary>
        /// The least oil that could repeat one of this record's suspect tests, or
        /// <see cref="float.PositiveInfinity"/> if nothing on the bench can repeat any of them.
        /// <para>
        /// The terminal reads this to tell the player how short they are <i>before</i> they press the
        /// button, because "you cannot check this any more" is information they are owed, not a
        /// punchline.
        /// </para>
        /// </summary>
        public float SmallestSuspectDraw(SampleState sample)
        {
            float smallest = float.PositiveInfinity;
            if (sample == null) return smallest;

            foreach (var result in sample.Results)
            {
                if (!result.Suspect) continue;

                foreach (var machine in Machines)
                {
                    if (machine.Def == null || machine.Def.Id != result.MachineId) continue;
                    if (machine.Def.SampleVolumeMl < smallest) smallest = machine.Def.SampleVolumeMl;
                }
            }

            return smallest;
        }

        /// <summary>
        /// Close the day and settle every verdict that has come due. Returns the reports so the
        /// summary screen can show what the player got wrong days after they got it wrong.
        /// </summary>
        public IReadOnlyList<ConsequenceReport> EndDay()
        {
            DayInProgress = false;
            DaySecondsRemaining = 0f;

            lastReports.Clear();
            Settle(Samples.ResolveDue(Day, Tuning));

            // If that closed the run — the contract ran out, or the money did — everything still
            // pending is about to be stranded, because no further day will ever resolve it. §5.4
            // delays the cost; it does not cancel it. A verdict the player never hears back on is
            // diagnostic work that silently did nothing, and with DaysToFailure running to 14 days
            // that was most of them.
            //
            // Settled after the due pass rather than instead of it, so a player who tips into
            // bankruptcy on this day's reports still gets the rest of their reckoning.
            if (IsRunOver) Settle(Samples.ResolveDue(Day, Tuning, settleEverything: true));

            // Cardboard does not survive the night (#31). Raised before DayEnded so anything listening
            // for the summary can retire the props in the same pass.
            Deliveries.SweepSpent();

            DayEnded?.Invoke(lastReports);
            return lastReports;
        }

        private void Settle(List<ConsequenceReport> reports)
        {
            foreach (var report in reports)
            {
                Economy.Apply(report);
                lastReports.Add(report);

                // MONITOR on a developing fault keeps the unit in service and it gets resampled
                // next cycle with worse numbers (§5.4). Queue it now; the chemistry progresses when
                // the next day generates it.
                if (report.RequeueSample) Samples.QueueRequeue(report.Sample);
            }
        }

        /// <summary>
        /// Re-send the units the player chose to keep watching, with the fault further along.
        /// <para>
        /// A re-draw travels in the same box as the rest of that firm's day: it came off the same
        /// site on the same lorry, and giving it a carton of its own would put a second delivery note
        /// on the bench for one vial. The sender is copied off the original, which
        /// <see cref="SampleRegistry.BuildRequeue"/> deliberately does not do — it knows about
        /// chemistry, not about who posted the box.
        /// </para>
        /// </summary>
        private void GenerateRequeues()
        {
            foreach (var id in Samples.TakePendingRequeues())
            {
                var previous = Samples.Get(id);
                var generated = Samples.BuildRequeue(id, generator, Day, ref rng);
                if (generated == null) continue;

                generated.State.Customer = previous != null ? previous.Customer : null;
                generated.State.IsSettled = false;
                generated.State.TemperatureC = rng.Range(4f, 22f);

                Samples.Add(generated);
                PackInCarton(generated.State);
            }
        }

        // -- Arrivals -------------------------------------------------------------------------------

        private void GenerateArrivals(in DayPlan plan)
        {
            // Cleared before the early return, not after it: a day with nothing scheduled must not
            // leave yesterday's paperwork on the bench claiming to be today's.
            notes.Clear();
            cartons.Clear();
            dayCartons.Clear();

            // Kept for the vials #32 adds to a box afterwards. A contract tuned to a harsh healthy
            // rate must not hand out generously healthy oil just because a bottle arrived through a
            // paperwork slip rather than through the plan.
            dayHealthyChance = plan.HealthyChance;

            if (plan.ProfileIds == null || plan.ProfileIds.Length == 0) return;

            for (int i = 0; i < plan.SampleCount; i++)
            {
                string profileId = plan.ProfileIds[rng.Range(0, plan.ProfileIds.Length)];
                if (!profilesById.TryGetValue(profileId, out var profile)) continue;

                // Who sent it is decided before the label, because the label is drawn from their
                // sites (#29). A profile nobody in the catalog runs still arrives — anonymously,
                // through the generic plant list — rather than being dropped from the day.
                var customer = PickSender(profileId, plan.CustomerIds);

                var request = GenerationRequest.Default(
                    profile, EquipmentTags.For(customer, profileId, ref rng), Day);
                request.HealthyChance = plan.HealthyChance;
                request.HoursSinceOilChange = rng.Range(0.15f, 1f) * profile.DefaultOilChangeHours;

                // Fill the ambiguity quota first. A borderline sample must have a fault to sit on the
                // edge of, so healthy is off the table for these.
                if (i < plan.BorderlineCount)
                {
                    request.ForceBorderline = true;
                    request.HealthyChance = 0f;
                }

                var generated = generator.Generate(request, ref rng);
                if (generated == null) continue;

                generated.State.FieldTechNote = EquipmentTags.Note(ref rng);
                generated.State.Customer = customer;

                // Arrives unsettled and at ambient. Agitating and warming are player actions with a
                // real time cost (§9) rather than menu clicks — and unboxing is not one of them, so
                // neither of these fields is touched again until the player shakes the vial into an
                // instrument (#31).
                generated.State.IsSettled = false;
                generated.State.TemperatureC = rng.Range(4f, 22f);

                Samples.Add(generated);
                PackInCarton(generated.State);
            }
        }

        /// <summary>
        /// Put a generated sample in its sender's box, and its line on their note.
        /// <para>
        /// The location is the carton, so §5.1's unload step is now literally taking a vial out of a
        /// box somebody carried in — <see cref="SampleLifecycle.TryMove"/> already treats leaving an
        /// <c>InCrate</c> location as that step and needed no change.
        /// </para>
        /// <para>
        /// <paramref name="listOnNote"/> is false for a vial the customer never wrote down — #32's
        /// unlisted sample, and the second half of a duplicated claim, which is listed by hand
        /// afterwards so the line can go somewhere other than the bottom of the page.
        /// </para>
        /// </summary>
        private void PackInCarton(SampleState sample, bool listOnNote = true)
        {
            if (sample == null) return;

            var carton = CartonFor(sample.Customer);

            sample.JobNumber = carton.JobNumber;
            sample.Location = SampleLocation.InCrate(carton.Id, carton.Contents.Count);

            carton.Add(sample.Id);
            if (listOnNote) carton.Note.Add(sample.EquipmentTag, sample.Profile, sample.Id);
        }

        /// <summary>
        /// A firm that runs this fluid, or null if none does.
        /// <para>
        /// Drawn through the run's own <see cref="Rng"/> so a seed reproduces a whole contract's
        /// senders, not just its chemistry — two players on the same seed have to see the same names
        /// on the same days or a shared run is not the same run.
        /// </para>
        /// <para>
        /// <paramref name="allowed"/> is <see cref="DayPlan.CustomerIds"/> and is null on every day of
        /// the shipping contract, which is why that contract draws exactly the senders it always did.
        /// A filter that matches nobody falls through to the whole catalog rather than to an anonymous
        /// delivery: a day naming a firm this build has never heard of is a content fault, and
        /// silently posting the vials from nowhere would hide it behind a delivery note with no name
        /// on it.
        /// </para>
        /// </summary>
        private CustomerDef PickSender(string profileId, string[] allowed)
        {
            candidates.Clear();

            var all = Content != null ? Content.Customers : null;
            if (all == null) return null;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || !all[i].Runs(profileId)) continue;
                if (!IsAllowed(allowed, all[i].Id)) continue;
                candidates.Add(all[i]);
            }

            if (candidates.Count == 0 && allowed != null && allowed.Length > 0)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].Runs(profileId)) candidates.Add(all[i]);
                }
            }

            return candidates.Count == 0 ? null : candidates[rng.Range(0, candidates.Count)];
        }

        private static bool IsAllowed(string[] allowed, string customerId)
        {
            if (allowed == null || allowed.Length == 0) return true;

            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(allowed[i], customerId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// One note per sender per day, and one box per note. A carton comes from one firm, so vials
        /// from two customers arriving on the same morning are two deliveries with two pieces of paper
        /// — which is what makes #32's reconciliation a per-carton question rather than a daily audit,
        /// and what makes "one carton per note" the only grouping the lab has (#31).
        /// </summary>
        private Carton CartonFor(CustomerDef customer)
        {
            string key = customer != null ? customer.Id : string.Empty;
            if (cartons.TryGetValue(key, out var existing)) return existing;

            var note = new DeliveryNote(customer, EquipmentTags.JobNumber(customer, ref rng), Day);
            notes[key] = note;

            var created = Deliveries.Book(note, Day);
            cartons[key] = created;

            // Kept in creation order beside the dictionary, because #32's discrepancies are rolled
            // per carton and a Dictionary's iteration order is not a promise. A seed that reproduced
            // a day's chemistry but not which of two firms got the bad note would not reproduce the
            // day.
            dayCartons.Add(created);
            return created;
        }

        private readonly Dictionary<string, DeliveryNote> notes = new();
        private readonly Dictionary<string, Carton> cartons = new();
        private readonly List<Carton> dayCartons = new();
        private readonly List<CustomerDef> candidates = new();
        private readonly List<EquipmentProfileDef> oilChoices = new();
        private readonly List<SampleId> callSubjects = new();
        private float dayHealthyChance = 0.35f;

        /// <summary>
        /// The paperwork that came with this morning's cartons, one per sender. Rebuilt each day —
        /// a note is a document on a bench for a shift, not a record. What survives the day is the
        /// job number on each <see cref="SampleState"/>.
        /// </summary>
        public IReadOnlyCollection<DeliveryNote> Notes => notes.Values;

        // -- Reconciliation (#32) -----------------------------------------------------------------------

        /// <summary>
        /// Let the day's deliveries go wrong, then write down where every vial actually came from.
        ///
        /// <para>
        /// <b>This is the only place a note and its box are allowed to disagree.</b> Everything above
        /// packs a carton that reconciles perfectly; everything below is #32. Keeping the two apart is
        /// what makes the discrepancy a thing that was <i>done to</i> the delivery rather than a
        /// special case threaded through generation — and it means a day with no careless senders in
        /// it takes exactly the path it always did.
        /// </para>
        ///
        /// <para>
        /// <b>Provenance is recorded last, over the finished page.</b> Line indices are a property of
        /// the note as printed, and inserting a claim in the middle of one moves every index after it.
        /// Recording as we went would have left half the carton pointing at the wrong rows.
        /// </para>
        /// </summary>
        private void SealDeliveries()
        {
            for (int i = 0; i < dayCartons.Count; i++) IntroduceDiscrepancies(dayCartons[i]);
            for (int i = 0; i < dayCartons.Count; i++) RecordProvenance(dayCartons[i]);
        }

        /// <summary>
        /// Roll one delivery against its sender's propensities and carry out whatever came up.
        /// <para>
        /// The drum claim is applied before the paperwork slip, matching the order
        /// <see cref="DeliveryDiscrepancies.Roll"/> draws them in — and so that a smudged label is
        /// chosen from a box that already contains everything it is going to.
        /// </para>
        /// </summary>
        private void IntroduceDiscrepancies(Carton carton)
        {
            var note = carton?.Note;
            if (note == null || note.Count == 0) return;

            var plan = DeliveryDiscrepancies.Roll(note.Customer, ref rng);

            if (plan.DuplicateClaim) ClaimTankTwice(carton, plan.SameDrum);

            switch (plan.Slip)
            {
                case PaperworkSlip.MissingSample: ClaimAVialThatIsNotThere(carton); break;
                case PaperworkSlip.UnlistedSample: PackAVialNobodyWroteDown(carton); break;
                case PaperworkSlip.UnreadableLabel: SmudgeALabel(carton); break;
            }
        }

        /// <summary>
        /// §6.1's trap and its innocent twin: a second vial from a tank the note already lists, booked
        /// as a second draw.
        ///
        /// <para>
        /// <b>Both vials carry the tank's tag and neither says which claim it answers.</b> That is the
        /// whole ambiguity — on paper the honest case and the corner-cutting case are the same page.
        /// What separates them is the oil: <see cref="SampleRegistry.BuildSplitDraw"/> copies the
        /// first vial's chemistry exactly, so two bottles off one drum measure the same to within
        /// instrument noise, while two genuine draws differ by the ordinary spread of a healthy
        /// baseline and their own fault rolls. The player finds out by running both, which is a
        /// measurement rather than an intuition (hard rule 3).
        /// </para>
        ///
        /// <para>
        /// The second line goes in at a random row rather than beside the first. Adjacent lines would
        /// make the duplicate a shape you spot without reading, and reading the note is the mechanic.
        /// </para>
        /// </summary>
        private void ClaimTankTwice(Carton carton, bool sameDrum)
        {
            var note = carton.Note;

            var line = note.Lines[rng.Range(0, note.Count)];
            if (!line.Arrived || line.Profile == null) return;
            if (!Samples.TryGet(line.Sample, out var original)) return;

            // Already ambiguous for another reason — leave it alone. One vial, one question.
            if (original.Ambiguity != SampleAmbiguity.None) return;

            GeneratedSample twin;
            if (sameDrum)
            {
                twin = Samples.BuildSplitDraw(line.Sample, generator, Day, ref rng);
            }
            else
            {
                var request = GenerationRequest.Default(line.Profile, line.TankTag, Day);
                request.HealthyChance = dayHealthyChance;
                request.HoursSinceOilChange = rng.Range(0.15f, 1f) * line.Profile.DefaultOilChangeHours;
                twin = generator.Generate(request, ref rng);
            }

            if (twin == null) return;

            twin.State.FieldTechNote = EquipmentTags.Note(ref rng);
            twin.State.Customer = note.Customer;
            twin.State.IsSettled = false;
            twin.State.TemperatureC = rng.Range(4f, 22f);

            // Set on both halves whichever branch built the twin: BuildSplitDraw marks them, a plain
            // generate does not, and the pair is ambiguous either way.
            original.Ambiguity = SampleAmbiguity.DuplicateClaim;
            twin.State.Ambiguity = SampleAmbiguity.DuplicateClaim;

            Samples.Add(twin);
            PackInCarton(twin.State, listOnNote: false);
            note.Insert(rng.Range(0, note.Count + 1), line.TankTag, line.Profile, twin.State.Id);
        }

        /// <summary>
        /// A line the box does not answer. Found by counting: the note declares n vials and n−1 came
        /// out of the carton, and exactly one tank on the page has no bottle carrying its tag.
        /// <para>
        /// The tag is drawn from the sender's own sites and checked against the page, because a
        /// "missing" line naming a tank that did arrive is not missing — it is a duplicate, and a
        /// different discrepancy with a different answer.
        /// </para>
        /// </summary>
        private void ClaimAVialThatIsNotThere(Carton carton)
        {
            var note = carton.Note;

            var profile = PickOil(note);
            if (profile == null) return;
            if (!TryDrawUnusedTag(note, profile, out string tankTag)) return;

            note.Insert(rng.Range(0, note.Count + 1), tankTag, profile, SampleId.None);
        }

        /// <summary>
        /// A vial the paperwork never mentions. Found the same way from the other side: one more
        /// bottle comes out of the box than the page declares, and its tag is on no line.
        /// <para>
        /// It is a real sample with real chemistry and it can be run, filed and paid for like any
        /// other — nothing about it needs registering, because its label speaks perfectly well. What
        /// it lacks is a customer expecting a result.
        /// </para>
        /// </summary>
        private void PackAVialNobodyWroteDown(Carton carton)
        {
            var note = carton.Note;

            var profile = PickOil(note);
            if (profile == null) return;
            if (!TryDrawUnusedTag(note, profile, out string tankTag)) return;

            var request = GenerationRequest.Default(profile, tankTag, Day);
            request.HealthyChance = dayHealthyChance;
            request.HoursSinceOilChange = rng.Range(0.15f, 1f) * profile.DefaultOilChangeHours;

            var extra = generator.Generate(request, ref rng);
            if (extra == null) return;

            extra.State.FieldTechNote = EquipmentTags.Note(ref rng);
            extra.State.Customer = note.Customer;
            extra.State.IsSettled = false;
            extra.State.TemperatureC = rng.Range(4f, 22f);

            Samples.Add(extra);
            PackInCarton(extra.State, listOnNote: false);
        }

        /// <summary>
        /// A label that did not survive the post. The note still lists the tank — the customer's
        /// paperwork is right and the bottle is what failed — so the vial can be identified by
        /// elimination against the other bottles in the same box, or settled outright by ringing the
        /// dispatcher (<see cref="TryCallCustomer"/>), which costs shift time.
        /// <para>
        /// Only ever applied to a vial that is not already ambiguous, and never to one that is the
        /// half of a duplicated claim: two questions about one bottle would leave a player with no
        /// way to answer either.
        /// </para>
        /// </summary>
        private void SmudgeALabel(Carton carton)
        {
            var note = carton.Note;

            int start = rng.Range(0, note.Count);
            for (int step = 0; step < note.Count; step++)
            {
                var line = note.Lines[(start + step) % note.Count];
                if (!line.Arrived) continue;
                if (!Samples.TryGet(line.Sample, out var sample)) continue;
                if (sample.Ambiguity != SampleAmbiguity.None) continue;
                if (string.IsNullOrEmpty(sample.EquipmentTag)) continue;

                // Its tank must be named exactly once on the page, or elimination has no answer.
                //
                // Blanking a label leaves the player one free route: every other bottle in the box
                // carries a legible tag, so the single line nobody claims is the one this vial belongs
                // to. If two bottles share a tank the survivor claims that line and nothing is left
                // unclaimed — the free route silently disappears and the only way left costs 45
                // seconds, with nothing telling the player this carton is different. Hard rule 3 does
                // not allow a question whose answer the player cannot reach.
                //
                // Duplicates arrive two ways and both are covered here: the deliberate same-drum roll,
                // and an ordinary coincidence, because tags are drawn from a handful of sites and tanks
                // and two vials in one box can collide on their own.
                if (note.CountFor(sample.EquipmentTag) > 1) continue;
                if (CartonCarriesTagTwice(carton, sample)) continue;

                sample.Ambiguity = SampleAmbiguity.UnreadableLabel;
                sample.EquipmentTag = null;
                return;
            }

            // Every bottle in this box shares its tank with another. Nothing is smudged rather than
            // posing a question with no answer — the carton simply arrives correct, which is the
            // failure mode a player never notices.
        }

        /// <summary>
        /// Does another bottle in this carton carry the same tag as <paramref name="candidate"/>?
        /// Asked of the vials rather than the note, because it is the bottles the player reads when
        /// working by elimination.
        /// </summary>
        private bool CartonCarriesTagTwice(Carton carton, SampleState candidate)
        {
            for (int i = 0; i < carton.Contents.Count; i++)
            {
                if (!Samples.TryGet(carton.Contents[i], out var other)) continue;
                if (other == candidate) continue;

                if (string.Equals(other.EquipmentTag, candidate.EquipmentTag, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Write down, host-side, which tank each vial in this box really came out of and which row of
        /// the finished note it answers. Nothing here is shown to anybody; it is what
        /// <see cref="ConsequenceResolver"/> scores a registration against days later.
        /// </summary>
        private void RecordProvenance(Carton carton)
        {
            var note = carton?.Note;
            if (note == null) return;

            for (int i = 0; i < note.Count; i++)
            {
                var line = note.Lines[i];
                if (line.Arrived) Samples.SetProvenance(line.Sample, line.TankTag, i);
            }

            // Anything in the box the page never claimed. Its own label is the truth about it, and
            // -1 records that no line is expecting it.
            for (int i = 0; i < carton.Contents.Count; i++)
            {
                var id = carton.Contents[i];
                if (note.IndexOf(id) >= 0) continue;
                if (Samples.TryGet(id, out var sample)) Samples.SetProvenance(id, sample.EquipmentTag, -1);
            }
        }

        /// <summary>An oil this note's sender actually runs, falling back to one already on the page.</summary>
        private EquipmentProfileDef PickOil(DeliveryNote note)
        {
            oilChoices.Clear();

            var customer = note.Customer;
            if (customer != null)
            {
                foreach (var oil in customer.Oils)
                {
                    if (oil != null && profilesById.ContainsKey(oil.Id)) oilChoices.Add(oil);
                }
            }

            if (oilChoices.Count > 0) return oilChoices[rng.Range(0, oilChoices.Count)];
            return note.Count > 0 ? note.Lines[rng.Range(0, note.Count)].Profile : null;
        }

        /// <summary>
        /// A tank tag for this sender that no line on the page already names.
        /// <para>
        /// Bounded retries rather than a loop: the tank lists are short and a small plant can run out
        /// of unused codes on a busy morning. Giving up quietly costs one discrepancy on one delivery
        /// — colliding would cost the player a reconciliation with two right answers.
        /// </para>
        /// </summary>
        private bool TryDrawUnusedTag(DeliveryNote note, EquipmentProfileDef profile, out string tankTag)
        {
            const int attempts = 8;

            for (int i = 0; i < attempts; i++)
            {
                tankTag = EquipmentTags.For(note.Customer, profile.Id, ref rng);
                if (!note.TryFind(tankTag, out _)) return true;
            }

            tankTag = null;
            return false;
        }

        /// <summary>
        /// Record what the player says an ambiguous vial is (#32). Delegates outright — the rule and
        /// its refusals belong to <see cref="DeliveryBay"/>, which owns the boxes and the paper.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryRegisterSample(SampleId id, int noteLine, out string refusal) =>
            Deliveries.TryRegisterSample(id, noteLine, out refusal);

        /// <summary>
        /// Ring the customer's dispatcher and read an unreadable label back to them (#32).
        ///
        /// <para>
        /// <b>What it costs is shift time, taken up front.</b>
        /// <see cref="DeliveryDiscrepancies.CallSeconds"/> comes straight off the working day, the
        /// way a flush costs instrument time — so the call competes with the runs you were going to
        /// start, which is what makes "work it out from the note instead" a real alternative rather
        /// than a slower one. It is charged once per carton however many bottles it settles, because
        /// you are on the phone to one dispatcher going down one page.
        /// </para>
        ///
        /// <para>
        /// <b>What it cannot do is settle a duplicated claim.</b> Asked whether they really drew that
        /// tank twice, the customer reads their own note back — and their note is the thing in doubt.
        /// Refused before any time is spent, with that said out loud, because a phone call that
        /// answered §6.1's trap would delete it.
        /// </para>
        /// </summary>
        /// <param name="registered">Vials the dispatcher was able to identify. Zero when refused.</param>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public bool TryCallCustomer(string cartonId, out int registered, out string refusal)
        {
            refusal = null;
            registered = 0;

            var carton = Deliveries.Find(cartonId);
            if (carton == null) { refusal = "No such delivery."; return false; }

            var note = carton.Note;
            if (note == null)
            {
                refusal = "There is no delivery note for that box, so there is nobody to ring.";
                return false;
            }

            if (!DayInProgress || ShiftOver)
            {
                refusal = $"{carton.SenderName}'s dispatch office has closed for the day.";
                return false;
            }

            callSubjects.Clear();
            for (int i = 0; i < carton.Contents.Count; i++)
            {
                if (!Samples.TryGet(carton.Contents[i], out var sample)) continue;
                if (sample.Ambiguity != SampleAmbiguity.UnreadableLabel) continue;
                if (!sample.NeedsRegistering) continue;
                callSubjects.Add(sample.Id);
            }

            if (callSubjects.Count == 0)
            {
                refusal = $"Nothing in {note.JobNumber} needs their dispatch record. An unreadable " +
                          "label is the only thing they can settle — asked about anything else they " +
                          "will read their own note back to you, and their note is what you are " +
                          "checking.";
                return false;
            }

            DaySecondsRemaining = Mathf.Max(0f, DaySecondsRemaining - DeliveryDiscrepancies.CallSeconds);

            foreach (var id in callSubjects)
            {
                if (!Samples.TryReadDispatchRecord(id, out string tankTag, out int line)) continue;
                if (!Samples.TryGet(id, out var sample)) continue;

                sample.RegisteredTag = tankTag;
                sample.RegisteredLine = line >= 0 ? line : SampleState.CannotTell;
                registered++;
            }

            return true;
        }

        // -- Saving and loading (#49) -----------------------------------------------------------------
        //
        // Narrow, internal, and paired with RunSnapshotCapture / RunSnapshotRestore, which are the
        // only callers. Everything below writes state the day cycle otherwise owns exclusively, so
        // none of it may be reachable from outside Residue.Gameplay — least of all from a client.

        /// <summary>The run seed. Diagnostic only: a seed reproduces generation, never decisions.</summary>
        internal int Seed { get; private set; }

        /// <summary>Live generator state, so a load resumes the stream rather than restarting it.</summary>
        internal void CaptureRng(out uint a, out uint b, out uint c, out uint d) =>
            rng.CaptureState(out a, out b, out c, out d);

        /// <summary>The id the next arrival will carry.</summary>
        internal int NextSampleId => generator.NextSampleId;

        /// <summary>
        /// Resume the sample stream exactly where the save left it. The generator is rebuilt rather
        /// than mutated because its id counter is the other half of the same fact.
        /// </summary>
        internal void RestoreGeneration(Rng state, int nextSampleId)
        {
            rng = state;
            generator = new SampleGenerator(Content.Faults, nextSampleId);
        }

        /// <summary>
        /// Rebuild the delivery bay from the restored samples (#49). Called once, after the vault is
        /// back — see <see cref="DeliveryBay.RebuildFrom"/> for why a carton is derived rather than
        /// written into the save.
        /// </summary>
        internal void RebuildDeliveries() => Deliveries.RebuildFrom(Samples.All);

        /// <summary>Put the day clock back. Saves happen at a day boundary, so this is normally a closed day.</summary>
        internal void RestoreDay(int day, bool dayInProgress, float daySecondsRemaining)
        {
            Day = day;
            DayInProgress = dayInProgress;
            DaySecondsRemaining = daySecondsRemaining;
        }

        /// <summary>
        /// Put back the summary the player was looking at. Purely presentational — every report here
        /// has already been applied to the economy and its sample already marked resolved — but a
        /// continued run that opened on an empty end-of-day screen would read as lost work.
        /// </summary>
        internal void RestoreReport(ConsequenceReport report)
        {
            if (report != null) lastReports.Add(report);
        }

        /// <summary>Samples that have arrived and not yet had a verdict filed.</summary>
        public List<SampleState> OpenSamples()
        {
            var open = new List<SampleState>();
            foreach (var s in Samples.All)
            {
                if (!s.FiledVerdict.HasValue) open.Add(s);
            }
            open.Sort((a, b) => a.Id.CompareTo(b.Id));
            return open;
        }
    }
}
