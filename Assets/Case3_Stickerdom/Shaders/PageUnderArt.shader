// Case 3 - state-driven under-art darkening for the album page sheet.
// The printed under-art must READ as background: its midtone bulk sits lower
// than the paper while paper highlights and the already-dark accents are
// left untouched (no deeper extremes). Driven at runtime via material
// properties so game state can lift or sink the layer without new textures.
Shader "Case3/PageUnderArt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Darken ("Under Art Darken", Range(0.5, 1)) = 0.84
        _MidLo0 ("Midtone Fade In Start", Range(0, 1)) = 0.34
        _MidLo1 ("Midtone Fade In End", Range(0, 1)) = 0.48
        _MidHi0 ("Midtone Fade Out Start", Range(0, 1)) = 0.66
        _MidHi1 ("Midtone Fade Out End", Range(0, 1)) = 0.82
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _Darken;
            float _MidLo0;
            float _MidLo1;
            float _MidHi0;
            float _MidHi1;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, IN.texcoord) * IN.color;
                // channel mean, matching the verification measurement
                float lum = (c.r + c.g + c.b) / 3.0;
                float w = smoothstep(_MidLo0, _MidLo1, lum)
                        * (1.0 - smoothstep(_MidHi0, _MidHi1, lum));
                c.rgb *= 1.0 - (1.0 - _Darken) * w;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
