Shader "Stylized Water 2/Dynamic Effect"
{
	Properties
	{
		[Enum(UnityEngine.Rendering.BlendOp)] _BlendOp("Blend Operation", float) = 0
		
		_BaseMap ("Texture (R=Height mask, G=Foam Mask)", 2D) = "black" {}
		_AnimationSpeed ("Panning Speed (XY)", Vector) = (0,0,0,0)
		_MaskMap ("Mask", 2D) = "white" {}
		_MaskAnimationSpeed ("Panning Speed (XY)", Vector) = (0,0,0,0)

		[Header(Output)]
		
		[PerRendererData] _HeightScale ("Height scale", Float) = 1.0
		[PerRendererData] _FoamStrength ("Foam strength", Float) = 1.0
		[PerRendererData] _NormalStrength ("Normal strength", Float) = 1.0
	}
	
	SubShader
	{
		Tags 
		{ 
			"LightMode" = "WaterDynamicEffect"
			//"LightMode" = "UniversalForward"
			"RenderQueue" = "Transparent"
			"RenderType" = "Transparent"
		}
		
		Blend SrcAlpha OneMinusSrcAlpha
		BlendOp [_BlendOp]
		ZWrite Off
		//ZClip Off
		//ZTest Always
		
		Pass
		{
			Name "Output"
			
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile_instancing
			//#pragma instancing_options procedural:ParticleInstancingSetup
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"
			
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Common.hlsl"
			
			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv : TEXCOORD0;
				float4 positionCS : SV_POSITION;
				float4 color : TEXCOORD1;

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};
			
			Varyings vert (Attributes input)
			{
				Varyings output;
				
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				output.color = input.color;

				return output;
			}
			
			float4 frag (Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				
				float4 col = tex2D(_BaseMap, TRANSFORM_TEX(input.uv + (_TimeParameters.xx * AnimationSpeed.xy), _BaseMap));
				float maskMap = tex2D(_MaskMap, TRANSFORM_TEX(input.uv + (_TimeParameters.xx * MaskAnimationSpeed.xy), _MaskMap)).r;

				float height = col.r * input.color.r;
				float foam = FoamStrength * col.g * input.color.g;

				float mask = saturate(height + foam);

				EffectOutput output;
				output.displacement = height * HeightScale;
				output.foamAmount = foam;
				output.normalGradient = height * NormalStrength;
				output.alpha = mask * input.color.a * col.a * maskMap;
				
				OUTPUT_EFFECT(output);
			}
			ENDHLSL
		}
	}
}