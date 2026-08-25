// Per-tile bevelled board surface.
//
// The reference board is not a flat panel with lines drawn on it: each floor tile is an
// individually bevelled slab. Measured across an empty tile in ref_0.00s.png, a cell reads
// dark seam -> bright bevel band -> slight dip -> gentle rise across the face
// (x3.00:64.9  x3.09:76.9  x3.17:62.2  x3.25..x3.92: 63.3 -> 75.0), and the same structure
// appears vertically (j3.17:58.0 seam, j3.25:85.0 bevel). Our old output was flat fill plus a
// hairline seam: the same transect read 60.3, 65.4, 70.8, 70.8 ... 69.6, a within-cell spread
// under 1.5 code values.
Shader "Case2/BoardTile"
{
    Properties
    {
        [MainColor] _BaseColor("Base Tile Color", Color) = (0.175, 0.225, 0.420, 1)
        [MainTexture] _SheenMap("Sheen & Bevel Map (unused)", 2D) = "white" {}
        _SheenStrength("Sheen Strength (unused)", Range(0, 1)) = 0.25
        _BevelContrast("Bevel Contrast (legacy)", Range(0, 1)) = 0.22
        _VerticalGrad("Vertical Gradient Strength", Range(0, 0.5)) = 0.08

        _SeamWidth("Grout Seam Width (cell fraction)", Range(0.005, 0.12)) = 0.030
        _BevelWidth("Bevel Band Width (cell fraction)", Range(0.02, 0.30)) = 0.070
        _BevelLift("Bevel Highlight Lift", Range(0, 0.8)) = 0.03
        _BevelShade("Bevel Inner Trough", Range(0, 0.5)) = 0.10
        _TopShade("High-z Edge Shade", Range(0, 0.5)) = 0.05
        _GradX("Face Gradient toward +x", Range(0, 0.5)) = 0.12
        _GradZ("Face Gradient toward -z", Range(0, 0.5)) = 0.10
        _SeamDarkness("Grout Darkness", Range(0.2, 1.0)) = 0.70
        _FaceLevel("Tile Face Level", Range(0.6, 1.2)) = 0.96
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SheenMap);
            SAMPLER(sampler_SheenMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _SheenMap_ST;
            float _SheenStrength;
            float _BevelContrast;
            float _VerticalGrad;
            float _SeamWidth;
            float _BevelWidth;
            float _BevelLift;
            float _BevelShade;
            float _TopShade;
            float _GradX;
            float _GradZ;
            float _SeamDarkness;
            float _FaceLevel;
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
                // Per-tile UV in OBJECT space. Every Tile_i_j is its own unit cube sitting on a
                // cell centre, so positionOS.xz + 0.5 is an exact [0..1] cell.
                //
                // This used to be frac(positionWS.xz), which silently ignored the Board root's
                // z offset of 0.5 and so placed every horizontal grout line through the MIDDLE
                // of a tile - measured on frame_00.png the seam landed at screen row j2.51
                // instead of the j2.0 cell boundary. Object space cannot drift that way.
                float2 cellUV = saturate(input.positionOS.xz + 0.5);
                float2 dEdge = min(cellUV, 1.0 - cellUV);
                float edgeDist = min(dEdge.x, dEdge.y);

                // 1. Grout groove, sitting exactly on the cell boundary.
                float seam = smoothstep(0.0, _SeamWidth, edgeDist);

                // 2. Bevel, with the directions taken from the reference rather than assumed.
                //    Across one cell horizontally it reads: seam 64.9 -> bright band 76.9 just
                //    inside the LOW-x edge -> trough 62.2 -> a steady rise to 75.0 at the high-x
                //    edge. Vertically it reads darkest right below the top seam (66.5) and
                //    brightest at the bottom of the cell (74.5).
                //
                //    CORRECTION, on a finer transect: the "bright band" was an artefact of coarse
                //    sampling straddling the previous cell. Stepping a single cell at 0.06 the
                //    reference reads 64.8 seam, 45.6 dark grout, then a MONOTONIC rise 59.7 -> 75.0
                //    to the high-x edge. There is no bright band inside the low-x edge at all, so
                //    _BevelLift is now near zero and the face gradient carries the shape.
                //    On a tile-only patch ours already measures std 22.9 against the reference's
                //    15.6, i.e. over-contrasted, not under - see the report for why the crop
                //    disagrees.
                //    So there is a sharp highlight on the low-x edge ONLY; the high-z edge is the
                //    darkest part of the tile, not a second highlight. The previous version put a
                //    bright band on both the low-x and the high-z edge and sloped the face the
                //    wrong way on both axes, which is why the tiles barely moved: the seam fix
                //    landed but the shading fought the reference instead of matching it.
                //    (A raised slab shows its NEAR face to the camera; a cavity shows its FAR
                //    wall. Opposite signs, same key light - both are now consistent.)
                float bandLo = _SeamWidth;
                float bandHi = _SeamWidth + _BevelWidth;
                float litEdge = 1.0 - smoothstep(bandLo, bandHi, cellUV.x);
                float trough = (1.0 - smoothstep(bandHi, bandHi + _BevelWidth * 1.6, cellUV.x))
                             * (1.0 - litEdge);
                float topShade = 1.0 - smoothstep(bandLo, bandHi * 2.2, 1.0 - cellUV.y);

                // 3. Face gradients: brighter toward high x and toward low z.
                float grad = (cellUV.x - 0.5) * _GradX - (cellUV.y - 0.5) * _GradZ;

                half3 baseCol = _BaseColor.rgb;
                half3 tileCol = baseCol * (_FaceLevel + grad
                                           + litEdge * _BevelLift
                                           - trough * _BevelShade
                                           - topShade * _TopShade);
                half3 groutCol = baseCol * _SeamDarkness;
                half3 finalCol = lerp(groutCol, tileCol, seam);

                // Board-wide vertical ambient gradient.
                float boardV = saturate((input.positionWS.z - 0.5) / 7.5);
                finalCol *= 1.0 + (boardV - 0.5) * _VerticalGrad;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
