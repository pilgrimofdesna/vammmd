using System;
using System.Collections.Generic;

namespace VamMmd.Core
{
    // One bone's keys, sorted by frame, one key per frame.
    // Curve bytes: 16 per key = X(x1,y1,x2,y2) Y(...) Z(...) R(...), each 0..127.
    // MMD rule: the curve stored on a key shapes the segment that ENDS at that key.
    public sealed class BoneTrack
    {
        public string Name;       // folded name (full-width letters and digits made half-width)
        public int[] Frame;
        public V3[] Pos;
        public Q[] Rot;
        public byte[] Curve;
        public bool AnyPos;       // some key has a non-zero translation
        public bool AnyRot;       // some key has a non-identity rotation

        public int Count { get { return Frame.Length; } }

        // Pose at a (fractional) frame. cursor is a caller-owned hint that makes sequential
        // playback O(1); any value is accepted.
        public void Sample(double frame, ref int cursor, out V3 pos, out Q rot)
        {
            int n = Frame.Length;
            if (n == 0) { pos = V3.Zero; rot = Q.Identity; return; }
            if (frame <= Frame[0] || n == 1) { pos = Pos[0]; rot = Rot[0]; cursor = 0; return; }
            if (frame >= Frame[n - 1]) { pos = Pos[n - 1]; rot = Rot[n - 1]; cursor = n - 2; return; }

            int i = cursor;
            if (i < 0 || i > n - 2) i = 0;
            if (!(Frame[i] <= frame && frame < Frame[i + 1]))
            {
                if (i + 2 <= n - 1 && Frame[i + 1] <= frame && frame < Frame[i + 2]) i = i + 1;
                else
                {
                    int lo = 0, hi = n - 1;      // invariant: Frame[lo] <= frame < Frame[hi]
                    while (hi - lo > 1)
                    {
                        int mid = (lo + hi) >> 1;
                        if (Frame[mid] <= frame) lo = mid; else hi = mid;
                    }
                    i = lo;
                }
            }
            cursor = i;

            int j = i + 1;
            double s = (frame - Frame[i]) / (double)(Frame[j] - Frame[i]);
            int c = j * 16;
            V3 a = Pos[i], b = Pos[j];
            double tx = Bezier.Ease(Curve[c], Curve[c + 1], Curve[c + 2], Curve[c + 3], s);
            double ty = Bezier.Ease(Curve[c + 4], Curve[c + 5], Curve[c + 6], Curve[c + 7], s);
            double tz = Bezier.Ease(Curve[c + 8], Curve[c + 9], Curve[c + 10], Curve[c + 11], s);
            double tr = Bezier.Ease(Curve[c + 12], Curve[c + 13], Curve[c + 14], Curve[c + 15], s);
            pos = new V3(a.x + (b.x - a.x) * tx, a.y + (b.y - a.y) * ty, a.z + (b.z - a.z) * tz);
            rot = Q.Slerp(Rot[i], Rot[j], tr);
        }
    }

    public sealed class MorphTrack
    {
        public string Name;
        public int[] Frame;
        public float[] Weight;

