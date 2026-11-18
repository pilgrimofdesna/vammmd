using System;

namespace VamMmd.Core
{
    // The host body's bones the player reads and drives (VaM's Genesis skeleton).
    public static class VB
    {
        public const int Hip = 0, Pelvis = 1, Abdomen = 2, Abdomen2 = 3, Chest = 4, Neck = 5, Head = 6;
        public const int SideBase = 7, SideCount = 23;

        public const int Collar = 0, Shldr = 1, ForeArm = 2, Hand = 3;
        public const int Thumb1 = 4, Thumb2 = 5, Thumb3 = 6, Index1 = 7, Index2 = 8, Index3 = 9;
        public const int Mid1 = 10, Mid2 = 11, Mid3 = 12, Ring1 = 13, Ring2 = 14, Ring3 = 15, Pinky1 = 16, Pinky2 = 17, Pinky3 = 18;
        public const int Thigh = 19, Shin = 20, Foot = 21, Toe = 22;

        public const int Count = SideBase + 2 * SideCount;

        public static int S(int side, int offset) { return SideBase + side * SideCount + offset; }

        private static readonly string[] CoreNames = { "hip", "pelvis", "abdomen", "abdomen2", "chest", "neck", "head" };
        private static readonly int[] CoreParents = { -1, Hip, Hip, Abdomen, Abdomen2, Chest, Neck };

        private static readonly string[] SideNames =
        {
            "Collar", "Shldr", "ForeArm", "Hand",
            "Thumb1", "Thumb2", "Thumb3", "Index1", "Index2", "Index3",
            "Mid1", "Mid2", "Mid3", "Ring1", "Ring2", "Ring3", "Pinky1", "Pinky2", "Pinky3",
            "Thigh", "Shin", "Foot", "Toe"
        };

        // parent as side offset; negative = core bone (-1 - index). Finger roots hang off the hand
        // (VaM's carpal bones in between are not driven).
        private static readonly int[] SideParents =
        {
            -1 - Chest, Collar, Shldr, ForeArm,
            Hand, Thumb1, Thumb2, Hand, Index1, Index2,
            Hand, Mid1, Mid2, Hand, Ring1, Ring2, Hand, Pinky1, Pinky2,
            -1 - Pelvis, Thigh, Shin, Foot
        };

        public static readonly string[] Name = new string[Count];   // DAZ bone names: hip, lShldr, rThumb1 ...
        public static readonly int[] Parent = new int[Count];

        static VB()
        {
            for (int i = 0; i < SideBase; i++) { Name[i] = CoreNames[i]; Parent[i] = CoreParents[i]; }
            for (int s = 0; s < 2; s++)
                for (int o = 0; o < SideCount; o++)
                {
                    int i = S(s, o);
                    Name[i] = (s == 0 ? "l" : "r") + SideNames[o];
                    int p = SideParents[o];
                    Parent[i] = p < 0 ? -1 - p : S(s, p);
                }
        }

        public static bool IsFinger(int index)
        {
            if (index < SideBase) return false;
            int o = (index - SideBase) % SideCount;
            return o >= Thumb1 && o <= Pinky3;
        }
    }

    // The host body in its rest pose, in MMD axes (Y up, facing -Z, left hand at +X), in metres,
    // relative to the point on the floor the dance is centred on.
    public sealed class BodyRest
    {
        public readonly V3[] Pos = new V3[VB.Count];
        public readonly Q[] Rot = new Q[VB.Count];

        public BodyRest()
        {
            for (int i = 0; i < VB.Count; i++) Rot[i] = Q.Identity;
        }
    }

    // Proportions of the model the dances were made for. Positions are in MMD units, left side,
    // with the floor at y = 0. Only directions and a few lengths are used for playback; the full
    // table is kept for contact detection.
    public sealed class RefBody
    {
        public string Label;
        public readonly V3[] Rest = new V3[B.Count];
        // finger tips, left side: thumb, index, middle, ring, little
        public readonly V3[] Tip = new V3[5];

        public V3 Dir(int fromSideOffset, int toSideOffset)
        {
            return (Rest[B.S(0, toSideOffset)] - Rest[B.S(0, fromSideOffset)]).NormalizedOr(new V3(1, 0, 0));
        }

        public double LegLength
        {
            get
            {
                return V3.Distance(Rest[B.S(0, B.Leg)], Rest[B.S(0, B.Knee)]) + V3.Distance(Rest[B.S(0, B.Knee)], Rest[B.S(0, B.Ankle)]);
            }
        }

