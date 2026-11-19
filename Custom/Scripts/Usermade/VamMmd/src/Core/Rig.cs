using System;

namespace VamMmd.Core
{
    // The fixed MMD bone set the player understands: the standard and semi-standard bones that
    // dance motions key. Index order is evaluation order (parents first).
    public static class B
    {
        public const int Root = 0, Center = 1, Groove = 2, Waist = 3, Lower = 4, Upper = 5, Upper2 = 6, Neck = 7, Head = 8;
        public const int SideBase = 9, SideCount = 35;

        // offsets inside one side
        public const int ShoulderP = 0, Shoulder = 1, ShoulderC = 2, Arm = 3, ArmTwist = 4, Elbow = 5, WristTwist = 6, Wrist = 7;
        public const int Thumb0 = 8, Thumb1 = 9, Thumb2 = 10, Index1 = 11, Index2 = 12, Index3 = 13;
        public const int Middle1 = 14, Middle2 = 15, Middle3 = 16, Ring1 = 17, Ring2 = 18, Ring3 = 19, Little1 = 20, Little2 = 21, Little3 = 22;
        public const int WaistCancel = 23, Leg = 24, Knee = 25, Ankle = 26, ToeTip = 27;
        public const int LegIKParent = 28, LegIK = 29, ToeIK = 30;
        public const int LegD = 31, KneeD = 32, AnkleD = 33, ToeEX = 34;
        public const int FirstD = 31;

        public const int Count = SideBase + 2 * SideCount;

        // side: 0 = left, 1 = right
        public static int S(int side, int offset) { return SideBase + side * SideCount + offset; }

        private static readonly string[] CoreNames = { "全ての親", "センター", "グルーブ", "腰", "下半身", "上半身", "上半身2", "首", "頭" };
        private static readonly int[] CoreParents = { -1, Root, Center, Groove, Waist, Waist, Upper, Upper2, Neck };

        private static readonly string[] SideNames =
        {
            "肩P", "肩", "肩C", "腕", "腕捩", "ひじ", "手捩", "手首",
            "親指0", "親指1", "親指2", "人指1", "人指2", "人指3",
            "中指1", "中指2", "中指3", "薬指1", "薬指2", "薬指3", "小指1", "小指2", "小指3",
            "腰キャンセル", "足", "ひざ", "足首", "つま先",
            "足IK親", "足IK", "つま先IK",
            "足D", "ひざD", "足首D", "足先EX"
        };

        // parent as a side offset; negative values are core bones (-1 - coreIndex)
        private static readonly int[] SideParents =
        {
            -1 - Upper2, ShoulderP, Shoulder, ShoulderC, Arm, ArmTwist, Elbow, WristTwist,
            Wrist, Thumb0, Thumb1, Wrist, Index1, Index2,
            Wrist, Middle1, Middle2, Wrist, Ring1, Ring2, Wrist, Little1, Little2,
            -1 - Lower, WaistCancel, Leg, Knee, Ankle,
            -1 - Root, LegIKParent, LegIK,
            WaistCancel, LegD, KneeD, AnkleD
        };

        public static readonly string[] Name = new string[Count];     // folded names as they appear in VMD files
        public static readonly int[] Parent = new int[Count];

        static B()
        {
            for (int i = 0; i < SideBase; i++) { Name[i] = CoreNames[i]; Parent[i] = CoreParents[i]; }
            for (int s = 0; s < 2; s++)
            {
                string lr = s == 0 ? "左" : "右";
                for (int o = 0; o < SideCount; o++)
                {
                    int i = S(s, o);
                    Name[i] = o == WaistCancel ? SideNames[o] + lr : lr + SideNames[o];
                    int p = SideParents[o];
                    Parent[i] = p < 0 ? -1 - p : S(s, p);
                }
            }
        }

        public static int Find(string foldedName)
        {
            for (int i = 0; i < Count; i++) if (Name[i] == foldedName) return i;
            return -1;
        }
    }

