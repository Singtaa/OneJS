using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS.Audio {
    /// <summary>
    /// Sound, from JavaScript, on every platform OneJS runs on.
    ///
    /// WHY THIS EXISTS RATHER THAN WEBAUDIO
    ///
    /// WebAudio is the obvious way to make noise from JS and it only exists in a
    /// browser, so a game built on it can never leave the web. That is the exact
    /// failure the portability contract is there to prevent, and sound is too
    /// ordinary a need to answer with "do not". This is the seam: AudioSource
    /// underneath, one API above, the same behaviour on WebGL and QuickJS.
    ///
    /// SHAPE
    ///
    /// Per DESIGN.md, the cost here is per *event*, not per frame or per object.
    /// Loading a clip crosses once and hands back a handle. Playing is a single
    /// call with primitives, no allocation, and returns a voice id the caller
    /// can forget about. Nothing calls into JavaScript while a sound plays.
    ///
    /// Sources are pooled. Creating a GameObject per sound effect would churn
    /// the heap on exactly the frames a game is busiest, which is when a player
    /// notices.
    /// </summary>
    public static class AudioBridge {
        /// <summary>
        /// How many sounds can play at once.
        ///
        /// Beyond this the oldest non-looping voice is taken, which is what a
        /// player expects: a new gunshot matters more than the tail of an old
        /// one. Looping voices are never stolen, because music going silent is
        /// far more noticeable than one effect being cut short.
        /// </summary>
        const int VoiceCount = 24;

        static GameObject _host;
        static AudioSource[] _voices;
        static int[] _voiceSerial;      // which play a voice is currently serving
        static bool[] _voiceLooping;
        static int _nextSerial = 1;
        static int _roundRobin;

        static readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
        static int _nextClipHandle = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() {
            // Domain reload and play-mode entry both land here. The host object
            // is not carried across, so the pool has to be rebuilt rather than
            // reused, or every voice is a destroyed AudioSource.
            _host = null;
            _voices = null;
            _voiceSerial = null;
            _voiceLooping = null;
            _nextSerial = 1;
            _roundRobin = 0;
            _clips.Clear();
            _nextClipHandle = 1;
        }

        static void EnsurePool() {
            if (_voices != null && _host != null) return;

            _host = new GameObject("OneJS Audio");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;

            _voices = new AudioSource[VoiceCount];
            _voiceSerial = new int[VoiceCount];
            _voiceLooping = new bool[VoiceCount];
            for (var i = 0; i < VoiceCount; i++) {
                var source = _host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                // 2D by default. A UI game has no listener position for a 3D
                // pan to be relative to, so spatialising would only ever make
                // sounds quieter for no reason.
                source.spatialBlend = 0f;
                _voices[i] = source;
            }

            // Nothing is audible without a listener, and a scene built for UI
            // often has no camera at all.
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) {
                _host.AddComponent<AudioListener>();
            }
        }

        // ============ clips ============

        /// <summary>
        /// Loads a clip and returns its handle.
        ///
        /// Returns a Task, which the bridge turns into a JS Promise, so the
        /// caller writes `await oj.audio.load(url)` and nothing polls.
        /// </summary>
        public static async Task<int> LoadClip(string url) {
            EnsurePool();
            using var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioTypeFor(url));
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (request.result != UnityWebRequest.Result.Success) {
                throw new Exception($"could not load audio from {url}: {request.error}");
            }
            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null) throw new Exception($"{url} did not decode to an audio clip");

            var handle = _nextClipHandle++;
            _clips[handle] = clip;
            return handle;
        }

        /// <summary>
        /// The decoder to ask for, inferred from the extension.
        ///
        /// UNKNOWN works on desktop, where the decoder sniffs the bytes, and
        /// does not on WebGL, where the browser needs telling. Guessing from
        /// the URL is the only information available before the download.
        /// </summary>
        static AudioType AudioTypeFor(string url) {
            var path = url;
            var query = path.IndexOf('?');
            if (query >= 0) path = path.Substring(0, query);
            if (path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return AudioType.OGGVORBIS;
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return AudioType.WAV;
            if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return AudioType.MPEG;
            if (path.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".aif", StringComparison.OrdinalIgnoreCase)) return AudioType.AIFF;
            return AudioType.UNKNOWN;
        }

        public static void UnloadClip(int clip) {
            if (!_clips.TryGetValue(clip, out var loaded)) return;
            // Stop anything still playing it first, or the source keeps a
            // reference to a destroyed clip and logs on the next frame.
            if (_voices != null) {
                for (var i = 0; i < _voices.Length; i++) {
                    if (_voices[i] != null && _voices[i].clip == loaded) StopVoiceAt(i);
                }
            }
            _clips.Remove(clip);
            UnityEngine.Object.Destroy(loaded);
        }

        public static int GetClipCount() => _clips.Count;

        public static float GetClipLength(int clip) =>
            _clips.TryGetValue(clip, out var loaded) && loaded != null ? loaded.length : 0f;

        // ============ playing ============

        /// <summary>
        /// Plays a clip once. Returns a voice id, or 0 if the clip is unknown.
        ///
        /// The id is a serial rather than a slot index, so holding one after the
        /// voice has been reused cannot silence a later, unrelated sound.
        /// </summary>
        public static int Play(int clip, float volume, float pitch) => Start(clip, volume, pitch, false);

        /// <summary>Plays a clip until stopped. For music and ambience.</summary>
        public static int PlayLooping(int clip, float volume, float pitch) => Start(clip, volume, pitch, true);

        static int Start(int clip, float volume, float pitch, bool loop) {
            if (!_clips.TryGetValue(clip, out var loaded) || loaded == null) return 0;
            EnsurePool();

            var slot = TakeVoice();
            if (slot < 0) return 0;

            var source = _voices[slot];
            source.clip = loaded;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            source.loop = loop;
            source.Play();

            _voiceLooping[slot] = loop;
            var serial = _nextSerial++;
            _voiceSerial[slot] = serial;
            return serial;
        }

        /// <summary>A free slot, or the oldest non-looping one, or -1.</summary>
        static int TakeVoice() {
            for (var i = 0; i < _voices.Length; i++) {
                var index = (_roundRobin + i) % _voices.Length;
                if (_voices[index] != null && !_voices[index].isPlaying) {
                    _roundRobin = (index + 1) % _voices.Length;
                    return index;
                }
            }
            // Everything is busy. Steal the oldest one-shot; never music.
            var oldest = -1;
            var oldestSerial = int.MaxValue;
            for (var i = 0; i < _voices.Length; i++) {
                if (_voiceLooping[i]) continue;
                if (_voiceSerial[i] < oldestSerial) {
                    oldestSerial = _voiceSerial[i];
                    oldest = i;
                }
            }
            return oldest;
        }

        static int SlotOf(int voice) {
            if (voice <= 0 || _voiceSerial == null) return -1;
            for (var i = 0; i < _voiceSerial.Length; i++) {
                if (_voiceSerial[i] == voice) return i;
            }
            return -1;
        }

        static void StopVoiceAt(int slot) {
            var source = _voices[slot];
            if (source == null) return;
            source.Stop();
            source.clip = null;
            _voiceSerial[slot] = 0;
            _voiceLooping[slot] = false;
        }

        public static void Stop(int voice) {
            var slot = SlotOf(voice);
            if (slot >= 0) StopVoiceAt(slot);
        }

        public static void StopAll() {
            if (_voices == null) return;
            for (var i = 0; i < _voices.Length; i++) StopVoiceAt(i);
        }

        public static bool IsPlaying(int voice) {
            var slot = SlotOf(voice);
            return slot >= 0 && _voices[slot] != null && _voices[slot].isPlaying;
        }

        public static void SetVoiceVolume(int voice, float volume) {
            var slot = SlotOf(voice);
            if (slot >= 0 && _voices[slot] != null) _voices[slot].volume = Mathf.Clamp01(volume);
        }

        public static void SetVoicePitch(int voice, float pitch) {
            var slot = SlotOf(voice);
            if (slot >= 0 && _voices[slot] != null) _voices[slot].pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        }

        public static void PauseVoice(int voice, bool paused) {
            var slot = SlotOf(voice);
            if (slot < 0 || _voices[slot] == null) return;
            if (paused) _voices[slot].Pause();
            else _voices[slot].UnPause();
        }

        // ============ global ============

        /// <summary>
        /// Everything at once, through the listener, so one call mutes a game
        /// rather than one per voice.
        /// </summary>
        public static void SetMasterVolume(float volume) => AudioListener.volume = Mathf.Clamp01(volume);

        public static float GetMasterVolume() => AudioListener.volume;

        public static void SetPaused(bool paused) => AudioListener.pause = paused;

        public static int GetVoiceCount() => VoiceCount;

        public static int GetActiveVoiceCount() {
            if (_voices == null) return 0;
            var active = 0;
            for (var i = 0; i < _voices.Length; i++) {
                if (_voices[i] != null && _voices[i].isPlaying) active++;
            }
            return active;
        }

        /// <summary>Drops every clip and voice. Called on teardown and hot reload.</summary>
        public static void Dispose() {
            StopAll();
            foreach (var clip in _clips.Values) {
                if (clip != null) UnityEngine.Object.Destroy(clip);
            }
            _clips.Clear();
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            _voices = null;
            _voiceSerial = null;
            _voiceLooping = null;
        }
    }
}