        public double ArmLength
        {
            get
            {
                return V3.Distance(Rest[B.S(0, B.Arm)], Rest[B.S(0, B.Elbow)]) + V3.Distance(Rest[B.S(0, B.Elbow)], Rest[B.S(0, B.Wrist)]);
            }
        }

        public double ShoulderWidth { get { return 2 * Rest[B.S(0, B.Arm)].x; } }

        // Joint positions measured from a Tda-style model the project owner uses
        // ("Megurine Luka [La La Love Me] White ver", 419 bones). Its floor sits at y = 0.8,
        // which is subtracted here. Only standard bones, left side; the right side is the mirror.
        public static RefBody Tda()
        {
            RefBody r = new RefBody();
            r.Label = "Tda-style reference";
            const double F = 0.8;
            Set(r, B.Root, 0, F, 0);
            Set(r, B.Center, 0, 8.42999, 0);
            Set(r, B.Groove, 0, 8.62999, 0);
            Set(r, B.Waist, 0, 13.09314, 0.26391);
            Set(r, B.Lower, 0, 13.95295, -0.5301);
            Set(r, B.Upper, 0, 14.01361, -0.53011);
            Set(r, B.Upper2, 0, 15.1417, -0.48047);
            Set(r, B.Neck, 0, 17.66867, -0.1138);
            Set(r, B.Head, 0, 18.55629, -0.12254);
            SetL(r, B.ShoulderP, 0.24584, 17.37906, -0.15265);
            SetL(r, B.Shoulder, 0.24584, 17.37906, -0.15265);
            SetL(r, B.ShoulderC, 1.1346, 17.15153, -0.14003);
            SetL(r, B.Arm, 1.1346, 17.15153, -0.14003);
            SetL(r, B.ArmTwist, 2.48721, 16.11655, -0.09297);
            SetL(r, B.Elbow, 3.38896, 15.42655, -0.10582);
            SetL(r, B.WristTwist, 4.5813, 14.52102, -0.13301);
            SetL(r, B.Wrist, 5.37619, 13.91733, -0.15114);
            SetL(r, B.Thumb0, 5.43554, 13.69636, -0.27641);
            SetL(r, B.Thumb1, 5.67953, 13.34175, -0.59516);
            SetL(r, B.Thumb2, 5.83539, 13.15833, -0.66557);
            SetL(r, B.Index1, 6.09505, 13.32485, -0.40797);
            SetL(r, B.Index2, 6.40499, 13.06271, -0.40715);
            SetL(r, B.Index3, 6.55664, 12.93724, -0.41264);
            SetL(r, B.Middle1, 6.123, 13.35982, -0.20676);
            SetL(r, B.Middle2, 6.46322, 13.05829, -0.19693);
            SetL(r, B.Middle3, 6.64957, 12.89676, -0.19918);
            SetL(r, B.Ring1, 6.06928, 13.37712, -0.00278);
            SetL(r, B.Ring2, 6.39149, 13.09568, -0.01676);
            SetL(r, B.Ring3, 6.56862, 12.94536, -0.02235);
            SetL(r, B.Little1, 5.98334, 13.35926, 0.16806);
            SetL(r, B.Little2, 6.25286, 13.13597, 0.15167);
            SetL(r, B.Little3, 6.38364, 13.04532, 0.14462);
            SetL(r, B.WaistCancel, 0.93055, 12.51994, -0.34958);
            SetL(r, B.Leg, 0.93055, 12.51994, -0.34958);
            SetL(r, B.Knee, 1.00896, 7.32956, -0.0181);
            SetL(r, B.Ankle, 1.05119, 1.58044, 0.70435);
            SetL(r, B.ToeTip, 1.05119, 0.79644, -1.13087);
            SetL(r, B.LegIKParent, 1.05119, 0.8, 0.70435);
            SetL(r, B.LegIK, 1.05119, 1.58044, 0.70435);
            SetL(r, B.ToeIK, 1.05119, 0.8, -1.12255);
            SetL(r, B.LegD, 0.93055, 12.51994, -0.34958);
            SetL(r, B.KneeD, 1.00896, 7.32956, -0.0181);
            SetL(r, B.AnkleD, 1.05119, 1.58044, 0.70435);
            SetL(r, B.ToeEX, 1.05119, 1.02214, -0.74079);
            r.Tip[0] = new V3(6.01921, 12.96589, -0.75267);    // 親指先
            r.Tip[1] = new V3(6.71268, 12.80966, -0.40548);    // 人指先
            r.Tip[2] = new V3(6.81966, 12.77001, -0.21693);    // 中指先
            r.Tip[3] = new V3(6.72372, 12.82309, -0.04274);    // 薬指先
            r.Tip[4] = new V3(6.51311, 12.92394, 0.12588);     // 小指先
            for (int i = 0; i < 5; i++) r.Tip[i] = new V3(r.Tip[i].x - CX, r.Tip[i].y - F, r.Tip[i].z);
            for (int i = 0; i < B.Count; i++) r.Rest[i] = new V3(r.Rest[i].x, r.Rest[i].y - F, r.Rest[i].z);
            return r;
        }

