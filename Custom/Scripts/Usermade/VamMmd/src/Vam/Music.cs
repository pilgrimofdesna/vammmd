using System;
using System.Collections;
using UnityEngine;
using VamMmd.Core;

namespace VamMmd
{
    // The song's audio on a source of our own. The WAV is decoded here, so nothing is added to
    // the scene's audio list and no other sound in the scene is touched.
    internal sealed class Music
    {
        private readonly GameObject _go;
        private readonly AudioSource _src;
        private AudioClip _clip;
        private int _generation;

        public bool Loading;
        public float Progress;
        public string Error;            // why the last load failed, or null
        public string Info = "";
        public double Length;           // seconds; 0 when nothing is loaded

        public Music(Transform parent)
        {
            _go = new GameObject("VamMmd.Music");
            _go.transform.SetParent(parent, false);
            _src = _go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            _src.spatialBlend = 0f;
            _src.dopplerLevel = 0f;
        }

        public bool Ready { get { return _clip != null && !Loading; } }
        public bool IsPlaying { get { return _clip != null && _src.isPlaying; } }

        // Position of the playing music in seconds. It moves in steps of one audio buffer.
        public double Time { get { return _clip == null ? 0 : _src.timeSamples / (double)_clip.frequency; } }

        public float Volume { set { _src.volume = Mathf.Clamp01(value); } }
        public float Pitch { set { _src.pitch = Mathf.Clamp(value, 0.05f, 3f); } }

        public void Play(double seconds)
        {
            if (_clip == null) return;
            int at = (int)(seconds * _clip.frequency);
            if (at < 0) at = 0;
            if (at >= _clip.samples - 1) return;
            _src.Stop();
            _src.timeSamples = at;
            _src.Play();
        }

        public void Pause()
        {
            if (_clip != null && _src.isPlaying) _src.Pause();
        }

        public void Unload()
        {
            _generation++;
            Loading = false;
            Progress = 0;
            Length = 0;
            Info = "";
            if (_src != null) { _src.Stop(); _src.clip = null; }
            if (_clip != null) { UnityEngine.Object.Destroy(_clip); _clip = null; }
        }

        public void Destroy()
        {
            Unload();
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }

        private static WavFile TryParse(byte[] bytes, out string error)
        {
            error = null;
            try { return WavFile.Parse(bytes); }
            catch (Exception e) { error = e.Message; return null; }
        }

        // Decodes a WAV into a clip a slice per rendered frame, so a long song does not freeze the picture.
        public IEnumerator Load(byte[] bytes, string name)
        {
            Unload();
            int gen = _generation;
            Error = null;
            string problem;
            WavFile wav = TryParse(bytes, out problem);
            if (wav == null) { Error = problem; yield break; }
            Loading = true;
            AudioClip clip = AudioClip.Create(name, wav.Frames, wav.Channels, wav.SampleRate, false);
            const int Chunk = 262144;
            float[] buf = new float[Chunk * wav.Channels];
            int done = 0;
            while (done < wav.Frames)
            {
                int n = Math.Min(Chunk, wav.Frames - done);
                float[] dst = n == Chunk ? buf : new float[n * wav.Channels];
                wav.Decode(done, n, dst);
                clip.SetData(dst, done);
                done += n;
                Progress = done / (float)wav.Frames;
                yield return null;
                if (gen != _generation)
                {
                    UnityEngine.Object.Destroy(clip);
                    yield break;
                }
            }
            _clip = clip;
            _src.clip = clip;
            Length = wav.LengthSeconds;
            Info = wav.SampleRate + " Hz, " + wav.Channels + " ch, " + wav.BitsPerSample + " bit";
            Loading = false;
        }
    }
}
