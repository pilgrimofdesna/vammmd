using System;
using System.Collections.Generic;
using System.Text;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;
using VamMmd.Core;

namespace VamMmd
{
    // The plugin. Class name, namespace and the registered names below are what scenes, triggers
    // and other plugins (UIAssist buttons, Keybindings) bind to: do not rename them.
    //
    //   actions  Play, Pause, Toggle Play, Stop, Restart, Next Song, Previous Song, Seek Forward, Seek Back
    //   bools    Playing, Seeking (read), Loop, Auto Next
    //   floats   Time, Speed, Volume, Audio Offset, Pelvis Travel
    //   choosers Dancer, Song, Motion
    //   urls     Motion File, Audio File
    //   strings  Library Folder
    public class Player : MVRScript
    {
        private const string DataDir = "Saves/PluginData/VamMmd";
        private const string SongSettingsFile = DataDir + "/songs.json";
        private const string CheckFile = DataDir + "/check.txt";
        private const double SeekStep = 10;
        private const int ResetFrames = 12;         // a physics reset of the Person takes 10 rendered frames
        private const double BigJump = 0.5;         // seconds; smaller clock corrections do not reset physics

        private JSONStorableStringChooser _dancerChooser, _songChooser, _motionChooser;
        private JSONStorableString _libraryFolder, _status;
        private JSONStorableUrl _motionUrl, _audioUrl;
        private JSONStorableBool _playing, _seeking, _loop, _autoNext;
        private JSONStorableFloat _time, _speed, _volume, _offset, _pelvisTravel;
        private UIDynamicButton _playButton;

        private readonly DanceClock _clock = new DanceClock();
        private Music _music;
        private Dancer _dancer;
        private VmdFile _vmd;
        private MotionSampler _sampler;
        private string _motionProblem, _audioProblem, _lastError, _loadedAudio = "";
        private JSONClass _songSettings = new JSONClass();
        private bool _songSettingsDirty;

        private bool _hooked;
        private bool _libraryDirty = true, _dancersDirty = true, _needLoadMotion, _needLoadAudio;
        private bool _hasSeek, _jump, _ignoreAudio, _holdRest, _syncing, _resumeAfterLoad;
        private double _seekTo;
        private int _resetFrames;
        private float _statusAt;

        // ---- set-up -------------------------------------------------------

        public override void Init()
        {
            try
            {
                _music = new Music(transform);
                BuildParams();
                BuildUi();
                SuperController sc = SuperController.singleton;
                sc.onAtomAddedHandlers += OnAtomChanged;
                sc.onAtomRemovedHandlers += OnAtomRemoved;
                sc.onAtomUIDRenameHandlers += OnAtomRenamed;
                _hooked = true;
                LoadSongSettings();
            }
            catch (Exception e)
            {
                SuperController.LogError("VamMmd Player: could not start: " + e);
            }
        }

