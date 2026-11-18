using System;
using System.Collections.Generic;
using System.Text;
using VamMmd.Core;

// Unity-free tests of the core. Build and run with tests/run.sh; every line must start with PASS.
public static class CoreTests
{
    private static int _failed, _passed;
    private static string _group = "";

    private static void Check(bool ok, string what)
    {
        if (ok) _passed++;
        else { _failed++; Console.WriteLine("FAIL: " + _group + ": " + what); }
    }

    private static void Near(double got, double want, double tol, string what)
    {
        Check(Math.Abs(got - want) <= tol, what + " (got " + got.ToString("R") + ", want " + want.ToString("R") + ")");
    }

    private static void NearV(V3 got, V3 want, double tol, string what)
    {
        Check(V3.Distance(got, want) <= tol, what + " (got " + got + ", want " + want + ")");
    }

    private static void NearQ(Q got, Q want, double tolRad, string what)
    {
        double a = (got * want.Conj).Angle;
        Check(a <= tolRad, what + " (got " + got + ", want " + want + ", off " + (a * 180 / Math.PI).ToString("0.####") + " deg)");
    }

    private static void Group(string name, Action body)
    {
        _group = name;
        int before = _failed;
        try { body(); }
        catch (Exception e) { _failed++; Console.WriteLine("FAIL: " + name + ": threw " + e); }
        if (_failed == before) Console.WriteLine("PASS: " + name);
    }

    public static int Main()
    {
        Group("math", MathTests);
        Group("shift-jis", SjisTests);
        Group("bezier", BezierTests);
        Group("vmd parse", VmdParseTests);
        Group("track sampling", SamplingTests);
        Group("rig forward", RigForwardTests);
        Group("rig cancel bones", CancelTests);
        Group("leg ik", LegIkTests);
        Group("toe ik and d bones", ToeAndDTests);
        Group("ik switches", IkSwitchTests);
        Group("body fit", FitTests);
        Group("clock", ClockTests);
        Group("wav", WavTests);
        Console.WriteLine((_failed == 0 ? "PASS: " : "FAIL: ") + _passed + " checks, " + _failed + " failed");
        return _failed == 0 ? 0 : 1;
    }

    private const double Deg = Math.PI / 180.0;

    // ---- math ------------------------------------------------------------

    private static void MathTests()
    {
        Q z90 = Q.AxisAngle(new V3(0, 0, 1), 90 * Deg);
        NearV(z90.Rotate(new V3(1, 0, 0)), new V3(0, 1, 0), 1e-12, "z90 turns +x to +y");
        Q x90 = Q.AxisAngle(new V3(1, 0, 0), 90 * Deg);
        // (a * b) applies b first
        NearV((z90 * x90).Rotate(new V3(0, 1, 0)), z90.Rotate(x90.Rotate(new V3(0, 1, 0))), 1e-12, "product order");
        NearQ(Q.Slerp(Q.Identity, z90, 0.5), Q.AxisAngle(new V3(0, 0, 1), 45 * Deg), 1e-12, "slerp half");
        NearQ(Q.Slerp(Q.Identity, new Q(-z90.x, -z90.y, -z90.z, -z90.w), 0.5), Q.AxisAngle(new V3(0, 0, 1), 45 * Deg), 1e-12, "slerp takes the short way");
        Q big = Q.AxisAngle(new V3(0, 1, 0), 170 * Deg);
        NearQ(Q.Slerp(Q.Identity, big, 0.25), Q.AxisAngle(new V3(0, 1, 0), 42.5 * Deg), 1e-12, "slerp is uniform on a wide turn");
        NearV(Q.FromTo(new V3(2, 0, 0), new V3(0, 0, 5)).Rotate(new V3(1, 0, 0)), new V3(0, 0, 1), 1e-12, "FromTo");
        NearV(Q.FromTo(new V3(1, 0, 0), new V3(-1, 0, 0)).Rotate(new V3(1, 0, 0)), new V3(-1, 0, 0), 1e-9, "FromTo opposite");
        NearQ(new Q(0, 0, 0, 0).Normalized, Q.Identity, 1e-12, "zero quaternion becomes identity");
        Near(z90.Angle, 90 * Deg, 1e-12, "angle");
    }

    private static void SjisTests()
    {
        // "左足ＩＫ" in cp932, padded to 15 bytes
        byte[] b = { 0x8D, 0xB6, 0x91, 0xAB, 0x82, 0x68, 0x82, 0x6A, 0, 0xFD, 0xFD, 0, 0, 0, 0 };
        string s = Sjis.Decode(b, 0, 15);
        Check(s == "左足ＩＫ", "decode 左足ＩＫ, stop at NUL");
        Check(Sjis.Fold(s) == "左足IK", "fold full-width letters");
        Check(Sjis.Fold("上半身２") == "上半身2", "fold full-width digit");
        // half-width katakana and ASCII
        byte[] k = { 0xC8, 0xB8, 0xC0, 0xB2, 0x49, 0x4B };
        Check(Sjis.Decode(k, 0, 6) == "ﾈｸﾀｲIK", "half-width katakana");
        // a name cut in the middle of a two-byte character drops the broken half
        byte[] cut = { 0x8D, 0xB6, 0x91 };
        Check(Sjis.Decode(cut, 0, 3) == "左", "cut name");
        Check(B.Find("左足IK") == B.S(0, B.LegIK), "bone lookup by folded name");
        Check(B.Find("腰キャンセル右") == B.S(1, B.WaistCancel), "waist cancel has the side as suffix");
        for (int i = 0; i < B.Count; i++) Check(B.Parent[i] < i, "parents come first: " + i);
    }

    private static void E(int x1, int y1, int x2, int y2, double s, double want)
    {
        Near(Bezier.Ease(x1, y1, x2, y2, s), want, 1e-9, "ease(" + x1 + "," + y1 + "," + x2 + "," + y2 + "," + s + ")");
    }

    private static void BezierTests()
    {
        // expected values: 120-step bisection at 40 decimal digits
        E(20, 107, 107, 20, 0.01, 0.048419689573548);
        E(20, 107, 107, 20, 0.3, 0.448842790841521);
        E(20, 107, 107, 20, 0.5, 0.500000000000000);
        E(20, 107, 107, 20, 0.77, 0.586480805038421);
        E(20, 107, 107, 20, 0.999, 0.994707573203383);
        E(64, 0, 64, 127, 0.01, 0.000132425445459);
        E(64, 0, 64, 127, 0.3, 0.157337924413340);
        E(64, 0, 64, 127, 0.5, 0.494095098305376);
        E(64, 0, 64, 127, 0.77, 0.910607324394954);
        E(64, 0, 64, 127, 0.999, 0.999998644204019);
        E(127, 0, 0, 127, 0.01, 0.000033708715549);
        E(127, 0, 0, 127, 0.3, 0.047395275203942);
        E(127, 0, 0, 127, 0.77, 0.975743963733854);
        E(127, 0, 0, 127, 0.999, 0.999999666295802);
        E(0, 127, 127, 0, 0.01, 0.156709407334586);
        E(0, 127, 127, 0, 0.3, 0.489772473271703);
        E(0, 127, 127, 0, 0.77, 0.527005922195873);
        E(0, 127, 127, 0, 0.999, 0.946889238423565);
        E(40, 10, 80, 120, 0.01, 0.002762668794945);
        E(40, 10, 80, 120, 0.3, 0.259669935927633);
        E(40, 10, 80, 120, 0.5, 0.538109118461858);
        E(40, 10, 80, 120, 0.77, 0.869380153112781);
        E(40, 10, 80, 120, 0.999, 0.999849070595110);
        E(1, 126, 126, 1, 0.3, 0.488022999808087);
        E(1, 126, 126, 1, 0.999, 0.956848106456168);
        E(127, 127, 0, 0, 0.3, 0.3);
        E(0, 0, 127, 127, 0.77, 0.77);
        // the flat middle of (127,0,0,127): the slope of x(t) is zero at t = 0.5
        Near(Bezier.Ease(127, 0, 0, 127, 0.5), 0.5, 1e-4, "flat middle");
        Near(Bezier.Ease(20, 107, 107, 20, 0), 0, 0, "start");
        Near(Bezier.Ease(20, 107, 107, 20, 1), 1, 0, "end");
    }

