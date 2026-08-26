using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Audio;
using Shared.Juice;
using Shared.Sequencing;
using Shared.Tweening;

namespace Case3
{
    /// <summary>
    /// Case 3 interaction: tap ANY waiting sticker and THAT sticker peels off the strip with a real page
    /// curl, flies along a short arc to ITS OWN ghost slot on the page trailing yellow-lime sparkles,
    /// keeps its blank paper back towards the camera until arrival, then flips into the filled reward card.
    ///
    /// The pairing is by name (Sticker_Hayvan -> Ghost_Hayvan) and is resolved in the scene setup, so the
    /// tap is a real selection: three stickers, three destinations, and the one the player touched is the
    /// one that moves. A ghost slot with no sticker in the scene is simply never a destination.
    ///
    /// Phase lengths come from .plan-build/timing.md: 0.30 s curl, 0.35 s flight, 0.30 s flip. The whole
    /// run adds a 0.12 s tap wind-up in front and a settle behind, because the reference never cuts a
    /// sequence off dead. Everything runs off one absolute scaled timeline, so deterministic capture can
    /// advance it one authored frame at a time instead of letting editor stalls collapse several phases.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Case3Director : SequenceDirector
    {
        /// <summary>One tappable sticker and the ghost slot it belongs in.</summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>
            /// The STACK key: which reward card this item is collected onto ("Cat", "Noodle",
            /// "Sweets"). Several page items share one key on purpose - that is the stack.
            /// </summary>
            public string key;
            /// <summary>
            /// What this individual item is called. The reference names the CARD, not the item, so
            /// this is what gets logged on pick and what the card tab reads for the first item of a
            /// stack; a cup of ramen and a tin of ramen are both collected onto the "Noodle" card.
            /// </summary>
            public string displayName;
            /// <summary>
            /// Material this item wears while it is UNCOVERED. Page items are authored wearing the dim
            /// material, so there is no earlier value to restore and it has to be named here; without
            /// it an uncovered page item would be handed a null material and disappear.
            /// </summary>
            public Material litMaterial;
            /// <summary>Peel driver, which lives on the sticker object itself.</summary>
            public StickerPeel peel;
            /// <summary>The sticker sprite renderer.</summary>
            public SpriteRenderer sticker;
            /// <summary>Ghost slot this sticker belongs in; supplies the landing pose.</summary>
            public SpriteRenderer targetSlot;
            /// <summary>Reference-filled card (art, label and 1/5), revealed only after attachment.</summary>
            public SpriteRenderer reward;

            [NonSerialized] public Vector3 HomePosition;
            [NonSerialized] public Quaternion HomeRotation;
            [NonSerialized] public Vector3 HomeScale;
            [NonSerialized] public int HomeSortingOrder;
            [NonSerialized] public Vector3 RewardHomeScale;
            [NonSerialized] public Color RewardHomeColor;
            [NonSerialized] public bool Captured;
            [NonSerialized] public bool Consumed;
        }

        [Header("Scene wiring (filled in by Case3SceneSetup)")]
        public Entry[] entries = new Entry[0];
        public StickerFlight flight;

        [Tooltip("Camera used to turn a tap into a world position.")]
        public Camera sceneCamera;

        [Header("VFX prefabs")]
        public GameObject attachBurstPrefab;

        [Header("Stacking (reward cards)")]
        [Tooltip("How many of a kind a card asks for. The reference draws 1/5 on BOTH filled cards - " +
                 "on the Cat card and on the Noodle card - while the page carries a different number " +
                 "of cats than of noodles, and it separately draws 1/2 for the page. So 5 is a fixed " +
                 "per-card requirement collected across the level, not a count of what is on this page.")]
        public int stackRequirement = 5;

        [Tooltip("One per reward card. The counter label is the card's own '1/5'; it is drawn INTO the " +
                 "card's bottom-right corner, in world space with the card, not as screen HUD.")]
        public StackCard[] stacks = new StackCard[0];

        /// <summary>A reward card and the running count of the kind collected onto it.</summary>
        [Serializable]
        public sealed class StackCard
        {
            /// <summary>Matches <see cref="Entry.key"/>.</summary>
            public string key;
            /// <summary>The filled card art; the same renderer every Entry with this key points at.</summary>
            public SpriteRenderer card;
            /// <summary>World-space "n/5" drawn in the card's bottom-right corner.</summary>
            public TMPro.TextMeshPro counter;
            /// <summary>
            /// How many of this kind the card asks for. 0 falls back to the global
            /// <see cref="stackRequirement"/>.
            ///
            /// It exists because the page does not hold the same number of every kind: the authored
            /// population gives Noodle and Sweets SIX collectibles each against a global requirement of
            /// five, so both cards could reach "6/5" - a counter past its own maximum, which the owner
            /// saw. The requirement follows the page rather than the page being trimmed to the
            /// requirement, because the board's density is a visual decision and this is a label.
            /// </summary>
            public int requirement;
            [NonSerialized] public int Collected;
        }

        [Header("Covered-item state")]
        [Tooltip("Material a COVERED sticker wears. Wired by Case3SceneSetup.Build from " +
                 "Materials/Case3_PageObjectDim.mat - the same one the page's dim items use.")]
        public Material dimMaterial;

        [Tooltip("Sorting order a collected item is lifted to for the whole run. A page item is " +
                 "authored in the 100s, under the reward cards at 200; without this it would fly to " +
                 "its card and land BEHIND it.")]
        public int flightSortingOrder = 590;

        [Header("Timing, seconds (from .plan-build/timing.md)")]
        [Tooltip("Pre-tap idle duration matching reference video (t=0.00 to 0.75s).")]
        public float idleDelay = 0.75f;
        [Tooltip("Tap wind-up before the corner lifts. Reference: tap -> peel start = 0.05 s.")]
        public float tapDuration = 0.05f;
        [Tooltip("Reference: 0.30 s curl.")]
        public float peelDuration = 0.30f;
        [Tooltip("Reference: 0.35 s flight.")]
        public float flightDuration = 0.35f;
        [Tooltip("Flip back to the printed face. Timed against the reference at 0.12 s.")]
        /// <summary>
        /// How long the sheet takes to unroll once it is sitting in its card.
        ///
        /// 0.30, not the 0.12 it was. At 0.12 the whole unroll happened inside three or four frames
        /// and read as the sheet flipping over rather than as paper relaxing open - the owner's
        /// "sanki flip oluyor gibi". His reference frames show a white rolled tube arriving intact
        /// and opening in the slot, so the curl is right and only its pace was wrong.
        /// </summary>
        /// NonSerialized: Stickerdom.unity carries flipDuration 0.12, and a serialized field is read
        /// from the SCENE, not from this initialiser - so raising it here would have changed nothing
        /// at all. Fourth time today that a scene value silently outranked source; the others were
        /// Case 4's trail numbers, Case 2's shard scale and Case 3's counter requirement.
        [System.NonSerialized] public float flipDuration = 0.30f;
        [Tooltip("Overshoot pop as the sticker meets the page.")]
        public float popDuration = 0.11f;
        [Tooltip("Ring-out after the pop; the reference never ends a sequence dead.")]
        public float settleDuration = 0.07f;

        [Header("Shape of the motion")]
        [Tooltip("Peel amount reached at the end of the curl. Just under 1 so a rounded edge survives into the flight.")]
        [Range(0.5f, 1f)] public float peelEnd = 0.96f;
        [Tooltip("How far the sticker lifts off the strip while it peels, in world units.")]
        public float peelLift = 0.34f;
        [Tooltip("Scale at the far end of the flight, as a fraction of the landing scale. Reference: 1.0 -> 0.6.")]
        public float flightShrink = 0.88f;
        [Tooltip("Peak of the landing overshoot, as a fraction: 0.05 means the sticker splats 5% wide before settling.")]
        public float popStretch = 0.050f;

        [Header("Landing trace (diagnostics)")]
        [Tooltip("Logs the sheet's DRAWN world position every frame from the start of the flight to the " +
                 "end of the settle, and states whether the landing was one move or several. Left on: " +
                 "it costs one 9x9 curl sample per frame for about half a second per run, and it is the " +
                 "only thing that can tell a smooth landing from a corrected one without a video.")]
        public bool traceLanding = true;

        [Tooltip("THE INVARIANT. Once the flight has begun, the drawn sheet must only ever get CLOSER " +
                 "to where it finally comes to rest. A frame that increases that remaining distance is " +
                 "a correction - a second move the player reads as a hop - and this is how big one is " +
                 "allowed to be, as a fraction of the flight chord.\n\n" +
                 "0.01 is measured, not chosen: tracked frame by frame off Stickerdom.mp4, the " +
                 "reference's first landing (t=1.074 to 1.406, 760 px chord) never retreats at all - " +
                 "its progress along the chord is strictly increasing on all 20 frames - and the " +
                 "largest lateral wobble the tracker itself reports is 7.2 px, 0.95% of the chord. So " +
                 "the reference's own measurement noise IS the band.")]
        public float landingReversalBand = 0.01f;