        private void BuildParams()
        {
            _dancerChooser = new JSONStorableStringChooser("Dancer", new List<string>(), "", "Dancer", OnDancerChosen);
            _dancerChooser.representsAtomUid = true;
            RegisterStringChooser(_dancerChooser);

            _libraryFolder = new JSONStorableString("Library Folder", "MMDdata", OnLibraryFolder);
            RegisterString(_libraryFolder);

            _songChooser = new JSONStorableStringChooser("Song", new List<string>(), new List<string>(), "", "Song", OnSongChosen);
            _songChooser.isStorable = false;
            RegisterStringChooser(_songChooser);

            _motionChooser = new JSONStorableStringChooser("Motion", new List<string>(), new List<string>(), "", "Motion", OnMotionChosen);
            _motionChooser.isStorable = false;
            RegisterStringChooser(_motionChooser);

            _motionUrl = new JSONStorableUrl("Motion File", "", OnMotionUrl, "vmd", "MMDdata");
            _motionUrl.allowFullComputerBrowse = false;
            RegisterUrl(_motionUrl);

            _audioUrl = new JSONStorableUrl("Audio File", "", OnAudioUrl, "wav", "MMDdata");
            _audioUrl.allowFullComputerBrowse = false;
            RegisterUrl(_audioUrl);

            _playing = new JSONStorableBool("Playing", false, OnPlayingSet);
            _playing.isStorable = false;
            RegisterBool(_playing);
            _seeking = new JSONStorableBool("Seeking", false);
            _seeking.isStorable = false;
            RegisterBool(_seeking);
            _loop = new JSONStorableBool("Loop", false);
            RegisterBool(_loop);
            _autoNext = new JSONStorableBool("Auto Next", false);
            RegisterBool(_autoNext);

            _time = new JSONStorableFloat("Time", 0f, OnTimeSet, 0f, 300f, true);
            _time.isStorable = false;
            RegisterFloat(_time);
            _speed = new JSONStorableFloat("Speed", 1f, 0.25f, 2f, true);
            RegisterFloat(_speed);
            _volume = new JSONStorableFloat("Volume", 0.8f, 0f, 1f, true);
            RegisterFloat(_volume);
            _offset = new JSONStorableFloat("Audio Offset", 0f, OnOffsetSet, -1f, 1f, false);
            _offset.isStorable = false;         // kept per song in Saves/PluginData/VamMmd/songs.json
            RegisterFloat(_offset);
            _pelvisTravel = new JSONStorableFloat("Pelvis Travel", 1f, 0.5f, 1.5f, true);
            RegisterFloat(_pelvisTravel);

            _status = new JSONStorableString("Status", "");
            _status.isStorable = false;

            RegisterAction(new JSONStorableAction("Play", Play));
            RegisterAction(new JSONStorableAction("Pause", Pause));
            RegisterAction(new JSONStorableAction("Toggle Play", TogglePlay));
            RegisterAction(new JSONStorableAction("Stop", Stop));
            RegisterAction(new JSONStorableAction("Restart", Restart));
            RegisterAction(new JSONStorableAction("Next Song", NextSong));
            RegisterAction(new JSONStorableAction("Previous Song", PreviousSong));
            RegisterAction(new JSONStorableAction("Seek Forward", SeekForward));
            RegisterAction(new JSONStorableAction("Seek Back", SeekBack));
        }

        private void BuildUi()
        {
            CreateFilterablePopup(_dancerChooser, false).popupPanelHeight = 500f;
            CreateFilterablePopup(_songChooser, false).popupPanelHeight = 800f;
            CreateScrollablePopup(_motionChooser, false).popupPanelHeight = 500f;
            Button("Pick a motion file (VMD)...", false, PickMotion);
            Button("Pick a music file (WAV)...", false, PickAudio);
            Button("Choose the library folder...", false, PickLibrary);
            CreateSpacer(false).height = 12f;
            _playButton = Button("Play", false, TogglePlay);
            _playButton.height = 80f;
            Button("Stop (give the body back)", false, Stop);
            Button("Restart", false, Restart);
            Button("Back 10 s", false, SeekBack);
            Button("Forward 10 s", false, SeekForward);
            Button("Previous song", false, PreviousSong);
            Button("Next song", false, NextSong);
            CreateSlider(_time, false);
            CreateToggle(_loop, false);
            CreateToggle(_autoNext, false);

            UIDynamicTextField text = CreateTextField(_status, true);
            text.height = 520f;
            CreateSlider(_speed, true);
            CreateSlider(_volume, true);
            UIDynamicSlider off = CreateSlider(_offset, true);
            off.label = "Audio Offset (s, + = music later)";
            off.valueFormat = "F3";
            CreateSlider(_pelvisTravel, true);
            CreateSpacer(true).height = 12f;
            Button("Measure the body again", true, Refit);
            Button("Check: stand in the dance rest pose", true, CheckRestPose);
            Button("Check: write body report", true, CheckBody);
            Button("Check: write tracking error", true, CheckTracking);
        }

        private UIDynamicButton Button(string label, bool right, UnityEngine.Events.UnityAction action)
        {
            UIDynamicButton b = CreateButton(label, right);
            b.button.onClick.AddListener(action);
            return b;
        }

        private void OnDisable()
        {
            try { StopInternal(true, true); } catch (Exception e) { Report(e); }
        }

        private void OnDestroy()
        {
            try { StopInternal(true, true); } catch (Exception) { }
            try
            {
                if (_hooked)
                {
                    SuperController sc = SuperController.singleton;
                    if (sc != null)
                    {
                        sc.onAtomAddedHandlers -= OnAtomChanged;
                        sc.onAtomRemovedHandlers -= OnAtomRemoved;
                        sc.onAtomUIDRenameHandlers -= OnAtomRenamed;
                    }
                    _hooked = false;
                }
                if (_music != null) _music.Destroy();
            }
            catch (Exception) { }
        }

