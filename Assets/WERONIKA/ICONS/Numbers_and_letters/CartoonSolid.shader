Shader "Custom/CartoonSolid"
{
    Properties
    {
        _LightColor   ("Light Color", Color) = (1, 0, 0, 1)
        _ShadowColor  ("Shadow Color", Color) = (0.2, 0, 0, 1)
        _ShadowOffset ("Shadow Offset", Range(-1,1)) = 0.0
        _ShadowSoft   ("Shadow Softness", Range(0.01,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Cull Back
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            fixed4 _LightColor;
            fixed4 _ShadowColor;
            float _ShadowOffset;
            float _ShadowSoft;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Stylizowany "kierunek światła"
                float3 lightDir = normalize(float3(0.3, 0.6, 0.7));

                float ndl = dot(normalize(i.normal), lightDir);

                // przesunięcie granicy światła
                ndl += _ShadowOffset;

                // miękkie przejście między kolorami
                float shade = smoothstep(0.0, _ShadowSoft, ndl);

                fixed3 color = lerp(_ShadowColor.rgb, _LightColor.rgb, shade);

                return fixed4(color, 1);
            }
            ENDCG
        }
    }
}
