Shader "Custom/Cartoon2" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _LightDirection ("Light Direction", Range(0, 360)) = 45
        _ShadowTint ("Shadow Tint", Range(0, 1)) = 0.5
        _RampTex ("Ramp Texture", 2D) = "white" {}
    }

    SubShader {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        LOD 100

        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _RampTex;
            float _OutlineWidth;
            float _LightDirection;
            float _ShadowTint;
            fixed4 _OutlineColor;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // Apply the main texture
                fixed4 col = tex2D(_MainTex, i.uv * _MainTex_ST.xy + _MainTex_ST.zw);

                // Apply the cel-shading effect
                float2 rampUV = i.uv * 2 - 1;
                float ramp = tex2D(_RampTex, rampUV).r;
                col.rgb = lerp(_OutlineColor.rgb, col.rgb, ramp);
                col.rgb += _OutlineColor.rgb * _OutlineWidth;

                // Apply the shadow tint
                col.rgb *= _ShadowTint;

                // Apply the directional lighting
                float angle = _LightDirection * (3.14159 / 180);
                float2 lightDir = float2(cos(angle), sin(angle));
                float2 normal = normalize(rampUV);
                float light = dot(normal, lightDir);
                col.rgb *= max(light, 0);

                // Set alpha to 1
                col.a = 1;

                return col;
            }
            ENDCG
        }
    }
}