Shader "Case1/ToonCell"
{
    // MEASURED off Fit The Shape.mp4. The reference's cells are not lit surfaces: each face is a single
    // flat colour with no falloff across it, the cube reads as one object rather than three differently
    // lit faces, and there is no specular anywhere. What separates a face from the one beside it is a
    // single hard step, not a gradient. URP/Lit could not produce that at any settings.
    Properties
    {
        _BaseColor("Base Colour", Color) = (1,1,1,1)
        _ShadeStep("Shade Step", Range(-1,1)) = 0.10
        _ShadeMul("Shaded Side Multiplier", Range(0.4,1)) = 0.80
        _TopLift("Top Face Lift", Range(1,1.35)) = 1.08
        _EdgeDarken("Lower Edge Darken", Range(0,0.6)) = 0.18
        // Polygon offset. The glyph is a recess whose floor z-fights with the cell face behind it and
        // renders as a thin outline. Pushing the recess GEOMETRY forward filled it but also moved the
        // landing point the selection gate measures, so the bias belongs in the raster, not the scene.
        [HideInInspector]_OffsetFactor("Offset Factor", Float) = 0
        [HideInInspector]_OffsetUnits("Offset Units", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ToonCell"
            Tags { "LightMode"="UniversalForward" }
            Offset [_OffsetFactor], [_OffsetUnits]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _ShadeStep; float _ShadeMul; float _TopLift; float _EdgeDarken;
            CBUFFER_END

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                Light main = GetMainLight();
                float ndl = dot(N, main.direction);

                // One hard step. No smoothstep: the reference has a crisp boundary, and any softness
                // here is exactly the gradient that made our cells look like a different game.
                float lit = ndl > _ShadeStep ? 1.0 : _ShadeMul;

                // The cube's top sliver reads a touch brighter in the reference; the bottom edge a touch
                // darker. Both are flat steps off the normal's y, not a falloff.
                float up = N.y > 0.55 ? _TopLift : 1.0;
                float down = N.y < -0.35 ? (1.0 - _EdgeDarken) : 1.0;

                float3 col = _BaseColor.rgb * lit * up * down;
                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
