using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DesktopBounce
{
    internal sealed class BounceAudio : IDisposable
    {
        private const int VoiceCount = 3;
        private readonly object gate = new object();
        private readonly Queue<float> pending = new Queue<float>(VoiceCount);
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Thread worker;
        private bool resetRequested;
        private bool unavailable;
        private bool disposed;

        internal BounceAudio()
        {
            worker = new Thread(PlaybackLoop) { IsBackground = true, Name = "Desk Bounce audio" };
            worker.Start();
        }

        internal void Play(float strength)
        {
            if (strength <= 0)
                return;

            lock (gate)
            {
                if (disposed || unavailable)
                    return;
                if (pending.Count == VoiceCount)
                    pending.Dequeue();
                pending.Enqueue(strength);
                wake.Set();
            }
        }

        internal void Stop()
        {
            lock (gate)
            {
                if (disposed || unavailable)
                    return;
                pending.Clear();
                resetRequested = true;
                wake.Set();
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                if (!unavailable)
                    wake.Set();
            }
            worker.Join(300);
        }

        private void PlaybackLoop()
        {
            var voices = new List<Voice>(VoiceCount);
            try
            {
                int sampleRate;
                short[] sample = ReadSample(out sampleRate);
                if (sample == null)
                    return;

                short[][] pitches = MakePitches(sample, sampleRate);
                var scaled = new short[pitches[0].Length];
                for (int index = 0; index < VoiceCount; index++)
                {
                    Voice voice = Voice.Open(sampleRate, scaled.Length);
                    if (voice == null)
                        break;
                    voices.Add(voice);
                }
                if (voices.Count == 0)
                    return;

                var random = new Random();
                int lastPitch = -1;
                while (true)
                {
                    wake.WaitOne();
                    while (true)
                    {
                        bool reset;
                        float strength = 0;
                        lock (gate)
                        {
                            if (disposed)
                                return;
                            reset = resetRequested;
                            resetRequested = false;
                            if (!reset)
                            {
                                if (pending.Count == 0)
                                    break;
                                strength = pending.Dequeue();
                            }
                        }

                        if (reset)
                        {
                            foreach (Voice voice in voices)
                                voice.Stop();
                            continue;
                        }

                        foreach (Voice voice in voices)
                        {
                            if (voice.IsBusy)
                                continue;

                            int pitch = random.Next(pitches.Length);
                            if (pitch == lastPitch)
                                pitch = (pitch + 1) % pitches.Length;
                            lastPitch = pitch;
                            short[] variant = pitches[pitch];
                            double energy = Math.Sqrt(Math.Min(1, Math.Max(0, (strength - 65) / 1250f)));
                            double gain = 0.075 + 0.32 * energy;
                            for (int index = 0; index < variant.Length; index++)
                                scaled[index] = (short)(variant[index] * gain);

                            if (!voice.Play(scaled, variant.Length))
                                return;
                            break;
                        }
                    }
                }
            }
            catch (IOException)
            {
                // A missing or damaged sample should leave the desktop toy usable.
            }
            finally
            {
                foreach (Voice voice in voices)
                    voice.Dispose();
                lock (gate)
                {
                    unavailable = true;
                    pending.Clear();
                    wake.Dispose();
                }
            }
        }

        private static short[] ReadSample(out int sampleRate)
        {
            sampleRate = 0;
            var stream = typeof(BounceAudio).Assembly.GetManifestResourceStream("DeskBounce.Bounce.wav");
            if (stream == null)
                return null;

            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x46464952) // RIFF
                    return null;
                reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x45564157) // WAVE
                    return null;

                int channels = 0;
                int dataLength = 0;
                long dataOffset = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    uint chunk = reader.ReadUInt32();
                    uint length = reader.ReadUInt32();
                    long next = stream.Position + length + (length & 1);
                    if (next > stream.Length)
                        return null;

                    if (chunk == 0x20746d66 && length >= 16) // fmt
                    {
                        int format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt16();
                        int bits = reader.ReadUInt16();
                        if (format != 1 || (channels != 1 && channels != 2) || bits != 16 ||
                            sampleRate < 8000 || sampleRate > 96000)
                            return null;
                    }
                    else if (chunk == 0x61746164) // data
                    {
                        if (length > 384000)
                            return null;
                        dataLength = (int)length;
                        dataOffset = stream.Position;
                    }
                    stream.Position = next;
                }

                int stride = channels * 2;
                if (stride == 0 || dataLength < stride || dataLength > sampleRate * stride)
                    return null;
                stream.Position = dataOffset;
                var sample = new short[dataLength / stride];
                for (int index = 0; index < sample.Length; index++)
                {
                    int value = reader.ReadInt16();
                    if (channels == 2)
                        value = (value + reader.ReadInt16()) / 2;
                    sample[index] = (short)value;
                }
                return sample;
            }
        }

        private static short[][] MakePitches(short[] source, int sampleRate)
        {
            var pitches = new short[9][];
            for (int index = 0; index < pitches.Length; index++)
            {
                double rate = Math.Pow(2, (index - 4) / 12.0);
                var variant = new short[(int)Math.Ceiling(source.Length / rate)];
                for (int frame = 0; frame < variant.Length; frame++)
                {
                    double position = frame * rate;
                    int first = Math.Min((int)position, source.Length - 1);
                    int second = Math.Min(first + 1, source.Length - 1);
                    double value = source[first] + (source[second] - source[first]) * (position - first);
                    double fade = Math.Min(frame / (sampleRate * 0.0006),
                        (variant.Length - 1 - frame) / (sampleRate * 0.004));
                    variant[frame] = (short)(value * Math.Min(1, fade));
                }
                pitches[index] = variant;
            }
            return pitches;
        }

        private sealed class Voice : IDisposable
        {
            private const int WaveMapper = -1;
            private const int InQueue = 0x10;
            private static readonly int HeaderSize = Marshal.SizeOf(typeof(WaveHeader));
            private static readonly int LengthOffset = Marshal.OffsetOf(typeof(WaveHeader), "BufferLength").ToInt32();
            private static readonly int FlagsOffset = Marshal.OffsetOf(typeof(WaveHeader), "Flags").ToInt32();
            private readonly IntPtr device;
            private IntPtr data;
            private IntPtr header;
            private bool prepared;

            private Voice(IntPtr device)
            {
                this.device = device;
            }

            internal bool IsBusy { get { return (Marshal.ReadInt32(header, FlagsOffset) & InQueue) != 0; } }

            internal static Voice Open(int sampleRate, int frameCount)
            {
                var format = new WaveFormat
                {
                    FormatTag = 1, Channels = 1, SampleRate = sampleRate, BytesPerSecond = sampleRate * 2,
                    BlockAlign = 2, BitsPerSample = 16
                };
                IntPtr handle;
                if (waveOutOpen(out handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                    return null;

                var voice = new Voice(handle);
                try
                {
                    int size = frameCount * 2;
                    voice.data = Marshal.AllocHGlobal(size);
                    voice.header = Marshal.AllocHGlobal(HeaderSize);
                    Marshal.StructureToPtr(new WaveHeader { Data = voice.data, BufferLength = size },
                        voice.header, false);
                    voice.prepared = waveOutPrepareHeader(handle, voice.header, HeaderSize) == 0;
                    return voice.prepared ? voice : null;
                }
                finally
                {
                    if (!voice.prepared)
                        voice.Dispose();
                }
            }

            internal bool Play(short[] samples, int count)
            {
                Marshal.Copy(samples, 0, data, count);
                Marshal.WriteInt32(header, LengthOffset, count * 2);
                return waveOutWrite(device, header, HeaderSize) == 0;
            }

            internal void Stop()
            {
                waveOutReset(device);
            }

            public void Dispose()
            {
                Stop();
                bool released = !prepared || waveOutUnprepareHeader(device, header, HeaderSize) == 0;
                bool closed = waveOutClose(device) == 0;
                if (released || closed)
                {
                    Marshal.FreeHGlobal(header);
                    Marshal.FreeHGlobal(data);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            internal ushort FormatTag, Channels;
            internal int SampleRate, BytesPerSecond;
            internal ushort BlockAlign, BitsPerSample, ExtraSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            internal IntPtr Data;
            internal int BufferLength, BytesRecorded;
            internal IntPtr User;
            internal int Flags, Loops;
            internal IntPtr Next, Reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr device, int deviceId, ref WaveFormat format,
            IntPtr callback, IntPtr instance, int flags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr device);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr device);
    }
}
