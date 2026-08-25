using System.Collections.Generic;
using UnityEngine;

namespace Shared.Audio
{
    /// <summary>
    /// Playback layer for the procedural bank. Owns a fixed pool of <see cref="AudioSource"/>s
    /// (no Instantiate/Destroy churn) and encodes the three rules the reference audio showed:
    /// important events are two layered, repeats climb in pitch while dropping in level, and
    /// playback is never tied to <see cref="Time.timeScale"/> so hitstop does not slow the sound.
    /// Everything is static; the singleton host object is created on first use.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioService : MonoBehaviour
    {
        /// <summary>Number of one shot voices; more than enough for a ripple burst plus a loop.</summary>
        public const int PoolSize = 16;

        /// <summary>Default gap before the second layer of a two layered event, in real seconds.</summary>
        public const float DefaultLayerDelay = 0.12f;

        /// <summary>Pitch multiplier added per repeat index (+4%).</summary>
        public const float RepeatPitchStep = 1.04f;

        /// <summary>Amplitude multiplier applied per repeat index (-35%).</summary>
        public const float RepeatGainStep = 0.65f;

        /// <summary>Random pitch spread applied to every one shot so stacked plays do not sound mechanical.</summary>
        public const float PitchJitter = 0.03f;

        static AudioService _instance;
        static float _masterVolume = 1f;
        static bool _muted;

        readonly List<AudioSource> _pool = new List<AudioSource>(PoolSize);
        readonly Dictionary<SfxId, AudioSource> _loops = new Dictionary<SfxId, AudioSource>();
        int _next;

        /// <summary>Global level applied on top of every per call volume, 0..1.</summary>
        public static float MasterVolume
        {
            get { return _masterVolume; }
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                ApplyMixToLoops();
            }
        }

        /// <summary>Silences everything without losing the current volume setting.</summary>
        public static bool Muted
        {
            get { return _muted; }
            set
            {
                _muted = value;
                ApplyMixToLoops();
            }
        }

        /// <summary>Flips <see cref="Muted"/> and returns the new state (handy for a UI button).</summary>
        public static bool ToggleMute()
        {
            Muted = !Muted;
            return _muted;
        }

        /// <summary>Synthesises the whole bank now, so no sequence pays synthesis cost on its first hit.</summary>
        public static void Prewarm()
        {
            SfxLibrary.Prewarm();
            Ensure();
        }

        /// <summary>Plays a one shot. Returns the voice, or null outside play mode.</summary>
        public static AudioSource Play(SfxId id, float volume = 1f, float pitch = 1f)
        {
            var svc = Ensure();
            if (svc == null) return null;
            var src = svc.Acquire();
            if (src == null) return null;
            src.clip = SfxLibrary.GetClip(id);
            src.loop = false;
            src.pitch = Mathf.Clamp(pitch * (1f + Random.Range(-PitchJitter, PitchJitter)), 0.1f, 3f);
            src.volume = Mix(volume);
            src.Play();
            return src;
        }

        /// <summary>
        /// Two layer play: the main hit now, a second accent <paramref name="delay"/> seconds later.
        /// The delay runs on the audio clock (<see cref="AudioSource.PlayDelayed"/>), so it stays
        /// correct while a hitstop is holding <see cref="Time.timeScale"/> near zero.
        /// </summary>
        public static void PlayLayered(SfxId main, SfxId layer, float delay = DefaultLayerDelay, float volume = 1f, float layerVolume = 0.7f)
        {
            Play(main, volume);

            var svc = Ensure();
            if (svc == null) return;
            var src = svc.Acquire();
            if (src == null) return;
            src.clip = SfxLibrary.GetClip(layer);
            src.loop = false;
            src.pitch = Mathf.Clamp(1f + Random.Range(-PitchJitter, PitchJitter), 0.1f, 3f);
            src.volume = Mix(volume * layerVolume);
            src.PlayDelayed(Mathf.Max(0f, delay));
        }

        /// <summary>
        /// Plays the <paramref name="index"/>-th repeat of a sound: pitch rises 4% and level drops
        /// 35% per step, which is how the reference ripple and debris series were measured.
        /// Index 0 is the first, unmodified hit.
        /// </summary>
        public static AudioSource PlayRepeat(SfxId id, int index, float volume = 1f)
        {
            int i = Mathf.Max(0, index);
            return Play(id, volume * Mathf.Pow(RepeatGainStep, i), Mathf.Pow(RepeatPitchStep, i));
        }

        /// <summary>Starts (or restarts) a looping bed such as <see cref="SfxId.CrowdAmbience"/>.</summary>
        public static AudioSource PlayLoop(SfxId id, float volume = 1f)
        {
            var svc = Ensure();
            if (svc == null) return null;

            AudioSource src;
            if (!svc._loops.TryGetValue(id, out src) || src == null)
            {
                src = svc.CreateSource("SfxLoop_" + id);
                svc._loops[id] = src;
            }
            src.clip = SfxLibrary.GetClip(id);
            src.loop = true;
            src.pitch = 1f;
            src.volume = Mix(volume);
            svc._loopVolumes[id] = volume;
            if (!src.isPlaying) src.Play();
            return src;
        }

        /// <summary>Stops a looping bed started by <see cref="PlayLoop"/>.</summary>
        public static void StopLoop(SfxId id)
        {
            if (_instance == null) return;
            AudioSource src;
            if (_instance._loops.TryGetValue(id, out src) && src != null) src.Stop();
        }

        /// <summary>Stops every voice, one shots and loops alike.</summary>
        public static void StopAll()
        {
            if (_instance == null) return;
            for (int i = 0; i < _instance._pool.Count; i++)
            {
                if (_instance._pool[i] != null) _instance._pool[i].Stop();
            }
            foreach (var kv in _instance._loops)
            {
                if (kv.Value != null) kv.Value.Stop();
            }
        }

        // ------------------------------------------------------------ internals

        readonly Dictionary<SfxId, float> _loopVolumes = new Dictionary<SfxId, float>();

        static float Mix(float volume)
        {
            return _muted ? 0f : Mathf.Clamp01(volume) * _masterVolume;
        }

        static void ApplyMixToLoops()
        {
            if (_instance == null) return;
            foreach (var kv in _instance._loops)
            {
                if (kv.Value == null) continue;
                float v;
                if (!_instance._loopVolumes.TryGetValue(kv.Key, out v)) v = 1f;
                kv.Value.volume = Mix(v);
            }
        }

        static AudioService Ensure()
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null; // edit mode tools use SfxLibrary directly
            var go = new GameObject("[AudioService]");
            _instance = go.AddComponent<AudioService>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            for (int i = 0; i < PoolSize; i++) _pool.Add(CreateSource("SfxVoice_" + i));
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        AudioSource CreateSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;       // 2D: these are UI/feedback sounds, not world sources
            src.bypassReverbZones = true;
            src.ignoreListenerPause = true; // keeps feedback audible through a hitstop or listener pause
            src.dopplerLevel = 0f;
            return src;
        }

        AudioSource Acquire()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[(_next + i) % _pool.Count];
                if (s != null && !s.isPlaying)
                {
                    _next = (_next + i + 1) % _pool.Count;
                    return s;
                }
            }
            // All busy: steal the oldest slot rather than allocating a new object.
            var stolen = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            return stolen;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            _masterVolume = 1f;
            _muted = false;
            SfxLibrary.ClearCache();
        }
    }
}
