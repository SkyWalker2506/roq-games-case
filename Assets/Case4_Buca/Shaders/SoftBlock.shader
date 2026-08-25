Shader "Case4/SoftBlock"
{
    Properties
    {
        [MainColor] _BaseColor("Base Green Color", Color) = (0.13, 0.65, 0.16, 1)
        _TopColor("Top Face Lit Color", Color) = (0.18, 0.76, 0.22, 1)
        _FrontColor("Front Face Color", Color) = (0.10, 0.55, 0.14, 1)
        _SideColor("Side Face Shadow Color", Color) = (0.07, 0.42, 0.10, 1)
        _CubeCellSize("Cube Cell Size (object space; 1 = one cell per block)", Float) = 1.0
        _SeamWidth("Cube Seam Width", Range(0.01, 0.30)) = 0.045
        _SeamDepth("Cube Seam Darkness", Range(0.1, 0.8)) = 0.16
        _BevelLift("Bevel Edge Highlight", Range(0, 0.5)) = 0.05
        _Smoothness("Gloss Smoothness", Range(0, 1)) = 0.25
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
                float4 _TopColor;
                float4 _FrontColor;
                float4 _SideColor;
                float _CubeCellSize;
                float _SeamWidth;
                float _SeamDepth;
                float _BevelLift;
                float _Smoothness;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 positionOS:TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.positionOS = i.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i):SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 Vd = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS));

                // 1. Distinct Per-Face Shading (Top vs Front vs Side):
                float isTop = saturate(N.y);
                float isFront = saturate(-N.z);
                float isSide = saturate(abs(N.x));

                float3 faceCol = _TopColor.rgb * isTop + _FrontColor.rgb * isFront * (1.0 - isTop) + _SideColor.rgb * isSide * (1.0 - isTop) * (1.0 - isFront);
                if (isTop + isFront + isSide < 0.1) faceCol = _BaseColor.rgb;

                // 2. 3D Per-Cube Subdivision Grid (Horizontal & Vertical Seams across all faces):
                // Object space, not world. The blocks are unit cubes scaled by blockPitch = 0.5275,
                // so a cell size of 1 puts exactly one cell on each block with the seams landing on
                // its real edges. The world-space grid could not do that: its cell was 0.52 against a
                // 0.5275 block, and its phase came from the solved stackX0, so seams cut across faces
                // at an arbitrary offset and the pile read as one subdivided slab instead of stacked
                // bricks. Object space also keeps the seams on the brick while it tumbles.
                // The +0.5 puts a cell boundary at the block edge (positionOS = +/-0.5) rather than
                // through its middle.
                float3 cellCoord = i.positionOS / max(0.01, _CubeCellSize) + 0.5;
                float3 cellFrac = frac(cellCoord);
                float3 dCell = min(cellFrac, 1.0 - cellFrac);

                float seamX = smoothstep(0.005, _SeamWidth, dCell.x);
                float seamY = smoothstep(0.005, _SeamWidth, dCell.y);
                float seamZ = smoothstep(0.005, _SeamWidth, dCell.z);

                // Face-appropriate seam masking
                float cubeSeam = 1.0;
                if (isTop > 0.5) cubeSeam = min(seamX, seamZ);
                else if (isFront > 0.5) cubeSeam = min(seamX, seamY);
                else cubeSeam = min(seamZ, seamY);

                float seamDarken = lerp(1.0 - _SeamDepth, 1.0, cubeSeam);

                // 3. Volumetric Bevel & Specular Highlight:
                float bevelEdge = pow(1.0 - saturate(dot(N, Vd)), 2.8) * _BevelLift;
                float3 H = normalize(normalize(l.direction) + Vd);
                float spec = pow(saturate(dot(N, H)), 20.0) * _Smoothness * isTop;

                // FLAT. The reference's blocks are unlit: its top face and its cube fronts both
                // measure g=246, so no directional term may separate them. The old
                // (0.70 + ndl * 0.30 * shadowAttenuation) was the second of two dimmers on the
                // vertical faces. At 1.0 every face renders blockBaseColor as authored; our top face
                // already measured 254 (linear ~0.99), so the top is a near no-op by construction
                // and only the fronts and sides move. If the tops shift by more than a point or two
                // after this, the assumed pipeline is wrong and this line should go back.
                float lightFactor = 1.0;
                float3 finalCol = faceCol * lightFactor * seamDarken + half3(1, 1, 1) * (bevelEdge + spec);

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    Fallback Off
}