        // ---- scene atoms --------------------------------------------------

        private void OnAtomChanged(Atom a) { _dancersDirty = true; }

        private void OnAtomRemoved(Atom a)
        {
            _dancersDirty = true;
            if (_dancer != null && _dancer.Person == a)
            {
                // the Person is going away: nothing to give back
                _dancer.Deactivate(false);
                _dancer = null;
                _clock.Playing = false;
                _holdRest = false;
            }
        }

        private void OnAtomRenamed(string from, string to)
        {
            _dancersDirty = true;
            if (_dancerChooser.val == from) _dancerChooser.valNoCallback = to;
        }

        private void RefreshDancers()
        {
            _dancersDirty = false;
            List<string> ids = new List<string>();
            List<Atom> atoms = SuperController.singleton.GetAtoms();
            for (int i = 0; i < atoms.Count; i++)
                if (atoms[i] != null && atoms[i].type == "Person") ids.Add(atoms[i].uid);
            _dancerChooser.choices = ids;
            if (_dancerChooser.val.Length == 0 || !ids.Contains(_dancerChooser.val))
            {
                // default: the Person this plugin sits on, else the first one in the scene
                string pick = "";
                if (containingAtom != null && containingAtom.type == "Person") pick = containingAtom.uid;
                else if (ids.Count > 0) pick = ids[0];
                if (pick != _dancerChooser.val && (pick.Length > 0 || !IsDancerAlive())) _dancerChooser.val = pick;
            }
        }

        private bool IsDancerAlive() { return _dancer != null && _dancer.Alive; }

        private void OnDancerChosen(string uid)
        {
            if (_dancer != null && _dancer.Person != null && _dancer.Person.uid == uid) return;
            StopInternal(true, true);
            _dancer = null;
        }

        // Finds the chosen Person and measures it. Returns false, with the reason in the status, when it cannot dance.
        private bool EnsureDancer(bool refit)
        {
            if (_dancer == null || !_dancer.Alive)
            {
                Atom a = _dancerChooser.val.Length > 0 ? SuperController.singleton.GetAtomByUid(_dancerChooser.val) : null;
                if (a == null || a.type != "Person") { _lastError = "Choose a Person to dance first."; return false; }
                _dancer = new Dancer(a);
                refit = true;
            }
            if (refit || _dancer.Rig == null)
            {
                FitOptions opt = new FitOptions();
                opt.PelvisTravel = _pelvisTravel.val;
                if (!_dancer.Fit(opt)) { _lastError = "The body could not be measured: " + _dancer.Problem; return false; }
            }
            return true;
        }

        // ---- library ------------------------------------------------------

        private void OnLibraryFolder(string v) { _libraryDirty = true; }

        private void RefreshLibrary()
        {
            _libraryDirty = false;
            List<string> folders = Library.SongFolders(_libraryFolder.val);
            List<string> names = new List<string>();
            for (int i = 0; i < folders.Count; i++) names.Add(Library.FileName(folders[i]));
            _songChooser.choices = folders;
            _songChooser.displayChoices = names;
            SyncSongChooser();
        }

        // Show which library song the current motion file belongs to, without reloading anything.
        private void SyncSongChooser()
        {
            string motion = Library.Norm(_motionUrl.val);
            string song = "";
            List<string> folders = _songChooser.choices;
            for (int i = 0; i < folders.Count; i++)
                if (motion.StartsWith(folders[i] + "/", StringComparison.OrdinalIgnoreCase) && folders[i].Length > song.Length) song = folders[i];
            _songChooser.valNoCallback = song;
            FillMotionChooser(song, motion);
        }

        private void FillMotionChooser(string songFolder, string current)
        {
            List<string> paths = new List<string>(), names = new List<string>();
            if (songFolder.Length > 0)
            {
                List<string> vmds = Library.Files(songFolder, ".vmd", 2);
                for (int i = 0; i < vmds.Count; i++)
                {
                    if (!IsBodyMotion(vmds[i])) continue;       // camera and face-only files are not dances
                    paths.Add(vmds[i]);
                    names.Add(vmds[i].Substring(songFolder.Length + 1));
                }
            }
            _motionChooser.choices = paths;
            _motionChooser.displayChoices = names;
            string match = "";
            for (int i = 0; i < paths.Count; i++)
                if (string.Equals(paths[i], current, StringComparison.OrdinalIgnoreCase)) match = paths[i];
            _motionChooser.valNoCallback = match;
        }

