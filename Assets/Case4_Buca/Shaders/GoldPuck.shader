Shader "Case4/GoldPuck"
{
    Properties
    {
        [MainColor] _BaseColor("Gold", Color) = (1,0.84,0.34,1)
        _EmissionColor("Warm Core", Color) = (0.15,0.07,0.005,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.82
        _Rim("Rim", Range(0,1)) = 0.30
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
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float4 _EmissionColor; float _Smoothness; float _Rim;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            V vert(A i){ V o; VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz); VertexNormalInputs n=GetVertexNormalInputs(i.normalOS); o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=n.normalWS; return o; }
            half4 frag(V i):SV_Target
            {
                float3 N=normalize(i.normalWS); float3 Vd=SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                Light l=GetMainLight(); float3 L=normalize(l.direction); float ndl=saturate(dot(N,L));
                float3 H=normalize(L+Vd); float spec=pow(saturate(dot(N,H)), lerp(16.0,96.0,_Smoothness));
                float fres=pow(1.0-saturate(dot(N,Vd)),3.0)*_Rim;
                float3 col=_BaseColor.rgb*(0.56+0.42*ndl)+spec*0.48+fres*_BaseColor.rgb+_EmissionColor.rgb;
                return half4(col,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    Fallback Off
}
