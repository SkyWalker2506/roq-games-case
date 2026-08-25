Shader "Case4/LaunchTargetPad"
{
    Properties
    {
        _RimColor("Warm Rim Color", Color) = (0.92, 0.72, 0.30, 1)
        _BaseColor("Near-Black Base Color", Color) = (0.08, 0.08, 0.11, 1)
        _CenterColor("Emissive Yellow Center", Color) = (1.0, 0.88, 0.20, 1)
        _AimRingColor("White Aim Ring", Color) = (1.0, 1.0, 1.0, 1)
        _EmissionIntensity("Emission Intensity", Range(0, 3)) = 1.4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float4 _BaseColor;
                float4 _CenterColor;
                float4 _AimRingColor;
                float _EmissionIntensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionOS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Local planar radial coordinate: r in [0, 1]
                float2 p = input.positionOS.xz;
                float r = length(p) * 2.0;

                // Layer 1: Emissive Yellow Center (r < 0.40)
                float centerMask = smoothstep(0.42, 0.38, r);
                // Layer 2: Near-Black Base (r in [0.40, 0.82])
                float baseMask = smoothstep(0.38, 0.42, r) * smoothstep(0.85, 0.80, r);
                // Layer 3: Warm Golden Outer Rim (r in [0.82, 1.0])
                float rimMask = smoothstep(0.80, 0.85, r);
                // Layer 4: Concentric White Aim Ring (at r ~ 0.62)
                float aimRing = exp(-pow((r - 0.62) / 0.035, 2.0));

                float3 col = _BaseColor.rgb * baseMask;
                col += _CenterColor.rgb * centerMask * _EmissionIntensity;
                col += _RimColor.rgb * rimMask;
                col += _AimRingColor.rgb * aimRing * 0.90;

                clip(1.05 - r);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
