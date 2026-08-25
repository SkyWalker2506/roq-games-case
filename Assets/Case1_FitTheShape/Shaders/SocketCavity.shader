Shader "Case1/SocketCavity"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 0.5, 0, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.55
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.35
        _CavityFloorBrightness("Cavity Floor Brightness", Range(0, 1)) = 0.75
        _CavityShadowStrength("Cavity Shadow Strength", Range(0, 1)) = 0.38
        _RimDarken("Rim Crevice Shadow", Range(0, 1)) = 0.28
        _LightDir("Key Light Direction", Vector) = (-0.35, 0.80, -0.48, 0)
        // Drum curvature fall-off, same mapping as SoftPlastic/MysteryCover: keyed on world height
        // (drum row centres sit at worldY 5.28 top .. 2.86 bottom). Default 0 so only the drum's
        // socket materials opt in; sockets must darken with the row faces around them.
        _CurveDarken("Drum Curvature Darken", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry+10"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _SpecularStrength;
                half _CavityFloorBrightness;
                half _CavityShadowStrength;
                half _RimDarken;
                half _CurveDarken;
                half4 _LightDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                half3  normalWS     : TEXCOORD1;
                half3  positionOS   : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs   = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = NormalizeNormalPerVertex(normInputs.normalWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = SafeNormalize(input.normalWS);
                half3 viewDir  = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                half3 keyDir   = SafeNormalize(_LightDir.xyz);

                // Diffuse wrap lighting inside the 3D socket cavity
                half NdotL = dot(normalWS, keyDir);
                half wrapLight = saturate(NdotL * 0.45h + 0.55h);

                // Top overhang shadow: faces pointing downwards inside the cavity receive soft realistic ceiling shadow
                half topOcclusion = saturate(-normalWS.y * 0.60h + 0.30h);
                half shadowFactor = lerp(1.0h, 1.0h - _CavityShadowStrength, topOcclusion);

                // Upward facing inner floor catches bright ambient bounce light, making the hollow pocket unmistakable
                half floorBoost = saturate(normalWS.y * 0.55h + 0.45h);
                half depthShading = lerp(_CavityFloorBrightness * 0.68h, _CavityFloorBrightness, floorBoost);

                // Combine into rich, vibrant cavity base (matching the block tint with 3D interior depth)
                half3 cavityRGB = _BaseColor.rgb * (depthShading * wrapLight * shadowFactor);

                // Soft plastic specular highlight on inner beveled walls
                half3 halfVector = SafeNormalize(keyDir + viewDir);
                half NdotH = saturate(dot(normalWS, halfVector));
                half spec = pow(NdotH, lerp(18.0h, 72.0h, _Smoothness)) * _SpecularStrength;
                cavityRGB += half3(1, 1, 1) * (spec * 0.40h);

                // Soft dark crevice ring around the socket perimeter (molded plastic cavity entrance)
                half VdotN = saturate(dot(normalWS, viewDir));
                half rimCrevice = pow(1.0h - VdotN, 2.2h) * _RimDarken;
                cavityRGB *= (1.0h - rimCrevice * 0.45h);

                cavityRGB *= saturate(1.0h
                    - _CurveDarken * 0.40h * smoothstep(4.6h, 5.4h, input.positionWS.y)
                    - _CurveDarken * 0.60h * (1.0h - smoothstep(2.6h, 3.9h, input.positionWS.y)));

                return half4(saturate(cavityRGB), _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