        private static bool IsBodyMotion(string path)
        {
            try { return VmdFile.PeekBoneKeyCount(FileManagerSecure.ReadAllBytes(path)) > 0; }
            catch (Exception) { return false; }
        }

        private void OnSongChosen(string folder)
        {
            if (_syncing || string.IsNullOrEmpty(folder)) return;
            FillMotionChooser(folder, "");
            List<string> motions = _motionChooser.choices;
            if (motions.Count == 0)
            {
                _lastError = "No dance motion in " + Library.FileName(folder) + " (only camera or face files).";
                return;
            }
            _motionChooser.valNoCallback = motions[0];
            SetSongFiles(motions[0]);
        }

        private void OnMotionChosen(string path)
        {
            if (_syncing || string.IsNullOrEmpty(path)) return;
            SetSongFiles(path);
        }

        // A motion and the music that lies next to it (same folder first, then the song folder).
        private void SetSongFiles(string motionPath)
        {
            _syncing = true;
            try
            {
                _motionUrl.val = motionPath;
                _audioUrl.val = FindAudioFor(motionPath);
            }
            finally { _syncing = false; }
            _needLoadMotion = true;
            _needLoadAudio = true;
        }

        private string FindAudioFor(string motionPath)
        {
            string folder = Library.Folder(motionPath);
            List<string> wavs = Library.Files(folder, ".wav", 0);
            if (wavs.Count == 0 && _songChooser.val.Length > 0 && folder.StartsWith(_songChooser.val, StringComparison.OrdinalIgnoreCase))
                wavs = Library.Files(_songChooser.val, ".wav", 2);
            return wavs.Count > 0 ? wavs[0] : "";
        }

        private void PickMotion() { _motionUrl.FileBrowse(); }
        private void PickAudio() { _audioUrl.FileBrowse(); }

        private void PickLibrary()
        {
            SuperController.singleton.GetDirectoryPathDialog(OnLibraryPicked, _libraryFolder.val, null, false);
        }

        private void OnLibraryPicked(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _libraryFolder.val = Library.Norm(SuperController.singleton.NormalizePath(path));
        }

        private void OnMotionUrl(string path)
        {
            _needLoadMotion = true;
            _libraryDirty = true;      // re-sync the song and motion choosers
            if (_syncing || insideRestore) return;      // a scene restore brings its own music path
            // picked by hand: the music is the WAV next to the motion, unless one in that folder is already set
            path = Library.Norm(path);
            string audio = Library.Norm(_audioUrl.val);
            if (path.Length > 0 && (audio.Length == 0 || Library.Folder(audio) != Library.Folder(path)))
            {
                string found = FindAudioFor(path);
                _syncing = true;
                try { _audioUrl.val = found; } finally { _syncing = false; }
                _needLoadAudio = true;
            }
        }

        private void OnAudioUrl(string path) { _needLoadAudio = true; }

        private void LoadMotion()
        {
            _needLoadMotion = false;
            bool wasPlaying = _clock.Playing || _resumeAfterLoad;
            _resumeAfterLoad = false;
            FlushSongSettings();
            _clock.Playing = false;
            _vmd = null;
            _sampler = null;
            _motionProblem = null;
            string path = Library.Norm(_motionUrl.val);
            if (path.Length > 0)
            {
                try
                {
                    VmdFile f = VmdFile.Parse(FileManagerSecure.ReadAllBytes(path));
                    if (f.BoneKeyCount == 0) _motionProblem = "this file holds no body motion (camera or face only)";
                    else { _vmd = f; _sampler = new MotionSampler(f); }
                }
                catch (Exception e)
                {
                    _motionProblem = e.Message;
                }
            }
            _offset.valNoCallback = SongOffset(path);
            SyncLength();
            _clock.Seek(0);
            _clock.Jumped = false;
            _music.Pause();
            _ignoreAudio = true;
            _holdRest = false;
            if (_sampler != null && wasPlaying) Play();
            else if (_sampler == null && _dancer != null && _dancer.Active) StopInternal(true, false);
            if (_dancer != null && _dancer.Active) _jump = true;        // another song: take its first pose cleanly
        }

