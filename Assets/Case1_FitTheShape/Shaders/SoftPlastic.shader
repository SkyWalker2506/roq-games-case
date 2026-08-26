Shader "Case1/SoftPlastic"
{
    Properties
    {
        _BaseColor("Base Colour", Color) = (1,1,1,1)
        _Smoothness("Highlight Tightness", Range(0,1)) = 0.72
        _SpecularStrength("Highlight Strength", Range(0,1)) = 0.38
        _Wrap("Wrapped Light", Range(0,1)) = 0.45
        _ShadeStrength("Shade Strength", Range(0,1)) = 0.22
        _BevelDarken("Bevel Darkening", Range(0,0.6)) = 0.18
        _RimLift("Edge Lift", Range(0,0.35)) = 0.12
        _EdgeInk("Silhouette Ink", Range(0,1)) = 0.35
        _EdgeInkWidth("Silhouette Ink Width", Range(1,12)) = 4.5
        _OutlineWidth("Outline Width", Range(0,0.08)) = 0.009
        // Only the pieces at the FRONT of the tray carry a line. The rows differ by scale - the back
        // row is authored at 0.73 of the front's height - so the scale IS the row, and gating on it
        // needs no per-object wiring at all: a piece that grows as it comes forward fades its line in
        // on the way, which is what the owner asked for.
        _OutlineScaleMin("Outline Fade Start (Y scale)", Float) = 0.80
        _OutlineScaleMax("Outline Full (Y scale)", Float) = 0.95
        _OutlineColor("Outline Colour", Color) = (0.06,0.05,0.12,1)
        // MEASURED off CASE1_TEPSI.png: six-row mean brightness of the drum reads 123.5/118.3/107.7/
        // 128.2/101.4/75.3 (brightest:darkest = 1.70). Rows that curve away from the camera darken.
        // Our flat capture read ratio 1.32. This term darkens by how far the surface faces away from
        // the view, so drum rows genuinely fall off while the camera-facing row keeps full brightness.
        // Default 0 so tray/deck/plate materials that share this shader are untouched; drum cell
        // materials opt in with an explicit value.
        _CurveDarken("Drum Curvature Darken", Range(0,1)) = 0
        _VertShade("Vertical Shade", Range(0,0.8)) = 0
        _VertShadeBias("Vertical Shade Bias", Range(-1,1)) = 0
        _BottomDarken("Bottom Gradient Darken", Range(0,1)) = 0.58
        _BottomDarkenPower("Bottom Gradient Power", Range(0.5,4)) = 1.35
        
        [Header(Shape Heightmap Indentation)]
        _ShapeType("Shape Type (0=None, 1=Square, 2=Triangle, 3=Hexagon, 4=Star, 5=Diamond)", Float) = 0
        _IndentDepth("Indent Parallax Depth", Range(0, 0.4)) = 0.18
        _IndentBevel("Indent Bevel Width", Range(0.01, 0.15)) = 0.065
        // How much harder the entrance sinks at the CORNERS than along the straight edges. Keyed on
        // the SDF's curvature, so it follows whatever shape the socket is - four corners on a
        // square, three on a triangle, ten on a star - instead of being told where to look.
        _IndentCornerSink("Indent Corner Sink", Range(0, 2)) = 0.9
        // Darkening laid exactly on the line where the wall turns into the floor. Without it the
        // wall and the floor are the same tone where they meet and read as one surface - the owner:
        // "birlesim yerleri curvlu olup ayrismali", "yapisiklar". Contact shadow is what separates
        // two planes that touch; the curve alone cannot, because a smooth join has no edge to see.
        _IndentCreaseAO("Indent Crease Shadow", Range(0, 1)) = 0.55
        _IndentFloorDarken("Indent Floor Darkness", Range(0, 1.0)) = 0.72
        // MEASURED off Fit The Shape.mp4 f_010. The reference socket is not a scaled copy of the
        // cell colour: inset/face per channel reads (0.272, 0.006, 0.000) on the orange cell and
        // (0.243, 0, 0) on the red - the dominant channel keeps about a quarter while the minor
        // channels are crushed to nothing. A scalar preserves those ratios and cannot get there:
        // _IndentFloorDarken alone lands orange at (69,46,14) against the reference (69,1,0).
        // Light entering the socket reflects off the coloured walls before it escapes, so it leaves
        // filtered by albedo^k. Fitted k = 10.2 on the orange and red cells. Default 1 is an exact
        // no-op (lerp(x, x, mask) == x), so materials that do not opt in are untouched.
        _CavityBounce("Cavity Bounce (albedo^k)", Range(1, 12)) = 1
        // MEASURED on our own capture after the bounce landed. Inside the socket the multiplicative
        // path is crushed to near zero, but the specular and rim below are ADDITIVE and are not
        // gated by the cavity, so they re-inject a neutral pedestal: the socket reads g == b == 27
        // constant while r slides 71..100, and the green cell - whose every channel the bounce
        // annihilates - comes out pure grey #1D1D1D (std 4.0, max |R-G| = 4 over 2097 px). The
        // reference socket has almost none of this: #450100, g = 1. Fitted 0.70 rather than full
        // removal because 1.0 overshoots by 2.0-2.7 L* on orange/pink/purple - the reference socket
        // does catch a little light. Default 0 is an exact no-op.
        _CavityLightKill("Cavity Light Kill", Range(0, 1)) = 0
        // GENERAL RULE, third instance: THE BEVEL AND THE FLOOR ARE DIFFERENT SURFACES AND MUST BE
        // GATED SEPARATELY. f7c726e established it for the additive light term; these two gates apply
        // the same rule to the bounce and to the floor darkening. slope is the bevel band in all three.
        //
        // _CavityBevelRelief: the bounce crushes every channel but the base's dominant one, so the
        // SURVIVING CHANNEL'S LUMINANCE WEIGHT decides what is left - red carries 0.2126, blue only
        // 0.0722. MEASURED: the bounce keeps 0.255 of face luminance on the orange diamond and 0.107
        // on the purple triangle, hitting purple 2.4x harder for reasons having nothing to do with its
        // socket. Meanwhile the reference bevel sits at 0.406 (orange) and 0.425 (purple) of its OWN
        // face - near-identical across two very different hues, which is a wall that is shaded but not
        // colour-filtered. Light reaching the floor bounces more times than light near the opening.
        //
        // _CavityFloorExtra: floor-only darkening, so the floor can drop without dragging the bevel
        // down with it. Reference floor/face is 0.092 orange, 0.072 purple; ours measured 0.150/0.129.
        //
        // BOTH ARE PER-MATERIAL AND BOTH DEFAULT TO 0. The pink star wants neither: its reference
        // bevel/face is 0.205 and floor/face 0.169, and ours already exceeds that step, so any global
        // value tuned on orange and purple makes pink worse while every aggregate number improves.
        _CavityBevelRelief("Cavity Bevel Relief", Range(0, 1)) = 0
        _CavityFloorExtra("Cavity Floor Extra Darken", Range(0, 1)) = 0
        // MEASURED off ref_frame_001, inward luminance profile from each socket's edge sampled at
        // reference-scale depths, normalised to the floor centre. The reference descends through a
        // WALL and bottoms out in a CREASE well inside the socket before the floor recovers:
        //     ref diamondA  trough at depth 10, recovers 13.3%   ref hexagon  depth 8,  21.2%
        //     ref diamondB  trough at depth  8, recovers 14.6%   ref STAR     depth 6,  22.3%
        //     ref triangle  trough at depth  8, recovers 25.7%
        // Ours reached its floor at depth 3 in EVERY cell and recovered 0.5-10.8% - the profile went
        // 6.09, 3.04, 0.89 and then dead flat. A rim and a floor with no wall between them is a flat
        // plate with an outline, which is what "derinligi artir" was pointing at.
        //
        // Why two knobs and not just a wider _IndentBevel: bevel width drives BOTH the outward edge
        // ramp (cavityMask, 0..0.8*bevel outside) and the inward wall band, so widening it to build a
        // wall also re-softens the cut that 721af8f sharpened. _IndentWall drives the INWARD half
        // only. 0 keeps the old 0.8*_IndentBevel exactly, so materials that do not opt in are
        // untouched.
        _IndentWall("Indent Wall Width (0 = 0.8 x bevel)", Range(0, 0.30)) = 0
        // Ambient occlusion in the crease where the wall meets the floor, easing off toward the floor
        // centre. This is what turns a flat floor into a trough-and-recovery. Default 0 is an exact
        // no-op.
        //
        // THIS PROPERTY HAS A CLOSED FORM, USE IT INSTEAD OF FITTING. At dist = -_IndentWall the
        // crease is at full strength AND slope is exactly 0, so the perturbed normal equals the floor
        // centre's and diffuse, bevel, specular and rim all cancel between the two points:
        //     wall-trough luminance / floor-centre luminance == 1 - _CavityCrease
        // Confirmed on disk at 0.22: predicted 0.780, measured 0.778 over five cells of our own
        // capture (spread 0.750-0.790).
        //
        // MEASURED TARGET, f_010, unpooled horizontal scanline through each socket centre:
        //     reference trough/centre  0.457 0.399 0.474 0.453 0.356  -> mean 0.428  -> c = 0.57
        // Do NOT re-tune this against a radially pooled inward profile. Pooling averages the
        // reference's near-black rim together with its shallow corners and reports 0.79-0.83 for the
        // reference itself - indistinguishable from an unfixed ours. That is how 0.22 was arrived at,
        // and it is the same failure family as the window that once sampled the socket WALL and
        // called the floor un-flat: the metric was blind to the very cue being tuned.
        _CavityCrease("Cavity Crease Occlusion", Range(0, 0.6)) = 0
        // MEASURED off f_010, saturation across the socket's opening edge (per-pixel
        // (max-min)/max, unpooled scanline perpendicular to the hexagon's vertical edge and the
        // triangle's):
        //     offsets -3 -2 -1 +0 +1 +2 +3
        //     ref hexagon   86 86 90 100 100 100 100      ref triangle  82 82 82 98 98 93 91
        //     our hexagon   87 85 82  78  78  82  97      our triangle  82 82 80 77 76 75 87
        // The reference's opening edge is the MOST saturated place on the cell - chroma rises
        // monotonically as the plastic turns into the wall. Ours DIPS to 75-78 first: a grey
        // smudge exactly where the hard cut should be, which is what reads as a soft drop shadow
        // pasted onto the face instead of an opening in it.
        //
        // NOT antialiasing. AA between the face (sat 86) and the floor (sat 100) can only produce
        // values BETWEEN 86 and 100; ours goes below both endpoints, so a whitish term is being
        // added, not interpolated.
        //
        // It is the additive pedestal, relocated rather than removed. _CavityLightKill = 1 zeroes
        // the specular and rim on the FLOOR, but its (1 - slope) factor deliberately spares the
        // BEVEL so the ring survives. keyColor is near-white and the albedo underneath has already
        // been crushed by the bounce, so what survives on the bevel is a near-white pedestal over
        // near-black - grey. This is the same failure the file already documents for
        // _CavityLightKill 0.7 ("the grey came from lighting, not albedo"), moved from the floor to
        // the bevel band by the very gate that protects the ring.
        //
        // Dimming it is the wrong correction: the reference's edge BRIGHTNESS already matches ours
        // (0.55 vs 0.59 of face at offset -1). Only its chroma differs. So tint the cavity's
        // additive light by the cell's own albedo instead - light that reaches the bevel has
        // bounced off coloured walls, the same physical story _CavityBounce already tells for the
        // multiplicative path. Brightness is preserved, chroma is restored. Default 0 is an exact
        // no-op (lerp to white), so tray, deck and plate materials are untouched.
        _CavityBevelTint("Cavity Bevel Light Tint", Range(0, 1)) = 0
        _IndentInnerShadow("Indent Inner Shadow", Range(0, 1.0)) = 0.68
        _IndentScale("Indent Scale", Range(0.5, 2.0)) = 1.0
        
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [HideInInspector]_OffsetFactor("Offset Factor", Float) = 0
        [HideInInspector]_OffsetUnits("Offset Units", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "SoftPlastic"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _OutlineColor;
            float _Wrap;
            float _ShadeStrength;
            float _Smoothness;
            float _SpecularStrength;
            float _BevelDarken;
            float _RimLift;
            float _EdgeInk;
            float _EdgeInkWidth;
            float _OutlineWidth;
            float _CurveDarken;
            float _VertShade;
            float _VertShadeBias;
            float _BottomDarken;
            float _BottomDarkenPower;
            float _ShapeType;
            float _IndentDepth;
            float _IndentBevel;
            float _IndentCornerSink;
            float _IndentCreaseAO;
            float _IndentFloorDarken;
            float _CavityBounce;
            float _CavityLightKill;
            float _CavityBevelRelief;
            float _CavityFloorExtra;
            float _IndentWall;
            float _CavityCrease;
            float _IndentInnerShadow;
            float _CavityBevelTint;
            float _IndentScale;
            float _Cull;
            float _OffsetFactor;
            float _OffsetUnits;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                float heightOS : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.heightOS = input.positionOS.y;
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.shadowCoord = GetShadowCoord(p);
                return output;
            }

            // -------------------------------------------------------------
            // 2D Signed Distance Fields for Exact Shape Footprints
            // -------------------------------------------------------------
            float sdBox2D(float2 p, float2 b) {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            float sdHexagon2D(float2 p, float r) {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            float sdEquilateralTriangle2D(float2 p, float r) {
                const float k = 1.73205080757;
                p.x = abs(p.x) - r;
                p.y = p.y + r / k;
                if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
                p.x -= clamp(p.x, -2.0 * r, 0.0);
                return -length(p) * sign(p.y);
            }

            float sdStar5_2D(float2 p, float r, float rf) {
                const float2 k1 = float2(0.809016994375, -0.587785252292);
                const float2 k2 = float2(-k1.x, k1.y);
                p.x = abs(p.x);
                p -= 2.0 * max(dot(k1, p), 0.0) * k1;
                p -= 2.0 * max(dot(k2, p), 0.0) * k2;
                p.x = abs(p.x);
                p.y -= r;
                float2 ba = rf * float2(-k1.y, k1.x) - float2(0.0, 1.0);
                float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
                return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
            }

            float EvaluateShapeSDF(float2 p, float shapeId) {
                // ShapeId enum in ShapeId.cs: Round=0, Square=1, Triangle=2, Hexagon=3, Star=4, Diamond=5
                if (shapeId < 0.5) return 1.0;
                if (shapeId < 1.5) return sdBox2D(p, float2(0.56, 0.56)) - 0.08;                         // 1 = Square
                if (shapeId < 2.5) return sdEquilateralTriangle2D(float2(p.x, p.y + 0.12), 0.72) - 0.06; // 2 = Triangle
                if (shapeId < 3.5) return sdHexagon2D(p.yx, 0.58) - 0.06;                                 // 3 = Hexagon
                // MEASURED off Fit The Shape.mp4 f_001, the reference's own pink star socket: the
                // radius from its centroid to a point is 34.0 px and to a valley 21.0 px, so
                // valley/tip = 0.618. Ours read 0.532 - a spikier star with longer, thinner points.
                // rf IS that ratio, so 0.48 -> 0.62 lands it (the -0.06 rounding lifts the measured
                // value about 0.05 above rf). The tip radius 0.78 is unchanged: rf sets fatness only,
                // and the socket's SIZE is set by _IndentScale, which is measured separately.
                if (shapeId < 4.5) return sdStar5_2D(p, 0.78, 0.62) - 0.06;                              // 4 = Star
                if (shapeId < 5.5) {
                    // 5 = Diamond (square rotated 45 degrees)
                    float2 p_rot = float2(p.x + p.y, p.x - p.y) * 0.70710678f;
                    return sdBox2D(p_rot, float2(0.52f, 0.52f)) - 0.08f;
                }
                return sdBox2D(p, float2(0.56, 0.56)) - 0.08;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 baseRGB = _BaseColor.rgb;
                // 1 outside the socket; driven down inside it so the additive highlight terms
                // below cannot put a pedestal under a base the cavity has already crushed.
                half cavityAtten = 1.0h;
                half creaseShade = 1.0h;   // contact shadow on the wall/floor and face/wall joins
                // White inside and out until a socket opts in; tints the ADDITIVE terms only.
                half3 cavityTint = half3(1.0h, 1.0h, 1.0h);
                half3 faceAlbedo = baseRGB;   // saved before the cavity crushes it

                // ------------------------------------------------------------------
                // 1. PHYSICAL 3D HEIGHTMAP CAVITY INDENTATION (Carved directly into cell body)
                // ------------------------------------------------------------------
                if (_ShapeType > 0.5 && input.normalOS.y > 0.45)
                {
                    // Parallax view displacement for 3D physical depth
                    float3 viewOS = TransformWorldToObjectDir(viewDir);
                    float2 uvOffset = viewOS.xz * _IndentDepth * 0.15;
                    float2 p = (float2(input.positionOS.x * 2.2, input.positionOS.z * 3.4) + uvOffset) / _IndentScale;

                    float dist = EvaluateShapeSDF(p, _ShapeType);

                    // Compute SDF gradient for 3D inward-facing slope normals
                    float eps = 0.015;
                    float sxp = EvaluateShapeSDF(p + float2(eps, 0), _ShapeType);
                    float sxm = EvaluateShapeSDF(p - float2(eps, 0), _ShapeType);
                    float syp = EvaluateShapeSDF(p + float2(0, eps), _ShapeType);
                    float sym = EvaluateShapeSDF(p - float2(0, eps), _ShapeType);
                    float dx = (sxp - sxm) / (2.0 * eps);
                    float dy = (syp - sym) / (2.0 * eps);
                    float2 grad = normalize(float2(dx, dy) + 0.0001);

                    // CORNER DETECTION, measured off the shape rather than hard-coded.
                    //
                    // The Laplacian of a distance field is ~0 along a straight edge - the field is a
                    // ramp there, second derivative zero - and rises like 1/r where the boundary
                    // curves. So it IS "how much of a corner is this", and it works for whatever the
                    // socket happens to be: four on a square, three on a triangle, ten on a star. No
                    // per-shape table, nothing to keep in sync with EvaluateShapeSDF.
                    float lap = (sxp + sxm + syp + sym - 4.0 * dist) / (eps * eps);
                    float corner = saturate(lap * eps);

                    // Beveled rim profile: inward normal perturbation inside the shape boundary.
                    // The peak sits ON the shape boundary and the band fades to zero at +bw OUTSIDE
                    // (the cut, unchanged) and at -ww INSIDE (the wall, now its own width).
                    float bw = _IndentBevel;
                    float ww = (_IndentWall > 0.0001) ? _IndentWall : bw * 0.8;
                    // MONOTONIC on the outside, which is what makes the entrance read as CURVED
                    // rather than as a flat frame with a crease cut in it.
                    //
                    // The old profile was sin(t * pi) across the whole band: a symmetric ridge that
                    // peaked ON the boundary and fell to zero BOTH ways. Outside the hole that means
                    // the face tilts up, crests, then drops - a raised lip. The eye reads a raised
                    // lip as a flat plate with a groove in it, which is exactly the "duz cerceve"
                    // the reference does not have.
                    //
                    // Now the outer half ramps straight from flat at +bw to full tilt at the
                    // boundary with no crest anywhere, so the face bends continuously down into the
                    // opening - one surface curving in. The inner half is unchanged in spirit: it
                    // still falls away from the boundary so the wall turns vertical and meets the
                    // floor.
                    float slope = 0.0;
                    if (dist < bw)
                    {
                        slope = (dist >= 0.0)
                              ? smoothstep(0.0, 1.0, 1.0 - saturate(dist / bw))
                              : 1.0 - smoothstep(0.0, 1.0, saturate(-dist / ww));
                    }
                    // The corners sink harder. Multiplying the SLOPE rather than widening the band
                    // keeps the opening the same size - a corner that ate into the face would change
                    // the socket's silhouette, and the reference's does not.
                    slope *= 1.0 + corner * _IndentCornerSink;

                    // WHERE THE WALL MEETS THE FLOOR, and where the face meets the wall.
                    //
                    // The profile already joins these smoothly - smoothstep has zero slope at both
                    // ends, so the wall's foot is tangent to the floor and the face's lip is tangent
                    // to the wall. That is the curve. But a tangent join has no edge, and with both
                    // sides lit the same it reads as one continuous surface, which is why they look
                    // stuck together.
                    //
                    // So the join gets a contact shadow instead of a crease: darkest exactly on the
                    // line, fading both ways. Two planes that touch are separated by the shadow in
                    // the corner between them, not by a hard edge - and this keeps the geometry
                    // smooth while making the junction legible.
                    float foot = (dist < 0.0) ? smoothstep(0.55, 1.0, saturate(-dist / ww)) : 0.0;
                    float lip  = (dist >= 0.0) ? smoothstep(0.55, 1.0, saturate(dist / bw)) : 0.0;
                    creaseShade = (half)(1.0 - _IndentCreaseAO * (foot * 0.85 + lip * 0.35));

                    // Perturbed Object Space normal (deep steep carved inward socket)
                    float3 N_OS = normalize(float3(grad.x * slope * 3.2f, 1.0f, grad.y * slope * 3.2f));
                    normalWS = normalize(TransformObjectToWorldNormal(N_OS));

                    // Cavity depth shading and floor ambient occlusion
                    float cavityMask = 1.0 - smoothstep(-0.012, 0.012, dist);
                    
                    // Top-down inner ceiling drop shadow
                    float innerShadow = saturate(p.y * 1.4f + 0.35f) * _IndentInnerShadow * cavityMask;
                    // MEASURED, reference f_010 orange socket, rim->centre luminance profile:
                    // bands 0-4 sit at Y 71/71/71/70/68, then it STEPS to 13/13/14/15 at bands 5-8.
                    // The reference socket is not a gradient and not a flat cut-out: it is a bright
                    // bevel ring around a black floor, a 5.4x step. Gating the additive highlight on
                    // cavityMask alone suppressed it everywhere and flattened ours to 26.5 across the
                    // whole socket - level matched, carving destroyed. slope is already the bevel band
                    // (peaks at the shape boundary, zero on the floor), so kill the light on the FLOOR
                    // and leave the bevel free to catch its highlight.
                    cavityAtten = 1.0h - cavityMask * (1.0h - slope) * _CavityLightKill;
                    // Light that reaches the bevel has bounced off the coloured wall, so the
                    // highlight it carries is tinted, not white. Gated by cavityMask so the
                    // face outside the socket keeps its neutral specular untouched.
                    cavityTint = lerp(half3(1.0h, 1.0h, 1.0h), saturate(faceAlbedo), cavityMask * _CavityBevelTint);

                    // Deepen cavity floor with rich dark tone
                    // Multi-bounce colour filtering inside the socket, applied only where the
                    // cavity actually is; _CavityBounce = 1 makes this lerp an exact no-op.
                    half3 cavityAlbedo = pow(saturate(baseRGB), _CavityBounce);
                    baseRGB = lerp(baseRGB, cavityAlbedo, cavityMask * (1.0h - slope * _CavityBevelRelief));
                    baseRGB *= lerp(1.0h, 1.0h - _IndentFloorDarken * (1.0h - slope), cavityMask);
                    baseRGB *= lerp(1.0h, 1.0h - _CavityFloorExtra * (1.0h - slope), cavityMask);
                    // Crease occlusion: full at the wall's base (dist = -ww), easing to nothing by
                    // 2.5 wall-widths further in, and gated by (1 - slope) so it darkens the FLOOR
                    // and not the wall above it. Same rule as _CavityBevelRelief and
                    // _CavityFloorExtra: the bevel and the floor are different surfaces.
                    float crease = saturate(1.0h - max(0.0h, -dist - ww) / (ww * 2.5h));
                    baseRGB *= lerp(1.0h, 1.0h - _CavityCrease, cavityMask * crease * (1.0h - slope));
                    baseRGB *= saturate(1.0h - innerShadow);
                }

                // ------------------------------------------------------------------
                // 2. SELF-CONTAINED STYLIZED TOY LIGHTING
                // Key directional light from top-left front (matches reference lighting)
                // ------------------------------------------------------------------
                half3 keyDir = normalize(half3(-0.35h, 0.85h, -0.45h));
                half3 keyColor = half3(1.0h, 0.98h, 0.96h);

                // Wrapped diffuse for soft, vibrant rounded toy shading
                half ndl = dot(normalWS, keyDir);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                half diffuse = lerp(1.0h - _ShadeStrength, 1.0h, smoothstep(0.02h, 0.98h, wrapped));

                // Rounded bevel normals with rich toy contrast
                half facing = saturate(dot(normalWS, viewDir));
                half bevel = lerp(1.0h - _BevelDarken, 1.0h, smoothstep(-0.08h, 1.02h, facing));
                // Contact shadow on the two junction lines - the wall's foot and the face's lip.
                // Applied to the diffuse base only, so the specular that runs along the curve still
                // catches the light and the join reads as a rounded corner rather than a painted
                // line.
                half3 colour = baseRGB * diffuse * bevel * creaseShade;

                // Glossy plastic specular highlight (curved highlight on top face and top bevel)
                half3 halfVector = SafeNormalize(keyDir + viewDir);
                half exponent = lerp(16.0h, 128.0h, _Smoothness);
                half specTerm = pow(saturate(dot(normalWS, halfVector)), exponent);
                half specular = specTerm * _SpecularStrength * 2.2h;
                colour += keyColor * cavityTint * specular * cavityAtten;

                // Soft top rim Fresnel highlight
                half rim = pow(1.0h - facing, 2.5h) * _RimLift * 1.8h;
                colour += lerp(baseRGB, half3(1, 1, 1), 0.5h) * cavityTint * rim * cavityAtten;

                // ------------------------------------------------------------------
                // 3. CONTINUOUS MULTI-STAGE VERTICAL SHADOW GRADIENT (Bottom -> Top)
                // Mesh Y bounds are [0.0, 0.36]. Normalizing by 0.36 maps base to 0.0 and top to 1.0.
                // 1. Base (hNorm 0.0 -> 0.35): dense dark shadow at base (shadowWeight 1.0 -> 0.75).
                // 2. Mid to upper (hNorm 0.35 -> 0.88): continues smoothly climbing as a soft shade.
                // 3. Top bevel/face (hNorm >= 0.88): zero shadow, bright and clean top face.
                // ------------------------------------------------------------------
                half hNorm = saturate(input.heightOS / 0.36h + _VertShadeBias);
                half shadowWeight;
                if (hNorm < 0.35h)
                {
                    half t = hNorm / 0.35h;
                    shadowWeight = lerp(1.0h, 0.75h, t * t);
                }
                else if (hNorm < 0.88h)
                {
                    half t = (hNorm - 0.35h) / 0.53h;
                    shadowWeight = lerp(0.75h, 0.0h, t * (2.0h - t));
                }
                else
                {
                    shadowWeight = 0.0h;
                }
                half bottomGrad = shadowWeight * _BottomDarken;
                colour *= saturate(1.0h - bottomGrad);

                // ------------------------------------------------------------------
                // 3b. DRUM CURVATURE FALL-OFF (target rows 128 -> 75, ratio 1.70)
                // MEASURED: with the 10.5 degree tele camera the face-centre view-facing across the
                // six visible drum rows is 0.78/0.93/0.99/1.00/0.94/0.80 - symmetric and nearly
                // flat, so a facing term cannot tell row 5 from row 1; it only darkened bevel
                // edges and flattened the profile (capture ratio fell 1.52 -> 1.41). World height
                // is monotonic across the rows (ray-cast against the drum cylinder, axis y=3.0
                // z=6.89 r=2.38: row centres sit at worldY 5.28/4.98/4.57/4.09/3.52/2.86), so the
                // fade is keyed on positionWS.y: a gentle ease-off above 4.6 and a hard fall-away
                // below 3.9, scaled by the material's _CurveDarken (0 leaves tray/deck untouched).
                // ------------------------------------------------------------------
                colour *= saturate(1.0h
                    - _CurveDarken * 0.40h * smoothstep(4.6h, 5.4h, input.positionWS.y)
                    - _CurveDarken * 0.60h * (1.0h - smoothstep(2.6h, 3.9h, input.positionWS.y)));

                // ------------------------------------------------------------------
                // 4. SILHOUETTE EDGE INK OUTLINE
                // ------------------------------------------------------------------
                half edge = pow(1.0h - facing, _EdgeInkWidth);
                colour *= lerp(1.0h, 1.0h - _EdgeInk, saturate(edge));

                return half4(colour, _BaseColor.a);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ silhouette outline
        //
        // _OutlineWidth and _OutlineColor have been declared in this shader since the first commit
        // and NOTHING ever read them - no pass, no fragment use, the width defaulting to 0. So the
        // dark contour the reference blocks carry has never been drawn by us; this is an addition,
        // not a restoration, and I checked the history rather than assuming a regression.
        //
        // Inverted hull, which is the right tool for a solid the camera sees in 3D: draw the mesh a
        // second time, push every vertex out along its normal, keep only the FRONT-culled faces and
        // paint them flat. What survives is exactly the silhouette, a constant band wide, and it
        // costs one extra draw with no depth trickery.
        //
        // The extrusion is scaled by the clip-space w so the band holds its width on screen instead
        // of growing with the piece's distance - the shapes sit at different depths on the tray and
        // an object-space extrusion would give the near ones a fatter line than the far ones.
        Pass
        {
            Name "SoftPlasticOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _OutlineColor;
            float _Wrap;
            float _ShadeStrength;
            float _Smoothness;
            float _SpecularStrength;
            float _BevelDarken;
            float _RimLift;
            float _EdgeInk;
            float _EdgeInkWidth;
            float _OutlineWidth;
            float _OutlineScaleMin;
            float _OutlineScaleMax;
            float _CurveDarken;
            float _VertShade;
            float _VertShadeBias;
            CBUFFER_END

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            OutlineVaryings vertOutline(OutlineAttributes input)
            {
                OutlineVaryings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);

                // RADIAL, not the vertex normal - and that is what fixes the star.
                //
                // An inverted hull pushed along the normal breaks wherever the surface is concave:
                // at the star's five notches the two adjacent side faces have normals more than 90
                // degrees apart, so the extruded faces cross each other and the band tears. No
                // amount of width tuning helps, because the failure is the direction field being
                // discontinuous there.
                //
                // These pieces are extruded flat shapes seen from above, so their silhouette IS the
                // XZ outline, and pushing every vertex away from the piece's own axis is a
                // CONTINUOUS field over that outline - concave or not, neighbouring vertices always
                // move in nearly the same direction and nothing can cross. Near the axis, where the
                // radial direction is undefined and the vertex is not on the silhouette anyway, it
                // falls back to the normal.
                float3 radialOS = float3(input.positionOS.x, 0.0, input.positionOS.z);
                float  r        = length(radialOS);
                float3 dirOS    = r > 1e-4 ? radialOS / r : input.normalOS;
                float3 dirWS    = normalize(TransformObjectToWorldDir(dirOS));

                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, dirWS);
                // The object's own Y scale, straight off its matrix: back-row pieces are authored at
                // 0.73 of the front's height, so this is the row without anyone passing it in.
                float yScale = length(float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y));
                float gate = smoothstep(_OutlineScaleMin, _OutlineScaleMax, yScale);

                float2 offset = normalize(normalCS.xy + 1e-6) * _OutlineWidth * gate * o.positionCS.w;
                o.positionCS.xy += offset;
                return o;
            }

            half4 fragOutline(OutlineVaryings input) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
