Shader "Case1/MysteryCover"
{
    Properties
    {
        _BaseColor("Cover Colour", Color) = (0.72,0.05,0.83,1)
        _PatternColor("Question Mark Colour", Color) = (1,0.72,1,1)
        _PatternTex("Question Mark Pattern", 2D) = "black" {}
        _PatternThreshold("Pattern Threshold", Range(0,1)) = 0.36
        _Smoothness("Highlight Tightness", Range(0,1)) = 0.40

        // MEASURED off CASE1_TEPSI.png (drum crop x245-905 y195-715, saturated pixels): no colour
        // exceeds 8.9% there — pink 8.9, green 7.3, magenta 6.7, teal 5.5, orange 4.1. Our covers are
        // ~48% of the drum's saturated pixels, so ONE flat cover colour always dominates (magenta 18%,
        // then teal 23.4%, then pink 20.6% — every single-colour attempt overshot). Instead each cover
        // instance picks one of four palette colours from a stable per-object hash, weighted so every
        // family lands inside its target band simultaneously. _PaletteMix 0 restores the old flat look.
        _PaletteMix("Palette Mix", Range(0,1)) = 1
        _CoverTeal("Cover Teal", Color) = (0.004,0.451,0.549,1)
        _CoverMagenta("Cover Magenta", Color) = (0.725,0.051,0.831,1)
        _CoverPink("Cover Pink", Color) = (0.969,0.435,0.651,1)
        _CoverPurple("Cover Purple", Color) = (0.427,0.027,0.918,1)
        _WeightTeal("Weight Teal", Range(0,1)) = 0.36
        _WeightMagenta("Weight Magenta", Range(0,1)) = 0.12
        _WeightPink("Weight Pink", Range(0,1)) = 0.13

        // Same curvature fall-off as SoftPlastic: target drum rows read 128 -> 75 (ratio 1.70).
        _CurveDarken("Drum Curvature Darken", Range(0,1)) = 0.55

        // A shine bar that travels across the cover, as the reference's "?" boxes have. Driven by
        // _Time so it needs no script and no per-cell animation: every cover using this material
        // sweeps on its own, and _ShineOffset staggers them so the drum does not flash as one block.
        _ShineStrength("Shine Strength", Range(0,2)) = 0.55
        _ShineWidth("Shine Width", Range(0.02,0.6)) = 0.16
        _ShineSpeed("Shine Sweep Speed", Range(0,3)) = 0.55
        _ShineOffset("Shine Phase Offset", Range(0,1)) = 0
        _ShineTilt("Shine Tilt", Range(-2,2)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+2" }
        Pass
        {
            Name "MysteryCover"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _PatternColor;
            float4 _PatternTex_ST;
            float _PatternThreshold;
            float _Smoothness;
            float _PaletteMix;
            float4 _CoverTeal;
            float4 _CoverMagenta;
            float4 _CoverPink;
            float4 _CoverPurple;
            float _WeightTeal;
            float _WeightMagenta;
            float _WeightPink;
            float _CurveDarken;
            float _ShineStrength;
            float _ShineWidth;
            float _ShineSpeed;
            float _ShineOffset;
            float _ShineTilt;
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
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float hash : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _PatternTex);
                // Per-object hash from the instance's pivot, quantised to half a cell so numerical
                // jitter cannot flip a cover's colour between frames. Every cover renderer sits at a
                // distinct pivot, so the drum reads as many differently coloured cells, as the
                // reference does, without touching the authored scene.
                // MEASURED on the 23:28 capture: the earlier frac(sin(dot)) hash clustered badly on
                // the drum's regular lattice (teal weight 0.32 drew only 3 covers, purple weight 0.04
                // drew 4). Integer avalanche hash (PCG-style) decorrelates lattice-aligned pivots.
                float3 pivot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                // Quantised at 0.5 world units (roughly half a cell): the f88 capture caught covers
                // snapping colour in a single idle frame because the finer 0.25 grid put resting
                // pivots on quantisation boundaries that the drum's settle wobble crossed.
                int3 cellId = (int3)floor(pivot * 2.0f + 0.5f);
                uint hx = (uint)(cellId.x + 512) * 73856093u
                        ^ (uint)(cellId.y + 512) * 19349663u
                        ^ (uint)(cellId.z + 512) * 83492791u;
                hx = hx * 747796405u + 2891336453u;
                hx ^= hx >> 16; hx *= 0x7feb352du;
                hx ^= hx >> 15; hx *= 0x846ca68bu;
                hx ^= hx >> 16;
                output.hash = (float)(hx & 0x00ffffffu) / 16777215.0f;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, input.uv).r;
                mask = smoothstep(_PatternThreshold, min(1.0h, _PatternThreshold + 0.20h), mask);

                // Weighted palette pick per cover instance (see the property block for the measured
                // rationale). Pattern glyphs are the base lifted towards white, matching the target
                // cover's base 179/10/207 vs glyph 227/89/209 relationship.
                half h = input.hash;
                half3 pal = _CoverPurple.rgb;
                if (h < _WeightTeal) pal = _CoverTeal.rgb;
                else if (h < _WeightTeal + _WeightMagenta) pal = _CoverMagenta.rgb;
                else if (h < _WeightTeal + _WeightMagenta + _WeightPink) pal = _CoverPink.rgb;
                half3 coverRGB = lerp(_BaseColor.rgb, pal, _PaletteMix);
                half3 glyphRGB = lerp(_PatternColor.rgb, lerp(pal, half3(1,1,1), 0.32h), _PaletteMix);
                half3 base = lerp(coverRGB, glyphRGB, mask);

                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light key = GetMainLight();
                half diffuse = lerp(0.78h, 1.0h,
                                    smoothstep(-0.28h, 0.82h, dot(normalWS, key.direction)));
                half facing = saturate(dot(normalWS, viewDir));
                half bevel = lerp(0.78h, 1.0h, smoothstep(0.10h, 0.82h, facing));
                half3 halfVector = SafeNormalize(key.direction + viewDir);
                half highlight = pow(saturate(dot(normalWS, halfVector)), lerp(10.0h, 72.0h, _Smoothness)) * 0.10h;

                // Travelling shine. A diagonal band in the cover's own UV, swept by time and wrapped,
                // so it enters one edge and leaves the other rather than fading in place. frac() on the
                // phase keeps it periodic; the band is shaped with smoothstep so its edges are soft.
                // Staggered per cover by the same per-instance hash that picks its colour: one
                // shared material would otherwise sweep every cover in unison and the drum would
                // flash as one block (the reference's covers shine independently).
                half travel = frac(_Time.y * _ShineSpeed + _ShineOffset + input.hash);
                half band = input.uv.x + input.uv.y * _ShineTilt;
                half d = abs(frac(band - travel + 0.5h) - 0.5h);
                half shine = 1.0h - smoothstep(0.0h, max(0.001h, _ShineWidth), d);
                shine *= shine * _ShineStrength;

                // Drum curvature fall-off keyed on world height, not view-facing: the tele camera
                // sees all six rows at facing 0.78-1.00 (symmetric), so facing cannot separate the
                // far rows. Row centres sit at worldY 5.28 (top) .. 2.86 (bottom, the band row);
                // ease off gently above 4.6, fall away hard below 3.9 (target rows 128 -> 75).
                half curve = saturate(1.0h
                    - _CurveDarken * 0.40h * smoothstep(4.6h, 5.4h, input.positionWS.y)
                    - _CurveDarken * 0.60h * (1.0h - smoothstep(2.6h, 3.9h, input.positionWS.y)));
                return half4((base * diffuse * bevel + highlight + shine) * curve, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