        /// <summary>
        /// The last trace, verbatim. The Unity console truncates a block this long and a CLI read of it
        /// comes back cut, so the report is also parked here where a one-line eval can fetch all of it.
        /// </summary>
        public static string LastLandingTrace = "";

        /// <summary>Verdict of the last trace; both checks had to pass.</summary>
        public static bool LastLandingGreen;

        struct TraceSample
        {
            public float t;
            public string phase;
            public Vector3 drawn;      // what the player sees: curl mesh AABB centre
            public Vector3 origin;     // sticker.transform.position, for naming the writer
        }

        readonly System.Collections.Generic.List<TraceSample> _trace =
            new System.Collections.Generic.List<TraceSample>(256);
        StickerPeel _tracePeel;
        float _traceChord;

        int _current = -1;
        Transform _stickerTf;
        float _t0;
        int _startFrame;
        /// <summary>Name written into the report.</summary>
        public override string SequenceName { get { return "Case3_Stickerdom"; } }

        /// <summary>Case 3 motion is sampled at the source video's 120 fps.</summary>
        public override int DeterministicCaptureFramerate { get { return 120; } }

        /// <summary>Keep filming long enough for the local landing stars to decay.</summary>
        public override float CaptureTailDuration { get { return 0.35f; } }

        /// <summary>Scaled time follows Time.captureFramerate during deterministic capture.</summary>
        protected override float SequenceClock { get { return Time.time; } }

        /// <summary>What this item is called on screen; falls back to its object name.</summary>
        public string NameOf(int index)
        {
            if (index < 0 || index >= Count || entries[index] == null) return "?";
            Entry e = entries[index];
            if (!string.IsNullOrEmpty(e.displayName)) return e.displayName;
            return e.sticker != null ? e.sticker.name : e.key;
        }

        /// <summary>Number of registered stickers.</summary>
        public int Count { get { return entries != null ? entries.Length : 0; } }

        /// <summary>Index of the sticker currently selected, or -1.</summary>
        public int CurrentIndex { get { return _current; } }

        /// <summary>The selected entry, or null.</summary>
        public Entry Current { get { return (_current >= 0 && _current < Count) ? entries[_current] : null; } }

        /// <summary>True once the prewarm has finished and the scene is waiting for a tap.</summary>
        public bool Ready { get; private set; }

        void Awake()
        {
            CaptureHome();
        }

