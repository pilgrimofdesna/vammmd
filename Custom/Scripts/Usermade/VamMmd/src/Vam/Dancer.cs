using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VamMmd.Core;

namespace VamMmd
{
    // One Person atom that dances: its bones and controllers, the rig fitted to its own body,
    // and the record of everything the player changed on it so it can be put back.
    internal sealed class Dancer
    {
        public readonly Atom Person;
        public MmdRig Rig;
        public RigPose Pose;
        public readonly FitReport Report = new FitReport();
        public string Problem;              // why this Person cannot dance, or null
        public float Scale = 1f;            // model metres -> world metres (the Person's scale)
        public float ParamScale = 1f;       // the Person's "scale" setting
        public float LiveScale = 1f;        // the same thing measured on the live skeleton
        public bool Active;                 // controllers are switched on and driven by the player
        public bool Restoring;              // controllers are back at their old pose, still on, waiting for the body to be snapped
        public bool HiddenForSave;          // between HideForSave and ShowAfterSave
        public int Driven;                  // controllers found

        private readonly DAZBone[] _bone = new DAZBone[VB.Count];
        private readonly FreeControllerV3[] _ctrl = new FreeControllerV3[VB.Count];
        private readonly Transform[] _ctrlT = new Transform[VB.Count];     // the controller's kinematic body
        private readonly Transform[] _ctrlC = new Transform[VB.Count];     // its "control" handle when that is another object
        private readonly Vector3[] _modelPos = new Vector3[VB.Count];
        private Transform _root;

        private bool _saved;
        private readonly FreeControllerV3.PositionState[] _savedPosState = new FreeControllerV3.PositionState[VB.Count];
        private readonly FreeControllerV3.RotationState[] _savedRotState = new FreeControllerV3.RotationState[VB.Count];
        private readonly Vector3[] _savedPos = new Vector3[VB.Count];       // in the root's frame
        private readonly Quaternion[] _savedRot = new Quaternion[VB.Count];
        private readonly Vector3[] _heldPos = new Vector3[VB.Count];        // world, during a scene save
        private readonly Quaternion[] _heldRot = new Quaternion[VB.Count];
        private JSONStorableFloat _scaleParam;                              // the Person's size slider
        private float _savedScale = 1f;                                     // Scale when the pose was recorded

        // How firmly the body follows its controllers: the hold springs of the driven controllers
        // are this many times what they were before the dance (1 = unchanged), the dampers
        // sqrt of it so that the body is as well damped as before. Put back with the states.
        public float Firmness = 1f;
        private bool _springsSaved;
        private readonly JSONStorableFloat[] _posSpring = new JSONStorableFloat[VB.Count];
        private readonly JSONStorableFloat[] _posDamper = new JSONStorableFloat[VB.Count];
        private readonly JSONStorableFloat[] _rotSpring = new JSONStorableFloat[VB.Count];
        private readonly JSONStorableFloat[] _rotDamper = new JSONStorableFloat[VB.Count];
        private readonly float[] _savedPosSpring = new float[VB.Count];
        private readonly float[] _savedPosDamper = new float[VB.Count];
        private readonly float[] _savedRotSpring = new float[VB.Count];
        private readonly float[] _savedRotDamper = new float[VB.Count];

        public Dancer(Atom person)
        {
            Person = person;
            FindBones();
        }

        public bool Alive { get { return Person != null && _root != null; } }

        private void FindBones()
        {
            Problem = null;
            if (Person == null || Person.mainController == null) { Problem = "no Person selected"; return; }
            FreeControllerV3 main = Person.mainController;
            _root = main.control != null ? main.control : main.transform;

            DAZCharacterSelector geo = Person.GetStorableByID("geometry") as DAZCharacterSelector;
            DAZBones rootBones = geo != null ? geo.rootBones : null;
            DAZBone[] all = rootBones != null ? rootBones.dazBones : null;
            if (all == null || all.Length == 0) all = Person.GetComponentsInChildren<DAZBone>(true);
            Dictionary<string, DAZBone> byName = new Dictionary<string, DAZBone>();
            if (all != null)
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && !byName.ContainsKey(all[i].name)) byName[all[i].name] = all[i];

