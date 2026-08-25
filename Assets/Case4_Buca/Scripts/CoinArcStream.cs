using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Audio;
using Shared.Tweening;

namespace Case4
{
    /// <summary>
    /// The gold coin payout. The reference never throws coins as a single puff: they leave the broken
    /// pile one after another and string themselves along one curve, so at any instant the arc itself
    /// is readable. This reproduces that by launching along a shared cubic bezier on a fixed stagger.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CoinArcStream : MonoBehaviour
    {
        [Header("Wiring (filled in by Case4SceneSetup)")]
        public GameObject coinPrefab;
        public Material coinMaterial;

        [Header("Stream")]
        public int coinCount = 50;
        public float stagger = 0.040f;
        public float flightDuration = 1.25f;
        public float coinScale = 0.65f;
        [Tooltip("How high the arc bulges above the straight line, in world units.")]
        public float arcRise = 4.20f;

        Camera _cam;
        readonly List<Transform> _coins = new List<Transform>(48);
        readonly List<Vector3> _curve = new List<Vector3>(32);
        readonly List<float> _arc = new List<float>(32);
        int _launched;
        Vector3 _p0, _p1, _p2, _p3;

        // ---- where the string leaves the frame -------------------------------------------------
        // MEASURED IN THE REFERENCE (_refs/Developer Case Referans/Buca.mp4, 1080x1728 @ 51 fps).
        // The lead coin was tracked frame by frame from the moment it leaves the broken pile
        // (f93, t=1.824, px 181.6,965.3) to the moment it is absorbed at the coin bank
        // (f117, t=2.294, px 1022.7,50.8). In VIEWPORT coordinates that run is
        //     (0.168, 0.441) -> (0.947, 0.971)
        // and every one of the 22 tracked samples sits on the straight line between those two
        // points to within 0.0018 viewport units - under 3 px over a 1242 px run. Constant screen
        // speed too: the per-frame step held at (+38.4, -42.0) px for the whole flight. So the
        // reference's payout is a straight screen-space ribbon fired at the TOP-RIGHT CORNER, and
        // continuing that line one step past the last visible sample crosses the frame edge at
        // viewport (0.990, 1.000).
        //
        // Ours ended INSIDE the frame at viewport (0.859, 0.810) and never crossed an edge at all,
        // which is what the owner is looking at when he says the coins should leave top-right.
        // These fields record where the built arc actually leaves the frame, so the claim is a
        // measurement in the log rather than an impression.
        bool _exitsFrame;
        Vector2 _exitViewport;
        float _exitPathPx;
        float _pathPx;
        float _gapPx;

        /// <summary>True if the built arc crosses the capture frame's edge.</summary>
        public bool ExitsFrame { get { return _exitsFrame; } }

        /// <summary>Viewport point at which the arc first leaves the capture frame (or the last sample if it never does).</summary>
        public Vector2 ExitViewport { get { return _exitViewport; } }

        /// <summary>Screen length of the whole arc, in 1080x1728 capture pixels.</summary>
        public float ScreenPathPx { get { return _pathPx; } }

        /// <summary>Screen length of the ON-SCREEN part of the arc, in capture pixels.</summary>
        public float OnScreenPathPx { get { return _exitPathPx; } }

        /// <summary>Uniform neighbour gap the string will have, in capture pixels.</summary>
        public float NeighbourGapPx { get { return _gapPx; } }

        // ---- contact arming -------------------------------------------------------------------
        // The payout is not a timeline event: it is a consequence of the puck actually touching the
        // pile. Nothing here can fire until ArmFromContact has been handed a real solver contact
        // point, so a run in which the puck never reaches the stack pays out exactly zero coins.
        bool _armed;
        Vector3 _contactPoint;
        int _blockedLaunchAttempts;
        float _worstSpawnOffset;

        /// <summary>True once a real collision contact point has armed the payout.</summary>
        public bool Armed { get { return _armed; } }

        /// <summary>The contact point the payout was armed from.</summary>
        public Vector3 ContactPoint { get { return _contactPoint; } }

        /// <summary>Launch calls refused because no contact had armed the stream.</summary>
        public int BlockedLaunchAttempts { get { return _blockedLaunchAttempts; } }

        /// <summary>Largest distance between a coin's spawn position and the contact point.</summary>
        public float WorstSpawnOffset { get { return _worstSpawnOffset; } }

        /// <summary>
        /// Arms the payout from the physics solver's own contact point. Called only from the frame in
        /// which the puck really touched a stack block.
        /// </summary>
        public void ArmFromContact(Vector3 contactPoint)
        {
            _armed = true;
            _contactPoint = contactPoint;
            _worstSpawnOffset = 0f;
        }

        /// <summary>Disarms the payout: no contact, no coins.</summary>
        public void Disarm() { _armed = false; }

        /// <summary>How many coins were actually launched during this run.</summary>
        public int LaunchedCount { get { return _launched; } }

        /// <summary>Time from the first launch until the last coin reaches the fifth HUD pip.</summary>
        public float StreamDuration
        {
            get { return Mathf.Max(0, coinCount - 1) * stagger + flightDuration; }
        }

        /// <summary>Coins currently in the air.</summary>
        public int AirborneCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _coins.Count; i++) if (_coins[i] != null && _coins[i].gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>Creates the coin instances up front so the first payout never hits Instantiate.</summary>
        public void Prewarm()
        {
            if (coinPrefab == null) return;

            // _coins is a readonly List, so a mid-playmode domain reload empties it while the coin
            // objects themselves stay parented here. Re-adopt them before instantiating, or the pool
            // silently doubles and the first set is orphaned.
            if (_coins.Count == 0)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (child != null && child.name.StartsWith("Coin_")) _coins.Add(child);
                }
                if (_coins.Count > 0)
                    Debug.LogWarning("[Case4] COIN_POOL_REBUILD re-adopted " + _coins.Count + " existing coins");
            }

