using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Residue.Net.Connect
{
    /// <summary>
    /// Small PCM voice transport for platforms that Vivox does not ship a client for. Audio uses
    /// the session's existing Relay connection: clients send one unreliable stream to the host and
    /// the host fans it out. Four players at 16 kHz mono stay modest enough for the co-op relay,
    /// while 20 ms packets keep speech latency low and remain comfortably below the MTU.
    /// </summary>
    internal sealed class RelayVoice
    {
        private const string MessageName = "residue.voice.pcm16";
        private const int SampleRate = 16000;
        private const int FrameSamples = 320;
        private const int FrameBytes = FrameSamples * sizeof(short);
        private const int PacketBytes = sizeof(ulong) + FrameBytes;

        private readonly Dictionary<ulong, RelayVoicePlayback> playbacks = new();
        private readonly float[] pending = new float[FrameSamples];

        private NetworkManager manager;
        private AudioClip microphone;
        private int microphonePosition;
        private int pendingCount;
        private bool handlerRegistered;
        private bool loggedSending;
        private bool loggedReceiving;

        public bool IsReady { get; private set; }
        public string Error { get; private set; }

        public bool Join()
        {
            manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.CustomMessagingManager == null)
                return Fail("VOICE NETWORK UNAVAILABLE");

            try
            {
                manager.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, OnMessage);
                handlerRegistered = true;

                microphone = Microphone.Start(null, true, 1, SampleRate);
                if (microphone == null) return Fail("NO MICROPHONE FOUND");

                microphonePosition = 0;
                pendingCount = 0;
                IsReady = true;
                Error = null;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RelayVoice] Could not start Linux voice ({e.GetType().Name}: {e.Message}).");
                return Fail("VOICE INPUT UNAVAILABLE");
            }
        }

        public void Tick(bool inputMuted, bool outputMuted, float outputVolume)
        {
            foreach (var playback in playbacks.Values)
            {
                if (playback == null) continue;
                playback.Muted = outputMuted;
                playback.Volume = outputVolume;
            }

            if (!IsReady || inputMuted || microphone == null) return;

            int position = Microphone.GetPosition(null);
            if (position < 0 || position == microphonePosition) return;

            int available = position >= microphonePosition
                ? position - microphonePosition
                : microphone.samples - microphonePosition + position;
            if (available <= 0) return;

            var captured = new float[available];
            if (!microphone.GetData(captured, microphonePosition)) return;
            microphonePosition = position;

            foreach (float sample in captured)
            {
                pending[pendingCount++] = sample;
                if (pendingCount != FrameSamples) continue;

                SendFrame(pending);
                pendingCount = 0;
            }
        }

        public void SetOutputVolume(float volume)
        {
            foreach (var playback in playbacks.Values)
                if (playback != null) playback.Volume = volume;
        }

        public void Leave()
        {
            IsReady = false;

            if (handlerRegistered && manager?.CustomMessagingManager != null)
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(MessageName);
            handlerRegistered = false;

            try
            {
                if (microphone != null && Microphone.IsRecording(null)) Microphone.End(null);
            }
            catch { /* Device removal during teardown is harmless. */ }
            microphone = null;

            foreach (var playback in playbacks.Values)
                if (playback != null) UnityEngine.Object.Destroy(playback.gameObject);
            playbacks.Clear();
            manager = null;
        }

        private bool Fail(string message)
        {
            Error = message;
            Leave();
            return false;
        }

        private void SendFrame(float[] samples)
        {
            if (manager == null || !manager.IsListening) return;

            var bytes = new byte[FrameBytes];
            for (int i = 0; i < FrameSamples; i++)
            {
                short pcm = (short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                bytes[i * 2] = (byte)pcm;
                bytes[i * 2 + 1] = (byte)(pcm >> 8);
            }

            ulong origin = manager.LocalClientId;
            if (manager.IsServer) SendFromServer(origin, bytes, except: origin);
            else SendTo(NetworkManager.ServerClientId, origin, bytes);

            if (!loggedSending)
            {
                loggedSending = true;
                Debug.Log("[RelayVoice] Microphone audio is streaming through Relay.");
            }
        }

        private void OnMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (manager == null || reader.Length < PacketBytes) return;

            reader.ReadValueSafe(out ulong claimedOrigin);
            var bytes = new byte[FrameBytes];
            reader.ReadBytesSafe(ref bytes, FrameBytes);

            // Clients may only speak as themselves. Packets arriving from the server retain the
            // original id the host wrote when it forwarded them.
            ulong origin = manager.IsServer ? senderClientId : claimedOrigin;
            if (origin == manager.LocalClientId) return;

            if (!loggedReceiving)
            {
                loggedReceiving = true;
                Debug.Log($"[RelayVoice] Receiving audio from player {origin}.");
            }

            Push(origin, bytes);

            if (manager.IsServer) SendFromServer(origin, bytes, except: senderClientId);
        }

        private void SendFromServer(ulong origin, byte[] bytes, ulong except)
        {
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId == manager.LocalClientId || clientId == except) continue;
                SendTo(clientId, origin, bytes);
            }
        }

        private void SendTo(ulong clientId, ulong origin, byte[] bytes)
        {
            using var writer = new FastBufferWriter(PacketBytes, Allocator.Temp);
            writer.WriteValueSafe(origin);
            writer.WriteBytesSafe(bytes, FrameBytes);
            manager.CustomMessagingManager.SendNamedMessage(
                MessageName, clientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        private void Push(ulong origin, byte[] bytes)
        {
            if (!playbacks.TryGetValue(origin, out var playback) || playback == null)
            {
                var avatar = FindAvatar(origin);
                if (avatar == null) return;

                var speaker = new GameObject($"Voice {origin}");
                speaker.transform.SetParent(avatar.transform, false);
                speaker.transform.localPosition = Vector3.up * 1.6f;
                playback = speaker.AddComponent<RelayVoicePlayback>();
                playback.Initialize(SampleRate, FrameSamples, 24f);
                playbacks[origin] = playback;
            }

            var samples = new float[FrameSamples];
            for (int i = 0; i < FrameSamples; i++)
            {
                short pcm = (short)(bytes[i * 2] | bytes[i * 2 + 1] << 8);
                samples[i] = pcm / 32768f;
            }
            playback.Push(samples);
        }

        private static PlayerAvatar FindAvatar(ulong ownerClientId)
        {
            foreach (var avatar in UnityEngine.Object.FindObjectsByType<PlayerAvatar>())
                if (avatar.IsSpawned && avatar.OwnerClientId == ownerClientId) return avatar;
            return null;
        }
    }

    /// <summary>Thread-safe jitter queue feeding one spatial AudioSource.</summary>
    internal sealed class RelayVoicePlayback : MonoBehaviour
    {
        // The audio callback commonly requests several network frames at once. Starting it with an
        // empty queue (or resuming after one missing packet) alternates real audio and silence,
        // which sounds much worse than a small, stable amount of voice latency.
        private const int MinimumBufferedFrames = 6;
        private const int CallbackSafetyFrames = 2;
        private const int MaximumQueuedFrames = 24;

        private readonly ConcurrentQueue<float[]> frames = new();
        private AudioSource source;
        private float[] current;
        private int currentOffset;
        private int frameSamples;
        private int queuedFrames;
        private int discardRequested;
        private volatile bool buffering = true;
        private volatile bool muted;

        public bool Muted
        {
            get => muted;
            set
            {
                if (muted == value) return;
                muted = value;
                if (value) Interlocked.Exchange(ref discardRequested, 1);
                else buffering = true;
            }
        }

        public float Volume
        {
            get => source != null ? source.volume : 1f;
            set { if (source != null) source.volume = Mathf.Clamp01(value); }
        }

        public void Initialize(int sampleRate, int samplesPerFrame, float maxDistance)
        {
            frameSamples = samplesPerFrame;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.clip = AudioClip.Create("Network voice", sampleRate, 1, sampleRate, true, Fill);
            source.Play();
        }

        public void Push(float[] frame)
        {
            if (muted) return;

            // If a stall grows latency beyond the useful window, catch up in one go. Keeping the
            // oldest packets would make lip/audio delay grow for the rest of the session.
            if (Volatile.Read(ref queuedFrames) >= MaximumQueuedFrames)
            {
                while (Volatile.Read(ref queuedFrames) > MinimumBufferedFrames &&
                       frames.TryDequeue(out _))
                    Interlocked.Decrement(ref queuedFrames);
            }

            frames.Enqueue(frame);
            Interlocked.Increment(ref queuedFrames);
        }

        private void Fill(float[] data)
        {
            Array.Clear(data, 0, data.Length);

            if (Interlocked.Exchange(ref discardRequested, 0) != 0)
            {
                current = null;
                currentOffset = 0;
                while (frames.TryDequeue(out _)) Interlocked.Decrement(ref queuedFrames);
                buffering = true;
            }

            if (muted || frameSamples <= 0) return;

            if (buffering)
            {
                int callbackFrames = (data.Length + frameSamples - 1) / frameSamples;
                int required = Math.Min(MaximumQueuedFrames,
                    Math.Max(MinimumBufferedFrames, callbackFrames + CallbackSafetyFrames));
                if (Volatile.Read(ref queuedFrames) < required) return;
                buffering = false;
            }

            int written = 0;
            while (written < data.Length)
            {
                if (current == null || currentOffset >= current.Length)
                {
                    if (!frames.TryDequeue(out current))
                    {
                        buffering = true;
                        break;
                    }
                    Interlocked.Decrement(ref queuedFrames);
                    currentOffset = 0;
                }

                int count = Math.Min(data.Length - written, current.Length - currentOffset);
                Array.Copy(current, currentOffset, data, written, count);
                currentOffset += count;
                written += count;
            }
        }
    }
}
