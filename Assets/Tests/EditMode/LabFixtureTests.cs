using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The two fittings a player can be inside rather than stood at: the lab coat and the chair.
    /// <para>
    /// Neither touches the chemistry, which is exactly what has to be pinned. The coat is a
    /// <i>prompted beat</i> and must never become a gate — <c>CLAUDE.md</c> records booking-in being
    /// deleted (#73) because a mandatory errand stopped the loop dead before any analysis could start,
    /// and a coat you have to fetch first is the same mechanic wearing different clothes. The seat's
    /// promise is narrower and just as easy to break: whatever it takes away, it gives back.
    /// </para>
    /// </summary>
    public sealed class LabFixtureTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            spawned.Clear();
        }

        // -----------------------------------------------------------------------------------------
        // The coat
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: **nothing in the lab asks whether the coat is on.**
        /// <para>
        /// This is the test the whole feature exists under. A coat is allowed to be a beat — walk to
        /// the lockers, open one, put it on — and is not allowed to be a precondition on prepping,
        /// loading, running, flushing, filing or a verdict. The failure mode is one innocent
        /// <c>if (coat.IsWorn)</c> written by somebody who never read <see cref="LabCoat"/>'s type
        /// doc, so the check is on the source tree rather than on behaviour: it is the only place the
        /// question can be asked before the answer has been shipped.
        /// </para>
        /// <para>
        /// Comment lines are skipped, so <c>PromptStrings</c> and <c>CharacterBody</c> may point at
        /// the type in prose — that is documentation, not a dependency. The scene builder lives under
        /// <c>Assets/Editor</c> and is deliberately out of scope: something has to place the coat.
        /// </para>
        /// </summary>
        [Test]
        public void NothingInTheLabAsksWhetherTheCoatIsOn()
        {
            var offenders = new List<string>();
            var mention = new Regex(@"\bLabCoat\b", RegexOptions.Compiled);

            foreach (string path in SourceFiles())
            {
                if (string.Equals(Path.GetFileName(path), "LabCoat.cs", StringComparison.Ordinal))
                    continue;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*")) continue;
                    if (!mention.IsMatch(trimmed)) continue;

                    offenders.Add($"{Path.GetFileName(path)}:{i + 1}  {trimmed}");
                }
            }

            Assert.IsEmpty(offenders,
                "Something outside LabCoat.cs has taken a dependency on the lab coat. Wearing it is a " +
                "beat, never a gate: if any of these is asking whether it is on, the coat has become " +
                "the errand #73 deleted, and if any of them is putting it on the wire it has become " +
                "state a client can be wrong about:\n  " + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// Promise: the source scan above is looking at something. A scanner pointed at the wrong
        /// folder finds nothing and passes, which is indistinguishable from a clean tree.
        /// </summary>
        [Test]
        public void TheCoatScan_ActuallyReadsTheSource()
        {
            var files = SourceFiles().ToList();

            Assert.Greater(files.Count, 50,
                $"Only {files.Count} source files were found under {ScriptsRoot()}.");

            Assert.IsTrue(files.Any(path =>
                    string.Equals(Path.GetFileName(path), "LabCoat.cs", StringComparison.Ordinal)),
                "LabCoat.cs was not among the scanned files, so the exemption above is exempting " +
                "nothing and the check is passing for the wrong reason.");
        }

        /// <summary>
        /// Promise: wearing it puts it on the body, where the walk cycle can move it, and on the body
        /// layer, where the wearer's own eye camera culls it. Both halves matter — a coat parented to
        /// the torso but left on the default layer fills the wearer's screen.
        /// </summary>
        [Test]
        public void WearingTheCoat_HangsItOnTheBodysTorso()
        {
            var coat = NewCoat(out var garment);
            var body = NewBody(out var torso);

            Assert.IsTrue(coat.Wear(body));

            Assert.IsTrue(coat.IsWorn);
            Assert.AreSame(torso, garment.parent, "The coat did not reach the torso pivot.");
            Assert.AreEqual(ThirdPersonView.PlayerBodyLayer, garment.gameObject.layer,
                "A worn coat has to be on the layer the rest of the body is on, or its owner spends " +
                "the shift looking at the inside of it.");
        }

        /// <summary>
        /// Promise: taking it off restores the hanger exactly — parent, local pose and layer. The
        /// asymmetric version of this leaves a coat drifting a few centimetres further out of the
        /// locker every time somebody borrows it.
        /// </summary>
        [Test]
        public void HangingTheCoatUp_PutsItExactlyBack()
        {
            var coat = NewCoat(out var garment);
            var body = NewBody(out _);

            var hanger = garment.parent;
            Vector3 pose = garment.localPosition;
            int layer = garment.gameObject.layer;

            Assert.IsTrue(coat.Wear(body));
            Assert.IsTrue(coat.Hang());

            Assert.IsFalse(coat.IsWorn);
            Assert.AreSame(hanger, garment.parent);
            Assert.AreEqual(pose, garment.localPosition);
            Assert.AreEqual(layer, garment.gameObject.layer);
        }

        /// <summary>
        /// Promise: the coat refuses rather than half-works. A body with no torso wired is a scene
        /// wiring mistake, and a coat that silently reparented to nothing would leave the garment at
        /// the world origin with no way to get it back.
        /// </summary>
        [Test]
        public void TheCoat_RefusesABodyWithNowhereToHang()
        {
            var coat = NewCoat(out var garment);
            var hanger = garment.parent;

            var bare = new GameObject("Body_NoTorso");
            bare.SetActive(false);
            spawned.Add(bare);

            Assert.IsFalse(coat.Wear(bare.AddComponent<CharacterBody>()));
            Assert.IsFalse(coat.IsWorn);
            Assert.AreSame(hanger, garment.parent);
        }

        /// <summary>Promise: wearing it twice is not two coats. Hanging it twice is not an exception.</summary>
        [Test]
        public void TheCoat_IsIdempotentBothWays()
        {
            var coat = NewCoat(out _);
            var body = NewBody(out _);

            Assert.IsTrue(coat.Wear(body));
            Assert.IsFalse(coat.Wear(body));

            Assert.IsTrue(coat.Hang());
            Assert.IsFalse(coat.Hang());
        }

        // -----------------------------------------------------------------------------------------
        // The seat
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Promise: **the seat releases the player.** Everything it took away comes back — the motor,
        /// the eye height, and a position that is not inside the chair. A seat that keeps any one of
        /// them presents as the game having frozen, and the player has no way to argue with it.
        /// </summary>
        [Test]
        public void TheSeat_ReleasesThePlayer()
        {
            var player = NewPlayer(out var motor, out var head);
            var seat = NewSeat(new Vector3(3.72f, 0f, 1.02f), 8f);

            Assert.IsTrue(seat.Seat(player));
            Assert.IsTrue(player.IsSeated);
            Assert.IsTrue(seat.IsOccupied);
            Assert.IsFalse(motor.enabled, "Sitting parks the body by switching the motor off.");
            Assert.Less(head.localPosition.y, player.StandEyeHeight,
                "A seated player's eyes have to drop, or sitting is only a teleport.");

            seat.Release();

            Assert.IsFalse(player.IsSeated);
            Assert.IsFalse(seat.IsOccupied);
            Assert.IsTrue(motor.enabled, "The motor never came back, so the player cannot walk again.");
            Assert.AreEqual(player.StandEyeHeight, head.localPosition.y, 0.0001f);
        }

        /// <summary>
        /// Promise: the seat does not use <c>PlayerController.enabled</c> as its lock.
        /// <para>
        /// <c>TerminalScreen.Close</c> and <c>ShiftPause.End</c> both set that flag unconditionally
        /// back to true, so a seat built on it would be released by closing the very terminal the chair
        /// is pulled up to — and released without moving the player, leaving them stood inside the
        /// desk. Looking, aiming and every screen therefore keep working while seated, which is also
        /// the behaviour this asserts.
        /// </para>
        /// </summary>
        [Test]
        public void SittingDown_LeavesTheControllerItselfEnabled()
        {
            var player = NewPlayer(out _, out _);
            var seat = NewSeat(new Vector3(3.72f, 0f, 1.02f), 8f);

            Assert.IsTrue(seat.Seat(player));
            Assert.IsTrue(player.enabled,
                "The seat disabled PlayerController. Closing the terminal would then hand the player " +
                "back their legs while still sat in the chair.");
        }

        /// <summary>
        /// Promise: standing up does not strand you inside the furniture. The spot is derived from the
        /// seat's own facing, so it stays behind the chair however the chair is turned.
        /// </summary>
        [Test]
        public void StandingUp_PutsThePlayerClearOfTheChair()
        {
            var player = NewPlayer(out _, out _);
            var seat = NewSeat(new Vector3(3.72f, 0f, 1.02f), 8f);

            Assert.IsTrue(seat.Seat(player));
            seat.Release();

            Vector3 chair = seat.transform.position;
            Assert.Greater(Vector3.Distance(player.transform.position, chair), 0.5f,
                "The player was put down on top of the chair they just got out of.");

            // Behind it, in the seat's own -Z, which is away from whatever it is pulled up to.
            Assert.Less(Vector3.Dot(player.transform.position - chair, seat.transform.forward), 0f,
                "Standing up moved the player forwards, which at the terminal desk is into the desk.");
        }

        /// <summary>Promise: one chair, one occupant, and releasing an empty one is not an error.</summary>
        [Test]
        public void AnOccupiedSeat_TakesNobodyElseAndReleasesSafelyWhenEmpty()
        {
            var first = NewPlayer(out _, out _);
            var second = NewPlayer(out _, out _);
            var seat = NewSeat(Vector3.zero, 0f);

            seat.Release();
            Assert.IsFalse(seat.IsOccupied);

            Assert.IsTrue(seat.Seat(first));
            Assert.IsFalse(seat.Seat(second));
            Assert.IsFalse(second.IsSeated);

            seat.Release();
            seat.Release();
            Assert.IsFalse(seat.IsOccupied);
        }

        // -----------------------------------------------------------------------------------------
        // Fixtures
        // -----------------------------------------------------------------------------------------
        //
        // Everything is built on a deactivated GameObject, the house pattern from PlayerScreenTests.
        // An active PlayerController runs its Awake, which logs an error about the InputActionAsset
        // the suite has no reason to give it — and a logged error fails the test that caused it.

        private PlayerController NewPlayer(out CharacterController motor, out Transform head)
        {
            var go = new GameObject("Player_UnderTest");
            go.SetActive(false);
            spawned.Add(go);

            motor = go.AddComponent<CharacterController>();

            var headGo = new GameObject("Head");
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            head = headGo.transform;

            var player = go.AddComponent<PlayerController>();
            var so = new UnityEditor.SerializedObject(player);
            so.FindProperty("head").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        private LabSeat NewSeat(Vector3 position, float yaw)
        {
            var go = new GameObject("Chair_UnderTest");
            go.SetActive(false);
            spawned.Add(go);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go.AddComponent<LabSeat>();
        }

        private LabCoat NewCoat(out Transform garment)
        {
            var hanger = new GameObject("Buegel_UnderTest");
            hanger.SetActive(false);
            spawned.Add(hanger);

            var cloth = new GameObject("Kittel");
            cloth.transform.SetParent(hanger.transform, false);
            cloth.transform.localPosition = new Vector3(0f, -0.02f, 0f);

            var coat = hanger.AddComponent<LabCoat>();
            var so = new UnityEditor.SerializedObject(coat);
            so.FindProperty("garment").objectReferenceValue = cloth.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            garment = cloth.transform;
            return coat;
        }

        private CharacterBody NewBody(out Transform torso)
        {
            var root = new GameObject("Body_UnderTest");
            root.SetActive(false);
            spawned.Add(root);

            var torsoGo = new GameObject("Torso");
            torsoGo.transform.SetParent(root.transform, false);
            torso = torsoGo.transform;

            var body = root.AddComponent<CharacterBody>();
            var so = new UnityEditor.SerializedObject(body);
            so.FindProperty("torso").objectReferenceValue = torso;
            so.ApplyModifiedPropertiesWithoutUndo();
            return body;
        }

        private static string ScriptsRoot() => Path.Combine(Application.dataPath, "Scripts");

        private static IEnumerable<string> SourceFiles() =>
            Directory.Exists(ScriptsRoot())
                ? Directory.EnumerateFiles(ScriptsRoot(), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".Generated.cs", StringComparison.Ordinal))
                : Enumerable.Empty<string>();
    }
}
