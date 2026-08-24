using UnityEngine;

namespace Residue.Gameplay.World
{
    /// <summary>
    /// Builds the lab's deliberately uneventful room tone at runtime. Procedural clips keep the
    /// ambience deterministic and avoid shipping a large looping recording for a handful of fans,
    /// mains hum and relay clicks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LabAmbience : MonoBehaviour
    {
        private const int SampleRate = 24000;
        private const int LoopSeconds = 12;

        private AudioClip ventilationClip;
        private AudioClip ballastClip;
        private AudioClip relayClip;
        private AudioSource relaySource;
        private float nextRelayAt;
        private uint randomState = 0x6c61626fu; // "labo"

        private void Awake()
        {
            ventilationClip = BuildVentilationLoop();
            ballastClip = BuildBallastLoop();
            relayClip = BuildRelayClick();

            CreateLoop("Lueftungsanlage", ventilationClip, 0.16f, 780f);
            CreateLoop("Leuchtstoffroehren", ballastClip, 0.035f, 2400f);

            var relay = new GameObject("Messgeraete_Relais");
            relay.transform.SetParent(transform, false);
            relay.transform.localPosition = new Vector3(0f, 1.1f, -3.25f);
            relaySource = ConfigureSource(relay, 0.11f, true);
            relaySource.clip = relayClip;
            nextRelayAt = Time.unscaledTime + 7f + Next01() * 9f;
        }

        private void Update()
        {
            if (relaySource == null || Time.unscaledTime < nextRelayAt) return;

            relaySource.pitch = 0.92f + Next01() * 0.12f;
            relaySource.PlayOneShot(relayClip);
            nextRelayAt = Time.unscaledTime + 18f + Next01() * 27f;
        }

        private void OnDestroy()
        {
            if (ventilationClip != null) Destroy(ventilationClip);
            if (ballastClip != null) Destroy(ballastClip);
            if (relayClip != null) Destroy(relayClip);
        }

        private void CreateLoop(string name, AudioClip clip, float volume, float lowPass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var source = ConfigureSource(go, volume, false);
            source.clip = clip;
            source.loop = true;
            source.Play();

            var filter = go.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = lowPass;
            filter.lowpassResonanceQ = 1f;
        }

        private static AudioSource ConfigureSource(GameObject go, float volume, bool spatial)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = spatial ? 0.75f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = 12f;
            source.dopplerLevel = 0f;
            return source;
        }

        private static AudioClip BuildVentilationLoop()
        {
            int length = SampleRate * LoopSeconds;
            var samples = new float[length];
            uint noiseState = 0x4f494c31u;
            float filteredNoise = 0f;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = SignedNoise(ref noiseState);
                filteredNoise += (noise - filteredNoise) * 0.025f;

                // Slightly wandering ventilation fan, kept phase-perfect across the 12 s seam.
                float motor = Mathf.Sin(2f * Mathf.PI * 47f * t) * 0.24f;
                motor += Mathf.Sin(2f * Mathf.PI * 94f * t + 0.7f) * 0.08f;
                float airflow = filteredNoise * (0.55f + 0.08f * Mathf.Sin(2f * Mathf.PI * t / 6f));
                samples[i] = Mathf.Clamp((motor + airflow) * 0.42f, -0.9f, 0.9f);
            }

            CloseLoop(samples);
            return MakeClip("Lab ventilation loop", samples);
        }

        private static AudioClip BuildBallastLoop()
        {
            int length = SampleRate * LoopSeconds;
            var samples = new float[length];
            uint noiseState = 0x44494e31u;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                // German mains is 50 Hz; magnetic fluorescent ballasts audibly excite at 100 Hz.
                float hum = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.48f;
                hum += Mathf.Sin(2f * Mathf.PI * 200f * t + 0.25f) * 0.14f;
                hum += SignedNoise(ref noiseState) * 0.018f;
                samples[i] = hum * 0.34f;
            }

            CloseLoop(samples);
            return MakeClip("Fluorescent ballast loop", samples);
        }

        private static AudioClip BuildRelayClick()
        {
            int length = SampleRate / 5;
            var samples = new float[length];
            uint noiseState = 0x52454c31u;

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * 54f);
                float mechanism = Mathf.Sin(2f * Mathf.PI * 720f * t) * 0.34f;
                samples[i] = (mechanism + SignedNoise(ref noiseState) * 0.42f) * envelope;
            }

            return MakeClip("Instrument relay click", samples);
        }

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void CloseLoop(float[] samples)
        {
            const int blendSamples = 1024;
            int start = samples.Length - blendSamples;
            for (int i = 0; i < blendSamples; i++)
            {
                float blend = i / (float)(blendSamples - 1);
                samples[start + i] = Mathf.Lerp(samples[start + i], samples[i], blend);
            }
        }

        private float Next01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00ffffffu) / 16777216f;
        }

        private static float SignedNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return ((state & 0x00ffffffu) / 8388608f) - 1f;
        }
    }
}