        private void LoadAudio()
        {
            _needLoadAudio = false;
            _audioProblem = null;
            string path = Library.Norm(_audioUrl.val);
            if (path == _loadedAudio && path.Length > 0 && (_music.Ready || _music.Loading)) { SyncLength(); return; }
            _loadedAudio = "";
            if (path.Length == 0) { _music.Unload(); SyncLength(); return; }
            if (!Library.HasExtension(path, ".wav"))
            {
                _music.Unload();
                _audioProblem = "only WAV music is supported in this version";
                SyncLength();
                return;
            }
            try
            {
                byte[] bytes = FileManagerSecure.ReadAllBytes(path);
                _ignoreAudio = true;
                StartCoroutine(_music.Load(bytes, Library.FileName(path)));
                _loadedAudio = path;
            }
            catch (Exception e)
            {
                _music.Unload();
                _audioProblem = e.Message;
            }
            SyncLength();
        }

        private void SyncLength()
        {
            double len = _sampler != null ? _sampler.LengthSeconds : 0;
            if (_music != null && _music.Ready && _music.Length + _offset.val > len) len = _music.Length + _offset.val;
            _clock.Length = len;
            _time.max = len > 1 ? (float)len : 1f;
        }

        // ---- per-song settings --------------------------------------------

        private void LoadSongSettings()
        {
            try
            {
                if (!FileManagerSecure.FileExists(SongSettingsFile)) return;
                JSONNode n = JSON.Parse(FileManagerSecure.ReadAllText(SongSettingsFile));
                if (n != null && n.AsObject != null) _songSettings = n.AsObject;
            }
            catch (Exception e)
            {
                SuperController.LogError("VamMmd Player: could not read " + SongSettingsFile + ": " + e.Message);
            }
        }

        private float SongOffset(string motionPath)
        {
            if (motionPath.Length == 0 || !_songSettings.HasKey(motionPath)) return 0f;
            JSONNode n = _songSettings[motionPath]["offset"];
            return n != null ? Mathf.Clamp(n.AsFloat, -5f, 5f) : 0f;
        }

        private void OnOffsetSet(float v)
        {
            string path = Library.Norm(_motionUrl.val);
            if (path.Length == 0) return;
            if (!_songSettings.HasKey(path)) _songSettings[path] = new JSONClass();
            _songSettings[path]["offset"].AsFloat = v;
            _songSettingsDirty = true;
            SyncLength();
            _music.Pause();             // restarted at the shifted position on the next frame
            _ignoreAudio = true;
        }

        private void FlushSongSettings()
        {
            if (!_songSettingsDirty) return;
            _songSettingsDirty = false;
            try
            {
                if (!FileManagerSecure.DirectoryExists(DataDir)) FileManagerSecure.CreateDirectory(DataDir);
                FileManagerSecure.WriteAllText(SongSettingsFile, _songSettings.ToString(""));
            }
            catch (Exception e)
            {
                SuperController.LogError("VamMmd Player: could not save " + SongSettingsFile + ": " + e.Message);
            }
        }

        // ---- transport ----------------------------------------------------

        private void Play()
        {
            _lastError = null;
            if (_needLoadMotion) LoadMotion();
            if (_sampler == null)
            {
                _lastError = _motionProblem != null ? "This motion cannot be played: " + _motionProblem : "Choose a song first.";
                return;
            }
            if (!EnsureDancer(_dancer == null || !_dancer.Active)) return;
            bool fresh = !_dancer.Active || _holdRest;
            _holdRest = false;
            if (_clock.Length > 0 && _clock.Time >= _clock.Length - 0.02) { _clock.Seek(0); _clock.Jumped = false; fresh = true; }
            _dancer.Activate();
            _clock.Ended = false;
            _clock.Playing = true;
            _music.Pause();
            _ignoreAudio = true;
            if (fresh) _jump = true;    // go to the dance pose cleanly instead of being dragged there
        }

        private void Pause()
        {
            _clock.Playing = false;
            if (_music != null) _music.Pause();
        }

        private void TogglePlay()
        {
            if (_clock.Playing) Pause(); else Play();
        }

        private void Stop() { StopInternal(true, false); }