        void CaptureHome()
        {
            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.sticker == null || e.Captured) continue;
                Transform t = e.sticker.transform;
                e.HomePosition = t.position;
                e.HomeRotation = t.rotation;
                e.HomeScale = t.localScale;
                e.HomeSortingOrder = e.sticker.sortingOrder;
                if (e.reward != null)
                {
                    e.RewardHomeScale = e.reward.transform.localScale;
                    e.RewardHomeColor = e.reward.color;
                }
                e.Captured = true;
            }
        }

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// True when the sticker can still be tapped.
        ///
        /// A sticker that another sheet is DRAWN ON TOP OF is not tappable, because it is not what
        /// the player can see and reach. In the reference a covered item reads dim and does nothing
        /// when tapped; it becomes a collectible sticker at the instant the sheet above it comes off.
        /// The rule is one-directional and stays that way: covered implies dim and untappable, but
        /// uncovered does NOT imply lit - the reference's page print stays dim forever.
        /// </summary>
        public bool Playable(int index)
        {
            if (index < 0 || index >= Count) return false;
            Entry e = entries[index];
            if (e == null || e.sticker == null || e.peel == null || e.targetSlot == null || e.Consumed)
                return false;
            return !Covered(index);
        }

        // ------------------------------------------------------------------ coverage

        /// <summary>
        /// How much of a page item another item has to hide before it stops being tappable.
        ///
        /// DERIVED, and re-derived here because the first derivation was measured through a BLIND
        /// instrument. The earlier number (2%) came from a sweep that reported "one pair at 13.7%,
        /// 0.00% for all nine others". Every one of those zeroes was an artefact: fourteen of the
        /// nineteen page sprites had Read/Write off, <see cref="SpriteAlphaAt"/> returns -1 for an
        /// unreadable texture, and -1 fails the alpha test at every sample, so those sprites measured
        /// as having no drawn area at all. The page was never that empty.
        ///
        /// With Read/Write on, the true population of per-item coverage on the authored page is
        ///     0.00 0.00 0.00 0.00 0.00 1.39 1.92 | 13.72 15.93 25.82 26.13 28.19 45.10 66.28 86.04
        /// - a real gap between 1.92% (PageObj_choc, grazed by the marshmallows) and 13.72%
        /// (Sticker_Sweets under Sticker_Cat). 5% sits in the middle of THAT gap: 2.6x above the
        /// highest non-overlap and 2.7x below the lowest genuine one. 2% would have sat 4% clear of
        /// PageObj_choc, which is inside measurement noise.
        /// </summary>
        public const float CoverThreshold = 0.05f;

        /// <summary>Sample grid used to measure coverage. Resolves a 5% region with room to spare.</summary>
        const int CoverageSamples = 64;

        bool[] _covered;
        bool _coverageDirty = true;
        Material[] _litMaterial;
        bool _dimWarned;

        /// <summary>
        /// Entries whose sprite texture could not be read on the last <see cref="RecomputeCoverage"/>.
        ///
        /// This is the failure that produced the wrong threshold and the wrong page: an unreadable
        /// texture makes <see cref="Coverage"/> return 0 for a sprite that is in fact buried, and 0
        /// reads as "uncovered, light it up". It is a measurement outage and it must never again be
        /// indistinguishable from a measurement of zero, so it is counted, logged and asserted on by
        /// Case3CoverageGate rather than swallowed.
        /// </summary>
        public int CoverageBlindCount { get; private set; }
        bool _blindWarned;

        /// <summary>
        /// Fraction of this sticker's own DRAWN area that a sticker drawn above it hides.
        ///
        /// It reuses PickSticker's rule verbatim - alpha at or above <see cref="TapAlphaThreshold"/>,
        /// higher <c>sortingOrder</c> wins - deliberately. Two disagreeing definitions of "on top" is
        /// the failure this project keeps finding, so there is exactly one.
        /// </summary>
        public float Coverage(int index)
        {
            if (index < 0 || index >= Count) return 0f;
            Entry e = entries[index];
            if (e == null || e.sticker == null || e.Consumed || !e.sticker.enabled) return 0f;

            Bounds b = e.sticker.bounds;
            int drawn = 0, hidden = 0;
            for (int yi = 0; yi < CoverageSamples; yi++)
            {
                float fy = (yi + 0.5f) / CoverageSamples;
                for (int xi = 0; xi < CoverageSamples; xi++)
                {
                    float fx = (xi + 0.5f) / CoverageSamples;
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx),
                                            Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                    if (SpriteAlphaAt(e.sticker, p) < TapAlphaThreshold) continue;
                    drawn++;
                    for (int j = 0; j < Count; j++)
                    {
                        if (j == index) continue;
                        Entry o = entries[j];
                        if (o == null || o.sticker == null || o.Consumed || !o.sticker.enabled) continue;
                        if (o.sticker.sortingOrder <= e.sticker.sortingOrder) continue;
                        if (SpriteAlphaAt(o.sticker, p) < TapAlphaThreshold) continue;
                        hidden++;
                        break;
                    }
                }
            }
            return drawn == 0 ? 0f : (float)hidden / drawn;
        }

        /// <summary>True when a sheet drawn above this one hides at least <see cref="CoverThreshold"/> of it.</summary>
        public bool Covered(int index)
        {
            if (index < 0 || index >= Count) return false;
            if (_coverageDirty) RecomputeCoverage();
            return _covered != null && index < _covered.Length && _covered[index];
        }

        /// <summary>
        /// Recomputes coverage for every sticker and puts each one into the state that follows from it.
        /// Called at prewarm, at peel completion - the instant the reference promotes - and on reset.
        /// Nothing ever reverts on its own: a sticker only changes state when this runs.
        /// </summary>
        public void RecomputeCoverage()
        {
            _coverageDirty = false;
            if (_covered == null || _covered.Length != Count) _covered = new bool[Count];
            if (_litMaterial == null || _litMaterial.Length != Count) _litMaterial = new Material[Count];

            CoverageBlindCount = 0;
            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.sticker == null) { _covered[i] = false; continue; }
                if (e.sticker.sprite != null && e.sticker.sprite.texture != null &&
                    !e.sticker.sprite.texture.isReadable)
                {
                    CoverageBlindCount++;
                    if (!_blindWarned)
                    {
                        _blindWarned = true;
                        Debug.LogError("[Case3] " + e.sticker.name + "'s sprite texture is not readable, so its " +
                                       "coverage cannot be measured and will read as 0% - i.e. it will be lit and " +
                                       "tappable however deeply it is buried. Tick Read/Write on every page sprite. " +
                                       "This exact outage is what made the whole page measure 0.00% before.");
                    }
                }
                _covered[i] = Coverage(i) >= CoverThreshold;
            }
            ApplyCoveredMaterials();
        }

        /// <summary>Covered reads dim, uncovered reads as itself. One frame, no fade - as in the reference.</summary>
        void ApplyCoveredMaterials()
        {
            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.sticker == null) continue;

                if (_litMaterial[i] == null)
                    _litMaterial[i] = e.litMaterial != null ? e.litMaterial
                                    : (e.sticker.sharedMaterial != dimMaterial ? e.sticker.sharedMaterial : null);

                if (_covered[i])
                {
                    if (dimMaterial == null)
                    {
                        if (!_dimWarned)
                        {
                            _dimWarned = true;
                            Debug.LogError("[Case3] " + e.sticker.name + " is covered and must read dim, but " +
                                           "dimMaterial is not wired. Run Case3SceneSetup.Build. The sticker is " +
                                           "still untappable, so the page cannot be played wrong - it just looks wrong.");
                        }
                        continue;
                    }
                    if (e.sticker.sharedMaterial != dimMaterial) e.sticker.sharedMaterial = dimMaterial;
                }
                else if (e.sticker.sharedMaterial == dimMaterial)
                {
                    if (_litMaterial[i] == null)
                    {
                        // Never hand a renderer a null material to "fix" a lit state - it would draw
                        // magenta and the page would be worse than the bug. Say so instead.
                        if (!_dimWarned)
                        {
                            _dimWarned = true;
                            Debug.LogError("[Case3] " + e.sticker.name + " is uncovered and must read lit, but no " +
                                           "litMaterial is wired for it. Run Case3SceneSetup.Build.");
                        }
                        continue;
                    }
                    e.sticker.sharedMaterial = _litMaterial[i];
                }
            }
        }

        // ------------------------------------------------------------------ stacking

        /// <summary>The card for a key, or null.</summary>
        public StackCard StackOf(string key)
        {
            if (stacks == null || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < stacks.Length; i++)
                if (stacks[i] != null && stacks[i].key == key) return stacks[i];
            return null;
        }

        /// <summary>
        /// Sorting order a sticker is lifted to while it flies, and the floor the collected pile sits on.
        ///
        /// Derived from the cards rather than authored: `flightSortingOrder` was 590 while the reward
        /// cards sit at 600/601/602, so every sticker flew UNDERNEATH the album on its way there and
        /// only appeared once it had already landed. A serialized constant cannot notice that the cards
        /// moved; reading them can. The authored value is still honoured as a floor.
        /// </summary>
        int CarrySortingOrder()
        {
            int top = flightSortingOrder;
            if (stacks != null)
                for (int i = 0; i < stacks.Length; i++)
                    if (stacks[i] != null && stacks[i].card != null && stacks[i].card.sortingOrder + 10 > top)
                        top = stacks[i].card.sortingOrder + 10;
            return top;
        }

        /// <summary>How many of a kind have been collected onto its card so far.</summary>
        public int StackCount(string key)
        {
            StackCard c = StackOf(key);
            return c != null ? c.Collected : 0;
        }

        /// <summary>
        /// Adds one to a card's stack and rewrites its counter.
        ///
        /// The counter is the whole point of a stack: collecting a SECOND ramen does not open a second
        /// card, it lands on the Noodle card and turns 1/5 into 2/5. Called once, at attach.
        /// </summary>
        void PushStack(string key)
        {
            StackCard c = StackOf(key);
            if (c == null)
            {
                Debug.LogError("[Case3] no reward card is wired for key '" + key + "'; the collected item " +
                               "has nowhere to stack. Run Case3SceneSetup.Build.");
                return;
            }
            c.Collected++;
            RefreshStackLabel(c);
        }

        /// <summary>Writes "n/5" onto a card, and hides the label until the card holds something.</summary>
        void RefreshStackLabel(StackCard c)
        {
            if (c == null || c.counter == null) return;
            // Per-card first, global as the fallback. Deliberately NOT clamping Collected: clamping
            // would print 5/5 with six items sitting on the card, replacing an impossible number with
            // a wrong one.
            c.counter.text = c.Collected + "/" + DenominatorFor(c);
            c.counter.enabled = c.Collected > 0;

            // The counter is drawn ON TOP of everything on its card.
            //
            // It is authored as a child of the card, so it inherited the card's order (~600) while the
            // landed sheets are lifted to CarrySortingOrder() + stack depth (612 and up). The sticker
            // therefore covered its own counter - the fourth time today that an order which was right
            // when it was written went stale once the object it sits over moved band.
            //
            // Derived, not authored: whatever the pile climbs to, the number stays above it.
            var cr = c.counter.renderer;
            if (cr != null)
            {
                cr.sortingLayerID = c.card != null ? c.card.sortingLayerID : cr.sortingLayerID;
                cr.sortingOrder = CarrySortingOrder() + MaxRequirement() + 1;
            }
        }

        /// <summary>
        /// How many of a kind the card asks for: exactly what the PAGE holds.
        ///
        /// COUNTED AT RUNTIME, deliberately, rather than read from the serialized `requirement`.
        /// That field lives in the scene, and the scene is hand-authored by the owner, so a corrected
        /// value in the builder only reaches the card if someone re-runs Build - which nobody is
        /// going to do before delivery. Counting the entries needs no scene write and cannot go stale
        /// when the page's population changes.
        ///
        /// The serialized value is still honoured as a fallback for a card with no items on the page.
        /// </summary>
        int DenominatorFor(StackCard c)
        {
            int have = 0;
            if (entries != null && c != null)
                for (int i = 0; i < entries.Length; i++)
                    if (entries[i] != null && entries[i].key == c.key) have++;
            if (have > 0) return have;
            return Mathf.Max(1, c != null && c.requirement > 0 ? c.requirement : stackRequirement);
        }

        /// <summary>
        /// Where the drawn sheet's centre has to be so that its stuck edge stays on
        /// <paramref name="anchor"/>, easing onto <paramref name="rest"/> by the end of the unroll.
        ///
        /// The correction is what the edge has drifted this frame; blending it out over the unroll
        /// means the sheet finishes exactly on its authored resting centre instead of wherever the
        /// arithmetic left it.
        /// </summary>
        static Vector3 PeelDrawnCentre(StickerPeel peel, Vector3 anchor, Vector3 edgeNow, Vector3 rest, float e)
        {
            Vector3 centreNow = peel.VisualWorldCentre(9);
            Vector3 held = centreNow + (anchor - edgeNow);      // edge pinned exactly
            return Vector3.Lerp(held, rest, e * e);             // and settled onto rest by the end
        }

        /// <summary>The deepest any pile on any card can get, so the counter can clear all of them.</summary>
        int MaxRequirement()
        {
            int top = stackRequirement;
            if (stacks != null)
                for (int i = 0; i < stacks.Length; i++)
                    if (stacks[i] != null && stacks[i].requirement > top) top = stacks[i].requirement;
            return top;
        }

        /// <summary>Empties every card. Only the replay path uses it.</summary>
        void ResetStacks()
        {
            if (stacks == null) return;
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] == null) continue;
                stacks[i].Collected = 0;
                RefreshStackLabel(stacks[i]);
            }
        }

        /// <summary>
        /// Nudges an already-landed sheet off the centre of its card so the pile reads as a pile.
        ///
        /// Offsets are a fraction of the CARD sprite's own width, not world units, so the pile keeps its
        /// proportions whatever the card is scaled to. It alternates side to side and grows with depth,
        /// so each layer clears the one under it instead of hiding inside it.
        ///
        /// Deliberately does NOT rotate. A tilted fan was tried and the owner rejected it: these are
        /// printed cards in a neat album, not a hand thrown down on a table, and a few degrees of tilt
        /// read as sloppy rather than as depth. Position only.
        ///
        /// DEVIATION, recorded rather than fitted: the reference never lands a SECOND item of a kind -
        /// every counter in it reads 1/5 - so there is no footage to measure these against. They are an
        /// authored look chosen to read at this card size, not a measurement.
        /// </summary>
        Vector3 FanOffset(SpriteRenderer card, int index)
        {
            if (card == null || card.sprite == null || index <= 0) return Vector3.zero;
            float w = card.sprite.bounds.size.x * card.transform.lossyScale.x;
            float side = (index % 2 == 1) ? 1f : -1f;

            // BOUNDED, and that is the point. The offset used to grow linearly with depth
            // (1 + 0.55 per item), which is fine for two or three and absurd for six: the sixth sheet
            // sat 0.32 card-widths out and the pile hung off both sides of the card. The owner saw
            // exactly that on the 6/6 cards - "stickerlar tasmasin, daha ortaya gelsin".
            //
            // A pile of real stickers does not fan further and further either; it converges, because
            // each new one lands on the mound rather than beside it. So the spread saturates: the
            // first few separate enough to be legible, and after that they stack in place.
            float depth = 1f - Mathf.Exp(-0.9f * index);        // 0.59, 0.83, 0.93, 0.97, 0.99 ...
            return new Vector3(side * w * 0.075f * depth, -w * 0.040f * depth, -0.01f * index);
        }

        /// <summary>
        /// Where this item's sheet comes to rest, in DRAWN world space. Decided once, before the sheet
        /// moves at all, and never recomputed.
        ///
        /// Anchored on the REWARD CARD, not on the ghost slot. The two are not the same point: in
        /// Stickerdom.unity every Ghost_* sits at y = 5.350 and every Reward_* at y = 5.500, so a sheet
        /// flown to the ghost stops 0.15 u - 15 px at this camera's 100 px/u - below the art it is about
        /// to become. The ghost still supplies the landing SCALE, which is what it was authored for.
        /// </summary>
        Vector3 RestingPlace(Entry e, int fanIndex)
        {
            Transform anchor = e.reward != null ? e.reward.transform : e.targetSlot.transform;
            Vector3 p = anchor.position + FanOffset(e.reward, fanIndex);
            p.z = e.targetSlot.transform.position.z - 0.15f;
            return p;
        }

        /// <summary>
        /// Moves the sticker so that the thing the PLAYER SEES is centred on <paramref name="target"/>.
        ///
        /// Writing <c>transform.position = target</c> does not do this and that is the whole bug: while
        /// the sheet is curled, the drawn paper sits a long way from its own transform - measured at
        /// 1.18 u for the cup of ramen and 2.79 u for the candy cane, on a 6-8 u flight. The flight aimed
        /// the transform, so the DRAWN sheet stopped a fifth of the way short of the card, and the curl
        /// unwinding during the flip then dragged it the rest of the way in 27 ms. Two moves where the
        /// player asked for one.
        ///
        /// The offset is a pure function of the pose, not of the position, so one probe measures it
        /// exactly: place, ask where the paper landed, subtract. Costs one 9x9 curl sample per frame.
        /// </summary>
        void PlaceDrawnAt(StickerPeel peel, Vector3 target)
        {
            if (_stickerTf == null) return;
            if (peel == null) { _stickerTf.position = target; return; }

            _stickerTf.position = target;
            Vector3 drawn = peel.VisualWorldCentre(9);
            _stickerTf.position = new Vector3(target.x - (drawn.x - target.x),
                                              target.y - (drawn.y - target.y),
                                              target.z);
        }

        // ------------------------------------------------------------------ landing trace

        /// <summary>Starts a trace. <paramref name="chord"/> is the straight-line flight distance the band is a fraction of.</summary>
        void TraceBegin(StickerPeel peel, float chord)
        {
            _trace.Clear();
            _tracePeel = peel;
            _traceChord = Mathf.Max(0.001f, chord);
        }

        /// <summary>One sample. Call it every frame of the landing AND immediately after any instant write.</summary>
        void TraceMark(string phase)
        {
            if (!traceLanding || _tracePeel == null) return;
            _trace.Add(new TraceSample
            {
                t = SequenceClock - _t0,
                phase = phase,
                drawn = _tracePeel.VisualWorldCentre(9),
                origin = _stickerTf != null ? _stickerTf.position : Vector3.zero
            });
        }

        /// <summary>
        /// Prints the trace and rules on the invariant.
        ///
        /// The rule is structural, not a look: from the first frame of the flight onward the DRAWN
        /// sheet's distance to where it finally rests must be non-increasing. Every frame that
        /// increases it is named, with the phase it happened in, so the writer is identifiable from
        /// the log alone rather than by re-reading the coroutine.
        /// </summary>
        void TraceEnd()
        {
            if (!traceLanding || _trace.Count < 2) { _tracePeel = null; return; }

            Vector3 rest = _trace[_trace.Count - 1].drawn;
            float band = landingReversalBand * _traceChord;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(4096);
            sb.AppendFormat("[Case3] LANDING TRACE {0} -> {1} card; chord {2:0.000} u; band {3:0.0000} u " +
                            "({4:0.0}% of chord); rest ({5:0.000}, {6:0.000})\n",
                            NameOf(_current), Current != null ? Current.key : "?", _traceChord, band,
                            landingReversalBand * 100f, rest.x, rest.y);
            sb.Append("      t      phase     drawn.x  drawn.y   dist_to_rest   d(dist)\n");

            float worst = 0f;
            int worstIndex = -1;
            int reversals = 0;
            float prev = (_trace[0].drawn - rest).magnitude;

            // CHECK B needs to know where the AUTHORED flight ended.
            int flightEnd = -1;
            for (int i = 0; i < _trace.Count; i++)
                if (_trace[i].phase == "flight-end") { flightEnd = i; break; }

            float postTravel = 0f;
            float postWorstStep = 0f;
            int postWorstIndex = -1;

            for (int i = 0; i < _trace.Count; i++)
            {
                float d = (_trace[i].drawn - rest).magnitude;
                float step = i == 0 ? 0f : d - prev;
                bool isReversal = step > band;
                if (isReversal) { reversals++; if (step > worst) { worst = step; worstIndex = i; } }

                float moved = 0f;
                if (i > 0 && flightEnd >= 0 && i > flightEnd)
                {
                    moved = (_trace[i].drawn - _trace[i - 1].drawn).magnitude;
                    postTravel += moved;
                    if (moved > postWorstStep) { postWorstStep = moved; postWorstIndex = i; }
                }

                sb.AppendFormat("  {0:0.000}  {1,-10} {2,8:0.000} {3,8:0.000}   {4,10:0.0000}  {5,9:+0.0000;-0.0000; 0.0000}{6}{7}\n",
                                _trace[i].t, _trace[i].phase, _trace[i].drawn.x, _trace[i].drawn.y, d, step,
                                isReversal ? "   <== REVERSAL" : "",
                                moved > band ? "   <== POST-FLIGHT MOVE " + moved.ToString("0.0000") : "");
                prev = d;
            }

            // A: the flight only ever closes the gap. Catches an instant hop AWAY - the fan write.
            bool a = reversals == 0;
            // B: the flight IS the landing. Once flightDuration has elapsed the drawn sheet is home and
            //    does not move again. This is the check that catches a SMOOTH second move, which A
            //    cannot see: a correction that happens to point AT the target still reads to the player
            //    as a second move, and that is what the curl unwind was doing.
            bool b = flightEnd >= 0 && postTravel <= band;

            sb.AppendFormat("  CHECK A  flight approaches monotonically : {0} - {1} frame(s) retreated by more " +
                            "than {2:0.0000} u; worst {3:0.0000} u ({4:0.0}% of chord){5}\n",
                            a ? "GREEN" : "RED", reversals, band, worst, 100f * worst / _traceChord,
                            worstIndex >= 0 ? " in phase '" + _trace[worstIndex].phase + "'" : "");
            sb.AppendFormat("  CHECK B  the flight IS the landing      : {0} - the drawn sheet travelled " +
                            "{1:0.0000} u ({2:0.0}% of chord) AFTER the flight ended; budget {3:0.0000} u. " +
                            "Largest single frame {4:0.0000} u{5}\n",
                            b ? "GREEN" : "RED", postTravel, 100f * postTravel / _traceChord, band, postWorstStep,
                            postWorstIndex >= 0 ? " in phase '" + _trace[postWorstIndex].phase + "'" : "");

            bool green = a && b;
            sb.AppendFormat("  VERDICT {0}", green ? "GREEN" : "RED");
            LastLandingGreen = green;
            LastLandingTrace = sb.ToString();
            if (green) Debug.Log(LastLandingTrace); else Debug.LogError(LastLandingTrace);
            _tracePeel = null;
        }

        /// <summary>Marks coverage as needing a recompute; the next question about it pays for it.</summary>
        public void InvalidateCoverage() { _coverageDirty = true; }

        /// <summary>Alpha at or above which a sticker pixel counts as drawn under the finger.</summary>
        public const float TapAlphaThreshold = 0.5f;

        static bool _unreadableWarned;

        /// <summary>Turns a screen point into the world point the orthographic camera sees there.</summary>
        public Vector3 TapWorldPoint(Vector2 screenPoint)
        {
            Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
            if (cam == null) return Vector3.zero;
            return cam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, Mathf.Abs(cam.transform.position.z)));
        }

        /// <summary>
        /// Index of the still-playable sticker DRAWN under a screen point, or -1.
        ///
        /// The sprite AABB is only a cheap reject here; what decides is the sprite's own alpha at that
        /// point. A box test alone peels a sticker that is not under the finger over most of the tappable
        /// area - the sticker sprites are cut-out drawings inside rectangles, and their boxes overlap, so
        /// a tap on the chocolate bar or on bare page next to the candy cane used to peel the sweets
        /// sheet, and part of the cat's visible art used to tap through to it.
        ///
        /// Only <see cref="entries"/> are ever candidates, so the two dim decoy stickers on the page stay
        /// untappable exactly as before: this narrows what a tap can select, it never widens it.
        ///
        /// When two sheets both draw opaque pixels at the point, the one drawn ON TOP wins - what the
        /// player sees is what the player gets - and only a genuine sorting tie falls back to the nearer
        /// centre.
        /// </summary>
        public int PickSticker(Vector2 screenPoint)
        {
            Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
            if (cam == null) return -1;

            Vector3 world = TapWorldPoint(screenPoint);

            int hit = -1;
            int bestOrder = int.MinValue;
            float best = float.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                if (!Playable(i)) continue;
                Entry e = entries[i];
                Bounds b = BoundsOf(e);
                if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;

                float alpha = SpriteAlphaAt(e.sticker, world);
                if (alpha < 0f)
                {
                    // The texture is not CPU-readable, so the drawn shape cannot be consulted. Falling
                    // back to the box keeps the scene playable, but it silently restores the bug this
                    // method exists to fix, so it is reported instead of being swallowed.
                    if (!_unreadableWarned)
                    {
                        _unreadableWarned = true;
                        Debug.LogError("[Case3] " + e.sticker.name + "'s sprite texture is not readable; the tap " +
                                       "hit test has fallen back to the sprite rectangle and will peel stickers " +
                                       "that are not drawn under the finger. Tick Read/Write on the sticker sprites.");
                    }
                    alpha = 1f;
                }
                if (alpha < TapAlphaThreshold) continue;

                int order = e.sticker.sortingOrder;
                float d = (new Vector2(world.x, world.y) - new Vector2(b.center.x, b.center.y)).sqrMagnitude;
                if (order > bestOrder || (order == bestOrder && d < best))
                {
                    bestOrder = order;
                    best = d;
                    hit = i;
                }
            }
            return hit;
        }

        /// <summary>
        /// Alpha of the sprite pixel a world point lands on, or -1 when the texture cannot be read.
        /// Points outside the sprite rect read as 0.
        /// </summary>
        public static float SpriteAlphaAt(SpriteRenderer sr, Vector3 world)
        {
            if (sr == null || sr.sprite == null) return -1f;
            Sprite sp = sr.sprite;
            Texture2D tex = sp.texture;
            if (tex == null || !tex.isReadable) return -1f;

            Vector3 localPoint = sr.transform.InverseTransformPoint(world);
            Vector2 local = new Vector2(localPoint.x, localPoint.y);
            if (sr.flipX) local.x = -local.x;
            if (sr.flipY) local.y = -local.y;

            // Sprite local space puts the pivot at the origin, so pixels = units * ppu measured from the
            // pivot; the sprite's rect origin then puts that into the texture's own pixel grid.
            Vector2 px = local * sp.pixelsPerUnit + sp.pivot;
            int x = Mathf.FloorToInt(px.x + sp.rect.x);
            int y = Mathf.FloorToInt(px.y + sp.rect.y);
            if (x < sp.rect.x || y < sp.rect.y || x >= sp.rect.xMax || y >= sp.rect.yMax) return 0f;

            return tex.GetPixel(x, y).a;
        }

        /// <summary>
        /// Screen position of a point that is genuinely DRAWN on the sticker, for synthetic taps.
        ///
        /// It used to return the bounds centre, which is the one point where a box test and the drawn
        /// shape are guaranteed to agree - a gate built on it could not see a hit test that is wrong
        /// everywhere else. The centre of the opaque pixels is used instead, and if that centre happens
        /// to fall in a transparent hole (a concave drawing) the nearest opaque sample is taken.
        /// </summary>
        public Vector2 ScreenPointOf(int index)
        {
            Camera cam = sceneCamera != null ? sceneCamera : Camera.main;
            if (cam == null || index < 0 || index >= Count || entries[index] == null || entries[index].sticker == null) return Vector2.zero;
            return cam.WorldToScreenPoint(ArtPointOf(index));
        }

        /// <summary>World position of a point drawn on the sticker; the bounds centre if alpha is unreadable.</summary>
        public Vector3 ArtPointOf(int index, int samples = 41)
        {
            Entry e = entries[index];
            Bounds b = BoundsOf(e);
            if (SpriteAlphaAt(e.sticker, b.center) < 0f) return b.center;

            Vector3 sum = Vector3.zero;
            int n = 0;
            samples = Mathf.Max(3, samples);
            for (int yi = 0; yi < samples; yi++)
            {
                float fy = (yi + 0.5f) / samples;
                for (int xi = 0; xi < samples; xi++)
                {
                    float fx = (xi + 0.5f) / samples;
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                    if (SpriteAlphaAt(e.sticker, p) < TapAlphaThreshold) continue;
                    sum += p;
                    n++;
                }
            }
            if (n == 0) return b.center;

            Vector3 centre = sum / n;
            centre.z = b.center.z;
            if (SpriteAlphaAt(e.sticker, centre) >= TapAlphaThreshold) return centre;

            // Concave drawing: the centre of mass sits in a hole. Take the opaque sample nearest to it.
            Vector3 bestPoint = b.center;
            float bestDist = float.MaxValue;
            for (int yi = 0; yi < samples; yi++)
            {
                float fy = (yi + 0.5f) / samples;
                for (int xi = 0; xi < samples; xi++)
                {
                    float fx = (xi + 0.5f) / samples;
                    Vector3 p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), Mathf.Lerp(b.min.y, b.max.y, fy), b.center.z);
                    if (SpriteAlphaAt(e.sticker, p) < TapAlphaThreshold) continue;
                    float d = (p - centre).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; bestPoint = p; }
                }
            }
            return bestPoint;
        }

        static Bounds BoundsOf(Entry e)
        {
            // The sprite renderer is switched off while the curl mesh draws, and a disabled renderer
            // still reports bounds - but only the sprite's own box is meaningful for a hit test.
            return e.sticker.bounds;
        }

        /// <summary>
        /// Runs the sequence for the sticker at <paramref name="index"/>. Returns false when it cannot be
        /// played, in which case nothing changes: the other stickers stay tappable and the page keeps
        /// whatever it already collected.
        /// </summary>
        /// <summary>
        /// True while the run currently starting was started by a real press rather than by the capture
        /// harness's autoPlayForCapture path. A pressed run skips <see cref="idleDelay"/> entirely: the
        /// owner's note was "uzerine basinca direk iletsin sticker", and idleDelay exists only to line the
        /// CAPTURE up with the reference video's timestamps. Read and cleared once inside RunSequence, so
        /// the harness path is bit-identical to before and the frame indices every gate uses still hold.
        /// </summary>
        bool _pressDriven;

        /// <summary>Index the last press actually resolved to, or -1. Only used to catch a silent swap.</summary>
        int _requested = -1;

        public bool PlaySelected(int index)
        {
            if (IsPlaying || !Playable(index)) return false;

            _current = index;
            _requested = index;
            _stickerTf = entries[index].sticker.transform;
            Debug.Log("[Case3] SELECTED " + NameOf(index) + " (" + entries[index].sticker.name + ")" +
                      " -> card=" + entries[index].key + " " + (StackCount(entries[index].key) + 1) +
                      "/" + stackRequirement);
            _pressDriven = true;
            Play();

            if (!IsPlaying)
            {
                _pressDriven = false;
                Debug.LogWarning("[Case3] PlaySelected: Play() was refused (no input behind the call)");
                return false;
            }
            return true;
        }

        bool EnsureSelection()
        {
            if (Playable(_current)) { _stickerTf = entries[_current].sticker.transform; return true; }
            for (int i = 0; i < Count; i++)
            {
                if (!Playable(i)) continue;
                _current = i;
                _stickerTf = entries[i].sticker.transform;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ prewarm

        /// <summary>
        /// Pays every first-use cost before the sequence clock starts: the procedural audio bank, the
        /// particle pools, and one throwaway render of every curl mesh so the shader is compiled. Without
        /// this the first play-mode frames stall exactly where the capture clock starts.
        /// It does NOT start the sequence; the scene comes up idle and waits for a tap.
        /// </summary>
        protected override IEnumerator Start()
        {
            // FrameStripCapture may request Play after a wall-clock settle timeout. Running these
            // first-use preparations atomically prevents that harness coroutine from interleaving with
            // ResetPose and clearing the selected sticker halfway through the first captured run.
            IEnumerator warm = Prewarm();
            while (warm.MoveNext()) { }
            Ready = true;
            Debug.Log("[Case3] prewarm finished; scene is idle and waiting for a tap (" + Count + " stickers)");
            yield return base.Start();
        }

        IEnumerator Prewarm()
        {
            CaptureHome();
            AudioService.Prewarm();
            if (attachBurstPrefab != null) VFXPool.Prewarm(attachBurstPrefab, 4);
            if (flight != null && flight.sparklePrefab != null) VFXPool.Prewarm(flight.sparklePrefab, 16);

            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null || e.peel == null || e.peel.sticker == null) continue;

                e.peel.Prepare();
                e.peel.SetMeshMode(true);
                e.peel.SetProgress(0.4f);          // a frame with real curl geometry compiles the shader
                yield return null;
                e.peel.SetProgress(0.85f);
                yield return null;
                e.peel.ResetInstant();
            }

            Entry first = Count > 0 ? entries[0] : null;
            if (flight != null && flight.sparklePrefab != null && first != null && first.sticker != null)
            {
                flight.TryEmit(first.sticker.transform.position, SequenceClock);
                yield return null;
            }
            if (attachBurstPrefab != null && first != null && first.targetSlot != null)
            {
                VFXPool.Play(attachBurstPrefab, first.targetSlot.transform.position);
                yield return null;
            }

            VFXPool.ReclaimAll();
            ResetPose();          // also computes the page's opening covered/lit state
            yield return null;
        }

        // ------------------------------------------------------------------ the sequence

        protected override IEnumerator RunSequence()
        {
            // The capture harness calls Play() straight, with no pick behind it.
            if (!EnsureSelection())
            {
                Debug.LogError("[Case3] no playable sticker left; nothing to run");
                yield break;
            }

            // WHAT WAS TAPPED vs WHAT IS ABOUT TO FLY. EnsureSelection falls back to the first playable
            // entry whenever _current is not playable, and that fallback is silent: it always lands on
            // the same low-index item, which reads as "whatever I pick, it places one specific object".
            // A run that is not the run the player asked for is now loud.
            if (_requested >= 0 && _current != _requested)
                Debug.LogError(string.Format(
                    "[Case3] SELECTION DIVERGED: the press picked {0} (index {1}) but the sequence is " +
                    "running {2} (index {3}). EnsureSelection fell back because Playable({1}) went false " +
                    "between the press and the first frame of the run.",
                    NameOf(_requested), _requested, NameOf(_current), _current));
            _requested = -1;

            Entry cur = Current;
            StickerPeel peel = cur.peel;
            SpriteRenderer targetSlot = cur.targetSlot;

            if (peel == null || peel.sticker == null || flight == null || targetSlot == null)
            {
                Debug.LogError("[Case3] Director is not wired; run Case3SceneSetup.Build.");
                yield break;
            }

            CaptureHome();
            // Lift the whole piece of paper over the cards BEFORE Prepare, because the curl mesh
            // takes its own sorting order from the sprite's at that moment.
            cur.sticker.sortingOrder = Mathf.Max(cur.HomeSortingOrder, CarrySortingOrder());
            peel.Prepare();

            Debug.Log(string.Format("[Case3] RUN_BEGIN item={0} ({1}) -> card={2} slot={3}",
                NameOf(_current), cur.sticker.name, cur.key, targetSlot.name));

            _t0 = SequenceClock;
            _startFrame = Time.frameCount;

            // A real press transfers immediately; only the capture harness waits out idleDelay.
            float tIdle = _pressDriven ? 0f : idleDelay;
            _pressDriven = false;
            float tTap = tIdle + tapDuration;
            float tPeel = tTap + peelDuration;
            float tFlight = tPeel + flightDuration;
            float tFlip = tFlight + flipDuration;
            float tPop = tFlip + popDuration;
            float tEnd = tPop + settleDuration;

            Vector3 homePosition = cur.HomePosition;
            Vector3 homeScale = cur.HomeScale;
            Vector3 slotScale = targetSlot.transform.localScale;

            // ONE resting place, decided HERE, before anything moves, and never touched again.
            // The fan offset for a stacked sheet is part of it - not something applied after the sheet
            // has already parked in the middle of the card. Commit 989f281 applied it at attach with an
            // instant write, and the end-of-settle line then snapped the sheet back to the slot centre:
            // measured on a second cup of ramen, +0.331 u out at t=0.821 and -0.331 u back at t=1.002.
            // The owner saw both. The offset also did not survive the snap, so the pile it was meant to
            // make was never on screen at all.
            int fanIndex = StackCount(cur.key);
            Vector3 restCentre = RestingPlace(cur, fanIndex);
            Quaternion restRotation = cur.reward != null ? cur.reward.transform.rotation : Quaternion.identity;

            peel.SetMeshMode(true);
            peel.SetProgress(0f);
            peel.SetAlpha(1f);
            flight.ResetTrail(SequenceClock);
            // Clearing the card here is right for the FIRST of a kind and wrong for every one after it:
            // the card is ONE renderer shared by the whole kind, so zeroing it wiped the cat that was
            // already collected. Frame-by-frame, the filled card - art, name tab and counter together -
            // vanished for ~0.65 s between the first and second cat and came back with the new art.
            // The reference never blanks a card it has already filled.
            if (StackCount(cur.key) == 0) SetRewardAlpha(cur, 0f);

            // ---------------------------------------------------------- 0. idle on page (0.00 to 0.75s)
            if (tIdle > 0.001f)
            {
                BeginStep("idle");
                while (SequenceClock < _t0 + tIdle)
                {
                    yield return null;
                }
                EndStep();
            }

            // ---------------------------------------------------------- 1. tap wind-up + curl
            BeginStep("peel");
            AudioService.Play(SfxId.TapPop, 0.55f);
            Fire(JuiceEvent.Anticipation,
                 string.Format("{0:0.00} s dip and widen on the strip before the corner lifts", tapDuration));

            while (SequenceClock < _t0 + tTap)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tIdle) / Mathf.Max(0.0001f, tapDuration));
                float w = Mathf.Sin(Mathf.PI * k);
                _stickerTf.position = homePosition + Vector3.down * (0.10f * w);
                _stickerTf.localScale = Vector3.Scale(homeScale, new Vector3(1f + 0.025f * w, 1f - 0.045f * w, 1f));
                yield return null;
            }
            _stickerTf.localScale = homeScale;

            AudioService.Play(SfxId.PeelShhh, 0.72f);
            Fire(JuiceEvent.Deform,
                 string.Format("page curl on a {0}x{0} grid mesh, cylinder radius {1:0.000} local units, " +
                               "wrap clamped at 180 deg so the white back turns to camera",
                               peel.segments, peel.CurlRadius));

            bool secondPeelLayer = false;

            // The edge that stays stuck for the whole peel, taken while the sheet is still flat.
            //
            // The CENTRE is the wrong thing to hold here, and holding it is what the owner has been
            // describing all along: "olacagi yerde ters donuyor gibi", "kendi etrafinda rotasyon
            // gibi". As a sheet rolls up its drawn centre naturally travels toward the stuck edge, so
            // forbidding the centre to move leaves rotation as the only thing left for it to do.
            // Hold the edge and the middle is free to come with the roll, which is what paper does.
            Vector3 drawnHome = peel.VisualWorldCentre(9);
            Vector3 peelEdgeHome = peel.AnchoredEdgeWorld();

            while (SequenceClock < _t0 + tPeel)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tTap) / Mathf.Max(0.0001f, peelDuration));
                // OutCubic, not InOutSine: the sheet rips free fast and then settles, which is what the
                // reference does, and it also parks the half-peeled state (flap folded back over the art,
                // roll clearly rounded) where the frame strip actually samples it.
                float e = Ease.Evaluate(EaseType.OutCubic, k);

                peel.SetProgress(e * peelEnd);
                // Pin the stuck EDGE, not the transform and not the centre.
                //
                // Writing the transform let the paper slide: the curl's centroid compensation moves
                // the drawn mesh away from its own transform as the roll grows - up to 3.23 world
                // units on the cat - so the transform sat still and the sheet wandered off ("random
                // bir yere giderek aciliyor"). Pinning the centre fixes that but buys the other
                // complaint, rotation in place. Pinning the edge fixes both.
                Vector3 lift = new Vector3(0f, peelLift * e, -0.05f * e);
                Vector3 edgeNowP = peel.AnchoredEdgeWorld();
                Vector3 centreNowP = peel.VisualWorldCentre(9);
                PlaceDrawnAt(peel, centreNowP + (peelEdgeHome + lift - edgeNowP));

                // Two-layer rule from .plan-build/audio.md: main hit, second accent 0.10-0.14 s later.
                if (!secondPeelLayer && SequenceClock - _t0 >= tTap + 0.10f)
                {
                    AudioService.Play(SfxId.PeelShhh, 0.22f, 1.18f);
                    secondPeelLayer = true;
                }
                yield return null;
            }

            peel.SetProgress(peelEnd);
            // The flight starts from exactly where the peel left the PAPER, which is now a known
            // point rather than a measured one: the peel pinned it there every frame. Measuring it
            // again would only reintroduce the risk of the two disagreeing by a frame.
            // The flight starts from wherever holding the edge has left the paper - read it, do not
            // assume it, because the centre has legitimately travelled during the roll.
            Vector3 endLift = new Vector3(0f, peelLift, -0.05f);
            Vector3 edgeEnd = peel.AnchoredEdgeWorld();
            Vector3 centreEnd = peel.VisualWorldCentre(9);
            PlaceDrawnAt(peel, centreEnd + (peelEdgeHome + endLift - edgeEnd));
            Vector3 drawnLaunch = peel.VisualWorldCentre(9);
            drawnLaunch.z = drawnHome.z + endLift.z;

            // PEEL COMPLETION IS THE PROMOTION INSTANT. In the reference the jar is uncovered at
            // t=4.11 and is a fully collectible sticker from that frame on - it is tapped and
            // collected at t=7.12 - and the change lands as the sheet above it comes free, about
            // 0.72 s before that sheet finishes landing. Not at the landing, not over a fade.
            // Coverage is not recomputed continuously and must not be: the curl mesh still lies
            // over the page for the whole peel, and the sheet's own SpriteRenderer went off at the
            // start of the run. This one call is the decision, and this is the instant to take it.
            RecomputeCoverage();
            EndStep();

            // ---------------------------------------------------------- 2. flight
            BeginStep("flight");
            AudioService.Play(SfxId.WhooshArc, 0.28f);
            Fire(JuiceEvent.Trail, "yellow-lime dotted star trail follows the curled white paper back");

            Vector3 flightEndScale = slotScale * flightShrink;

            TraceBegin(peel, Vector3.Distance(drawnLaunch, restCentre));
            TraceMark("flight");

            while (SequenceClock < _t0 + tFlight)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tPeel) / Mathf.Max(0.0001f, flightDuration));
                // OutQuad, not InOutQuad. MEASURED off Stickerdom.mp4: the reference's first landing was
                // tracked frame by frame from t=1.074 to t=1.406 and its arc-length progress fitted
                // against five curves - OutQuad RMSE 0.079, linear 0.105, InOutQuad 0.134, OutCubic
                // 0.173, OutQuart 0.233. The reference's peak speed lands at about a quarter of the way
                // through and decays from there; InOutQuad peaks dead centre and crawls at both ends,
                // which is why the sheet used to appear to stall just short of the card.
                float e = Ease.Evaluate(EaseType.OutQuad, k);

                _stickerTf.localScale = Vector3.Lerp(homeScale, flightEndScale, e);
                // Squaring to the card happens ALONG the flight. It used to be an instant
                // `sheet.rotation = card.rotation` at attach, which is a hop of its own on the four
                // page items that are authored at an angle.
                _stickerTf.rotation = Quaternion.Slerp(cur.HomeRotation, restRotation, e);

                // Curled and blank for the WHOLE flight - this is what the reference does, and the
                // owner's own frames confirm it: a white rolled tube crosses the board and only
                // unrolls once it is sitting in its card.
                //
                // I briefly unwound this in flight, reading "sanki flip oluyor gibi" as a complaint
                // about arriving folded. It was not. The complaint was that the unroll IN THE SLOT was
                // over in 0.12 s, which reads as a snap rather than as paper relaxing open. The curl
                // stays; the unroll got the time instead.
                peel.SetProgress(peelEnd);

                Vector3 want = flight.Evaluate(drawnLaunch, restCentre, e);
                PlaceDrawnAt(peel, want);
                // The trail follows the PAPER, not the transform the paper is hanging off.
                flight.TryEmit(want, SequenceClock);
                TraceMark("flight");
                yield return null;
            }

            _stickerTf.localScale = flightEndScale;
            _stickerTf.rotation = restRotation;
            peel.SetProgress(peelEnd);
            PlaceDrawnAt(peel, restCentre);
            TraceMark("flight-end");
            EndStep();

            // ---------------------------------------------------------- 3. flip back to the printed face
            BeginStep("flip");
            Fire(JuiceEvent.Deform,
                 "the last of the curl is pressed out and the sheet beds down onto the card");

            // The sheet is at its slot now. The curl is about to unwind back to progress 0, and the
            // contact shadow keys off progress, so without this it would fade straight back IN on top of
            // the card as the sheet flattens. A placed sticker is printed onto the page and casts nothing.
            peel.MarkPlaced();

            // The edge that will stay put for the whole unroll, taken while the sheet is still fully
            // curled and already sitting at its card.
            Vector3 edgeAnchor = peel.AnchoredEdgeWorld();

            while (SequenceClock < _t0 + tFlip)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tFlight) / Mathf.Max(0.0001f, flipDuration));
                float e = Ease.Evaluate(EaseType.OutCubic, k);

                peel.SetProgress(Mathf.Lerp(peelEnd, 0f, e));
                // Press, not shrink: the sheet dips slightly under its settled size and comes back,
                // which is what a thumb smoothing a sticker down looks like. The old lerp only ever
                // approached 0.99 from above, so the beat had no contact in it.
                float press = 1f - 0.045f * Mathf.Sin(k * Mathf.PI);
                _stickerTf.localScale = Vector3.Lerp(flightEndScale, slotScale, Ease.Evaluate(EaseType.OutQuad, k)) * press;
                // Re-anchored EVERY frame of the unwind. The curl offset collapses as the paper
                // flattens, and holding the transform still while that happens is precisely what used
                // to drag the drawn sheet 1.18 u across the album in 27 ms. Pinning the DRAWN centre
                // instead lets the paper unwind in place, which is what the reference does.
                // Hold the STUCK EDGE, not the centre. Pinning the centre is what made the sheet
                // turn about itself - paper does not do that. One edge holds, the rest opens out from
                // it, and the centre travels the half-length it gains as the roll flattens.
                Vector3 edgeNow = peel.AnchoredEdgeWorld();
                PlaceDrawnAt(peel, PeelDrawnCentre(peel, edgeAnchor, edgeNow, restCentre, e));
                TraceMark("flip");
                yield return null;
            }

            peel.SetProgress(0f);
            peel.SetMeshMode(false);
            PlaceDrawnAt(peel, restCentre);
            TraceMark("flip-end");
            EndStep();

            // ---------------------------------------------------------- 4. attach
            BeginStep("attach");
            float attachTime = SequenceTime;

            if (attachBurstPrefab != null)
            {
                // 0.42 shrank both the particle size AND the spread: 0.1-0.25 world units became
                // 4.2-10.5 px at 100 px/unit, in a cluster ~60 px wide. The reference resolves 7-15
                // elements of 10-20 px across a ~150 px smear. One number moves both.
                GameObject burst = VFXPool.Play(attachBurstPrefab,
                    restCentre + new Vector3(0f, 0f, -0.20f), Quaternion.identity, 1.30f);
                RestartSeededParticles(burst, (uint)(0xC3A770u + _current * 101));
            }

            // The reference-filled card crop contains the attached art and its green name tab.
            // Cross-fade to those exact pixels at arrival instead of rebuilding them with substitute UI.
            //
            // STACKING. The card is shared by every item of its kind. The FIRST ramen fades the card
            // in from nothing; the second must not blink the card out and fade it back in, because in
            // the reference the card stays put and only its counter moves. So the fade starts from
            // where the card already is, and the counter is pushed here, before the pop, so the number
            // the card pops with is the new one.
            bool firstOfKind = StackCount(cur.key) == 0;
            PushStack(cur.key);
            if (cur.reward != null)
            {
                // THE SHEET THAT FLEW IS WHAT THE CARD SHOWS. Every one of them, the first included.
                //
                // The first of a kind used to switch its own renderer OFF and hand the card over to
                // card_filled_<key>.png, which has ONE item baked into it. There are 14 tappable
                // entries and 3 of those cards, so the first Sweets collected always drew the candy
                // cane whatever was tapped: the owner tapped the chocolate bar and got "ilkini her
                // zaman seker koyuyor". Proved rather than guessed - on a fresh session the tap
                // resolved PickSticker -> 9 (Chocolate), PlaySelected(9), _current -1 -> 9, and the
                // chocolate did leave the page; only the card's pixels were somebody else's. Every
                // LATER item of a kind already stayed on screen, which is exactly why he saw the
                // first as fixed and the rest as varying.
                //
                // So nothing gets hidden any more. The card supplies the frame, the green name tab
                // and the counter; the sheet supplies the subject, drawn over them.
                //
                // DEVIATION, recorded rather than fitted: card_filled_* still has an item baked into
                // its middle, and the landed sheet only covers it because it is bigger - measured
                // footprints run 1.02 x 1.27 u (ramen tin) to 2.11 x 2.48 u (cat) against a baked art
                // box of roughly 1.05 x 1.58 u. The small ones leave an edge of the wrong drawing
                // showing. That is a texture problem, not a code one, and it disappears the moment
                // card_filled_* becomes a frame with a transparent interior.
                // Page dressing only: the contact shadow goes, the die-cut rim stays. Switching every
                // companion off here took the sticker's white border with it, so the landed sheet sat
                // on the card with no die cut while the reference's card subject plainly has one.
                if (cur.peel != null) cur.peel.SetPageDressingEnabled(false);
                cur.sticker.sortingOrder = CarrySortingOrder() + StackCount(cur.key);
                // Drag the rim up with the sheet. Without this the die cut stays in the page band and
                // is drawn under the card, so the landed sticker has no white border.
                if (cur.peel != null) cur.peel.SyncMeshSorting();
                // NO position write here. The fan offset was folded into restCentre before the
                // flight began, so the sheet has already been sitting on its fanned spot since it
                // landed. Attach is a visual event now, not a move.
            }
            TraceMark("attach");

            AudioService.PlayLayered(SfxId.AttachPop, SfxId.RippleTick, 0.055f);

            Fire(JuiceEvent.ImpactVFX, "tight yellow-lime star burst and filled reward card at the target");
            Fire(JuiceEvent.Overshoot,
                 string.Format("landing peaks at {0:0.00}x the slot size before settling back to 1.00x",
                               1f + popStretch));
            Fire(JuiceEvent.SquashStretch,
                 string.Format("+{0:0.00} on X / -{1:0.00} on Y as it splats onto the page", popStretch, popStretch * 0.55f));

            while (SequenceClock < _t0 + tPop)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tFlip) / Mathf.Max(0.0001f, popDuration));
                float w = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f));
                if (cur.reward != null)
                {
                    SetRewardAlpha(cur, firstOfKind ? Ease.Evaluate(EaseType.OutCubic, k) : 1f);
                    cur.reward.transform.localScale = Vector3.Scale(cur.RewardHomeScale,
                        new Vector3(1f + popStretch * w, 1f - popStretch * 0.55f * w, 1f));
                }
                else
                {
                    _stickerTf.localScale = Vector3.Scale(slotScale,
                        new Vector3(1f + popStretch * w, 1f - popStretch * 0.55f * w, 1f));
                }
                TraceMark("pop");
                yield return null;
            }

            while (SequenceClock < _t0 + tEnd)
            {
                float s = SequenceClock - _t0 - tPop;
                float d = 0.055f * Mathf.Exp(-6.5f * s) * Mathf.Cos(18f * s);
                if (cur.reward != null)
                    cur.reward.transform.localScale = Vector3.Scale(cur.RewardHomeScale,
                        new Vector3(1f + d, 1f - d * 0.6f, 1f));
                else
                    _stickerTf.localScale = Vector3.Scale(slotScale, new Vector3(1f + d, 1f - d * 0.6f, 1f));
                TraceMark("settle");
                yield return null;
            }

            _stickerTf.localScale = slotScale;
            PlaceDrawnAt(peel, restCentre);
            TraceMark("final");
            TraceEnd();
            if (cur.reward != null)
            {
                cur.reward.transform.localScale = cur.RewardHomeScale;
                SetRewardAlpha(cur, 1f);
            }
            cur.Consumed = true;
            RecomputeCoverage();
            EndStep();

            Debug.Log(string.Format(
                "[Case3] PROOF peel start -> attach = {0:0.000} s (visual target ~0.8-1.1 s); " +
                "run = {1:0.000} s over {2} frames ({3:0.0} fps); sparkle bursts = {4}",
                attachTime - tapDuration, SequenceTime, Time.frameCount - _startFrame,
                (Time.frameCount - _startFrame) / Mathf.Max(0.001f, SequenceTime),
                flight != null ? flight.EmittedCount : 0));
            Debug.Log(string.Format("[Case3] RUN_END {0} (sprite '{1}') landed on the {2} card at {3}/{4}; " +
                                    "the card now draws sprite '{5}'; {6} item(s) still tappable",
                NameOf(_current), cur.sticker != null && cur.sticker.sprite != null ? cur.sticker.sprite.name : "?",
                cur.key, StackCount(cur.key), stackRequirement,
                cur.reward != null && cur.reward.sprite != null ? cur.reward.sprite.name : "?",
                PlayableCount()));
        }

        int PlayableCount()
        {
            int n = 0;
            for (int i = 0; i < Count; i++) if (Playable(i)) n++;
            return n;
        }

        // ------------------------------------------------------------------ reset

        /// <summary>
        /// Full reset back to the untouched scene. Only Replay() (the capture harness) uses it; a player
        /// tap never resets, because the point of the scene is that each sticker the player places stays.
        /// </summary>
        protected override void ResetState()
        {
            StopAllCoroutines();
            ResetPose();
        }

        void ResetPose()
        {
            CaptureHome();

            for (int i = 0; i < Count; i++)
            {
                Entry e = entries[i];
                if (e == null) continue;

                if (e.peel != null)
                {
                    Squash.Cancel(e.peel.transform);
                    e.peel.ResetInstant();
                }
                if (e.peel != null) e.peel.SetCompanionsEnabled(true);
                if (e.sticker != null)
                {
                    e.sticker.enabled = true;
                    e.sticker.sortingOrder = e.HomeSortingOrder;
                    Transform t = e.sticker.transform;
                    Squash.Cancel(t);
                    t.position = e.HomePosition;
                    t.rotation = e.HomeRotation;
                    t.localScale = e.HomeScale;
                }
                if (e.reward != null)
                {
                    e.reward.transform.localScale = e.RewardHomeScale;
                    SetRewardAlpha(e, 0f);
                }
                e.Consumed = false;
            }

            _current = -1;
            _stickerTf = null;
            ResetStacks();

            // Back to the page's opening state, dim items included. Without this a replay would
            // start with whatever was promoted during the previous run still lit.
            InvalidateCoverage();
            RecomputeCoverage();

            if (flight != null) flight.ResetTrail(SequenceClock);

            VFXPool.ReclaimAll();
            HitstopService.Resume();
            CameraShakeService.StopAll();
            Tweener.CancelAll();
        }

        void SetRewardAlpha(Entry e, float alpha)
        {
            if (e == null || e.reward == null) return;
            Color c = e.RewardHomeColor;
            c.a = Mathf.Clamp01(alpha);
            e.reward.color = c;
            e.reward.enabled = c.a > 0.001f;

            // The counter is a separate renderer sitting on the card, so it has to be faded by the
            // same hand or it would snap on ahead of the card it belongs to.
            StackCard sc = StackOf(e.key);
            if (sc != null && sc.counter != null)
            {
                Color t = sc.counter.color;
                t.a = c.a;
                sc.counter.color = t;
                sc.counter.enabled = sc.Collected > 0 && c.a > 0.001f;
            }
        }

        /// <summary>The yellow-lime the reference's landing burst measures at; same value as StickerFlight.sparkleColor.</summary>
        static readonly Color BurstColor = new Color(0.93f, 1f, 0.16f, 1f);

        static void RestartSeededParticles(GameObject root, uint seed)
        {
            if (root == null) return;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.useAutoRandomSeed = false;
                system.randomSeed = seed + (uint)(i * 97);

                // The burst kept the prefab's WHITE start colour. The trail is recoloured to
                // sparkleColor at runtime and the burst had no equivalent line, so the landing read as
                // a pale bloom while ImpactVFX was logged as a "yellow-lime star burst".
                ParticleSystem.MainModule main = system.main;
                main.startColor = BurstColor;

                // The landing burst had NO sorting order set anywhere - it kept the prefab's 0 and
                // therefore drew behind the page (50), the cards (200s) and the reward art (600s),
                // while ImpactVFX was logged as fired. Same lever as the trail; see
                // StickerFlight.VfxSortingOrder.
                ParticleSystemRenderer psr = system.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    psr.sortingOrder = StickerFlight.VfxSortingOrder + 1;

                    // PFX_StickerStar is ADDITIVE, and additive can only RAISE the blue channel. The
                    // reward card sits at B=159; in the reference 98% of the burst's pixels have their
                    // blue PULLED DOWN, landing at B=84. An additive burst therefore cannot reach the
                    // reference's colour over this card at any size or count - it has to cover the card,
                    // not add to it. psr.material is a per-instance copy, so the trail's shared
                    // PFX_StickerStar asset stays additive and its measurement is untouched.
                    Material instanced = psr.material;
                    if (instanced != null)
                    {
                        instanced.SetFloat("_Blend", 0f);
                        instanced.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        instanced.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        instanced.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    }
                }

                system.Play(true);
            }
        }

        // ------------------------------------------------------------------ real tap

        /// <summary>
        /// Lets a person actually tap a sticker - any of them. The press is resolved to the sticker it
        /// landed on and only that sticker peels; a press on empty page is ignored and the scene stays as
        /// it is, still ready for the next pick.
        /// </summary>
        void Update()
        {
            if (IsPlaying) return;

            Vector2 screen;
            if (!TapThisFrame(out screen)) return;

            int index = PickSticker(screen);
            if (index < 0)
            {
                Debug.Log("[Case3] press at " + screen + " hit no playable sticker; ignored");
                return;
            }

            // Tell the sheet where the finger landed. The reference peels AWAY from the tap - all three
            // of its peels start at the sheet end farthest from the tap point - so the curl direction is
            // the player's, not a constant. No origin means the sticker keeps its own fallback angle.
            if (entries[index] != null && entries[index].peel != null)
                entries[index].peel.SetPeelOrigin(TapWorldPoint(screen));

            PlaySelected(index);
        }

        static bool TapThisFrame(out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = touch.primaryTouch.position.ReadValue();
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }
            return false;
        }
    }
}