        private static void Set(RefBody r, int bone, double x, double y, double z) { r.Rest[bone] = new V3(x, y, z); }

        private const double CX = 0.00274;     // the model's centre line sits this far off x = 0

        private static void SetL(RefBody r, int offset, double x, double y, double z)
        {
            r.Rest[B.S(0, offset)] = new V3(x - CX, y, z);
            r.Rest[B.S(1, offset)] = new V3(-(x - CX), y, z);
        }

        // The reference body itself as a rig (VMD units), for measuring what a dance was meant to do.
        public MmdRig ToRig()
        {
            MmdRig rig = new MmdRig();
            rig.SetRest(Rest);
            rig.UnitScale = 1;
            return rig;
        }
    }

    public sealed class FitOptions
    {
        public bool AlignArms = true;       // put the arms into the reference A-pose before applying rotations
        public bool AlignFingers = true;
        public bool AlignLegs = true;
        public double PelvisTravel = 1;
    }

    // What the fit measured; shown to the user as numbers.
    public sealed class FitReport
    {
        public double MetresPerUnit;        // translation scale: body leg length / reference leg length
        public double LegLength;            // metres, thigh + shin
        public double ArmLength;            // metres, upper arm + forearm
        public double ShoulderWidth;        // metres between the shoulder joints
        public double ArmVsReference;       // (arm / leg) of the body over (arm / leg) of the reference
        public double ShoulderVsReference;  // same for shoulder width
        public double FloorShift;           // metres the rest skeleton was raised to keep the soles on the floor
        public string Problem;              // null when the body could be fitted
    }

    public static class RigBuilder
    {
        // Which host bone each output follows: position from one MMD bone, rotation from another
        // (twist bones change the rotation only).
        private static void OutputBones(int vb, out int posBone, out int rotBone)
        {
            if (vb < VB.SideBase)
            {
                switch (vb)
                {
                    case VB.Hip: case VB.Pelvis: posBone = B.Lower; break;
                    case VB.Abdomen: posBone = B.Upper; break;
                    case VB.Abdomen2: case VB.Chest: posBone = B.Upper2; break;
                    case VB.Neck: posBone = B.Neck; break;
                    default: posBone = B.Head; break;
                }
                rotBone = posBone;
                return;
            }
            int s = (vb - VB.SideBase) / VB.SideCount, o = (vb - VB.SideBase) % VB.SideCount;
            int po, ro;
            switch (o)
            {
                case VB.Collar: po = B.Shoulder; ro = B.Shoulder; break;
                case VB.Shldr: po = B.Arm; ro = B.ArmTwist; break;
                case VB.ForeArm: po = B.Elbow; ro = B.WristTwist; break;
                case VB.Hand: po = B.Wrist; ro = B.Wrist; break;
                case VB.Thigh: po = B.LegD; ro = B.LegD; break;
                case VB.Shin: po = B.KneeD; ro = B.KneeD; break;
                case VB.Foot: po = B.AnkleD; ro = B.AnkleD; break;
                case VB.Toe: po = B.ToeEX; ro = B.ToeEX; break;
                default: po = B.Thumb0 + (o - VB.Thumb1); ro = po; break;     // fingers map one to one
            }
            posBone = B.S(s, po);
            rotBone = B.S(s, ro);
        }

