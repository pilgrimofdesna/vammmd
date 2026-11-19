using System;
using System.Globalization;
using System.IO;
using System.Text;
using VamMmd.Core;

// Developer tool (not part of the plugin): dumps what the core reads and samples from a VMD,
// so it can be compared with an independent implementation.
//   mono Dump.exe sample FILE.vmd STEP_FRAMES  -> one line per bone per sampled frame
//   mono Dump.exe info FILE.vmd                -> header, counts, IK keys
//   mono Dump.exe rig FILE.vmd STEP_FRAMES [exact] -> world transform of every rig bone on the reference body
public static class Dump
{
    public static int Main(string[] a)
    {
        CultureInfo ci = CultureInfo.InvariantCulture;
        Console.OutputEncoding = new UTF8Encoding(false);
        byte[] d = File.ReadAllBytes(a[1]);
        VmdFile f = VmdFile.Parse(d);
        if (a[0] == "info")
        {
            Console.WriteLine("model=" + f.ModelName + " bones=" + f.Bones.Count + " boneKeys=" + f.BoneKeyCount + " morphs=" + f.Morphs.Count +
                " camera=" + f.Camera.Count + " ik=" + f.IkKeys.Count + " last=" + f.LastFrame + " warn=" + f.Warning);
            foreach (IkKey k in f.IkKeys)
            {
                StringBuilder sb = new StringBuilder("ik frame " + k.Frame + " show " + k.Show);
                for (int i = 0; i < k.Names.Length; i++) sb.Append(" " + k.Names[i] + "=" + (k.Enabled[i] ? 1 : 0));
                Console.WriteLine(sb.ToString());
            }
            return 0;
        }
        double step = double.Parse(a[2], ci);
        if (a[0] == "rig")
        {
            // world transform of every rig bone on the reference body, plain MMD iterations
            MmdRig rig = RefBody.Tda().ToRig();
            rig.ExactReach = a.Length > 3 && a[3] == "exact";
            RigPose pose = rig.NewPose();
            MotionSampler ms = new MotionSampler(f);
            for (double fr = 0; fr <= f.LastFrame; fr += step)
            {
                ms.Sample(fr / VmdFile.Fps, pose);
                rig.Evaluate(pose);
                for (int i = 0; i < B.Count; i++)
                {
                    V3 p = pose.WorldPos[i]; Q q = pose.WorldRot[i];
                    Console.WriteLine(string.Format(ci, "{0}\t{1:R}\t{2:R}\t{3:R}\t{4:R}\t{5:R}\t{6:R}\t{7:R}\t{8:R}", B.Name[i], fr, p.x, p.y, p.z, q.x, q.y, q.z, q.w));
                }
            }
            return 0;
        }
        foreach (BoneTrack t in f.Bones)
        {
            int cur = 0;
            for (double fr = 0; fr <= f.LastFrame; fr += step)
            {
                V3 p; Q q;
                t.Sample(fr, ref cur, out p, out q);
                Console.WriteLine(string.Format(ci, "{0}\t{1:R}\t{2:R}\t{3:R}\t{4:R}\t{5:R}\t{6:R}\t{7:R}\t{8:R}", t.Name, fr, p.x, p.y, p.z, q.x, q.y, q.z, q.w));
            }
        }
        return 0;
    }
}
