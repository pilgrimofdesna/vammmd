# VamMmd Player: developer notes

Notes for whoever (Claude included) works on this next. The owner is not a programmer: explain
in Korean with concrete examples, and ask when a decision is theirs.

## Layout

- `Custom/Scripts/Usermade/VamMmd/VamMmd.cslist`: what VaM loads. Every `.cs` must be listed.
- `src/Core/`: Unity-free calculation (own `V3`/`Q` doubles, VMD reader, Shift-JIS table, rig,
  IK, body fit, clock, WAV reader). Namespace `VamMmd.Core`. No `UnityEngine`, no VaM types.
- `src/Vam/`: the plugin. Exactly one `MVRScript` (`VamMmd.Player`); VaM instantiates every
  `MVRScript` subclass it finds, so helpers are plain classes.
- `tests/`: `run.sh` (core tests), `compile_vam.sh` (compile against the game), `tools/`
  (dump tool, independent Python evaluator).
- `tools/`: generators (`gen_sjis_table.py` writes `SjisTable.cs`; `pmx_bones.py` dumps a
  model's bone table).

## Build and test (run both before every commit)

```sh
sh tests/run.sh          # must end with "PASS: N checks, 0 failed"
VAM_MANAGED=/path/to/VaM_Data/Managed sh tests/compile_vam.sh
```

- Needs `mono-mcs mono-runtime mono-utils` (`apt-get update` first).
- The game DLLs come from the owner's PC (`D:\user_entertainment\vam\VaM_Data\Managed`).
- Warnings are errors in both scripts.
- A test that fails is fixed in the code. Change an expectation only when the expectation was
  wrong, and say why in a comment (see "rest: ankle on target").
- Cross-check with the independent evaluator after touching `Rig.cs` or `Vmd.cs`
  (`tests/tools/rigcmp.py`, header comment has the commands). Last result on five dances: all 79
  bones within 0.00003 units and 0.0004 degrees, right thumb excepted (the measured model is not
  symmetric there).

## VaM traps

- C# 6, .NET 3.5: no `out var`, tuples, pattern matching, local functions; no
  `string.IsNullOrWhiteSpace`, `StringBuilder.Clear`, `Enum.TryParse`, `Tuple`.
- VaM refuses any plugin that references a type in `System.IO` or `System.Reflection`. This is
  checked on the compiled module, so it bites indirectly: **`e.GetType().Name` references
  `System.Reflection.MemberInfo`** and would stop the whole plugin from loading.
  `compile_vam.sh` looks for such references. Files go through `FileManagerSecure` only.
- Plugins can read files only inside the VaM folder, and write without a prompt only under
  `Saves/PluginData` and `Custom/PluginData`.
- The class name `VamMmd.Player` and every registered action and parameter name are a public
  contract (scenes, triggers, UIAssist buttons bind to `plugin#N_VamMmd.Player` + name). Add
  names, never rename.
- Every `+=` on a `SuperController` handler has its `-=` in `OnDestroy`.
- Controller targets are written in `FixedUpdate` to the controller's own transform (its
  kinematic body) and to `control` when that is another object. A controller in state `Off`
  mirrors its bone and cannot hold a target; `Comply` moves its own target.
- `Atom.ResetPhysics(true, false)` snaps the bones onto the controllers. It pauses physics for
  all atoms for 5 frames, takes 10 frames in all, has no "finished" flag (count 12 frames), and a
  second call while one runs is dropped. `SuperController.freezeAnimation` is true only during
  the paused half; the clock and the music stop on it.
- Restore is in two steps for that reason: controllers back to the old pose while still `On`,
  reset, then the old states (`Dancer.BeginRestore` / `FinishRestore`).
- Loading and restore order is not ours: param callbacks only set flags (`_needLoadMotion`,
  `_needLoadAudio`, `_libraryDirty`), the work happens in `Update`.

## Conventions in the core

- MMD's own axes everywhere: Y up, the model faces -Z, its left hand is at +X. VaM is the same
  handedness turned half round Y: position `(x,y,z) -> (-x,y,-z)`, quaternion
  `(x,y,z,w) -> (-x,y,-z,w)`, no mirroring (`Vam/Conv.cs`). Do not copy formulas from
  right-handed code (saba, Blender).
- Quaternion product `a * b` applies `b` first. Local rotation = IK rotation * keyed rotation;
  appended bones (D bones, 肩C, 腰キャンセル) = keyed * appended.
- A key's curve shapes the segment that **ends** at that key. saba and mmd2timeline use the
  earlier key; that is their bug, not a second convention.
- Bone names are compared after folding full-width letters and digits to half-width.
- The rig is the fixed semi-standard bone set (79 bones) placed on the character's own joints.
  One output per VaM bone: position from one MMD bone, rotation from another (twist bones).
- The reference body (`RefBody.Tda`) was measured from the owner's own Tda-style model with
  `tools/pmx_bones.py`. Do not replace it with tables taken from other players.

## Decisions that were not obvious (do not undo without a reason)

- **Leg IK is finished in closed form** (`MmdRig.ExactReach`, default on). The plain 40
  iterations that every open MMD implementation uses crawl when the leg is almost straight: after
  a 0.5 unit hip drop from the rest pose the ankle is still 0.35 units off; on real dances 16 to
  34 percent of reachable leg solves end more than 0.05 units from the goal. The finish keeps the
  iterations' knee direction, sets the knee angle from the triangle and swings the thigh once.
  It moves the knee by up to 0.8 units where the iterations had not converged. Whether MMD itself
  lands exactly is not known; planted feet matter more here. `ExactReach = false` gives the plain
  behaviour (the cross-check with `mmdref.py` uses it).
- **The knee can never be fully straight**: its hinge stops at -0.5 degrees, as in the model.
  At rest the ankle therefore sits 0.0016 units above its goal. That is expected.
- **Scale = leg length ratio**, not height: the pelvis path is what a stroker reads, and it is
  the legs that have to reach the floor.
- **Feet, collar and spine are not re-aligned** to the reference: turning the foot would tilt
  the sole, and the others have no clear counterpart.
- **Small clock corrections do not reset physics.** The clock snaps to the music when it is
  more than 80 ms off (a frame hitch); only a jump over 0.5 s, a seek or a new song snaps the
  body, because every reset freezes all physics for 5 frames.
- **The body is measured from the rest skeleton** (`DAZBone.morphedWorldPosition`, model metres,
  x already negated on import), not from the live pose, so the Person never has to be posed.
  The scale is the Person's `scale` setting, cross-checked against live bone lengths.

## Not verified in the game yet

`control == transform` on Person controllers; that the rest rotations read from
`morphedLocalToWorldMatrix` are the controllers' frames; that the model space is the main
controller's frame; that a reset after a full-pose write is visually clean; audio through an
`AudioSource` of our own. The three Check buttons write the numbers that settle most of these
to `Saves/PluginData/VamMmd/check.txt`.