    // A skeleton in its MMD rest pose (every bone's rest rotation is identity) plus the list of
    // "outputs": points and frames rigidly attached to bones, which the host reads back
    // (for VaM: one per body controller / bone).
    public sealed class MmdRig
    {
        public readonly V3[] Rest = new V3[B.Count];       // rest position of each bone, model space
        public readonly V3[] Offset = new V3[B.Count];     // Rest - Rest[parent]

        public double UnitScale = 1;        // model-space length per VMD translation unit
        public double PelvisTravel = 1;     // extra gain on centre and groove translation

        public int LegIkLoops = 40;
        public double LegIkLimit = 2.0;               // radians per link per iteration
        public int ToeIkLoops = 3;
        public double ToeIkLimit = 4.0;
        public double KneeMin = -Math.PI;             // knee bends only about X, inside this range
        public double KneeMax = -0.5 * Math.PI / 180.0;
        // MMD's 40 iterations crawl when the leg is almost straight: after a 0.5 unit hip drop from
        // the rest pose the ankle is still 0.35 units off its goal, so the foot would bob with the hip.
        // With this on, the solver's result is finished in closed form (knee angle from the triangle,
        // one swing of the thigh): identical where the iterations converged, exact where they did not.
        public bool ExactReach = true;

        public int OutputCount;
        public int[] OutPosBone = new int[0];
        public int[] OutRotBone = new int[0];
        public V3[] OutOffset = new V3[0];           // in the position bone's rest frame
        public Q[] OutRot = new Q[0];                // output rotation when the rotation bone is at rest

        public void SetRest(V3[] rest)
        {
            for (int i = 0; i < B.Count; i++) Rest[i] = rest[i];
            for (int i = 0; i < B.Count; i++)
                Offset[i] = B.Parent[i] < 0 ? Rest[i] : Rest[i] - Rest[B.Parent[i]];
        }

        public void SetOutputs(int[] posBone, int[] rotBone, V3[] restPos, Q[] restRot)
        {
            OutputCount = posBone.Length;
            OutPosBone = posBone;
            OutRotBone = rotBone;
            OutOffset = new V3[OutputCount];
            OutRot = new Q[OutputCount];
            for (int k = 0; k < OutputCount; k++)
            {
                OutOffset[k] = restPos[k] - Rest[posBone[k]];
                OutRot[k] = restRot[k];
            }
        }

        public RigPose NewPose() { return new RigPose(this); }

        // ---- evaluation -------------------------------------------------

        // Fills world transforms and outputs from p.AnimRot / p.AnimPos / IK switches.
        public void Evaluate(RigPose p)
        {
            for (int i = 0; i < B.SideBase; i++) Forward(p, i);
            for (int s = 0; s < 2; s++)
                for (int o = 0; o < B.FirstD; o++) Forward(p, B.S(s, o));

            for (int s = 0; s < 2; s++)
            {
                p.KneeAngle[s] = 0;
                if (p.LegIkOn[s]) SolveLeg(p, s);
                if (p.ToeIkOn[s]) SolveToe(p, s);
            }

            for (int s = 0; s < 2; s++)
                for (int o = B.FirstD; o < B.SideCount; o++) Forward(p, B.S(s, o));

            for (int k = 0; k < OutputCount; k++)
            {
                int pb = OutPosBone[k];
                p.OutPos[k] = p.WorldPos[pb] + p.WorldRot[pb].Rotate(OutOffset[k]);
                p.OutRot[k] = p.WorldRot[OutRotBone[k]] * OutRot[k];
            }
        }

