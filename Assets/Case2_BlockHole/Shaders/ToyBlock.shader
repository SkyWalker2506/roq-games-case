// Painted-wood grain on the block faces.
//
// MEASURED on ref_0.00s.png (= Block Hole.mp4 frame 0) against
// .plan-build/verify/BlockHole/frame_00.png, both 1080x1728, same board scale.
// Metric: sigma of the 4-neighbour Laplacian of the block's DOMINANT sRGB channel inside
// its hue mask eroded by 12 px (the erosion drops the bevel, the white held-block outline
// and the grab dot without having to name them).
//
//                 reference   ours (before)
//   red block        6.76        2.82
//   green block      2.90        2.78
//   cyan block       4.45        2.78
//   purple block     5.49        2.51
//   board tile       1.95        3.12   <- our own floor, for scale
//
// Our four blocks sat at 2.5-2.8, i.e. BELOW our own board tiles: the blocks carried no
// grain above the board's own texture noise. The cause was not a missing feature - the
// grain code below was already here - it was the serialised values: every
// Case2_BlockSurface_*.mat shipped _GrainStrength 0.055, which is nothing.
// (The .mat files also carry a _GrainScale, which this shader has never declared.)
//
// Strength is PER MATERIAL, not one shared number: the reference's own spread is 2.90 to 6.76,
// a factor of 2.3, so no single strength can land all four. The four materials stay separate
// assets - colour identity is the mechanic here, a block is matched to its hole by colour.
//
// WHAT IS MATCHED, and why not sigma directly. Our render's own noise floor is 3.11 against the
// reference's 1.95, so an absolute sigma match would demand a green block QUIETER than our own
// noise - i.e. no grain at all on green. What is matched instead is the grain's own contrast
// above each image's own board floor, in quadrature: g = sqrt(sigma_block^2 - sigma_tile^2),
// which for the reference is red 6.47, green 2.14, cyan 4.00, purple 5.13. Each material's
// strength was then solved from a probe capture: sigma^2 = floor^2 + (k*strength)^2, one
// capture at a known strength gives k, and strength = g_ref / k.
//
//   material                 strength   sigma landed   ref    ratio
//   Case2_BlockSurface_L       0.294        7.54       6.76   1.12
//   Case2_BlockSurface_Square  0.097        3.69       2.90   1.27
//   Case2_BlockSurface_Two     0.187        4.89       4.45   1.10
//   Case2_BlockSurface_Cross   0.229        5.56       5.49   1.01
//
// Green sits 27% high and cannot be brought down: 2.90 is below our own 3.11 noise floor, so
// the residual is our render's noise, not grain. The other three land within 12%.
//
// SPACING was measured too, not eyeballed: radially-averaged power spectrum of a 110 and a
// 128 px patch inside the green face. Reference 46.5 / 48.9 px, i.e. 2.52-2.64 streaks per
// 123 px cell; ours now 46.5 / 48.9 px, the same to the bin.
Shader "Case2/ToyBlock"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _PatternMap("Top Pattern Map", 2D) = "white" {}
        _PatternInfluence("Pattern Influence", Range(0, 1)) = 0.55
        _GrainStrength("Wood Grain Strength", Range(0, 1)) = 0.28   // per material, see header
        _GrainFrequency("Grain Frequency", Range(1, 30)) = 2.1    // rings per faceUV unit; faceUV = positionOS * 1.5
        _EdgeLift("Bevel Edge Highlight", Range(0, 0.8)) = 0.22
        _FaceContrast("Volumetric Face Contrast", Range(0, 0.8)) = 0.35
        _Smoothness("Smoothness", Range(0, 1)) = 0.30
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_PatternMap);
            SAMPLER(sampler_PatternMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _PatternMap_ST;
            float _PatternInfluence;
            float _GrainStrength;
            float _GrainFrequency;
            float _EdgeLift;
            float _FaceContrast;
            float _Smoothness;
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
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.uv = input.uv * _PatternMap_ST.xy + _PatternMap_ST.zw;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            // Procedural painted-wood grain in object UV/local space.
            //
            // Two things here are MEASURED off the reference rather than chosen:
            //
            // 1. ORIENTATION. The streaks run ALONG +x (the block's long screen axis), so the
            //    field has to vary with uv.y. It used to vary with uv.x, which drew the streaks
            //    across the block instead of along it - the same amount of grain, turned ninety
            //    degrees, reading as a contour map rather than as cut wood.
            //
            // 2. MEANDER SCALED BY THE PERIOD. The distortion is now a fraction of one ring
            //    period, not a fixed distance in UV. As a fixed 0.22 it was 46% of a period at
            //    _GrainFrequency 2.1 and 9% at 12.0, so lowering the frequency to the reference's
            //    spacing also turned the streaks into knots. Radially-averaged power spectrum of
            //    a 110-128 px patch inside the green face: the reference peaks at 46.5-48.9 px,
            //    i.e. 2.5-2.6 streaks per 123 px cell.
            float WoodGrain(float2 uv)
            {
                float period = 1.0 / max(_GrainFrequency, 0.001);
                float wave = sin(uv.x * 3.1 + sin(uv.y * 1.9) * 1.6) * 0.22 * period;
                wave += sin(uv.x * 8.3 + cos(uv.y * 4.4) * 1.1) * 0.07 * period;
                float coord = (uv.y + wave) * _GrainFrequency;

                // Ring density modulation: one broad groove per period with a thinner companion
                // line between, which is what the reference's faces show.
                float rings = sin(coord * 6.28318);
                float fine = sin(coord * 18.8495) * 0.32;
                float combined = rings + fine;
                // The reference's grooves have HARD edges - that is what a Laplacian sees, and
                // measuring one confirmed it: with our smooth sine grooves matched to the
                // reference's 46 px period the Laplacian sigma stopped responding to
                // _GrainStrength at all (0.45 -> 0.35 moved red 3.40 -> 3.41), because a smooth
                // 46 px wave carries almost no second derivative. Squaring the profile up with a
                // smoothstep is what puts the edge back.
                float g = saturate(combined * 0.5 + 0.5);
                g = smoothstep(0.34, 0.72, g);
                return pow(g, 1.4);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                float3 Vd = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light l = GetMainLight();

                // 1. Organic Painted Wood Grain (Evaluated per object face in local coords)
                float2 faceUV = input.uv;
                if (abs(N.y) > 0.5) faceUV = input.positionOS.xz * 1.5;
                else if (abs(N.x) > 0.5) faceUV = input.positionOS.zy * 1.5;
                else faceUV = input.positionOS.xy * 1.5;

                float grain = WoodGrain(faceUV);
                // Value variation (albedo relief)
                half3 albedo = _BaseColor.rgb * (1.0 - grain * _GrainStrength);

                // Normal perturbation from grain gradient
                float2 e = float2(0.01, 0.0);
                float dGx = (WoodGrain(faceUV + e.xy) - WoodGrain(faceUV - e.xy)) * 0.5;
                float dGy = (WoodGrain(faceUV + e.yx) - WoodGrain(faceUV - e.yx)) * 0.5;
                // Scaled by _GrainStrength so the carved highlight moves WITH the albedo
                // relief. Left as a constant 0.15 it was the only part of the grain that
                // survived at _GrainStrength 0.055, which is why the faces read as a faint
                // smear rather than as cut wood.
                float3 grainNormalPerturb = float3(dGx, 0, dGy) * (_GrainStrength * 3.5);
                float3 perturbedN = normalize(N + grainNormalPerturb);

                // 2. Volumetric Shading & Beveling:
                // Lit top face (+Y), darker side walls, ambient contrast
                float ndl = saturate(dot(perturbedN, normalize(l.direction)));
                float topLighting = saturate(N.y * 0.45 + 0.55); // Top is significantly brighter than sides
                float sideLighting = saturate(1.0 - abs(N.y)) * (0.85 - _FaceContrast);
                float faceVol = topLighting - sideLighting * 0.35;

                // Wide bevel edge highlight
                float bevelEdge = pow(1.0 - saturate(dot(N, Vd)), 2.5) * _EdgeLift;

                // Directional specular gloss on toy surface
                float3 H = normalize(normalize(l.direction) + Vd);
                float spec = pow(saturate(dot(perturbedN, H)), 24.0) * _Smoothness * topLighting;

                half3 finalCol = albedo * (0.75 + ndl * 0.25) * faceVol + half3(1, 1, 1) * (bevelEdge + spec);
                return half4(finalCol, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
