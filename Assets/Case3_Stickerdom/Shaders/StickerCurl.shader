// Page-curl peel for a sticker, URP Unlit.
//
// _CurlDir is a FREE CONTINUOUS DIRECTION, not one of four cases. The vertex stage works entirely in the
// (along, across) frame that _CurlDir defines - along = dot(p, dir), across = dot(p, perp) - and every
// other term (the arc, the tangent run, the fold ripple, the cast shadow, the shading) is written in that
// frame and never in x/y. So rotating _CurlDir rotates the frame and nothing else: the curl SHAPE is
// invariant under the direction, and 37 deg is as ordinary a value as 90 deg. That invariant is what
// StickerPeel leans on when it points the fold at the finger, and it is checked by rendering the peel
// with the direction pinned back to the old constant and requiring the result to be byte-identical.
//
// Nothing here degenerates at an axis or at a diagonal. The one input the frame cannot survive is a ZERO
// _CurlDir, whose normalize() is a NaN; StickerPeel guarantees a unit vector, which is why there is no
// guard burned into the inner loop.
//
// The sheet is a flat grid mesh in local space. Everything on the +_CurlDir side of a moving fold line
// wraps a cylinder of radius _CurlRadius; once it has wrapped _MaxAngle (pi by default) the rest of the
// sheet continues along the tangent, which lays the peeled flap flat and mirrored with its white back
// towards the camera. The fold line itself is displaced by a sine so the crease travels like a ripple
// instead of sweeping as a rigid straight edge.
//
// Two faces: the front samples the sticker sprite, the back is plain paper (_BackColor). Which one is
// shown comes from cos(theta), so it agrees with the geometry that the rasteriser actually sees.
//
// Deliberately NOT SRP Batcher compatible: the per-sticker properties live outside a UnityPerMaterial
// CBUFFER because the SRP Batcher ignores MaterialPropertyBlock overrides, and this effect is driven
// entirely from a MaterialPropertyBlock so a single shared material can serve every sticker.
Shader "Case3/StickerCurl"
{
    Properties
    {
        [MainTexture] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Front Tint", Color) = (1,1,1,1)
        _BackColor ("Back Face", Color) = (0.86,0.84,0.80,1)
        _CurlDir ("Curl Direction (xy)", Vector) = (0.5145,0.8575,0,0)
        _FoldPos ("Fold Position", Float) = 100
        _CurlRadius ("Curl Radius", Float) = 0.5
        _MaxAngle ("Max Wrap Angle", Float) = 3.14159265
        _WaveAmp ("Fold Wave Amplitude", Float) = 0.0
        _WaveFreq ("Fold Wave Frequency", Float) = 2.0
        _WavePhase ("Fold Wave Phase", Float) = 0.0
        _ShadowWidth ("Fold Shadow Width", Float) = 0.35
        _ShadowStrength ("Fold Shadow Strength", Range(0,1)) = 0.45
        _ShadeFloor ("Curl Shading Floor", Range(0,1)) = 0.55
        _BackAO ("Curl Inner Darkening", Range(0,1)) = 0.45
        _CreaseHighlight ("Crease Highlight", Range(0,1)) = 0.18
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StickerCurlUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off              // the flap turns over: both faces have to draw
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _Color;
            float4 _BackColor;
            float4 _CurlDir;
            float _FoldPos;
            float _CurlRadius;
            float _MaxAngle;
            float _WaveAmp;
            float _WaveFreq;
            float _WavePhase;
            float _ShadowWidth;
            float _ShadowStrength;
            float _ShadeFloor;
            float _BackAO;
            float _CreaseHighlight;
            float _Alpha;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                // x = cos(theta): sign tells front from back. y = sin(theta): peaks on the rounded part
                // of the roll, which is where paper self-shadows. z = fold shadow cast on the flat sheet.
                float3 curl       : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                float2 p = input.positionOS.xy;
                float2 dir = normalize(_CurlDir.xy);
                float2 perp = float2(-dir.y, dir.x);

                float along = dot(p, dir);
                float across = dot(p, perp);
                float fold = _FoldPos + _WaveAmp * sin(across * _WaveFreq + _WavePhase);

                float u = along - fold;
                float3 pos = float3(p, 0.0);
                float cosT = 1.0;
                float sinT = 0.0;
                float shadow = 0.0;

                if (u > 0.0)
                {
                    float radius = max(_CurlRadius, 0.0001);
                    float theta = min(u / radius, _MaxAngle);
                    float rest = u - theta * radius;      // straight tangent run past the clamp
                    sinT = sin(theta);
                    cosT = cos(theta);

                    float newAlong = fold + radius * sinT + rest * cosT;
                    pos.xy = p + dir * (newAlong - along);
                    pos.z = -(radius * (1.0 - cosT) + rest * sinT);   // -z lifts towards the camera
                }
                else
                {
                    // Flat side of the crease sits in the curl's shadow, strongest right at the fold.
                    shadow = _ShadowStrength * saturate(1.0 + u / max(_ShadowWidth, 0.0001));
                }

                o.positionCS = TransformObjectToHClip(pos);
                o.uv = input.uv;
                o.curl = float3(cosT, sinT, shadow);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                float cosT = i.curl.x;
                float sinT = abs(i.curl.y);

                // Flat paper reads at full brightness; the rounded part of the roll turns away from the
                // camera and darkens, which is the thin grey edge the curl shows in the reference.
                float shade = lerp(_ShadeFloor, 1.0, saturate(abs(cosT)));
                float ao = 1.0 - _BackAO * sinT;

                half3 rgb;
                if (cosT < 0.0)
                {
                    rgb = _BackColor.rgb * shade * ao;                       // white paper underside
                }
                else
                {
                    rgb = tex.rgb * _Color.rgb * shade * lerp(1.0, ao, 0.6); // printed face
                }

                rgb *= (1.0 - i.curl.z);
                // A narrow paper highlight on the rounded crease makes the roll read as a physical
                // sheet without a separate particle effect or a fake glowing rim.
                float crease = pow(saturate(sinT), 7.0) * _CreaseHighlight;
                rgb = lerp(rgb, half3(1.0, 0.985, 0.94), crease);

                half alpha = tex.a * _Color.a * _Alpha;
                clip(alpha - 0.004);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
