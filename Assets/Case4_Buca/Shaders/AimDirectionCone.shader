Shader "Case4/AimDirectionCone"
{
    Properties
    {
        [MainColor] _BaseColor("Cone Tint Color", Color) = (1.0, 0.92, 0.65, 0.38)
        _TipColor("Tip Emissive Highlight", Color) = (1.0, 1.0, 0.90, 0.85)
        _RimStrength("Fresnel Rim Strength", Range(0, 2)) = 1.2
        _FadeStart("Fade Start", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+50" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TipColor;
                float _RimStrength;
                float _FadeStart;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float3 positionOS : TEXCOORD3; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.uv = input.uv;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                float3 Vd = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                // Fresnel soft rim on conical surface
                float fresnel = pow(1.0 - saturate(dot(N, Vd)), 2.2) * _RimStrength;

                // Taper gradient along cone height (OS.y or OS.z from base 0 to apex tip 1)
                float tHeight = saturate((input.positionOS.z + 0.5) / 1.5);
                float alpha = lerp(_BaseColor.a, _TipColor.a, tHeight) + fresnel * 0.25;
                float3 col = lerp(_BaseColor.rgb, _TipColor.rgb, tHeight) + float3(1, 1, 1) * fresnel * 0.4;

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
