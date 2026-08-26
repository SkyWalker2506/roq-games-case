// Per-tile bevelled board surface.
//
// MEASURED, not assumed. Positive control first: a horizontal transect of the reference
// (_refs/Developer Case Referans/Block Hole.mp4 frame 300, y=900, sRGB luma) across one
// cell boundary reads
//     76 76 76 | 74 69 67 64 61 53 42 40 | 49 62 77 89 88 | 66 64 64 65 65 | 63 63 63 ...
//     face A     shaded trailing edge      GROUT floor      LIT chamfer lip   face B
// Cell pitch 108 px. Grout floor 40 = 0.53x the light face (76). The lit lip OVERSHOOTS the
// face by +26 code values (89 vs 63), and it sits only on the LOW-x edge; the trailing
// high-x edge falls to 52 (-17%) before the groove. Vertically the same shape appears with a
// weaker lip (74 vs face 63, +11) on the tile's TOP edge and a -24% fall on its bottom edge.
// The reference face itself is FLAT between the chamfers - all the modelling is in ~10% of
// the cell at each edge, not in a broad ramp.
//
// The same transect on our own frame before this change read
//     58 58 57 | 52 46 42 34 | 38 41 43 43 41 | 41 40 39 38 38 ... 45
// i.e. a 2 px hairline groove and NO lip at all: the post-groove peak was 43 against a face
// of 44, an overshoot of MINUS one code value. That is the whole reason the board read as a
// painted checkerboard rather than as separate slabs. The tiles were never flat geometry -
// each Tile_i_j is its own unit cube at scale (1, 0.064, 1) - so the fix is entirely in the
// material, and the previous defaults (_BevelLift 0.03, _SeamDarkness 0.70) were simply an
// order of magnitude too timid.
Shader "Case2/BoardTile"
{
    Properties
    {
        [MainColor] _BaseColor("Base Tile Color", Color) = (0.280, 0.293, 0.408, 1)
        [MainTexture] _SheenMap("Sheen & Bevel Map (unused)", 2D) = "white" {}
        _SheenStrength("Sheen Strength (unused)", Range(0, 1)) = 0.12
        _BevelContrast("Bevel Contrast (legacy, unused)", Range(0, 1)) = 0.20
        _VerticalGrad("Board Vertical Gradient", Range(0, 0.5)) = 0.05

        // Tiles are 3 units deep so a rising one has a wall to show; the border is 0.08 units tall,
        // so a RESTING tile would hang under the board. Nothing below this world height is drawn.
        // HoleGlowHighlight lifts it per-renderer while a tile is in flight, because that part of
        // the climb happens below the floor and is exactly what should be seen through the opening.
        _ClipMinY("Clip Below World Y", Float) = -0.02

        // Grout groove. _SeamWidth is the FLAT floor half-width; _SeamSoft is the ramp out of
        // it. Reference groove: ~4.2% of the cell at half depth, floor at 0.53x the face.
        _SeamWidth("Grout Floor Half-Width (cell fraction)", Range(0.002, 0.08)) = 0.022
        _SeamSoft("Grout Edge Softness (cell fraction)", Range(0.002, 0.08)) = 0.020
        _SeamDarkness("Grout Darkness (x face)", Range(0.2, 1.0)) = 0.55
        _Round("Tile Corner Radius (cell fraction)", Range(0.0, 0.35)) = 0.11

        // Lit chamfer, on the screen-left and screen-top edges of every tile.
        _BevelRise("Chamfer Rise Width", Range(0.004, 0.08)) = 0.018
        _BevelWidth("Chamfer Total Width", Range(0.02, 0.30)) = 0.085
        _BevelLift("Chamfer Highlight (left edge)", Range(0, 1.2)) = 0.42
        _BevelLiftZ("Chamfer Highlight (top edge)", Range(0, 1.2)) = 0.16

        // Shaded chamfer, on the screen-right and screen-bottom edges.
        _ShadeWidth("Shaded Edge Width", Range(0.02, 0.30)) = 0.085
        _ShadeAmt("Shaded Edge Depth (right)", Range(0, 0.6)) = 0.16
        _ShadeAmtZ("Shaded Edge Depth (bottom)", Range(0, 0.6)) = 0.22

        // The reference face is flat; these stay near zero on purpose.
        _GradX("Face Gradient toward +x", Range(0, 0.5)) = 0.03
        _GradZ("Face Gradient toward -z", Range(0, 0.5)) = 0.03
        _FaceLevel("Tile Face Level", Range(0.6, 1.2)) = 0.96

        // The ONLY light-driven term on this surface. The tile colour stays authored; the
        // main light contributes nothing but its shadow attenuation.
        _ShadowStrength("Cast Shadow Strength", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SheenMap);
            SAMPLER(sampler_SheenMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SheenMap_ST;
            float _SheenStrength;
            float _BevelContrast;
            float _VerticalGrad;
            float _SeamWidth;
            float _SeamSoft;
            float _SeamDarkness;
            float _Round;
            float _BevelRise;
            float _BevelWidth;
            float _BevelLift;
            float _BevelLiftZ;
            float _ShadeWidth;
            float _ShadeAmt;
            float _ShadeAmtZ;
            float _GradX;
            float _GradZ;
            float _FaceLevel;
            float _ShadowStrength;
                float  _ClipMinY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(input.positionWS.y - _ClipMinY);

                // Per-tile UV in OBJECT space. Every Tile_i_j is its own unit cube sitting on a
                // cell centre, so positionOS.xz + 0.5 is an exact [0..1] cell. This must not go
                // back to frac(positionWS.xz): the Board root carries a 0.5 z offset, which put
                // every horizontal grout line through the MIDDLE of a tile.
                //
                // Screen mapping, established by transecting our own frame rather than guessed:
                // higher cellUV.x is screen-RIGHT, higher cellUV.y is screen-UP.
                float2 cellUV = saturate(input.positionOS.xz + 0.5);

                // Rounded-square tile outline. The reference tiles are rounded slabs, so the
                // grout opens up at the corners instead of meeting in a square cross.
                float2 q = abs(cellUV - 0.5) - (0.5 - _Round);
                float sdf = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Round;
                float edgeDist = -sdf;                       // >0 inside the tile

                // 1. Grout groove with a FLAT floor, not a hairline crossing zero.
                float seam = smoothstep(_SeamWidth, _SeamWidth + _SeamSoft, edgeDist);

                // 2. Lit chamfer. Distance measured from the outer lip of the groove so the
                //    highlight sits just inside it, exactly as the reference transect shows.
                float tX = cellUV.x - _SeamWidth;
                float tY = (1.0 - cellUV.y) - _SeamWidth;
                float lipX = smoothstep(0.0, _BevelRise, tX) * (1.0 - smoothstep(_BevelRise, _BevelWidth, tX));
                float lipY = smoothstep(0.0, _BevelRise, tY) * (1.0 - smoothstep(_BevelRise, _BevelWidth, tY));
                float lit = lipX * _BevelLift + lipY * _BevelLiftZ;

                // 3. Shaded chamfer on the opposite two edges.
                float uX = (1.0 - cellUV.x) - _SeamWidth;
                float uY = cellUV.y - _SeamWidth;
                float shade = (1.0 - smoothstep(0.0, _ShadeWidth, uX)) * _ShadeAmt
                            + (1.0 - smoothstep(0.0, _ShadeWidth, uY)) * _ShadeAmtZ;

                // 4. Face gradient - deliberately near zero, the reference face is flat.
                float grad = (cellUV.x - 0.5) * _GradX - (cellUV.y - 0.5) * _GradZ;

                half3 baseCol = _BaseColor.rgb;
                half3 tileCol = baseCol * max(0.0, _FaceLevel + grad + lit - shade);
                half3 groutCol = baseCol * _SeamDarkness;
                half3 finalCol = lerp(groutCol, tileCol, seam);

                // Board-wide vertical ambient gradient.
                float boardV = saturate((input.positionWS.z - 0.5) / 7.5);
                finalCol *= 1.0 + (boardV - 0.5) * _VerticalGrad;

                // Cast shadow only - no diffuse, no specular, no ambient from the light.
                // Moving the light changes the shadow and nothing else.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                finalCol *= 1.0 - (1.0 - mainLight.shadowAttenuation) * _ShadowStrength;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
