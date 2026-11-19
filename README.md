# VamMmd Player

An MMD dance player for Virt-A-Mate (VaM 1.22), written from scratch. It plays VMD motions on a
Person atom together with the song's music.

Status: **first build, not yet run inside VaM.** The calculation core is tested outside VaM
(see "Tests"); the VaM side compiles against the game's own assemblies but nobody has pressed
Play in the game yet.

## What it does differently

| Problem seen in existing players | Here |
|---|---|
| The easing curve of the wrong key is used, rotations are blended in a straight line, the pose is held for a frame at each key | Curves of the key that ends the segment, proper spherical blending, no hold. Checked against a reference evaluation on real dances (15 million samples, under 0.0001 degree). |
| One fixed size for every body (the same 0.85 or 8.5 cm per MMD unit for everyone) | The dance skeleton is rebuilt on the character's own joints. Travel is scaled by the character's leg length against the model the dances were made for. |
| The arms' A-pose is one constant angle for all arm bones | Each limb is turned from the character's rest pose to the reference rest pose by its own measured direction (upper arm, forearm, hand, fingers, thigh, shin). |
| Seeking drags the body through the scene for half a second | The pose is written first, then the body is snapped onto it by VaM's own physics reset. |
| The music clock is read raw (it moves in 23 ms steps), so the body moves in steps | The dance has its own clock that advances with each physics step and is pulled gently towards the music. In a heavy scene, where the game runs slower than real time, it learns the difference and stays with the music. |
| Targets are computed once per rendered frame, one frame late | Targets are computed inside every physics step for that step's own time. |
| Loading music touches the scene (audio list, other clips) | The WAV is decoded by the plugin itself and played on its own source. |
| Nothing is put back afterwards | Stop returns every controller to the state and pose it had before the dance. A scene saved during a dance is saved with the body as it was before the dance. |
| Feet float when the legs are almost straight (MMD's leg solver gives up after 40 rounds) | The leg solver's result is finished exactly, so the ankle sits on its goal whenever the goal can be reached. |

Not in this build yet: face morphs, fingers, camera motion, hand-contact keeping, several
dancers, MP3/OGG, the in-scene remote panel and status chip (see the UI proposal in the project
notes). The plugin window has the controls for now.

## Install

Copy the folder `Custom/Scripts/Usermade/VamMmd` into the same place of your VaM folder
(`<VaM>/Custom/Scripts/Usermade/VamMmd`). In VaM add the plugin `VamMmd.cslist` as a scene
plugin, a session plugin, or on the Person.

Dances are looked for in `<VaM>/MMDdata` (one folder per song with its `.vmd` and `.wav`);
another folder can be chosen in the plugin window. Motion files must lie inside the VaM folder,
VaM does not let plugins read anything else.

## Use

1. Choose the **Dancer** (a Person in the scene) and a **Song**. A song folder with several
   motions lists them under **Motion**; camera-only files are left out.
2. **Play**. The body is measured, the controllers of the body are switched on, and the dance
   starts. **Stop** gives the body back as it was.
3. **Audio Offset** shifts the music against the motion; it is remembered per song in
   `Saves/PluginData/VamMmd/songs.json`.
4. **Pelvis Travel** scales how far the hips move across the floor and up and down (1 = as fitted).
5. **Body Firmness** is how firmly the body follows the dance: the Person's own controller
   springs times this value (1 = unchanged, default 2). Stop puts the old springs back.

As a session plugin the player stays across scenes but takes nobody in a newly opened scene
until Play is pressed (then the only Person, or the first woman, unless a Dancer is chosen).

The three **Check** buttons write what the plugin measured to the VaM message log and to
`Saves/PluginData/VamMmd/check.txt`:

- *stand in the dance rest pose*: the character should stand in MMD's A-pose with nothing twisted;
- *write body report*: the measured skeleton, the scale and every controller found;
- *write tracking error*: how far each bone is from the target its controller holds right now.

## For triggers and other plugins

The plugin registers these names (they will not change). UIAssist buttons, Keybindings and scene
triggers can call them. The storable id is `plugin#N_VamMmd.Player`.

- actions: `Play`, `Pause`, `Toggle Play`, `Stop`, `Restart`, `Next Song`, `Previous Song`,
  `Seek Forward`, `Seek Back`
- bools: `Playing`, `Seeking` (true while the body is being placed after a jump), `Loop`, `Auto Next`
- floats: `Time` (seconds, can be set), `Speed`, `Volume`, `Audio Offset`, `Pelvis Travel`,
  `Body Firmness`
- choosers: `Dancer`, `Song`, `Motion`; urls: `Motion File`, `Audio File`; string: `Library Folder`

## Tests

```sh
sh tests/run.sh                                        # the core, no Unity or VaM needed (mono)
VAM_MANAGED=<VaM>/VaM_Data/Managed sh tests/compile_vam.sh   # compile check against the game
```

`tests/tools` holds a second, independent evaluator in Python (`mmdref.py`) and a script that
compares the C# skeleton with it on a real dance (`rigcmp.py`).

## Licence

MIT (see `LICENSE`). A VaM Hub package of the plugin is published as CC BY, the closest of
the licence types VaM offers for a `.var`. No code from other players is included; the
reference body in `RefBody.Tda` is a table of bone positions measured from a model, not the
model itself.