            Driven = 0;
            for (int k = 0; k < VB.Count; k++)
            {
                DAZBone b;
                byName.TryGetValue(VB.Name[k], out b);
                _bone[k] = b;
                _ctrl[k] = null; _ctrlT[k] = null; _ctrlC[k] = null;
                _posSpring[k] = _posDamper[k] = _rotSpring[k] = _rotDamper[k] = null;
                if (b == null)
                {
                    if (!VB.IsFinger(k) && Problem == null) Problem = "this Person has no bone named " + VB.Name[k];
                    continue;
                }
                if (VB.IsFinger(k)) continue;
                FreeControllerV3 fc = b.control;
                if (fc == null) continue;
                _ctrl[k] = fc;
                _ctrlT[k] = fc.transform;
                _posSpring[k] = fc.GetFloatJSONParam("holdPositionSpring");
                _posDamper[k] = fc.GetFloatJSONParam("holdPositionDamper");
                _rotSpring[k] = fc.GetFloatJSONParam("holdRotationSpring");
                _rotDamper[k] = fc.GetFloatJSONParam("holdRotationDamper");
                if (fc.control != null && fc.control != fc.transform) _ctrlC[k] = fc.control;
                Driven++;
            }
            if (Problem == null && Driven < 10) Problem = "this Person has only " + Driven + " body controllers";
        }

        // Reads the body's proportions from its rest skeleton (the bind pose after the look's morphs;
        // the pose the Person is in does not matter) and fits the dance skeleton to it.
        public bool Fit(FitOptions opt)
        {
            FindBones();
            if (Problem != null) return false;

            BodyRest body = new BodyRest();
            for (int k = 0; k < VB.Count; k++)
            {
                DAZBone b = _bone[k];
                if (b == null)
                {
                    // a missing finger joint: put it on its parent, it then has no length and no effect
                    int par = VB.Parent[k];
                    body.Pos[k] = body.Pos[par];
                    body.Rot[k] = body.Rot[par];
                    _modelPos[k] = _modelPos[par];
                    continue;
                }
                Vector3 p = b.morphedWorldPosition;
                Matrix4x4 m = b.morphedLocalToWorldMatrix;
                Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
                Quaternion q = f.sqrMagnitude > 1e-8f && u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(f, u) : Quaternion.identity;
                _modelPos[k] = p;
                body.Pos[k] = Conv.ToMmd(p);
                body.Rot[k] = Conv.ToMmd(q);
            }

            MmdRig rig = RigBuilder.Build(body, RefBody.Tda(), opt, Report);
            if (rig == null) { Problem = Report.Problem; return false; }
            Rig = rig;
            Pose = rig.NewPose();
            MeasureScale();
            return true;
        }