    // ---- synthetic VMD ------------------------------------------------------

    private sealed class VmdBuilder
    {
        private readonly List<byte> _bones = new List<byte>();
        private readonly List<byte> _morphs = new List<byte>();
        private readonly List<byte> _ik = new List<byte>();
        private int _nb, _nm, _nik;

        private static void F(List<byte> l, float v) { l.AddRange(BitConverter.GetBytes(v)); }
        private static void I(List<byte> l, int v) { l.AddRange(BitConverter.GetBytes(v)); }

        private static void Name(List<byte> l, byte[] raw, int len)
        {
            for (int i = 0; i < len; i++) l.Add(i < raw.Length ? raw[i] : (byte)0);
        }

        // curve: 16 values X(x1,y1,x2,y2) Y Z R, or null for linear
        public void Bone(byte[] rawName, int frame, V3 pos, Q rot, int[] curve)
        {
            Name(_bones, rawName, 15);
            I(_bones, frame);
            F(_bones, (float)pos.x); F(_bones, (float)pos.y); F(_bones, (float)pos.z);
            F(_bones, (float)rot.x); F(_bones, (float)rot.y); F(_bones, (float)rot.z); F(_bones, (float)rot.w);
            byte[] c = new byte[64];
            for (int ch = 0; ch < 4; ch++)
            {
                int x1 = curve == null ? 20 : curve[ch * 4], y1 = curve == null ? 20 : curve[ch * 4 + 1];
                int x2 = curve == null ? 107 : curve[ch * 4 + 2], y2 = curve == null ? 107 : curve[ch * 4 + 3];
                c[ch * 16 + 0] = (byte)x1; c[ch * 16 + 4] = (byte)y1; c[ch * 16 + 8] = (byte)x2; c[ch * 16 + 12] = (byte)y2;
            }
            _bones.AddRange(c);
            _nb++;
        }

        public void Morph(byte[] rawName, int frame, float w)
        {
            Name(_morphs, rawName, 15); I(_morphs, frame); F(_morphs, w); _nm++;
        }

        public void Ik(int frame, bool show, byte[][] names, bool[] on)
        {
            I(_ik, frame); _ik.Add((byte)(show ? 1 : 0)); I(_ik, names.Length);
            for (int i = 0; i < names.Length; i++) { Name(_ik, names[i], 20); _ik.Add((byte)(on[i] ? 1 : 0)); }
            _nik++;
        }

        public byte[] Build(bool withTail)
        {
            List<byte> d = new List<byte>();
            Name(d, Encoding.ASCII.GetBytes("Vocaloid Motion Data 0002"), 30);
            Name(d, Encoding.ASCII.GetBytes("test model"), 20);
            I(d, _nb); d.AddRange(_bones);
            if (!withTail) return d.ToArray();
            I(d, _nm); d.AddRange(_morphs);
            I(d, 0);            // camera
            I(d, 0);            // light
            I(d, 0);            // shadow
            I(d, _nik); d.AddRange(_ik);
            return d.ToArray();
        }
    }

    // cp932 bytes of the bone names used by the tests
    private static readonly byte[] NCenter = { 0x83, 0x5A, 0x83, 0x93, 0x83, 0x5E, 0x81, 0x5B };                    // センター
    private static readonly byte[] NLArm = { 0x8D, 0xB6, 0x98, 0x72 };                                             // 左腕
    private static readonly byte[] NLLegIk = { 0x8D, 0xB6, 0x91, 0xAB, 0x82, 0x68, 0x82, 0x6A };                    // 左足ＩＫ
    private static readonly byte[] NRLegIk = { 0x89, 0x45, 0x91, 0xAB, 0x82, 0x68, 0x82, 0x6A };                    // 右足ＩＫ
    private static readonly byte[] NLToeIk = { 0x8D, 0xB6, 0x82, 0xC2, 0x82, 0xDC, 0x90, 0xE6, 0x82, 0x68, 0x82, 0x6A };   // 左つま先ＩＫ
    private static readonly byte[] NLKnee = { 0x8D, 0xB6, 0x82, 0xD0, 0x82, 0xB4 };                                // 左ひざ
    private static readonly byte[] NA = { 0x82, 0xA0 };                                                           // あ

