using System;
using System.Collections.Generic;
using Residue.Chemistry;
using Residue.Data;
using Residue.Gameplay.Simulation;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// What the tutorial is pointing at, and what the player has already done about it.
    ///
    /// <para>
    /// <b>It observes; it is never asked.</b> Nothing in the simulation holds a reference to this
    /// type, nothing consults it before allowing an action, and there is no method on it that returns
    /// a refusal. It subscribes to signals the lab already raises — <see cref="LabCommands.Accepted"/>
    /// for the actions a player takes and <c>LabState.RunCompleted</c> for the one thing that happens
    /// without them — and ticks a box. Detaching it changes the run in no way at all, which is the
    /// property <c>TutorialTests</c> asserts rather than the property this comment claims.
    /// </para>
    ///
    /// <para>
    /// <b>Nothing here gates anything, and that is the whole design.</b> <c>CLAUDE.md</c> records
    /// booking-in being torn out (#73) because "the loop stopped dead at a keyboard": an objective
    /// that had to be finished before the next thing worked would be exactly that mistake wearing a
    /// tutorial's clothes. So every step is independently satisfiable in any order, a player who never
    /// looks at the card plays an ordinary short contract, and the card can be put away entirely.
    /// </para>
    ///
    /// <para>
    /// <b>It never says what a sample is.</b> Hard rule 1: a player who understands cause must beat
    /// one who memorised a table, so the tutorial teaches where to look and how the room works and
    /// stops there. Not one element, fault or root cause is named in any line it draws — the words
    /// live in <see cref="TutorialStrings"/>, which carries that rule at the point of editing, and
    /// <c>TutorialTests</c> holds it shut against the real content tables.
    /// </para>
    ///
    /// <para>
    /// <b>Local, and off the wire.</b> <see cref="LabCommands.Accepted"/> fires in the process that
    /// asked, so this tracks the person at this keyboard. Nothing about it reaches
    /// <c>SampleState</c>, a <c>RunSnapshot</c> or a view type; the tutorial is single player
    /// (<see cref="TutorialRun"/>) and progress through it is a fact about a session, not about a lab.
    /// </para>
    /// </summary>
    public sealed class TutorialObjectives : IDisposable
    {
        /// <summary>One line of the card: what to do, the sentence under it, and which day it is on.</summary>
        public readonly struct Objective
        {
            public readonly TutorialStep Step;

            /// <summary>Contract day this belongs to. The card shows a day's group once it has begun.</summary>
            public readonly int Day;

            /// <summary>The imperative, drawn for every objective on the card.</summary>
            public readonly LocKey Line;

            /// <summary>The one extra sentence, drawn only under whichever objective is next.</summary>
            public readonly LocKey Detail;

            public Objective(TutorialStep step, int day, LocKey line, LocKey detail)
            {
                Step = step;
                Day = day;
                Line = line;
                Detail = detail;
            }
        }

        /// <summary>
        /// The live tracker, or null on any run that is not the tutorial.
        /// <para>
        /// A static for the same reason <see cref="LabView.Current"/> is one: the HUD is built per
        /// player on the other side of a scene load and has no wiring to hand it a reference. Null is
        /// the ordinary case and every reader treats it as "draw nothing".
        /// </para>
        /// </summary>
        public static TutorialObjectives Current { get; private set; }

        /// <summary>
        /// Attach a tracker to a tutorial run, replacing any that was left over. Called by
        /// <see cref="LabRuntime"/> and nothing else.
        /// </summary>
        public static TutorialObjectives Begin(LabState lab)
        {
            Current?.Dispose();
            Current = new TutorialObjectives(lab);
            return Current;
        }

        /// <summary>
        /// Detach the tracker belonging to this lab. Checked against the lab rather than cleared
        /// outright, for the reason <see cref="LabRuntime.OnDestroy"/> checks the executor and the
        /// host view: a second runtime tearing itself down must not unhook the one that is running.
        /// </summary>
        public static void End(LabState lab)
        {
            if (Current == null || Current.Lab != lab) return;

            Current.Dispose();
            Current = null;
        }

        // -- The script --------------------------------------------------------------------------------

        /// <summary>
        /// The fourteen objectives, in drawing order.
        ///
        /// <para>
        /// Day one is §5.1's loop with every arrow a player action: unload, unbox, prep, load, run,
        /// carry the printout, file, and close the day. Day two is the two things hard rule 3 rests
        /// on. Contamination and calibration drift are only fair because a blank run and a certified
        /// standard reveal them, and nothing else in the game gives anybody a reason to push solvent
        /// through a working instrument — so if the tutorial points at nothing else, it points at
        /// those two.
        /// </para>
        ///
        /// <para>
        /// They are on the second day rather than the first because both only mean anything on an
        /// instrument that has already had something through it. A blank on an untouched machine reads
        /// back nothing and teaches the opposite of the lesson.
        /// </para>
        /// </summary>
        private static readonly Objective[] Script =
        {
            new(TutorialStep.TakeACarton, 1,
                TutorialStrings.TakeACartonLine, TutorialStrings.TakeACartonDetail),
            new(TutorialStep.OpenTheCarton, 1,
                TutorialStrings.OpenTheCartonLine, TutorialStrings.OpenTheCartonDetail),
            new(TutorialStep.TakeAVial, 1,
                TutorialStrings.TakeAVialLine, TutorialStrings.TakeAVialDetail),
            new(TutorialStep.LoadAnInstrument, 1,
                TutorialStrings.LoadAnInstrumentLine, TutorialStrings.LoadAnInstrumentDetail),
            new(TutorialStep.StartTheRun, 1,
                TutorialStrings.StartTheRunLine, TutorialStrings.StartTheRunDetail),
            new(TutorialStep.LetARunFinish, 1,
                TutorialStrings.LetARunFinishLine, TutorialStrings.LetARunFinishDetail),
            new(TutorialStep.FileTheSlip, 1,
                TutorialStrings.FileTheSlipLine, TutorialStrings.FileTheSlipDetail),
            new(TutorialStep.FileAVerdict, 1,
                TutorialStrings.FileAVerdictLine, TutorialStrings.FileAVerdictDetail),
            new(TutorialStep.EndTheDay, 1,
                TutorialStrings.EndTheDayLine, TutorialStrings.EndTheDayDetail),

            new(TutorialStep.RunABlank, 2,
                TutorialStrings.RunABlankLine, TutorialStrings.RunABlankDetail),
            new(TutorialStep.FillABottle, 2,
                TutorialStrings.FillABottleLine, TutorialStrings.FillABottleDetail),
            new(TutorialStep.FlushAnInstrument, 2,
                TutorialStrings.FlushAnInstrumentLine, TutorialStrings.FlushAnInstrumentDetail),
            new(TutorialStep.RunAStandard, 2,
                TutorialStrings.RunAStandardLine, TutorialStrings.RunAStandardDetail),
            new(TutorialStep.Recalibrate, 2,
                TutorialStrings.RecalibrateLine, TutorialStrings.RecalibrateDetail)
        };

        /// <summary>
        /// The action that ticks each step, or <see cref="TutorialStep.None"/> for a command the card
        /// has nothing to say about.
        /// <para>
        /// A pure function rather than a switch buried in the handler, so the mapping is a thing a
        /// test can read: every objective except <see cref="TutorialStep.LetARunFinish"/> — the one
        /// thing on the card that happens without the player — has to be reachable from a real
        /// <see cref="LabCommandKind"/>, or the card is asking for something no signal will ever
        /// report and a player who did it would watch the box stay empty.
        /// </para>
        /// </summary>
        public static TutorialStep StepFor(LabCommandKind kind) => kind switch
        {
            LabCommandKind.TakeCarton => TutorialStep.TakeACarton,
            LabCommandKind.OpenCarton => TutorialStep.OpenTheCarton,
            LabCommandKind.TakeVial => TutorialStep.TakeAVial,
            LabCommandKind.LoadMachine => TutorialStep.LoadAnInstrument,
            LabCommandKind.StartRun => TutorialStep.StartTheRun,
            LabCommandKind.FileSlip => TutorialStep.FileTheSlip,
            LabCommandKind.FileVerdict => TutorialStep.FileAVerdict,
            LabCommandKind.EndDay => TutorialStep.EndTheDay,
            LabCommandKind.RunBlank => TutorialStep.RunABlank,
            LabCommandKind.FillBottle => TutorialStep.FillABottle,
            LabCommandKind.FlushMachine => TutorialStep.FlushAnInstrument,
            LabCommandKind.RunReference => TutorialStep.RunAStandard,
            LabCommandKind.Calibrate => TutorialStep.Recalibrate,
            _ => TutorialStep.None
        };

        // -- Instance ----------------------------------------------------------------------------------

        private readonly HashSet<TutorialStep> done = new();
        private LabState lab;

        public TutorialObjectives(LabState lab)
        {
            this.lab = lab;
            HighestDayStarted = lab != null ? Math.Max(1, lab.Day) : 1;

            LabCommands.Accepted += OnAccepted;

            if (lab == null) return;
            lab.DayStarted += OnDayStarted;
            lab.RunCompleted += OnRunCompleted;
        }

        /// <summary>The run this is watching. Used by <see cref="End"/> and by nothing else.</summary>
        public LabState Lab => lab;

        /// <summary>Every objective, on every day, in drawing order.</summary>
        public IReadOnlyList<Objective> All => Script;

        /// <summary>
        /// The latest contract day that has begun, so the card can show a day's group once it exists.
        /// Day two's objectives are not hidden to gate them — nothing is gated — but a card that
        /// listed the blank on the first morning would be pointing at an instrument nothing has been
        /// through.
        /// </summary>
        public int HighestDayStarted { get; private set; }

        /// <summary>
        /// Bumped by anything that changes what the card should read as. The HUD redraws off this
        /// rather than rebuilding fourteen rows every frame — a card that is up for a whole shift is
        /// not a place to spend a per-frame allocation.
        /// </summary>
        public int Version { get; private set; }

        public bool IsDone(TutorialStep step) => done.Contains(step);

        /// <summary>Is this objective's day under way?</summary>
        public bool IsVisible(in Objective objective) => objective.Day <= HighestDayStarted;

        /// <summary>How many of the objectives now on the card are ticked.</summary>
        public int DoneCount
        {
            get
            {
                int count = 0;
                foreach (var objective in Script)
                {
                    if (IsVisible(objective) && done.Contains(objective.Step)) count++;
                }
                return count;
            }
        }

        /// <summary>How many objectives are on the card at all.</summary>
        public int VisibleCount
        {
            get
            {
                int count = 0;
                foreach (var objective in Script)
                {
                    if (IsVisible(objective)) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// The first unticked objective on the card, or <see cref="TutorialStep.None"/> when there is
        /// none left. Only ever a hint about where to read next: it marks a row, it does not withhold
        /// the rows after it, and the player is free to do any of them first.
        /// </summary>
        public TutorialStep Next
        {
            get
            {
                foreach (var objective in Script)
                {
                    if (IsVisible(objective) && !done.Contains(objective.Step)) return objective.Step;
                }
                return TutorialStep.None;
            }
        }

        /// <summary>
        /// Tick a step. Public because the signal for one of them arrives from the day cycle rather
        /// than from a command, and because a test that could not tick a box would have to fake the
        /// whole command path to assert anything about ordering.
        /// </summary>
        public void Complete(TutorialStep step)
        {
            if (step == TutorialStep.None) return;
            if (!done.Add(step)) return;

            Version++;
        }

        private void OnAccepted(LabCommand command) => Complete(StepFor(command.Kind));

        private void OnDayStarted(int day)
        {
            if (day <= HighestDayStarted) return;

            HighestDayStarted = day;
            Version++;
        }

        /// <summary>
        /// A run finished, whatever kind it was. A blank and a certified standard both count, because
        /// the objective is "an instrument finished something while you were doing something else" and
        /// all three kinds teach that equally.
        /// </summary>
        private void OnRunCompleted(MachineInstance machine, TestResult result) =>
            Complete(TutorialStep.LetARunFinish);

        public void Dispose()
        {
            LabCommands.Accepted -= OnAccepted;

            if (lab != null)
            {
                lab.DayStarted -= OnDayStarted;
                lab.RunCompleted -= OnRunCompleted;
            }
            lab = null;
        }
    }
}
