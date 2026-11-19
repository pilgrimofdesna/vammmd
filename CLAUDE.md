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
- Lowering a `JSONStorableFloat`'s `max` below its value makes the UI slider clamp itself and
  fire the callback as if the user had dragged it. Set `valNoCallback` first (`SyncLength`;
  before that fix every song shorter than the one before it was skipped).
- `isStorable = false` does not stop a restore; params that must not be reset by a partial
  restore also get `isRestorable = false`.
- `FileManagerSecure.GetFiles(dir, pattern)` matches the pattern case-sensitively ("*.vmd"
  misses "X.VMD"). Pass `null` (all files) and filter with `Library.HasExtension`.
- A scene save stores every controller's state and pose as it is at that moment.
  `onBeforeSceneSaveHandlers` and `onSceneSavedHandlers` run in the same call with no physics
  step between them; `Dancer.HideForSave` / `ShowAfterSave` show the save the body from before
  the dance. Presets saved from the Person's own tabs do not go through these handlers.
- VaM runs slower than real time whenever a frame takes longer than `physicsUpdateCap` physics
  steps (`Time.maximumDeltaTime`; cap 2 at 90 Hz means below 45 fps). Audio does not slow down.
- A plugin that is switched off still receives button clicks and trigger actions, but no
  `Update`. Anything that takes the body checks `isActiveAndEnabled` first. Coroutines stop
  when the object is deactivated (`OnDisable` unloads the music, `OnEnable` asks for it again).

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
  more than 120 ms off (a frame hitch); only a jump over 0.5 s, a seek or a new song snaps the
  body, because every reset freezes all physics for 5 frames.
- **The clock learns the music's rate** (`DanceClock.Rate`). With the pull alone, a scene at
  36 fps (game time 0.8x) left the dance up to 80 ms late and jumped it forward about twice a
  second. The rate is the integral part of a critically damped pair with the pull
  (`PullSeconds` 0.3). Simulated: steady lag under 10 ms from 20 to 90 fps, no jump for a drop
  from 90 to 34 fps, one for 90 to 30. With a 0.5 s pull the same drops gave 2 and 9 jumps;
  the price of 0.3 s is a step ripple of 0.7 ms instead of 0.4 ms from the music's 23 ms
  position blocks. Without music the rate fades back to 1 (the dance runs on game time).
  A second jump within 3 s of the last one sets the rate from the drift it just measured;
  without that a start at 20 fps hopped forward for 11 s and a recovery from 20 to 90 fps
  hopped backward 22 times (now two jumps each). Two real hitches close together teach a
  wrong rate for a moment; the pull brings it back without further jumps (tested).
- **The pose given back follows a size change**: it was recorded in metres at the size the
  Person had at Play, so Stop and a scene save scale it by the size now over the size then.
- **The `Playing` bool counts a start that waits for the music**, so a toggle bound to it does
  not flip by itself and setting it to false cancels the wait.
- **Play waits for the music** (`_playPending`): a WAV decodes over about a second, and a
  dance that starts before it is ready plays its beginning in silence.
- **Body Firmness multiplies the Person's own hold springs** (default x2, dampers x sqrt so
  the damping ratio stays), instead of setting absolute values or mmd2timeline's
  AllJointsController percent presets: a scene's own tuning is kept as the base, x1 changes
  nothing, and Stop / a scene save put the old values back. The default x2 is a guess until
  the tracking report from the game is in; the body report prints every spring and damper.
  Param names (game IL): `holdPositionSpring` 0..10000, `holdPositionDamper` 0..100,
  `holdRotationSpring` 0..1000, `holdRotationDamper` 0..10.
- **As a session plugin the player takes nobody in a newly opened scene** (owner's wish,
  2026-11-19): `onSceneLoadedHandlers` stops it and clears the dancer, and no dancer is picked
  automatically; Play with nobody chosen takes the only Person, else the first woman. A scene
  or Person plugin keeps picking its own Person as before. Session plugin =
  `containingAtom.type == "SessionPluginManager"`.
- **The body is measured from the rest skeleton** (`DAZBone.morphedWorldPosition`, model metres,
  x already negated on import), not from the live pose, so the Person never has to be posed.
  The scale is the Person's `scale` setting, cross-checked against live bone lengths.

## Licence

MIT, chosen by the owner on 2026-11-19; a Hub `.var` says CC BY (VaM's package licence list
has no MIT). Keep the two consistent, and do not copy code from GPL (mmd2timeline),
CC BY-SA (Misiv, KAI) or CC BY-NC-SA (UIAssist) sources: ideas and file formats only.

## Not verified in the game yet

`control == transform` on Person controllers; that the rest rotations read from
`morphedLocalToWorldMatrix` are the controllers' frames; that the model space is the main
controller's frame; that a reset after a full-pose write is visually clean; audio through an
`AudioSource` of our own. The three Check buttons write the numbers that settle most of these
to `Saves/PluginData/VamMmd/check.txt`.
