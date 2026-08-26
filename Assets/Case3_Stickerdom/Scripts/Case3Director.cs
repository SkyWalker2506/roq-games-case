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
        public float flipDuration = 0.12f;
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
            c.counter.text = c.Collected + "/" + Mathf.Max(1, stackRequirement);
            c.counter.enabled = c.Collected > 0;
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
        void FanOntoStack(Transform sheet, SpriteRenderer card, int index)
        {
            if (sheet == null || card == null || card.sprite == null) return;
            float w = card.sprite.bounds.size.x * card.transform.lossyScale.x;
            float side = (index % 2 == 1) ? 1f : -1f;
            float depth = 1f + (index - 1) * 0.55f;
            sheet.position = sheet.position + new Vector3(side * w * 0.10f * depth, -w * 0.055f * depth, -0.01f * index);
            // Square to the card. A page sticker is authored at an angle so the pile on the desk looks
            // scattered, and the sheet carries that angle all the way to the album unless it is squared
            // here - which is the other half of "yamuk yapma": not just no fan, no inherited tilt either.
            sheet.rotation = card.transform.rotation;
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

        public bool PlaySelected(int index)
        {
            if (IsPlaying || !Playable(index)) return false;

            _current = index;
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
            cur.sticker.sortingOrder = Mathf.Max(cur.HomeSortingOrder, flightSortingOrder);
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
            Vector3 slotPos = targetSlot.transform.position;
            slotPos.z = targetSlot.transform.position.z - 0.15f;
            Vector3 slotScale = targetSlot.transform.localScale;

            peel.SetMeshMode(true);
            peel.SetProgress(0f);
            peel.SetAlpha(1f);
            flight.ResetTrail(SequenceClock);
            SetRewardAlpha(cur, 0f);

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

            while (SequenceClock < _t0 + tPeel)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tTap) / Mathf.Max(0.0001f, peelDuration));
                // OutCubic, not InOutSine: the sheet rips free fast and then settles, which is what the
                // reference does, and it also parks the half-peeled state (flap folded back over the art,
                // roll clearly rounded) where the frame strip actually samples it.
                float e = Ease.Evaluate(EaseType.OutCubic, k);

                peel.SetProgress(e * peelEnd);
                _stickerTf.position = homePosition + new Vector3(0f, peelLift * e, -0.05f * e);

                // Two-layer rule from .plan-build/audio.md: main hit, second accent 0.10-0.14 s later.
                if (!secondPeelLayer && SequenceClock - _t0 >= tTap + 0.10f)
                {
                    AudioService.Play(SfxId.PeelShhh, 0.22f, 1.18f);
                    secondPeelLayer = true;
                }
                yield return null;
            }

            peel.SetProgress(peelEnd);
            Vector3 launchPos = homePosition + new Vector3(0f, peelLift, -0.05f);
            _stickerTf.position = launchPos;

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

            while (SequenceClock < _t0 + tFlight)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tPeel) / Mathf.Max(0.0001f, flightDuration));
                float e = Ease.Evaluate(EaseType.InOutQuad, k);

                _stickerTf.position = flight.Evaluate(launchPos, slotPos, e);
                _stickerTf.localScale = Vector3.Lerp(homeScale, flightEndScale, e);

                // The reference keeps the blank, curled paper back visible for the whole flight.
                // The printed face is revealed only once the sheet reaches its card.
                peel.SetProgress(peelEnd);
                flight.TryEmit(_stickerTf.position, SequenceClock);
                yield return null;
            }

            _stickerTf.position = slotPos;
            _stickerTf.localScale = flightEndScale;
            EndStep();

            // ---------------------------------------------------------- 3. flip back to the printed face
            BeginStep("flip");
            Fire(JuiceEvent.Deform,
                 "the white paper back flips flat at the target and reveals the printed reward face");

            // The sheet is at its slot now. The curl is about to unwind back to progress 0, and the
            // contact shadow keys off progress, so without this it would fade straight back IN on top of
            // the card as the sheet flattens. A placed sticker is printed onto the page and casts nothing.
            peel.MarkPlaced();

            while (SequenceClock < _t0 + tFlip)
            {
                float k = Mathf.Clamp01((SequenceClock - _t0 - tFlight) / Mathf.Max(0.0001f, flipDuration));
                float e = Ease.Evaluate(EaseType.OutCubic, k);

                peel.SetProgress(Mathf.Lerp(peelEnd, 0f, e));
                _stickerTf.localScale = Vector3.Lerp(flightEndScale, slotScale * 0.99f, Ease.Evaluate(EaseType.OutQuad, k));
                _stickerTf.position = slotPos;
                yield return null;
            }

            peel.SetProgress(0f);
            peel.SetMeshMode(false);
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
                    slotPos + new Vector3(0f, 0f, -0.20f), Quaternion.identity, 1.30f);
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
                // The FIRST of a kind hands the card its printed face and then gets out of the way.
                // Every one after it STAYS on screen, fanned over the pile: the card is one renderer
                // shared by every item of its kind, so hiding the second sheet too left the counter
                // reading 2/5 with pixels identical to 1/5 - the owner's "it lands on the empty panel
                // even when it is full". A stack has to be visible to be a stack.
                if (firstOfKind)
                {
                    cur.sticker.enabled = false;
                    if (cur.peel != null) cur.peel.SetCompanionsEnabled(false);
                    SetRewardAlpha(cur, 0f);
                }
                else
                {
                    if (cur.peel != null) cur.peel.SetCompanionsEnabled(false);
                    cur.sticker.sortingOrder = cur.reward.sortingOrder + StackCount(cur.key);
                    FanOntoStack(cur.sticker.transform, cur.reward, StackCount(cur.key) - 1);
                }
            }

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
                yield return null;
            }

            _stickerTf.localScale = slotScale;
            _stickerTf.position = slotPos;
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
            Debug.Log(string.Format("[Case3] RUN_END {0} landed on the {1} card at {2}/{3}; {4} item(s) still tappable",
                NameOf(_current), cur.key, StackCount(cur.key), stackRequirement, PlayableCount()));
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
