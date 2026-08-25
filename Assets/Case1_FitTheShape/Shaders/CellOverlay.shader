Shader "Case1/CellOverlay"
{
    // Flat colour that always draws in front of the drum. The unknown marks sit on a cell face that is
    // part of a cylinder, so no amount of offsetting reliably clears the cells stacked in front of it -
    // they were correctly built, sized and positioned and still hidden. Depth testing is the wrong tool
    // for a mark that is conceptually printed ON the face.
    Properties { _BaseColor("Base Colour", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return half4(_BaseColor.rgb, _BaseColor.a); }
            ENDHLSL
        }
    }
    Fallback Off
}
