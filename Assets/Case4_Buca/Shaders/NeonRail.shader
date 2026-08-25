Shader "Case4/NeonRail"
{
    Properties
    {
        [MainColor] _BaseColor("Base", Color) = (0.97, 0.98, 1, 1)
        [MainTexture] _GlowMap("Rail Glow Profile", 2D) = "white" {}
        _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.65
        _GlowIntensity("Glow Intensity", Range(0, 2)) = 1.25
        _DividerCenterX("Divider Centre X (world)", Float) = -31.090
        _DividerHalfWidth("Divider Half Width (world)", Float) = 1.0
        _DividerMaxZ("Divider Max Z (world)", Float) = -8.0
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

            TEXTURE2D(_GlowMap);
            SAMPLER(sampler_GlowMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _GlowMap_ST;
            float4 _EmissionColor;
            float _Smoothness;
            float _GlowIntensity;
            float _DividerCenterX;
            float _DividerHalfWidth;
            float _DividerMaxZ;
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
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv * _GlowMap_ST.xy + _GlowMap_ST.zw;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                
                // Measured across the rail in docs/verify/case4/ref/ref_0.00s.png (rows y=520, 700, 900):
                // the reference steps straight from floor (85,99,116) to a solid ~40 px white band
                // (250,253,254) and straight back, with no mid-grey wall anywhere in the cross-section.
                // The old wallBase of 0.48 linear encodes to ~186 sRGB, which is itself above the
                // "light rail" threshold, so the rail's side walls were counted as lit rail as well and
                // roughly doubled the lit area: 185864 px against the reference's 90926.
                // Two corrections: the walls drop to the reference's own floor value, and every rail top
                // takes the director's colour instead of only the arch, because rows y=700 and y=900 sit
                // on the straight side rails and read 250-253 there too, not "dark metal".
                // CROWN MASK - world Y, metres, read off the live level_frame mesh, not assumed.
                // The arena frame is one mesh whose rail profile is a thin wall (y 0.006 to 0.832,
                // 0.664 thick on the rails, 0.941 on the divider) with a 12-sided piping tube of
                // radius 0.1 centred at y=0.821 running along each of its two top edges, so the tube
                // crest sits at y=0.921.
                // The mask used to be smoothstep(0.85, 0.99, N.y), and on THIS mesh a normal test
                // cannot hold the crown together. The wall's top quad carries smoothed corner
                // normals of (+-0.707, 0.707, 0) and the tube is a full cylinder whose normals sweep
                // the entire circle, so only the middle of the flat top and the single crest row of
                // each tube ever cleared 0.85. Everything between them fell to wallBase and the one
                // tube read as two thin outlines with a dark channel between: measured on
                // .plan-build/verify/Buca_T1_before/frame_00.png, column x=540 across the arch top
                // gave a 3 px run, an 8 px gap and a 12 px run, against the reference's single 17 px
                // run at the same column (_refs/Developer Case Referans/Buca.mp4, t=0.00 s).
                // Height separates the crown from the wall cleanly and does not care how the FBX was
                // smoothed. The crown is continuous in y: the tube's inner flank descends from its
                // crest and crosses y = 0.832 at x = centre +- 0.0994, i.e. within 0.6 mm of the
                // point where the flat top takes over, so a threshold at the flat top's own height
                // cannot open a gap between them - which a normal test could not avoid.
                // The band is 0.012 m deep, ending exactly on the flat top at y = 0.832. A deeper
                // band bleeds down the side walls: at 0.790 the run at column x=540 measured 30 px
                // against the reference's 17 px, 7 px of which was whitened inner wall below the
                // crown, and the divider measured 87 px against the reference's 44 px.
                float top = smoothstep(0.820, 0.832, input.positionWS.y);
                float4 glowTex = SAMPLE_TEXTURE2D(_GlowMap, sampler_GlowMap, input.uv);
                float glowVal = glowTex.r;

                // At t=2.10 s the reference's arch and both straight side rails have gone saturated cyan
                // (38,250,252) but the centre divider is still white, so the divider has to sit out the
                // colour swap. Its world box is unambiguous: the Divider collider is at x=-31.090,
                // z=-13.749 with half-extents 0.586 and 3.887, and every ancestor transform is identity
                // at the origin. The arch sits at z=+0.866, far outside the z cut, so it is unaffected.
                float dividerMask = step(abs(input.positionWS.x - _DividerCenterX), _DividerHalfWidth)
                                  * step(input.positionWS.z, _DividerMaxZ);

                float3 wallBase = float3(0.105, 0.125, 0.165);
                float3 railTop = lerp(_BaseColor.rgb, float3(0.97, 0.98, 1.0), dividerMask);
                float3 col = lerp(wallBase, railTop, top);

                float rimGlow = pow(1.0 - saturate(abs(N.y)), 3.0) * 0.12;
                col += _EmissionColor.rgb * (1.0 - dividerMask)
                     * (top * 0.90 + rimGlow + glowVal * _GlowIntensity * 0.45);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