        // The Person's size setting, checked against the live skeleton: the summed length of both
        // arms and legs now, over the same lengths in the rest skeleton.
        private void MeasureScale()
        {
            ParamScale = 1f;
            if (_scaleParam == null)
            {
                JSONStorable rescale = Person.GetStorableByID("rescaleObject");
                if (rescale != null) _scaleParam = rescale.GetFloatJSONParam("scale");
            }
            if (_scaleParam != null) ParamScale = _scaleParam.val;
            int[] from = { VB.Shldr, VB.ForeArm, VB.Thigh, VB.Shin };
            int[] to = { VB.ForeArm, VB.Hand, VB.Shin, VB.Foot };
            float live = 0, model = 0;
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < from.Length; i++)
                {
                    int a = VB.S(s, from[i]), b = VB.S(s, to[i]);
                    if (_bone[a] == null || _bone[b] == null) continue;
                    live += Vector3.Distance(_bone[a].transform.position, _bone[b].transform.position);
                    model += Vector3.Distance(_modelPos[a], _modelPos[b]);
                }
            LiveScale = model > 0.1f ? live / model : 0f;
            bool liveOk = LiveScale > 0.2f && LiveScale < 5f && !float.IsNaN(LiveScale);
            bool paramOk = ParamScale > 0.2f && ParamScale < 5f && !float.IsNaN(ParamScale);
            if (paramOk && (!liveOk || Mathf.Abs(LiveScale / ParamScale - 1f) < 0.03f)) Scale = ParamScale;
            else if (liveOk) Scale = LiveScale;
            else Scale = 1f;
        }

        // Remember what the controllers were doing, then switch every driven one to hold
        // position and rotation.
        public void Activate()
        {
            if (Active || !Alive) return;
            if (Restoring)
            {
                // started again before the old states were handed back: they are still on, keep the record
                Restoring = false;
                Active = true;
                ApplyFirmness();
                return;
            }
            Vector3 rp = _root.position;
            Quaternion inv = Quaternion.Inverse(_root.rotation);
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) continue;
                _savedPosState[k] = fc.currentPositionState;
                _savedRotState[k] = fc.currentRotationState;
                _savedPos[k] = inv * (_ctrlT[k].position - rp);
                _savedRot[k] = inv * _ctrlT[k].rotation;
                fc.currentPositionState = FreeControllerV3.PositionState.On;
                fc.currentRotationState = FreeControllerV3.RotationState.On;
            }
            _saved = true;
            _savedScale = Scale;
            SaveSprings();
            ApplyFirmness();
            Active = true;
        }

        // Stop driving. With restore, controllers get back the states and the pose (relative to the
        // Person's root) they had before the dance.
        public void Deactivate(bool restore)
        {
            if (!Active) return;
            Active = false;
            if (!restore || !_saved || !Alive) return;
            Vector3 rp = _root.position;
            Quaternion rq = _root.rotation;
            float grow = SavedPoseScale();
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) continue;
                Place(k, rp + rq * (_savedPos[k] * grow), rq * _savedRot[k]);
                fc.currentPositionState = _savedPosState[k];
                fc.currentRotationState = _savedRotState[k];
            }
            RestoreSprings();
            _saved = false;
        }

        // First half of a clean restore: the controllers go back to the pose they had before the
        // dance but keep holding, so that a physics reset can snap the body onto them.
        public void BeginRestore()
        {
            if (!Active) return;
            Active = false;
            if (!_saved || !Alive) return;
            Vector3 rp = _root.position;
            Quaternion rq = _root.rotation;
            float grow = SavedPoseScale();
            for (int k = 0; k < VB.Count; k++)
                if (_ctrl[k] != null) Place(k, rp + rq * (_savedPos[k] * grow), rq * _savedRot[k]);
            Restoring = true;
        }

        // Second half: the controllers get their old on/off states back.
        public void FinishRestore()
        {
            if (!Restoring) return;
            Restoring = false;
            _saved = false;
            if (!Alive) return;
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) continue;
                fc.currentPositionState = _savedPosState[k];
                fc.currentRotationState = _savedRotState[k];
            }
            RestoreSprings();
        }

        // A scene save reads the controllers as they are. While the dance holds them that would
        // store the dance pose with every controller switched on, and the scene would open with a
        // rigid body and nothing to give back. So for the moment of the save the controllers show
        // what they were before the dance; ShowAfterSave puts the dance back. VaM calls both in
        // the same frame, with no physics step between them, so the body does not move.
        public void HideForSave()
        {
            if (HiddenForSave || !(Active || Restoring) || !_saved || !Alive) return;
            Vector3 rp = _root.position;
            Quaternion rq = _root.rotation;
            float grow = SavedPoseScale();
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) continue;
                _heldPos[k] = _ctrlT[k].position;
                _heldRot[k] = _ctrlT[k].rotation;
                Place(k, rp + rq * (_savedPos[k] * grow), rq * _savedRot[k]);
                fc.currentPositionState = _savedPosState[k];
                fc.currentRotationState = _savedRotState[k];
            }
            RestoreSprings();
            HiddenForSave = true;
        }

        public void ShowAfterSave()
        {
            if (!HiddenForSave) return;
            HiddenForSave = false;
            if (!(Active || Restoring) || !_saved || !Alive) return;
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) continue;
                fc.currentPositionState = FreeControllerV3.PositionState.On;
                fc.currentRotationState = FreeControllerV3.RotationState.On;
                Place(k, _heldPos[k], _heldRot[k]);
            }
            SaveSprings();
            ApplyFirmness();
        }

        // ---- firmness -------------------------------------------------------

        public void SetFirmness(float f)
        {
            Firmness = f;
            if (Active && !HiddenForSave) ApplyFirmness();
        }

        private void SaveSprings()
        {
            for (int k = 0; k < VB.Count; k++)
            {
                _savedPosSpring[k] = _posSpring[k] != null ? _posSpring[k].val : 0f;
                _savedPosDamper[k] = _posDamper[k] != null ? _posDamper[k].val : 0f;
                _savedRotSpring[k] = _rotSpring[k] != null ? _rotSpring[k].val : 0f;
                _savedRotDamper[k] = _rotDamper[k] != null ? _rotDamper[k].val : 0f;
            }
            _springsSaved = true;
        }

        private void ApplyFirmness()
        {
            if (!_springsSaved || !Alive) return;
            float m = Mathf.Clamp(Firmness, 0.05f, 20f), d = Mathf.Sqrt(m);
            for (int k = 0; k < VB.Count; k++)
            {
                if (_ctrl[k] == null) continue;
                SetScaled(_posSpring[k], _savedPosSpring[k], m);
                SetScaled(_posDamper[k], _savedPosDamper[k], d);
                SetScaled(_rotSpring[k], _savedRotSpring[k], m);
                SetScaled(_rotDamper[k], _savedRotDamper[k], d);
            }
        }

        private static void SetScaled(JSONStorableFloat p, float saved, float factor)
        {
            if (p == null) return;
            float v = Mathf.Clamp(saved * factor, p.min, p.max);
            if (Mathf.Abs(p.val - v) > 1e-4f) p.val = v;
        }

        private void RestoreSprings()
        {
            if (!_springsSaved || !Alive) return;
            for (int k = 0; k < VB.Count; k++)
            {
                if (_ctrl[k] == null) continue;
                SetScaled(_posSpring[k], _savedPosSpring[k], 1f);
                SetScaled(_posDamper[k], _savedPosDamper[k], 1f);
                SetScaled(_rotSpring[k], _savedRotSpring[k], 1f);
                SetScaled(_rotDamper[k], _savedRotDamper[k], 1f);
            }
            _springsSaved = false;
        }

        // The pose from before the dance was recorded in metres at the size the Person had then.
        // When the size slider was moved since, the body has grown or shrunk about its root and
        // the old pose has to grow with it, or the body would be put back crouched or stretched.
        private float SavedPoseScale()
        {
            if (_scaleParam != null && Mathf.Abs(_scaleParam.val - ParamScale) > 0.001f) MeasureScale();
            return _savedScale > 0.01f ? Scale / _savedScale : 1f;
        }

        private static string ParamText(JSONStorableFloat p) { return p == null ? "-" : p.val.ToString("0.###"); }

        private void Place(int k, Vector3 pos, Quaternion rot)
        {
            _ctrlT[k].SetPositionAndRotation(pos, rot);
            if (_ctrlC[k] != null) _ctrlC[k].SetPositionAndRotation(pos, rot);
        }

        // Writes the evaluated pose to the controllers. Controllers the user holds or possesses are left alone.
        public void Write(RigPose pose)
        {
            if (!Active || !Alive) return;
            // the Person's size slider was moved during the dance: the skeleton has already grown or shrunk
            if (_scaleParam != null && Mathf.Abs(_scaleParam.val - ParamScale) > 0.001f) MeasureScale();
            Vector3 rp = _root.position;
            Quaternion rq = _root.rotation;
            float s = Scale;
            for (int k = 0; k < VB.Count; k++)
            {
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null || fc.possessed || fc.isGrabbing) continue;
                V3 p = pose.OutPos[k];
                Q q = pose.OutRot[k];
                Vector3 wp = rp + rq * (new Vector3(-(float)p.x, (float)p.y, -(float)p.z) * s);
                Quaternion wq = rq * new Quaternion(-(float)q.x, (float)q.y, -(float)q.z, (float)q.w);
                Place(k, wp, wq);
            }
        }

        // Snap the body onto its controllers without the half-second drag (pauses physics for 5 frames).
        public void SnapPhysics()
        {
            if (Alive) Person.ResetPhysics(true, false);
        }

        // ---- reports for the in-game checks -------------------------------

        public string FitText()
        {
            if (Problem != null) return "Body: " + Problem;
            if (Rig == null) return "Body: not measured yet";
            StringBuilder sb = new StringBuilder();
            sb.Append("Body: legs ").Append((Report.LegLength * Scale).ToString("0.00")).Append(" m, arms ")
              .Append((Report.ArmLength * Scale).ToString("0.00")).Append(" m, shoulders ")
              .Append((Report.ShoulderWidth * Scale).ToString("0.00")).Append(" m");
            if (Mathf.Abs(Scale - 1f) > 0.005f) sb.Append(" (scale ").Append(Scale.ToString("0.00")).Append(")");
            sb.Append("\nOne MMD unit = ").Append((Report.MetresPerUnit * Scale * 100).ToString("0.0")).Append(" cm for this body");
            sb.Append("\nAgainst the dance's model: arms ").Append(Percent(Report.ArmVsReference))
              .Append(", shoulders ").Append(Percent(Report.ShoulderVsReference));
            return sb.ToString();
        }

        private static string Percent(double ratio)
        {
            double d = (ratio - 1) * 100;
            if (Math.Abs(d) < 0.5) return "the same";
            return Math.Abs(d).ToString("0") + "% " + (d > 0 ? "longer" : "shorter");
        }

        // Everything a developer needs to judge the fit without seeing the screen.
        public string BodyReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Person: ").Append(Person != null ? Person.uid : "(none)").Append('\n');
            if (Problem != null) sb.Append("PROBLEM: ").Append(Problem).Append('\n');
            sb.Append("scale setting ").Append(ParamScale.ToString("0.0000")).Append(", live skeleton ").Append(LiveScale.ToString("0.0000"))
              .Append(", used ").Append(Scale.ToString("0.0000")).Append('\n');
            sb.Append("rest skeleton (model metres): leg ").Append(Report.LegLength.ToString("0.0000")).Append(", arm ").Append(Report.ArmLength.ToString("0.0000"))
              .Append(", shoulder width ").Append(Report.ShoulderWidth.ToString("0.0000")).Append(", metres per MMD unit ").Append(Report.MetresPerUnit.ToString("0.00000"))
              .Append(", floor shift ").Append(Report.FloorShift.ToString("0.0000")).Append('\n');
            sb.Append("arm vs reference ").Append(Report.ArmVsReference.ToString("0.000")).Append(", shoulder vs reference ").Append(Report.ShoulderVsReference.ToString("0.000")).Append('\n');
            if (_root != null)
            {
                DAZCharacterSelector geo = Person.GetStorableByID("geometry") as DAZCharacterSelector;
                DAZBones rb = geo != null ? geo.rootBones : null;
                if (rb != null)
                {
                    Vector3 dp = Quaternion.Inverse(_root.rotation) * (rb.transform.position - _root.position);
                    Vector3 de = (Quaternion.Inverse(_root.rotation) * rb.transform.rotation).eulerAngles;
                    sb.Append("skeleton root in the main control's frame: pos ").Append(dp.ToString("F4")).Append(" euler ").Append(de.ToString("F2"))
                      .Append(" lossyScale ").Append(rb.transform.lossyScale.ToString("F4")).Append(" male ").Append(rb.isMale).Append('\n');
                }
            }
            sb.Append("bone: model pos | rest euler | controller | control==transform | states | start-rot vs rest (deg)\n");
            for (int k = 0; k < VB.Count; k++)
            {
                DAZBone b = _bone[k];
                sb.Append(VB.Name[k]).Append(": ");
                if (b == null) { sb.Append("MISSING\n"); continue; }
                Matrix4x4 m = b.morphedLocalToWorldMatrix;
                Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
                Quaternion rest = f.sqrMagnitude > 1e-8f && u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(f, u) : Quaternion.identity;
                sb.Append(b.morphedWorldPosition.ToString("F4")).Append(" | ").Append(rest.eulerAngles.ToString("F1")).Append(" | ");
                FreeControllerV3 fc = _ctrl[k];
                if (fc == null) sb.Append("- | - | - | ");
                else
                    sb.Append(fc.name).Append(" | ").Append(_ctrlC[k] == null).Append(" | ")
                      .Append(fc.currentPositionState).Append('/').Append(fc.currentRotationState).Append(" | ")
                      .Append("spring ").Append(ParamText(_posSpring[k])).Append('/').Append(ParamText(_rotSpring[k]))
                      .Append(" damper ").Append(ParamText(_posDamper[k])).Append('/').Append(ParamText(_rotDamper[k])).Append(" | ");
                sb.Append(Quaternion.Angle(rest, b.startingRotationRelativeToRoot).ToString("0.0")).Append('\n');
            }
            return sb.ToString();
        }

        // How far each bone is from the target its controller holds, right now.
        public string TrackingReport()
        {
            StringBuilder sb = new StringBuilder();
            float worstP = 0, worstA = 0;
            string worstPn = "", worstAn = "";
            sb.Append("bone: distance to target (mm) | angle to target (deg)\n");
            for (int k = 0; k < VB.Count; k++)
            {
                if (_ctrl[k] == null || _bone[k] == null) continue;
                float d = Vector3.Distance(_bone[k].transform.position, _ctrlT[k].position) * 1000f;
                float a = Quaternion.Angle(_bone[k].transform.rotation, _ctrlT[k].rotation);
                sb.Append(VB.Name[k]).Append(": ").Append(d.ToString("0.0")).Append(" | ").Append(a.ToString("0.0")).Append('\n');
                if (d > worstP) { worstP = d; worstPn = VB.Name[k]; }
                if (a > worstA) { worstA = a; worstAn = VB.Name[k]; }
            }
            sb.Append("worst distance ").Append(worstP.ToString("0.0")).Append(" mm (").Append(worstPn).Append("), worst angle ")
              .Append(worstA.ToString("0.0")).Append(" deg (").Append(worstAn).Append(")\n");
            return sb.ToString();
        }
    }
}