        // Reference direction a host bone should have in the MMD rest pose, or false when the bone
        // keeps its own rest direction. child is the host bone that marks the direction.
        private static bool AlignTarget(int o, RefBody rf, FitOptions opt, out int childOffset, out V3 dirLeft, out bool fromParentSegment)
        {
            childOffset = -1; dirLeft = V3.Zero; fromParentSegment = false;
            if (o == VB.Shldr && opt.AlignArms) { childOffset = VB.ForeArm; dirLeft = rf.Dir(B.Arm, B.Elbow); return true; }
            if (o == VB.ForeArm && opt.AlignArms) { childOffset = VB.Hand; dirLeft = rf.Dir(B.Elbow, B.Wrist); return true; }
            if (o == VB.Hand && opt.AlignArms) { childOffset = VB.Mid1; dirLeft = rf.Dir(B.Wrist, B.Middle1); return true; }
            if (o == VB.Thigh && opt.AlignLegs) { childOffset = VB.Shin; dirLeft = rf.Dir(B.Leg, B.Knee); return true; }
            if (o == VB.Shin && opt.AlignLegs) { childOffset = VB.Foot; dirLeft = rf.Dir(B.Knee, B.Ankle); return true; }
            if (o >= VB.Thumb1 && o <= VB.Pinky3 && opt.AlignFingers && opt.AlignArms)
            {
                int f = (o - VB.Thumb1) / 3, seg = (o - VB.Thumb1) % 3;
                int b0 = B.Thumb0 + f * 3 + seg;
                if (seg < 2) { childOffset = o + 1; dirLeft = rf.Dir(b0, b0 + 1); return true; }
                // Last segment: the host has no tip joint to measure, so give it the reference's
                // bend relative to the segment before it.
                dirLeft = (rf.Tip[f] - rf.Rest[B.S(0, b0)]).NormalizedOr(rf.Dir(b0 - 1, b0));
                fromParentSegment = true;
                return true;
            }
            return false;
        }

