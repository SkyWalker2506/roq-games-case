// Analytical cavity shader for the board holes.
//
// Measured across the purple cross cavity in ref_0.00s.png (row j6.5, right edge at cell x=3.0):
//   x=2.85  44/ 11/165  L= 74   inner wall, saturated, FLAT
//   x=2.95  57/ 15/173  L= 82   wall -> lip
//   x=3.05 207/ 98/254  L=187   LIP - and note this is OUTSIDE the opening
//   x=3.15 192/101/248  L=181   lip
//   x=3.25 101/ 64/179  L=115   falloff into the board
//   x=3.35  49/ 60/115  L= 75   plain tile
// and the left wall reads 44/11/167 against the right wall's 44/11/166 - the same to within
// one code value, so the inner wall is NOT directionally shaded, it is a uniform band.
//
// Two things follow, and both were wrong before:
//   1. The bright lip sits OUTSIDE the opening, ~0.20 cells of raised bevel lying on the board.
//      The old shader clipped at d > 0.04, so it could not draw an outer ring at all, and the
//      hole read as a dark shape painted onto the tiles. Ring/core brightness measured 1.28
//      against the reference's 2.04.
//   2. The inner wall is ~0.45 cells wide at roughly 2.3x the floor. The old wall band was
//      0.22 wide and its lighting term collapsed to ~0.1 on the shadowed side, which made it
//      colourimetrically identical to the floor - our transect was flat at 33/7/50 all the way
//      across, with no wall at all.
//
// All shape SDFs below are in WORLD CELLS measured from the hole pivot, which sits at the
// shape's bounding-box centre. The caller sets _QuadScale to the pit plate's scale to make
// that true. Nothing here depends on the silhouette mesh's extents any more.
Shader "Case2/HoleDepthGradient"
{
    Properties
    {
        [MainColor] _LipColor("Lip Base Color", Color) = (0.12, 0.44, 0.09, 1)
        _PitTopColor("Inner Wall Color", Color) = (0.05, 0.16, 0.08, 1)
        _PitBottomColor("Pit Floor Color", Color) = (0.015, 0.045, 0.025, 1)
        _BoardTint("Board Tint (outer ring fade target)", Color) = (0.216, 0.255, 0.470, 1)
        _LipWidth("Lip Reach Inside", Range(0.0, 0.30)) = 0.12
        _LipOuter("Lip Reach Outside", Range(0.0, 0.60)) = 0.0
        _LipFade("Lip Outer Falloff", Range(0.01, 0.30)) = 0.03
        _LipLift("Lip Whitening", Range(0, 1)) = 0.0
        _WallHeight("Visible Far-Wall Depth (cells)", Range(0.1, 2.0)) = 1.0
        _BevelIntensity("Lip Bevel Intensity", Range(0, 1.5)) = 0.50
        // DEAD: declared here and in the CBUFFER, read by no line of the fragment program.
        // The cavity's contrast comes from _PitTopColor / _PitBottomColor (0.60 and 0.19 tints
        // of the hole colour), which is where a real contrast change has to be made.
        _CavityContrast("Cavity Depth Contrast (DEAD - unread)", Range(1.0, 4.0)) = 2.0
        _Open("Pit Open (1 = fully open, 0 = sealed)", Range(0, 1)) = 1
        // Must exceed the deepest interior distance of the widest opening or that opening can
        // never seal: the Square/P hole is a 3x2 box whose centre is 1.0 cells from its nearest
        // edge, so at 0.8 it eroded down to a smaller patch and stopped. HoleGlowHighlight also
        // writes this through its MaterialPropertyBlock; the .mat serialises no value for it.
        _CloseErode("Erosion at Full Close (cells)", Range(0, 2)) = 1.3
        _ShapeType("Shape Type (0=P, 1=Cross, 2=Bar, 3=L)", Float) = 0
        _QuadScale("Quad Scale", Float) = 1.0

        // ---- target-hole halo. MEASURED off Block Hole.mp4 (65 fps, 939 frames), by differencing
        // a rest frame against a held frame so the halo localises itself instead of being looked
        // for at guessed coordinates.
        //
        // The profile is taken on a STRAIGHT LEFT EDGE (the green hole's col-4 boundary over row
        // j0), not as a mean over an annulus. The annulus was measured first and it lies: it
        // averages a narrow, near-saturated core with a lot of empty board and returns a broad,
        // weak halo. Building that gave a flat purple wash 0.45 cells wide where the reference has
        // a thin bright rim. On the transect, f190 at rest against f205 held:
        //
        //   d (cells)  0.000  0.024  0.048  0.072  0.097  0.121  0.145  0.169  0.193  0.217  0.242  0.266  0.290  0.314
        //   dG         +28.6 +121.5 +126.6 +115.9 +100.2  +88.9  +72.4  +59.5  +41.0  +27.8  +14.7   +7.0   +2.6   +0.2
        //
        // Solving lit = (1-a)*rest + a*neon per channel gives a = ~1.0 out to 0.07 cells, 0.6 at
        // 0.145, 0.25 at 0.217, 0.03 at 0.29 and 0 by 0.32. smoothstep(0.32, 0.08, d) reproduces
        // that to within the channel spread: it predicts 1.00 / 0.86 / 0.40 / 0.04 at those four
        // distances. A LINEAR ramp over 0.45 predicts 0.68 / 0.51 / 0.36 and is visibly wrong.
        //
        // INSIDE the opening the same difference is +2 to +3, i.e. nothing, so the halo is
        // strictly outside. Its colour is the hole's own - the delta is +G with -R and -B for the
        // green hole, which is an alpha blend toward the hole colour, not an additive white bloom.
        //
        // NOTE the transect must be taken on a LEFT or RIGHT edge. On the bottom edge the visible
        // far wall of the cavity puts the rim about 0.2 cells past where the distance field says
        // it is, and a transect there reads the halo as starting late.
        _GlowStrength("Target Glow (0 = dark, 1 = lit)", Range(0, 1)) = 0
        _GlowColor("Target Glow Colour (the hole's own)", Color) = (1, 1, 1, 1)
        _GlowReach("Glow Reach Outside (cells)", Range(0, 1)) = 0.32
        _GlowCore("Glow Saturated Core (cells)", Range(0, 0.5)) = 0.08
        _GlowPeak("Glow Alpha in the core", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "HoleCavity"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _LipColor;
            float4 _PitTopColor;
            float4 _PitBottomColor;
            float4 _BoardTint;
            float _LipWidth;
            float _LipOuter;
            float _LipFade;
            float _LipLift;
            float _WallHeight;
            float _BevelIntensity;
            float _CavityContrast;
            float _Open;
            float _CloseErode;
            float _ShapeType;
            float _QuadScale;
            float _GlowStrength;
            float4 _GlowColor;
            float _GlowReach;
            float _GlowCore;
            float _GlowPeak;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float sdBox(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Subtraction, used instead of a union of overlapping boxes.
            float sdSub(float shape, float cut) { return max(shape, -cut); }

            // p is in world cells relative to the hole pivot. +z is up-screen.
            //
            // Every shape is ONE outer box with cells cut out of it. That is not cosmetic:
            // min(a,b) is only a true distance field OUTSIDE the union. Inside, along the seam
            // where two constituent boxes meet, both report ~0, so min() reports ~0 too and the
            // lip band painted itself straight across the middle of every hole. That is what the
            // pale sage / pink / lavender blobs were - a false rim on an internal seam, not an
            // outer halo. A single box with cuts has no internal seams.
            #define CUT_BLEED 0.06

            float GetShapeSDF(float2 p, int shapeType)
            {
                // Every cut is OVERSIZED on its outward sides by CUT_BLEED and re-centred by the
                // same amount, so the cut's inner edges stay exactly on the cell line while its
                // outer edges push past the enclosing box.
                //
                // With a cut sized exactly 0.5 and centred exactly on the corner cell, the cut's
                // outer face lands on the enclosing box's own face - both at 1.5 for the plus -
                // so along that whole face d = max(0, -0) = 0. Zero distance is INSIDE the lip
                // band and survives clip(_LipOuter + _LipFade - d), which is why our cross painted
                // a bright rectangle around its 3x3 bounding box: measured on frame_137.png
                // against ref_1.30s.png, +9115 purple px at 1.30, and the excess sits in the cells
                // AROUND the cross - (0,j5) +1335, (0,j7) +2049, (2,j7) +1095, (3,j7) +596 - not
                // in the five cells the opening actually occupies.
                //
                // The over-fill measured per hole tracks the number of cuts exactly: the cyan bar
                // has none and reads +4 to +8 points against the reference, the one-cut red L
                // +5 to +13, the one-cut green P +17 to +25.
                if (shapeType == 0)
                {
                    // Green P: 3x2 with the lower-LEFT cell left as board.
                    // Cells (4,j0)(5,j0)(6,j0) over (5,j1)(6,j1). Pivot (5.5, 7.0).
                    return sdSub(sdBox(p, float2(1.5, 1.0)),
                                 sdBox(p - float2(-1.0 - CUT_BLEED * 0.5, -0.5 - CUT_BLEED * 0.5),
                                       float2(0.5 + CUT_BLEED * 0.5, 0.5 + CUT_BLEED * 0.5)));
                }
                else if (shapeType == 1)
                {
                    // Purple plus: 3x3 with all four corner cells cut.
                    // Cells (1,j5)(0,j6)(1,j6)(2,j6)(1,j7). Pivot (1.5, 1.5).
                    float2 h = float2(0.5 + CUT_BLEED * 0.5, 0.5 + CUT_BLEED * 0.5);
                    float2 o = float2(1.0 + CUT_BLEED * 0.5, 1.0 + CUT_BLEED * 0.5);
                    float d = sdBox(p, float2(1.5, 1.5));
                    d = sdSub(d, sdBox(p - float2(-o.x, -o.y), h));
                    d = sdSub(d, sdBox(p - float2( o.x, -o.y), h));
                    d = sdSub(d, sdBox(p - float2(-o.x,  o.y), h));
                    d = sdSub(d, sdBox(p - float2( o.x,  o.y), h));
                    return d;
                }
                else if (shapeType == 2)
                {
                    // Cyan bar: 1x3, a single box, no cuts - and so no perimeter seam either.
                    // Cells (6,j2)(6,j3)(6,j4). Pivot (6.5, 4.5).
                    return sdBox(p, float2(0.5, 1.5));
                }
                else
                {
                    // Red L: 3x2 with the TWO upper-right cells left as board, leaving four cells
                    // (4,j7)(5,j7)(6,j7) along the bottom and (4,j6) above the left end - the same
                    // four-cell footprint the red BLOCK has. Pivot (5.5, 1.0).
                    //
                    // This was a 2x2 minus its upper-right corner, i.e. three cells, and an
                    // occupancy audit of the reference agreed with it. The audit was counting
                    // VISIBLE red pixels on frame 0, and on frame 0 the fourth cell is underneath
                    // the cyan bar. Re-measured on Block Hole.mp4 (939 frames, 65 fps) at a frame
                    // where the bar has been dragged away, sampling a 25x25 px patch at each cell
                    // centre of the 7x8 grid:
                    //
                    //   cell (6,j7)   f0 41/227/250 (the cyan bar)   f471 116/0/7   f520 137/0/7
                    //   cell (5,j7)   f0 136/0/8                                    f520 136/0/7
                    //   cell (4,j7)   f0 44/0/0                                     f520 45/0/0
                    //   cell (4,j6)   f0 113/3/11                                   f520 151/0/6
                    //
                    // The bar leaves (6,j7) between f469 and f471 and the cell underneath it is
                    // the red opening, reading the same 137/0/7 as (5,j7) beside it. It stays that
                    // way for the rest of the clip.
                    //
                    // The cut is a 2x1, not a 1x1: it takes (5,j6) and (6,j6) together.
                    return sdSub(sdBox(p, float2(1.5, 1.0)),
                                 sdBox(p - float2(0.5 + CUT_BLEED * 0.5, 0.5 + CUT_BLEED * 0.5),
                                       float2(1.0 + CUT_BLEED * 0.5, 0.5 + CUT_BLEED * 0.5)));
                }
            }

            // EXACT distance to the opening, valid OUTSIDE it. Used by nothing but the halo.
            //
            // GetShapeSDF above is a subtraction, max(shape, -cut). That has the right SIGN
            // everywhere but the wrong MAGNITUDE outside a subtracted corner, because max() takes
            // whichever of the two terms is larger and both of them are lower bounds on the true
            // distance. Worked through on the plus, in cells from the pivot:
            //
            //   p = (1.50, 1.50) - the notch's outer corner, ON the 3x3 bounding box.
            //     sdBox(p, 1.5)     =  0.00      (on the enclosing box's own corner)
            //     -sdBox(p - cut)   = +0.06      (CUT_BLEED past the cut's outer face)
            //     max               = +0.06      true distance to the plus: 1.00
            //   p = (1.70, 1.20) - outside the bounding box, off the corner.
            //     max               = +0.20      true distance to the plus: 0.73
            //
            // The lip only reads d out to _LipOuter + _LipFade = 0.03 cells, which is why
            // CUT_BLEED was enough to keep IT off the notches. The halo reads d out to
            // _GlowReach = 0.32, and at 0.06 and 0.20 the whole notch is inside that band: the
            // cross painted a bright rounded rectangle over its entire 3x3 bounding box with four
            // small dark islands where the notch centres finally passed 0.32. Measured on
            // .plan-build/verify/BlockHole/frame_00.png (HEAD, 254-frame strip): the four
            // 0.5x0.5-cell squares at the notches' OUTER corners - every point of which is at
            // least 0.5 cells from the plus - read 110/50/183 to 117/51/191 against plain board
            // 40/52/106 and 48/62/118, with 44% to 49% of their pixels outside the board's own
            // colour envelope. The cyan bar, which is a single sdBox with no cuts, is correct.
            //
            // A union of boxes IS the true distance field outside the union - it is only wrong
            // INSIDE, along the seam where two constituent boxes meet, which is the whole reason
            // GetShapeSDF cannot be written this way. The halo is gated to d >= 0, so it never
            // samples the seam. Keeping the two functions separate also means the pit interior,
            // the wall march and the lip are byte-for-byte the same program they were.
            float GetOuterSDF(float2 p, int shapeType)
            {
                if (shapeType == 0)
                {
                    // Green P: full top row, plus the bottom row minus its left cell.
                    return min(sdBox(p - float2( 0.0,  0.5), float2(1.5, 0.5)),
                               sdBox(p - float2( 0.5, -0.5), float2(1.0, 0.5)));
                }
                else if (shapeType == 1)
                {
                    // Purple plus: a 3x1 bar crossed with a 1x3 bar.
                    return min(sdBox(p, float2(1.5, 0.5)), sdBox(p, float2(0.5, 1.5)));
                }
                else if (shapeType == 2)
                {
                    // Cyan bar: one box, already exact - GetShapeSDF returns the same value.
                    return sdBox(p, float2(0.5, 1.5));
                }
                else
                {
                    // Red L: the full bottom row of three, plus the cell above its left end.
                    return min(sdBox(p - float2( 0.0, -0.5), float2(1.5, 0.5)),
                               sdBox(p - float2(-1.0,  0.5), float2(0.5, 0.5)));
                }
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.positionOS.xz * _QuadScale;
                int shapeType = (int)round(_ShapeType);

                float d = GetShapeSDF(p, shapeType);
                float dOut = GetOuterSDF(p, shapeType);

                // Sealing. The reference's target hole is GONE by 2.40s - the tiles are back and
                // only a couple of stray shards remain - but ClosePit had no visual effect at all
                // here: the plate's scale ignored _pitOpen, so the hole stayed open forever.
                // Eroding the distance field shrinks the opening shut from its whole outline.
                d += (1.0 - _Open) * _CloseErode;
                // The same erosion on the exact field, so a sealing hole cannot grow a halo where
                // its opening has just closed. _Open is 1 for the whole approach, so this is a
                // no-op everywhere the closure invariant is measured.
                dOut += (1.0 - _Open) * _CloseErode;

                // Discard everything past the outer ring so the board shows through - except
                // while this hole is the target, when the plate has to reach far enough out to
                // draw the halo. pitCoverScale is 4 on all four holes, so the plate spans +-2
                // cells from the pivot and the widest shape (the 3x3 cross) leaves exactly 0.5
                // cells of margin on its axes: the measured 0.32-cell reach fits with room.
                //
                // The halo arm of this clip is on dOut, not on d. It has to be: keeping a pixel
                // that d thinks is 0.06 cells out of a notch and dOut knows is 1.0 cells out would
                // paint _BoardTint over a real board tile and then put the halo on top of it.
                // With _GlowStrength = 0 the arm is -dOut, which is positive only where dOut < 0,
                // i.e. inside the opening, where the lip arm already keeps the pixel - so an unlit
                // hole clips exactly the set it clipped before and renders the same pixels.
                float glowOuter = _GlowReach * step(0.0001, _GlowStrength);
                clip(max(_LipOuter + _LipFade - d, glowOuter - dOut));

                // Outward perimeter normal, for the upper-left key light.
                float2 e = float2(0.004, 0.0);
                float2 edgeNormal = normalize(float2(
                    GetShapeSDF(p + e.xy, shapeType) - GetShapeSDF(p - e.xy, shapeType),
                    GetShapeSDF(p + e.yx, shapeType) - GetShapeSDF(p - e.yx, shapeType)) + 1e-5);
                float2 L = normalize(float2(-0.7071, 0.7071));
                float nDotL = dot(edgeNormal, L);

                // --- floor: the deepest part of the cavity
                float3 floorColor = _PitBottomColor.rgb;

                // --- inner wall.
                // The wall you can see is the one on the FAR (+z) side of the opening: the camera
                // looks down at 80 degrees, so it sees the cavity face that turns back toward it.
                // Verified against all sixteen hole cells of the reference - every cell whose
                // opening ends 0.5 cells above reads bright (cross 53/11/178, green 0/74/4,
                // red 113/0/4 and 152/0/4, cyan 0/111/210) and every cell with 1.5 or more cells
                // of opening above it reads dark (25/10/59, 2/13/0, 47/0/0, 0/41/68).
                //
                // This cannot be written as a distance field, which is why the old wall band never
                // appeared: the cross's centre cell lies FARTHER from every edge than its side
                // arms do, yet the centre is the dark one and the arms are the lit ones. So march
                // +z and find how soon the shape ends.
                float wallMask = 0.0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float t = (float(i) + 0.5) / 8.0 * _WallHeight;
                    float outside = step(0.0, GetShapeSDF(p + float2(0.0, t), shapeType));
                    wallMask = max(wallMask, outside * (1.0 - t / _WallHeight));
                }
                wallMask = pow(saturate(wallMask), 0.6);

                float3 wallColor = _PitTopColor.rgb;

                // --- lip. Measured across the left edge of all three non-glowing holes, the
                // rim is the hole's OWN colour and sits entirely INSIDE the opening, about 0.12
                // cells wide, with the board outside left untouched:
                //   green  d=+0.02 22/128/53  +0.08 4/111/4   then interior 0/74/4
                //   red    d=+0.02 168/18/38  +0.08 238/0/3   then interior 46/0/0
                //   cyan   d=-0.04 21/184/230                 then interior 0/41/68
                // and at d=-0.10 outside, every one of them still reads plain board tile. So the
                // rim is neither whitened nor does it reach outward - lerping it 33% to white in
                // LINEAR space is what produced the pale wash, and _LipOuter 0.20 is what put it
                // on the board. Both are now zero by default.
                float3 lipBase = lerp(_LipColor.rgb, float3(1.0, 1.0, 1.0), _LipLift);
                float rimHighlight = saturate(nDotL) * _BevelIntensity;
                float3 lipColor = lipBase * (0.80 + rimHighlight * 0.45);

                float3 col = lerp(floorColor, wallColor, wallMask);

                float lipT = smoothstep(-_LipWidth - 0.04, -_LipWidth + 0.04, d);
                col = lerp(col, lipColor, lipT);

                float outT = smoothstep(_LipOuter, _LipOuter + _LipFade, d);
                col = lerp(col, _BoardTint.rgb, outT);

                // --- target halo, OUTSIDE the mouth only.
                // Saturated out to _GlowCore, then a smoothstep to nothing at _GlowReach; see the
                // transect in the Properties block for where those two numbers come from.
                // step(0, d) keeps it strictly outside: the reference's interior does not change
                // when the hole lights up, and the sealed-hole closure invariant must not either.
                // On dOut, the exact outside distance - see GetOuterSDF. Reading d here is what
                // lit the plus's four notch cells. sign(dOut) == sign(d) everywhere, so step()
                // keeps meaning exactly "strictly outside the opening".
                float haloT = smoothstep(_GlowReach, min(_GlowCore, _GlowReach - 1e-4), dOut) * step(0.0, dOut);
                col = lerp(col, _GlowColor.rgb, saturate(_GlowStrength * _GlowPeak) * haloT);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
