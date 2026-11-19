using System;
using System.Text;

namespace VamMmd.Core
{
    public sealed class FormatException2 : Exception
    {
        public FormatException2(string message) : base(message) { }
    }

    // Little-endian reader over a byte array. VaM plugins may not use System.IO,
    // so files arrive as byte[] and are read with this.
    public sealed class ByteReader
    {
        private readonly byte[] _d;
        private int _o;

        public ByteReader(byte[] data, int offset)
        {
            if (data == null) throw new FormatException2("no data");
            _d = data;
            _o = offset;
        }

        public int Position { get { return _o; } set { _o = value; } }
        public int Length { get { return _d.Length; } }
        public int Remaining { get { return _d.Length - _o; } }
        public byte[] Data { get { return _d; } }

        public void Need(int n)
        {
            if (n < 0 || _o + n > _d.Length || _o + n < _o)
                throw new FormatException2("file ends early (need " + n + " bytes at " + _o + " of " + _d.Length + ")");
        }

        public void Skip(int n) { Need(n); _o += n; }

        public byte U8() { Need(1); return _d[_o++]; }

        public int U16()
        {
            Need(2);
            int v = _d[_o] | (_d[_o + 1] << 8);
            _o += 2;
            return v;
        }

        public int I32()
        {
            Need(4);
            int v = _d[_o] | (_d[_o + 1] << 8) | (_d[_o + 2] << 16) | (_d[_o + 3] << 24);
            _o += 4;
            return v;
        }

        public uint U32() { return unchecked((uint)I32()); }

        public float F32()
        {
            Need(4);
            float v = Single(_d, _o);
            _o += 4;
            return v;
        }

        public static float Single(byte[] d, int o)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(d, o);
            byte[] t = new byte[4];
            t[0] = d[o + 3]; t[1] = d[o + 2]; t[2] = d[o + 1]; t[3] = d[o];
            return BitConverter.ToSingle(t, 0);
        }
    }

    // Shift-JIS (code page 932) decoding with our own table.
    public static class Sjis
    {
        // Decodes up to max bytes starting at offset and stops at the first NUL.
        // A lead byte with no trail byte left (names cut at 15 bytes) is dropped.
        public static string Decode(byte[] d, int offset, int max)
        {
            int end = offset + max;
            if (end > d.Length) end = d.Length;
            StringBuilder sb = new StringBuilder(max);
            int i = offset;
            while (i < end)
            {
                int b = d[i];
                if (b == 0) break;
                if (b < 0x80) { sb.Append((char)b); i++; continue; }
                if (b >= 0xA1 && b <= 0xDF) { sb.Append((char)(0xFF61 + (b - 0xA1))); i++; continue; }
                int lead;
                if (b >= 0x81 && b <= 0x9F) lead = b - 0x81;
                else if (b >= 0xE0 && b <= 0xFC) lead = b - 0xE0 + 31;
                else { sb.Append('?'); i++; continue; }
                if (i + 1 >= end) break;
                int t = d[i + 1];
                int trail;
                if (t >= 0x40 && t <= 0x7E) trail = t - 0x40;
                else if (t >= 0x80 && t <= 0xFC) trail = t - 0x80 + 63;
                else { sb.Append('?'); i++; continue; }
                char c = SjisTable.Data[lead * SjisTable.Trails + trail];
                sb.Append(c == '\0' ? '?' : c);
                i += 2;
            }
            return sb.ToString();
        }

        // Names differ between models only by full-width versus half-width letters and digits
        // ("足ＩＫ" / "足IK", "上半身２" / "上半身2"). Fold full-width ASCII to half-width and
        // half-width katakana is left alone.
        public static string Fold(string s)
        {
            if (s == null) return "";
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char f = c;
                if (c >= 0xFF01 && c <= 0xFF5E) f = (char)(c - 0xFF01 + 0x21);
                else if (c == 0x3000) f = ' ';
                if (f != c && sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, i); }
                if (sb != null) sb.Append(f);
            }
            return sb == null ? s : sb.ToString();
        }
    }
}
