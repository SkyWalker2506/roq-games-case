Shader "Case2/BoardFrame"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.390, 0.455, 0.765, 1)
        _HighlightColor("Highlight Color", Color) = (0.75, 0.82, 0.98, 1)
        _ShadowColor("Shadow Color", Color) = (0.18, 0.22, 0.42, 1)
        _BevelWidth("Bevel Width", Range(0, 0.5)) = 0.25
        _CornerRadius("Corner Radius", Range(0.1, 1.5)) = 0.65
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
                float4 _HighlightColor;
                float4 _ShadowColor;
                float _BevelWidth;
                float _CornerRadius;
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
                // Playfield bounds: X in [-0.26, 7.26], Z in [-0.26, 8.26]
                // 2D distance to rounded outer boundary
                float2 center = float2(3.5, 4.0);
                float2 halfSize = float2(3.76, 4.26);
                float2 d = abs(i.positionWS.xz - center) - (halfSize - _CornerRadius);
                float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - _CornerRadius;

                // Soft rounded plastic toy bevel profile
                float bevelProfile = sin(saturate((dist + 0.52) / 0.52) * 3.14159);
                float topHighlight = pow(saturate(bevelProfile), 1.5) * 0.45;
                float bottomShadow = saturate((4.0 - i.positionWS.z) / 4.5) * 0.25;

                float3 col = _BaseColor.rgb;
                col = lerp(col, _HighlightColor.rgb, topHighlight);
                col = lerp(col, _ShadowColor.rgb, bottomShadow);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
