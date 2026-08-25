Shader "ShapeSlot/SpriteAlphaBoost"
{
    // Renders a SpriteRenderer as a SOLID tint colour, using the texture's alpha as a mask but
    // BOOSTING it so a semi-transparent source (e.g. a ~50%-alpha white frame) shows fully opaque.
    // Lets a soft/low-opacity sprite read as crisp solid white over a coloured background without
    // editing the source PNG. Works in URP's transparent path (built-in CG sprite shader).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _AlphaBoost ("Alpha Boost", Range(1,12)) = 10
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0
        _Intensity ("Intensity (HDR, beats tonemapping)", Range(1,6)) = 2.5
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            float _AlphaBoost;
            float _AlphaCutoff;
            float _Intensity;
            sampler2D _MainTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                fixed a = saturate(tex.a * _AlphaBoost) * IN.color.a;
                a = a < _AlphaCutoff ? 0 : a;
                return half4(IN.color.rgb * _Intensity, a); // HDR rgb survives tonemapping → reads white
            }
            ENDCG
        }
    }
}
