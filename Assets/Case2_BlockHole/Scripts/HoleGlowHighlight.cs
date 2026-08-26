using UnityEngine;

namespace Case2
{
    /// <summary>
    /// One hole on the board. Owns the two visual layers the reference video shows around a hole:
    /// a neon glow that only lights up for a block whose shape matches this hole, and a dark gradient
    /// pit that opens while the shards are falling and closes again afterwards.
    /// Both layers are driven through a <see cref="MaterialPropertyBlock"/>; no material is copied.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoleGlowHighlight : MonoBehaviour
    {
        [Header("Identity (filled in by Case2SceneSetup)")]
        [Tooltip("Shape this hole accepts: Single, 2, L. A block only lights this hole when the keys match.")]
        public string shapeKey = "";
        public BlockShapeId shapeId = BlockShapeId.Unknown;

        [Tooltip("Neon colour of the glow; taken from the hole's own art material so hole and block agree.")]
        public Color neonColor = Color.white;

        [Header("Materials / meshes (filled in by Case2SceneSetup)")]
        [Tooltip("Additive unlit material used for the neon glow plate.")]
        public Material neonMaterial;

        [Tooltip("Lit plastic material used for the permanent thick colour lip.")]
        public Material rimMaterial;

        [Tooltip("Radial dark gradient material used for the pit interior.")]
        public Material pitMaterial;

        [Tooltip("Silhouette of the matching block; both the glow and the pit use it, so the hole lights and opens in the exact block shape.")]
        public Mesh silhouetteMesh;

        [Header("Feel")]
        // Three of the four fields below were dead - authored into BlockHole.unity on all four
        // holes with hand-tuned-looking values and read by nothing. Two of them are dead AND
        // WRONG, which is why they were not simply reconnected. Measured off Block Hole.mp4 at
        // 65 fps by differencing a rest frame against a held one:
        //
        //   glowSpreadMin 1.02 / glowSpreadMax 1.10. These are silhouette scale multipliers, i.e.
        //   a spill of 2% to 10% past the mouth. The reference's halo reaches 0.45 CELLS, which
        //   on the 3-cell cross is 30% of its half-extent and on the 1-cell bar is 90%. A single
        //   scale multiplier cannot express that at all - the reach is a distance, not a ratio -
        //   so the halo is driven by glowReachCells below and these two stay dead.
        //
        //   pulseHz 2.1. There is NO pulse. The green hole's halo steps from 0 to full between
        //   f198 and f199 - one frame, 15 ms - and then holds +20.10, +20.13, +20.09 ... +19.81
        //   for the next 25 frames (0.38 s), a drift of 1.4%. A 2.1 Hz pulse has a 0.48 s period
        //   and could not be flat across 0.38 s. The red hole behaves the same way from f736.
        //
        //   glowGain 0.48 looked right for one round and is not. It matched an annulus MEAN,
        //   which is the wrong instrument for this effect: the halo is a near-saturated core with
        //   a fast roll-off, and averaging that over a ring returns a broad weak wash. Built that
        //   way it produced a flat purple square where the reference has a thin bright rim. On a
        //   straight edge the alpha solves to ~1.0 - the reference REPLACES the board pixel with
        //   the hole's colour, (34,47,96) becoming (0,174,1) at 0.048 cells out.
        [Tooltip("DEAD and WRONG: a silhouette scale multiplier cannot express the reference's halo, "
            + "which reaches a fixed 0.45 cells whatever the hole's size. See glowReachCells.")]
        public float glowSpreadMin = 1.35f;

        [Tooltip("DEAD and WRONG: see glowSpreadMin.")]
        public float glowSpreadMax = 1.48f;

        [Tooltip("DEAD and WRONG. It looked right against a mean taken over an annulus, which is "
            + "the wrong measurement: the reference's halo is a near-saturated core with a fast "
            + "roll-off, and an annulus average of that returns a broad weak wash. On a straight "
            + "edge the alpha solves to ~1.0, not 0.48. See glowPeakAlpha.")]
        public float glowGain = 0.95f;

        [Tooltip("DEAD and WRONG: the reference's halo does not pulse. It steps on in one frame "
            + "and holds flat to within 1.4% for the whole hold.")]
        public float pulseHz = 2.1f;

        [Tooltip("How far past the mouth the halo reaches, in world cells. MEASURED on a straight "
            + "edge of the reference's green hole: the difference is +126 on its own channel at "
            + "0.048 cells, +27.8 at 0.217, +2.6 at 0.290 and 0 by 0.314. The pit plate spans "
            + "+-2 cells (pitCoverScale 4) and the widest shape leaves 0.5 cells of margin, so "
            + "0.32 fits with room.")]
        public float glowReachCells = 0.32f;

        [Tooltip("How far out the halo stays fully saturated before it starts to roll off, in "
            + "world cells. MEASURED: solving lit = (1-a)*rest + a*neon on the reference gives "
            + "a = ~1.0 from the rim out to 0.07 cells.")]
        public float glowCoreCells = 0.08f;

        [Tooltip("Alpha inside the core. MEASURED as ~1.0 - at 0.048 cells the reference's board "
            + "pixel is REPLACED by its hole's colour, (34,47,96) becoming (0,174,1). This is what "
            + "the scene's authored glowGain 0.48 got wrong.")]
        [Range(0f, 1f)] public float glowPeakAlpha = 1.0f;

        [Header("Halo band shape and motion")]
        [Tooltip("How far the rim is pushed towards white. Spends the 'brighter' budget on a narrow "
            + "inner band rather than on the whole halo, because a wide bright band is exactly the "
            + "blur being complained about.")]
        [Range(0f, 1f)] public float glowRimWhiteness = 0.55f;

        [Tooltip("How far past 1 the rim is lifted so it reads as a light source. Kept small: "
            + "bloom lives in the CaseGrades asset and is not ours to retune.")]
        [Range(1f, 2f)] public float glowRimGain = 1.15f;

        [Tooltip("Amplitude of the travelling wave, as a fraction of the reach. MEASURED: on the "
            + "reference's green hole over f206-f260, a 0.04-0.30 cell ring split into eight "
            + "angular sectors still swings 15-32% per sector once the global level is divided out, "
            + "so the outline moves. A whole-ring average cannot see that, which is how the note on "
            + "pulseHz came to call the halo flat.")]
        [Range(0f, 0.6f)] public float glowWaveAmplitude = 0.20f;

        [Tooltip("Lobes around the outline. CHOSEN, not measured - a short hold at 65 fps will not "
            + "settle a spatial period.")]
        [Range(1f, 16f)] public float glowWaveLobes = 6f;

        [Tooltip("How fast the wave travels around the outline. CHOSEN, not measured.")]
        [Range(0f, 4f)] public float glowWaveRevolutionsPerSecond = 0.9f;

        [Tooltip("Seconds the halo takes to fade out once the hole stops being the target. "
            + "MEASURED on the reference's red hole: the band falls from its peak to its floor "
            + "over f897-f907, about 0.15 s. Switching ON is not ramped at all - the reference "
            + "steps in a single 15 ms frame.")]
        public float glowFadeOutSeconds = 0.15f;

        [Tooltip("Height above the hole the neon plate sits at, so it draws over the floor tiles.")]
        public float glowHeight = 0.008f;

        [Tooltip("Height above the hole the dark pit sits at.")]
        public float pitHeight = 0.016f;

        [Tooltip("Pit size relative to the silhouette; below 1 so the hole's own coloured lip stays visible.")]
        public float pitScale = 0.74f;

        [Tooltip("How far the dark pit stays open when nothing is happening. A hole has to read as a hole at a glance, not as another coloured plate like the blocks.")]
        public float restingPitOpen = 0.96f;

        [Tooltip("How far the pit plate is stretched past the silhouette mesh. The mesh is only a drawing "
            + "surface - the shader clips to its own SDF - so a hole whose real opening is larger than its "
            + "block silhouette (the green P) needs a bigger plate or the shape gets cut off at the mesh "
            + "edge. _QuadScale is set to this same value, which makes the shader's SDF read in world "
            + "cells measured from the hole pivot instead of in mesh-object units.")]
        public float pitCoverScale = 1f;

        [Tooltip("How many world cells the cavity's distance field is eroded by at full close. The "
            + "shader shrinks the opening shut from its whole outline, so this has to exceed the "
            + "deepest interior distance of the WIDEST hole or that hole never seals: the Square/P "
            + "opening is a 3x2 box whose centre sits 1.0 cells from its nearest edge, so the "
            + "shader's old 0.8 default could only ever erode it down to a smaller patch and stop. "
            + "1.30 clears every authored shape (P 1.00, cross 0.74, L 0.58, bar 0.50) with margin - "
            + "L is 0.58 and not 0.50 because its opening is now a 3x2 with two cells cut, sampled "
            + "on a 501x501 grid of GetShapeSDF over +-2.5 cells. "
            + "It is a no-op while a hole is open: the shader scales it by (1 - _Open), and _Open "
            + "is 1 at rest.")]
        public float closeErode = 1.30f;

        [Tooltip("Colour the cavity's outer bevel ring fades into at its outer edge. This is the "
            + "board tile behind the ring; measured off the reference's empty tiles at about "
            + "55/65/120.")]
        public Color boardTint = new Color(0.216f, 0.255f, 0.470f, 1f);

        [Tooltip("Per-channel multiplier applied to the cavity once the hole has been fed. The " +
                 "reference extinguishes a spent opening between 1.60s and 1.95s: red is cut " +
                 "hardest, blue barely at all, and the cavity turns from magenta to indigo.")]
        // Re-solved from the cell INTERIOR rather than the whole cell. Stripping the lip band off
        // the cross's right arm at 1.95 (inset 26 px of a 122 px cell) gives reference 39/11/142
        // against our 58/15/120: the reference's spent cavity is not merely darker, its blue stays
        // HIGH while its red collapses. The first solve read the whole cell, where the untinted lip
        // pulled the model toward "dim everything", and blue came out 0.68 when it wants 0.80.
        public Color spentPitTint = new Color(0.26f, 0.32f, 0.80f, 1f);

        [Tooltip("How much of the spent tint the lip takes, 0 keeps the rim at full colour. The " +
                 "reference's rim stays bright but goes indigo with the rest of the hole.")]
        // PROVEN NECESSARY, not guessed. Two captures of frame 205 differing only in spentPitTint
        // ((1,1,1) -> 105/32/165, and (0.39,0.44,0.68) -> 80/25/145) solve the arm cell as
        // C + K*tint per channel: red is C=64.0 + K=41.0. The untinted residue alone is 64 red,
        // and the reference's whole-cell red is 61 - so with the lip left at full neonColor the
        // target is unreachable at ANY cavity tint (the solve returns -0.07). Decomposing the same
        // cell into the lip ring and the interior confirms where it sits: the reference's ring is
        // 72/17/167, r/b 0.43, while ours is 91/30/157, r/b 0.58. Its rim is bright in BLUE, not
        // in red, so the rim is tinted too - just less than the cavity behind it.
        [Range(0f, 1f)] public float spentLipMix = 0.30f;

        Transform _glowPlate;
        Renderer _glowRenderer;
        Transform _rim;
        Renderer _rimRenderer;
        Transform _pit;
        Renderer _pitRenderer;
        MaterialPropertyBlock _mpb;
        float _glow;
        float _glowTarget;
        float _pitOpen;
        float _pitTarget;
        float _pitRate = 8f;
        float _spent;
        float _spentTarget;
        float _spentRate = 5f;
        string _lastLogged;
        bool _loggedCavityShape;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int CenterColorId = Shader.PropertyToID("_CenterColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

        /// <summary>World point a matching block snaps to.</summary>
        public Vector3 SnapPoint { get { return transform.position; } }

        /// <summary>Current glow brightness, 0 when the hole is dark. This is the value the no-glow proof reads.</summary>
        public float GlowIntensity { get { return _glow; } }

        /// <summary>True while the hole is actually emitting.</summary>
        public bool IsGlowing { get { return _glow > 0.001f; } }

        /// <summary>How far the pit has opened, 0 closed to 1 fully open.</summary>
        public float PitOpen { get { return _pitOpen; } }

        /// <summary>
        /// The cavity plate's renderer. Exposed so a test can find this hole's own pixels by
        /// hiding it and differencing, rather than by trusting a screen rectangle worked out from
        /// the same geometry the shader uses - which would agree with the shader by construction.
        /// </summary>
        public Renderer PitRendererForProbe { get { return _pitRenderer; } }

        /// <summary>This hole's shape, from the enum when it is set and from the key otherwise.</summary>
        public BlockShapeId ResolvedShape
        {
            get { return shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(shapeKey); }
        }

        /// <summary>True when a block with <paramref name="otherShapeKey"/> belongs in this hole.</summary>
        public bool Matches(string otherShapeKey)
        {
            return Matches(BlockShapeIds.Parse(otherShapeKey));
        }

        public bool Matches(BlockShapeId otherShape)
        {
            BlockShapeId mine = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(shapeKey);
            return mine != BlockShapeId.Unknown && mine == otherShape;
        }

        /// <summary>
        /// Called every frame a block hovers this hole. The neon lights up only on a shape match; a wrong
        /// block leaves it dark. Returns whether the hover matched, and logs the decision once per change
        /// so a batchmode run carries the proof.
        /// </summary>
        public bool RequestGlow(string otherShapeKey)
        {
            return RequestGlow(BlockShapeIds.Parse(otherShapeKey));
        }

        public bool RequestGlow(BlockShapeId otherShape)
        {
            string otherShapeKey = BlockShapeIds.Key(otherShape);
            bool match = Matches(otherShape);
            SetLit(match);

            string stamp = otherShapeKey + "/" + match;
            if (_lastLogged != stamp)
            {
                _lastLogged = stamp;
                Debug.Log(string.Format(
                    "[Case2] HOVER hole={0} holeShape={1} blockShape={2} match={3} glow={4} intensity={5:0.000}",
                    name, shapeKey, otherShapeKey, match, match ? "ON" : "OFF", _glow));
            }
            return match;
        }

        /// <summary>Turns the neon on or off; it fades rather than popping.</summary>
        public void SetLit(bool lit)
        {
            _glowTarget = lit ? 1f : 0f;
            // Instant, not ramped. The reference steps its halo from nothing to full between two
            // consecutive frames (green f198 -> f199, 15 ms at 65 fps); anything eased here would
            // be a slower rise than the source has, and at our 120 fps capture clock even a fast
            // MoveTowards would spread it over several captured frames.
            if (lit) _glow = 1f;
        }

        /// <summary>Clears the hover state and the change log, so the next hover logs again.</summary>
        public void ClearHover()
        {
            _lastLogged = null;
            SetLit(false);
        }

        /// <summary>
        /// DEAD BEAT - MOVES NO PIXELS. Raises <c>_pitOpen</c> from <see cref="restingPitOpen"/>
        /// (0.96) to 1.0, but the only consumer of <c>_pitOpen</c> that reaches the shader is
        /// <c>_Open = Clamp01(_pitOpen / restingPitOpen)</c>, which is 1.0 at BOTH values. So the
        /// "the pit opens as the shards start falling" beat renders exactly the same frame as the
        /// resting hole, and always has. Its one real effect is a side effect: it gives the
        /// following <see cref="ClosePit"/> 1.0 units to travel instead of 0.96, so the close runs
        /// ~4% slower than its stated duration.
        /// <para>
        /// Left working rather than deleted: making it visible means opening the cavity WIDER than
        /// its resting state, which is a look change to the shatter beat and wants its own
        /// before/after against the reference, not a drive-by. The call sites are
        /// Case2Director.DropTail and Case2Director.Prewarm.
        /// </para>
        /// </summary>
        public void OpenPit(float duration)
        {
            EnsurePit();
            _pitTarget = 1f;
            _pitRate = 1f / Mathf.Max(0.01f, duration);
        }

        /// <summary>
        /// DEAD BEAT - RENDERS NOTHING, and now by choice rather than by accident.
        /// <para>
        /// It used to raise <c>_glow</c> towards 0.85 while the opening sealed, which was harmless
        /// only because <c>_glow</c> reached nothing. It reaches the screen now, so leaving that
        /// in would put the halo BACK on a hole that has just been fed. The reference does the
        /// opposite - see the body.
        /// </para>
        /// The last beat of the sequence is carried by <see cref="ClosePit"/> eroding the opening
        /// shut plus the Squash.Bump on the hole transform, both of which do render.
        /// </summary>
        public void FlashSeal(float seconds)
        {
            // Deliberately inert, and now for a measured reason rather than an accidental one.
            // Reviving it would put a bright halo back on a hole that has just been FED, and the
            // reference does the opposite: its red hole's halo band falls monotonically from +101
            // at the shatter to +5 by f907 and never comes back. Kept as a named no-op so the
            // director's call site keeps reading, and so the next reader finds this note instead
            // of the idea.
        }

        /// <summary>
        /// Fades the cavity to its spent colour: the opening has been fed and stops advertising
        /// itself. MEASURED, cross-hole arm cell (2,6) mean luminance across the reference's own
        /// frames: 86 -> 113 -> 142 -> 147 -> 130 -> 141 at 1.60 -> 79 at 1.95. Ours over the same
        /// span was 92 -> 144 -> 141 -> 143 -> 143 -> 118 -> 101, and the r/b ratio never moved
        /// (0.65 at t=0.19, 0.64 at t=1.95) because the wall and floor tints are constants of
        /// neonColor with nothing to fade them. At 1.95 that left our cavity 28% brighter than the
        /// reference's and 68% redder in r/b.
        /// </summary>
        public void Spend(float duration)
        {
            _spentTarget = 1f;
            _spentRate = 1f / Mathf.Max(0.01f, duration);
        }

        // ---------------------------------------------------------------- tile rise on close
        //
        // MEASURED off Block Hole.mp4, the purple cross's hole, frames 62-88 at 30 fps. Each of the
        // five cross cells was tracked by normalised cross-correlation of its settled tile face
        // (frame 90) against every earlier frame, taking the vertical offset with the best score:
        //
        //   cell   peak rise   peak frame   duration
        //   (1,3)   +1.79 u       66         100 ms
        //   (2,2)   +2.30 u       67         100 ms
        //   (0,2)   +2.30 u       73         133 ms
        //   (1,1)   +2.11 u       74         167 ms
        //
        // Mean peak 2.13 u, sd 0.24. The peaks span frames 66-74, so the cells do NOT pop together -
        // there is a per-cell stagger of about 270 ms across the opening.
        //
        // 2.1 world units sounds enormous for a board tile and is not: the camera is orthographic at
        // 80 degrees, so a world-Y displacement projects onto screen at 122.55 * cos(80) = 21.28
        // px/unit. 2.1 units is 45 screen px against a 120.7 px cell - about a third of a tile
        // height, which is what it reads as. Authoring this by eye in screen pixels would have
        // produced roughly a tenth of the real motion. Same trap as liftHeight being worth 9 px.

        [Header("Tile rise (measured, see the block comment above)")]
        [Tooltip("Peak height a fed cell's tile pops to, world units. Measured 1.79/2.11/2.30/2.30 "
            + "across four cells; 2.1 u is 45 screen px at 122.55*cos(80) = 21.28 px per world unit.")]
        public float tileRiseHeight = 2.1f;

        [Tooltip("How far BELOW the board plane a fed cell's tile starts, world units. The tile does "
            + "not begin flush: it waits down inside the pit and climbs out. Measured -5.08 u on cell "
            + "(2,2) held across frames 61-63 and -5.17 u on cell (1,1) at frames 65-66.")]
        public float tileRiseDepth = 5.1f;

        [Tooltip("Seconds for the whole arc: up from tileRiseDepth to flush, past it by "
            + "tileRiseHeight, and back. Measured 200-230 ms on cell (2,2) (f63 deep -> f70 flush).")]
        // NonSerialized: every hole in the scene carries its own 0.21, which would override this.
        // Owner-directed deviation from the measured 200-230 ms - at the reference's speed the tiles
        // read as a flicker rather than as blocks arriving, so the arc is roughly doubled.
        [System.NonSerialized] public float tileRiseDuration = 0.45f;

        [Tooltip("Fraction of the arc spent climbing to flush. Measured: cell (2,2) crossed the board "
            + "plane at f66 of a f63-f70 motion, so about half.")]
        [Range(0.1f, 0.9f)] public float tileRiseCrossFraction = 0.5f;

        [Tooltip("Seconds the per-cell start times are spread over. Measured: peaks spanned frames "
            + "66-74 at 30 fps, i.e. 267 ms.")]
        public float tileRiseStagger = 0.27f;

        Transform[] _cellTiles;
        Vector3[] _cellTileHome;
        readonly System.Collections.Generic.List<Coroutine> _riseRoutines =
            new System.Collections.Generic.List<Coroutine>();

        /// <summary>World-axis occupancy of each hole opening, top row = +z. These are the same
        /// footprints GetShapeSDF draws in HoleDepthGradient.shader, read back as cells.</summary>
        static string[] HoleMask(BlockShapeId id)
        {
            switch (id)
            {
                case BlockShapeId.Square: return new[] { "###", ".##" };   // green P
                case BlockShapeId.Cross:  return new[] { ".#.", "###", ".#." };
                case BlockShapeId.Two:    return new[] { "#", "#", "#" };  // cyan bar
                // Four cells, and the fourth one - the bottom-right - is the cell the cyan bar
                // stands on. It is part of the opening, it is empty, and it happens to have an
                // object over it; see the transect in HoleDepthGradient.shader's shapeType 3.
                // This is now the same footprint BlockDragController.ShapeMask gives the red
                // BLOCK, which is what the reference shows.
                case BlockShapeId.L:      return new[] { "#..", "###" };
                default:                  return null;
            }
        }

        /// <summary>
        /// Where a block of <paramref name="blockShape"/> should bring its ART BOUNDS CENTRE to
        /// rest so that its own cells land on THIS opening's cells.
        ///
        /// <para>
        /// This replaces <see cref="SnapPoint"/> for seating, and it is the fifth appearance today
        /// of one root: a BOUNDING-BOX CENTRE standing in for a shape's actual cells.
        /// </para>
        /// <para>
        /// MEASURED. The green hole is a five-cell P - the shader draws 3x2 with the lower-left
        /// cell left as board (shapeType 0), HoleMask says {"###", ".##"}, and both agree with the
        /// reference. The green BLOCK is a 2x2: its art bounds measure 2.000 x 2.000. Seating put
        /// the block's 2x2 bbox centre on the hole's 3x2 bbox centre, which leaves the block
        /// straddling the middle of the opening, sitting half a cell off in x and covering no
        /// complete cell column - "gidiyor objenin ortasinda duruyor", exactly.
        /// </para>
        /// <para>
        /// The fix is to stop comparing boxes and compare CELLS: slide the block's mask over the
        /// hole's mask and take the whole-cell offset with the most overlap, breaking ties toward
        /// the centred position. For the cross, the bar and the L, whose block and hole masks are
        /// identical, the best offset is zero and this returns exactly what SnapPoint returned -
        /// so those three cannot move. For the square it returns half a cell to the right, seating
        /// the 2x2 on the P's right-hand 2x2, whose cells are real tile centres.
        /// </para>
        /// </summary>
        public const float SeatDepth = 0.72f;

        readonly System.Collections.Generic.Dictionary<BlockShapeId, Vector3> _seatCache
            = new System.Collections.Generic.Dictionary<BlockShapeId, Vector3>();

        /// <summary>
        /// THE TARGET POSE. One value per hole per block shape, solved once from the hole's own
        /// cell list and then simply read - "objelerin target poz gibi bir yer olsun, oraya
        /// gitsinler". The drop tween's endpoint is this and nothing else: no hole centre, no
        /// bounds centre, no per-shape correction anywhere in the path. Depth is part of the pose,
        /// so "too deep" is an authored number (<see cref="SeatDepth"/>) rather than a computed
        /// one.
        /// <para>
        /// y is the seat DEPTH below the board, not the board surface: the piece comes to rest
        /// inside the opening and breaks there.
        /// </para>
        /// </summary>
        public Vector3 TargetPose(BlockShapeId blockShape)
        {
            Vector3 seat;
            if (!_seatCache.TryGetValue(blockShape, out seat))
            {
                seat = SolveSeat(blockShape);
                _seatCache[blockShape] = seat;
            }
            return seat;
        }

        Vector3 SolveSeat(BlockShapeId blockShape)
        {
            BlockShapeId hid = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(shapeKey);
            string[] H = HoleMask(hid);
            string[] B = BlockShapeIds.Mask(blockShape);
            Vector3 pivot = transform.position;
            if (H == null || B == null || H.Length == 0 || B.Length == 0) return SeatY(SnapPoint);

            int hr = H.Length, hc = H[0].Length;
            int br = B.Length, bc = B[0].Length;
            if (br > hr || bc > hc) return SeatY(SnapPoint);

            int bestDc = 0, bestDr = 0, bestScore = -1;
            float bestTie = float.MaxValue;
            for (int dr = 0; dr <= hr - br; dr++)
            for (int dc = 0; dc <= hc - bc; dc++)
            {
                int score = 0;
                for (int r = 0; r < br; r++)
                for (int c = 0; c < bc && c < B[r].Length; c++)
                {
                    if (B[r][c] != '#') continue;
                    int R = dr + r, C = dc + c;
                    if (R < H.Length && C < H[R].Length && H[R][C] == '#') score++;
                }
                // Tie-break toward the centred placement, so a symmetric case cannot drift.
                float tie = Mathf.Abs(dc - (hc - bc) * 0.5f) + Mathf.Abs(dr - (hr - br) * 0.5f);
                if (score > bestScore || (score == bestScore && tie < bestTie))
                {
                    bestScore = score; bestTie = tie; bestDc = dc; bestDr = dr;
                }
            }

            // Same cell convention CacheCellTiles uses: column 0 is -x, row 0 is +z.
            return new Vector3(
                pivot.x - hc * 0.5f + bestDc + bc * 0.5f,
                SnapPoint.y - SeatDepth,
                pivot.z + hr * 0.5f - bestDr - br * 0.5f);
        }

        static Vector3 SeatY(Vector3 v) { return new Vector3(v.x, v.y - SeatDepth, v.z); }

        /// <summary>
        /// Finds the board tile sitting in each cell of this opening, once. The tiles are always
        /// there - the pit plate is a quad ABOVE an intact grid, not a cut in the board - so the
        /// rise only has to move them, not create them.
        /// </summary>
        void CacheCellTiles()
        {
            _cellTiles = new Transform[0];
            _cellTileHome = new Vector3[0];
            BlockShapeId id = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(shapeKey);
            string[] mask = HoleMask(id);
            if (mask == null) return;

            int rows = mask.Length, cols = mask[0].Length;
            var tiles = new System.Collections.Generic.List<Transform>();
            var homes = new System.Collections.Generic.List<Vector3>();
            Vector3 pivot = transform.position;

            var all = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Tile_")) all.Add(t);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols && c < mask[r].Length; c++)
                {
                    if (mask[r][c] != '#') continue;
                    float cx = pivot.x - cols * 0.5f + c + 0.5f;
                    float cz = pivot.z + rows * 0.5f - r - 0.5f;
                    Transform best = null; float bestD = 0.5f;
                    for (int i = 0; i < all.Count; i++)
                    {
                        Vector3 p = all[i].position;
                        float d = Mathf.Max(Mathf.Abs(p.x - cx), Mathf.Abs(p.z - cz));
                        if (d < bestD) { bestD = d; best = all[i]; }
                    }
                    if (best != null) { tiles.Add(best); homes.Add(best.position); }
                }
            _cellTiles = tiles.ToArray();
            _cellTileHome = homes.ToArray();
            _cellTileRenderers = new Renderer[_cellTiles.Length];
            for (int i = 0; i < _cellTiles.Length; i++)
                _cellTileRenderers[i] = _cellTiles[i] != null ? _cellTiles[i].GetComponentInChildren<Renderer>(true) : null;
        }

        Renderer[] _cellTileRenderers;

        /// <summary>
        /// A rising tile is drawn only once it has reached the board plane; below it, what the
        /// player sees rising is the pit plate's own fill, which is clipped to the opening's SDF.
        /// <para>
        /// "o bosluktan geliyormus gibi degil de baska bir yerden geliyormus gibi gozukuyor" - the
        /// cube was showing through the board wherever the opening is narrower than the cube, which
        /// for a cross or an L is most of its perimeter. The rule is: clipped below the surface,
        /// free above it - "son uzerine cikinca boslugu gecebilir".
        /// </para>
        /// <para>
        /// The clip is done by the APERTURE rather than by the cube because the board tile's
        /// material and shader belong to another workstream this session. The pit plate already
        /// clips every pixel it draws to the hole's exact distance field - including the cross's
        /// notches, which is the geometry that defeats anything bounds-based - so the sub-surface
        /// part of the rise is carried there and the cube itself is simply not drawn until it is
        /// legitimately above the board. Reported as a substitution, not presented as a clip of
        /// the cube's own mesh.
        /// </para>
        /// </summary>
        void SetTileVisible(int i, bool visible)
        {
            if (_cellTileRenderers == null || i >= _cellTileRenderers.Length) return;
            Renderer r = _cellTileRenderers[i];
            if (r != null && r.enabled != visible) r.enabled = visible;
        }

        /// <summary>Half the widest side of this opening, world units. Read by the gate.</summary>
        public float OpeningHalfExtent
        {
            get
            {
                BlockShapeId id = shapeId != BlockShapeId.Unknown ? shapeId : BlockShapeIds.Parse(shapeKey);
                string[] m = HoleMask(id);
                return m == null ? 1f : Mathf.Max(m.Length, m[0].Length) * 0.5f;
            }
        }

        /// <summary>How many board tiles this opening will pop. Read by the gate.</summary>
        public int CellTileCount { get { if (_cellTiles == null) CacheCellTiles(); return _cellTiles.Length; } }

        /// <summary>Resting world position of the i-th fed cell's tile. Read by the gate.</summary>
        public Vector3 CellTileHome(int i) { if (_cellTiles == null) CacheCellTiles(); return _cellTileHome[i]; }

        /// <summary>How far the i-th tile is from where it started. Must be 0 once the rise is over.</summary>
        public float CellTileOffset(int i)
        {
            if (_cellTiles == null || _cellTiles[i] == null) return 0f;
            return Vector3.Distance(_cellTiles[i].position, _cellTileHome[i]);
        }

        /// <summary>Peak height any of this hole's tiles is currently at, world units. Read by the gate.</summary>
        public float TileRisePeak
        {
            get
            {
                if (_cellTiles == null) return 0f;
                float m = 0f;
                for (int i = 0; i < _cellTiles.Length; i++)
                    if (_cellTiles[i] != null) m = Mathf.Max(m, _cellTiles[i].position.y - _cellTileHome[i].y);
                return m;
            }
        }

        /// <summary>
        /// Pops the board tiles of a fed opening up and settles them flush again - the beat the
        /// reference plays after the shards have fallen in and the cavity has sealed.
        /// <para>
        /// Every tile is returned to the exact position it was cached at, not to a recomputed one.
        /// The closure invariant is that a fed hole reads as plain board and never reopens; a rise
        /// that left a tile a hair proud would break it in a way a pit-open check cannot see.
        /// </para>
        /// </summary>
        public void RiseTiles()
        {
            if (_cellTiles == null) CacheCellTiles();
            // A rise already in flight is cancelled rather than raced. Two RiseOne coroutines on
            // the same tile write the same transform in the same frame and the loser wins by
            // execution order, which is not a thing any measurement can pin down.
            SnapCellTilesHome();
            for (int i = 0; i < _cellTiles.Length; i++)
            {
                if (_cellTiles[i] == null) continue;
                float delay = _cellTiles.Length > 1
                    ? tileRiseStagger * i / (_cellTiles.Length - 1)
                    : 0f;
                _riseRoutines.Add(StartCoroutine(RiseOne(i, delay)));
            }
        }

        /// <summary>
        /// Stops any tile rise in flight and puts every cached cell tile back on the exact position
        /// it was cached at.
        /// <para>
        /// This is the piece the replay path was missing. The rise runs as coroutines on THIS
        /// component, so <c>Case2Director.ResetState</c>'s <c>StopAllCoroutines()</c> - which runs
        /// on the director - never reached it, and <see cref="ResetInstant"/> moved every layer of
        /// the hole except the board tiles. The frame-strip harness replays the sequence on the
        /// first update after the measure pass completes, and that instant lands INSIDE the rise's
        /// tileRiseStagger + tileRiseDuration window, so tiles left mid-arc kept climbing into the
        /// recorded run. A tile at the measured tileRiseHeight of 2.1 world units stands 2.07 u
        /// above a pit plate at pitHeight 0.034 and simply draws over the cavity: the purple
        /// cross's top and left arms rendered as plain board (48/60/116 and 49/56/114) in frame_00
        /// of the dense strip while its other three cells rendered as wall and floor.
        /// </para>
        /// <para>
        /// The tiles are returned to <c>_cellTileHome</c>, never to a recomputed position - the
        /// same rule <see cref="RiseOne"/> ends on, and the reason the closure invariant can be
        /// measured in pixels at all.
        /// </para>
        /// </summary>
        public void SnapCellTilesHome()
        {
            for (int i = 0; i < _riseRoutines.Count; i++)
                if (_riseRoutines[i] != null) StopCoroutine(_riseRoutines[i]);
            _riseRoutines.Clear();

            if (_cellTiles == null) return;
            // A rise cancelled mid-arc must not leave a tile switched off - that would be a hole in
            // the board that no pit-open check could see.
            for (int i = 0; i < _cellTiles.Length; i++) SetTileVisible(i, true);
            for (int i = 0; i < _cellTiles.Length; i++)
                if (_cellTiles[i] != null) _cellTiles[i].position = _cellTileHome[i];
        }

        System.Collections.IEnumerator RiseOne(int i, float delay)
        {
            Transform t = _cellTiles[i];
            Vector3 home = _cellTileHome[i];
            // Drop it into the pit for the whole stagger delay, so it is genuinely waiting down
            // there when its turn comes rather than appearing at depth on its first moving frame.
            if (t != null) t.position = new Vector3(home.x, home.y + RiseCurve(0f), home.z);
            SetTileVisible(i, false);           // waiting ~5 units down inside the pit
            float end = Time.time + delay;
            while (Time.time < end) yield return null;

            float start = Time.time;
            float dur = Mathf.Max(0.01f, tileRiseDuration);
            while (true)
            {
                float k = (Time.time - start) / dur;
                if (k >= 1f) break;
                float h = RiseCurve(k);
                if (t != null) t.position = new Vector3(home.x, home.y + h, home.z);
                // Clipped below, free above. The cube appears the moment it reaches the board
                // plane and may exceed the opening freely from there - which is the overshoot the
                // reference plays and the owner explicitly allows.
                SetTileVisible(i, h >= -0.001f);
                yield return null;
            }
            if (t != null) t.position = home;      // exact, not recomputed
            SetTileVisible(i, true);
        }

        /// <summary>
        /// Height of a rising tile at normalised time <paramref name="k"/>, world units, negative
        /// below the board plane.
        /// <para>
        /// Two phases, because the reference is two phases. The tile waits about 5.1 units DOWN
        /// inside the pit, climbs to flush over the first half of the arc, then overshoots past the
        /// board by about 2.1 and settles. The first version of this only had the second half - it
        /// started flush and popped up - which is the beat the owner described as needing to come
        /// "from deeper", and the reference agrees with him: cell (2,2) sits at -5.08 u for three
        /// frames before it moves at all.
        /// </para>
        /// <para>
        /// The overshoot uses sin(pi*sqrt(u)) rather than sin(pi*u) so its peak lands at 62% of the
        /// arc against a measured 57%; a plain sine would put it at 75%.
        /// </para>
        /// </summary>
        public float RiseCurve(float k)
        {
            float cross = Mathf.Clamp(tileRiseCrossFraction, 0.05f, 0.95f);
            if (k < cross)
            {
                float u = Mathf.Clamp01(k / cross);
                float outQuad = 1f - (1f - u) * (1f - u);
                return -tileRiseDepth * (1f - outQuad);
            }
            float v = Mathf.Clamp01((k - cross) / (1f - cross));
            return Mathf.Sin(Mathf.Sqrt(v) * Mathf.PI) * tileRiseHeight;
        }

        /// <summary>Deepest point below the board any of this hole's tiles is currently at. Read by the gate.</summary>
        public float TileRiseDeepest
        {
            get
            {
                if (_cellTiles == null) return 0f;
                float m = 0f;
                for (int i = 0; i < _cellTiles.Length; i++)
                    if (_cellTiles[i] != null) m = Mathf.Max(m, _cellTileHome[i].y - _cellTiles[i].position.y);
                return m;
            }
        }

        /// <summary>Closes the pit again, returning the floor to normal.</summary>
        public void ClosePit(float duration)
        {
            // All the way shut, not back to resting: the reference seals the target hole
            // completely and the board reads as plain tiles again by the end of the clip.
            _pitTarget = 0f;
            _pitRate = 1f / Mathf.Max(0.01f, duration);
        }

        /// <summary>
        /// Permanently retires the opening: the cavity plate is switched off and stays off, so the
        /// board tiles underneath it are what renders. Used once a hole has been fed. The pit plate
        /// is a separate quad sitting ABOVE an intact tile grid - it is not a cut in the board - so
        /// removing it is literally all it takes to put the checkerboard back.
        /// <para>
        /// This exists because the player-facing path used to call <see cref="ResetInstant"/> here,
        /// which sets _pitOpen back to restingPitOpen (0.96) and re-opened the hole a fifth of a
        /// second after ClosePit had just sealed it. The close animation was working; it was being
        /// undone.
        /// </para>
        /// </summary>
        public void SealShut()
        {
            _lastLogged = null;
            _glow = 0f;
            _glowTarget = 0f;
            _pitOpen = 0f;
            _pitTarget = 0f;
            ApplyGlow();
            ApplyPit();
        }

        /// <summary>Snaps every layer back to its resting state without animating; used by a replay reset.</summary>
        public void ResetInstant()
        {
            _lastLogged = null;
            _glow = 0f;
            _glowTarget = 0f;
            _spent = 0f;
            _spentTarget = 0f;
            _pitOpen = restingPitOpen;
            _pitTarget = restingPitOpen;
            // The board is part of the resting state. Without this a rise still in flight from the
            // previous run walks its tiles across the reset and stands them over the reopened pit.
            SnapCellTilesHome();
            EnsurePit();
            ApplyGlow();
            ApplyPit();
        }

        /// <summary>
        /// A hole is dark from the first frame: without this every hole renders as a flat coloured
        /// plate exactly like a block, and nothing on the board says which one is the opening.
        /// </summary>
        void Start()
        {
            EnsureRim();
            EnsurePit();
            _pitOpen = restingPitOpen;
            _pitTarget = restingPitOpen;
            ApplyPit();

            // The Cross hole used to be lit here unconditionally, which was harmless while the
            // glow drew nothing. Now that it does, a hole lit before anyone has touched a block
            // is exactly the wrong information - and the reference lights a hole only from the
            // frame its block is picked up. BlockDragController.BeginHold owns that now, so both
            // the scripted sequence and a real pointer light it through the same path.
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            // The seal flash deliberately does NOT enter here any more. It used to pull _glow up
            // to 0.85 while the opening sealed, which was invisible while ApplyGlow drew nothing.
            // Now that _glow reaches the screen, that would be a re-flash of the halo AFTER the
            // hole has been fed - and the reference has none: on its red hole the band falls from
            // +101 at the shatter to +5 by f907 and never rises again.
            _glow = Mathf.MoveTowards(_glow, _glowTarget,
                                      (1f / Mathf.Max(0.01f, glowFadeOutSeconds)) * dt);
            ApplyGlow();

            _spent = Mathf.MoveTowards(_spent, _spentTarget, _spentRate * dt);
            _pitOpen = Mathf.MoveTowards(_pitOpen, _pitTarget, _pitRate * dt);
            ApplyPit();
        }

        void EnsureRim()
        {
            // Unified SDFHolePit now renders the complete hole surface (lip with directional bevel lighting + interior depth gradient).
        }

        /// <summary>
        /// Keeps the legacy neon plate switched off. Every consumer of <c>_glow</c> ends here, so
        /// <c>_glow</c>, <see cref="SetLit"/> and <see cref="FlashSeal"/> currently have no visual
        /// effect at all; the hole's appearance comes entirely from the SDF pit material. SetLit
        /// and RequestGlow are still worth keeping - RequestGlow returns the shape match and logs
        /// the HOVER proof line - but nothing they set is drawn.
        /// </summary>
        void ApplyGlow()
        {
            if (_glowRenderer != null && _glowRenderer.gameObject.activeSelf)
            {
                _glowRenderer.gameObject.SetActive(false);
            }
        }

        static readonly int BoardTintId = Shader.PropertyToID("_BoardTint");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int LipOuterId = Shader.PropertyToID("_LipOuter");
        static readonly int LipFadeId = Shader.PropertyToID("_LipFade");
        static readonly int LipLiftId = Shader.PropertyToID("_LipLift");
        static readonly int WallHeightId = Shader.PropertyToID("_WallHeight");
        // DEAD: this id is never handed to the property block, and _CavityContrast does not
        // appear anywhere in HoleDepthGradient's fragment program either - it is declared in
        // Properties and in the CBUFFER and then never read. Nothing can tune cavity contrast.
        static readonly int CavityContrastId = Shader.PropertyToID("_CavityContrast");
        static readonly int LipColorId = Shader.PropertyToID("_LipColor");
        static readonly int PitTopColorId = Shader.PropertyToID("_PitTopColor");
        static readonly int PitBottomColorId = Shader.PropertyToID("_PitBottomColor");
        static readonly int ShapeTypeId = Shader.PropertyToID("_ShapeType");
        static readonly int LipWidthId = Shader.PropertyToID("_LipWidth");
        static readonly int BevelIntensityId = Shader.PropertyToID("_BevelIntensity");
        static readonly int QuadScaleId = Shader.PropertyToID("_QuadScale");
        static readonly int CloseErodeId = Shader.PropertyToID("_CloseErode");
        static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        static readonly int GlowReachId = Shader.PropertyToID("_GlowReach");
        static readonly int GlowCoreId = Shader.PropertyToID("_GlowCore");
        static readonly int GlowPeakId = Shader.PropertyToID("_GlowPeak");
        static readonly int GlowHotId = Shader.PropertyToID("_GlowHot");
        static readonly int GlowGainId = Shader.PropertyToID("_GlowGain");
        static readonly int GlowWaveAmpId = Shader.PropertyToID("_GlowWaveAmp");
        static readonly int GlowWaveLobesId = Shader.PropertyToID("_GlowWaveLobes");
        static readonly int GlowWaveSpeedId = Shader.PropertyToID("_GlowWaveSpeed");

        /// <summary>
        /// Halo band geometry in world cells, solved against the reference's own transect.
        /// Constants rather than serialised fields because the scene that would carry them is
        /// hand-authored and this branch must not write it.
        /// </summary>
        const float GlowReachCellsFixed = 0.28f;
        const float GlowCoreCellsFixed = 0.03f;

        // ------------------------------------------------------------------ cavity tints
        //
        // MEASURED against ref_0.00s.png (= Block Hole.mp4 frame 0). Instrument: the hole's hue
        // mask eroded by 12 px, then TWO modes - the mode of the whole interior, which is the
        // floor, and the mode of its brightest quartile, which is the visible far wall. The four
        // floor modes it finds on the reference are 45/0/0, 1/13/0, 0/41/67 and 24/10/58, which
        // are the same four values this shader's own header records from a hand transect years
        // earlier - that agreement is the instrument's positive control.
        //
        //                  FLOOR (mode of the interior)      WALL (mode of its top quartile)
        //                  ours before   reference           ours before   reference
        //   red             43, 6,11      45, 0, 0           123,20,28     114, 0, 4
        //   green            5,25, 0       1,13, 0            14,75, 0       0,64, 3
        //   cyan             3,35,44       0,41,67             6,98,120     60,76,120
        //   purple          31, 8,46      24,10,58            76,21,114     43,10,164
        //
        // In C* terms the floors were red -17%, green +130%, cyan -41%, purple -20%. "Make the
        // holes more vivid" would have been wrong: green is the one that is already too vivid.
        //
        // The tints below are old_tint * (reference_linear / ours_linear) per channel, refined by
        // one capture. Zeros are not rounding: the reference's red floor really has no green and
        // no blue in it, and its cyan floor no red.
        //
        // NOT taken from the reference: the cyan wall's red. The reference's cyan opening sits
        // against the board's lavender frame and its top-quartile mode picks up 60 red from it,
        // which is the frame, not the wall. That channel is left where it was.
        /// <summary>
        /// Left at the shared 0.60 on purpose. The far wall IS off - the reference's purple wall
        /// reads 43,10,164 against our 76,21,114 and its red one 114,0,4 against our 123,20,28 -
        /// but a first solve moved it the wrong way and no locator for it survived scrutiny.
        /// A top-quartile-of-the-interior sampler lands on the held block for the red hole
        /// (173,51,252) and on the board frame for the green one (86,100,177) in the reference's
        /// own frame 0, and a hue classifier moves its mask when the tint it is measuring moves -
        /// the green hole's mask went 44,866 px to 22,723 px between two rounds. Shipping a wall
        /// change on either of those would be tuning against an instrument that cannot see the
        /// wall. The floor, whose mode is stable under both, is what this pass corrects.
        /// </summary>
        static Color WallTintFor(BlockShapeId id)
        {
            return new Color(0.60f, 0.60f, 0.60f, 1f);
        }

        static Color FloorTintFor(BlockShapeId id)
        {
            switch (id)
            {
                // old 0.19 * (reference_linear / ours_linear), per channel, from the table above.
                // A zero is a real zero: the reference's red floor carries no green and no blue,
                // its green floor no blue, its cyan floor no red. Those channels are switched off,
                // not rounded down.
                //
                // Cyan and purple took one secant step after the first capture. Recording the
                // misstep because it is the useful part: I first read the gate's error as
                // reference-minus-ours and RAISED both blues, which drove cyan's error from 37 to
                // 187 and purple's from 13 to 61. The sign is ours-minus-reference; the blues were
                // already over, and the correction is downward. A step that moves the number the
                // wrong way by 5x is the cheapest possible proof that a sign was assumed.
                case BlockShapeId.L:      return new Color(0.2064f, 0.0000f, 0.0000f, 1f);  // red
                case BlockShapeId.Square: return new Color(0.0380f, 0.0787f, 0.0000f, 1f);  // green
                case BlockShapeId.Two:    return new Color(0.0000f, 0.2212f, 0.2801f, 1f);  // cyan
                case BlockShapeId.Cross:  return new Color(0.1474f, 0.2375f, 0.2396f, 1f);  // purple
                default:                  return new Color(0.19f,   0.19f,   0.19f,   1f);
            }
        }

        Vector3 GetPitBaseScale()
        {
            float k = Mathf.Max(0.01f, pitCoverScale);
            return new Vector3(k, 1f, k);
        }

        void EnsurePit()
        {
            if (_pit != null || pitMaterial == null) return;
            _pit = BuildPlate("SDFHolePit", pitMaterial, UnitQuadXZ(), pitHeight, out _pitRenderer);
            _pit.localScale = GetPitBaseScale();
            _pitRenderer.gameObject.SetActive(true);
            ApplyPitProperties();
        }

        int GetShapeTypeInt()
        {
            if (shapeKey.IndexOf("Square", System.StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (shapeKey.IndexOf("Cross", System.StringComparison.OrdinalIgnoreCase) >= 0 || shapeKey.IndexOf("Plus", System.StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (shapeKey.IndexOf("2", System.StringComparison.OrdinalIgnoreCase) >= 0 || shapeKey.IndexOf("Bar", System.StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (shapeKey.IndexOf("L", System.StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            return 0;
        }

        void ApplyPitProperties()
        {
            if (_pitRenderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _pitRenderer.GetPropertyBlock(_mpb);

            Color spentNeon = new Color(neonColor.r * spentPitTint.r,
                                        neonColor.g * spentPitTint.g,
                                        neonColor.b * spentPitTint.b, 1f);
            _mpb.SetColor(LipColorId, _spent > 0f
                ? Color.Lerp(neonColor, Color.Lerp(neonColor, spentNeon, spentLipMix), _spent)
                : neonColor);

            // Wall and floor are tints of the hole's own colour - still a tint, so a hole that
            // changes colour takes its cavity with it, and the four colour identities stay in the
            // four materials where they belong. What changed is that the tint is now PER HOLE and
            // PER CHANNEL. One shared pair (0.60 / 0.19) could not be right: measured against the
            // reference, the same pair left the red floor with 6 green and 11 blue where the
            // reference has 0 and 0, and left the green floor 130% too chromatic while the cyan
            // one was 41% short. See WallTintFor / FloorTintFor for the solve.
            // Lit, the cavity is a straight tint of the hole's colour. Spent, every channel is
            // scaled by spentPitTint first, so the opening darkens AND loses its red - which is
            // what the reference does between 1.60s and 1.95s. _spent is 0 for the whole of the
            // approach, so nothing before the shatter can move.
            Color cavity = _spent > 0f ? Color.Lerp(neonColor, spentNeon, _spent) : neonColor;
            // shapeId is Unknown on holes that only carry a shapeKey - the same fallback every
            // other read of it in this class uses. Getting this wrong would silently hand all
            // four holes the default tint and leave every measurement unmoved, so ResolvedShape
            // is logged once per hole below.
            BlockShapeId cavityShape = ResolvedShape;
            Color wallTint = WallTintFor(cavityShape);
            Color floorTint = FloorTintFor(cavityShape);
            if (!_loggedCavityShape)
            {
                _loggedCavityShape = true;
                Debug.Log(string.Format("[Case2] CAVITY hole={0} shapeKey={1} resolved={2} wallTint={3:0.000},{4:0.000},{5:0.000} floorTint={6:0.000},{7:0.000},{8:0.000}",
                    name, shapeKey, cavityShape, wallTint.r, wallTint.g, wallTint.b, floorTint.r, floorTint.g, floorTint.b));
            }
            Color wall = new Color(cavity.r * wallTint.r, cavity.g * wallTint.g, cavity.b * wallTint.b, 1f);
            Color floorColor = new Color(cavity.r * floorTint.r, cavity.g * floorTint.g, cavity.b * floorTint.b, 1f);

            _mpb.SetColor(PitTopColorId, wall);
            _mpb.SetColor(PitBottomColorId, floorColor);
            _mpb.SetColor(BoardTintId, boardTint);

            _mpb.SetFloat(ShapeTypeId, (float)GetShapeTypeInt());
            // Band sizes in world cells. The rim sits ENTIRELY INSIDE the opening and is the
            // hole's own colour: measured on all three non-glowing holes, the bright rim spans
            // d = 0 to +0.12 cells inward and the board 0.10 cells outside is untouched. The
            // previous 0.20-cell outward reach plus a 33% lerp to white is what produced the
            // pale collar. _WallHeight is how far down the visible far wall extends.
            _mpb.SetFloat(LipWidthId, 0.12f);
            _mpb.SetFloat(LipOuterId, 0.0f);
            _mpb.SetFloat(LipFadeId, 0.03f);
            _mpb.SetFloat(LipLiftId, 0.0f);
            _mpb.SetFloat(WallHeightId, 1.0f);
            _mpb.SetFloat(BevelIntensityId, 0.50f);
            _mpb.SetFloat(QuadScaleId, Mathf.Max(0.01f, pitCoverScale));

            // The halo. It rides the SDF pit rather than reviving the neon plate: cd6d07f made
            // this material the sole authority for the hole surface, and a second quad drawn over
            // the same opening would fight it. _glow is 1 only while a block that MATCHES this
            // hole is held - that is the whole point of the effect, and a halo on all four holes
            // would be decoration with the targeting cue removed.
            _mpb.SetFloat(GlowStrengthId, _glow);
            _mpb.SetColor(GlowColorId, cavity);
            // NOT read from glowReachCells / glowCoreCells any more, and that is deliberate rather
            // than sloppy. Those two are serialised on the hand-authored scene's hole objects, at
            // 0.32 and 0.08, and this branch is not allowed to re-serialise that scene - so the
            // only place a corrected band can be written is here. The authored values are left
            // alone and reported to the owner instead.
            //
            // Half-width is what actually changed, because core and reach trade against each other:
            // smoothstep(reach, core, d) is at half at (core + reach) / 2.
            //     reference  0.156 cells   (from the transect: +126 at 0.048, +27.8 at 0.217)
            //     before     0.200 cells   (core 0.08, reach 0.32) - 28% too wide
            //     after      0.155 cells   (core 0.03, reach 0.28)
            _mpb.SetFloat(GlowReachId, GlowReachCellsFixed);
            _mpb.SetFloat(GlowCoreId, GlowCoreCellsFixed);
            _mpb.SetFloat(GlowPeakId, Mathf.Clamp01(glowPeakAlpha));
            // Thinner and brighter pull against each other through bloom, so the extra brightness
            // is spent on a narrow inner band while the band as a whole got narrower.
            _mpb.SetFloat(GlowHotId, glowRimWhiteness);
            _mpb.SetFloat(GlowGainId, glowRimGain);
            _mpb.SetFloat(GlowWaveAmpId, glowWaveAmplitude);
            _mpb.SetFloat(GlowWaveLobesId, glowWaveLobes);
            _mpb.SetFloat(GlowWaveSpeedId, glowWaveRevolutionsPerSecond);
            // Written here rather than left to the material: the .mat serialises no _CloseErode
            // at all, so the value the cavity actually sealed with was the shader default and
            // nothing in the scene showed it.
            _mpb.SetFloat(CloseErodeId, Mathf.Max(0f, closeErode));
            // Normalised so that the resting state is exactly 1.0 and no hole is eroded while
            // it simply sits on the board.
            _mpb.SetFloat(OpenId, Mathf.Clamp01(_pitOpen / Mathf.Max(0.01f, restingPitOpen)));

            _pitRenderer.SetPropertyBlock(_mpb);
        }

        void ApplyPit()
        {
            if (_pitRenderer == null)
            {
                if (_pitOpen <= 0f) return;
                EnsurePit();
                if (_pitRenderer == null) return;
            }

            bool visible = _pitOpen > 0.0005f;
            if (_pitRenderer.gameObject.activeSelf != visible) _pitRenderer.gameObject.SetActive(visible);
            if (!visible) return;

            _pit.localScale = GetPitBaseScale();
            ApplyPitProperties();
        }

        void EnsureGlowPlate()
        {
            if (_glowPlate != null || neonMaterial == null || silhouetteMesh == null) return;
            _glowPlate = BuildPlate("NeonGlow", neonMaterial, silhouetteMesh, glowHeight, out _glowRenderer);
        }

        static Mesh _unitQuadXZ;

        /// <summary>Drops the shared quad so it is rebuilt per Play instead of leaking one mesh per Play
        /// when the domain reload is not there to clear the static.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (_unitQuadXZ != null) Destroy(_unitQuadXZ);
            _unitQuadXZ = null;
        }

        /// <summary>
        /// A 1x1 quad lying in the XZ plane, centred on the origin. The cavity shader reads
        /// positionOS.xz, so the pit needs a surface whose object-space extents are exactly
        /// known: it used to borrow the block's silhouette mesh, whose span measured 1.54 world
        /// units for a nominal 2-cell shape and whose centre was offset from the pivot, which is
        /// what clipped the wider hole shapes and made every band width unpredictable. With this
        /// quad, scaling the plate by k and setting _QuadScale to k makes the shader's SDF read
        /// in world cells measured straight from the hole pivot.
        /// </summary>
        static Mesh UnitQuadXZ()
        {
            if (_unitQuadXZ != null) return _unitQuadXZ;
            Mesh m = new Mesh();
            m.name = "Case2_PitQuadXZ";
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3( 0.5f, 0f,  0.5f), new Vector3( 0.5f, 0f, -0.5f)
            };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            m.hideFlags = HideFlags.DontSave;
            _unitQuadXZ = m;
            return m;
        }

        Transform BuildPlate(string plateName, Material material, Mesh mesh, float height, out Renderer rendererOut)
        {
            GameObject go = new GameObject(plateName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(0f, 0.02f, 0f);
            go.hideFlags = HideFlags.DontSave;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            rendererOut = mr;
            go.SetActive(false);
            return go.transform;
        }
    }
}
