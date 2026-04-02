Shader "Custom/InkOutline_Final_B"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width", Range(0,0.05)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // ============================
        // PASS 1 — OUTLINE (Z TYŁU)
        // ============================
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Offset 1, 1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _OutlineWidth;
            float4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;

                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
                float3 worldNormal = normalize(TransformObjectToWorldNormal(v.normal));

                worldPos += worldNormal * _OutlineWidth;

                o.pos = TransformWorldToHClip(worldPos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }

            ENDHLSL
        }

        // ============================
        // PASS 2 — FILL (NA WIERZCHU)
        // ============================
        Pass
        {
            Name "Fill"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BaseColor;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
                o.pos = TransformWorldToHClip(worldPos);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return _BaseColor;
            }

            ENDHLSL
        }
    }
}
