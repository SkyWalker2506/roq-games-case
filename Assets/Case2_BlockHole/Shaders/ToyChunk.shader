// Shard material for the shatter.
//
// This pass used to be declared "RenderType"="Opaque" with `Cull Back ZWrite On` and NO Blend
// statement at all, so the alpha it returned was discarded by the hardware. BlockShatterSink
// serialises `shardAlpha`, folds it into shardColor.a and writes it to _BaseColor every burst -
// all of which could never have any effect. That is why our chunks read as flat opaque polygons
// while the reference's are glassy and layer visibly where they overlap.
//
// Measured at 1.60 against the reference, the shortfall is concentrated in the mid-tones:
// 14,961 px in the 140-200 blue band against our 5,273, while we already EXCEED it at peak
// brightness. A translucent shard over the dark pit lands squarely in that band - one layer of
// (152,45,249) at 0.62 over a (33,10,48) floor resolves to (107,32,172) - and two overlapping
// layers build toward the peak. Layering is what produces that distribution; opaque chunks
// cannot, at any size or count.
Shader "Case2/ToyChunk"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color)=(0.5,0.4,1,1)
        _EdgeLift("Edge Lift",Range(0,0.5))=0.12
        _FaceContrast("Face Contrast",Range(0,0.5))=0.16
        // The chunks are pieces OF the blocks, so they carry the same painted-wood grain.
        // Both break paths land here: BlockShatterSink.Shatter assigns `shardMaterial` to
        // every MeshRenderer it finds in ONE loop, whether the cloud came from a block's
        // own `fracturedPrefab` (only Drag_2 has one) or from BuildProceduralFragments
        // (the other three). One material, so one shader, covers both - and the scene wires
        // exactly one shardMaterial (Case2_CrystalShard, guid 43a4325891bca4bf388d1968fd51fb0a).
        //
        // PROVEN to reach pixels, not assumed: two dense captures differing only in this
        // default (0.20 -> 0.0) changed 37,982 px at frame 160 and 41,043 px at frame 170,
        // max channel-sum difference 96 and 124, and the changed bounding box was the shard
        // cloud itself (x 67-520, y 915-1387). Frame 150, before any chunk spawns, changed
        // ZERO pixels - the negative arm of the same probe.
        _GrainStrength("Wood Grain Strength", Range(0, 1)) = 0.20
        _GrainFrequency("Grain Frequency", Range(1, 30)) = 2.1
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
        Pass
        {
            Tags{"LightMode"="UniversalForward"}
            // ZWrite Off so overlapping shards accumulate instead of the nearest one hiding the
            // rest - the layering IS the effect, not a side effect of it.
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _EdgeLift; float _FaceContrast;
            float _GrainStrength; float _GrainFrequency;
            CBUFFER_END
            struct A{float4 positionOS:POSITION; float3 normalOS:NORMAL;};
            struct V{float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;};
            V vert(A i){V o;VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);VertexNormalInputs n=GetVertexNormalInputs(i.normalOS);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=n.normalWS;return o;}

            // Byte-for-byte the streamline field ToyBlock uses, so a chunk's grain is the same
            // material as the face it broke off. Sampled in WORLD xz rather than object space:
            // a fracture piece's object space is its own little box, so object-space UVs would
            // give every chip its own arbitrary grain scale.
            float WoodGrain(float2 uv)
            {
                float period = 1.0 / max(_GrainFrequency, 0.001);
                float wave = sin(uv.x * 3.1 + sin(uv.y * 1.9) * 1.6) * 0.22 * period;
                wave += sin(uv.x * 8.3 + cos(uv.y * 4.4) * 1.1) * 0.07 * period;
                float coord = (uv.y + wave) * _GrainFrequency;
                float rings = sin(coord * 6.28318);
                float fine = sin(coord * 18.8495) * 0.32;
                float g = saturate((rings + fine) * 0.5 + 0.5);
                g = smoothstep(0.34, 0.72, g);
                return pow(g, 1.4);
            }

            half4 frag(V i):SV_Target
            {
                float3 N=normalize(i.normalWS); Light l=GetMainLight();
                float2 faceUV = (abs(N.y) > 0.5 ? i.positionWS.xz : (abs(N.x) > 0.5 ? i.positionWS.zy : i.positionWS.xy)) * 1.5;
                float grain = WoodGrain(faceUV);
                float2 e = float2(0.01, 0.0);
                float dGx = (WoodGrain(faceUV + e.xy) - WoodGrain(faceUV - e.xy)) * 0.5;
                float dGy = (WoodGrain(faceUV + e.yx) - WoodGrain(faceUV - e.yx)) * 0.5;
                N = normalize(N + float3(dGx, 0, dGy) * (_GrainStrength * 3.5));
                float ndl=saturate(dot(N,normalize(l.direction)));
                float3 Vd=SafeNormalize(GetWorldSpaceViewDir(i.positionWS)); float edge=pow(1-saturate(dot(N,Vd)),3)*_EdgeLift;
                float face=abs(N.y)*_FaceContrast;
                // Grazing faces read denser, the way a real glassy chip does - so a chunk seen
                // edge-on is more solid than one seen flat, and the cloud gains internal structure
                // rather than being a uniform wash.
                float density=saturate(_BaseColor.a*(1.0+edge*1.6));
                float3 albedo = _BaseColor.rgb * (1.0 - grain * _GrainStrength);
                return half4(albedo*(0.70+ndl*0.24+face+edge),density);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
