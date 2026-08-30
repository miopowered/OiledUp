using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Residue.Gameplay.Settings;
using Residue.Gameplay.World;
using UnityEngine;

namespace Residue.Tests.EditMode
{
    /// <summary>
    /// The audio layer's two promises (#46): the room tone actually gets built, and every sound
    /// answers to the slider it belongs under.
    /// <para>
    /// Both are here because silence is the failure this project is least equipped to notice. There
    /// are no screenshots of audio, the EditMode suite is the only thing that runs without a human at
    /// the Editor, and the lab shipped mute for long enough that the README said so as a feature
    /// gap — while <c>LabAmbience</c> had been sitting in the scene the whole time, warning once
    /// every half minute that its clip was null.
    /// </para>
    /// </summary>
    public sealed class AudioTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();

            GameSettings.AmbienceVolume = 1f;
            GameSettings.EffectsVolume = 1f;
        }

        private AudioSource NewSource()
        {
            var go = new GameObject("TestSource");
            spawned.Add(go);
            return go.AddComponent<AudioSource>();
        }

        /// <summary>
        /// Promise: the room tone exists at all.
        /// <para>
        /// This is the test that was missing. The lab was silent and the only evidence was a runtime
        /// warning about a null clip, which reads like a lifetime bug and sent the investigation
        /// looking for something destroying the clip — when nothing had built it. Asserting the build
        /// directly says which of those two it is, without an Editor.
        /// </para>
        /// </summary>
        [Test]
        public void TheRoomTone_IsActuallyBuilt()
        {
            var go = new GameObject("Ambience");
            spawned.Add(go);
            var ambience = go.AddComponent<LabAmbience>();

            ambience.BuildClips();

            foreach (var field in typeof(LabAmbience).GetFields(
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Instance))
            {
                if (field.FieldType != typeof(AudioClip)) continue;

                var clip = (AudioClip)field.GetValue(ambience);
                Assert.IsNotNull(clip,
                    $"LabAmbience.{field.Name} did not get built, so the lab plays nothing and every " +
                    "source holding it warns once per tick for the rest of the session.");
                Assert.Greater(clip.samples, 0, $"LabAmbience.{field.Name} is an empty clip.");
            }
        }

        /// <summary>
        /// Promise: the ambience slider reaches the room tone.
        /// <para>
        /// Half volume means half, and it is applied on registration rather than only on the next
        /// change — a source created while the slider was already down must not start at full and
        /// duck a moment later.
        /// </para>
        /// </summary>
        [Test]
        public void ASourceRegistered_TakesItsCategorySliderImmediately()
        {
            GameSettings.AmbienceVolume = 0.5f;

            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Ambience, 0.2f);

            Assert.AreEqual(0.1f, source.volume, 1e-4f,
                "A source joining a bus whose slider is already down has to arrive at that volume.");
        }

        /// <summary>
        /// Promise: moving a slider moves what is already playing.
        /// <para>
        /// And moving it back restores the authored volume exactly. The bus keeps the authored value
        /// rather than reading it back off the source for this reason: a bus that re-read would
        /// compound every change, so the room would get quieter each time the slider was touched and
        /// no setting could recover it.
        /// </para>
        /// </summary>
        [Test]
        public void MovingASlider_MovesWhatIsAlreadyPlaying_AndBackAgain()
        {
            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Ambience, 0.4f);

            GameSettings.AmbienceVolume = 0.25f;
            Assert.AreEqual(0.1f, source.volume, 1e-4f, "The slider has to reach a live source.");

            GameSettings.AmbienceVolume = 0.5f;
            GameSettings.AmbienceVolume = 1f;
            Assert.AreEqual(0.4f, source.volume, 1e-4f,
                "Back at full, a source must be at its authored volume — not compounded down by the " +
                "trip through the other values.");
        }

        /// <summary>
        /// Promise: the two sliders are actually separate.
        /// <para>
        /// A settings screen with four sliders that all move the same gain is worse than one slider,
        /// because it tells the player something untrue about what they control.
        /// </para>
        /// </summary>
        [Test]
        public void TheCategories_DoNotBleedIntoEachOther()
        {
            var ambience = NewSource();
            var effects = NewSource();
            AudioBus.Register(ambience, AudioCategory.Ambience, 1f);
            AudioBus.Register(effects, AudioCategory.Effects, 1f);

            GameSettings.AmbienceVolume = 0f;

            Assert.AreEqual(0f, ambience.volume, 1e-4f, "Ambience should have followed its own slider.");
            Assert.AreEqual(1f, effects.volume, 1e-4f, "Effects should not have moved.");
        }

        /// <summary>
        /// Promise: a destroyed source does not keep the bus alive or throw on the next slider move.
        /// <para>
        /// Props are created and destroyed constantly — a slip prints, a carton is discarded — so
        /// this is the ordinary case rather than an edge one, and a caller that forgets to unregister
        /// must not be able to break the settings screen for everything else.
        /// </para>
        /// </summary>
        [Test]
        public void ADestroyedSource_IsDroppedRatherThanThrowing()
        {
            var source = NewSource();
            AudioBus.Register(source, AudioCategory.Effects, 1f);
            int before = AudioBus.Count;

            UnityEngine.Object.DestroyImmediate(source.gameObject);

            Assert.DoesNotThrow(() => AudioBus.Apply(),
                "A slider move after a prop was destroyed must not throw.");
            Assert.Less(AudioBus.Count, before, "The dead source should have been dropped.");
        }

        /// <summary>
        /// Promise: registering the same source twice re-routes it rather than stacking it.
        /// <para>
        /// A prop that is re-bound — which pooled printouts are, every frame — would otherwise grow
        /// the list without bound and pay for every past registration on every slider move.
        /// </para>
        /// </summary>
        [Test]
        public void RegisteringTwice_ReplacesRatherThanStacks()
        {
            var source = NewSource();

            AudioBus.Register(source, AudioCategory.Ambience, 1f);
            int after = AudioBus.Count;
            AudioBus.Register(source, AudioCategory.Effects, 0.5f);

            Assert.AreEqual(after, AudioBus.Count, "A re-registration must not add a second entry.");

            GameSettings.AmbienceVolume = 0f;
            Assert.AreEqual(0.5f, source.volume, 1e-4f,
                "The source should be on Effects now, so the Ambience slider must not touch it.");
        }

        // -- The sound bank ---------------------------------------------------------------------------

        /// <summary>
        /// Every instrument in <c>ContentTables.Machines</c>. Written out rather than read off the
        /// catalog on purpose: these tests must not need a <c>ContentCatalog</c> loaded, and a row
        /// added to the tables without a voice added to <c>LabSoundBank</c> is exactly the change
        /// this list is here to make visible.
        /// </summary>
        private static readonly string[] Instruments =
        {
            "cooling_curve", "karl_fischer", "viscometer", "flash_point",
            "tan_titrator", "centrifuge", "elemental"
        };

        private const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
            BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Promise: the bank actually builds. Same failure mode as the room tone, one layer up — a
        /// clip that never gets made is silence plus a warning once per press, and there is no
        /// screenshot of it.
        /// <para>
        /// Discovered by reflection rather than listed, so a sound added later is covered without
        /// anyone remembering to extend this.
        /// </para>
        /// </summary>
        [Test]
        public void TheSoundBank_BuildsEveryClip()
        {
            foreach (var clip in SharedClips())
            {
                Assert.IsNotNull(clip.Value, $"LabSoundBank.{clip.Key} did not get built.");
                Assert.Greater(clip.Value.samples, 0, $"LabSoundBank.{clip.Key} is an empty clip.");
            }

            foreach (string id in Instruments)
            {
                var clip = LabSoundBank.RunFinished(id);
                Assert.IsNotNull(clip, $"No run-finished chime for '{id}', so it finishes in silence.");
                Assert.Greater(clip.samples, 0, $"The chime for '{id}' is an empty clip.");
            }
        }

        /// <summary>
        /// Promise: one clip per sound, not one per caller.
        /// <para>
        /// Props are pooled and reconciled — <c>SlipReconciler</c> re-binds every slip every frame —
        /// so a bank that built on access rather than caching would synthesise a couple of hundred
        /// thousand samples on frames that have no business allocating anything. It is also what
        /// makes the guard below meaningful: a memoised lookup has no room for a second answer.
        /// </para>
        /// </summary>
        [Test]
        public void TheSoundBank_HandsOutOneClipPerSound_NotOnePerCaller()
        {
            foreach (var clip in SharedClips())
            {
                Assert.AreSame(clip.Value, SharedClips()[clip.Key],
                    $"LabSoundBank.{clip.Key} built a second clip for the second caller.");
            }

            foreach (string id in Instruments)
            {
                Assert.AreSame(LabSoundBank.RunFinished(id), LabSoundBank.RunFinished(id),
                    $"The chime for '{id}' is being rebuilt on every run.");
            }

            // The running loop is deliberately one clip for the whole lab, distinguished by playback
            // pitch rather than by seven copies of the same two seconds of motor.
            Assert.AreSame(LabSoundBank.MachineLoop, LabSoundBank.MachineLoop);
        }

        /// <summary>
        /// Promise: four instruments across the room are four different sounds.
        /// <para>
        /// This is the half of #46 that changes how the game plays. A single chime shared by every
        /// machine tells you that something finished and sends you on the same patrol you were
        /// already walking; the point is to know <i>which</i> box to walk to.
        /// </para>
        /// Compared on the samples rather than on the reference, because two table rows with the
        /// same numbers in them would be two objects and one sound.
        /// </summary>
        [Test]
        public void EveryInstrument_FinishesWithItsOwnVoice()
        {
            var fingerprints = new Dictionary<string, string>();

            foreach (string id in Instruments)
            {
                string print = Fingerprint(LabSoundBank.RunFinished(id));
                var clash = fingerprints.FirstOrDefault(p => p.Value == print);

                Assert.IsNull(clash.Key,
                    $"'{id}' finishes with the same sound as '{clash.Key}'. Two instruments that " +
                    "sound alike are one instrument as far as the player across the room is " +
                    "concerned; give it its own row in LabSoundBank.Voices.");

                fingerprints[id] = print;
            }

            // And the running loops are told apart too, by pitch, since they share a clip.
            var pitches = Instruments.Select(LabSoundBank.RunningPitch).ToArray();
            Assert.AreEqual(pitches.Length, pitches.Distinct().Count(),
                "Two instruments run at the same pitch, so a busy bench is one sound played twice.");
        }

        /// <summary>
        /// <b>The guard.</b> Promise: a sound never encodes a result.
        ///
        /// <para>
        /// A run-finished chime that soured on a bad number would be a verdict handed to every
        /// client through the speakers — ahead of the measurement, outside every check that guards
        /// the wire, and invisible to the reflection sweep in <c>NetworkViewTests</c> because it
        /// never touches a view. Hard rules 1 and 2 both, in a channel neither was written for. It
        /// would also break the game rather than the build: a player who could hear "that one is
        /// bad" would stop reading the instrument.
        /// </para>
        ///
        /// <para>
        /// So the whole of <c>LabSoundBank</c> is checked, not just the one method — a private
        /// helper taking a <c>TestResult</c> would be the same leak one call deeper. Types from
        /// <c>Residue.Chemistry</c> and <c>Residue.Gameplay.Simulation</c> are barred outright,
        /// which catches <c>TestResult</c>, <c>SampleState</c>, <c>SampleGroundTruth</c>,
        /// <c>ReadingSeverity</c>, <c>FaultSeverity</c> and <c>MachineInstance</c> at once and needs
        /// no list to stay current. The word sweep catches what a type cannot: a <c>bool wasBad</c>
        /// is a leak with no interesting type on it at all.
        /// </para>
        ///
        /// <para>
        /// "Sample" is not on the word list and cannot be: in audio it means a PCM frame, and
        /// <c>SampleRate</c> is not a chemistry term. That is what the type ban is for.
        /// </para>
        /// </summary>
        [Test]
        public void NoSound_CanBeToldWhatTheRunFound()
        {
            var offenders = new List<string>();

            foreach (var (member, type) in TypesTouchedBy(typeof(LabSoundBank)))
            {
                if (IsVerdictType(type))
                    offenders.Add($"LabSoundBank.{member} is typed on {type.FullName}");
            }

            // A verdict does not need an interesting type. "bool contaminated", "int severity",
            // "float drift" would all get through the sweep above.
            string[] forbidden =
            {
                "Result", "Verdict", "Severity", "Fault", "Truth", "Contamin", "Drift",
                "Blank", "Caution", "Critical", "Reading", "Healthy", "Suspect"
            };

            foreach (var (member, _) in TypesTouchedBy(typeof(LabSoundBank)))
            {
                foreach (string word in forbidden)
                {
                    if (member.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        offenders.Add($"LabSoundBank.{member} is named after a verdict (matches '{word}')");
                }
            }

            Assert.IsEmpty(offenders,
                "A sound must differ by which instrument finished and by nothing else:\n  " +
                string.Join("\n  ", offenders.Distinct()));

            // And structurally: the only thing anyone can hand the bank is a string.
            foreach (var method in typeof(LabSoundBank).GetMethods(BindingFlags.Public | BindingFlags.Static |
                                                                   BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in method.GetParameters())
                {
                    Assert.AreEqual(typeof(string), parameter.ParameterType,
                        $"LabSoundBank.{method.Name} takes a {parameter.ParameterType.Name}. The bank's " +
                        "public surface is 'which instrument, by name' on purpose — anything richer is " +
                        "a door a verdict can walk through.");
                }
            }
        }

        /// <summary>
        /// The other half of the guard, at the call site rather than at the bank.
        ///
        /// <para>
        /// A clean signature on <c>RunFinished</c> proves nothing on its own if the method calling
        /// it is <c>OnRunCompleted(MachineInstance, TestResult)</c> — the numbers would be in scope
        /// and one <c>if</c> away from picking a different clip, and the test above would still
        /// pass. So this reads the IL of every method in <c>Residue.Gameplay</c>, finds the ones
        /// that actually call <c>LabSoundBank.RunFinished</c>, and requires that none of them was
        /// handed anything from the simulation.
        /// </para>
        ///
        /// <para>
        /// It also asserts that <i>something</i> calls it. Without that this test passes loudest on
        /// the day the sound is deleted, which is the failure it exists to prevent.
        /// </para>
        ///
        /// <para>
        /// There is a second reason the caller must be verdict-free, and it is not about secrecy:
        /// <c>LabState.RunCompleted</c> is an event on the host's own lab and has no replicated
        /// twin, so a chime raised from it would never sound on a joined client. The rule and the
        /// co-op requirement point at the same line — the edge on
        /// <c>IMachineView.IsRunning</c>, which both sides can read and which says only that the
        /// box stopped.
        /// </para>
        /// </summary>
        [Test]
        public void TheRunFinishedSound_IsAskedForByCodeThatHoldsNoNumbers()
        {
            var target = typeof(LabSoundBank).GetMethod(nameof(LabSoundBank.RunFinished),
                                                        BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(target, "LabSoundBank.RunFinished has been renamed; update this guard.");

            // A MethodDef token, or this sweep is looking for the wrong four bytes. A runtime that
            // hands out 0 would match every zero word in every method body and fail this test
            // everywhere at once, which would read as a catastrophe rather than as an unsupported
            // reflection API.
            int metadataToken = target.MetadataToken;
            if ((metadataToken & unchecked((int)0xFF000000)) != 0x06000000)
            {
                Assert.Ignore("This runtime does not hand out method tokens, so the call sites cannot " +
                              "be read. NoSound_CanBeToldWhatTheRunFound still covers the bank itself.");
            }

            byte[] token = BitConverter.GetBytes(metadataToken);

            int bodiesRead = 0;
            var callers = new List<MethodBase>();

            foreach (var type in LoadableTypes(typeof(MachineStation).Assembly))
            {
                IEnumerable<MethodBase> members;
                try
                {
                    members = type.GetMethods(Everything).Cast<MethodBase>()
                                  .Concat(type.GetConstructors(Everything));
                }
                catch (Exception) { continue; }

                foreach (var method in members)
                {
                    byte[] il;
                    try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                    catch (Exception) { continue; }

                    if (il == null) continue;
                    bodiesRead++;

                    if (Contains(il, token)) callers.Add(method);
                }
            }

            if (bodiesRead == 0)
            {
                Assert.Ignore("This runtime does not hand out IL, so the call sites cannot be read. " +
                              "NoSound_CanBeToldWhatTheRunFound still covers the bank itself.");
            }

            Assert.IsNotEmpty(callers,
                "Nothing in Residue.Gameplay asks for a run-finished chime any more. Either the sound " +
                "was deleted — in which case #46's whole point went with it — or it moved to an " +
                "assembly this sweep does not read.");

            foreach (var caller in callers)
            {
                foreach (var parameter in caller.GetParameters())
                {
                    Assert.IsFalse(IsVerdictType(parameter.ParameterType),
                        $"{caller.DeclaringType?.Name}.{caller.Name} asks for a run-finished chime " +
                        $"while holding a {parameter.ParameterType.Name}. Even if it does not read it " +
                        "today, the numbers are in scope and one 'if' away from choosing the clip — " +
                        "and a method that can see a TestResult is a method that only runs on the " +
                        "host, so a joined client would hear nothing at all.");
                }
            }
        }

        // -- Helpers ----------------------------------------------------------------------------------

        /// <summary>Every clip the bank exposes as a property, by name. Reflected so the list cannot go stale.</summary>
        private static Dictionary<string, AudioClip> SharedClips()
        {
            var clips = new Dictionary<string, AudioClip>();

            foreach (var property in typeof(LabSoundBank).GetProperties(
                         BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (property.PropertyType != typeof(AudioClip)) continue;
                clips[property.Name] = (AudioClip)property.GetValue(null);
            }

            Assert.IsNotEmpty(clips, "LabSoundBank exposes no clips at all.");
            return clips;
        }

        /// <summary>
        /// A cheap summary of what a clip sounds like: its length plus the energy in the first
        /// tenth of a second. Two chimes that agree on both are the same sound to a player, whatever
        /// the table says.
        /// </summary>
        private static string Fingerprint(AudioClip clip)
        {
            int window = Mathf.Min(clip.samples, 2400);
            var data = new float[window * clip.channels];
            clip.GetData(data, 0);

            double energy = 0d;
            for (int i = 0; i < data.Length; i++) energy += Mathf.Abs(data[i]);

            return $"{clip.samples}:{energy:F3}";
        }

        /// <summary>
        /// Anything the host simulates. Barred from the sound layer wholesale rather than by name,
        /// because that is the boundary CLAUDE.md's assembly diagram already draws and it needs no
        /// list to stay current.
        /// </summary>
        private static bool IsVerdictType(Type type)
        {
            while (type != null && (type.IsByRef || type.IsArray || type.IsPointer))
                type = type.GetElementType();

            if (type == null) return false;

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    if (IsVerdictType(argument)) return true;
            }

            string ns = type.Namespace;
            return ns != null &&
                   (ns.StartsWith("Residue.Chemistry", StringComparison.Ordinal) ||
                    ns.StartsWith("Residue.Gameplay.Simulation", StringComparison.Ordinal));
        }

        /// <summary>Every type a member of <paramref name="type"/> mentions, nested types included.</summary>
        private static IEnumerable<(string Member, Type Type)> TypesTouchedBy(Type type)
        {
            foreach (var field in type.GetFields(Everything))
                yield return (field.Name, field.FieldType);

            foreach (var property in type.GetProperties(Everything))
                yield return (property.Name, property.PropertyType);

            foreach (var method in type.GetMethods(Everything))
            {
                yield return (method.Name, method.ReturnType);
                foreach (var parameter in method.GetParameters())
                    yield return ($"{method.Name}({parameter.Name})", parameter.ParameterType);
            }

            foreach (var nested in type.GetNestedTypes(Everything))
                foreach (var member in TypesTouchedBy(nested))
                    yield return ($"{nested.Name}.{member.Member}", member.Type);
        }

        /// <summary>
        /// Types from an assembly, minus any that will not load. A partial sweep is worth more than
        /// a test that throws on an unrelated missing optional dependency.
        /// </summary>
        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return true;
            }
            return false;
        }
    }
}
