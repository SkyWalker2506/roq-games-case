Shader "Examples/StencilDepthAwareMask"
{
    Properties
    {
        [IntRange] _StencilID ("Stencil ID", Range(0, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ColorMask 0
            Cull Off

            ZTest LEqual
            ZWrite Off

            Stencil
            {
                Ref [_StencilID]
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }
        }
    }
}