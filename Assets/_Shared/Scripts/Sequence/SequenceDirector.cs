using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Shared.Sequencing
{
    /// <summary>
    /// Base class for a single case interaction. A concrete director implements <see cref="RunSequence"/> as a
    /// coroutine and calls <see cref="BeginStep"/>/<see cref="EndStep"/>/<see cref="Fire"/> as the sequence
    /// runs, which fills a <see cref="SequenceReport"/> with proof of what actually happened.
    /// </summary>
    public abstract class SequenceDirector : MonoBehaviour
    {
        // NOTE: the old serialised field was called "playOnStart" and every staged case scene has
        // "playOnStart: 1" baked into it (lesson #4: scene data wins over C# initialisers). Renaming the
        // field orphans that stored value, so the new default below is what actually takes effect without
        // touching a single case scene.
        [Tooltip("Batchmode capture only. In game mode this stays off: the player starts the sequence.")]
        [SerializeField] bool autoPlayForCapture = false;

        [Tooltip("Seconds to wait before the capture auto-play, so the scene can settle.")]
        [SerializeField] float autoPlayDelay = 0.4f;

        readonly SequenceReport _report = new SequenceReport();
        Coroutine _running;
        float _startTime;
        bool _unlocked;

        /// <summary>Evidence log for the most recent (or current) run.</summary>
        public SequenceReport Report { get { return _report; } }

        /// <summary>True while the sequence coroutine is running.</summary>
        public bool IsPlaying { get { return _running != null; } }

        /// <summary>
        /// Clock used by the sequence and its report. Most cases deliberately use unscaled gameplay time;
        /// a case may override this when its visible motion is authored on the scaled game clock.
        /// </summary>
        protected virtual float SequenceClock { get { return Time.unscaledTime; } }

        /// <summary>
        /// Fixed playback rate requested from the frame-strip harness. Zero keeps Unity's normal time
        /// management. The harness applies this only while it owns a capture and restores the old value.
        /// </summary>
        public virtual int DeterministicCaptureFramerate { get { return 0; } }

        /// <summary>
        /// Extra seconds the frame-strip harness should film after the interaction report completes.
        /// This lets particles decay on film without keeping player input locked behind a cosmetic tail.
        /// </summary>
        public virtual float CaptureTailDuration { get { return 0f; } }

        /// <summary>Seconds elapsed on this director's clock since the current run started.</summary>
        public float SequenceTime { get { return IsPlaying || _report.completed ? SequenceClock - _startTime : 0f; } }

        /// <summary>Name written into the report; defaults to the GameObject name.</summary>
        public virtual string SequenceName { get { return gameObject.name; } }

        /// <summary>Raised with the finished report when the sequence completes.</summary>
        public event Action<SequenceReport> OnSequenceComplete;

        /// <summary>
        /// True once this director is allowed to run without a player input backing the call. Set by
        /// <see cref="AllowPlayWithoutInput"/> (capture harness) or by the first input-backed
        /// <see cref="Play"/>.
        /// </summary>
        public bool PlayUnlocked { get { return _unlocked; } }

        /// <summary>
        /// Lets the sequence be driven with no real input behind it. Only the batchmode capture harness and
        /// the explicit <c>autoPlayForCapture</c> switch use this; game mode never calls it.
        /// </summary>
        public void AllowPlayWithoutInput()
        {
            _unlocked = true;
        }

        /// <summary>
        /// Starts the sequence from the current scene state. Does nothing if already playing.
        /// A call that is not backed by real input and has not been unlocked is an auto-play attempt
        /// (base Start, or a case director calling Play() from its own Start override) and is refused:
        /// nothing in this project runs itself any more, the player starts it.
        /// </summary>
        public void Play()
        {
            if (_running != null) return;

            if (!_unlocked)
            {
                if (!InputIsActiveThisFrame())
                {
                    Debug.Log("[SequenceDirector] AUTOPLAY_SUPPRESSED name=" + SequenceName +
                              " reason=no-input-behind-call");
                    return;
                }
                _unlocked = true;
            }

            _report.Reset(SequenceName);
            _startTime = SequenceClock;
            _running = StartCoroutine(Drive());
        }

        /// <summary>Stops anything in flight, restores the scene via <see cref="ResetState"/>, and plays again from the top.</summary>
        public void Replay()
        {
            Stop();
            ResetState();
            Play();
        }

        /// <summary>Stops the sequence immediately, leaving the scene where it is.</summary>
        public void Stop()
        {
            if (_running != null)
            {
                StopCoroutine(_running);
                _running = null;
            }
            Shared.Tweening.Tweener.CancelAll();
            Shared.Juice.HitstopService.Resume();
            Shared.Juice.CameraShakeService.StopAll();
        }

        /// <summary>The sequence itself. Yield to pace the phases; the base class handles timing and reporting.</summary>
        protected abstract IEnumerator RunSequence();

        /// <summary>Puts the scene back to its pre-sequence state so a replay looks identical to the first run.</summary>
        protected abstract void ResetState();

        /// <summary>Opens a named phase in the report.</summary>
        protected void BeginStep(string stepName)
        {
            _report.BeginStep(stepName, SequenceClock - _startTime);
        }

        /// <summary>Closes the currently open phase.</summary>
        protected void EndStep()
        {
            _report.EndStep(SequenceClock - _startTime);
        }

        /// <summary>Records that a juice technique fired right now. Call it where the effect is triggered, not where it is configured.</summary>
        protected void Fire(JuiceEvent juiceEvent, string detail = null)
        {
            _report.Fire(juiceEvent, SequenceClock - _startTime, detail);
        }

        IEnumerator Drive()
        {
            yield return RunSequence();

            _report.Complete(SequenceClock - _startTime);
            _running = null;

            Action<SequenceReport> handler = OnSequenceComplete;
            if (handler != null) handler(_report);
        }

        /// <summary>
        /// Startup hook. It does NOT start the sequence: in game mode the scene comes up idle and waits for
        /// the player. The only exception is <c>autoPlayForCapture</c>, which the batchmode frame-strip
        /// harness uses so the capture path keeps working.
        /// </summary>
        protected virtual IEnumerator Start()
        {
            if (!autoPlayForCapture)
            {
                Debug.Log("[SequenceDirector] AUTOPLAY_DISABLED name=" + SequenceName +
                          " scene=" + gameObject.scene.name + " isPlaying=" + IsPlaying);
                yield break;
            }

            if (autoPlayDelay > 0f) yield return new WaitForSeconds(autoPlayDelay);
            AllowPlayWithoutInput();
            Play();
        }

        /// <summary>
        /// True when a real pointer / touch / key is active in the frame the call is made. Evaluated at call
        /// time so it never depends on script execution order between the EventSystem and this component.
        /// </summary>
        static bool InputIsActiveThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null &&
                (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame ||
                 mouse.rightButton.isPressed || mouse.rightButton.wasReleasedThisFrame))
            {
                return true;
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null &&
                (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasReleasedThisFrame))
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.isPressed) return true;

            return false;
        }

        void OnDisable()
        {
            // Never leave the global services in a modified state behind us.
            Shared.Juice.HitstopService.Resume();
            Shared.Juice.CameraShakeService.StopAll();
        }
    }
}
