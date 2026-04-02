Shader "Hidden/StylizedWater2/DisplacementToNormals"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		
		Pass
		{
			HLSLPROGRAM
			
			#pragma vertex vert
			#pragma fragment frag
			
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "DynamicEffects.hlsl"

			struct Attributes
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};
			
			Varyings vert (Attributes input)
			{
				Varyings output;
				
				output.vertex = TransformObjectToHClip(input.vertex.xyz);
				output.uv = input.uv;

				return output;
			}

			float _TexelSize;
			#define STRENGTH 2.0
			
			float4 frag (Varyings input) : SV_Target
			{
				float2 uv = input.uv;
				float radius = _TexelSize;
				const int mip = 0.0;

				const float xLeft = SAMPLE_TEXTURE2D_LOD(_WaterDynamicEffectsBuffer, sampler_LinearClamp, float2(uv.xy - float2(radius, 0.0)), mip)[DE_NORMALS_CHANNEL] * STRENGTH;
				const float xRight = SAMPLE_TEXTURE2D_LOD(_WaterDynamicEffectsBuffer, sampler_LinearClamp, float2(uv.xy + float2(radius, 0.0)), mip)[DE_NORMALS_CHANNEL] * STRENGTH;

				const float yUp = SAMPLE_TEXTURE2D_LOD(_WaterDynamicEffectsBuffer, sampler_LinearClamp, float2(uv.xy - float2(0.0, radius)), mip)[DE_NORMALS_CHANNEL] * STRENGTH;
				const float yDown = SAMPLE_TEXTURE2D_LOD(_WaterDynamicEffectsBuffer, sampler_LinearClamp, float2(uv.xy + float2(0.0, radius)), mip)[DE_NORMALS_CHANNEL] * STRENGTH;

				float xDelta = ((xLeft - xRight) + 1.0) * 0.5f;
				float yDelta = ((yUp - yDown) + 1.0) * 0.5f;

				float4 normals = float4(xDelta, yDelta, 1.0, yDelta);

				return normals;
			}
			ENDHLSL
		}
	}
}