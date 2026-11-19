using System;

namespace VamMmd.Core
{
    // Minimal RIFF/WAVE reader: PCM 8/16/24/32 bit and 32-bit float, any channel count.
    // The player decodes WAV itself so that loading music has no side effects on the scene.
    public sealed class WavFile
    {
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
        public bool IsFloat;
        public int DataOffset;
        public int Frames;              // sample frames (one sample per channel each)
        private byte[] _d;
        private int _bytesPerFrame;

        public double LengthSeconds { get { return SampleRate > 0 ? Frames / (double)SampleRate : 0; } }

        public static WavFile Parse(byte[] d)
        {
            if (d == null || d.Length < 44) throw new FormatException2("not a WAV file (too short)");
            if (!Tag(d, 0, "RIFF") || !Tag(d, 8, "WAVE")) throw new FormatException2("not a WAV file (no RIFF/WAVE header)");
            WavFile w = new WavFile();
            w._d = d;
            int o = 12;
            bool haveFmt = false;
            int format = 0;
            while (o + 8 <= d.Length)
            {
                long size = (uint)(d[o + 4] | (d[o + 5] << 8) | (d[o + 6] << 16) | (d[o + 7] << 24));
                int body = o + 8;
                if (Tag(d, o, "fmt "))
                {
                    if (size < 16 || body + 16 > d.Length) throw new FormatException2("WAV format chunk is damaged");
                    format = d[body] | (d[body + 1] << 8);
                    w.Channels = d[body + 2] | (d[body + 3] << 8);
                    w.SampleRate = d[body + 4] | (d[body + 5] << 8) | (d[body + 6] << 16) | (d[body + 7] << 24);
                    w.BitsPerSample = d[body + 14] | (d[body + 15] << 8);
                    if (format == 0xFFFE && size >= 26 && body + 26 <= d.Length)
                        format = d[body + 24] | (d[body + 25] << 8);       // extensible: real format in the sub-format GUID
                    haveFmt = true;
                }
                else if (Tag(d, o, "data"))
                {
                    if (!haveFmt) throw new FormatException2("WAV data comes before its format");
                    long avail = d.Length - body;
                    if (size > avail || size == 0) size = avail;           // streamed or cut files
                    w.DataOffset = body;
                    w._bytesPerFrame = w.Channels * (w.BitsPerSample / 8);
                    if (w._bytesPerFrame <= 0) throw new FormatException2("WAV has no channels or bits");
                    w.Frames = (int)(size / w._bytesPerFrame);
                    break;
                }
                long next = (long)body + size + (size & 1);
                if (next <= o || next > d.Length) break;
                o = (int)next;
            }
            if (!haveFmt || w.Frames <= 0) throw new FormatException2("WAV has no audio data");
            w.IsFloat = format == 3;
            if (format != 1 && format != 3) throw new FormatException2("WAV encoding " + format + " is not supported (only PCM and float)");
            if (w.IsFloat && w.BitsPerSample != 32) throw new FormatException2("only 32-bit float WAV is supported");
            if (!w.IsFloat && w.BitsPerSample != 8 && w.BitsPerSample != 16 && w.BitsPerSample != 24 && w.BitsPerSample != 32)
                throw new FormatException2("WAV with " + w.BitsPerSample + " bits is not supported");
            if (w.Channels < 1 || w.Channels > 8 || w.SampleRate < 1000 || w.SampleRate > 384000)
                throw new FormatException2("WAV header has unusual values (" + w.Channels + " channels, " + w.SampleRate + " Hz)");
            return w;
        }

        // Decodes count frames starting at frame start into dst (interleaved, -1..1).
        // Returns the number of frames written.
        public int Decode(int start, int count, float[] dst)
        {
            if (start < 0) start = 0;
            if (start + count > Frames) count = Frames - start;
            if (count * Channels > dst.Length) count = dst.Length / Channels;
            if (count <= 0) return 0;
            byte[] d = _d;
            int o = DataOffset + start * _bytesPerFrame;
            int n = count * Channels;
            if (IsFloat)
            {
                for (int i = 0; i < n; i++, o += 4) dst[i] = ByteReader.Single(d, o);
            }
            else if (BitsPerSample == 16)
            {
                for (int i = 0; i < n; i++, o += 2) dst[i] = (short)(d[o] | (d[o + 1] << 8)) / 32768f;
            }
            else if (BitsPerSample == 24)
            {
                for (int i = 0; i < n; i++, o += 3)
                {
                    int v = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16);
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                    dst[i] = v / 8388608f;
                }
            }
            else if (BitsPerSample == 32)
            {
                for (int i = 0; i < n; i++, o += 4)
                    dst[i] = (d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) / 2147483648f;
            }
            else
            {
                for (int i = 0; i < n; i++, o += 1) dst[i] = (d[o] - 128) / 128f;
            }
            return count;
        }

        private static bool Tag(byte[] d, int o, string tag)
        {
            if (o + 4 > d.Length) return false;
            return d[o] == tag[0] && d[o + 1] == tag[1] && d[o + 2] == tag[2] && d[o + 3] == tag[3];
        }
    }
}