        // Stops and, when the body was being driven, puts it back as it was before the dance.
        // Normally in two steps: the controllers go back to their old pose while still holding, the
        // body is snapped onto them, and only then they get their old on/off states back (a
        // controller that is off cannot be snapped to). immediate does it all at once, for when
        // the plugin itself is going away.
        private void StopInternal(bool restore, bool immediate)
        {
            _clock.Playing = false;
            _holdRest = false;
            _hasSeek = false;
            _jump = false;
            if (_music != null) _music.Pause();
            if (_dancer != null && _dancer.Active)
            {
                if (!restore) _dancer.Deactivate(false);
                else if (immediate)
                {
                    _dancer.Deactivate(true);
                    try { _dancer.SnapPhysics(); } catch (Exception) { }
                }
                else
                {
                    _dancer.BeginRestore();
                    _jump = true;
                }
            }
            else if (_dancer != null && _dancer.Restoring && immediate) _dancer.FinishRestore();
            _clock.Seek(0);
            _clock.Jumped = false;
            FlushSongSettings();
        }

        private void Restart()
        {
            RequestSeek(0);
            if (!_clock.Playing) Play();
        }

        private void SeekForward() { RequestSeek(_clock.Time + SeekStep); }
        private void SeekBack() { RequestSeek(_clock.Time - SeekStep); }

        private void RequestSeek(double t)
        {
            _seekTo = t;
            _hasSeek = true;
        }

        private void OnTimeSet(float v) { RequestSeek(v); }

        private void OnPlayingSet(bool v)
        {
            if (v == _clock.Playing) return;
            if (v) Play(); else Pause();
        }

        private void NextSong() { StepSong(1); }
        private void PreviousSong() { StepSong(-1); }

        private void StepSong(int dir)
        {
            if (_libraryDirty) RefreshLibrary();
            List<string> songs = _songChooser.choices;
            if (songs.Count == 0) { _lastError = "The library folder has no songs: " + _libraryFolder.val; return; }
            int i = songs.IndexOf(_songChooser.val);
            // skip folders without a dance; give up after one full round
            for (int tries = 0; tries < songs.Count; tries++)
            {
                i = ((i < 0 ? (dir > 0 ? -1 : 0) : i) + dir + songs.Count) % songs.Count;
                FillMotionChooser(songs[i], "");
                if (_motionChooser.choices.Count == 0) continue;
                _resumeAfterLoad = _resumeAfterLoad || _clock.Playing;
                _songChooser.valNoCallback = songs[i];
                _motionChooser.valNoCallback = _motionChooser.choices[0];
                SetSongFiles(_motionChooser.choices[0]);
                return;
            }
            _lastError = "No song in the library has a dance motion.";
        }

        // ---- the frame loop -----------------------------------------------

        private void Update()
        {
            try
            {
                if (_music == null) return;
                if (_dancersDirty) RefreshDancers();
                if (_libraryDirty) RefreshLibrary();
                if (_needLoadMotion) LoadMotion();
                if (_needLoadAudio) LoadAudio();

                SuperController sc = SuperController.singleton;
                bool frozen = (sc != null && sc.freezeAnimation) || Time.timeScale < 0.01f;
                if (_resetFrames > 0) _resetFrames--;

                if (_clock.Ended)
                {
                    // the clock stopped itself at the end of the song
                    _clock.Ended = false;
                    if (_loop.val) { _clock.Playing = true; RequestSeek(0); }
                    else if (_autoNext.val) { _resumeAfterLoad = true; NextSong(); }
                }

                if (_hasSeek)
                {
                    _hasSeek = false;
                    _clock.Seek(_seekTo);
                    _clock.Jumped = false;
                    _music.Pause();
                    _ignoreAudio = true;
                    if (_dancer != null && _dancer.Active && !_holdRest)
                    {
                        ApplyPose(_clock.Time);     // targets move now; the body is snapped as soon as a reset may run
                        _jump = true;
                    }
                }
                if (_jump && _resetFrames == 0)
                {
                    _jump = false;
                    if (_dancer != null && (_dancer.Active || _dancer.Restoring))
                    {
                        if (_dancer.Active) { if (_holdRest) ApplyRest(); else ApplyPose(_clock.Time); }
                        _dancer.SnapPhysics();
                        _resetFrames = ResetFrames;
                    }
                }
                else if (_resetFrames == 0 && _dancer != null && _dancer.Restoring)
                {
                    _dancer.FinishRestore();    // the body is back in its old pose: hand the controllers back
                }

                SyncMusic(frozen);
                if (!_music.Loading && _clock.Length <= 0.001 && (_sampler != null || _music.Ready)) SyncLength();
                else if (_music.Ready && _music.Length + _offset.val > _clock.Length + 0.01) SyncLength();

                _time.valNoCallback = (float)_clock.Time;
                if (_playing.val != _clock.Playing) _playing.valNoCallback = _clock.Playing;
                bool seeking = _resetFrames > 0 || _jump;
                if (_seeking.val != seeking) _seeking.valNoCallback = seeking;
                if (Time.unscaledTime >= _statusAt)
                {
                    _statusAt = Time.unscaledTime + 0.25f;
                    ShowStatus();
                }
            }
            catch (Exception e) { Report(e); }
        }

