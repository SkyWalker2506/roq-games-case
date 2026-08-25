using System.Collections.Generic;
using UnityEngine;

namespace Shared.Juice
{
    /// <summary>
    /// Per-prefab pool of particle effects. Instances are reused once their particles die out, so an
    /// impact-heavy sequence never hits Instantiate/Destroy during play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VFXPool : MonoBehaviour
    {
        sealed class Pool
        {
            public GameObject Prefab;
            public readonly Stack<GameObject> Idle = new Stack<GameObject>(8);
        }

        struct Live
        {
            public GameObject Instance;
            public Pool Owner;
            public ParticleSystem[] Systems;
            public float SafetyDeadline;   // unscaled time at which we reclaim regardless
        }

        static VFXPool _instance;

        readonly Dictionary<int, Pool> _pools = new Dictionary<int, Pool>(16);
        readonly List<Live> _live = new List<Live>(32);

        /// <summary>Number of effects currently playing (diagnostics / tests).</summary>
        public static int LiveCount { get { return _instance == null ? 0 : _instance._live.Count; } }

        /// <summary>Plays <paramref name="prefab"/> at a world position and returns the pooled instance.</summary>
        public static GameObject Play(GameObject prefab, Vector3 position)
        {
            return Play(prefab, position, Quaternion.identity, 1f);
        }

        /// <summary>Plays <paramref name="prefab"/> at a world pose with a uniform <paramref name="scale"/>; the instance returns to the pool when its particles finish.</summary>
        public static GameObject Play(GameObject prefab, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            if (prefab == null) return null;
            VFXPool p = EnsureInstance();
            return p.PlayInternal(prefab, position, rotation, scale);
        }

        /// <summary>Pre-creates <paramref name="count"/> idle instances so the first impact does not allocate.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return;
            VFXPool p = EnsureInstance();
            Pool pool = p.GetPool(prefab);
            for (int i = 0; i < count; i++)
            {
                GameObject go = Instantiate(prefab, p.transform);
                go.SetActive(false);
                pool.Idle.Push(go);
            }
        }

        /// <summary>Stops every live effect and returns all instances to their pools.</summary>
        public static void ReclaimAll()
        {
            if (_instance == null) return;
            for (int i = _instance._live.Count - 1; i >= 0; i--) _instance.Reclaim(i);
        }

        GameObject PlayInternal(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
        {
            Pool pool = GetPool(prefab);
            GameObject go = pool.Idle.Count > 0 ? pool.Idle.Pop() : Instantiate(prefab, transform);

            Transform t = go.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = prefab.transform.localScale * scale;
            go.SetActive(true);

            ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float longest = 0.5f;
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
                ParticleSystem.MainModule main = systems[i].main;
                longest = Mathf.Max(longest, main.duration + main.startLifetime.constantMax);
            }

            _live.Add(new Live
            {
                Instance = go,
                Owner = pool,
                Systems = systems,
                SafetyDeadline = Time.unscaledTime + longest + 1f
            });
            return go;
        }

        Pool GetPool(GameObject prefab)
        {
            int key = prefab.GetInstanceID();
            Pool pool;
            if (!_pools.TryGetValue(key, out pool))
            {
                pool = new Pool { Prefab = prefab };
                _pools.Add(key, pool);
            }
            return pool;
        }

        void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Live live = _live[i];
                if (live.Instance == null) { _live.RemoveAt(i); continue; }

                bool alive = false;
                for (int j = 0; j < live.Systems.Length; j++)
                {
                    if (live.Systems[j] != null && live.Systems[j].IsAlive(true)) { alive = true; break; }
                }

                if (!alive || Time.unscaledTime > live.SafetyDeadline) Reclaim(i);
            }
        }

        void Reclaim(int index)
        {
            Live live = _live[index];
            _live.RemoveAt(index);
            if (live.Instance == null) return;

            for (int j = 0; j < live.Systems.Length; j++)
            {
                if (live.Systems[j] != null) live.Systems[j].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            live.Instance.SetActive(false);
            live.Instance.transform.SetParent(transform, false);
            live.Owner.Idle.Push(live.Instance);
        }

        static VFXPool EnsureInstance()
        {
            if (_instance != null) return _instance;
            GameObject go = new GameObject("[VFXPool]");
            _instance = go.AddComponent<VFXPool>();
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
        }
    }
}
