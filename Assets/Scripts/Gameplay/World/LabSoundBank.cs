using System.Collections.Generic;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Every sound the lab makes that is not room tone (#46), synthesised once and shared.
    ///
    /// <para>
    /// <b>The rule this type exists to keep.</b> A sound must never encode a result. The one that
    /// matters — <see cref="RunFinished"/> — takes an instrument id and nothing else, so a chime can
    /// differ by <i>which</i> machine finished and cannot differ by what the machine found. That is
    /// hard rules 1 and 2 in a channel the reflection sweep in <c>NetworkViewTests</c> cannot see: a
    /// chime that soured on a bad number would hand every client a verdict nobody measured, and it
    /// would do it before the vial was even collected. There is deliberately no overload here that
    /// could be handed a <c>TestResult</c>, a severity or a sample — <c>AudioTests</c> asserts that
    /// there is not, because "we remembered not to" is not a boundary.
    /// </para>
    ///
    /// <para>
    /// <b>Why the clips are generated rather than imported.</b> The same argument
    /// <c>ContentTables</c> makes for balance data and <see cref="LabAmbience"/> makes for the room
    /// tone: a wave file is opaque in a diff, needs Git LFS, and cannot be tuned in a pull request.
    /// A chime is a decay envelope and two partials, and those are three readable numbers in the
    /// table below.
    /// </para>
    ///
    /// <para>
    /// <b>Static, and memoised per instrument.</b> Props are pooled and reconciled — <c>SlipReconciler</c>
    /// re-binds every slip every frame — so a clip built per component, per bind or per run would
    /// rebuild a couple of hundred thousand samples on a frame that had no business allocating
    /// anything. Every caller gets the same <see cref="AudioClip"/> reference for the same sound, for
    /// the whole session, and <c>AudioTests</c> pins that too.
    /// </para>
    ///
    /// <para>
    /// The synthesis helpers at the bottom are deliberately the same shape as
    /// <see cref="LabAmbience"/>'s — its own xorshift rather than <see cref="UnityEngine.Random"/>,
    /// so a build is reproducible, and its crossfade at the loop seam. They are copied rather than
    /// shared because they are private to that component and hoisting them into a third type would
    /// be a bigger change than the fifteen lines it saved.
    /// </para>
    /// </summary>
    public static class LabSoundBank
    {
        private const int SampleRate = 24000;

        // -- Run finished ------------------------------------------------------------------------

        /// <summary>
        /// The chime an instrument makes when its run ends, keyed on <b>which instrument</b>.
        ///
        /// <para>
        /// This is the sound #46 says changes how the game plays: without it, keeping four
        /// instruments busy means walking a patrol, because the only way to learn that a centrifuge
        /// has finished is to go and look at it. Distinguishable per instrument so the answer to
        /// "what finished?" arrives with the sound rather than after the walk.
        /// </para>
        ///
        /// <para>
        /// <b>The signature is the guard.</b> A string in, a clip out, memoised — so the clip for
        /// <c>centrifuge</c> is the same object on the run that came back clean, the run that came
        /// back critical, the blank, the certified standard and the run on a drifted instrument.
        /// There is no argument through which a verdict could reach this, which is why the caller
        /// (<see cref="MachineStation"/>) raises it from the edge on
        /// <see cref="IMachineView.IsRunning"/> and not from <c>OnRunCompleted</c>, which holds the
        /// numbers and only ever runs on the host.
        /// </para>
        /// </summary>
        public static AudioClip RunFinished(string instrumentId)
        {
            string key = string.IsNullOrEmpty(instrumentId) ? "instrument" : instrumentId;

            // Unity's == rather than a plain null check: a clip built in a previous play session is
            // a destroyed object that is not reference-null, and playing one is silence plus a
            // warning per occurrence.
            if (runFinished.TryGetValue(key, out var cached) && cached != null) return cached;

            var clip = BuildChime(key, VoiceFor(key));
            runFinished[key] = clip;
            return clip;
        }

        /// <summary>
        /// What to pitch <see cref="MachineLoop"/> at for this instrument, so a room with four
        /// machines working does not sound like one machine playing four times. Comes out of the
        /// same voice row as the chime, so the running sound and the finished sound agree about
        /// which box they belong to.
        /// </summary>
        public static float RunningPitch(string instrumentId) => VoiceFor(instrumentId).LoopPitch;

        private static readonly Dictionary<string, AudioClip> runFinished = new();

        // -- Shared clips ------------------------------------------------------------------------

        /// <summary>
        /// The loop an instrument runs on, pitched per machine by <see cref="RunningPitch"/>.
        /// <para>
        /// <b>It knows nothing about how long a run takes.</b> That is deliberate:
        /// <c>LabRuntime.machineTimeScale</c> is a testing knob currently set to 0.05 in the scene,
        /// so the 300 s a viscometer advertises is fifteen seconds in practice and a loop built to
        /// fill a run would be wrong by a factor of twenty. It starts when the instrument starts and
        /// stops when it stops; the information is in the silence, which is what #46 asked for.
        /// </para>
        /// </summary>
        public static AudioClip MachineLoop =>
            machineLoop != null ? machineLoop : (machineLoop = BuildMachineLoop());

        /// <summary>
        /// Liquid in a bottle being shaken. Played while the player holds Interact at an instrument,
        /// which since #73 is where §4.5's agitation cost is actually paid — a 2.5 s hold that made
        /// no sound at all read as an unresponsive button rather than as work being done.
        /// </summary>
        public static AudioClip Agitate =>
            agitate != null ? agitate : (agitate = BuildAgitateLoop());

        /// <summary>The wash station's tap. #46's fourth item: a 4 s hold with nothing but a progress bar.</summary>
        public static AudioClip SolventPour =>
            solventPour != null ? solventPour : (solventPour = BuildPourLoop());

        /// <summary>Glass leaving a shelf.</summary>
        public static AudioClip PickUp => pickUp != null
            ? pickUp
            : (pickUp = BuildTransient("Pick up", 0.14f, 1480f, 2360f, 0.35f, 60f, 0.30f, 190f, 0x50494b31u));

        /// <summary>Glass meeting a surface. Duller and lower than <see cref="PickUp"/> on purpose.</summary>
        public static AudioClip PutDown => putDown != null
            ? putDown
            : (putDown = BuildTransient("Put down", 0.20f, 520f, 780f, 0.45f, 34f, 0.45f, 120f, 0x50555431u));

        /// <summary>
        /// A vial seated in an instrument: a thump and then the latch. Distinct from
        /// <see cref="PutDown"/> because loading is the action with the time cost on it, and the two
        /// should not be confusable at the moment a 2.5 s hold pays off.
        /// </summary>
        public static AudioClip Load => load != null ? load : (load = BuildLoadClunk());

        /// <summary>
        /// The lab declining to do something.
        /// <para>
        /// Deliberately a dull, short, low thud rather than a buzzer or a descending two-tone. Hard
        /// rule 4 is about colour and does not literally apply, but its spirit does: a sound that
        /// means "bad" is a signal channel and this game reserves those for a verdict the player
        /// measured. A refusal is a door that did not open, not a diagnosis — and the sentence
        /// <c>PlayerInteractor.Say</c> puts on screen is what carries the reason.
        /// </para>
        /// </summary>
        public static AudioClip Refused => refused != null
            ? refused
            : (refused = BuildTransient("Refused", 0.26f, 132f, 196f, 0.50f, 22f, 0.18f, 70f, 0x52454631u));

        private static AudioClip machineLoop, agitate, solventPour, pickUp, putDown, load, refused;

        /// <summary>
        /// Statics survive an Enter Play Mode that skips the domain reload, so without this the bank
        /// would hand out last session's clips — every one of them a destroyed object, and Unity's
        /// only complaint a "PlayOneShot was called with a null AudioClip" once per press. The same
        /// hazard <see cref="AudioBus"/> resets for, and the same reason #46 was hard to see.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCache()
        {
            runFinished.Clear();
            machineLoop = agitate = solventPour = pickUp = putDown = load = refused = null;
        }

        // -- Voices ------------------------------------------------------------------------------

        /// <summary>
        /// One instrument's character. Everything here is timbre — nothing in this struct can depend
        /// on, or express, what a run found.
        /// </summary>
        private readonly struct Voice
        {
            /// <summary>Hz of the first note.</summary>
            public readonly float Root;

            /// <summary>How many notes the chime has. One is a ping, three is a phrase.</summary>
            public readonly int Notes;

            /// <summary>Frequency ratio between successive notes. Below 1 falls, above 1 rises.</summary>
            public readonly float Step;

            /// <summary>Seconds between note onsets.</summary>
            public readonly float NoteSeconds;

            /// <summary>Exponential decay rate per note. Large is a click, small is a bell.</summary>
            public readonly float Decay;

            /// <summary>Ratio of the second partial to the fundamental, and how loud it is.</summary>
            public readonly float Partial, PartialGain;

            /// <summary>Strike transient. Mechanical instruments get more of it.</summary>
            public readonly float Noise;

            /// <summary>Playback pitch for the shared running loop.</summary>
            public readonly float LoopPitch;

            public Voice(float root, int notes, float step, float noteSeconds, float decay,
                         float partial, float partialGain, float noise, float loopPitch)
            {
                Root = root;
                Notes = Mathf.Max(1, notes);
                Step = step;
                NoteSeconds = noteSeconds;
                Decay = decay;
                Partial = partial;
                PartialGain = partialGain;
                Noise = noise;
                LoopPitch = loopPitch;
            }
        }

        /// <summary>
        /// One row per instrument in <c>ContentTables.Machines</c>, keyed by <c>MachineDef.Id</c>.
        /// <para>
        /// Written out rather than derived from a hash, because "distinguishable" is the whole
        /// requirement and a hash cannot promise that seven ids land seven recognisable places. The
        /// rows are spread across register, note count and direction so that hearing one across the
        /// room is enough: the cooling curve — the 900 s monster — gets a low falling pair, the Karl
        /// Fischer a thin triple blip, the centrifuge a spin-down octave drop.
        /// </para>
        /// An instrument absent from this table still gets a voice; see <see cref="VoiceFor"/>.
        /// </summary>
        private static readonly Dictionary<string, Voice> Voices = new()
        {
            //                            root  notes  step  gap    decay  partial  pGain  noise  loop
            ["cooling_curve"] = new Voice(196f, 2, 0.75f, 0.34f, 2.6f, 2.76f, 0.22f, 0.05f, 0.70f),
            ["karl_fischer"] = new Voice(988f, 3, 1.00f, 0.09f, 15.0f, 3.00f, 0.14f, 0.22f, 1.34f),
            ["viscometer"] = new Voice(392f, 1, 1.00f, 0.00f, 2.2f, 2.00f, 0.32f, 0.02f, 0.95f),
            ["flash_point"] = new Voice(740f, 2, 1.00f, 0.12f, 9.0f, 4.20f, 0.36f, 0.34f, 1.12f),
            ["tan_titrator"] = new Voice(523f, 3, 1.26f, 0.16f, 6.0f, 2.40f, 0.24f, 0.08f, 1.02f),
            ["centrifuge"] = new Voice(330f, 2, 0.50f, 0.42f, 2.4f, 1.50f, 0.40f, 0.13f, 0.84f),
            ["elemental"] = new Voice(659f, 2, 1.50f, 0.14f, 5.0f, 3.40f, 0.18f, 0.04f, 1.22f)
        };

        /// <summary>
        /// The voice for an instrument, or one derived from its id when the table has no row.
        /// <para>
        /// The fallback is not politeness. §5.5's layout mode adds instruments, and an id this file
        /// has never heard of must still finish audibly rather than silently — a machine you cannot
        /// hear finish is the exact failure #46 is about. A derived voice is not guaranteed to be
        /// distinguishable from every other, which is why a shipped instrument gets a row.
        /// </para>
        /// </summary>
        private static Voice VoiceFor(string instrumentId)
        {
            if (!string.IsNullOrEmpty(instrumentId) && Voices.TryGetValue(instrumentId, out var known))
                return known;

            uint h = Hash(instrumentId);

            return new Voice(
                root: 220f * Mathf.Pow(2f, (h % 24u) / 12f),
                notes: 1 + (int)((h >> 5) % 3u),
                step: FallbackSteps[(h >> 8) % 4u],
                noteSeconds: 0.10f + (h >> 11) % 5u * 0.06f,
                decay: 2.5f + (h >> 14) % 6u * 2.2f,
                partial: 1.5f + (h >> 17) % 6u * 0.45f,
                partialGain: 0.12f + (h >> 20) % 4u * 0.08f,
                noise: 0.03f + (h >> 22) % 5u * 0.07f,
                loopPitch: 0.75f + (h >> 25) % 8u * 0.09f);
        }

        /// <summary>Falling, gently falling, flat, rising. Enough spread to tell two unknowns apart.</summary>
        private static readonly float[] FallbackSteps = { 0.5f, 0.75f, 1f, 1.5f };

        // -- Synthesis ---------------------------------------------------------------------------

        private static AudioClip BuildChime(string instrumentId, Voice voice)
        {
            float tail = Mathf.Clamp(4.2f / Mathf.Max(0.5f, voice.Decay), 0.22f, 2.4f);
            float seconds = voice.NoteSeconds * (voice.Notes - 1) + tail;
            int length = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));

            var samples = new float[length];

            // Seeded off the id so the same instrument gets byte-identical audio on every machine
            // and every run. |1 because xorshift is stuck at zero.
            uint noiseState = Hash(instrumentId) | 1u;

            for (int note = 0; note < voice.Notes; note++)
            {
                float frequency = voice.Root * Mathf.Pow(voice.Step, note);
                int onset = Mathf.RoundToInt(SampleRate * voice.NoteSeconds * note);
                float gain = 1f / (1f + note * 0.35f);

                for (int i = onset; i < length; i++)
                {
                    float u = (i - onset) / (float)SampleRate;
                    float envelope = Mathf.Exp(-u * voice.Decay);

                    float body = Mathf.Sin(2f * Mathf.PI * frequency * u) +
                                 voice.PartialGain *
                                 Mathf.Sin(2f * Mathf.PI * frequency * voice.Partial * u);

                    float strike = voice.Noise * Mathf.Exp(-u * 120f) * SignedNoise(ref noiseState);

                    samples[i] += (body * envelope + strike) * gain;
                }
            }

            ScaleToPeak(samples, 0.85f);
            FadeOut(samples);
            return MakeClip($"Run finished — {instrumentId}", samples);
        }

        /// <summary>
        /// Two seconds of a box doing work. Every frequency is a whole number of cycles in that
        /// window so the seam is silent before the crossfade even touches it.
        /// </summary>
        private static AudioClip BuildMachineLoop()
        {
            int length = SampleRate * 2;
            var samples = new float[length];
            uint noiseState = 0x4d414348u; // "MACH"
            float rumble = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                rumble += (SignedNoise(ref noiseState) - rumble) * 0.05f;

                float motor = Mathf.Sin(2f * Mathf.PI * 62f * t) * 0.42f +
                              Mathf.Sin(2f * Mathf.PI * 124f * t + 0.4f) * 0.16f;
                float whine = Mathf.Sin(2f * Mathf.PI * 318f * t) * 0.07f;
                float wobble = 0.86f + 0.14f * Mathf.Sin(2f * Mathf.PI * 3f * t);

                samples[i] = Mathf.Clamp((motor + whine + rumble * 0.5f) * 0.5f * wobble, -0.95f, 0.95f);
            }

            CloseLoop(samples);
            return MakeClip("Instrument running loop", samples);
        }

        /// <summary>One second of shaking, four strokes, so the loop point falls on a stroke boundary.</summary>
        private static AudioClip BuildAgitateLoop()
        {
            int length = SampleRate;
            var samples = new float[length];
            uint noiseState = 0x53484b31u; // "SHK1"
            float filtered = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                filtered += (SignedNoise(ref noiseState) - filtered) * 0.22f;

                float stroke = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 4f * t)), 3f);
                float liquid = filtered * 0.62f;
                float glass = Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.05f;

                samples[i] = (liquid + glass) * stroke;
            }

            CloseLoop(samples);
            return MakeClip("Vial agitation loop", samples);
        }

        /// <summary>Solvent running into a bottle. Brighter filtering than the ventilation, plus a burble.</summary>
        private static AudioClip BuildPourLoop()
        {
            int length = SampleRate;
            var samples = new float[length];
            uint noiseState = 0x504f5552u; // "POUR"
            float filtered = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                filtered += (SignedNoise(ref noiseState) - filtered) * 0.35f;

                float burble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 7f * t) *
                               Mathf.Sin(2f * Mathf.PI * 3f * t);

                samples[i] = filtered * 0.62f * burble;
            }

            CloseLoop(samples);
            return MakeClip("Solvent pour loop", samples);
        }

        /// <summary>A thump, then the latch a beat later. Two events, because seating a vial is two.</summary>
        private static AudioClip BuildLoadClunk()
        {
            int length = Mathf.RoundToInt(SampleRate * 0.34f);
            var samples = new float[length];
            uint noiseState = 0x4c4f4431u; // "LOD1"
            int latchAt = Mathf.RoundToInt(SampleRate * 0.07f);

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                float thump = Mathf.Sin(2f * Mathf.PI * 170f * t) * Mathf.Exp(-t * 20f) * 0.8f;
                thump += SignedNoise(ref noiseState) * Mathf.Exp(-t * 90f) * 0.3f;

                float latch = 0f;
                if (i >= latchAt)
                {
                    float u = (i - latchAt) / (float)SampleRate;
                    latch = (Mathf.Sin(2f * Mathf.PI * 1250f * u) * 0.4f +
                             SignedNoise(ref noiseState) * 0.5f) * Mathf.Exp(-u * 90f);
                }

                samples[i] = thump + latch;
            }

            ScaleToPeak(samples, 0.8f);
            FadeOut(samples);
            return MakeClip("Vial seated", samples);
        }

        /// <summary>
        /// The shape every short percussive noise in the lab has: two partials under one decay, with
        /// a noise transient on the front. Parameterised rather than copied four times so the family
        /// stays a family — a pick-up and a put-down are the same object at different registers.
        /// </summary>
        private static AudioClip BuildTransient(string name, float seconds, float low, float high,
                                                float highGain, float decay, float noise,
                                                float noiseDecay, uint seed)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
            var samples = new float[length];
            uint noiseState = seed | 1u;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * decay);

                float body = Mathf.Sin(2f * Mathf.PI * low * t) +
                             highGain * Mathf.Sin(2f * Mathf.PI * high * t);

                samples[i] = body * envelope +
                             noise * Mathf.Exp(-t * noiseDecay) * SignedNoise(ref noiseState);
            }

            ScaleToPeak(samples, 0.8f);
            FadeOut(samples);
            return MakeClip(name, samples);
        }

        // -- Helpers -----------------------------------------------------------------------------

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            if (clip == null)
            {
                // Same failure LabAmbience reports, said once at the cause rather than once per
                // press for the rest of the session.
                Debug.LogError(
                    $"[LabSoundBank] AudioClip.Create returned nothing for '{name}', so the lab will " +
                    "be silent. Check that Unity audio is not disabled in Project Settings > Audio.");
                return null;
            }

            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>Crossfade the tail into the head so a looping clip has no click at the seam.</summary>
        private static void CloseLoop(float[] samples)
        {
            const int blendSamples = 1024;
            if (samples.Length <= blendSamples * 2) return;

            int start = samples.Length - blendSamples;
            for (int i = 0; i < blendSamples; i++)
            {
                float blend = i / (float)(blendSamples - 1);
                samples[start + i] = Mathf.Lerp(samples[start + i], samples[i], blend);
            }
        }

        /// <summary>Ramp the last few milliseconds to zero. A one-shot cut mid-cycle is an audible tick.</summary>
        private static void FadeOut(float[] samples, int fadeSamples = 256)
        {
            int fade = Mathf.Min(fadeSamples, samples.Length);
            int start = samples.Length - fade;
            for (int i = 0; i < fade; i++)
                samples[start + i] *= 1f - i / (float)fade;
        }

        private static void ScaleToPeak(float[] samples, float peak)
        {
            float max = 0f;
            for (int i = 0; i < samples.Length; i++) max = Mathf.Max(max, Mathf.Abs(samples[i]));
            if (max <= 1e-5f) return;

            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        }

        /// <summary>
        /// Xorshift, as <see cref="LabAmbience"/> uses and for the reason CLAUDE.md gives:
        /// <see cref="UnityEngine.Random"/> is global mutable state that another system can advance
        /// between two builds, and <c>System.Random</c>'s algorithm is not pinned across runtimes.
        /// A clip has to come out the same on every machine or a sound is not a shared reference.
        /// </summary>
        private static float SignedNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00ffffffu) / 8388608f - 1f;
        }

        /// <summary>FNV-1a. Pinned here rather than <c>string.GetHashCode</c>, which is randomised per process.</summary>
        private static uint Hash(string value)
        {
            uint hash = 2166136261u;
            if (string.IsNullOrEmpty(value)) return hash;

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
