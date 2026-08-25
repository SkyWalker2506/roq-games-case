// Inverted-hull outline for URP. The mesh is drawn a second time, expanded along its normals with
// front faces culled, so the visible result is a thin band that follows the exact silhouette of the
// source mesh (rounded corners included). Used twice in Case 2: white around the held block, and in
// the block colour around a hole that accepts it.
//
// The properties are declared outside UnityPerMaterial on purpose: that keeps the shader out of the
// SRP Batcher, which is what lets a MaterialPropertyBlock drive colour and width per renderer without
// cloning the material.
Shader "Case2/BlockOutline"
{
    Properties
    {
        _OutlineColor ("Outline Colour", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width", Float) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _OutlineColor;
            float _OutlineWidth;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 n = normalize(IN.normalOS);
                float3 posOS = IN.positionOS.xyz + n * _OutlineWidth;
                OUT.positionCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, _OutlineColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
