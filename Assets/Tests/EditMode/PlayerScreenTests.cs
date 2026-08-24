using System.Collections.Generic;
using NUnit.Framework;
using Residue.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// Guards that the terminal screen belongs to a <i>player</i> and reference books do not.
    /// <para>
    /// Until M4 there was one HUD, one terminal view and one reading view, sitting at the scene root
    /// and wired to the only player there was. A station that opens a fixed screen is correct exactly
    /// once; with four players in the room it shows player A's terminal to player B, which is not a
    /// cosmetic bug — the buttons on it file verdicts against the record.
    /// </para>
    /// <para>
    /// These tests pin the terminal resolution rule. Books intentionally have no screen resolution
    /// anymore: their words are rasterised on their physical page mesh during inspection. What the
    /// terminal tests cannot reach is the drawing itself — a <c>UIDocument</c> only owns a panel while it is enabled and the test
    /// runner has no panel settings to give it, so "the right screen was raised" is asserted on the
    /// resolution and not on pixels.
    /// </para>
    /// </summary>
    public sealed class PlayerScreenTests
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

        /// <summary>
        /// A player object shaped the way M4 spawns one: screens parented underneath rather than
        /// pointed at from the scene. Left inactive because none of this needs a running frame, and
        /// because a replica's screens are inactive too — which is precisely the case the lookup has
        /// to keep working for.
        /// </summary>
        private PlayerInteractor NewPlayer(bool withScreens)
        {
            var root = new GameObject("Player_UnderTest");
            root.SetActive(false);
            spawned.Add(root);

            if (withScreens)
            {
                var terminal = new GameObject("TerminalUI");
                terminal.transform.SetParent(root.transform);
                terminal.AddComponent<TerminalScreen>();

            }

            return root.AddComponent<PlayerInteractor>();
        }

        private T Loose<T>(string label) where T : Component
        {
            var go = new GameObject(label);
            go.SetActive(false);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        private TerminalStation NewTerminal(TerminalScreen fallback)
        {
            var station = Loose<TerminalStation>("Terminal_UnderTest");
            if (fallback == null) return station;

            var so = new UnityEditor.SerializedObject(station);
            so.FindProperty("screen").objectReferenceValue = fallback;
            so.ApplyModifiedPropertiesWithoutUndo();
            return station;
        }

        // -- Resolution -------------------------------------------------------------------------------

        [Test]
        public void APlayer_FindsTheScreensItCarries()
        {
            var player = NewPlayer(withScreens: true);

            Assert.IsNotNull(player.Terminal,
                "A player carrying its own terminal view must find it without being wired to one. " +
                "There is no scene build step left once the player is a spawned prefab.");
        }

        [Test]
        public void APlayer_WithNoScreens_BorrowsNobodyElses()
        {
            NewPlayer(withScreens: true);
            var bare = NewPlayer(withScreens: false);

            Assert.IsNull(bare.Terminal,
                "The lookup reached outside this player and found someone else's screen. That is the " +
                "singleton coming back: whoever spawned first would own every terminal in the lab.");
        }

        [Test]
        public void TwoPlayers_EachResolveTheirOwn()
        {
            var a = NewPlayer(withScreens: true);
            var b = NewPlayer(withScreens: true);

            Assert.AreNotSame(a.Terminal, b.Terminal);
        }

        // -- Terminal ---------------------------------------------------------------------------------

        [Test]
        public void ATerminal_RaisesTheScreenOfWhoeverPressedIt()
        {
            var a = NewPlayer(withScreens: true);
            var b = NewPlayer(withScreens: true);

            // Wired to a third, shared screen on purpose: a scene that still has one must not be able
            // to override the player who is actually standing there.
            var shared = Loose<TerminalScreen>("SharedTerminalUI");
            var station = NewTerminal(shared);

            Assert.AreSame(a.Terminal, station.ScreenFor(a));
            Assert.AreSame(b.Terminal, station.ScreenFor(b));
        }

        [Test]
        public void ATerminal_FallsBackToItsWiredScreen_ForAPlayerCarryingNone()
        {
            var player = NewPlayer(withScreens: false);
            var shared = Loose<TerminalScreen>("SharedTerminalUI");

            Assert.AreSame(shared, NewTerminal(shared).ScreenFor(player));
        }

        [Test]
        public void ATerminal_WithNoScreenAnywhere_SaysSoRatherThanGoingDead()
        {
            var player = NewPlayer(withScreens: false);
            var station = NewTerminal(null);

            Assert.IsFalse(station.CanInteract(player));
            StringAssert.Contains("no display", station.Prompt(player),
                "§9: an interactable that refuses in silence reads as a broken interaction. It has " +
                "to name the reason even when the reason is a build fault.");
        }

        [Test]
        public void ATerminal_TakesASlip_EvenWithNoScreen()
        {
            var player = NewPlayer(withScreens: false);
            var station = NewTerminal(null);

            var slip = Loose<PrintoutProp>("Printout_UnderTest");
            Assert.IsTrue(player.TryCarry(slip));

            Assert.IsTrue(station.CanInteract(player),
                "Filing is a hand-over at the desk, not something you read. Gating it on a display " +
                "would strand the slip in the player's hands with the day clock running.");
        }

        // -- Book -------------------------------------------------------------------------------------

        [Test]
        public void ABook_HasNoLegacyScreenOrPrimaryUse()
        {
            var book = Loose<ReferenceBook>("Book_UnderTest");
            Assert.IsNull(book.UseHint);
            Assert.IsNull(book.InspectionText,
                "Reference text belongs on the physical pages, not in the HUD text overlay.");
            Assert.IsNull(typeof(ReferenceBook).GetField("screen",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        }

        [Test]
        public void ABook_BuildsAThreeDimensionalPageSurfaceForInspection()
        {
            var book = Loose<ReferenceBook>("Book_UnderTest");

            Assert.DoesNotThrow(book.BeginInspection);
            var surface = book.GetComponent<InspectableBookSurface>();
            Assert.IsNotNull(surface);
            Assert.GreaterOrEqual(surface.PageCount, 1);
            var pages = surface.transform.Find("OpenPages");
            Assert.IsTrue(pages.gameObject.activeSelf);
            Assert.IsNotNull(pages.GetComponent<Renderer>().sharedMaterial.mainTexture,
                "The reference words must be a texture on the physical page renderer.");
            Assert.Greater(pages.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, 0.02f,
                "The replacement book must have physical edge thickness, not be another 2D plane.");
            book.EndInspection();
            Assert.IsTrue(pages.gameObject.activeSelf,
                "Inspection only moves the item into focus; it must not create or hide its text pages.");
        }

        // -- Screens with no panel --------------------------------------------------------------------
        //
        // A remote player's screens are switched off with the rest of that avatar, and a UIDocument
        // owns no rootVisualElement while it is disabled. Both screens used to cache that element in
        // Awake, which made a replica's copy throw on construction.

        [Test]
        public void ATerminalScreen_WithNoPanel_DeclinesToOpen()
        {
            var screen = Loose<TerminalScreen>("TerminalUI_NoPanel");

            Assert.DoesNotThrow(() => screen.Open());
            Assert.IsFalse(screen.IsOpen,
                "A screen with nothing to draw into reported itself open. It would then hold the " +
                "player's controls disabled with no visible way to close it.");
        }

    }
}
