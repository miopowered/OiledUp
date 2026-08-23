using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Where the fixtures are. The only piece of scene knowledge <see cref="LabCommandExecutor"/>
    /// needs, kept behind an interface so the executor stays steppable in an edit-mode test with no
    /// scene at all — the same reason <c>LabState</c> and <c>SessionRegistry</c> are plain C# classes.
    /// <para>
    /// An executor built without one does not check reach. That is the honest behaviour rather than
    /// a hole: with no geometry there is nothing to compare against, and the only caller in that
    /// situation is a test or a process with no placed lab, neither of which has an untrusted player
    /// in it.
    /// </para>
    /// </summary>
    public interface ILabStations
    {
        /// <summary>
        /// The world position of a placed fixture. False when nothing in the scene has registered
        /// under that id, which is treated as "cannot be checked" rather than "does not exist" —
        /// the executor separately refuses ids that name no instrument.
        /// </summary>
        bool TryLocate(string fixtureId, out Vector3 position);
    }
}