        public static MmdRig Build(BodyRest body, RefBody rf, FitOptions opt, FitReport rep)
        {
            if (opt == null) opt = new FitOptions();
            if (rep == null) rep = new FitReport();
            MmdRig rig = new MmdRig();

            // 1. The body in the MMD rest pose: same bone lengths, limbs turned to the reference directions.
            V3[] pa = new V3[VB.Count];
            Q[] ra = new Q[VB.Count];
            Q[] c = new Q[VB.Count];
            for (int k = 0; k < VB.Count; k++)
            {
                int par = VB.Parent[k];
                Q cp = par < 0 ? Q.Identity : c[par];
                pa[k] = par < 0 ? body.Pos[k] : pa[par] + cp.Rotate(body.Pos[k] - body.Pos[par]);
                c[k] = cp;
                if (k >= VB.SideBase)
                {
                    int s = (k - VB.SideBase) / VB.SideCount, o = (k - VB.SideBase) % VB.SideCount;
                    int child; V3 dir; bool fromParent;
                    if (AlignTarget(o, rf, opt, out child, out dir, out fromParent))
                    {
                        if (s == 1) dir = new V3(-dir.x, dir.y, dir.z);
                        V3 cur;
                        if (fromParent)
                        {
                            // Assume the last segment continues the one before it.
                            cur = cp.Rotate(body.Pos[k] - body.Pos[par]);
                        }
                        else cur = cp.Rotate(body.Pos[VB.S(s, child)] - body.Pos[k]);
                        if (cur.SqrLength > 1e-12) c[k] = (Q.FromTo(cur, dir) * cp).Normalized;
                    }
                }
                ra[k] = (c[k] * body.Rot[k]).Normalized;
            }

            // 2. Keep the soles on the floor: turning the legs changes how far down they reach.
            double shift = 0;
            for (int s = 0; s < 2; s++) shift += body.Pos[VB.S(s, VB.Toe)].y - pa[VB.S(s, VB.Toe)].y;
            shift *= 0.5;
            for (int k = 0; k < VB.Count; k++) pa[k] = new V3(pa[k].x, pa[k].y + shift, pa[k].z);
            rep.FloorShift = shift;

            // 3. Measurements.
            double leg = 0, arm = 0;
            for (int s = 0; s < 2; s++)
            {
                leg += V3.Distance(pa[VB.S(s, VB.Thigh)], pa[VB.S(s, VB.Shin)]) + V3.Distance(pa[VB.S(s, VB.Shin)], pa[VB.S(s, VB.Foot)]);
                arm += V3.Distance(pa[VB.S(s, VB.Shldr)], pa[VB.S(s, VB.ForeArm)]) + V3.Distance(pa[VB.S(s, VB.ForeArm)], pa[VB.S(s, VB.Hand)]);
            }
            leg *= 0.5; arm *= 0.5;
            double shoulder = V3.Distance(pa[VB.S(0, VB.Shldr)], pa[VB.S(1, VB.Shldr)]);
            rep.LegLength = leg; rep.ArmLength = arm; rep.ShoulderWidth = shoulder;
            if (!(leg > 0.05) || !(arm > 0.05) || double.IsNaN(leg + arm + shoulder))
            {
                rep.Problem = "could not measure the body (leg " + leg.ToString("0.###") + " m, arm " + arm.ToString("0.###") + " m)";
                return null;
            }
            double scale = leg / rf.LegLength;
            rep.MetresPerUnit = scale;
            rep.ArmVsReference = (arm / leg) / (rf.ArmLength / rf.LegLength);
            rep.ShoulderVsReference = (shoulder / leg) / (rf.ShoulderWidth / rf.LegLength);

            // 4. MMD bones placed on the body's own joints.
            V3[] rest = new V3[B.Count];
            V3 legRoot = (pa[VB.S(0, VB.Thigh)] + pa[VB.S(1, VB.Thigh)]) * 0.5;
            V3 refLegRoot = new V3(0, rf.Rest[B.S(0, B.Leg)].y, rf.Rest[B.S(0, B.Leg)].z);
            double k2 = legRoot.y / refLegRoot.y;        // height scale for the pivots that have no joint in the body
            if (!(k2 > 0)) k2 = scale;
            rest[B.Root] = V3.Zero;
            rest[B.Center] = new V3(legRoot.x, rf.Rest[B.Center].y * k2, legRoot.z + (rf.Rest[B.Center].z - refLegRoot.z) * k2);
            rest[B.Groove] = new V3(legRoot.x, rf.Rest[B.Groove].y * k2, legRoot.z + (rf.Rest[B.Groove].z - refLegRoot.z) * k2);
            rest[B.Waist] = legRoot + (rf.Rest[B.Waist] - refLegRoot) * k2;
            rest[B.Lower] = pa[VB.Abdomen];
            rest[B.Upper] = pa[VB.Abdomen];
            rest[B.Upper2] = pa[VB.Abdomen2];
            rest[B.Neck] = pa[VB.Neck];
            rest[B.Head] = pa[VB.Head];
            for (int s = 0; s < 2; s++)
            {
                double m = s == 0 ? 1 : -1;
                V3 collar = pa[VB.S(s, VB.Collar)], sh = pa[VB.S(s, VB.Shldr)], el = pa[VB.S(s, VB.ForeArm)], hand = pa[VB.S(s, VB.Hand)];
                rest[B.S(s, B.ShoulderP)] = collar;
                rest[B.S(s, B.Shoulder)] = collar;
                rest[B.S(s, B.ShoulderC)] = sh;
                rest[B.S(s, B.Arm)] = sh;
                rest[B.S(s, B.ArmTwist)] = (sh + el) * 0.5;
                rest[B.S(s, B.Elbow)] = el;
                rest[B.S(s, B.WristTwist)] = (el + hand) * 0.5;
                rest[B.S(s, B.Wrist)] = hand;
                for (int f = 0; f < 15; f++) rest[B.S(s, B.Thumb0 + f)] = pa[VB.S(s, VB.Thumb1 + f)];
                V3 thigh = pa[VB.S(s, VB.Thigh)], shin = pa[VB.S(s, VB.Shin)], foot = pa[VB.S(s, VB.Foot)], toe = pa[VB.S(s, VB.Toe)];
                rest[B.S(s, B.WaistCancel)] = thigh;
                rest[B.S(s, B.Leg)] = thigh;
                rest[B.S(s, B.Knee)] = shin;
                rest[B.S(s, B.Ankle)] = foot;
                V3 refToe = rf.Rest[B.S(0, B.ToeTip)] - rf.Rest[B.S(0, B.Ankle)];
                V3 tip = foot + new V3(m * refToe.x, refToe.y, refToe.z) * scale;
                rest[B.S(s, B.ToeTip)] = tip;
                rest[B.S(s, B.LegIKParent)] = new V3(foot.x, 0, foot.z);
                rest[B.S(s, B.LegIK)] = foot;
                rest[B.S(s, B.ToeIK)] = tip;
                rest[B.S(s, B.LegD)] = thigh;
                rest[B.S(s, B.KneeD)] = shin;
                rest[B.S(s, B.AnkleD)] = foot;
                rest[B.S(s, B.ToeEX)] = toe;
            }
            rig.SetRest(rest);
            rig.UnitScale = scale;
            rig.PelvisTravel = opt.PelvisTravel;

            // 5. One output per host bone.
            int[] posBone = new int[VB.Count], rotBone = new int[VB.Count];
            for (int k = 0; k < VB.Count; k++) OutputBones(k, out posBone[k], out rotBone[k]);
            rig.SetOutputs(posBone, rotBone, pa, ra);
            return rig;
        }
    }
}
