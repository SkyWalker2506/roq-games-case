using UnityEngine;

namespace Shared.Juice
{
    /// <summary>
    /// Transform-offset camera shake and directional punch. No Cinemachine dependency, runs on unscaled
    /// time so it keeps moving through hitstop, and always restores the exact pre-shake position.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraShakeService : MonoBehaviour
    {
        static CameraShakeService _instance;

        Vector3 _basePosition;      // camera position with our offset removed
        Vector3 _appliedOffset;     // offset we added last frame
        bool _hasBase;

        // Random shake
        float _shakeAmplitude;
        float _shakeDuration;
        float _shakeElapsed;
        float _shakeFrequency;
        float _noiseSeed;

        // Directional punch
        Vector3 _punchDirection;
        float _punchAmplitude;
        float _punchDuration;
        float _punchElapsed;

        /// <summary>Service bound to <see cref="Camera.main"/>, created on first use. Null if there is no main camera.</summary>
        public static CameraShakeService Instance
        {
            get
            {
                if (_instance != null) return _instance;
                Camera cam = Camera.main;
                if (cam == null) return null;
                _instance = cam.GetComponent<CameraShakeService>();
                if (_instance == null) _instance = cam.gameObject.AddComponent<CameraShakeService>();
                return _instance;
            }
        }

        /// <summary>True while a shake or punch is still displacing the camera.</summary>
        public bool IsShaking
        {
            get { return _shakeElapsed < _shakeDuration || _punchElapsed < _punchDuration; }
        }

        /// <summary>Random noise shake: <paramref name="amplitude"/> in world units, decaying to zero over <paramref name="duration"/> seconds at <paramref name="frequency"/> Hz.</summary>
        public static void Shake(float amplitude, float duration, float frequency = 22f)
        {
            CameraShakeService s = Instance;
            if (s == null || duration <= 0f) return;
            s.CacheBase();

            // A weaker request never cuts a stronger ongoing shake short.
            float ongoing = 0f;
            if (s._shakeElapsed < s._shakeDuration)
            {
                float k = s._shakeElapsed / s._shakeDuration;
                ongoing = s._shakeAmplitude * (1f - k) * (1f - k);
            }
            if (amplitude < ongoing) return;

            s._shakeAmplitude = amplitude;
            s._shakeDuration = duration;
            s._shakeElapsed = 0f;
            s._shakeFrequency = Mathf.Max(1f, frequency);
            s._noiseSeed = Random.value * 100f;
        }

        /// <summary>Directional kick along <paramref name="direction"/> that springs back to rest over <paramref name="duration"/> seconds.</summary>
        public static void Punch(Vector3 direction, float amplitude, float duration)
        {
            CameraShakeService s = Instance;
            if (s == null || duration <= 0f) return;
            s.CacheBase();

            s._punchDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
            s._punchAmplitude = amplitude;
            s._punchDuration = duration;
            s._punchElapsed = 0f;
        }

        /// <summary>Cancels every active shake and snaps the camera back to its untouched position.</summary>
        public static void StopAll()
        {
            if (_instance == null) return;
            _instance.RestoreBase();
            _instance._shakeDuration = 0f;
            _instance._shakeElapsed = 0f;
            _instance._punchDuration = 0f;
            _instance._punchElapsed = 0f;
            _instance._shakeAmplitude = 0f;
            _instance._punchAmplitude = 0f;
        }

        void CacheBase()
        {
            if (_hasBase) return;
            _basePosition = transform.localPosition;
            _appliedOffset = Vector3.zero;
            _hasBase = true;
        }

        void RestoreBase()
        {
            if (!_hasBase) return;
            transform.localPosition = _basePosition; // exact assignment, no float drift
            _appliedOffset = Vector3.zero;
            _hasBase = false;
        }

        void LateUpdate()
        {
            if (!_hasBase) return;

            // Keep the cached base bit-exact unless somebody else moved the camera this frame.
            Vector3 expected = _basePosition + _appliedOffset;
            Vector3 current = transform.localPosition;
            if (current != expected) _basePosition = current - _appliedOffset;

            float dt = Time.unscaledDeltaTime;
            Vector3 offset = Vector3.zero;

            if (_shakeElapsed < _shakeDuration)
            {
                _shakeElapsed += dt;
                float k = Mathf.Clamp01(_shakeElapsed / _shakeDuration);
                float falloff = (1f - k) * (1f - k);           // quadratic decay reads as "energy draining"
                float t = _shakeElapsed * _shakeFrequency;
                float nx = Mathf.PerlinNoise(_noiseSeed, t) * 2f - 1f;
                float ny = Mathf.PerlinNoise(_noiseSeed + 31.7f, t) * 2f - 1f;
                float nz = Mathf.PerlinNoise(_noiseSeed + 73.3f, t) * 2f - 1f;
                offset += new Vector3(nx, ny, nz * 0.5f) * (_shakeAmplitude * falloff);
            }

            if (_punchElapsed < _punchDuration)
            {
                _punchElapsed += dt;
                float k = Mathf.Clamp01(_punchElapsed / _punchDuration);
                // Kick out fast, spring back with a damped oscillation.
                float curve = Mathf.Sin(k * Mathf.PI * 1.5f) * Mathf.Pow(1f - k, 2f);
                offset += _punchDirection * (_punchAmplitude * curve);
            }

            if (!IsShaking && offset.sqrMagnitude < 1e-8f)
            {
                RestoreBase();
                return;
            }

            transform.localPosition = _basePosition + offset;
            _appliedOffset = offset;
        }

        void OnDisable()
        {
            RestoreBase();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
        }
    }
}
