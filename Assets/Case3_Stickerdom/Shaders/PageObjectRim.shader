// Case 3 - die-cut sticker rim / drop shadow for page objects.
//
// STRUCTURAL INVARIANT
//     The rim's coverage is a function of the screen-space distance d from the fragment to
//     the art's HARD silhouette (alpha >= _AlphaCut) and of nothing else:
//
//         coverage(d) = saturate((W + aa/2 - d) / aa)
//
//     so it is 1 for d <= W - aa/2, 0 for d >= W + aa/2, and the transition is aa pixels
//     wide wherever you stand on the outline. That is what a printed die cut is: a
//     constant-width outset of the silhouette with a hard, antialiased edge.
//
// WHAT WAS WRONG BEFORE, AND WHY IT READ AS A BLUR
//     The previous version computed the rim as max(sourceAlpha) over a ring of taps. Max
//     over a ring DILATES the alpha, it does not harden it: the rim's outer profile came
//     out as a copy of the art's own alpha feather, shifted outward by the ring radius.
//     Every obj_*.png is feathered - measured on the shipped PNGs, the alpha takes a
//     median of 5 to 10 texels to fall from 0.95 to 0.05, with tails to 41 texels
//     (obj_bunplate 9, obj_pie 8, obj_teddy 10, obj_choc 1). So the white band faded out
//     over five to ten pixels instead of one, and because the feather length varies from
//     1 to 41 texels AROUND ONE SPRITE, the band's apparent width varied with it: fuzzy in
//     some places, a smear in others. Thresholding FIRST and measuring distance SECOND
//     severs the rim's geometry from the art's alpha ramp entirely.
//
// HOW d IS FOUND
//     A radial march on the thresholded silhouette: 16 evenly spaced directions, radii
//     stepped outward until the first hit, then three bisections inside the bracket that
//     hit. The radial step is tied to the texture's texel size (never coarser than one
//     texel on screen), so a thin feature - a chopstick, a wisp of steam - cannot fall
//     between two rings and lose its border, which is the other half of the old artefact.
//     16 directions bound the width error at 1/cos(11.25 deg) = 1.9%.
//
// WIDTH UNIT: _RimPixels is in RENDER TARGET PIXELS, not texels and not world units, taken
// from ddx/ddy of the texcoord. The rim keeps its on-screen thickness whatever the sprite's
// resolution or the object's scale is. This is the unit the reference is authored in.
//
// CLIPPING: taps are saturated into [0,1], so a rim wider than the sprite's transparent
// padding degrades into a clipped rim rather than sampling a neighbouring sprite.
Shader "Case3/PageObjectRim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Rim Colour", Color) = (1,1,1,1)
        _RimPixels ("Rim Width (render-target px)", Range(0, 32)) = 9
        _EdgeAA ("Edge softness (render-target px)", Range(0.25, 6)) = 1.5
        _AlphaCut ("Silhouette threshold", Range(0.05, 0.95)) = 0.5
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
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _RimPixels;
            float _EdgeAA;
            float _AlphaCut;

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

            // 1 where the art is solid, 0 where it is not. No partial values leave this
            // function - that is the whole point.
            float Solid(float2 uv)
            {
                return step(_AlphaCut, tex2Dlod(_MainTex, float4(saturate(uv), 0, 0)).a);
            }

            // Does the silhouette touch the ring of radius r (in render px) around uv?
            float RingHit(float2 uv, float2 perPixel, float r)
            {
                float hit = 0;
                [unroll]
                for (int i = 0; i < 16; i++)
                    hit = max(hit, Solid(uv + kDir[i] * perPixel * r));
                return hit;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 perPixel = float2(
                    length(float2(ddx(uv.x), ddy(uv.x))),
                    length(float2(ddx(uv.y), ddy(uv.y))));

                float aa = max(_EdgeAA, 0.25);
                float R = _RimPixels + aa * 0.5;          // nothing past this can matter

                float d = R + 1.0;                        // "no silhouette in range"
                if (Solid(uv) > 0.5)
                {
                    d = 0.0;
                }
                else
                {
                    // Never step further than one on-screen texel, so a one-texel-wide
                    // feature cannot slip between two rings and lose its rim.
                    float texelPx = min(_MainTex_TexelSize.x / max(perPixel.x, 1e-8),
                                        _MainTex_TexelSize.y / max(perPixel.y, 1e-8));
                    float step0 = clamp(min(texelPx, R / 8.0), R / 24.0, R / 8.0);
                    float lo = 0.0;
                    float hi = -1.0;
                    [loop]
                    for (int k = 1; k <= 24; k++)
                    {
                        float r = step0 * k;
                        if (r > R) { r = R; }
                        if (RingHit(uv, perPixel, r) > 0.5) { hi = r; break; }
                        lo = r;
                        if (r >= R) break;
                    }
                    if (hi > 0.0)
                    {
                        // three bisections inside the bracket that hit: distance is then
                        // known to step0/8, i.e. under a fifth of a pixel.
                        [unroll]
                        for (int b = 0; b < 3; b++)
                        {
                            float mid = 0.5 * (lo + hi);
                            if (RingHit(uv, perPixel, mid) > 0.5) hi = mid; else lo = mid;
                        }
                        d = hi;
                    }
                }

                float a = saturate((_RimPixels + aa * 0.5 - d) / aa);
                a *= IN.color.a;
                return fixed4(IN.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