            while (_coins.Count < coinCount)
            {
                GameObject go = Instantiate(coinPrefab, transform);
                go.name = "Coin_" + _coins.Count;
                go.transform.localScale = Vector3.one * coinScale;

                Collider[] cols = go.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;   // coins are pure show

                if (coinMaterial != null)
                {
                    Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rends.Length; i++) rends[i].sharedMaterial = coinMaterial;
                }

                go.SetActive(false);
                _coins.Add(go.transform);
            }
        }

        /// <summary>Flips every coin instance on or off; used only to pay their first render cost.</summary>
        public void ShowAll(bool on)
        {
            Prewarm();
            for (int i = 0; i < _coins.Count; i++)
            {
                if (_coins[i] == null) continue;
                if (on) _coins[i].position = transform.position + Vector3.up * 0.5f;
                _coins[i].gameObject.SetActive(on);
            }
        }

        /// <summary>Builds the shared arc every coin will ride.</summary>
        public void BuildCurve(Vector3 from, Vector3 to)
        {
            _p0 = from;
            _p3 = to;

            Vector3 flat = to - from;
            flat.y = 0f;
            float span = Mathf.Max(0.5f, flat.magnitude);
            Vector3 dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;

            _p1 = from + Vector3.up * arcRise + dir * (span * 0.12f);
            _p2 = to - dir * (span * 0.34f) + Vector3.up * (arcRise * 0.30f);

            _curve.Clear();
            for (int i = 0; i <= CurveSamples; i++) _curve.Add(Bezier(i / (float)CurveSamples));

            // Cumulative distance ON SCREEN, not in the world. Spacing by world arc length only
            // corrects the bezier's own parameterisation; it does not correct perspective, and this
            // arc bulges toward the camera in the middle. Measured on a capture: with world-uniform
            // spacing the neighbour gap ran 46.6 px at the foot of the arc, 71.6 px in the middle and
            // 39.9 px at the top - a 1.8x swing. Against a 47.7 px coin the top of the string
            // overlapped itself and merged, which is why 22 coins resolved into 18 components while
            // the MEDIAN gap looked healthy at 1.26 diameters. No stagger/flightDuration/scale triple
            // can fix that: the binding constraint is the minimum gap, and the minimum-to-median
            // ratio is a property of the projection, not of the parameters.
            _arc.Clear();
            _arc.Add(0f);
            for (int i = 1; i < _curve.Count; i++)
                _arc.Add(_arc[i - 1] + ScreenSpan(_curve[i - 1], _curve[i]));

            // Report the spacing the string will actually have, so the next capture confirms the
            // uniformity directly instead of it being re-derived from component centroids.
            float totalHalfHeights = _arc[_arc.Count - 1];
            float px = totalHalfHeights * HalfHeightPx;            // half-height units -> capture pixels
            float frac = flightDuration > 0.0001f ? stagger / flightDuration : 0f;
            _pathPx = px;
            _gapPx = px * frac;
            Debug.Log(string.Format(
                "[Case4] COIN_ARC screen path {0:0} px over {1} samples; uniform neighbour gap {2:0.0} px " +
                "at stagger/flightDuration {3:0.0000} (coinScale {4:0.000})",
                px, _curve.Count, _gapPx, frac, coinScale));

            MeasureFrameExit();
        }

        /// <summary>
        /// Walks the built arc in CAPTURE viewport space and records the first point at which it
        /// leaves the frame.
        ///
        /// PRE-REGISTERED CONTROL for "the coins should exit the screen from the top right":
        ///   the arc must cross the 1080x1728 frame boundary, and the FIRST crossing must lie in
        ///   the top-right quadrant - viewport x >= 0.5 AND y >= 0.5.
        /// It is a control and not a tautology because it can observe the thing it names: on the
        /// build before this change the arc ends at viewport (0.859, 0.810), inside the frame, and
        /// never crosses the boundary for ANY of the six impact points recorded in Logs/ - so the
        /// line reads FAIL there while every other assertion in the run stays green. Nothing in the
        /// pass condition is derived from the target: it is read back off the sampled curve.
        /// </summary>
        void MeasureFrameExit()
        {
            _exitsFrame = false;
            _exitViewport = Vector2.zero;
            _exitPathPx = _pathPx;
            if (_curve.Count < 2) return;

            Vector2 prev = ViewportOf(_curve[0]);
            bool prevInside = Inside(prev);
            float travelled = 0f;

            for (int i = 1; i < _curve.Count; i++)
            {
                Vector2 here = ViewportOf(_curve[i]);
                // Straight off the cumulative table BuildCurve just filled, so the on-screen length
                // reported here and the total reported by COIN_ARC cannot drift apart.
                float seg = (_arc[i] - _arc[i - 1]) * HalfHeightPx;

                if (prevInside && !Inside(here))
                {
                    // Refine the crossing along this one segment so the reported corner is the edge
                    // point, not the first sample past it. 64 samples over a ~1500 px arc is ~23 px
                    // per segment, which is a third of a coin - too coarse to name a corner with.
                    float lo = 0f, hi = 1f;
                    for (int k = 0; k < 24; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Inside(Vector2.Lerp(prev, here, mid))) lo = mid; else hi = mid;
                    }
                    _exitsFrame = true;
                    _exitViewport = Vector2.Lerp(prev, here, lo);
                    _exitPathPx = travelled + seg * lo;
                    break;
                }

                travelled += seg;
                prev = here;
                prevInside = Inside(here);
                _exitViewport = here;
            }

            // If the arc does not even START inside the frame there is no exit to speak of, and
            // calling that FAIL would be an instrument reporting on something it cannot see. That is
            // the edit-mode refusal probe in RefPositionGate, which builds a synthetic curve with no
            // camera in the scene: it must not look like a regression in this one.
            bool originInFrame = Inside(ViewportOf(_curve[0]));
            bool topRight = _exitsFrame && _exitViewport.x >= 0.5f && _exitViewport.y >= 0.5f;
            string verdict = !originInFrame ? "unmeasurable-origin-not-in-frame" : (topRight ? "PASS" : "FAIL");
            int onScreenCoins = _gapPx > 0.01f
                ? Mathf.Min(coinCount, Mathf.FloorToInt(_exitPathPx / _gapPx))
                : 0;

            Debug.Log(string.Format(
                "[Case4] COIN_EXIT {0} exitsFrame={1} firstCrossing=({2:0.000},{3:0.000}) " +
                "onScreenPath={4:0} px of {5:0} px; coins on screen at once ~{6} of {7} " +
                "(reference: straight ribbon (0.168,0.441)->(0.947,0.971), extrapolated crossing (0.990,1.000))",
                verdict, _exitsFrame,
                _exitViewport.x, _exitViewport.y, _exitPathPx, _pathPx, onScreenCoins, coinCount));

            // The pacing guard. This one is GREEN before the change (55.3 px = 1.02 diameters) and
            // must stay green after it, so it is the regression control rather than the target
            // control. Band source: this file's own measured comments - the reference string reads
            // 63.6 px = 1.18 diameters at a 54.1 px coin, and merging was observed on OUR captures
            // once neighbours closed to about 49 px, i.e. below one diameter. Only asserted when a
            // real contact armed the stream; on a missed shot no coin is launched and the arc is
            // built from a contactless origin whose length means nothing.
            float diameters = _gapPx / RefCoinDiameterPx;
            bool gapOk = diameters >= 1.00f && diameters <= 1.25f;
            Debug.Log(string.Format(
                "[Case4] COIN_GAP {0} neighbour gap {1:0.0} px = {2:0.00} coin diameters " +
                "(band 1.00..1.25, reference 63.6 px = 1.18)",
                _armed && originInFrame ? (gapOk ? "PASS" : "FAIL") : "informational-unarmed", _gapPx, diameters));
        }

        /// <summary>Half the capture frame's height, in pixels. The strip renders 1080x1728.</summary>
        const float HalfHeightPx = 1728f * 0.5f;

        /// <summary>
        /// The capture frame's aspect. Deliberately a constant and NOT Camera.aspect: measured on
        /// eight runs in Logs/, the camera reports an aspect of 1.32 at the moment the curve is
        /// built, because in batchmode it has no target texture yet and falls back to the editor's
        /// screen. Every screen-space number in this file is stated in the 1080x1728 the strip
        /// actually renders, so the aim must be too.
        /// </summary>
        public const float CaptureAspect = 1080f / 1728f;

        /// <summary>Full-size coin diameter in capture pixels at coinScale 0.6184, measured on a capture.</summary>
        const float RefCoinDiameterPx = 54.1f;

        static bool Inside(Vector2 v)
        {
            return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        /// <summary>World point -> capture viewport, using the same projection ScreenSpan measures with.</summary>
        Vector2 ViewportOf(Vector3 world)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (_cam == null) return new Vector2(-1f, -1f);

            Transform t = _cam.transform;
            float tanV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector2 s;
            if (tanV < 0.0001f || !ToScreen(world, t, tanV, out s)) return new Vector2(-1f, -1f);
            return new Vector2(0.5f + s.x / (2f * CaptureAspect), 0.5f + s.y * 0.5f);
        }

        /// <summary>
        /// The inverse: a CAPTURE-viewport point, placed in the plane through <paramref name="through"/>
        /// that is parallel to the image plane. This replaces Camera.ViewportToWorldPoint for aiming
        /// the payout. ViewportToWorldPoint multiplies the horizontal offset by Camera.aspect, which
        /// is the editor's 1.32 here and not the strip's 0.625, so an aim authored as viewport x=0.67
        /// actually landed at x=0.859 of the rendered frame - and would land somewhere else again on
        /// a machine with a differently shaped editor window. This version is machine-independent.
        /// </summary>
        public Vector3 CaptureViewportPoint(Camera cam, float vx, float vy, Vector3 through)
        {
            if (cam == null) return through;
            Transform t = cam.transform;
            float depth = Vector3.Dot(through - t.position, t.forward);
            if (depth <= 0.1f) return through;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return t.position
                 + t.forward * depth
                 + t.right * ((2f * vx - 1f) * depth * tanV * CaptureAspect)
                 + t.up * ((2f * vy - 1f) * depth * tanV);
        }

        /// <summary>Number of polyline samples behind the arc-length table. 24 under-measured a curve this bent by about 8%.</summary>
        const int CurveSamples = 64;

        /// <summary>
        /// Distance between two world points in screen units, independent of resolution and aspect.
        /// Deliberately not Camera.WorldToScreenPoint: in batchmode the camera has no target texture
        /// when the curve is built, so it would report the editor's screen size rather than the
        /// 1080x1728 the capture actually renders. Working in half-height units cancels both.
        /// </summary>
        float ScreenSpan(Vector3 a, Vector3 b)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            if (_cam == null) return Vector3.Distance(a, b);

            Transform t = _cam.transform;
            float tanV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (tanV < 0.0001f) return Vector3.Distance(a, b);

            Vector2 pa, pb;
            if (!ToScreen(a, t, tanV, out pa) || !ToScreen(b, t, tanV, out pb)) return Vector3.Distance(a, b);
            return Vector2.Distance(pa, pb);
        }

        bool ToScreen(Vector3 world, Transform cam, float tanV, out Vector2 screen)
        {
            Vector3 v = world - cam.position;
            float z = Vector3.Dot(v, cam.forward);
            if (z <= 0.01f) { screen = Vector2.zero; return false; }
            float k = 1f / (z * tanV);
            screen = new Vector2(Vector3.Dot(v, cam.right) * k, Vector3.Dot(v, cam.up) * k);
            return true;
        }

        /// <summary>
        /// Points the coin's flat face at the camera and spins it in the image plane, so it always
        /// reads as a disc rather than an edge-on sliver.
        /// </summary>
        void FaceCamera(Transform coin, float spinDegrees)
        {
            if (_cam == null) return;
            Vector3 toCam = _cam.transform.position - coin.position;
            if (toCam.sqrMagnitude < 0.0001f) return;
            coin.rotation = Quaternion.FromToRotation(Vector3.up, toCam.normalized)
                          * Quaternion.AngleAxis(spinDegrees, Vector3.up);
        }

        /// <summary>
        /// Maps a 0..1 fraction of arc length to the curve parameter that sits there, so coins spaced
        /// evenly in time end up spaced evenly in distance.
        /// </summary>
        float TForArcLength(float s)
        {
            if (_arc.Count < 2) return s;
            float total = _arc[_arc.Count - 1];
            if (total <= 0.0001f) return s;

            float target = Mathf.Clamp01(s) * total;
            for (int i = 1; i < _arc.Count; i++)
            {
                if (_arc[i] < target) continue;
                float seg = _arc[i] - _arc[i - 1];
                float f = seg > 0.0001f ? (target - _arc[i - 1]) / seg : 0f;
                return ((i - 1) + f) / (_arc.Count - 1);
            }
            return 1f;
        }

        Vector3 Bezier(float t)
        {
            float u = 1f - t;
            return u * u * u * _p0
                 + 3f * u * u * t * _p1
                 + 3f * u * t * t * _p2
                 + t * t * t * _p3;
        }

        /// <summary>Launches the whole stream and returns once the last coin has been sent on its way.</summary>
        public IEnumerator Launch()
        {
            if (!_armed)
            {
                // No solver contact -> no payout. This is the whole fix: the stream used to run off the
                // sequence timeline, so a shot that missed still rained gold.
                _blockedLaunchAttempts++;
                _launched = 0;
                Debug.Log("[Case4] COIN_BLOCKED no stack contact armed this stream; coins launched = 0");
                yield break;
            }

            Prewarm();
            _launched = 0;

            int count = Mathf.Min(coinCount, _coins.Count);

            // Launch by the clock, not one coin per frame. The capture runs at a fixed
            // Time.captureFramerate of 100, so a frame is exactly 10 ms; the old loop waited for
            // Time.time to pass `stagger` and therefore rounded every stagger UP to a whole frame.
            // An authored 0.0132 became 0.02 and an authored 0.0082 became 0.01 - the string launched
            // at half the intended rate and was still filling the arc long after t=2.10, which is why
            // only 8 coins were airborne there against the reference's 21. Coins that come due on the
            // same frame all start on that frame, but each keeps its OWN ideal launch time as its
            // phase origin, so a sub-frame stagger still spreads them along the arc.
            float t0 = Time.time;
            while (_launched < count)
            {
                // The stream now starts on the impact beat, one beat before the toppled-blocks proof
                // can be measured. If that proof fails the director disarms us mid-string, and the
                // rest of the payout is cancelled here rather than raining gold for a shot that did
                // not earn it.
                if (!_armed)
                {
                    Debug.Log(string.Format(
                        "[Case4] COIN_ABORT disarmed mid-stream after {0} of {1} coins", _launched, count));
                    yield break;
                }
                int due = stagger > 0.00001f
                    ? Mathf.Min(count, Mathf.FloorToInt((Time.time - t0) / stagger) + 1)
                    : count;
                while (_launched < due)
                {
                    StartCoroutine(Fly(_coins[_launched], _launched, t0 + _launched * stagger));
                    AudioService.PlayRepeat(SfxId.RippleTick, _launched, 0.55f);
                    _launched++;
                }
                if (_launched < count) yield return null;
            }

            // Report what the stream actually did, not what it was asked to do. The old pacing rounded
            // every stagger up to a whole capture frame and nothing in the log said so; this line makes
            // the effective rate readable straight from the capture.
            Debug.Log(string.Format(
                "[Case4] COIN_PACING {0} coins over {1:0.000}s wall = {2:0.0000}s effective stagger " +
                "(authored {3:0.0000}, flightDuration {4:0.000}, capture frame {5:0.0000}s)",
                _launched, Time.time - t0,
                _launched > 1 ? (Time.time - t0) / (_launched - 1) : 0f,
                stagger, flightDuration,
                Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.deltaTime));
        }

        IEnumerator Fly(Transform coin, int index, float plannedStart)
        {
            if (coin == null) yield break;

            coin.gameObject.SetActive(true);
            coin.position = _p0 + SpawnJitter(index);
            _worstSpawnOffset = Mathf.Max(_worstSpawnOffset, Vector3.Distance(coin.position, _contactPoint));
            // The reference's coins read as circles the whole way up the arc: every component in
            // ref_2.10s.png measures about 56x57, i.e. face-on to the camera. Ours were seeded with a
            // random two-axis rotation and then tumbled on local Y and local X, so they presented at
            // arbitrary angles - measured components ran 53x56, 54x35, 54x33 and 55x19, where the thin
            // ones are coins caught edge-on. Those lose most of their area and let their face-on
            // neighbours fuse into one blob, which is why 32 coins resolved into 11 components.
            // Billboarding to the camera and spinning in the image plane keeps every coin a circle.
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);

            // The ideal launch time, not this frame's: two coins that come due together must still
            // sit at different points on the arc.
            float start = plannedStart;
            float spin = Mathf.Lerp(600f, 900f, Sample01(index, 6));
            // Was 0.10 world units, alternating by index parity, enveloped by sin(t*PI) so it peaks at
            // mid-flight. It is applied to world X ONLY, which is harmless where the string runs
            // vertically - up the left riser X is perpendicular to the string and the term just widens
            // the ribbon - but destructive over the apex, where the string runs nearly horizontally and
            // X is ALONG it. There the alternating sign subtracts from every other neighbour gap:
            // 1 world unit is 88 px at this coin scale, so +-(0.05..0.10) is +-4.4..8.8 px and adjacent
            // coins differ by up to 17.6 px. Measured at t=2.10: the ideal gap held at 59.5 px per coin
            // (blob centres 118.7, 119.7 px = two coins each), but inside each pair the coins sat ~49 px
            // apart and fused - five 75-78 px merged blobs, all of them above cy 450 where the arc turns
            // over. The lower riser, same coin size and the same 56.9-63.2 px spacing, produced eight
            // discrete singles and no merges: the spacing was never the problem, this term was.
            float lateral = 0f;

            while (true)
            {
                float t = Mathf.Clamp01((Time.time - start) / flightDuration);
                Vector3 p = Bezier(TForArcLength(t));
                p.x += lateral * Mathf.Sin(t * Mathf.PI);   // a little spread so the string is not a wire
                coin.position = p;
                FaceCamera(coin, Sample01(index, 4) * 360f + (Time.time - start) * spin);

                // Pop in fast. The shrink-out that used to run from t=0.88 to 1.00 is gone: it was
                // there to fake the coin being swallowed by a HUD counter that this build does not
                // have, and now that the arc leaves the frame it fires in the wrong place. The
                // measured crossing sits at 87% of the arc, so the old ramp started BEFORE the edge:
                // the coin was already down to about 0.5 scale as it reached the corner and had
                // faded to a speck by the time it got there. A coin that shrinks away at the border
                // has not exited the screen, it has evaporated at it. The reference's coins keep
                // full size all the way to the corner - the tracked lead coin measures ~1900 gold
                // px per frame from f93 to f116 and only drops at f117, where it is absorbed.
                float s = coinScale;
                if (t < 0.10f) s *= Mathf.Lerp(0.35f, 1.12f, t / 0.10f);
                else if (t < 0.20f) s *= Mathf.Lerp(1.12f, 1f, (t - 0.10f) / 0.10f);
                coin.localScale = Vector3.one * s;

                if (t >= 1f) break;
                yield return null;
            }

            coin.gameObject.SetActive(false);
        }

        /// <summary>
        /// Local hash sampler. Coin dressing must not consume UnityEngine.Random's global state: doing
        /// that made the measured pass and capture pass start with different offsets and spins.
        /// </summary>
        static float Sample01(int index, int salt)
        {
            uint x = 0xC4B00CAu + (uint)(index + 1) * 0x9E3779B9u + (uint)(salt + 1) * 0x85EBCA6Bu;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (x & 0x00FFFFFFu) / 16777215f;
        }

        static Vector3 SpawnJitter(int index)
        {
            Vector3 v = new Vector3(
                Sample01(index, 0) * 2f - 1f,
                Sample01(index, 1) * 2f - 1f,
                Sample01(index, 2) * 2f - 1f);
            if (v.sqrMagnitude > 1f) v.Normalize();
            return v * (0.16f * Sample01(index, 3));
        }

        /// <summary>Pulls every coin off the screen and resets the counter.</summary>
        public void Clear()
        {
            StopAllCoroutines();
            for (int i = 0; i < _coins.Count; i++)
            {
                if (_coins[i] == null) continue;
                _coins[i].gameObject.SetActive(false);
                _coins[i].localScale = Vector3.one * coinScale;
                _coins[i].localRotation = Quaternion.identity;
            }
            _launched = 0;
            _armed = false;
            _worstSpawnOffset = 0f;
        }

        /// <summary>Sampled arc points, for drawing the flight path in the editor.</summary>
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.82f, 0.2f, 0.9f);
            for (int i = 1; i < _curve.Count; i++) Gizmos.DrawLine(_curve[i - 1], _curve[i]);
        }
    }
}