        // The music follows the dance clock: started, paused and repositioned from here only.
        private void SyncMusic(bool frozen)
        {
            if (!_clock.Playing || frozen || !_music.Ready)
            {
                _music.Pause();
                return;
            }
            _music.Volume = _volume.val;
            _music.Pitch = _speed.val * Time.timeScale;
            if (!_music.IsPlaying)
            {
                double pos = _clock.Time - _offset.val;
                if (pos >= 0 && pos < _music.Length - 0.05) _music.Play(pos);
            }
            _ignoreAudio = false;
        }

        private void FixedUpdate()
        {
            try
            {
                if (_dancer == null || !_dancer.Active) return;
                SuperController sc = SuperController.singleton;
                if (sc != null && sc.freezeAnimation) return;       // frozen by the user, loading, or physics paused
                if (_holdRest) { ApplyRest(); return; }
                if (_sampler == null || !_clock.Playing) return;

                double dt = Time.fixedDeltaTime;
                _clock.Speed = _speed.val;
                double expected = _clock.Time + dt * _clock.Speed;
                double audio = !_ignoreAudio && _music.IsPlaying ? _music.Time + _offset.val : double.NaN;
                _clock.Jumped = false;
                _clock.Step(dt, audio);
                if (_clock.Jumped)
                {
                    _clock.Jumped = false;
                    // The music is ahead or behind by more than the clock will glide over. A small
                    // correction (a frame hitch) just moves the targets; a large one snaps the body.
                    if (Math.Abs(_clock.Time - expected) > BigJump) _jump = true;
                }
                ApplyPose(_clock.Time);
            }
            catch (Exception e) { Report(e); }
        }

        private void ApplyPose(double seconds)
        {
            if (_dancer == null || _dancer.Rig == null || _sampler == null) return;
            _dancer.Rig.PelvisTravel = _pelvisTravel.val;
            _sampler.Sample(seconds, _dancer.Pose);
            _dancer.Rig.Evaluate(_dancer.Pose);
            _dancer.Write(_dancer.Pose);
        }

        // The rest pose of the dance skeleton on this body (arms down in MMD's A-pose): nothing keyed.
        private void ApplyRest()
        {
            if (_dancer == null || _dancer.Rig == null) return;
            RigPose p = _dancer.Pose;
            for (int i = 0; i < B.Count; i++) { p.AnimRot[i] = Q.Identity; p.AnimPos[i] = V3.Zero; }
            p.LegIkOn[0] = p.LegIkOn[1] = true;
            p.ToeIkOn[0] = p.ToeIkOn[1] = true;
            _dancer.Rig.Evaluate(p);
            _dancer.Write(p);
        }

        // ---- checks (results go to the message log and to Saves/PluginData/VamMmd/check.txt) ----

        private void Refit()
        {
            _lastError = null;
            if (EnsureDancer(true) && _dancer.Active) _jump = true;
        }

        private void CheckRestPose()
        {
            _lastError = null;
            if (!EnsureDancer(_dancer == null || !_dancer.Active)) return;
            Pause();
            _dancer.Activate();
            _holdRest = true;
            _jump = true;
        }

        private void CheckBody()
        {
            _lastError = null;
            if (!EnsureDancer(_dancer == null || !_dancer.Active)) return;
            WriteCheck("BODY REPORT\n" + _dancer.FitText() + "\n\n" + _dancer.BodyReport());
        }

