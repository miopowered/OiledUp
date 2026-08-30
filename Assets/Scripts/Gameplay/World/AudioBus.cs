using System.Collections.Generic;
using Residue.Gameplay.Settings;
using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Which slider a sound answers to (#46). Master is not here: it is
    /// <see cref="AudioListener.volume"/>, which Unity already applies to everything, and a category
    /// that duplicated it would let the two disagree.
    /// <para>
    /// Voice is not here either. Playback gain for <c>RelayVoice</c> lives in <c>Residue.Net</c>,
    /// which reads <see cref="GameSettings.VoiceVolume"/> for itself — <c>Residue.Gameplay</c> cannot
    /// reference <c>Residue.Net</c>, and that direction is the boundary keeping ground truth off a
    /// serializer.
    /// </para>
    /// </summary>
    public enum AudioCategory
    {
        /// <summary>Room tone. Quiet enough that the instruments sit on top of it.</summary>
        Ambience,

        /// <summary>Everything the player or a machine causes: runs, presses, refusals, the wash.</summary>
        Effects
    }

    /// <summary>
    /// Routes every <see cref="AudioSource"/> in the lab to the volume slider it belongs under (#46).
    ///
    /// <para>
    /// <b>Why this is code and not an <c>AudioMixer</c>.</b> A mixer is the obvious answer and
    /// <see cref="GameSettings.EffectsVolume"/>'s own comment promised one. It cannot be built here:
    /// Unity exposes no scripting API that creates an <c>AudioMixer</c> asset, so a mixer would have
    /// to be authored by hand in the Editor and committed as opaque YAML — which is the thing
    /// <c>CLAUDE.md</c> rules out for balance data, for the same reason it applies here. Every asset
    /// in this project is generated from readable source, and a four-entry gain table does not earn
    /// an exception. The bus is about thirty lines and is testable without an Editor; the mixer would
    /// be neither.
    /// </para>
    ///
    /// <para>
    /// <b>Registration rather than a scene scan.</b> Sources appear and vanish constantly — a slip
    /// prints, a carton is discarded, a machine spins down — so a bus that re-scanned the scene would
    /// either miss the ones created since or pay a <c>FindObjectsByType</c> every time a slider moved.
    /// Each source states its category and its own authored volume once; the bus multiplies. A source
    /// whose <see cref="GameObject"/> has since been destroyed is dropped on the next pass, so a
    /// caller that forgets to <see cref="Unregister"/> leaks nothing but one list slot until then.
    /// </para>
    ///
    /// <para>
    /// The authored volume is kept separately rather than read back off the source, because reading
    /// it back would compound: at 50% ambience the stored value would already be halved, and the next
    /// slider move would halve it again until the room fell silent and no slider could recover it.
    /// </para>
    /// </summary>
    public static class AudioBus
    {
        private struct Routed
        {
            public AudioSource Source;
            public AudioCategory Category;

            /// <summary>The volume the source asked for, before any slider. See the type doc.</summary>
            public float Authored;
        }

        private static readonly List<Routed> routed = new();
        private static bool listening;

        /// <summary>
        /// Statics survive an Enter Play Mode that skips the domain reload, so the list would
        /// otherwise still hold last session's sources — every one of them destroyed, and the first
        /// slider move walking a list of corpses to find that out.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Reset()
        {
            routed.Clear();
            GameSettings.Changed -= Apply;
            listening = false;
        }

        /// <summary>The multiplier a category currently applies, for a one-shot to scale itself by.</summary>
        public static float Volume(AudioCategory category) => category switch
        {
            AudioCategory.Ambience => GameSettings.AmbienceVolume,
            AudioCategory.Effects => GameSettings.EffectsVolume,
            _ => 1f
        };

        /// <summary>
        /// Put <paramref name="source"/> under <paramref name="category"/> and set its volume now.
        /// <paramref name="authoredVolume"/> is what it would play at with every slider at full.
        /// </summary>
        public static void Register(AudioSource source, AudioCategory category, float authoredVolume)
        {
            if (source == null) return;

            if (!listening)
            {
                GameSettings.Changed += Apply;
                listening = true;
            }

            for (int i = 0; i < routed.Count; i++)
            {
                if (routed[i].Source != source) continue;

                routed[i] = new Routed { Source = source, Category = category, Authored = authoredVolume };
                source.volume = authoredVolume * Volume(category);
                return;
            }

            routed.Add(new Routed { Source = source, Category = category, Authored = authoredVolume });
            source.volume = authoredVolume * Volume(category);
        }

        public static void Unregister(AudioSource source)
        {
            for (int i = routed.Count - 1; i >= 0; i--)
                if (routed[i].Source == source) routed.RemoveAt(i);
        }

        /// <summary>
        /// Fire a one-shot through <paramref name="source"/> at the category's current volume.
        /// <para>
        /// One-shots are scaled here rather than by registering the source, because
        /// <see cref="AudioSource.PlayOneShot(AudioClip,float)"/> takes its own multiplier and a
        /// source that also plays a loop would otherwise need two volumes at once.
        /// </para>
        /// Silently does nothing without a clip. A missing clip is a content gap, not a fault worth a
        /// warning per occurrence — and Unity's own warning for this is what #46 was chasing.
        /// </summary>
        public static void PlayOneShot(AudioSource source, AudioClip clip, AudioCategory category,
                                       float volume = 1f)
        {
            if (source == null || clip == null) return;

            source.PlayOneShot(clip, volume * Volume(category));
        }

        /// <summary>Re-apply every slider. Raised by <see cref="GameSettings.Changed"/>.</summary>
        public static void Apply()
        {
            for (int i = routed.Count - 1; i >= 0; i--)
            {
                var entry = routed[i];
                if (entry.Source == null) { routed.RemoveAt(i); continue; }

                entry.Source.volume = entry.Authored * Volume(entry.Category);
            }
        }

        /// <summary>How many sources are currently routed. For tests and diagnostics.</summary>
        public static int Count => routed.Count;
    }
}
