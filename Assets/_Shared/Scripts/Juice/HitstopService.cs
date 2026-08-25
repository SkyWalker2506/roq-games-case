using UnityEngine;

namespace Shared.Juice
{
    /// <summary>
    /// Freeze-frame on impact. Drops <see cref="Time.timeScale"/> for a short window and restores it on an
    /// unscaled timer. Nested calls keep the longest remaining stop; the time scale is never left at zero.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitstopService : MonoBehaviour
    {
        /// <summary>Hardest freeze the service will apply, in real seconds.</summary>
        public const float MaxDuration = 0.15f;

        static HitstopService _instance;

        float _remaining;
        float _restoreTimeScale = 1f;
        float _restoreFixedDelta = 0.02f;
        bool _active;

        /// <summary>True while a hitstop is holding the time scale down.</summary>
        public static bool IsActive { get { return _instance != null && _instance._active; } }

        /// <summary>Seconds of hitstop still to run.</summary>
        public static float Remaining { get { return _instance == null ? 0f : _instance._remaining; } }

        /// <summary>Freezes gameplay for a short real-time window, optionally slowing instead of stopping dead.</summary>
        /// <param name="seconds">Real-time length of the freeze, clamped to 0..<see cref="MaxDuration"/>.</param>
        /// <param name="timeScale">Time scale held during the freeze; 0 is a hard stop.</param>
        public static void Stop(float seconds, float timeScale = 0f)
        {
            seconds = Mathf.Clamp(seconds, 0f, MaxDuration);
            if (seconds <= 0f) return;

            HitstopService s = EnsureInstance();
            if (!s._active)
            {
                s._restoreTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                s._restoreFixedDelta = Time.fixedDeltaTime;
                s._active = true;
            }

            // Longest remaining stop wins; a shorter nested hit never ends an ongoing freeze early.
            s._remaining = Mathf.Max(s._remaining, seconds);
            Time.timeScale = Mathf.Clamp(timeScale, 0f, 0.5f);
            Time.fixedDeltaTime = s._restoreFixedDelta * Mathf.Max(Time.timeScale, 0.01f);
        }

        /// <summary>Ends any active hitstop immediately and restores the previous time scale.</summary>
        public static void Resume()
        {
            if (_instance != null) _instance.Restore();
        }

        static HitstopService EnsureInstance()
        {
            if (_instance != null) return _instance;
            GameObject go = new GameObject("[Hitstop]");
            go.hideFlags = HideFlags.HideInHierarchy;
            _instance = go.AddComponent<HitstopService>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        void Restore()
        {
            if (!_active) return;
            _active = false;
            _remaining = 0f;
            Time.timeScale = _restoreTimeScale;
            Time.fixedDeltaTime = _restoreFixedDelta;
        }

        void Update()
        {
            if (!_active) return;
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f) Restore();
        }

        // Safety valves: the time scale must never survive a teardown at zero.
        void OnDisable() { Restore(); }
        void OnApplicationQuit() { Restore(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            if (Time.timeScale <= 0f) Time.timeScale = 1f;
        }
    }
}
