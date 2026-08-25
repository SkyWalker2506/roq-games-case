Shader "Case4/DarkGeometricProp"
{
    Properties
    {
        [MainColor] _BaseColor("Base Dark Color", Color) = (0.13, 0.14, 0.17, 1)
        _TopHighlight("Top Highlight Color", Color) = (0.22, 0.24, 0.29, 1)
        _ShadowColor("Shadow Face Color", Color) = (0.06, 0.07, 0.09, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.15
        _BevelLift("Bevel Edge Lift", Range(0, 0.5)) = 0.12
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopHighlight;
                float4 _ShadowColor;
                float _Smoothness;
                float _BevelLift;
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
                float3 N = normalize(i.normalWS);
                float3 Vd = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float ndl = saturate(dot(N, normalize(l.direction)));

                float isTop = saturate(N.y);
                float3 faceCol = lerp(_BaseColor.rgb, _TopHighlight.rgb, isTop * 0.75);
                faceCol = lerp(_ShadowColor.rgb, faceCol, saturate(ndl * 0.8 + 0.2));

                float edgeLift = pow(1.0 - saturate(dot(N, Vd)), 3.0) * _BevelLift;
                float3 finalCol = faceCol * l.shadowAttenuation + half3(1, 1, 1) * edgeLift;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    Fallback Off
}