        public double Sample(double frame, ref int cursor)
        {
            int n = Frame.Length;
            if (n == 0) return 0;
            if (frame <= Frame[0] || n == 1) { cursor = 0; return Weight[0]; }
            if (frame >= Frame[n - 1]) { cursor = n - 2; return Weight[n - 1]; }
            int i = cursor;
            if (i < 0 || i > n - 2) i = 0;
            if (!(Frame[i] <= frame && frame < Frame[i + 1]))
            {
                int lo = 0, hi = n - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (Frame[mid] <= frame) lo = mid; else hi = mid;
                }
                i = lo;
            }
            cursor = i;
            double s = (frame - Frame[i]) / (double)(Frame[i + 1] - Frame[i]);
            return Weight[i] + (Weight[i + 1] - Weight[i]) * s;
        }
    }

    public struct CameraKey
    {
        public int Frame;
        public float Distance;      // MMD stores it negative: the camera sits behind the target
        public V3 Target;
        public V3 Euler;            // radians, as stored
        public int Fov;             // degrees
        public bool Orthographic;
        public byte[] Curve;        // 24 bytes: (x1,x2,y1,y2) for X, Y, Z, rotation, distance, FOV
    }

    // One "show / IK" key: the model's visibility and the on/off state of the listed IK bones
    // from this frame on (step, no interpolation).
    public sealed class IkKey
    {
        public int Frame;
        public bool Show;
        public string[] Names;      // folded
        public bool[] Enabled;
    }

    public sealed class VmdFile
    {
        public string ModelName = "";
        public bool OldHeader;
        public List<BoneTrack> Bones = new List<BoneTrack>();
        public List<MorphTrack> Morphs = new List<MorphTrack>();
        public List<CameraKey> Camera = new List<CameraKey>();
        public List<IkKey> IkKeys = new List<IkKey>();
        public int BoneKeyCount, MorphKeyCount;
        public int LastFrame;                 // largest frame number of any bone, morph or camera key
        public string Warning;                // non-fatal oddities found while reading, or null

        public bool IsCameraOnly { get { return BoneKeyCount == 0 && MorphKeyCount == 0 && Camera.Count > 0; } }

        public const double Fps = 30.0;

        public BoneTrack FindBone(string foldedName)
        {
            for (int i = 0; i < Bones.Count; i++) if (Bones[i].Name == foldedName) return Bones[i];
            return null;
        }

        public MorphTrack FindMorph(string foldedName)
        {
            for (int i = 0; i < Morphs.Count; i++) if (Morphs[i].Name == foldedName) return Morphs[i];
            return null;
        }

        // IK state of a bone at a frame: +1 on, 0 off, -1 the file says nothing about this bone.
        public int IkState(string foldedName, double frame)
        {
            int state = -1;
            for (int i = 0; i < IkKeys.Count; i++)
            {
                IkKey k = IkKeys[i];
                // The first key also covers the time before it (files key frame 0 in practice).
                if (k.Frame > frame && i > 0) break;
                for (int j = 0; j < k.Names.Length; j++)
                    if (k.Names[j] == foldedName) { state = k.Enabled[j] ? 1 : 0; break; }
            }
            return state;
        }

        private struct RawBone { public int Frame; public int Offset; public int Order; }
        private struct RawMorph { public int Frame; public float W; public int Order; }

        // Number of bone keys in a file without reading it all: 0 for camera or face only files,
        // -1 when the bytes are not a VMD.
        public static int PeekBoneKeyCount(byte[] d)
        {
            if (d == null || d.Length < 54) return -1;
            string magic = Sjis.Decode(d, 0, 30);
            if (!magic.StartsWith("Vocaloid Motion Data")) return -1;
            int o = 30 + (magic.StartsWith("Vocaloid Motion Data 0002") ? 20 : 10);
            if (o + 4 > d.Length) return -1;
            int n = d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24);
            return n < 0 ? -1 : n;
        }

        public static VmdFile Parse(byte[] d)
        {
            if (d == null || d.Length < 54) throw new FormatException2("not a VMD file (too short)");
            string magic = Sjis.Decode(d, 0, 30);
            if (!magic.StartsWith("Vocaloid Motion Data")) throw new FormatException2("not a VMD file (bad header)");
            VmdFile f = new VmdFile();
            f.OldHeader = !magic.StartsWith("Vocaloid Motion Data 0002");
            int nameLen = f.OldHeader ? 10 : 20;
            f.ModelName = Sjis.Decode(d, 30, nameLen);
            ByteReader r = new ByteReader(d, 30 + nameLen);

            // ---- bone keys: 15 name, u32 frame, 3f pos, 4f quat, 64 curve = 111 bytes
            int nb = r.I32();
            if (nb < 0 || (long)nb * 111 > r.Remaining) throw new FormatException2("bone key count " + nb + " does not fit the file");
            Dictionary<string, List<RawBone>> byName = new Dictionary<string, List<RawBone>>();
            List<string> order = new List<string>();
            Dictionary<ulong, string> nameCache = new Dictionary<ulong, string>();
            int p = r.Position;
            int last = 0;
            for (int i = 0; i < nb; i++, p += 111)
            {
                string name = CachedName(d, p, 15, nameCache);
                List<RawBone> list;
                if (!byName.TryGetValue(name, out list))
                {
                    list = new List<RawBone>();
                    byName[name] = list;
                    order.Add(name);
                }
                RawBone rb;
                rb.Frame = d[p + 15] | (d[p + 16] << 8) | (d[p + 17] << 16) | (d[p + 18] << 24);
                rb.Offset = p;
                rb.Order = i;
                if (rb.Frame < 0) continue;
                list.Add(rb);
            }
            r.Position = p;
            f.BoneKeyCount = nb;
            for (int n = 0; n < order.Count; n++)
            {
                List<RawBone> list = byName[order[n]];
                if (list.Count == 0) continue;
                list.Sort(CompareRawBone);
                // One key per frame: the one written last in the file wins.
                int m = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (m > 0 && list[m - 1].Frame == list[i].Frame) list[m - 1] = list[i];
                    else list[m++] = list[i];
                }
                BoneTrack t = new BoneTrack();
                t.Name = order[n];
                t.Frame = new int[m];
                t.Pos = new V3[m];
                t.Rot = new Q[m];
                t.Curve = new byte[m * 16];
                for (int i = 0; i < m; i++)
                {
                    int o = list[i].Offset;
                    t.Frame[i] = list[i].Frame;
                    V3 pos = new V3(ByteReader.Single(d, o + 19), ByteReader.Single(d, o + 23), ByteReader.Single(d, o + 27));
                    if (double.IsNaN(pos.x + pos.y + pos.z) || double.IsInfinity(pos.x + pos.y + pos.z)) pos = V3.Zero;
                    Q q = new Q(ByteReader.Single(d, o + 31), ByteReader.Single(d, o + 35), ByteReader.Single(d, o + 39), ByteReader.Single(d, o + 43)).Normalized;
                    t.Pos[i] = pos;
                    t.Rot[i] = q;
                    if (pos.SqrLength > 1e-12) t.AnyPos = true;
                    if (Math.Abs(q.w) < 1 - 1e-9) t.AnyRot = true;
                    int c = o + 47;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        // In the 64-byte block, channel ch (X, Y, Z, rotation) keeps
                        // x1, y1, x2, y2 at bytes ch*16 + 0, 4, 8, 12.
                        t.Curve[i * 16 + ch * 4 + 0] = Clamp127(d[c + ch * 16 + 0]);
                        t.Curve[i * 16 + ch * 4 + 1] = Clamp127(d[c + ch * 16 + 4]);
                        t.Curve[i * 16 + ch * 4 + 2] = Clamp127(d[c + ch * 16 + 8]);
                        t.Curve[i * 16 + ch * 4 + 3] = Clamp127(d[c + ch * 16 + 12]);
                    }
                }
                if (t.Frame[m - 1] > last) last = t.Frame[m - 1];
                f.Bones.Add(t);
            }

            // Everything after the bone section is optional: old tools stop early.
            try
            {
                if (r.Remaining >= 4) ReadMorphs(r, f, nameCache, ref last);
                if (r.Remaining >= 4) ReadCamera(r, f, ref last);
                if (r.Remaining >= 4) { int nl = r.I32(); CheckCount(nl, 28, r, "light"); r.Skip(nl * 28); }
                if (r.Remaining >= 4) { int ns = r.I32(); CheckCount(ns, 9, r, "shadow"); r.Skip(ns * 9); }
                if (r.Remaining >= 4) ReadIk(r, f);
            }
            catch (FormatException2 e)
            {
                f.Warning = "the file is cut short after the bone keys: " + e.Message;
            }
            f.LastFrame = last;
            return f;
        }

        private static void CheckCount(int n, int size, ByteReader r, string what)
        {
            if (n < 0 || (long)n * size > r.Remaining) throw new FormatException2(what + " key count " + n + " does not fit the file");
        }

        private static void ReadMorphs(ByteReader r, VmdFile f, Dictionary<ulong, string> nameCache, ref int last)
        {
            byte[] d = r.Data;
            int nm = r.I32();
            CheckCount(nm, 23, r, "morph");
            f.MorphKeyCount = nm;
            Dictionary<string, List<RawMorph>> byName = new Dictionary<string, List<RawMorph>>();
            List<string> order = new List<string>();
            int p = r.Position;
            for (int i = 0; i < nm; i++, p += 23)
            {
                string name = CachedName(d, p, 15, nameCache);
                List<RawMorph> list;
                if (!byName.TryGetValue(name, out list))
                {
                    list = new List<RawMorph>();
                    byName[name] = list;
                    order.Add(name);
                }
                RawMorph rm;
                rm.Frame = d[p + 15] | (d[p + 16] << 8) | (d[p + 17] << 16) | (d[p + 18] << 24);
                rm.W = ByteReader.Single(d, p + 19);
                rm.Order = i;
                if (rm.Frame < 0 || float.IsNaN(rm.W) || float.IsInfinity(rm.W)) continue;
                list.Add(rm);
            }
            r.Position = p;
            for (int n = 0; n < order.Count; n++)
            {
                List<RawMorph> list = byName[order[n]];
                if (list.Count == 0) continue;
                list.Sort(CompareRawMorph);
                int m = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    if (m > 0 && list[m - 1].Frame == list[i].Frame) list[m - 1] = list[i];
                    else list[m++] = list[i];
                }
                MorphTrack t = new MorphTrack();
                t.Name = order[n];
                t.Frame = new int[m];
                t.Weight = new float[m];
                for (int i = 0; i < m; i++) { t.Frame[i] = list[i].Frame; t.Weight[i] = list[i].W; }
                if (t.Frame[m - 1] > last) last = t.Frame[m - 1];
                f.Morphs.Add(t);
            }
        }

        private static void ReadCamera(ByteReader r, VmdFile f, ref int last)
        {
            int nc = r.I32();
            CheckCount(nc, 61, r, "camera");
            for (int i = 0; i < nc; i++)
            {
                CameraKey k = new CameraKey();
                k.Frame = r.I32();
                k.Distance = r.F32();
                k.Target = new V3(r.F32(), r.F32(), r.F32());
                k.Euler = new V3(r.F32(), r.F32(), r.F32());
                k.Curve = new byte[24];
                for (int j = 0; j < 24; j++) k.Curve[j] = Clamp127(r.U8());
                k.Fov = r.I32();
                k.Orthographic = r.U8() != 0;
                if (k.Frame < 0) continue;
                f.Camera.Add(k);
                if (k.Frame > last) last = k.Frame;
            }
            f.Camera.Sort(CompareCamera);
        }

        private static void ReadIk(ByteReader r, VmdFile f)
        {
            int nk = r.I32();
            if (nk < 0 || (long)nk * 9 > r.Remaining) throw new FormatException2("IK key count " + nk + " does not fit the file");
            for (int i = 0; i < nk; i++)
            {
                IkKey k = new IkKey();
                k.Frame = r.I32();
                k.Show = r.U8() != 0;
                int cnt = r.I32();
                if (cnt < 0 || (long)cnt * 21 > r.Remaining) throw new FormatException2("IK entry count " + cnt + " does not fit the file");
                k.Names = new string[cnt];
                k.Enabled = new bool[cnt];
                for (int j = 0; j < cnt; j++)
                {
                    k.Names[j] = Sjis.Fold(Sjis.Decode(r.Data, r.Position, 20));
                    r.Skip(20);
                    k.Enabled[j] = r.U8() != 0;
                }
                f.IkKeys.Add(k);
            }
            f.IkKeys.Sort(CompareIk);
        }

        private static byte Clamp127(byte b) { return b > 127 ? (byte)127 : b; }

        // Names repeat tens of thousands of times; decode each distinct byte pattern once.
        private static string CachedName(byte[] d, int o, int len, Dictionary<ulong, string> cache)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < len; i++)
            {
                byte b = d[o + i];
                if (b == 0) break;
                h = (h ^ b) * 1099511628211UL;
            }
            string s;
            if (cache.TryGetValue(h, out s)) return s;
            s = Sjis.Fold(Sjis.Decode(d, o, len));
            cache[h] = s;
            return s;
        }

        private static int CompareRawBone(RawBone a, RawBone b)
        {
            if (a.Frame != b.Frame) return a.Frame < b.Frame ? -1 : 1;
            return a.Order < b.Order ? -1 : (a.Order > b.Order ? 1 : 0);
        }

        private static int CompareRawMorph(RawMorph a, RawMorph b)
        {
            if (a.Frame != b.Frame) return a.Frame < b.Frame ? -1 : 1;
            return a.Order < b.Order ? -1 : (a.Order > b.Order ? 1 : 0);
        }

        private static int CompareCamera(CameraKey a, CameraKey b)
        {
            return a.Frame < b.Frame ? -1 : (a.Frame > b.Frame ? 1 : 0);
        }

        private static int CompareIk(IkKey a, IkKey b)
        {
            return a.Frame < b.Frame ? -1 : (a.Frame > b.Frame ? 1 : 0);
        }
    }

    // MMD's interpolation curve: a cubic Bezier from (0,0) to (1,1) with control points
    // (x1,y1) and (x2,y2) given as 0..127. Ease(s) returns y at the point where x = s.
    public static class Bezier
    {
        public static double Ease(int bx1, int by1, int bx2, int by2, double s)
        {
            if (s <= 0) return 0;
            if (s >= 1) return 1;
            if (bx1 == by1 && bx2 == by2) return s;       // on the diagonal: linear
            double x1 = bx1 / 127.0, y1 = by1 / 127.0, x2 = bx2 / 127.0, y2 = by2 / 127.0;
            // x(t) = 3(1-t)^2 t x1 + 3(1-t) t^2 x2 + t^3 is monotonic for x1, x2 in 0..1.
            double lo = 0, hi = 1, t = s;
            for (int i = 0; i < 64; i++)
            {
                double u = 1 - t;
                double x = 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t;
                double e = x - s;
                if (Math.Abs(e) < 1e-15) break;
                if (e > 0) hi = t; else lo = t;
                double dx = 3 * u * u * x1 + 6 * u * t * (x2 - x1) + 3 * t * t * (1 - x2);
                double tn = dx > 1e-9 ? t - e / dx : -1;
                if (tn <= lo || tn >= hi) tn = 0.5 * (lo + hi);      // Newton left the bracket: bisect
                t = tn;
            }
            double v = 1 - t;
            return 3 * v * v * t * y1 + 3 * v * t * t * y2 + t * t * t;
        }
    }
}