        // Local rotation before IK: the keyed rotation, times whatever the bone copies from another.
        private static Q BaseLocal(RigPose p, int i)
        {
            if (i < B.SideBase) return p.AnimRot[i];
            int s = (i - B.SideBase) / B.SideCount, o = (i - B.SideBase) % B.SideCount;
            switch (o)
            {
                case B.ShoulderC: return p.AnimRot[i] * p.AnimRot[B.S(s, B.ShoulderP)].Conj;   // cancels 肩P
                case B.WaistCancel: return p.AnimRot[i] * p.AnimRot[B.Waist].Conj;             // cancels 腰
                case B.LegD: return p.AnimRot[i] * p.LocalRot[B.S(s, B.Leg)];                  // copies the solved leg
                case B.KneeD: return p.AnimRot[i] * p.LocalRot[B.S(s, B.Knee)];
                case B.AnkleD: return p.AnimRot[i] * p.LocalRot[B.S(s, B.Ankle)];
                default: return p.AnimRot[i];
            }
        }

        private void Forward(RigPose p, int i)
        {
            Q baseLocal = BaseLocal(p, i);
            p.BaseRot[i] = baseLocal;
            p.IkRot[i] = Q.Identity;
            p.LocalRot[i] = baseLocal;
            Place(p, i);
        }

        private void Place(RigPose p, int i)
        {
            int par = B.Parent[i];
            double sc = UnitScale;
            if (i == B.Center || i == B.Groove) sc *= PelvisTravel;
            V3 t = Offset[i] + p.AnimPos[i] * sc;
            if (par < 0)
            {
                p.WorldRot[i] = p.LocalRot[i];
                p.WorldPos[i] = t;
            }
            else
            {
                p.WorldRot[i] = (p.WorldRot[par] * p.LocalRot[i]).Normalized;
                p.WorldPos[i] = p.WorldPos[par] + p.WorldRot[par].Rotate(t);
            }
        }

        // Re-place the leg chain of one side after an IK step.
        private void UpdateLeg(RigPose p, int s, int from)
        {
            for (int o = from; o <= B.ToeTip; o++)
            {
                int i = B.S(s, o);
                p.LocalRot[i] = p.IkRot[i] * p.BaseRot[i];
                Place(p, i);
            }
        }

        // MMD's leg IK: cyclic coordinate descent over (knee, leg), goal 足ＩＫ, effector 足首.
        // The knee turns only about its X axis inside [KneeMin, KneeMax]; iterations stop as soon
        // as one of them does not bring the ankle closer (the best state is kept).
        private void SolveLeg(RigPose p, int s)
        {
            int goal = B.S(s, B.LegIK), eff = B.S(s, B.Ankle), knee = B.S(s, B.Knee), leg = B.S(s, B.Leg);
            double best = double.MaxValue;
            Q bestKnee = Q.Identity, bestLeg = Q.Identity;
            double bestAngle = 0;
            for (int it = 0; it < LegIkLoops; it++)
            {
                StepKnee(p, s, knee, goal, eff, it);
                StepFree(p, s, leg, goal, eff, LegIkLimit, B.Leg, SkipAngle);
                double d = V3.Distance(p.WorldPos[eff], p.WorldPos[goal]);
                if (d < best)
                {
                    best = d;
                    bestKnee = p.IkRot[knee];
                    bestLeg = p.IkRot[leg];
                    bestAngle = p.KneeAngle[s];
                    // close enough: with the closed-form finish the rest of the way is exact anyway
                    if (d < (ExactReach ? 1e-5 * UnitScale : 1e-9)) break;
                }
                else
                {
                    p.IkRot[knee] = bestKnee;
                    p.IkRot[leg] = bestLeg;
                    p.KneeAngle[s] = bestAngle;
                    UpdateLeg(p, s, B.Leg);
                    break;
                }
            }
            if (ExactReach && best > 1e-9) FinishLeg(p, s, knee, leg, goal, eff);
        }

