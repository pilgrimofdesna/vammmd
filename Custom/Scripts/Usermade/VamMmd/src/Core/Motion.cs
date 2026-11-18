using System;
using System.Collections.Generic;

namespace VamMmd.Core
{
    // Binds one VMD file to the rig's bones and fills a RigPose for any time.
    // The file is shared and read-only; cursors live here, so several dancers can play one file.
    public sealed class MotionSampler
    {
        public readonly VmdFile File;
        private readonly BoneTrack[] _track = new BoneTrack[B.Count];
        private readonly int[] _cursor = new int[B.Count];

        // IK switch per side: +1 / 0 from the file's IK keys, or decided from the keys when the file is silent.
        private readonly string[] _legIkName = new string[2];
        private readonly string[] _toeIkName = new string[2];
        private readonly bool[] _legIkDefault = new bool[2];
        private readonly bool _hasIkKeys;

        public int BoundBones;                 // how many of the rig's bones the file keys
        public readonly List<string> Unknown = new List<string>();     // keyed bones the rig does not have

        public MotionSampler(VmdFile file)
        {
            File = file;
            for (int i = 0; i < file.Bones.Count; i++)
            {
                BoneTrack t = file.Bones[i];
                int b = B.Find(t.Name);
                if (b >= 0) { if (_track[b] == null) BoundBones++; _track[b] = t; }
                else if (t.AnyRot || t.AnyPos) Unknown.Add(t.Name);
            }
            _hasIkKeys = file.IkKeys.Count > 0;
            for (int s = 0; s < 2; s++)
            {
                _legIkName[s] = B.Name[B.S(s, B.LegIK)];
                _toeIkName[s] = B.Name[B.S(s, B.ToeIK)];
                // A file with no IK section: legs are FK only when the IK bone carries no motion
                // while the leg or knee is keyed with real rotations (motion-capture style files).
                BoneTrack ik = _track[B.S(s, B.LegIK)];
                BoneTrack leg = _track[B.S(s, B.Leg)], knee = _track[B.S(s, B.Knee)];
                bool ikMoves = ik != null && (ik.AnyPos || ik.AnyRot);
                bool fkMoves = (leg != null && leg.AnyRot) || (knee != null && knee.AnyRot);
                _legIkDefault[s] = ikMoves || !fkMoves;
            }
        }

        public double LengthSeconds { get { return File.LastFrame / VmdFile.Fps; } }

        public bool Has(int bone) { return _track[bone] != null; }

        // seconds -> pose inputs (AnimRot, AnimPos, IK switches). Does not evaluate the rig.
        public void Sample(double seconds, RigPose p)
        {
            double frame = seconds * VmdFile.Fps;
            if (frame < 0 || double.IsNaN(frame)) frame = 0;
            for (int i = 0; i < B.Count; i++)
            {
                BoneTrack t = _track[i];
                if (t == null) { p.AnimRot[i] = Q.Identity; p.AnimPos[i] = V3.Zero; continue; }
                t.Sample(frame, ref _cursor[i], out p.AnimPos[i], out p.AnimRot[i]);
            }
            for (int s = 0; s < 2; s++)
            {
                bool leg = _legIkDefault[s];
                bool toe = true;
                if (_hasIkKeys)
                {
                    int st = File.IkState(_legIkName[s], frame);
                    if (st >= 0) leg = st == 1;
                    int tt = File.IkState(_toeIkName[s], frame);
                    if (tt >= 0) toe = tt == 1;
                }
                p.LegIkOn[s] = leg;
                p.ToeIkOn[s] = leg && toe;
            }
        }
    }

    // The dance clock. It advances by the physics step and is pulled gently towards the music,
    // so the pose moves smoothly even though the audio position is only reported in coarse steps.
    public sealed class DanceClock
    {
        public double Time;                 // seconds into the dance
        public double Speed = 1;
        public bool Playing;
        public double Length;               // seconds; 0 = unknown
        public bool Loop;
        public double LoopStart, LoopEnd;   // A-B loop when LoopEnd > LoopStart

        public double PullSeconds = 0.3;    // time constant of the pull towards the audio position
        public double SnapSeconds = 0.12;   // further off than this: jump (a seek happened elsewhere)

        // How fast the music runs against the time steps this clock is given. 1 when the game keeps
        // up with real time. A heavy scene makes the game run slow (VaM caps the time a frame may
        // take), the music does not slow down with it, and the dance has to run faster per step
        // to stay with it. Learned from the error, so that a steady difference leaves no lag.
        public double Rate = 1;
        public const double RateMin = 0.5, RateMax = 3;

        public bool Jumped;                 // set by Step/Seek when time moved discontinuously; the host clears it
        public bool Ended;                  // set when the end was reached and playback stopped

        public void Seek(double seconds)
        {
            if (double.IsNaN(seconds)) return;
            if (seconds < 0) seconds = 0;
            if (Length > 0 && seconds > Length) seconds = Length;
            Time = seconds;
            Jumped = true;
            Ended = false;
        }

        // dt: seconds of game time for this step. audioTime: where the music is now, or NaN when
        // there is no usable audio clock (no music, paused, not started yet).
        public void Step(double dt, double audioTime)
        {
            if (!Playing || dt <= 0 || double.IsNaN(dt)) return;
            Time += dt * Speed * Rate;
            if (!double.IsNaN(audioTime))
            {
                double err = audioTime - Time;
                // The rate follows the error (critically damped together with the pull below). A
                // jump teaches it no more than the largest error the pull would have seen.
                double kp = 1.0 / PullSeconds;
                double bounded = err > SnapSeconds ? SnapSeconds : (err < -SnapSeconds ? -SnapSeconds : err);
                Rate += bounded * dt * kp * kp / (4 * (Speed > 0.05 ? Speed : 0.05));
                if (Rate < RateMin) Rate = RateMin;
                if (Rate > RateMax) Rate = RateMax;
                if (Math.Abs(err) > SnapSeconds) { Time = audioTime; Jumped = true; }
                else Time += err * Math.Min(1.0, dt * kp);
            }
            else
            {
                // no music to follow: drift back to the game's own time over a few seconds
                Rate += (1 - Rate) * Math.Min(1.0, dt / 2.0);
            }
            double end = Length;
            double loopTo = 0;
            if (LoopEnd > LoopStart) { end = LoopEnd; loopTo = LoopStart; }
            if (end > 0 && Time >= end)
            {
                if (Loop || LoopEnd > LoopStart) { Time = loopTo; Jumped = true; }
                else { Time = end; Playing = false; Ended = true; }
            }
        }
    }
}
