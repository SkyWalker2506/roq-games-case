// Case 3 - state-driven dimming for individual page background objects.
// Adapted from Case3/PageUnderArt: each background sticker object is its own
// sprite; this shader makes it READ as background (darker, desaturated, with
// highlights compressed the way the reference's background stickers are).
// All parameters are runtime material state so game logic can lift an object
// to foreground (set _Darken=1, _HiExtra=0, _Sat=1) without new textures.
Shader "Case3/PageObjectDim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // REFERENCE TRANSFORM. Measured on Stickerdom.mp4 at the promotion instant, over
        // regions uncovered in BOTH states so it is a tone change and not a reveal:
        //   cup band  dim V 74.31 S 0.620 -> lit V 149.53 S 0.615
        //   jar lid   dim V 85.41 S 0.467 -> lit V 168.13 S 0.466
        // Through the sRGB EOTF those are LINEAR ratios 0.227 and 0.234 (mean 0.231) with
        // saturation ratios 1.008 and 0.998. The reference's dim is therefore a FLAT value
        // multiply with saturation untouched - no desaturation, no highlight rolloff, and
        // the same factor on a dark region as on a bright one. _Darken and _HiExtra are the
        // tone-dependent part and are defaulted OFF for that reason; the whole dim now lives
        // in _Value. They are kept as properties because the shader's job is still to be
        // state-driven: lifting an object to foreground is _Value = 1.
        _Darken ("Base Darken", Range(0.3, 1)) = 1
        _Sat ("Saturation Keep", Range(0, 1)) = 1
        _HiExtra ("Highlight Extra Darken", Range(0, 0.5)) = 0
        // THRESHOLD UNIT: these four compare against `lum`, which is a LINEAR value
        // (the project is Linear colour space, m_ActiveColorSpace: 1). They are NOT sRGB.
        // Written as sRGB they read 0.5 / 0.9 / 0.18 / 0.34; the linear equivalents below
        // are ((s+0.055)/1.055)^2.4. Entering sRGB numbers here made _Lo a bit-exact no-op
        // over sRGB 0..128 and pushed the highlight rolloff up to sRGB 188..243.
        _Hi0 ("Highlight Rolloff Start", Range(0, 1)) = 0.2140
        _Hi1 ("Highlight Rolloff End", Range(0, 1)) = 0.7874
        _Lo0 ("Dark Protect End", Range(0, 1)) = 0.0272
        _Lo1 ("Dark Protect Start", Range(0, 1)) = 0.0946
        // Ungated value multiply, and the ONLY thing the dim state is made of.
        // 0.238 is the reference's linear factor (see above). This default and
        // Case3_PageObjectDim.mat are a TWO-COPY PAIR: a material that does not
        // serialise _Value falls back to this number, so both must carry the same
        // value or the fix is only half applied. That failure has already happened
        // once on this shader, with the four thresholds below.
        _Value ("Value Multiply", Range(0, 1)) = 0.238
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
            float _Sat;
            float _HiExtra;
            float _Hi0;
            float _Hi1;
            float _Lo0;
            float _Lo1;
            float _Value;

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
                c.rgb = lerp(float3(lum, lum, lum), c.rgb, _Sat);
                float f = _Darken - _HiExtra * smoothstep(_Hi0, _Hi1, lum);
                // pixels that are already dark stay untouched (same rule as PageUnderArt)
                f = 1.0 - (1.0 - f) * smoothstep(_Lo0, _Lo1, lum);
                c.rgb *= f;
                // unconditional: no threshold, no smoothstep, no divide
                c.rgb *= _Value;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
