Shader "Case4/Floor"
{
    Properties
    {
        _LowColor("Low Color", Color) = (0.392, 0.451, 0.506, 1)   // 100, 115, 129
        _HighColor("High Color", Color) = (0.314, 0.365, 0.439, 1) // 80, 93, 112
        _ShadowColor("Shadow Color", Color) = (0.051, 0.086, 0.180, 1) // 13, 22, 46
        _Smoothness("Smoothness", Range(0, 1)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColor;
                float4 _HighColor;
                float4 _ShadowColor;
                float _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float atten = l.shadowAttenuation;

                // Vertical gradient from low (bottom of viewport) to high (top)
                float vGrad = saturate((i.positionWS.z + 5.0) / 16.0);
                float3 baseFloor = lerp(_LowColor.rgb, _HighColor.rgb, vGrad);

                // Receive cast shadows: lerp to _ShadowColor
                float3 col = lerp(_ShadowColor.rgb, baseFloor, atten);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    Fallback Off
}