        private void CheckTracking()
        {
            _lastError = null;
            if (_dancer == null || !_dancer.Active) { _lastError = "Start a dance or the rest-pose check first."; return; }
            string head = "TRACKING ERROR at " + _clock.Time.ToString("0.00") + " s" + (_holdRest ? " (rest pose)" : _clock.Playing ? " (playing)" : " (paused)")
                + ", physics " + (1f / Time.fixedDeltaTime).ToString("0") + " Hz\n";
            WriteCheck(head + _dancer.TrackingReport());
        }

        private void WriteCheck(string text)
        {
            SuperController.LogMessage("VamMmd Player\n" + text);
            try
            {
                if (!FileManagerSecure.DirectoryExists(DataDir)) FileManagerSecure.CreateDirectory(DataDir);
                string old = FileManagerSecure.FileExists(CheckFile) ? FileManagerSecure.ReadAllText(CheckFile) : "";
                if (old.Length > 200000) old = "";
                FileManagerSecure.WriteAllText(CheckFile, old + "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\n" + text + "\n");
            }
            catch (Exception e)
            {
                SuperController.LogError("VamMmd Player: could not write " + CheckFile + ": " + e.Message);
            }
        }

        // ---- status -------------------------------------------------------

        private static string Clock(double s)
        {
            if (s < 0 || double.IsNaN(s)) s = 0;
            int t = (int)s;
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        private void ShowStatus()
        {
            StringBuilder sb = new StringBuilder();
            string state = _holdRest ? "Rest-pose check" : _clock.Playing ? "Playing" : (_dancer != null && _dancer.Active ? "Paused" : "Stopped");
            sb.Append(state).Append("  ").Append(Clock(_clock.Time)).Append(" / ").Append(Clock(_clock.Length)).Append('\n');
            if (_lastError != null) sb.Append("! ").Append(_lastError).Append('\n');
            sb.Append('\n');

            if (_motionUrl.val.Length == 0) sb.Append("Motion: none chosen\n");
            else if (_motionProblem != null) sb.Append("Motion: ").Append(Library.FileName(_motionUrl.val)).Append("\n! ").Append(_motionProblem).Append('\n');
            else if (_vmd != null)
            {
                sb.Append("Motion: ").Append(Library.FileName(_motionUrl.val)).Append('\n');
                sb.Append(_vmd.BoneKeyCount).Append(" keys, ").Append(Clock(_sampler.LengthSeconds)).Append(", made for \"").Append(_vmd.ModelName).Append("\"\n");
                sb.Append(_sampler.BoundBones).Append(" body bones used");
                if (_sampler.Unknown.Count > 0) sb.Append(", ").Append(_sampler.Unknown.Count).Append(" model-specific bones ignored");
                sb.Append('\n');
                if (_vmd.Warning != null) sb.Append("! ").Append(_vmd.Warning).Append('\n');
            }

            if (_audioUrl.val.Length == 0) sb.Append("Music: none (the dance plays silently)\n");
            else if (_audioProblem != null) sb.Append("Music: ").Append(Library.FileName(_audioUrl.val)).Append("\n! ").Append(_audioProblem).Append('\n');
            else if (_music.Error != null) sb.Append("Music: ").Append(Library.FileName(_audioUrl.val)).Append("\n! ").Append(_music.Error).Append('\n');
            else if (_music.Loading) sb.Append("Music: loading ").Append((_music.Progress * 100).ToString("0")).Append("%\n");
            else if (_music.Ready) sb.Append("Music: ").Append(Library.FileName(_audioUrl.val)).Append('\n').Append(Clock(_music.Length)).Append(", ").Append(_music.Info).Append('\n');

            sb.Append('\n');
            if (_dancer != null) sb.Append(_dancer.FitText()).Append('\n');
            else sb.Append("Body: measured when the dance starts\n");
            if (_songChooser.choices.Count == 0) sb.Append("\nNo songs found in the library folder \"").Append(_libraryFolder.val).Append("\".\n");

            string text = sb.ToString();
            if (_status.val != text) _status.val = text;
            if (_playButton != null)
            {
                string label = _clock.Playing ? "Pause" : "Play";
                if (_playButton.label != label) _playButton.label = label;
            }
        }

        private void Report(Exception e)
        {
            string msg = e.Message;      // not GetType().Name: that member lives in System.Reflection, which VaM refuses
            if (msg == _lastError) return;
            _lastError = msg;
            SuperController.LogError("VamMmd Player: " + e);
        }
    }
}
