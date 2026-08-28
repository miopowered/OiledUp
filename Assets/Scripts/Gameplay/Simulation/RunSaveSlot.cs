using System.IO;
using Residue.Data;
using UnityEngine;

namespace Residue.Gameplay.Simulation
{
    /// <summary>
    /// The one save slot a run lives in, and the only thing that joins <see cref="RunSaveStore"/> to
    /// <see cref="RunSnapshot"/> (#49).
    ///
    /// <para>
    /// <b>Host-only, structurally rather than by convention.</b> <see cref="TrySave"/> takes a
    /// <see cref="LabState"/> and there is no overload that takes a replicated view, so a process
    /// with no simulation has nothing to hand it — and a client has no simulation by construction
    /// (<c>LabRuntime.SimulatesLocally</c>, which exists to keep a truth-bearing generator out of
    /// every player's process). The hook that calls it is installed inside the branch of
    /// <c>LabRuntime.BuildLabIfAuthoritative</c> that a client returns before reaching, and
    /// <see cref="TakeContinueRequest"/> is read from the same branch. A client therefore neither
    /// writes a save nor loads one, which matters because a save on a client would be a second
    /// source of truth for state the host owns — the two would diverge on the first command the host
    /// refused.
    /// </para>
    ///
    /// <para>
    /// <b>One slot.</b> Not a limitation to be lifted later so much as a decision: a 20-day contract
    /// with delayed consequences is a run you live with, and a save the player can roll back would
    /// turn a wrong verdict from something they did into something they can undo. §5.4's whole
    /// design is that the cost lands days after the call.
    /// </para>
    /// </summary>
    public static class RunSaveSlot
    {
        /// <summary>File name inside <c>Application.persistentDataPath</c>.</summary>
        public const string FileName = "run.save";

        private static RunSaveStore store;

        /// <summary>
        /// Where the run is written. Defaults to the platform's own save directory.
        /// <para>
        /// Settable so a test can point it at a temporary directory and not at the machine's real
        /// save. Nothing in the game assigns it.
        /// </para>
        /// </summary>
        public static RunSaveStore Store
        {
            get => store ??= new RunSaveStore(Path.Combine(Application.persistentDataPath, FileName));
            set => store = value;
        }

        /// <summary>
        /// The player pressed CONTINUE and the lab scene has not loaded yet.
        /// <para>
        /// A latch rather than an argument because the lab is reached through a scene load, and the
        /// component that builds the run wakes on the other side of it. This is the same shape, and
        /// the same reason, as <c>LabRuntime.SimulatesLocally</c>.
        /// </para>
        /// </summary>
        public static void RequestContinue() => continueRequested = true;

        /// <summary>Read the CONTINUE latch and clear it, so a later NEW SHIFT cannot inherit it.</summary>
        public static bool TakeContinueRequest()
        {
            bool requested = continueRequested;
            continueRequested = false;
            return requested;
        }

        /// <summary>Drop a pending CONTINUE without acting on it — for a path back to the menu.</summary>
        public static void ForgetContinueRequest() => continueRequested = false;

        private static bool continueRequested;

        /// <summary>
        /// What is in the slot, for a menu deciding whether to offer CONTINUE. False when there is no
        /// save, or when the file on disk is damaged beyond both the primary and its backup.
        /// </summary>
        public static bool TryReadHeadline(out RunSaveHeadline headline)
        {
            headline = default;
            return Store.TryLoad(out string payload, out _, out _) &&
                   RunSnapshotCodec.TryReadHeadline(payload, out headline);
        }

        /// <summary>
        /// Write the run out. Called at the end of a day and nowhere else — see
        /// <see cref="RunSnapshotCapture"/> for why that is the only quiescent moment.
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public static bool TrySave(LabState lab, out string refusal)
        {
            refusal = null;

            if (lab == null)
            {
                refusal = "There is no run to save.";
                return false;
            }

            var snapshot = RunSnapshotCapture.Of(lab);
            return Store.TrySave(RunSnapshotCodec.Encode(snapshot), out refusal);
        }

        /// <summary>
        /// Rebuild the saved run against this build's content.
        /// <para>
        /// Three separate refusals live behind this one call and all of them are the player's to
        /// read: no save at all, a save this build's format cannot parse, and a save naming content
        /// this build no longer has. None of them ever loads a partial run.
        /// </para>
        /// </summary>
        /// <param name="refusal">Player-facing reason when this returns false. Never null then.</param>
        public static bool TryLoad(ContentCatalog catalog, out LabState lab, out string refusal)
        {
            lab = null;

            if (!Store.TryLoad(out string payload, out _, out string storeRefusal))
            {
                refusal = "There is no saved run to continue. " + storeRefusal;
                return false;
            }

            if (!RunSnapshotCodec.TryDecode(payload, out var snapshot, out refusal)) return false;

            return RunSnapshotRestore.TryRebuild(snapshot, catalog, out lab, out refusal);
        }
    }
}
