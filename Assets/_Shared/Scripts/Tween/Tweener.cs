using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shared.Tweening
{
    /// <summary>Cancellable reference to a running tween; invalid once the tween finishes or is cancelled.</summary>
    public readonly struct TweenHandle
    {
        internal readonly int Index;
        internal readonly int Gen;

        internal TweenHandle(int index, int gen) { Index = index; Gen = gen; }

        /// <summary>An empty handle that refers to no tween.</summary>
        public static TweenHandle None { get { return new TweenHandle(-1, 0); } }

        /// <summary>True while the tween still exists and has not completed or been cancelled.</summary>
        public bool IsActive { get { return Tweener.IsActive(this); } }

        /// <summary>Total seconds from creation until this tween finishes (delay + duration).</summary>
        public float TotalDuration { get { return Tweener.GetTotalDuration(this); } }

        /// <summary>Sets the easing curve. Returns this handle for chaining.</summary>
        public TweenHandle SetEase(EaseType ease) { Tweener.SetEase(this, ease, Ease.DefaultBackOvershoot); return this; }

        /// <summary>Sets the easing curve with a custom Back/Elastic overshoot amount. Returns this handle for chaining.</summary>
        public TweenHandle SetEase(EaseType ease, float overshoot) { Tweener.SetEase(this, ease, overshoot); return this; }

        /// <summary>Delays the start by <paramref name="seconds"/>. Returns this handle for chaining.</summary>
        public TweenHandle SetDelay(float seconds) { Tweener.SetDelay(this, seconds); return this; }

        /// <summary>Runs on unscaled time so hitstop does not freeze this tween. Returns this handle for chaining.</summary>
        public TweenHandle SetUnscaled(bool unscaled = true) { Tweener.SetUnscaled(this, unscaled); return this; }

        /// <summary>Invokes <paramref name="callback"/> when the tween reaches its end (not when cancelled). Returns this handle for chaining.</summary>
        public TweenHandle OnComplete(Action callback) { Tweener.SetOnComplete(this, callback); return this; }

        /// <summary>Stops the tween immediately without firing its completion callback.</summary>
        public void Cancel() { Tweener.Cancel(this); }

        /// <summary>Jumps to the end value and fires the completion callback.</summary>
        public void Complete() { Tweener.CompleteNow(this); }
    }

    /// <summary>Update-driven, allocation-light tween runner for float, Vector3 and Color values.</summary>
    public static class Tweener
    {
        enum Kind : byte { Float, Vector3, Color, Callback }

        sealed class TweenState
        {
            public int Gen;
            public bool Active;
            public Kind Kind;
            public float Duration;
            public float Delay;
            public float Elapsed;
            public bool Unscaled;
            public EaseType Ease;
            public float Overshoot;
            public Vector4 From;
            public Vector4 To;
            public Action<float> OnFloat;
            public Action<Vector3> OnVector3;
            public Action<Color> OnColor;
            public Action OnDone;
        }

        static readonly List<TweenState> States = new List<TweenState>(64);
        static readonly Stack<int> Free = new Stack<int>(64);
        static TweenRunner _runner;

        /// <summary>Number of tweens currently running (diagnostics / tests).</summary>
        public static int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < States.Count; i++) if (States[i].Active) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------- creation

        /// <summary>Tweens a float from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/> seconds, pushing each value to <paramref name="onUpdate"/>.</summary>
        public static TweenHandle Float(float from, float to, float duration, Action<float> onUpdate)
        {
            int i;
            TweenState s = Acquire(Kind.Float, duration, out i);
            s.From = new Vector4(from, 0f, 0f, 0f);
            s.To = new Vector4(to, 0f, 0f, 0f);
            s.OnFloat = onUpdate;
            return new TweenHandle(i, s.Gen);
        }

        /// <summary>Tweens a Vector3 from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/> seconds, pushing each value to <paramref name="onUpdate"/>.</summary>
        public static TweenHandle Vector3(Vector3 from, Vector3 to, float duration, Action<Vector3> onUpdate)
        {
            int i;
            TweenState s = Acquire(Kind.Vector3, duration, out i);
            s.From = new Vector4(from.x, from.y, from.z, 0f);
            s.To = new Vector4(to.x, to.y, to.z, 0f);
            s.OnVector3 = onUpdate;
            return new TweenHandle(i, s.Gen);
        }

        /// <summary>Tweens a Color from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/> seconds, pushing each value to <paramref name="onUpdate"/>.</summary>
        public static TweenHandle Color(Color from, Color to, float duration, Action<Color> onUpdate)
        {
            int i;
            TweenState s = Acquire(Kind.Color, duration, out i);
            s.From = new Vector4(from.r, from.g, from.b, from.a);
            s.To = new Vector4(to.r, to.g, to.b, to.a);
            s.OnColor = onUpdate;
            return new TweenHandle(i, s.Gen);
        }

        /// <summary>Schedules <paramref name="callback"/> to fire once after <paramref name="delay"/> seconds.</summary>
        public static TweenHandle Delay(float delay, Action callback)
        {
            int i;
            TweenState s = Acquire(Kind.Callback, 0f, out i);
            s.Delay = delay;
            s.OnDone = callback;
            return new TweenHandle(i, s.Gen);
        }

        /// <summary>Moves <paramref name="target"/> to <paramref name="to"/> in local space; automatically cancels if the transform is destroyed.</summary>
        public static TweenHandle MoveLocal(Transform target, Vector3 to, float duration)
        {
            Vector3 from = target.localPosition;
            return Vector3(from, to, duration, v => { if (target != null) target.localPosition = v; });
        }

        /// <summary>Scales <paramref name="target"/> to <paramref name="to"/>; automatically cancels if the transform is destroyed.</summary>
        public static TweenHandle ScaleTo(Transform target, Vector3 to, float duration)
        {
            Vector3 from = target.localScale;
            return Vector3(from, to, duration, v => { if (target != null) target.localScale = v; });
        }

        // ---------------------------------------------------------------- control

        /// <summary>Stops every running tween. Completion callbacks are not fired.</summary>
        public static void CancelAll()
        {
            for (int i = 0; i < States.Count; i++)
            {
                if (States[i].Active) Release(i);
            }
        }

        // ---------------------------------------------------------------- internals

        internal static bool IsActive(TweenHandle h)
        {
            if (h.Index < 0 || h.Index >= States.Count) return false;
            TweenState s = States[h.Index];
            return s.Active && s.Gen == h.Gen;
        }

        internal static float GetTotalDuration(TweenHandle h)
        {
            TweenState s = Resolve(h);
            return s == null ? 0f : s.Delay + s.Duration;
        }

        internal static void SetEase(TweenHandle h, EaseType ease, float overshoot)
        {
            TweenState s = Resolve(h);
            if (s == null) return;
            s.Ease = ease;
            s.Overshoot = overshoot;
        }

        internal static void SetDelay(TweenHandle h, float seconds)
        {
            TweenState s = Resolve(h);
            if (s != null) s.Delay = Mathf.Max(0f, seconds);
        }

        internal static void SetUnscaled(TweenHandle h, bool unscaled)
        {
            TweenState s = Resolve(h);
            if (s != null) s.Unscaled = unscaled;
        }

        internal static void SetOnComplete(TweenHandle h, Action callback)
        {
            TweenState s = Resolve(h);
            if (s != null) s.OnDone = callback;
        }

        internal static void Cancel(TweenHandle h)
        {
            if (!IsActive(h)) return;
            Release(h.Index);
        }

        internal static void CompleteNow(TweenHandle h)
        {
            TweenState s = Resolve(h);
            if (s == null) return;
            Apply(s, 1f);
            Action done = s.OnDone;
            Release(h.Index);
            if (done != null) done();
        }

        static TweenState Resolve(TweenHandle h)
        {
            return IsActive(h) ? States[h.Index] : null;
        }

        static TweenState Acquire(Kind kind, float duration, out int index)
        {
            EnsureRunner();

            TweenState s;
            if (Free.Count > 0)
            {
                index = Free.Pop();
                s = States[index];
            }
            else
            {
                s = new TweenState();
                States.Add(s);
                index = States.Count - 1;
            }

            s.Active = true;
            s.Kind = kind;
            s.Duration = Mathf.Max(0f, duration);
            s.Delay = 0f;
            s.Elapsed = 0f;
            s.Unscaled = false;
            s.Ease = EaseType.Linear;
            s.Overshoot = Shared.Tweening.Ease.DefaultBackOvershoot;
            s.OnFloat = null;
            s.OnVector3 = null;
            s.OnColor = null;
            s.OnDone = null;
            s.From = Vector4.zero;
            s.To = Vector4.zero;
            return s;
        }

        static void Release(int index)
        {
            TweenState s = States[index];
            s.Active = false;
            s.Gen++;
            s.OnFloat = null;
            s.OnVector3 = null;
            s.OnColor = null;
            s.OnDone = null;
            Free.Push(index);
        }

        static void Apply(TweenState s, float k)
        {
            float e = Shared.Tweening.Ease.Evaluate(s.Ease, k, s.Overshoot);
            switch (s.Kind)
            {
                case Kind.Float:
                    if (s.OnFloat != null) s.OnFloat(Mathf.LerpUnclamped(s.From.x, s.To.x, e));
                    break;
                case Kind.Vector3:
                    if (s.OnVector3 != null)
                        s.OnVector3(UnityEngine.Vector3.LerpUnclamped(
                            new Vector3(s.From.x, s.From.y, s.From.z),
                            new Vector3(s.To.x, s.To.y, s.To.z), e));
                    break;
                case Kind.Color:
                    if (s.OnColor != null)
                        s.OnColor(UnityEngine.Color.LerpUnclamped(
                            new Color(s.From.x, s.From.y, s.From.z, s.From.w),
                            new Color(s.To.x, s.To.y, s.To.z, s.To.w), e));
                    break;
            }
        }

        internal static void Tick(float scaled, float unscaled)
        {
            // Snapshot the count: tweens started from callbacks begin next frame.
            int count = States.Count;
            for (int i = 0; i < count; i++)
            {
                TweenState s = States[i];
                if (!s.Active) continue;

                s.Elapsed += s.Unscaled ? unscaled : scaled;
                float local = s.Elapsed - s.Delay;
                if (local < 0f) continue;

                float k = s.Duration <= 0f ? 1f : Mathf.Clamp01(local / s.Duration);
                Apply(s, k);

                if (k >= 1f)
                {
                    Action done = s.OnDone;
                    Release(i);
                    if (done != null) done();
                }
            }
        }

        static void EnsureRunner()
        {
            if (_runner != null) return;
            GameObject go = new GameObject("[Tweener]");
            go.hideFlags = HideFlags.HideInHierarchy;
            _runner = go.AddComponent<TweenRunner>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            States.Clear();
            Free.Clear();
            _runner = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A single-mode load replaces everything the running tweens were pointing at.
            if (mode == LoadSceneMode.Single) CancelAll();
        }

        sealed class TweenRunner : MonoBehaviour
        {
            void Update() { Tick(Time.deltaTime, Time.unscaledDeltaTime); }
        }
    }

    /// <summary>Chains tweens on a shared timeline by assigning their start delays. Build it in one go, then let it run.</summary>
    public sealed class TweenSequence
    {
        float _cursor;
        float _lastStart;
        float _end;

        /// <summary>Creates an empty sequence whose timeline starts at zero.</summary>
        public static TweenSequence Create() { return new TweenSequence(); }

        /// <summary>Total length of the sequence in seconds.</summary>
        public float Duration { get { return _end; } }

        /// <summary>Starts <paramref name="handle"/> after everything appended so far, then advances the cursor past it.</summary>
        public TweenSequence Append(TweenHandle handle)
        {
            _lastStart = _cursor;
            handle.SetDelay(_cursor);
            float end = handle.TotalDuration; // delay was just set to the cursor, so this is an absolute end time
            _cursor = end;
            if (end > _end) _end = end;
            return this;
        }

        /// <summary>Starts <paramref name="handle"/> at the same moment as the previously appended tween (parallel step).</summary>
        public TweenSequence Join(TweenHandle handle)
        {
            handle.SetDelay(_lastStart);
            float end = handle.TotalDuration;
            if (end > _cursor) _cursor = end;
            if (end > _end) _end = end;
            return this;
        }

        /// <summary>Inserts a pause of <paramref name="seconds"/> before the next appended step.</summary>
        public TweenSequence AppendInterval(float seconds)
        {
            _cursor += Mathf.Max(0f, seconds);
            if (_cursor > _end) _end = _cursor;
            return this;
        }

        /// <summary>Fires <paramref name="callback"/> at the current cursor position without advancing it.</summary>
        public TweenSequence AppendCallback(Action callback)
        {
            Tweener.Delay(_cursor, callback);
            _lastStart = _cursor;
            return this;
        }

        /// <summary>Fires <paramref name="callback"/> when the sequence ends. Call this last.</summary>
        public TweenSequence OnComplete(Action callback)
        {
            Tweener.Delay(_end, callback);
            return this;
        }
    }
}
