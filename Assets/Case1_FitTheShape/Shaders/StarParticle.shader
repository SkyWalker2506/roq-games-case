Shader "Case1/StarParticle"
{
    Properties {
        _Color("Tint", Color) = (1.0, 0.92, 0.20, 1.0)
        _Intensity("Intensity", Float) = 5.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+5000" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest Always

            // Premultiplied Alpha Blend: Vibrant solid gold body + intense luminous HDR glow
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                
                half a = saturate(input.color.a * _Color.a);
                // Vivid, saturated glowing gold/amber stars (#FFDE38)
                half3 gold = half3(1.0h, 0.87h, 0.22h);
                half intensity = (_Intensity > 0.01h ? _Intensity : 5.0h);
                half3 rgb = gold * intensity * a;
                output.color = half4(rgb, a);
                return output;
            }
            half4 frag(Varyings input) : SV_Target 
            { 
                return input.color; 
            }
            ENDHLSL
        }
    }
    Fallback Off
}