        // Closed-form finish of the leg IK from the state the iterations left: the knee angle that
        // gives the hip-goal distance, then the smallest thigh swing that puts the ankle on the line.
        // Out of reach (or folded further than the hinge allows) it takes the nearest hinge limit.
        private void FinishLeg(RigPose p, int s, int knee, int leg, int goal, int eff)
        {
            V3 k = p.WorldRot[leg].Conj.Rotate(p.WorldPos[knee] - p.WorldPos[leg]);     // thigh, leg frame
            V3 sh = p.WorldRot[knee].Conj.Rotate(p.WorldPos[eff] - p.WorldPos[knee]);   // shin, knee frame
            double d = V3.Distance(p.WorldPos[goal], p.WorldPos[leg]);
            // |k + Rx(t) sh|^2 = |k|^2 + |sh|^2 + 2 (A + B cos t + C sin t)
            double A = k.x * sh.x, Bc = k.y * sh.y + k.z * sh.z, C = k.z * sh.y - k.y * sh.z;
            double R = Math.Sqrt(Bc * Bc + C * C);
            if (R < 1e-12) return;
            double D = 0.5 * (d * d - k.SqrLength - sh.SqrLength) - A;
            double phi = Math.Atan2(C, Bc);
            double cur = p.KneeAngle[s];
            double a = cur;
            bool found = false;
            double c = D / R;
            if (c >= -1 && c <= 1)
            {
                double al = Math.Acos(c);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    double t = Wrap(phi + sign * al);
                    if (t < KneeMin || t > KneeMax) continue;
                    if (!found || Math.Abs(t - cur) < Math.Abs(a - cur)) a = t;
                    found = true;
                }
            }
            if (!found)
            {
                // no angle inside the hinge range gives this distance: take the limit that comes closest
                double eMin = Math.Abs(A + Bc * Math.Cos(KneeMin) + C * Math.Sin(KneeMin) - (D + A));
                double eMax = Math.Abs(A + Bc * Math.Cos(KneeMax) + C * Math.Sin(KneeMax) - (D + A));
                a = eMin < eMax ? KneeMin : KneeMax;
            }
            p.KneeAngle[s] = a;
            p.IkRot[knee] = Q.AxisAngle(new V3(1, 0, 0), a) * p.BaseRot[knee].Conj;
            UpdateLeg(p, s, B.Knee);
            StepFree(p, s, leg, goal, eff, double.MaxValue, B.Leg, 0);
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private void SolveToe(RigPose p, int s)
        {
            int goal = B.S(s, B.ToeIK), eff = B.S(s, B.ToeTip), ankle = B.S(s, B.Ankle);
            double best = double.MaxValue;
            Q bestAnkle = p.IkRot[ankle];
            for (int it = 0; it < ToeIkLoops; it++)
            {
                StepFree(p, s, ankle, goal, eff, ToeIkLimit, B.Ankle, SkipAngle);
                double d = V3.Distance(p.WorldPos[eff], p.WorldPos[goal]);
                if (d < best) { best = d; bestAnkle = p.IkRot[ankle]; if (d < 1e-9) break; }
                else
                {
                    p.IkRot[ankle] = bestAnkle;
                    UpdateLeg(p, s, B.Ankle);
                    break;
                }
            }
        }

        // One CCD step on an unconstrained link: turn it so the effector direction meets the goal direction.
        private const double SkipAngle = 1.745e-5;         // 0.001 degrees: MMD solvers leave smaller steps out

        private void StepFree(RigPose p, int s, int link, int goal, int eff, double limit, int fromOffset, double skip)
        {
            Q inv = p.WorldRot[link].Conj;
            V3 g = inv.Rotate(p.WorldPos[goal] - p.WorldPos[link]);
            V3 e = inv.Rotate(p.WorldPos[eff] - p.WorldPos[link]);
            V3 gn = g.NormalizedOr(V3.Zero), en = e.NormalizedOr(V3.Zero);
            if (gn.SqrLength == 0 || en.SqrLength == 0) return;
            double dot = V3.Dot(en, gn);
            if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
            double angle = Math.Acos(dot);
            if (angle < skip) return;
            if (angle > limit) angle = limit;
            V3 axis = V3.Cross(en, gn).NormalizedOr(V3.Zero);
            if (axis.SqrLength == 0) return;
            Q rot = Q.AxisAngle(axis, angle);
            Q local = p.IkRot[link] * p.BaseRot[link] * rot;
            p.IkRot[link] = (local * p.BaseRot[link].Conj).Normalized;
            UpdateLeg(p, s, fromOffset);
        }

