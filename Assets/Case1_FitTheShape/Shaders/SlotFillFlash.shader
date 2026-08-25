Shader "Case1/SlotFillFlash"
{
    Properties
    {
        _Color("Tint",Color)=(1,1,1,1)
        _Intensity("Intensity",Range(0,8))=2
        _Core("Core",Range(0,3))=1
        _CoreFalloff("Core Falloff",Range(0.5,10))=3
        _Spike("Spike",Range(0,3))=0
        _SpikeThin("Spike Thinness",Range(1,32))=10
        _SpikeSharp("Spike Sharpness",Range(0.5,8))=2
        _Ring("Ring",Range(0,4))=0
        _RingRadius("Ring Radius",Range(0,1))=0.8
        _RingThin("Ring Thinness",Range(1,64))=14
        // Additive by default (One One). The arrival sparkle overrides these to alpha blending: on a
        // bright cell an additive gold star just clips to white and disappears into the face.
        [HideInInspector]_SrcBlend("Src Blend",Float)=1
        [HideInInspector]_DstBlend("Dst Blend",Float)=1
    }
    SubShader
    {
        Tags{"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline"}
        Pass
        {
            Name "AdditiveFlash"
            Cull Off ZWrite Off ZTest LEqual Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color; float _Intensity; float _Core; float _CoreFalloff; float _Spike; float _SpikeThin; float _SpikeSharp; float _Ring; float _RingRadius; float _RingThin;
            CBUFFER_END
            struct A{float4 positionOS:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            struct V{float4 positionCS:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
            V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color*_Color;o.uv=i.uv;return o;}
            half4 frag(V i):SV_Target
            {
                float2 p=i.uv*2-1; float r=length(p);
                float core=pow(saturate(1-r),_CoreFalloff)*_Core;
                float2 a=abs(p);
                float sx=saturate(1-a.y*_SpikeThin)*saturate(1-a.x);
                float sy=saturate(1-a.x*_SpikeThin)*saturate(1-a.y);
                float star=pow(saturate(max(sx,sy)),_SpikeSharp)*_Spike;
                float ring=pow(saturate(1-abs(r-_RingRadius)*_RingThin),2)*_Ring;
                float m=saturate(core+star+ring)*i.color.a;
                return half4(i.color.rgb*_Intensity*m,m);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
