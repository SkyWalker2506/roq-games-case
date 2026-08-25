// Case 3 - die-cut sticker rim / drop shadow for page objects.
//
// The reference's page items are printed stickers: every one of them, bright or dimmed,
// is cut out with a thick white border and drops a soft shadow onto the paper. Our page
// object textures carry no border at all (measured: 0 near-white pixels inside the opaque
// area of all 14 obj_*.png), so the rim cannot come from the art - it has to be generated.
//
// This shader renders the sprite's alpha DILATED by a fixed width, flat-filled with
// _Color. Drawn on a sibling renderer one sorting order behind the object it belongs to,
// the dilated silhouette shows only as a ring around the object: a die cut. Give it a dark
// colour and an offset and the same pass is the drop shadow.
//
// WIDTH UNIT: _RimPixels is in RENDER TARGET PIXELS, not texels and not world units. The
// UV step per screen pixel is taken from ddx/ddy of the texcoord, so the rim keeps the same
// on-screen thickness whatever the sprite's texture resolution or the object's scale is.
// This is the unit the reference is authored in - its rims measure 8-14 px on a 1080-wide
// frame regardless of how big the sticker is.
//
// CLIPPING: every obj_*.png is imported with spriteMeshType 0 (FullRect) and carries at
// least 9 px of transparent padding on every side, so a dilation of up to ~7 texels has
// somewhere to go. The ring taps are saturated into [0,1] so a wider setting degrades into
// a clipped rim rather than sampling a neighbouring sprite.
Shader "Case3/PageObjectRim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Rim Colour", Color) = (1,1,1,1)
        _RimPixels ("Rim Width (render-target px)", Range(0, 32)) = 10
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
            float _RimPixels;

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

            // 16 evenly spaced directions; the ring is sampled at full radius and at half
            // radius so a thin neck in the art cannot leave a gap in the rim.
            static const float2 kDir[16] =
            {
                float2( 1.0000,  0.0000), float2( 0.9239,  0.3827),
                float2( 0.7071,  0.7071), float2( 0.3827,  0.9239),
                float2( 0.0000,  1.0000), float2(-0.3827,  0.9239),
                float2(-0.7071,  0.7071), float2(-0.9239,  0.3827),
                float2(-1.0000,  0.0000), float2(-0.9239, -0.3827),
                float2(-0.7071, -0.7071), float2(-0.3827, -0.9239),
                float2( 0.0000, -1.0000), float2( 0.3827, -0.9239),
                float2( 0.7071, -0.7071), float2( 0.9239, -0.3827)
            };

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                // uv travelled per render-target pixel, along u and along v
                float2 perPixel = float2(
                    length(float2(ddx(uv.x), ddy(uv.x))),
                    length(float2(ddx(uv.y), ddy(uv.y))));
                float2 step = perPixel * _RimPixels;

                float a = tex2Dlod(_MainTex, float4(uv, 0, 0)).a;
                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    float2 d = kDir[i] * step;
                    a = max(a, tex2Dlod(_MainTex, float4(saturate(uv + d), 0, 0)).a);
                    a = max(a, tex2Dlod(_MainTex, float4(saturate(uv + d * 0.5), 0, 0)).a);
                }

                a *= IN.color.a;
                // premultiplied, to match Blend One OneMinusSrcAlpha
                return fixed4(IN.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