        // One CCD step on the knee: rotation about local X only, accumulated and clamped.
        private void StepKnee(RigPose p, int s, int knee, int goal, int eff, int iteration)
        {
            Q inv = p.WorldRot[knee].Conj;
            V3 gn = inv.Rotate(p.WorldPos[goal] - p.WorldPos[knee]).NormalizedOr(V3.Zero);
            V3 en = inv.Rotate(p.WorldPos[eff] - p.WorldPos[knee]).NormalizedOr(V3.Zero);
            if (gn.SqrLength == 0 || en.SqrLength == 0) return;
            double dot = V3.Dot(en, gn);
            if (dot > 1) dot = 1; else if (dot < -1) dot = -1;
            double angle = Math.Acos(dot);
            if (angle > LegIkLimit) angle = LegIkLimit;
            V3 xAxis = new V3(1, 0, 0);
            double d1 = V3.Dot(Q.AxisAngle(xAxis, angle).Rotate(en), gn);
            double d2 = V3.Dot(Q.AxisAngle(xAxis, -angle).Rotate(en), gn);
            double a = p.KneeAngle[s] + (d1 > d2 ? angle : -angle);
            if (iteration == 0 && (a < KneeMin || a > KneeMax))
            {
                // First step on the wrong side of the hinge: mirror it into the allowed range.
                if (-a > KneeMin && -a < KneeMax) a = -a;
                else
                {
                    double half = 0.5 * (KneeMin + KneeMax);
                    if (Math.Abs(half - a) > Math.Abs(half + a)) a = -a;
                }
            }
            if (a < KneeMin) a = KneeMin; else if (a > KneeMax) a = KneeMax;
            p.KneeAngle[s] = a;
            // The knee's own keyed rotation is replaced by the hinge angle.
            p.IkRot[knee] = Q.AxisAngle(xAxis, a) * p.BaseRot[knee].Conj;
            UpdateLeg(p, s, B.Knee);
        }
    }

    // Working state of one dancer for one evaluation. Reused every step: no allocations while playing.
    public sealed class RigPose
    {
        public readonly Q[] AnimRot = new Q[B.Count];      // in: keyed rotation of each bone
        public readonly V3[] AnimPos = new V3[B.Count];    // in: keyed translation, VMD units
        public readonly bool[] LegIkOn = { true, true };   // in
        public readonly bool[] ToeIkOn = { true, true };   // in

        public readonly Q[] BaseRot = new Q[B.Count];      // local rotation before IK
        public readonly Q[] IkRot = new Q[B.Count];
        public readonly Q[] LocalRot = new Q[B.Count];     // final local rotation
        public readonly Q[] WorldRot = new Q[B.Count];     // out
        public readonly V3[] WorldPos = new V3[B.Count];   // out
        public readonly double[] KneeAngle = new double[2];

        public readonly V3[] OutPos;
        public readonly Q[] OutRot;

        public RigPose(MmdRig rig)
        {
            OutPos = new V3[rig.OutputCount];
            OutRot = new Q[rig.OutputCount];
            for (int i = 0; i < B.Count; i++)
            {
                AnimRot[i] = Q.Identity; BaseRot[i] = Q.Identity; IkRot[i] = Q.Identity;
                LocalRot[i] = Q.Identity; WorldRot[i] = Q.Identity;
            }
            for (int k = 0; k < OutRot.Length; k++) OutRot[k] = Q.Identity;
        }
    }
}
