using System;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where a player action is sent, and how the answer comes back.
    /// <para>
    /// <b>The seam.</b> Every interaction in the world layer calls <see cref="Send"/> and then does
    /// its local half — reparenting a vial, destroying a slip — in the callback, if and only if the
    /// answer was yes. There is exactly one place in the game that knows whether "ask the server"
    /// means a round trip or a method call, and it is the two lines below.
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>No session.</b> <see cref="Router"/> is null, so <see cref="Send"/>
    /// calls <see cref="Executor"/> and invokes the callback before it returns. Single player is not
    /// a special case in any call site; it is the case where the hop is zero long.</description></item>
    /// <item><description><b>Host.</b> The netcode layer installs a router that recognises it is the
    /// server and calls the same executor with the same actor, again synchronously. A host's own
    /// actions are validated exactly as a client's are — see <see cref="LabCommandExecutor"/> for why
    /// there is no fast path.</description></item>
    /// <item><description><b>Client.</b> The router sends the request and remembers the callback
    /// against a sequence number; the host replies to that one client and the callback runs then.
    /// §3.1 is explicit that there is no fast-twitch action here and latency is irrelevant, which is
    /// what buys the right to wait for the answer rather than predict it.</description></item>
    /// </list>
    /// <para>
    /// <b>What deliberately does not come through here.</b> <see cref="Interactable.Prompt"/> and
    /// <see cref="Interactable.CanInteract"/> are asked every frame of every frame a player is
    /// looking at something. They stay local reads, always, and a command is never sent to answer
    /// one. That is safe because nothing they decide is trusted: the executor re-runs the same
    /// gateway when the request lands, so a client whose prompt is optimistic gets a refusal rather
    /// than a result, and a client whose prompt is pessimistic has greyed out a button it would have
    /// been allowed to press. Neither can corrupt the lab.
    /// </para>
    /// <para>
    /// Static because there is one lab and one process-wide answer to "is there a session". The
    /// netcode layer sets <see cref="Router"/>; <c>Residue.Gameplay</c> cannot see it, and must not
    /// (CLAUDE.md's assembly diagram), so the dependency is inverted through this field.
    /// </para>
    /// </summary>
    public static class LabCommands
    {
        /// <summary>
        /// Delivers a request to whoever is authoritative and calls back with the answer. Null when
        /// this process is the authority — set by <c>Residue.Net.LabNetwork</c> while a session is up.
        /// </summary>
        public delegate void Route(ILabActor actor, LabCommand command, Action<LabCommandResult> answered);

        /// <summary>Installed by the netcode layer on spawn and cleared on despawn.</summary>
        public static Route Router;

        /// <summary>
        /// The host's validator. Installed by <see cref="LabRuntime"/> on a process that simulates,
        /// and null on a client — which is why a client with no router refuses everything locally
        /// instead of quietly succeeding against a lab that is not there.
        /// </summary>
        public static LabCommandExecutor Executor;

        /// <summary>
        /// Ask the lab to do something.
        /// <para>
        /// <paramref name="answered"/> runs in this process, on the player who asked. It is the right
        /// place for the local half of an action — and the only correct place, because the local half
        /// must not happen when the host says no.
        /// </para>
        /// </summary>
        public static void Send(ILabActor actor, LabCommand command, Action<LabCommandResult> answered = null)
        {
            if (actor == null)
            {
                Debug.LogWarning($"[LabCommands] {command.Kind} sent with no actor; ignored.");
                answered?.Invoke(LabCommandResult.No("No such player."));
                return;
            }

            if (Router != null)
            {
                Router(actor, command, answered);
                return;
            }

            answered?.Invoke(ExecuteHere(actor, command));
        }

        /// <summary>
        /// Run a command against this process's own lab. The router calls this once it has decided
        /// that this process is the server; nothing else should.
        /// </summary>
        public static LabCommandResult ExecuteHere(ILabActor actor, LabCommand command) =>
            Executor != null
                ? Executor.Execute(actor, command)
                : LabCommandResult.No("The lab is not running here.");

        /// <summary>
        /// The shape almost every call site wants: say the refusal out loud to the player who tried,
        /// and run the local half only when the answer was yes.
        /// <para>
        /// The refusal is passed through verbatim. It was written by the gateway that produced it and
        /// is already addressed to the player; rewording one here would put a second voice between
        /// the rule and the person it applies to.
        /// </para>
        /// </summary>
        public static void Attempt(PlayerInteractor player, LabCommand command,
                                   Action<LabCommandResult> onAccepted)
        {
            Send(player, command, result =>
            {
                if (!result.Accepted)
                {
                    if (player != null) player.Say(result.Refusal);
                    return;
                }
                onAccepted?.Invoke(result);
            });
        }
    }
}