    private static void VmdParseTests()
    {
        VmdBuilder b = new VmdBuilder();
        // written out of order, with a duplicate frame: the later record wins
        b.Bone(NCenter, 30, new V3(0, 5, 0), Q.Identity, null);
        b.Bone(NCenter, 0, new V3(0, 1, 0), Q.Identity, null);
        b.Bone(NLArm, 10, V3.Zero, Q.AxisAngle(new V3(0, 0, 1), 30 * Deg), null);
        b.Bone(NCenter, 30, new V3(0, 7, 0), Q.Identity, new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        b.Morph(NA, 20, 1f);
        b.Morph(NA, 0, 0f);
        b.Ik(0, true, new byte[][] { NLLegIk, NRLegIk }, new bool[] { false, true });
        b.Ik(100, true, new byte[][] { NLLegIk }, new bool[] { true });
        VmdFile f = VmdFile.Parse(b.Build(true));
        Check(f.ModelName == "test model", "model name");
        Check(f.Bones.Count == 2 && f.BoneKeyCount == 4, "bone tracks");
        BoneTrack c = f.FindBone("センター");
        Check(c != null && c.Count == 2 && c.Frame[0] == 0 && c.Frame[1] == 30, "sorted, one key per frame");
        Near(c.Pos[1].y, 7, 1e-6, "later duplicate wins");
        Check(c.Curve[16 + 0] == 1 && c.Curve[16 + 1] == 2 && c.Curve[16 + 2] == 3 && c.Curve[16 + 3] == 4 && c.Curve[16 + 12] == 13 && c.Curve[16 + 15] == 16, "curve bytes X and R");
        Check(c.AnyPos && !c.AnyRot, "centre moves, does not turn");
        Check(f.Morphs.Count == 1 && f.Morphs[0].Frame[0] == 0, "morph track sorted");
        int cur = 0;
        Near(f.Morphs[0].Sample(5, ref cur), 0.25, 1e-9, "morph linear");
        Check(f.LastFrame == 30, "last frame");
        Check(f.IkState("左足IK", 50) == 0 && f.IkState("左足IK", 100) == 1 && f.IkState("右足IK", 500) == 1, "ik states step");
        Check(f.IkState("左つま先IK", 10) == -1, "unknown ik bone");
        // file that ends right after the bone keys (old tools)
        VmdFile g = VmdFile.Parse(b.Build(false));
        Check(g.Bones.Count == 2 && g.Morphs.Count == 0 && g.Warning == null, "no tail is fine");
        // truncated in the middle of the tail: bones survive, warning set
        byte[] full = b.Build(true);
        byte[] cutBytes = new byte[full.Length - 30];
        Array.Copy(full, cutBytes, cutBytes.Length);
        VmdFile h = VmdFile.Parse(cutBytes);
        Check(h.Bones.Count == 2 && h.Warning != null, "cut tail keeps the bones and warns");
        bool threw = false;
        try { VmdFile.Parse(new byte[100]); } catch (FormatException2) { threw = true; }
        Check(threw, "garbage is rejected");
        // a bone count larger than the file
        byte[] bad = b.Build(true);
        bad[50] = 0xFF; bad[51] = 0xFF; bad[52] = 0xFF; bad[53] = 0x7F;
        threw = false;
        try { VmdFile.Parse(bad); } catch (FormatException2) { threw = true; }
        Check(threw, "impossible key count is rejected");
    }

    private static void SamplingTests()
    {
        VmdBuilder b = new VmdBuilder();
        int[] slowStart = { 127, 0, 0, 127, 20, 20, 107, 107, 20, 20, 107, 107, 64, 0, 64, 127 };
        Q r0 = Q.Identity, r1 = Q.AxisAngle(new V3(0, 1, 0), 160 * Deg), r2 = Q.AxisAngle(new V3(0, 1, 0), 100 * Deg);
        // key 0 carries a curve that must NOT be used for the first segment
        b.Bone(NCenter, 0, new V3(0, 0, 0), r0, new int[] { 0, 127, 127, 0, 0, 127, 127, 0, 0, 127, 127, 0, 0, 127, 127, 0 });
        b.Bone(NCenter, 10, new V3(10, 10, 10), r1, slowStart);
        b.Bone(NCenter, 20, new V3(10, 0, 10), r2, null);
        VmdFile f = VmdFile.Parse(b.Build(true));
        BoneTrack t = f.Bones[0];
        int cur = 0; V3 p; Q q;
        t.Sample(3, ref cur, out p, out q);
        // segment 0..10 uses key 10's curves: X eased by (127,0,0,127) at s = 0.3, Y linear, rotation by (64,0,64,127)
        Near(p.x, 10 * 0.047395275203942, 1e-6, "x uses the ending key's X curve");
        Near(p.y, 3, 1e-6, "y uses the ending key's Y curve (linear)");
        NearQ(q, Q.AxisAngle(new V3(0, 1, 0), 160 * 0.157337924413340 * Deg), 1e-6, "rotation: ending key's curve, slerp");
        // exactly on a key frame: the key's own value, and the segment after it starts there
        t.Sample(10, ref cur, out p, out q);
        NearV(p, new V3(10, 10, 10), 1e-6, "on key: position");
        NearQ(q, r1, 1e-6, "on key: rotation");
        t.Sample(10.5, ref cur, out p, out q);
        Near(p.y, 9.5, 1e-6, "just after a key the next segment is already moving");
        NearQ(q, Q.AxisAngle(new V3(0, 1, 0), 157 * Deg), 1e-6, "rotation moves inside the frame after a key");
        // hold before the first and after the last key
        t.Sample(-5, ref cur, out p, out q); NearV(p, V3.Zero, 0, "before first key");
        t.Sample(99, ref cur, out p, out q); NearV(p, new V3(10, 0, 10), 1e-6, "after last key");
        // any cursor value works, and backwards seeks too
        cur = 12345; t.Sample(15, ref cur, out p, out q); Near(p.y, 5, 1e-6, "bad cursor is ignored");
        t.Sample(2, ref cur, out p, out q); Near(p.y, 2, 1e-6, "seek backwards");
        // slerp, not lerp: halfway through a 160 degree turn with a linear curve is 80 degrees
        VmdBuilder b2 = new VmdBuilder();
        b2.Bone(NLArm, 0, V3.Zero, Q.Identity, null);
        b2.Bone(NLArm, 10, V3.Zero, r1, null);
        BoneTrack t2 = VmdFile.Parse(b2.Build(true)).Bones[0];
        cur = 0; t2.Sample(2.5, ref cur, out p, out q);
        NearQ(q, Q.AxisAngle(new V3(0, 1, 0), 40 * Deg), 1e-6, "quarter of a wide turn is a quarter of the angle");
    }

    // ---- rig ---------------------------------------------------------------

    private static void RigForwardTests()
    {
        RefBody rf = RefBody.Tda();
        MmdRig rig = rf.ToRig();
        RigPose p = rig.NewPose();
        p.LegIkOn[0] = p.LegIkOn[1] = false; p.ToeIkOn[0] = p.ToeIkOn[1] = false;
        rig.Evaluate(p);
        for (int i = 0; i < B.Count; i++) NearV(p.WorldPos[i], rf.Rest[i], 1e-9, "rest pose: " + B.Name[i]);
        // left arm 90 degrees about Z: the elbow swings around the shoulder joint
        int arm = B.S(0, B.Arm), elbow = B.S(0, B.Elbow), wrist = B.S(0, B.Wrist);
        Q z90 = Q.AxisAngle(new V3(0, 0, 1), 90 * Deg);
        p.AnimRot[arm] = z90;
        rig.Evaluate(p);
        V3 want = rf.Rest[arm] + z90.Rotate(rf.Rest[elbow] - rf.Rest[arm]);
        NearV(p.WorldPos[elbow], want, 1e-9, "elbow after arm turn");
        NearQ(p.WorldRot[wrist], z90, 1e-9, "wrist inherits the arm rotation");
        // centre translation: everything above moves, the IK targets do not
        p.AnimRot[arm] = Q.Identity;
        p.AnimPos[B.Center] = new V3(1, -2, 3);
        rig.Evaluate(p);
        NearV(p.WorldPos[B.Head], rf.Rest[B.Head] + new V3(1, -2, 3), 1e-9, "head follows the centre");
        NearV(p.WorldPos[B.S(0, B.LegIK)], rf.Rest[B.S(0, B.LegIK)], 1e-9, "foot IK target stays");
        // twist bone: the forearm rotation changes, the elbow position does not
        p.AnimPos[B.Center] = V3.Zero;
        V3 axis = rf.Dir(B.Arm, B.Elbow);
        p.AnimRot[B.S(0, B.ArmTwist)] = Q.AxisAngle(axis, 40 * Deg);
        rig.Evaluate(p);
        NearV(p.WorldPos[elbow], rf.Rest[elbow], 0.02, "twist keeps the elbow (the twist bone sits on the arm line)");
        Near(p.WorldRot[elbow].Angle, 40 * Deg, 1e-9, "twist reaches the forearm");
        // translation scale and pelvis travel
        MmdRig r2 = rf.ToRig();
        r2.UnitScale = 0.08; r2.PelvisTravel = 1.5;
        RigPose p2 = r2.NewPose();
        p2.LegIkOn[0] = p2.LegIkOn[1] = false;
        p2.AnimPos[B.Center] = new V3(0, 1, 0);
        p2.AnimPos[B.S(0, B.LegIK)] = new V3(0, 1, 0);
        r2.Evaluate(p2);
        Near(p2.WorldPos[B.Lower].y - rf.Rest[B.Lower].y, 0.12, 1e-9, "centre: unit scale times pelvis travel");
        Near(p2.WorldPos[B.S(0, B.LegIK)].y - rf.Rest[B.S(0, B.LegIK)].y, 0.08, 1e-9, "IK target: unit scale only");
    }

    private static void CancelTests()
    {
        RefBody rf = RefBody.Tda();
        MmdRig rig = rf.ToRig();
        RigPose p = rig.NewPose();
        p.LegIkOn[0] = p.LegIkOn[1] = false; p.ToeIkOn[0] = p.ToeIkOn[1] = false;
        // 肩P turns the shoulder; 肩C undoes it for the arm
        Q r = Q.AxisAngle(new V3(0, 0, 1), 25 * Deg);
        p.AnimRot[B.S(0, B.ShoulderP)] = r;
        rig.Evaluate(p);
        NearQ(p.WorldRot[B.S(0, B.Shoulder)], r, 1e-9, "shoulder turns with 肩P");
        NearQ(p.WorldRot[B.S(0, B.Arm)], Q.Identity, 1e-9, "arm keeps its direction");
        Check(V3.Distance(p.WorldPos[B.S(0, B.Arm)], rf.Rest[B.S(0, B.Arm)]) > 0.1, "but the shoulder joint moved");
        // 腰 turns the trunk; 腰キャンセル keeps the legs' rotation
        p.AnimRot[B.S(0, B.ShoulderP)] = Q.Identity;
        Q w = Q.AxisAngle(new V3(0, 1, 0), 30 * Deg);
        p.AnimRot[B.Waist] = w;
        rig.Evaluate(p);
        NearQ(p.WorldRot[B.Upper], w, 1e-9, "upper body turns with 腰");
        NearQ(p.WorldRot[B.Lower], w, 1e-9, "lower body turns with 腰");
        NearQ(p.WorldRot[B.S(0, B.Leg)], Q.Identity, 1e-9, "legs do not turn");
        NearQ(p.WorldRot[B.S(1, B.LegD)], Q.Identity, 1e-9, "D legs do not turn either");
    }

    private static void LegIkTests()
    {
        RefBody rf = RefBody.Tda();
        MmdRig rig = rf.ToRig();
        RigPose p = rig.NewPose();
        int ik = B.S(0, B.LegIK), ankle = B.S(0, B.Ankle), knee = B.S(0, B.Knee), leg = B.S(0, B.Leg);
        // rest: the goal is on the ankle, but the hinge keeps the knee bent by at least 0.5 degrees
        // (that is what lets a straight leg start bending), which lifts the ankle by 0.0016 units
        rig.Evaluate(p);
        NearV(p.WorldPos[ankle], rf.Rest[ankle], 0.003, "rest: ankle on target");
        Near(p.KneeAngle[0], rig.KneeMax, 1e-12, "rest: knee at its upper limit");
        // lift the foot 3 units and bring it 1 forward (-Z): reachable, the knee must bend forward
        p.AnimPos[ik] = new V3(0.3, 3, -1);
        rig.Evaluate(p);
        V3 target = rf.Rest[ik] + new V3(0.3, 3, -1);
        Check(V3.Distance(p.WorldPos[ankle], target) < 0.01, "ankle reaches a reachable target (off by " + V3.Distance(p.WorldPos[ankle], target).ToString("0.#####") + ")");
        Check(p.KneeAngle[0] < -5 * Deg && p.KneeAngle[0] >= -Math.PI, "knee angle inside its range: " + (p.KneeAngle[0] / Deg).ToString("0.##"));
        V3 straight = (p.WorldPos[leg] + p.WorldPos[ankle]) * 0.5;
        Check(p.WorldPos[knee].z < straight.z - 0.2, "knee points forward (-Z)");
        Near(V3.Distance(p.WorldPos[leg], p.WorldPos[knee]), V3.Distance(rf.Rest[leg], rf.Rest[knee]), 1e-9, "thigh length kept");
        Near(V3.Distance(p.WorldPos[knee], p.WorldPos[ankle]), V3.Distance(rf.Rest[knee], rf.Rest[ankle]), 1e-9, "shin length kept");
        // the knee's local rotation is a pure X rotation
        Q kl = p.LocalRot[knee];
        Check(Math.Abs(kl.y) < 1e-9 && Math.Abs(kl.z) < 1e-9, "knee turns about X only");
        // deep squat: centre down 5 units, feet stay
        p.AnimPos[ik] = V3.Zero;
        p.AnimPos[B.Center] = new V3(0, -5, 0);
        rig.Evaluate(p);
        for (int s = 0; s < 2; s++)
        {
            Check(V3.Distance(p.WorldPos[B.S(s, B.Ankle)], rf.Rest[B.S(s, B.Ankle)]) < 0.02, "squat: ankle " + s + " stays planted");
            Check(p.KneeAngle[s] < -60 * Deg, "squat: knee " + s + " bends well: " + (p.KneeAngle[s] / Deg).ToString("0.#"));
        }
        // small hip drops from the rest pose: the plain 40 iterations leave the ankle far from its goal,
        // the closed-form finish lands on it and changes nothing where the iterations had converged
        foreach (double drop in new double[] { 0.05, 0.2, 0.5, 1, 3, 7 })
        {
            p.AnimPos[B.Center] = new V3(0, -drop, 0);
            rig.ExactReach = false; rig.Evaluate(p);
            double plain = V3.Distance(p.WorldPos[ankle], rf.Rest[ankle]);
            V3 plainKnee = p.WorldPos[knee];
            rig.ExactReach = true; rig.Evaluate(p);
            double exact = V3.Distance(p.WorldPos[ankle], rf.Rest[ankle]);
            Check(exact < 1e-9, "drop " + drop + ": ankle exactly on the goal (" + exact.ToString("0.#######") + ")");
            Check(p.KneeAngle[0] <= rig.KneeMax && p.KneeAngle[0] >= rig.KneeMin, "drop " + drop + ": knee inside the hinge range");
            Check(p.WorldPos[knee].z < plainKnee.z + 1e-6, "drop " + drop + ": knee goes forward, never back");
            if (drop == 0.5) Check(plain > 0.2, "drop 0.5: plain iterations are " + plain.ToString("0.###") + " units off (documented weakness)");
            if (drop >= 3) Check(V3.Distance(p.WorldPos[knee], plainKnee) < 0.01, "drop " + drop + ": same pose as the plain iterations");
        }
        p.AnimPos[B.Center] = new V3(0, -5, 0);
        rig.Evaluate(p);
        // symmetric input gives a mirrored pose
        NearV(p.WorldPos[B.S(1, B.Knee)], new V3(-p.WorldPos[knee].x, p.WorldPos[knee].y, p.WorldPos[knee].z), 1e-6, "left and right mirror");
        // out of reach: the leg stretches towards the target and stays as long as it is
        p.AnimPos[B.Center] = V3.Zero;
        p.AnimPos[ik] = new V3(0, -4, 0);
        rig.Evaluate(p);
        V3 far = rf.Rest[ik] + new V3(0, -4, 0);
        double reach = V3.Distance(p.WorldPos[leg], p.WorldPos[ankle]);
        Check(reach <= rf.LegLength + 1e-9 && reach > rf.LegLength - 0.02, "out of reach: leg nearly straight");
        V3 d1 = (p.WorldPos[ankle] - p.WorldPos[leg]).NormalizedOr(V3.Zero), d2 = (far - p.WorldPos[leg]).NormalizedOr(V3.Zero);
        Check(V3.Dot(d1, d2) > 0.9995, "out of reach: leg points at the target");
        // same input twice gives the same output (no state carried between evaluations)
        p.AnimPos[ik] = new V3(0.3, 3, -1);
        rig.Evaluate(p);
        V3 a1 = p.WorldPos[knee];
        p.AnimPos[ik] = new V3(-2, 6, 2); rig.Evaluate(p);
        p.AnimPos[ik] = new V3(0.3, 3, -1); rig.Evaluate(p);
        NearV(p.WorldPos[knee], a1, 1e-12, "evaluation is stateless");
    }

    private static void ToeAndDTests()
    {
        RefBody rf = RefBody.Tda();
        MmdRig rig = rf.ToRig();
        RigPose p = rig.NewPose();
        int ik = B.S(0, B.LegIK), ankle = B.S(0, B.Ankle), toe = B.S(0, B.ToeTip), toeIk = B.S(0, B.ToeIK);
        // turning the foot IK bone pitches the foot: the toe tip follows the toe target
        Q pitch = Q.AxisAngle(new V3(1, 0, 0), 30 * Deg);
        p.AnimRot[ik] = pitch;
        rig.Evaluate(p);
        Check(V3.Distance(p.WorldPos[toe], p.WorldPos[toeIk]) < 0.01, "toe tip reaches the toe target");
        NearV(p.WorldPos[ankle], rf.Rest[ankle], 0.003, "ankle stays");
        // D bones copy the solved leg
        NearQ(p.WorldRot[B.S(0, B.LegD)], p.WorldRot[B.S(0, B.Leg)], 1e-9, "足D = 足");
        NearQ(p.WorldRot[B.S(0, B.KneeD)], p.WorldRot[B.S(0, B.Knee)], 1e-9, "ひざD = ひざ");
        NearQ(p.WorldRot[B.S(0, B.AnkleD)], p.WorldRot[ankle], 1e-9, "足首D = 足首");
        NearV(p.WorldPos[B.S(0, B.AnkleD)], p.WorldPos[ankle], 1e-9, "足首D position");
        // a key on a D bone adds to the copy
        Q extra = Q.AxisAngle(new V3(0, 1, 0), 10 * Deg);
        p.AnimRot[B.S(0, B.LegD)] = extra;
        rig.Evaluate(p);
        NearQ(p.WorldRot[B.S(0, B.LegD)], p.WorldRot[B.S(0, B.WaistCancel)] * extra * p.LocalRot[B.S(0, B.Leg)], 1e-9, "D key times the copied rotation");
        // toe IK off: the ankle keeps its keyed rotation
        p.AnimRot[B.S(0, B.LegD)] = Q.Identity;
        p.ToeIkOn[0] = false;
        rig.Evaluate(p);
        NearQ(p.LocalRot[ankle], Q.Identity, 1e-9, "toe IK off leaves the ankle alone");
    }

    private static void IkSwitchTests()
    {
        // file without IK section, IK bone moving: IK on
        VmdBuilder b = new VmdBuilder();
        b.Bone(NLLegIk, 0, new V3(0, 1, 0), Q.Identity, null);
        b.Bone(NLKnee, 0, V3.Zero, Q.AxisAngle(new V3(1, 0, 0), -20 * Deg), null);
        MotionSampler m = new MotionSampler(VmdFile.Parse(b.Build(false)));
        RigPose p = RefBody.Tda().ToRig().NewPose();
        m.Sample(0, p);
        Check(p.LegIkOn[0] && p.ToeIkOn[0], "moving IK bone: IK on");
        Check(p.LegIkOn[1], "side with no keys at all: IK on");
        // FK-style file: knee keyed, IK bone absent
        VmdBuilder b2 = new VmdBuilder();
        b2.Bone(NLKnee, 0, V3.Zero, Q.AxisAngle(new V3(1, 0, 0), -20 * Deg), null);
        m = new MotionSampler(VmdFile.Parse(b2.Build(false)));
        m.Sample(0, p);
        Check(!p.LegIkOn[0] && !p.ToeIkOn[0], "knee keyed, no IK keys: FK leg");
        NearQ(p.AnimRot[B.S(0, B.Knee)], Q.AxisAngle(new V3(1, 0, 0), -20 * Deg), 1e-6, "knee key is delivered");
        // IK section wins over the guess, and switches in time
        VmdBuilder b3 = new VmdBuilder();
        b3.Bone(NLLegIk, 0, new V3(0, 1, 0), Q.Identity, null);
        b3.Ik(0, true, new byte[][] { NLLegIk, NLToeIk }, new bool[] { false, true });
        b3.Ik(60, true, new byte[][] { NLLegIk, NLToeIk }, new bool[] { true, false });
        m = new MotionSampler(VmdFile.Parse(b3.Build(true)));
        m.Sample(1.0, p);
        Check(!p.LegIkOn[0] && !p.ToeIkOn[0], "IK key off at frame 30 (toe IK needs the leg IK)");
        m.Sample(2.0, p);
        Check(p.LegIkOn[0] && !p.ToeIkOn[0], "IK key on at frame 60, toe off");
        Near(m.LengthSeconds, 0, 1e-9, "length of a one-frame file");
    }

    // A plain T-pose body, roughly Genesis-sized, in MMD axes (left at +X, facing -Z), metres.
    private static BodyRest TPose(double legScale, double armScale, double shoulderHalf)
    {
        BodyRest b = new BodyRest();
        double hipY = 0.10 + 0.82 * legScale;
        b.Pos[VB.Hip] = new V3(0, hipY + 0.10, 0.01);
        b.Pos[VB.Pelvis] = new V3(0, hipY + 0.06, 0.01);
        b.Pos[VB.Abdomen] = new V3(0, hipY + 0.14, 0.0);
        b.Pos[VB.Abdomen2] = new V3(0, hipY + 0.24, -0.01);
        b.Pos[VB.Chest] = new V3(0, hipY + 0.34, -0.01);
        b.Pos[VB.Neck] = new V3(0, hipY + 0.53, 0.0);
        b.Pos[VB.Head] = new V3(0, hipY + 0.61, -0.01);
        for (int s = 0; s < 2; s++)
        {
            double m = s == 0 ? 1 : -1;
            b.Pos[VB.S(s, VB.Collar)] = new V3(m * 0.03, hipY + 0.47, 0.0);
            b.Pos[VB.S(s, VB.Shldr)] = new V3(m * shoulderHalf, hipY + 0.46, 0.0);
            b.Pos[VB.S(s, VB.ForeArm)] = new V3(m * (shoulderHalf + 0.27 * armScale), hipY + 0.46, 0.0);
            b.Pos[VB.S(s, VB.Hand)] = new V3(m * (shoulderHalf + 0.52 * armScale), hipY + 0.46, 0.0);
            double hx = shoulderHalf + 0.52 * armScale;
            for (int f = 0; f < 5; f++)
            {
                double z = -0.03 + f * 0.015;
                for (int seg = 0; seg < 3; seg++)
                    b.Pos[VB.S(s, VB.Thumb1 + f * 3 + seg)] = new V3(m * (hx + 0.07 + seg * 0.03), hipY + 0.46, z);
            }
            b.Pos[VB.S(s, VB.Thigh)] = new V3(m * 0.085, hipY, 0.0);
            b.Pos[VB.S(s, VB.Shin)] = new V3(m * 0.10, hipY - 0.42 * legScale, -0.01);
            b.Pos[VB.S(s, VB.Foot)] = new V3(m * 0.11, 0.08, 0.03);
            b.Pos[VB.S(s, VB.Toe)] = new V3(m * 0.12, 0.02, -0.10);
        }
        return b;
    }

    private static void FitTests()
    {
        RefBody rf = RefBody.Tda();
        BodyRest body = TPose(1.0, 1.0, 0.16);
        FitReport rep = new FitReport();
        MmdRig rig = RigBuilder.Build(body, rf, new FitOptions(), rep);
        Check(rig != null && rep.Problem == null, "fit succeeds");
        Near(rep.MetresPerUnit, rep.LegLength / rf.LegLength, 1e-12, "scale = leg length ratio");
        Check(rep.MetresPerUnit > 0.06 && rep.MetresPerUnit < 0.09, "plausible scale: " + rep.MetresPerUnit.ToString("0.####"));
        // bone lengths survive the A-pose alignment
        for (int s = 0; s < 2; s++)
        {
            Near(V3.Distance(rig.Rest[B.S(s, B.Arm)], rig.Rest[B.S(s, B.Elbow)]), 0.27, 1e-9, "upper arm length side " + s);
            Near(V3.Distance(rig.Rest[B.S(s, B.Elbow)], rig.Rest[B.S(s, B.Wrist)]), 0.25, 1e-9, "forearm length side " + s);
            Near(V3.Distance(rig.Rest[B.S(s, B.Leg)], rig.Rest[B.S(s, B.Knee)]), V3.Distance(body.Pos[VB.S(s, VB.Thigh)], body.Pos[VB.S(s, VB.Shin)]), 1e-9, "thigh length side " + s);
        }
        // the rest arms now point along the reference directions
        V3 want = rf.Dir(B.Arm, B.Elbow);
        V3 got = (rig.Rest[B.S(0, B.Elbow)] - rig.Rest[B.S(0, B.Arm)]).NormalizedOr(V3.Zero);
        NearV(got, want, 1e-9, "left upper arm direction");
        V3 gotR = (rig.Rest[B.S(1, B.Elbow)] - rig.Rest[B.S(1, B.Arm)]).NormalizedOr(V3.Zero);
        NearV(gotR, new V3(-want.x, want.y, want.z), 1e-9, "right upper arm direction is the mirror");
        NearV((rig.Rest[B.S(0, B.Knee)] - rig.Rest[B.S(0, B.Leg)]).NormalizedOr(V3.Zero), rf.Dir(B.Leg, B.Knee), 1e-9, "thigh direction");
        // soles stay on the floor
        Near(rig.Rest[B.S(0, B.ToeEX)].y, body.Pos[VB.S(0, VB.Toe)].y, 1e-9, "toe joint height unchanged");
        // identity pose: every output sits where the aligned body is; unaligned bones are where they were
        RigPose p = rig.NewPose();
        rig.Evaluate(p);
        NearV(p.OutPos[VB.Head], body.Pos[VB.Head] + new V3(0, rep.FloorShift, 0), 1e-6, "head output at rest");
        NearQ(p.OutRot[VB.Head], Q.Identity, 1e-9, "head rotation at rest");
        NearV(p.OutPos[VB.S(0, VB.Foot)], rig.Rest[B.S(0, B.Ankle)], 3e-4, "foot output at rest (IK on)");
        Check(p.OutPos[VB.S(0, VB.Hand)].y < body.Pos[VB.S(0, VB.Hand)].y - 0.2, "A-pose: hands are lower than in the T-pose");
        // undo the A-pose with a key on 腕: the T-pose comes back exactly
        Q a = Q.FromTo(new V3(1, 0, 0), want);            // what the fit applied to the left arm chain
        p.AnimRot[B.S(0, B.Arm)] = a.Conj;
        rig.Evaluate(p);
        NearV(p.OutPos[VB.S(0, VB.ForeArm)], body.Pos[VB.S(0, VB.ForeArm)] + new V3(0, rep.FloorShift, 0), 1e-9, "elbow back in the T-pose");
        NearQ(p.OutRot[VB.S(0, VB.Shldr)], Q.Identity, 1e-9, "upper arm rotation back to its rest");
        p.AnimRot[B.S(0, B.Arm)] = Q.Identity;
        // translation: one VMD unit of centre moves the hip by the scale
        rig.Evaluate(p);
        V3 hip0 = p.OutPos[VB.Hip];
        p.AnimPos[B.Center] = new V3(0, -1, 0);
        rig.Evaluate(p);
        Near(hip0.y - p.OutPos[VB.Hip].y, rep.MetresPerUnit, 1e-9, "hip drop for one unit");
        Check(V3.Distance(p.OutPos[VB.S(0, VB.Foot)], rig.Rest[B.S(0, B.Ankle)]) < 1e-3, "feet stay planted while the hip drops");
        // every bone length of the driven chain is constant in a moving pose (targets never contradict the body)
        p.AnimPos[B.Center] = new V3(0.5, -3, 1);
        p.AnimRot[B.Upper] = Q.AxisAngle(new V3(1, 0, 0), 20 * Deg);
        p.AnimRot[B.S(1, B.Arm)] = Q.AxisAngle(new V3(0, 1, 0), 50 * Deg);
        p.AnimRot[B.S(1, B.Elbow)] = Q.AxisAngle(new V3(0, 1, 0), 70 * Deg);
        p.AnimPos[B.S(0, B.LegIK)] = new V3(1, 2, -3);
        rig.Evaluate(p);
        int[][] pairs =
        {
            new int[] { VB.S(1, VB.Shldr), VB.S(1, VB.ForeArm) }, new int[] { VB.S(1, VB.ForeArm), VB.S(1, VB.Hand) },
            new int[] { VB.S(0, VB.Thigh), VB.S(0, VB.Shin) }, new int[] { VB.S(0, VB.Shin), VB.S(0, VB.Foot) },
            new int[] { VB.S(1, VB.Thigh), VB.S(1, VB.Shin) }, new int[] { VB.Abdomen2, VB.Chest }, new int[] { VB.Hip, VB.Pelvis },
            new int[] { VB.Chest, VB.S(0, VB.Collar) }, new int[] { VB.Neck, VB.Head }
        };
        RigPose q0 = rig.NewPose();
        rig.Evaluate(q0);
        for (int i = 0; i < pairs.Length; i++)
            Near(V3.Distance(p.OutPos[pairs[i][0]], p.OutPos[pairs[i][1]]), V3.Distance(q0.OutPos[pairs[i][0]], q0.OutPos[pairs[i][1]]), 1e-9,
                "length " + VB.Name[pairs[i][0]] + "-" + VB.Name[pairs[i][1]] + " constant");
        // a longer-legged body gets a larger scale, and the report shows relative arm length
        FitReport rep2 = new FitReport();
        RigBuilder.Build(TPose(1.1, 1.0, 0.16), rf, new FitOptions(), rep2);
        Check(rep2.MetresPerUnit > rep.MetresPerUnit * 1.05, "longer legs, larger scale");
        Check(rep2.ArmVsReference < rep.ArmVsReference, "same arms on longer legs are relatively shorter");
        // a broken body is reported, not fitted
        FitReport rep3 = new FitReport();
        MmdRig none = RigBuilder.Build(new BodyRest(), rf, new FitOptions(), rep3);
        Check(none == null && rep3.Problem != null, "empty body is refused");
        // alignment can be switched off: rest arms stay horizontal
        FitOptions off = new FitOptions(); off.AlignArms = false; off.AlignLegs = false;
        MmdRig flat = RigBuilder.Build(body, rf, off, new FitReport());
        Near(flat.Rest[B.S(0, B.Elbow)].y, flat.Rest[B.S(0, B.Arm)].y, 1e-9, "no alignment keeps the T-pose");
    }

    private static void ClockTests()
    {
        DanceClock c = new DanceClock();
        c.Length = 10; c.Playing = true;
        for (int i = 0; i < 90; i++) c.Step(1 / 90.0, double.NaN);
        Near(c.Time, 1.0, 1e-9, "free run");
        // audio slightly ahead: pulled smoothly, never a jump
        c.Jumped = false;
        double audio = c.Time + 0.02;
        double prev = c.Time; double maxStep = 0;
        for (int i = 0; i < 180; i++)
        {
            audio += 1 / 90.0;
            c.Step(1 / 90.0, audio);
            maxStep = Math.Max(maxStep, c.Time - prev); prev = c.Time;
        }
        Check(!c.Jumped, "small offset does not jump");
        Check(Math.Abs(c.Time - audio) < 0.002, "converges to the audio: " + (c.Time - audio).ToString("0.#####"));
        Check(maxStep < 1.1 / 90.0 + 0.0005, "step stays close to the frame time: " + maxStep.ToString("0.#####"));
        // coarse audio clock (reported in 23 ms blocks): the dance clock stays smooth
        c.Length = 100;
        prev = c.Time; double worst = 0;
        double trueAudio = c.Time;
        for (int i = 0; i < 900; i++)
        {
            trueAudio += 1 / 90.0;
            double reported = Math.Floor(trueAudio / 0.0232) * 0.0232;
            c.Step(1 / 90.0, reported);
            worst = Math.Max(worst, Math.Abs((c.Time - prev) - 1 / 90.0)); prev = c.Time;
        }
        // (0.6 ms with the 0.5 s pull of the first build; the pull is 0.3 s now so that a change of
        // the game's speed is followed without a jump, which costs a little smoothness here)
        Check(worst < 0.001, "blocky audio time moves the step by under 1 ms: " + worst.ToString("0.######"));
        Check(!c.Jumped, "blocky audio time never snaps");
        Check(Math.Abs(c.Rate - 1) < 0.01, "rate stays at 1 when the game keeps up: " + c.Rate.ToString("0.####"));
        // big offset: snap and flag
        c.Step(1 / 90.0, c.Time + 3);
        Check(c.Jumped, "large offset snaps and reports a jump");
        // A heavy scene: VaM lets a frame count for at most two physics steps, so below 45 fps at
        // 90 Hz the game runs slower than real time while the music does not. The dance has to
        // stay with the music without jumping.
        SlowGame(36, 90, 2, 0, 0.012, "36 fps at 90 Hz");
        SlowGame(30, 90, 2, 1, 0.012, "30 fps at 90 Hz");
        SlowGame(30, 72, 2, 0, 0.012, "30 fps at 72 Hz");
        SlowGame(25, 60, 2, 0, 0.012, "25 fps at 60 Hz");
        // far slower than real time from the first frame: two corrections, not seconds of them
        SlowGame(25, 90, 2, 2, 0.012, "25 fps at 90 Hz");
        SlowGame(20, 90, 2, 2, 0.012, "20 fps at 90 Hz");
        SlowGame(18, 90, 2, 3, 0.012, "18 fps at 90 Hz");
        // the frame rate recovers (20 -> 90 fps): the dance must not hop backwards for seconds
        {
            DanceClock k = new DanceClock(); k.Length = 1000; k.Playing = true;
            double real = 0, acc = 0; int jumps = 0; double late = 0;
            while (real < 40)
            {
                double frame = real < 15 ? 1 / 20.0 : 1 / 90.0;
                real += frame;
                acc += Math.Min(frame, 2 / 90.0);
                while (acc >= 1 / 90.0 - 1e-12)
                {
                    acc -= 1 / 90.0;
                    k.Jumped = false;
                    k.Step(1 / 90.0, Math.Floor(real / 0.0232) * 0.0232);
                    if (k.Jumped && real > 15) jumps++;
                }
                if (real > 25) late = Math.Max(late, Math.Abs(real - 0.0116 - k.Time));
            }
            Check(jumps <= 2, "frame rate recovers 20 -> 90 fps: jumps " + jumps);
            Check(late < 0.02, "frame rate recovers: in step again afterwards: " + late.ToString("0.####"));
        }
        // two real hitches a second apart (the music ran on while the game stood still) teach the
        // rate something wrong; it must find its way back without further jumps
        {
            DanceClock k = new DanceClock(); k.Length = 1000; k.Playing = true;
            double heard = 0; int jumps = 0;
            for (int i = 0; i < 90 * 20; i++)
            {
                heard += 1 / 90.0;
                if (i == 90 * 5 || i == 90 * 6) heard += 0.15;
                k.Jumped = false;
                k.Step(1 / 90.0, heard);
                if (k.Jumped) jumps++;
            }
            Check(jumps == 2, "two hitches: one jump each and no more: " + jumps);
            Check(Math.Abs(k.Rate - 1) < 0.01 && Math.Abs(k.Time - heard) < 0.003, "two hitches: rate and time settle again: " + k.Rate.ToString("0.###"));
        }
        // the frame rate drops in the middle of a song: at most one small correction, then in step again
        {
            DanceClock k = new DanceClock(); k.Length = 1000; k.Playing = true;
            double real = 0, acc = 0; int jumps = 0; double late = 0;
            for (int f = 0; f < 60 * 40; f++)
            {
                double frame = f < 60 * 10 ? 1 / 90.0 : 1 / 34.0;
                real += frame;
                acc += Math.Min(frame, 2 / 90.0);
                while (acc >= 1 / 90.0 - 1e-12)
                {
                    acc -= 1 / 90.0;
                    k.Jumped = false;
                    k.Step(1 / 90.0, Math.Floor(real / 0.0232) * 0.0232);
                    if (k.Jumped) jumps++;
                }
                if (real > 30) late = Math.Max(late, Math.Abs(real - 0.0116 - k.Time));
            }
            Check(jumps <= 1, "frame rate drop 90 -> 34 fps: jumps " + jumps);
            Check(late < 0.02, "frame rate drop: in step again afterwards: " + late.ToString("0.####"));
        }
        // no music: the learned rate fades and the dance runs on the game's own time
        {
            DanceClock k = new DanceClock(); k.Length = 1000; k.Playing = true; k.Rate = 1.4;
            for (int i = 0; i < 90 * 20; i++) k.Step(1 / 90.0, double.NaN);
            Check(Math.Abs(k.Rate - 1) < 0.001, "rate returns to 1 without music: " + k.Rate.ToString("0.####"));
        }
        // end of the dance
        c = new DanceClock(); c.Length = 1; c.Playing = true;
        for (int i = 0; i < 100; i++) c.Step(1 / 90.0, double.NaN);
        Check(!c.Playing && c.Ended && c.Time == 1, "stops at the end");
        // loop
        c = new DanceClock(); c.Length = 1; c.Playing = true; c.Loop = true;
        for (int i = 0; i < 100; i++) c.Step(1 / 90.0, double.NaN);
        Check(c.Playing && c.Time < 0.2 && c.Jumped, "loops to the start and reports the jump");
        // A-B loop
        c = new DanceClock(); c.Length = 10; c.Playing = true; c.LoopStart = 2; c.LoopEnd = 3; c.Seek(2.9); c.Jumped = false;
        for (int i = 0; i < 20; i++) c.Step(1 / 90.0, double.NaN);
        Check(c.Time >= 2 && c.Time < 2.2 && c.Jumped, "A-B loop");
        // speed and pause
        c = new DanceClock(); c.Length = 10; c.Playing = true; c.Speed = 0.5;
        for (int i = 0; i < 90; i++) c.Step(1 / 90.0, double.NaN);
        Near(c.Time, 0.5, 1e-9, "half speed");
        double held = c.Time; c.Playing = false; c.Step(1, double.NaN); Near(c.Time, held, 0, "paused");
        c.Seek(99); Near(c.Time, 10, 0, "seek clamps to the length");
    }

    // Plays 60 s of music in real time while the game gets fps frames a second, physics steps of
    // 1/hz and at most cap steps per frame. After the first seconds the dance must be within tol
    // of the music (the music position is reported in 23 ms blocks, so half a block late on
    // average) with no more than allowedJumps corrections on the way.
    private static void SlowGame(double fps, double hz, int cap, int allowedJumps, double tol, string name)
    {
        DanceClock k = new DanceClock(); k.Length = 1000; k.Playing = true;
        double real = 0, acc = 0, worst = 0; int jumps = 0;
        double frame = 1 / fps, step = 1 / hz;
        while (real < 60)
        {
            real += frame;
            acc += Math.Min(frame, cap * step);
            while (acc >= step - 1e-12)
            {
                acc -= step;
                k.Jumped = false;
                k.Step(step, Math.Floor(real / 0.0232) * 0.0232);
                if (k.Jumped) jumps++;
            }
            if (real > 10) worst = Math.Max(worst, Math.Abs(real - 0.0116 - k.Time));
        }
        Check(jumps <= allowedJumps, name + ": jumps " + jumps);
        Check(worst < tol + step, name + ": stays with the music: " + worst.ToString("0.####") + " s, rate " + k.Rate.ToString("0.###"));
    }

    private static void WavTests()
    {
        // 16-bit stereo, 4 frames, with a LIST chunk before the data
        List<byte> d = new List<byte>();
        d.AddRange(Encoding.ASCII.GetBytes("RIFF")); d.AddRange(BitConverter.GetBytes(0)); d.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        d.AddRange(Encoding.ASCII.GetBytes("fmt ")); d.AddRange(BitConverter.GetBytes(16));
        d.AddRange(BitConverter.GetBytes((short)1)); d.AddRange(BitConverter.GetBytes((short)2));
        d.AddRange(BitConverter.GetBytes(44100)); d.AddRange(BitConverter.GetBytes(44100 * 4));
        d.AddRange(BitConverter.GetBytes((short)4)); d.AddRange(BitConverter.GetBytes((short)16));
        d.AddRange(Encoding.ASCII.GetBytes("LIST")); d.AddRange(BitConverter.GetBytes(3)); d.Add(1); d.Add(2); d.Add(3); d.Add(0);   // odd size + pad
        d.AddRange(Encoding.ASCII.GetBytes("data")); d.AddRange(BitConverter.GetBytes(16));
        short[] smp = { 0, 16384, -16384, 32767, -32768, 1, 2, 3 };
        for (int i = 0; i < smp.Length; i++) d.AddRange(BitConverter.GetBytes(smp[i]));
        WavFile w = WavFile.Parse(d.ToArray());
        Check(w.Channels == 2 && w.SampleRate == 44100 && w.Frames == 4 && !w.IsFloat, "header");
        float[] buf = new float[8];
        Check(w.Decode(0, 4, buf) == 4, "decode count");
        Near(buf[1], 0.5, 1e-6, "sample scale"); Near(buf[2], -0.5, 1e-6, "negative sample"); Near(buf[4], -1, 1e-6, "full scale");
        Check(w.Decode(3, 10, buf) == 1, "decode clamps at the end");
        Near(buf[0], 2 / 32768.0, 1e-9, "offset decode");
        // 24-bit mono
        List<byte> e = new List<byte>();
        e.AddRange(Encoding.ASCII.GetBytes("RIFF")); e.AddRange(BitConverter.GetBytes(0)); e.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        e.AddRange(Encoding.ASCII.GetBytes("fmt ")); e.AddRange(BitConverter.GetBytes(16));
        e.AddRange(BitConverter.GetBytes((short)1)); e.AddRange(BitConverter.GetBytes((short)1));
        e.AddRange(BitConverter.GetBytes(48000)); e.AddRange(BitConverter.GetBytes(48000 * 3));
        e.AddRange(BitConverter.GetBytes((short)3)); e.AddRange(BitConverter.GetBytes((short)24));
        e.AddRange(Encoding.ASCII.GetBytes("data")); e.AddRange(BitConverter.GetBytes(6));
        e.Add(0x00); e.Add(0x00); e.Add(0x40);      // +0.5
        e.Add(0x00); e.Add(0x00); e.Add(0xC0);      // -0.5
        for (int i = 0; i < 40; i++) e.Add(0);      // padding so the file is long enough to look real
        WavFile w24 = WavFile.Parse(e.ToArray());
        float[] b24 = new float[2];
        w24.Decode(0, 2, b24);
        Near(b24[0], 0.5, 1e-6, "24-bit positive"); Near(b24[1], -0.5, 1e-6, "24-bit negative");
        bool threw = false;
        try { WavFile.Parse(new byte[64]); } catch (FormatException2) { threw = true; }
        Check(threw, "not a wav is rejected");
    }
}
