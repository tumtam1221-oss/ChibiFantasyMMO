// Made with Amplify Shader Editor v1.9.9.12
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "ToonScapes/URP/Terrain URP17"
{
	Properties
	{
		[HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
		[HideInInspector] _TerrainHolesTexture( "_TerrainHolesTexture", 2D ) = "white" {}
		[HideInInspector] _Splat3( "Splat3", 2D ) = "white" {}
		[HideInInspector] _Control( "Control", 2D ) = "white" {}
		[HideInInspector] _Splat2( "Splat2", 2D ) = "white" {}
		[HideInInspector] _Splat1( "Splat1", 2D ) = "white" {}
		[HideInInspector] _Splat0( "Splat0", 2D ) = "white" {}
		[HideInInspector] _Normal0( "Normal0", 2D ) = "white" {}
		[HideInInspector] _Normal1( "Normal1", 2D ) = "white" {}
		[HideInInspector] _Normal2( "Normal2", 2D ) = "white" {}
		[HideInInspector] _Normal3( "Normal3", 2D ) = "white" {}
		[Header(Color)][Space(8)][Toggle( _ENABLECOLORTINT_ON )] _EnableColorTint( "Enable Color Tint", Float ) = 1
		[Space(8)] _BaseTint( "Base Tint", Color ) = ( 1, 1, 1, 1 )
		[Header(Textures)][NoScaleOffset][SingleLineTexture][Space (8)] _TextureRamp3( "Texture Ramp", 2D ) = "white" {}
		_RampScale( "Ramp Scale", Range( 0, 1 ) ) = 0.5
		_RampOffset( "Ramp Offset", Range( 0, 1 ) ) = 0.5
		[Header(Highlights)][Space(8)][Toggle( _ENABLESPECULARHIGHLIGHTS_ON )] _EnableSpecularHighlights( "Enable Specular Highlights", Float ) = 1
		[Header(Specular Highlights)][Space(8)] _SpecularColor( "Specular Color", Color ) = ( 0, 0, 0, 0 )
		_SpecularIntensity1( "Specular Intensity", Range( 0, 1 ) ) = 0
		_Smoothness( "Smoothness", Range( 0, 1 ) ) = 0
		[Space(8)] _SecondarySpecularIntensity( "Secondary Specular Intensity", Range( 0, 1 ) ) = 0.5
		_SecondarySpecularSize( "Secondary Specular Size", Range( 0, 1 ) ) = 0
		_SecondarySmoothness( "Secondary Smoothness", Range( 0.001, 1 ) ) = 1
		[Header(Surface Options)][Space (8)] _Occlusion1( "Occlusion", Range( 0, 1 ) ) = 1
		_AdditionalLightInfluence( "Additional Light Influence", Range( 0, 1 ) ) = 0.5
		_AdditionalLightFalloff( "Additional Light Falloff", Range( 0, 12 ) ) = 1


		//_TransmissionShadow( "Transmission Shadow", Range( 0, 1 ) ) = 0.5
		//_TransStrength( "Trans Strength", Range( 0, 50 ) ) = 1
		//_TransNormal( "Trans Normal Distortion", Range( 0, 1 ) ) = 0.5
		//_TransScattering( "Trans Scattering", Range( 1, 50 ) ) = 2
		//_TransDirect( "Trans Direct", Range( 0, 1 ) ) = 0.9
		//_TransAmbient( "Trans Ambient", Range( 0, 1 ) ) = 0.1
		//_TransShadow( "Trans Shadow", Range( 0, 1 ) ) = 0.5

		//_TessPhongStrength( "Tess Phong Strength", Range( 0, 1 ) ) = 0.5
		//_TessValue( "Tess Max Tessellation", Range( 1, 32 ) ) = 16
		//_TessMin( "Tess Min Distance", Float ) = 10
		//_TessMax( "Tess Max Distance", Float ) = 25
		//_TessEdgeLength ( "Tess Edge length", Range( 2, 50 ) ) = 16
		//_TessMaxDisp( "Tess Max Displacement", Float ) = 25

		[KeywordEnum(Vertex, Pixel)] _InstancedTerrainNormals("Instanced Terrain Normals", Float) = 1.0

		[ToggleOff(_SPECULARHIGHLIGHTS_OFF)] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
		[ToggleOff] _ScreenSpaceReflections("Screen Space Reflections", Float) = 1.0
		[ToggleOff] _ScreenSpaceReflectionsContributeTransparent("Screen Space Reflections Contribute Transparent", Float) = 1.0
		[HideInInspector][ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0

		[HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

		//[HideInInspector][ToggleUI] _AddPrecomputedVelocity("Add Precomputed Velocity", Float) = 1
		//[HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1

		//[HideInInspector] _AlphaClip("__clip", Float) = 1.0
	}

	SubShader
	{
		PackageRequirements
		{
			"com.unity.render-pipelines.universal": "[17.0,18.0]"
		}

		

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-100" "UniversalMaterialType"="Lit" "DisableBatching"="False" "IgnoreProjector"="True" "TerrainCompatible"="True" "MaskMapR"="Metallic" "MaskMapG"="AO" "MaskMapB"="Height" "MaskMapA"="Smoothness" "AlwaysRenderMotionVectors"="false" }

	LOD 0

		Cull Back
		ZWrite On
		ZTest LEqual
		Offset 0 , 0
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma target 4.5
		#pragma prefer_hlslcc gles
		// ensure rendering platforms toggle list is visible

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

		#define ASE_ADJUST_CLIP_POSITION( x ) x

		#ifndef ASE_TESS_FUNCS
		#define ASE_TESS_FUNCS
		float4 FixedTess( float tessValue )
		{
			return tessValue;
		}

		float CalcDistanceTessFactor (float4 vertex, float minDist, float maxDist, float tess, float4x4 o2w, float3 cameraPos )
		{
			float3 wpos = mul(o2w,vertex).xyz;
			float dist = distance (wpos, cameraPos);
			float f = clamp(1.0 - (dist - minDist) / (maxDist - minDist), 0.01, 1.0) * tess;
			return f;
		}

		float4 CalcTriEdgeTessFactors (float3 triVertexFactors)
		{
			float4 tess;
			tess.x = 0.5 * (triVertexFactors.y + triVertexFactors.z);
			tess.y = 0.5 * (triVertexFactors.x + triVertexFactors.z);
			tess.z = 0.5 * (triVertexFactors.x + triVertexFactors.y);
			tess.w = (triVertexFactors.x + triVertexFactors.y + triVertexFactors.z) / 3.0f;
			return tess;
		}

		float CalcEdgeTessFactor (float3 wpos0, float3 wpos1, float edgeLen, float3 cameraPos, float4 scParams )
		{
			float dist = distance (0.5 * (wpos0+wpos1), cameraPos);
			float len = distance(wpos0, wpos1);
			float f = max(len * scParams.y / (edgeLen * dist), 1.0);
			return f;
		}

		float DistanceFromPlane (float3 pos, float4 plane)
		{
			float d = dot (float4(pos,1.0f), plane);
			return d;
		}

		bool WorldViewFrustumCull (float3 wpos0, float3 wpos1, float3 wpos2, float cullEps, float4 planes[6] )
		{
			float4 planeTest;
			planeTest.x = (( DistanceFromPlane(wpos0, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[0]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[0]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.y = (( DistanceFromPlane(wpos0, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[1]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[1]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.z = (( DistanceFromPlane(wpos0, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[2]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[2]) > -cullEps) ? 1.0f : 0.0f );
			planeTest.w = (( DistanceFromPlane(wpos0, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos1, planes[3]) > -cullEps) ? 1.0f : 0.0f ) +
							(( DistanceFromPlane(wpos2, planes[3]) > -cullEps) ? 1.0f : 0.0f );
			return !all (planeTest);
		}

		float4 DistanceBasedTess( float4 v0, float4 v1, float4 v2, float tess, float minDist, float maxDist, float4x4 o2w, float3 cameraPos )
		{
			float3 f;
			f.x = CalcDistanceTessFactor (v0,minDist,maxDist,tess,o2w,cameraPos);
			f.y = CalcDistanceTessFactor (v1,minDist,maxDist,tess,o2w,cameraPos);
			f.z = CalcDistanceTessFactor (v2,minDist,maxDist,tess,o2w,cameraPos);

			return CalcTriEdgeTessFactors (f);
		}

		float4 EdgeLengthBasedTess( float4 v0, float4 v1, float4 v2, float edgeLength, float4x4 o2w, float3 cameraPos, float4 scParams )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;
			tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
			tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
			tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
			tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			return tess;
		}

		float4 EdgeLengthBasedTessCull( float4 v0, float4 v1, float4 v2, float edgeLength, float maxDisplacement, float4x4 o2w, float3 cameraPos, float4 scParams, float4 planes[6] )
		{
			float3 pos0 = mul(o2w,v0).xyz;
			float3 pos1 = mul(o2w,v1).xyz;
			float3 pos2 = mul(o2w,v2).xyz;
			float4 tess;

			if (WorldViewFrustumCull(pos0, pos1, pos2, maxDisplacement, planes))
			{
				tess = 0.0f;
			}
			else
			{
				tess.x = CalcEdgeTessFactor (pos1, pos2, edgeLength, cameraPos, scParams);
				tess.y = CalcEdgeTessFactor (pos2, pos0, edgeLength, cameraPos, scParams);
				tess.z = CalcEdgeTessFactor (pos0, pos1, edgeLength, cameraPos, scParams);
				tess.w = (tess.x + tess.y + tess.z) / 3.0f;
			}
			return tess;
		}
		#endif //ASE_TESS_FUNCS
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForward" "DisableBatching"="False" "IgnoreProjector"="True" "TerrainCompatible"="True" "MaskMapR"="Metallic" "MaskMapG"="AO" "MaskMapB"="Height" "MaskMapA"="Smoothness" "AlwaysRenderMotionVectors"="false" }

			Blend One Zero, One Zero
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
			#pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#pragma shader_feature _INSTANCEDTERRAINNORMALS_PIXEL
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
			#if ( UNITY_VERSION >= 60010000 )
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			#endif

			#if defined(UNITY_PLATFORM_META_QUEST) && ( UNITY_VERSION >= 60050000 )
            #pragma multi_compile _ META_QUEST_LIGHTUNROLL
            #endif

			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#if ( UNITY_VERSION >= 60030000 )
			#pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
			#endif
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile _ _LIGHT_LAYERS
			#pragma multi_compile_fragment _ _LIGHT_COOKIES
			#if ( UNITY_VERSION >= 60010000 )
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#else
			#pragma multi_compile _ _FORWARD_PLUS
			#endif

            #if defined(UNITY_PLATFORM_META_QUEST) && ( UNITY_VERSION >= 60050000 )
            #pragma multi_compile _ META_QUEST_ORTHO_PROJ
            #pragma multi_compile _ META_QUEST_NO_SPOTLIGHTS_LIGHT_LOOP
            #endif

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ LIGHTMAP_ON
			#if ( UNITY_VERSION >= 60010000 )
			#pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
			#endif
			#if ( UNITY_VERSION >= 60030000 )
			#pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
			#endif
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma multi_compile _ USE_LEGACY_LIGHTMAPS

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_FORWARD

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#if ( UNITY_VERSION >= 60010000 )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
			#else
			#pragma multi_compile_fog
			#endif
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_WORLD_VIEW_DIR
			#define ASE_NEEDS_WORLD_TANGENT
			#define ASE_NEEDS_FRAG_WORLD_TANGENT
			#define ASE_NEEDS_WORLD_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_BITANGENT
			#define ASE_NEEDS_WORLD_POSITION
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_FRAG_SHADOWCOORDS
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#pragma shader_feature_local _ENABLESPECULARHIGHLIGHTS_ON
			#pragma shader_feature_local _ENABLECOLORTINT_ON
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			#if ( UNITY_VERSION < 60010000 )
				#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
				#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 lightmapUVOrVertexSH : TEXCOORD3;
				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					half4 fogFactorAndVertexLight : TEXCOORD4;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					float2 dynamicLightmapUV : TEXCOORD5;
				#endif
				#if defined(USE_APV_PROBE_OCCLUSION)
					float4 probeOcclusion : TEXCOORD6;
				#endif
				float4 ase_texcoord7 : TEXCOORD7;
				float4 ase_texcoord8 : TEXCOORD8;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			TEXTURE2D(_Normal0);
			TEXTURE2D(_Splat0);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Control);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal1);
			TEXTURE2D(_Splat1);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal2);
			TEXTURE2D(_Splat2);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal3);
			TEXTURE2D(_Splat3);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_TerrainHolesTexture);
			SAMPLER(sampler_TerrainHolesTexture);
			TEXTURE2D(_TextureRamp3);
			SAMPLER(sampler_TextureRamp3);
			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			half3 ASEIndirectDiffuse( PackedVaryings input, half3 normalWS, float3 positionWS, half3 viewDirWS )
			{
			#if defined( DYNAMICLIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, 0, normalWS );
			#elif defined( LIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, 0, normalWS );
			#elif defined( PROBE_VOLUMES_L1 ) || defined( PROBE_VOLUMES_L2 )
				return SampleProbeVolumePixel( SampleSH( normalWS ), positionWS, normalWS, viewDirWS, input.positionCS.xy );
			#else
				return SampleSH( normalWS );
			#endif
			}
			
			half4 CalculateShadowMask1_g61188( half2 LightmapUV )
			{
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
				return SAMPLE_SHADOWMASK( LightmapUV.xy );
				#elif !defined (LIGHTMAP_ON)
				return unity_ProbesOcclusion;
				#else
				return half4( 1, 1, 1, 1 );
				#endif
			}
			
			float3 AdditionalLightsLambertMask17x( float3 WorldPosition, float2 ScreenUV, float3 WorldNormal, float4 ShadowMask )
			{
				#if ( UNITY_VERSION < 60010000 ) && !defined( USE_CLUSTER_LIGHT_LOOP )
					#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
				#endif
				#if ( UNITY_VERSION < 60010000 ) && !defined( CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK )
					#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
				#endif
				float3 Color = 0;
				#if defined(_ADDITIONAL_LIGHTS)
					#if ( UNITY_VERSION >= 60010000 )
						#define CALC_LAMBERT(Light) LightingLambert( AttLightColor, Light.direction, WorldNormal )
					#else
						#define CALC_LAMBERT(Light) ( dot( Light.direction, WorldNormal ) * 0.5 + 0.5 )* AttLightColor
					#endif
					#define SUM_LIGHTLAMBERT(Light)\
						half3 AttLightColor = Light.color * ( Light.distanceAttenuation * Light.shadowAttenuation );\
						Color += CALC_LAMBERT(Light);
					InputData inputData = (InputData)0;
					inputData.normalizedScreenSpaceUV = ScreenUV;
					inputData.positionWS = WorldPosition;
					uint meshRenderingLayers = GetMeshRenderingLayer();
					uint pixelLightCount = GetAdditionalLightsCount();	
					#if USE_CLUSTER_LIGHT_LOOP
					[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					}
					#endif
					LIGHT_LOOP_BEGIN( pixelLightCount )
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					LIGHT_LOOP_END
				#endif
				return Color;
			}
			
			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.texcoord );
				#endif
				
				float2 break956_g61186 = _Control_ST.zw;
				float2 appendResult959_g61186 = (float2(( break956_g61186.x + 0.001 ) , ( break956_g61186.y + 0.0001 )));
				float2 vertexToFrag961_g61186 = ( ( input.texcoord.xy * _Control_ST.xy ) + appendResult959_g61186 );
				output.ase_texcoord7.zw = vertexToFrag961_g61186;
				
				output.ase_texcoord7.xy = input.texcoord.xy;
				output.ase_texcoord8.xy = input.texcoord1.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord8.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif
				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				#ifdef ASE_CUSTOM_MOTION_VECTOR
					// Declared so the Motion Vector output port surfaces on the master node; only consumed by the motion vector passes.
					float3 aseCustomMotionVector = float3(0, 0, 0);
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				OUTPUT_LIGHTMAP_UV(input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy);
				#if defined(DYNAMICLIGHTMAP_ON)
					output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif
				OUTPUT_SH4(vertexInput.positionWS, normalInput.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.lightmapUVOrVertexSH.xyz, output.probeOcclusion);

				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					output.fogFactorAndVertexLight = 0;
					#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
						output.fogFactorAndVertexLight.x = ComputeFogFactor(vertexInput.positionCS.z);
					#endif
					#ifdef _ADDITIONAL_LIGHTS_VERTEX
						half3 vertexLight = VertexLighting( vertexInput.positionWS, normalInput.normalWS );
						output.fogFactorAndVertexLight.yzw = vertexLight;
					#endif
				#endif

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = input.texcoord1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = input.texcoord2;
				#endif
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				#endif
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag ( PackedVaryings input
						#if defined( ASE_WRITE_DEPTH )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined( _SURFACE_TYPE_TRANSPARENT )
					const bool isTransparent = true;
				#else
					const bool isTransparent = false;
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWS );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float4 ControlFinal724_g61186 = float4( 1, 1, 1, 1 );
				float2 uv_Splat0 = input.ase_texcoord7.xy * _Splat0_ST.xy + _Splat0_ST.zw;
				float4 tex2DNode2_g61186 = SAMPLE_TEXTURE2D( _Normal0, sampler_linear_repeat, uv_Splat0 );
				float _HeightA279_g61186 = tex2DNode2_g61186.a;
				float smoothstepResult859_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult937_g61186 = clamp( smoothstepResult859_g61186 , 0.001 , 0.999 );
				float2 vertexToFrag961_g61186 = input.ase_texcoord7.zw;
				float4 tex2DNode5_g61186 = SAMPLE_TEXTURE2D( _Control, sampler_linear_repeat, vertexToFrag961_g61186 );
				float _MaskA481_g61186 = tex2DNode5_g61186.r;
				float clampResult879_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_878_0_g61186 = ( clampResult937_g61186 * clampResult879_g61186 );
				float4 tex2DNode4_g61186 = SAMPLE_TEXTURE2D( _Splat0, sampler_linear_repeat, uv_Splat0 );
				float _LayerAlphaA778_g61186 = tex2DNode4_g61186.a;
				float2 uv_Splat1 = input.ase_texcoord7.xy * _Splat1_ST.xy + _Splat1_ST.zw;
				float4 tex2DNode1_g61186 = SAMPLE_TEXTURE2D( _Normal1, sampler_linear_repeat, uv_Splat1 );
				float _HeightB280_g61186 = tex2DNode1_g61186.a;
				float smoothstepResult861_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult938_g61186 = clamp( smoothstepResult861_g61186 , 0.001 , 0.999 );
				float _MaskB482_g61186 = tex2DNode5_g61186.g;
				float clampResult881_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_875_0_g61186 = ( clampResult938_g61186 * clampResult881_g61186 );
				float4 tex2DNode3_g61186 = SAMPLE_TEXTURE2D( _Splat1, sampler_linear_repeat, uv_Splat1 );
				float _LayerAlphaB779_g61186 = tex2DNode3_g61186.a;
				float2 uv_Splat2 = input.ase_texcoord7.xy * _Splat2_ST.xy + _Splat2_ST.zw;
				float4 tex2DNode10_g61186 = SAMPLE_TEXTURE2D( _Normal2, sampler_linear_repeat, uv_Splat2 );
				float _HeightC281_g61186 = tex2DNode10_g61186.a;
				float smoothstepResult860_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult939_g61186 = clamp( smoothstepResult860_g61186 , 0.001 , 0.999 );
				float _MaskC483_g61186 = tex2DNode5_g61186.b;
				float clampResult880_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_876_0_g61186 = ( clampResult939_g61186 * clampResult880_g61186 );
				float4 tex2DNode6_g61186 = SAMPLE_TEXTURE2D( _Splat2, sampler_linear_repeat, uv_Splat2 );
				float _LayerAlphaC780_g61186 = tex2DNode6_g61186.a;
				float2 uv_Splat3 = input.ase_texcoord7.xy * _Splat3_ST.xy + _Splat3_ST.zw;
				float4 tex2DNode11_g61186 = SAMPLE_TEXTURE2D( _Normal3, sampler_linear_repeat, uv_Splat3 );
				float _HeightD282_g61186 = tex2DNode11_g61186.a;
				float smoothstepResult862_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult940_g61186 = clamp( smoothstepResult862_g61186 , 0.001 , 0.999 );
				float _MaskD484_g61186 = tex2DNode5_g61186.a;
				float clampResult882_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_877_0_g61186 = ( clampResult940_g61186 * clampResult882_g61186 );
				float4 tex2DNode7_g61186 = SAMPLE_TEXTURE2D( _Splat3, sampler_linear_repeat, uv_Splat3 );
				float _LayerAlphaD781_g61186 = tex2DNode7_g61186.a;
				float4 weightedBlendVar887_g61186 = ControlFinal724_g61186;
				float weightedBlend887_g61186 = ( weightedBlendVar887_g61186.x*( temp_output_878_0_g61186 * _LayerAlphaA778_g61186 ) + weightedBlendVar887_g61186.y*( temp_output_875_0_g61186 * _LayerAlphaB779_g61186 ) + weightedBlendVar887_g61186.z*( temp_output_876_0_g61186 * _LayerAlphaC780_g61186 ) + weightedBlendVar887_g61186.w*( temp_output_877_0_g61186 * _LayerAlphaD781_g61186 ) );
				float4 weightedBlendVar888_g61186 = ControlFinal724_g61186;
				float weightedBlend888_g61186 = ( weightedBlendVar888_g61186.x*temp_output_878_0_g61186 + weightedBlendVar888_g61186.y*temp_output_875_0_g61186 + weightedBlendVar888_g61186.z*temp_output_876_0_g61186 + weightedBlendVar888_g61186.w*temp_output_877_0_g61186 );
				float FinalSmoothness897_g61186 = ( weightedBlend887_g61186 / max( weightedBlend888_g61186, 0.001 ) );
				float Smoothness237 = FinalSmoothness897_g61186;
				float3 temp_output_201_0 = ( _SpecularColor.rgb * Smoothness237 * _SecondarySpecularIntensity );
				float3 normalizeResult4_g61184 = normalize( ( ViewDirWS + SafeNormalize( _MainLightPosition.xyz ) ) );
				float smoothstepResult810_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult933_g61186 = clamp( smoothstepResult810_g61186 , 0.001 , 0.999 );
				float clampResult830_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_829_0_g61186 = ( clampResult933_g61186 * clampResult830_g61186 );
				float3 break635_g61186 = tex2DNode2_g61186.rgb;
				float2 appendResult655_g61186 = (float2(( ( break635_g61186.x * 2.0 ) - 1.0 ) , ( ( break635_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult656_g61186 = (float3(appendResult655_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break635_g61186.x * break635_g61186.x ) + ( break635_g61186.y * break635_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalA720_g61186 = appendResult656_g61186;
				float smoothstepResult812_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult934_g61186 = clamp( smoothstepResult812_g61186 , 0.001 , 0.999 );
				float clampResult832_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_826_0_g61186 = ( clampResult934_g61186 * clampResult832_g61186 );
				float3 break657_g61186 = tex2DNode1_g61186.rgb;
				float2 appendResult664_g61186 = (float2(( ( break657_g61186.x * 2.0 ) - 1.0 ) , ( ( break657_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult673_g61186 = (float3(appendResult664_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break657_g61186.x * break657_g61186.x ) + ( break657_g61186.y * break657_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalB721_g61186 = appendResult673_g61186;
				float smoothstepResult811_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult935_g61186 = clamp( smoothstepResult811_g61186 , 0.001 , 0.999 );
				float clampResult831_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_827_0_g61186 = ( clampResult935_g61186 * clampResult831_g61186 );
				float3 break676_g61186 = tex2DNode10_g61186.rgb;
				float2 appendResult683_g61186 = (float2(( ( break676_g61186.x * 2.0 ) - 1.0 ) , ( ( break676_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult692_g61186 = (float3(appendResult683_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break676_g61186.x * break676_g61186.x ) + ( break676_g61186.y * break676_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalC722_g61186 = appendResult692_g61186;
				float smoothstepResult813_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult936_g61186 = clamp( smoothstepResult813_g61186 , 0.001 , 0.999 );
				float clampResult833_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_828_0_g61186 = ( clampResult936_g61186 * clampResult833_g61186 );
				float3 break695_g61186 = tex2DNode11_g61186.rgb;
				float2 appendResult702_g61186 = (float2(( ( break695_g61186.x * 2.0 ) - 1.0 ) , ( ( break695_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult711_g61186 = (float3(appendResult702_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break695_g61186.x * break695_g61186.x ) + ( break695_g61186.y * break695_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalD723_g61186 = appendResult711_g61186;
				float4 weightedBlendVar840_g61186 = ControlFinal724_g61186;
				float3 weightedBlend840_g61186 = ( weightedBlendVar840_g61186.x*( temp_output_829_0_g61186 * _UnpackedNormalA720_g61186 ) + weightedBlendVar840_g61186.y*( temp_output_826_0_g61186 * _UnpackedNormalB721_g61186 ) + weightedBlendVar840_g61186.z*( temp_output_827_0_g61186 * _UnpackedNormalC722_g61186 ) + weightedBlendVar840_g61186.w*( temp_output_828_0_g61186 * _UnpackedNormalD723_g61186 ) );
				float4 weightedBlendVar841_g61186 = ControlFinal724_g61186;
				float weightedBlend841_g61186 = ( weightedBlendVar841_g61186.x*temp_output_829_0_g61186 + weightedBlendVar841_g61186.y*temp_output_826_0_g61186 + weightedBlendVar841_g61186.z*temp_output_827_0_g61186 + weightedBlendVar841_g61186.w*temp_output_828_0_g61186 );
				float3 FinalNormal765_g61186 = ( weightedBlend840_g61186 / max( weightedBlend841_g61186, 0.001 ) );
				float3 temp_output_61_0_g61186 = FinalNormal765_g61186;
				float3 Normal236 = temp_output_61_0_g61186;
				float3 tanToWorld0 = float3( TangentWS.x, BitangentWS.x, NormalWS.x );
				float3 tanToWorld1 = float3( TangentWS.y, BitangentWS.y, NormalWS.y );
				float3 tanToWorld2 = float3( TangentWS.z, BitangentWS.z, NormalWS.z );
				float3 tanNormal131 = Normal236;
				float3 worldNormal131 = normalize( float3( dot( tanToWorld0, tanNormal131 ), dot( tanToWorld1, tanNormal131 ), dot( tanToWorld2, tanNormal131 ) ) );
				float3 normalizeResult132 = normalize( worldNormal131 );
				float3 Normals227 = normalizeResult132;
				float dotResult185 = dot( normalizeResult4_g61184 , Normals227 );
				float temp_output_203_0 = ( 1.0 - _SecondarySmoothness );
				float3 temp_output_208_0 = saturate( ( saturate( (  (-1.0 + ( _SecondarySpecularSize - 0.0 ) * ( -0.5 - -1.0 ) / ( 1.0 - 0.0 ) ) + dotResult185 ) ) / ( ( 1.0 - temp_output_201_0 ) * temp_output_203_0 ) ) );
				float3 DirectSpecHighlights163 = ( (temp_output_201_0).xyz * temp_output_208_0 );
				float ase_lightIntensity = max( max( _MainLightColor.r, _MainLightColor.g ), _MainLightColor.b ) + 1e-7;
				float4 ase_lightColor = float4( _MainLightColor.rgb / ase_lightIntensity, ase_lightIntensity );
				float ase_lightAtten = 0;
				Light ase_mainLight = GetMainLight( ShadowCoord );
				ase_lightAtten = ase_mainLight.distanceAttenuation * ase_mainLight.shadowAttenuation;
				float3 bakedGI151 = ASEIndirectDiffuse( input, NormalWS, PositionWS, ViewDirWS );
				MixRealtimeAndBakedGI( ase_mainLight, NormalWS, bakedGI151, half4( 0, 0, 0, 0 ) );
				float4 HalfLambert154 = ( ( ase_lightColor * ase_lightAtten ) + float4( bakedGI151 , 0.0 ) );
				float4 SpecularHighlights228 = ( float4( DirectSpecHighlights163 , 0.0 ) * HalfLambert154 );
				#ifdef _ENABLESPECULARHIGHLIGHTS_ON
				float4 staticSwitch162 = SpecularHighlights228;
				#else
				float4 staticSwitch162 = float4( 0,0,0,0 );
				#endif
				float4 color136 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				#ifdef _ENABLECOLORTINT_ON
				float4 staticSwitch166 = _BaseTint;
				#else
				float4 staticSwitch166 = color136;
				#endif
				float smoothstepResult591_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult944_g61186 = clamp( smoothstepResult591_g61186 , 0.001 , 0.999 );
				float clampResult603_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_597_0_g61186 = ( clampResult944_g61186 * clampResult603_g61186 );
				float4 _LayerA287_g61186 = tex2DNode4_g61186;
				float smoothstepResult594_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult943_g61186 = clamp( smoothstepResult594_g61186 , 0.001 , 0.999 );
				float clampResult604_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_598_0_g61186 = ( clampResult943_g61186 * clampResult604_g61186 );
				float4 _LayerB300_g61186 = tex2DNode3_g61186;
				float smoothstepResult595_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult942_g61186 = clamp( smoothstepResult595_g61186 , 0.001 , 0.999 );
				float clampResult605_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_599_0_g61186 = ( clampResult942_g61186 * clampResult605_g61186 );
				float4 _LayerC301_g61186 = tex2DNode6_g61186;
				float smoothstepResult596_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult941_g61186 = clamp( smoothstepResult596_g61186 , 0.001 , 0.999 );
				float clampResult606_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_600_0_g61186 = ( clampResult941_g61186 * clampResult606_g61186 );
				float4 _LayerD302_g61186 = tex2DNode7_g61186;
				float4 weightedBlendVar619_g61186 = ControlFinal724_g61186;
				float4 weightedBlend619_g61186 = ( weightedBlendVar619_g61186.x*( temp_output_597_0_g61186 * _LayerA287_g61186 ) + weightedBlendVar619_g61186.y*( temp_output_598_0_g61186 * _LayerB300_g61186 ) + weightedBlendVar619_g61186.z*( temp_output_599_0_g61186 * _LayerC301_g61186 ) + weightedBlendVar619_g61186.w*( temp_output_600_0_g61186 * _LayerD302_g61186 ) );
				float4 weightedBlendVar620_g61186 = ControlFinal724_g61186;
				float weightedBlend620_g61186 = ( weightedBlendVar620_g61186.x*temp_output_597_0_g61186 + weightedBlendVar620_g61186.y*temp_output_598_0_g61186 + weightedBlendVar620_g61186.z*temp_output_599_0_g61186 + weightedBlendVar620_g61186.w*temp_output_600_0_g61186 );
				float4 FinalAlbedo479_g61186 = ( weightedBlend619_g61186 / max( weightedBlend620_g61186, 0.001 ) );
				float4 temp_output_60_0_g61186 = FinalAlbedo479_g61186;
				float4 localClipHoles100_g61186 = ( temp_output_60_0_g61186 );
				float2 uv_TerrainHolesTexture = input.ase_texcoord7.xy * _TerrainHolesTexture_ST.xy + _TerrainHolesTexture_ST.zw;
				float holeClipValue99_g61186 = SAMPLE_TEXTURE2D( _TerrainHolesTexture, sampler_TerrainHolesTexture, uv_TerrainHolesTexture ).r;
				float Hole100_g61186 = holeClipValue99_g61186;
				{
				#ifdef _ALPHATEST_ON
				clip(Hole100_g61186 == 0.0f ? -1 : 1);
				#endif
				}
				float4 Albedo235 = localClipHoles100_g61186;
				float dotResult144 = dot( Normals227 , SafeNormalize( _MainLightPosition.xyz ) );
				float RampScale158 = _RampScale;
				float RampOffset157 = _RampOffset;
				float CEL_Effect142 = saturate( (dotResult144*RampScale158 + RampOffset157) );
				float2 temp_cast_3 = (CEL_Effect142).xx;
				float3 WorldPosition288_g61187 = PositionWS;
				float3 WorldPosition305_g61187 = WorldPosition288_g61187;
				float2 ScreenUV286_g61187 = (ScreenPosNorm).xy;
				float2 ScreenUV305_g61187 = ScreenUV286_g61187;
				float3 WorldNormal281_g61187 = Normals227;
				float3 WorldNormal305_g61187 = WorldNormal281_g61187;
				half2 LightmapUV1_g61188 = (input.ase_texcoord8.xy*(unity_LightmapST).xy + (unity_LightmapST).zw);
				half4 localCalculateShadowMask1_g61188 = CalculateShadowMask1_g61188( LightmapUV1_g61188 );
				float4 ShadowMask360_g61187 = localCalculateShadowMask1_g61188;
				float4 ShadowMask305_g61187 = ShadowMask360_g61187;
				float3 localAdditionalLightsLambertMask17x305_g61187 = AdditionalLightsLambertMask17x( WorldPosition305_g61187 , ScreenUV305_g61187 , WorldNormal305_g61187 , ShadowMask305_g61187 );
				float3 saferPower177 = abs( saturate( localAdditionalLightsLambertMask17x305_g61187 ) );
				float3 temp_cast_6 = ( (0.001 + ( _AdditionalLightFalloff - 0.0 ) * ( 12.0 - 0.001 ) / ( 12.0 - 0.0 ) )).xxx;
				
				float3 SpecularTint195 = _SpecularColor.rgb;
				

				float3 BaseColor = ( staticSwitch162 + ( staticSwitch166 * ( ( ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, temp_cast_3 ) ) * HalfLambert154 ) + ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, (pow( saferPower177 , temp_cast_6 )* (0.001 + ( _AdditionalLightInfluence - 0.0 ) * ( 6.0 - 0.001 ) / ( 1.0 - 0.0 ) ) + RampOffset157).xy ) ) ) ) ).rgb;
				float3 Normal = Normal236;
				float3 Specular = ( _SpecularIntensity1 * SpecularTint195 * Smoothness237 );
				float Metallic = 0;
				float Smoothness = _Smoothness;
				float Occlusion = _Occlusion1;
				float3 Emission = 0;
				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif
				float3 BakedGI = 0;
				float3 RefractionColor = 1;
				float RefractionIndex = 1;
				float3 Transmission = 1;
				float3 Translucency = 1;

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _CLEARCOAT
					float CoatMask = 0;
					float CoatSmoothness = 0;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.viewDirectionWS = ViewDirWS;
				inputData.shadowCoord = ShadowCoord;

				#ifdef _NORMALMAP
						#if _NORMAL_DROPOFF_TS
							inputData.normalWS = TransformTangentToWorld(Normal, half3x3(TangentWS, BitangentWS, NormalWS));
						#elif _NORMAL_DROPOFF_OS
							inputData.normalWS = TransformObjectToWorldNormal(Normal);
						#elif _NORMAL_DROPOFF_WS
							inputData.normalWS = Normal;
						#endif
					inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
				#else
					inputData.normalWS = NormalWS;
				#endif

				#ifdef ASE_FOG
					inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.fogFactorAndVertexLight.x);
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
					inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				#endif

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float3 SH = SampleSH(inputData.normalWS.xyz);
				#else
					float3 SH = input.lightmapUVOrVertexSH.xyz;
				#endif

				#if defined(_SCREEN_SPACE_IRRADIANCE) && ( UNITY_VERSION >= 60030000 )
					#if ( UNITY_VERSION >= 60060000 )
						inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS));
					#else
						inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
					#endif
				#elif defined(DYNAMICLIGHTMAP_ON)
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, SH, inputData.normalWS);
					inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);
				#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
					inputData.bakedGI = SAMPLE_GI( SH, GetAbsolutePositionWS(inputData.positionWS),
						inputData.normalWS,
						inputData.viewDirectionWS,
						input.positionCS.xy,
						input.probeOcclusion,
						inputData.shadowMask );
				#else
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, SH, inputData.normalWS);
					inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);
				#endif

				#ifdef ASE_BAKEDGI
					inputData.bakedGI = BakedGI;
				#endif

				#if defined(DEBUG_DISPLAY)
					#if defined(DYNAMICLIGHTMAP_ON)
						inputData.dynamicLightmapUV = input.dynamicLightmapUV.xy;
					#endif
					#if defined(LIGHTMAP_ON)
						inputData.staticLightmapUV = input.lightmapUVOrVertexSH.xy;
					#else
						inputData.vertexSH = SH;
					#endif
					#if defined(USE_APV_PROBE_OCCLUSION)
						inputData.probeOcclusion = input.probeOcclusion;
					#endif
				#endif

				SurfaceData surfaceData;
				surfaceData.albedo              = BaseColor;
				surfaceData.metallic            = saturate(Metallic);
				surfaceData.specular            = Specular;
				surfaceData.smoothness          = saturate(Smoothness),
				surfaceData.occlusion           = Occlusion,
				surfaceData.emission            = Emission,
				surfaceData.alpha               = saturate(Alpha);
				surfaceData.normalTS            = Normal;
				surfaceData.clearCoatMask       = 0;
				surfaceData.clearCoatSmoothness = 1;

				#ifdef _CLEARCOAT
					surfaceData.clearCoatMask       = saturate(CoatMask);
					surfaceData.clearCoatSmoothness = saturate(CoatSmoothness);
				#endif

				#if defined(_DBUFFER)
					ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
				#endif

				#ifdef ASE_LIGHTING_SIMPLE
					half4 color = UniversalFragmentBlinnPhong( inputData, surfaceData);
				#else
					half4 color = UniversalFragmentPBR( inputData, surfaceData);
				#endif

				#ifdef ASE_TRANSMISSION
				{
					float shadow = _TransmissionShadow;

					#define SUM_LIGHT_TRANSMISSION(Light)\
						float3 atten = Light.color * Light.distanceAttenuation;\
						atten = lerp( atten, atten * Light.shadowAttenuation, shadow );\
						half3 transmission = max( 0, -dot( inputData.normalWS, Light.direction ) ) * atten * Transmission;\
						color.rgb += BaseColor * transmission;

					SUM_LIGHT_TRANSMISSION( GetMainLight( inputData.shadowCoord ) );

					#if defined(_ADDITIONAL_LIGHTS)
						uint meshRenderingLayers = GetMeshRenderingLayer();
						uint pixelLightCount = GetAdditionalLightsCount();
						#if USE_CLUSTER_LIGHT_LOOP
							[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
							{
								CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

								Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
								#ifdef _LIGHT_LAYERS
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								#endif
								{
									SUM_LIGHT_TRANSMISSION( light );
								}
							}
						#endif
						LIGHT_LOOP_BEGIN( pixelLightCount )
							Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
							#ifdef _LIGHT_LAYERS
							if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
							#endif
							{
								SUM_LIGHT_TRANSMISSION( light );
							}
						LIGHT_LOOP_END
					#endif
				}
				#endif

				#ifdef ASE_TRANSLUCENCY
				{
					float shadow = _TransShadow;
					float normal = _TransNormal;
					float scattering = _TransScattering;
					float direct = _TransDirect;
					float ambient = _TransAmbient;
					float strength = _TransStrength;

					#define SUM_LIGHT_TRANSLUCENCY(Light)\
						float3 atten = Light.color * Light.distanceAttenuation;\
						atten = lerp( atten, atten * Light.shadowAttenuation, shadow );\
						half3 lightDir = Light.direction + inputData.normalWS * normal;\
						half VdotL = pow( saturate( dot( inputData.viewDirectionWS, -lightDir ) ), scattering );\
						half3 translucency = atten * ( VdotL * direct + inputData.bakedGI * ambient ) * Translucency;\
						color.rgb += BaseColor * translucency * strength;

					SUM_LIGHT_TRANSLUCENCY( GetMainLight( inputData.shadowCoord ) );

					#if defined(_ADDITIONAL_LIGHTS)
						uint meshRenderingLayers = GetMeshRenderingLayer();
						uint pixelLightCount = GetAdditionalLightsCount();
						#if USE_CLUSTER_LIGHT_LOOP
							[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
							{
								CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

								Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
								#ifdef _LIGHT_LAYERS
								if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
								#endif
								{
									SUM_LIGHT_TRANSLUCENCY( light );
								}
							}
						#endif
						LIGHT_LOOP_BEGIN( pixelLightCount )
							Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
							#ifdef _LIGHT_LAYERS
							if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
							#endif
							{
								SUM_LIGHT_TRANSLUCENCY( light );
							}
						LIGHT_LOOP_END
					#endif
				}
				#endif

				#ifdef ASE_REFRACTION
					float4 projScreenPos = ScreenPos / ScreenPos.w;
					float3 refractionOffset = ( RefractionIndex - 1.0 ) * mul( UNITY_MATRIX_V, float4( NormalWS,0 ) ).xyz * ( 1.0 - dot( NormalWS, ViewDirWS ) );
					projScreenPos.xy += refractionOffset.xy;
					float3 refraction = SHADERGRAPH_SAMPLE_SCENE_COLOR( projScreenPos.xy ) * RefractionColor;
					color.rgb = lerp( refraction, color.rgb, color.a );
					color.a = 1;
				#endif

				#ifdef ASE_FINAL_COLOR_ALPHA_MULTIPLY
					color.rgb *= color.a;
				#endif

				#ifdef ASE_FOG
					#ifdef TERRAIN_SPLAT_ADDPASS
						color.rgb = MixFogColor(color.rgb, half3(0,0,0), inputData.fogCoord);
					#else
						color.rgb = MixFog(color.rgb, inputData.fogCoord);
					#endif
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif

				#if defined( ASE_OPAQUE_KEEP_ALPHA )
					return half4( color.rgb, color.a );
				#else
					return half4( color.rgb, OutputAlpha( color.a, isTransparent ) );
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }

			ZWrite On
			ZTest LEqual
			AlphaToMask Off
			ColorMask 0

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW // @diogo: removed _vertex for POM node

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_SHADOWCASTER

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			float3 _LightDirection;
			float3 _LightPosition;

			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input )
			{
				PackedVaryings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( output );

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				output.ase_texcoord1 = input.ase_texcoord;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				float3 positionWS = TransformObjectToWorld( input.positionOS.xyz );
				float3 normalWS = TransformObjectToWorldDir(input.normalOS);

				#if _CASTING_PUNCTUAL_LIGHT_SHADOW
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif

				float4 positionCS = ASE_ADJUST_CLIP_POSITION( TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)) );

				//code for UNITY_REVERSED_Z is moved into Shadows.hlsl from 6000.0.22 and or higher
				positionCS = ApplyShadowClamping(positionCS);

				output.positionCS = positionCS;
				output.positionWS = positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.ase_texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(	PackedVaryings input
						#if defined( ASE_WRITE_DEPTH )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				

				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					#if defined( _ALPHATEST_SHADOW_ON )
						AlphaDiscard( Alpha, AlphaClipThresholdShadow );
					#else
						AlphaDiscard( Alpha, AlphaClipThreshold );
					#endif
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthOnly"
			Tags { "LightMode"="DepthOnly" }

			ZWrite On
			ColorMask R
			AlphaToMask Off

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				output.ase_texcoord1 = input.ase_texcoord;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.ase_texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(	PackedVaryings input
						#if defined( ASE_WRITE_DEPTH )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );

				

				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				return 0;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "Meta"
			Tags { "LightMode"="Meta" }

			Cull Off

			HLSLPROGRAM
			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1

			#pragma shader_feature EDITOR_VISUALIZATION

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_META

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_WORLD_POSITION
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_VERT_TANGENT
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SHADOWCOORDS
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES1
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES2
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#pragma shader_feature_local _ENABLESPECULARHIGHLIGHTS_ON
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#if UNITY_VERSION >= 60010000
			#pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
			#endif
			#if UNITY_VERSION >= 60010000
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			#endif
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma shader_feature_local _ENABLECOLORTINT_ON
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				#ifdef EDITOR_VISUALIZATION
					float4 VizUV : TEXCOORD1;
					float4 LightCoord : TEXCOORD2;
				#endif
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 ase_texcoord5 : TEXCOORD5;
				float4 ase_texcoord6 : TEXCOORD6;
				float4 lightmapUVOrVertexSH : TEXCOORD7;
				float4 dynamicLightmapUV : TEXCOORD8;
				float4 ase_texcoord9 : TEXCOORD9;
				float4 ase_texcoord10 : TEXCOORD10;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			TEXTURE2D(_Normal0);
			TEXTURE2D(_Splat0);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Control);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal1);
			TEXTURE2D(_Splat1);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal2);
			TEXTURE2D(_Splat2);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal3);
			TEXTURE2D(_Splat3);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_TerrainHolesTexture);
			SAMPLER(sampler_TerrainHolesTexture);
			TEXTURE2D(_TextureRamp3);
			SAMPLER(sampler_TextureRamp3);
			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			half3 ASEIndirectDiffuse( PackedVaryings input, half3 normalWS, float3 positionWS, half3 viewDirWS )
			{
			#if defined( DYNAMICLIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, 0, normalWS );
			#elif defined( LIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, 0, normalWS );
			#elif defined( PROBE_VOLUMES_L1 ) || defined( PROBE_VOLUMES_L2 )
				return SampleProbeVolumePixel( SampleSH( normalWS ), positionWS, normalWS, viewDirWS, input.positionCS.xy );
			#else
				return SampleSH( normalWS );
			#endif
			}
			
			half4 CalculateShadowMask1_g61188( half2 LightmapUV )
			{
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
				return SAMPLE_SHADOWMASK( LightmapUV.xy );
				#elif !defined (LIGHTMAP_ON)
				return unity_ProbesOcclusion;
				#else
				return half4( 1, 1, 1, 1 );
				#endif
			}
			
			float3 AdditionalLightsLambertMask17x( float3 WorldPosition, float2 ScreenUV, float3 WorldNormal, float4 ShadowMask )
			{
				#if ( UNITY_VERSION < 60010000 ) && !defined( USE_CLUSTER_LIGHT_LOOP )
					#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
				#endif
				#if ( UNITY_VERSION < 60010000 ) && !defined( CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK )
					#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
				#endif
				float3 Color = 0;
				#if defined(_ADDITIONAL_LIGHTS)
					#if ( UNITY_VERSION >= 60010000 )
						#define CALC_LAMBERT(Light) LightingLambert( AttLightColor, Light.direction, WorldNormal )
					#else
						#define CALC_LAMBERT(Light) ( dot( Light.direction, WorldNormal ) * 0.5 + 0.5 )* AttLightColor
					#endif
					#define SUM_LIGHTLAMBERT(Light)\
						half3 AttLightColor = Light.color * ( Light.distanceAttenuation * Light.shadowAttenuation );\
						Color += CALC_LAMBERT(Light);
					InputData inputData = (InputData)0;
					inputData.normalizedScreenSpaceUV = ScreenUV;
					inputData.positionWS = WorldPosition;
					uint meshRenderingLayers = GetMeshRenderingLayer();
					uint pixelLightCount = GetAdditionalLightsCount();	
					#if USE_CLUSTER_LIGHT_LOOP
					[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					}
					#endif
					LIGHT_LOOP_BEGIN( pixelLightCount )
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					LIGHT_LOOP_END
				#endif
				return Color;
			}
			
			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.texcoord );
				#endif
				
				float2 break956_g61186 = _Control_ST.zw;
				float2 appendResult959_g61186 = (float2(( break956_g61186.x + 0.001 ) , ( break956_g61186.y + 0.0001 )));
				float2 vertexToFrag961_g61186 = ( ( input.texcoord.xy * _Control_ST.xy ) + appendResult959_g61186 );
				output.ase_texcoord3.zw = vertexToFrag961_g61186;
				float3 ase_tangentWS = TransformObjectToWorldDir( input.tangentOS.xyz );
				output.ase_texcoord4.xyz = ase_tangentWS;
				float3 ase_normalWS = TransformObjectToWorldNormal( input.normalOS );
				output.ase_texcoord5.xyz = ase_normalWS;
				float ase_tangentSign = input.tangentOS.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				output.ase_texcoord6.xyz = ase_bitangentWS;
				OUTPUT_LIGHTMAP_UV( input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy );
				float3 ase_positionWS = TransformObjectToWorld( ( input.positionOS ).xyz );
				#if !defined( OUTPUT_SH4 )
				OUTPUT_SH( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz );
				#elif UNITY_VERSION > 60000009
				OUTPUT_SH4( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz, output.probeOcclusion );
				#else
				OUTPUT_SH4( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz );
				#endif
				#if defined( DYNAMICLIGHTMAP_ON )
				output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord9 = screenPos;
				
				output.ase_texcoord3.xy = input.texcoord.xy;
				output.ase_texcoord10.xy = input.texcoord1.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord4.w = 0;
				output.ase_texcoord5.w = 0;
				output.ase_texcoord6.w = 0;
				output.ase_texcoord10.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				#ifdef EDITOR_VISUALIZATION
					float2 VizUV = 0;
					float4 LightCoord = 0;
					UnityEditorVizData(input.positionOS.xyz, input.texcoord.xy, input.texcoord1.xy, input.texcoord2.xy, VizUV, LightCoord);
					output.VizUV = float4(VizUV, 0, 0);
					output.LightCoord = LightCoord;
				#endif

				output.positionCS = MetaVertexPosition( input.positionOS, input.texcoord1.xy, input.texcoord1.xy, unity_LightmapST, unity_DynamicLightmapST );
				output.positionWS = TransformObjectToWorld( input.positionOS.xyz );
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				output.texcoord1 = input.texcoord1;
				output.texcoord2 = input.texcoord2;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;

				float4 ControlFinal724_g61186 = float4( 1, 1, 1, 1 );
				float2 uv_Splat0 = input.ase_texcoord3.xy * _Splat0_ST.xy + _Splat0_ST.zw;
				float4 tex2DNode2_g61186 = SAMPLE_TEXTURE2D( _Normal0, sampler_linear_repeat, uv_Splat0 );
				float _HeightA279_g61186 = tex2DNode2_g61186.a;
				float smoothstepResult859_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult937_g61186 = clamp( smoothstepResult859_g61186 , 0.001 , 0.999 );
				float2 vertexToFrag961_g61186 = input.ase_texcoord3.zw;
				float4 tex2DNode5_g61186 = SAMPLE_TEXTURE2D( _Control, sampler_linear_repeat, vertexToFrag961_g61186 );
				float _MaskA481_g61186 = tex2DNode5_g61186.r;
				float clampResult879_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_878_0_g61186 = ( clampResult937_g61186 * clampResult879_g61186 );
				float4 tex2DNode4_g61186 = SAMPLE_TEXTURE2D( _Splat0, sampler_linear_repeat, uv_Splat0 );
				float _LayerAlphaA778_g61186 = tex2DNode4_g61186.a;
				float2 uv_Splat1 = input.ase_texcoord3.xy * _Splat1_ST.xy + _Splat1_ST.zw;
				float4 tex2DNode1_g61186 = SAMPLE_TEXTURE2D( _Normal1, sampler_linear_repeat, uv_Splat1 );
				float _HeightB280_g61186 = tex2DNode1_g61186.a;
				float smoothstepResult861_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult938_g61186 = clamp( smoothstepResult861_g61186 , 0.001 , 0.999 );
				float _MaskB482_g61186 = tex2DNode5_g61186.g;
				float clampResult881_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_875_0_g61186 = ( clampResult938_g61186 * clampResult881_g61186 );
				float4 tex2DNode3_g61186 = SAMPLE_TEXTURE2D( _Splat1, sampler_linear_repeat, uv_Splat1 );
				float _LayerAlphaB779_g61186 = tex2DNode3_g61186.a;
				float2 uv_Splat2 = input.ase_texcoord3.xy * _Splat2_ST.xy + _Splat2_ST.zw;
				float4 tex2DNode10_g61186 = SAMPLE_TEXTURE2D( _Normal2, sampler_linear_repeat, uv_Splat2 );
				float _HeightC281_g61186 = tex2DNode10_g61186.a;
				float smoothstepResult860_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult939_g61186 = clamp( smoothstepResult860_g61186 , 0.001 , 0.999 );
				float _MaskC483_g61186 = tex2DNode5_g61186.b;
				float clampResult880_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_876_0_g61186 = ( clampResult939_g61186 * clampResult880_g61186 );
				float4 tex2DNode6_g61186 = SAMPLE_TEXTURE2D( _Splat2, sampler_linear_repeat, uv_Splat2 );
				float _LayerAlphaC780_g61186 = tex2DNode6_g61186.a;
				float2 uv_Splat3 = input.ase_texcoord3.xy * _Splat3_ST.xy + _Splat3_ST.zw;
				float4 tex2DNode11_g61186 = SAMPLE_TEXTURE2D( _Normal3, sampler_linear_repeat, uv_Splat3 );
				float _HeightD282_g61186 = tex2DNode11_g61186.a;
				float smoothstepResult862_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult940_g61186 = clamp( smoothstepResult862_g61186 , 0.001 , 0.999 );
				float _MaskD484_g61186 = tex2DNode5_g61186.a;
				float clampResult882_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_877_0_g61186 = ( clampResult940_g61186 * clampResult882_g61186 );
				float4 tex2DNode7_g61186 = SAMPLE_TEXTURE2D( _Splat3, sampler_linear_repeat, uv_Splat3 );
				float _LayerAlphaD781_g61186 = tex2DNode7_g61186.a;
				float4 weightedBlendVar887_g61186 = ControlFinal724_g61186;
				float weightedBlend887_g61186 = ( weightedBlendVar887_g61186.x*( temp_output_878_0_g61186 * _LayerAlphaA778_g61186 ) + weightedBlendVar887_g61186.y*( temp_output_875_0_g61186 * _LayerAlphaB779_g61186 ) + weightedBlendVar887_g61186.z*( temp_output_876_0_g61186 * _LayerAlphaC780_g61186 ) + weightedBlendVar887_g61186.w*( temp_output_877_0_g61186 * _LayerAlphaD781_g61186 ) );
				float4 weightedBlendVar888_g61186 = ControlFinal724_g61186;
				float weightedBlend888_g61186 = ( weightedBlendVar888_g61186.x*temp_output_878_0_g61186 + weightedBlendVar888_g61186.y*temp_output_875_0_g61186 + weightedBlendVar888_g61186.z*temp_output_876_0_g61186 + weightedBlendVar888_g61186.w*temp_output_877_0_g61186 );
				float FinalSmoothness897_g61186 = ( weightedBlend887_g61186 / max( weightedBlend888_g61186, 0.001 ) );
				float Smoothness237 = FinalSmoothness897_g61186;
				float3 temp_output_201_0 = ( _SpecularColor.rgb * Smoothness237 * _SecondarySpecularIntensity );
				float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - PositionWS : UNITY_MATRIX_V[ 2 ].xyz );
				float3 ase_viewDirWS = normalize( ase_viewVectorWS );
				float3 normalizeResult4_g61184 = normalize( ( ase_viewDirWS + SafeNormalize( _MainLightPosition.xyz ) ) );
				float smoothstepResult810_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult933_g61186 = clamp( smoothstepResult810_g61186 , 0.001 , 0.999 );
				float clampResult830_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_829_0_g61186 = ( clampResult933_g61186 * clampResult830_g61186 );
				float3 break635_g61186 = tex2DNode2_g61186.rgb;
				float2 appendResult655_g61186 = (float2(( ( break635_g61186.x * 2.0 ) - 1.0 ) , ( ( break635_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult656_g61186 = (float3(appendResult655_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break635_g61186.x * break635_g61186.x ) + ( break635_g61186.y * break635_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalA720_g61186 = appendResult656_g61186;
				float smoothstepResult812_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult934_g61186 = clamp( smoothstepResult812_g61186 , 0.001 , 0.999 );
				float clampResult832_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_826_0_g61186 = ( clampResult934_g61186 * clampResult832_g61186 );
				float3 break657_g61186 = tex2DNode1_g61186.rgb;
				float2 appendResult664_g61186 = (float2(( ( break657_g61186.x * 2.0 ) - 1.0 ) , ( ( break657_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult673_g61186 = (float3(appendResult664_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break657_g61186.x * break657_g61186.x ) + ( break657_g61186.y * break657_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalB721_g61186 = appendResult673_g61186;
				float smoothstepResult811_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult935_g61186 = clamp( smoothstepResult811_g61186 , 0.001 , 0.999 );
				float clampResult831_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_827_0_g61186 = ( clampResult935_g61186 * clampResult831_g61186 );
				float3 break676_g61186 = tex2DNode10_g61186.rgb;
				float2 appendResult683_g61186 = (float2(( ( break676_g61186.x * 2.0 ) - 1.0 ) , ( ( break676_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult692_g61186 = (float3(appendResult683_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break676_g61186.x * break676_g61186.x ) + ( break676_g61186.y * break676_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalC722_g61186 = appendResult692_g61186;
				float smoothstepResult813_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult936_g61186 = clamp( smoothstepResult813_g61186 , 0.001 , 0.999 );
				float clampResult833_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_828_0_g61186 = ( clampResult936_g61186 * clampResult833_g61186 );
				float3 break695_g61186 = tex2DNode11_g61186.rgb;
				float2 appendResult702_g61186 = (float2(( ( break695_g61186.x * 2.0 ) - 1.0 ) , ( ( break695_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult711_g61186 = (float3(appendResult702_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break695_g61186.x * break695_g61186.x ) + ( break695_g61186.y * break695_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalD723_g61186 = appendResult711_g61186;
				float4 weightedBlendVar840_g61186 = ControlFinal724_g61186;
				float3 weightedBlend840_g61186 = ( weightedBlendVar840_g61186.x*( temp_output_829_0_g61186 * _UnpackedNormalA720_g61186 ) + weightedBlendVar840_g61186.y*( temp_output_826_0_g61186 * _UnpackedNormalB721_g61186 ) + weightedBlendVar840_g61186.z*( temp_output_827_0_g61186 * _UnpackedNormalC722_g61186 ) + weightedBlendVar840_g61186.w*( temp_output_828_0_g61186 * _UnpackedNormalD723_g61186 ) );
				float4 weightedBlendVar841_g61186 = ControlFinal724_g61186;
				float weightedBlend841_g61186 = ( weightedBlendVar841_g61186.x*temp_output_829_0_g61186 + weightedBlendVar841_g61186.y*temp_output_826_0_g61186 + weightedBlendVar841_g61186.z*temp_output_827_0_g61186 + weightedBlendVar841_g61186.w*temp_output_828_0_g61186 );
				float3 FinalNormal765_g61186 = ( weightedBlend840_g61186 / max( weightedBlend841_g61186, 0.001 ) );
				float3 temp_output_61_0_g61186 = FinalNormal765_g61186;
				float3 Normal236 = temp_output_61_0_g61186;
				float3 ase_tangentWS = input.ase_texcoord4.xyz;
				float3 ase_normalWS = input.ase_texcoord5.xyz;
				float3 ase_bitangentWS = input.ase_texcoord6.xyz;
				float3 tanToWorld0 = float3( ase_tangentWS.x, ase_bitangentWS.x, ase_normalWS.x );
				float3 tanToWorld1 = float3( ase_tangentWS.y, ase_bitangentWS.y, ase_normalWS.y );
				float3 tanToWorld2 = float3( ase_tangentWS.z, ase_bitangentWS.z, ase_normalWS.z );
				float3 tanNormal131 = Normal236;
				float3 worldNormal131 = normalize( float3( dot( tanToWorld0, tanNormal131 ), dot( tanToWorld1, tanNormal131 ), dot( tanToWorld2, tanNormal131 ) ) );
				float3 normalizeResult132 = normalize( worldNormal131 );
				float3 Normals227 = normalizeResult132;
				float dotResult185 = dot( normalizeResult4_g61184 , Normals227 );
				float temp_output_203_0 = ( 1.0 - _SecondarySmoothness );
				float3 temp_output_208_0 = saturate( ( saturate( (  (-1.0 + ( _SecondarySpecularSize - 0.0 ) * ( -0.5 - -1.0 ) / ( 1.0 - 0.0 ) ) + dotResult185 ) ) / ( ( 1.0 - temp_output_201_0 ) * temp_output_203_0 ) ) );
				float3 DirectSpecHighlights163 = ( (temp_output_201_0).xyz * temp_output_208_0 );
				float ase_lightIntensity = max( max( _MainLightColor.r, _MainLightColor.g ), _MainLightColor.b ) + 1e-7;
				float4 ase_lightColor = float4( _MainLightColor.rgb / ase_lightIntensity, ase_lightIntensity );
				float ase_lightAtten = 0;
				Light ase_mainLight = GetMainLight( ShadowCoord );
				ase_lightAtten = ase_mainLight.distanceAttenuation * ase_mainLight.shadowAttenuation;
				float3 bakedGI151 = ASEIndirectDiffuse( input, ase_normalWS, PositionWS, ase_viewDirWS );
				MixRealtimeAndBakedGI( ase_mainLight, ase_normalWS, bakedGI151, half4( 0, 0, 0, 0 ) );
				float4 HalfLambert154 = ( ( ase_lightColor * ase_lightAtten ) + float4( bakedGI151 , 0.0 ) );
				float4 SpecularHighlights228 = ( float4( DirectSpecHighlights163 , 0.0 ) * HalfLambert154 );
				#ifdef _ENABLESPECULARHIGHLIGHTS_ON
				float4 staticSwitch162 = SpecularHighlights228;
				#else
				float4 staticSwitch162 = float4( 0,0,0,0 );
				#endif
				float4 color136 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				#ifdef _ENABLECOLORTINT_ON
				float4 staticSwitch166 = _BaseTint;
				#else
				float4 staticSwitch166 = color136;
				#endif
				float smoothstepResult591_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult944_g61186 = clamp( smoothstepResult591_g61186 , 0.001 , 0.999 );
				float clampResult603_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_597_0_g61186 = ( clampResult944_g61186 * clampResult603_g61186 );
				float4 _LayerA287_g61186 = tex2DNode4_g61186;
				float smoothstepResult594_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult943_g61186 = clamp( smoothstepResult594_g61186 , 0.001 , 0.999 );
				float clampResult604_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_598_0_g61186 = ( clampResult943_g61186 * clampResult604_g61186 );
				float4 _LayerB300_g61186 = tex2DNode3_g61186;
				float smoothstepResult595_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult942_g61186 = clamp( smoothstepResult595_g61186 , 0.001 , 0.999 );
				float clampResult605_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_599_0_g61186 = ( clampResult942_g61186 * clampResult605_g61186 );
				float4 _LayerC301_g61186 = tex2DNode6_g61186;
				float smoothstepResult596_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult941_g61186 = clamp( smoothstepResult596_g61186 , 0.001 , 0.999 );
				float clampResult606_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_600_0_g61186 = ( clampResult941_g61186 * clampResult606_g61186 );
				float4 _LayerD302_g61186 = tex2DNode7_g61186;
				float4 weightedBlendVar619_g61186 = ControlFinal724_g61186;
				float4 weightedBlend619_g61186 = ( weightedBlendVar619_g61186.x*( temp_output_597_0_g61186 * _LayerA287_g61186 ) + weightedBlendVar619_g61186.y*( temp_output_598_0_g61186 * _LayerB300_g61186 ) + weightedBlendVar619_g61186.z*( temp_output_599_0_g61186 * _LayerC301_g61186 ) + weightedBlendVar619_g61186.w*( temp_output_600_0_g61186 * _LayerD302_g61186 ) );
				float4 weightedBlendVar620_g61186 = ControlFinal724_g61186;
				float weightedBlend620_g61186 = ( weightedBlendVar620_g61186.x*temp_output_597_0_g61186 + weightedBlendVar620_g61186.y*temp_output_598_0_g61186 + weightedBlendVar620_g61186.z*temp_output_599_0_g61186 + weightedBlendVar620_g61186.w*temp_output_600_0_g61186 );
				float4 FinalAlbedo479_g61186 = ( weightedBlend619_g61186 / max( weightedBlend620_g61186, 0.001 ) );
				float4 temp_output_60_0_g61186 = FinalAlbedo479_g61186;
				float4 localClipHoles100_g61186 = ( temp_output_60_0_g61186 );
				float2 uv_TerrainHolesTexture = input.ase_texcoord3.xy * _TerrainHolesTexture_ST.xy + _TerrainHolesTexture_ST.zw;
				float holeClipValue99_g61186 = SAMPLE_TEXTURE2D( _TerrainHolesTexture, sampler_TerrainHolesTexture, uv_TerrainHolesTexture ).r;
				float Hole100_g61186 = holeClipValue99_g61186;
				{
				#ifdef _ALPHATEST_ON
				clip(Hole100_g61186 == 0.0f ? -1 : 1);
				#endif
				}
				float4 Albedo235 = localClipHoles100_g61186;
				float dotResult144 = dot( Normals227 , SafeNormalize( _MainLightPosition.xyz ) );
				float RampScale158 = _RampScale;
				float RampOffset157 = _RampOffset;
				float CEL_Effect142 = saturate( (dotResult144*RampScale158 + RampOffset157) );
				float2 temp_cast_3 = (CEL_Effect142).xx;
				float3 WorldPosition288_g61187 = PositionWS;
				float3 WorldPosition305_g61187 = WorldPosition288_g61187;
				float4 screenPos = input.ase_texcoord9;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float2 ScreenUV286_g61187 = (ase_positionSSNorm).xy;
				float2 ScreenUV305_g61187 = ScreenUV286_g61187;
				float3 WorldNormal281_g61187 = Normals227;
				float3 WorldNormal305_g61187 = WorldNormal281_g61187;
				half2 LightmapUV1_g61188 = (input.ase_texcoord10.xy*(unity_LightmapST).xy + (unity_LightmapST).zw);
				half4 localCalculateShadowMask1_g61188 = CalculateShadowMask1_g61188( LightmapUV1_g61188 );
				float4 ShadowMask360_g61187 = localCalculateShadowMask1_g61188;
				float4 ShadowMask305_g61187 = ShadowMask360_g61187;
				float3 localAdditionalLightsLambertMask17x305_g61187 = AdditionalLightsLambertMask17x( WorldPosition305_g61187 , ScreenUV305_g61187 , WorldNormal305_g61187 , ShadowMask305_g61187 );
				float3 saferPower177 = abs( saturate( localAdditionalLightsLambertMask17x305_g61187 ) );
				float3 temp_cast_6 = ( (0.001 + ( _AdditionalLightFalloff - 0.0 ) * ( 12.0 - 0.001 ) / ( 12.0 - 0.0 ) )).xxx;
				

				float3 BaseColor = ( staticSwitch162 + ( staticSwitch166 * ( ( ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, temp_cast_3 ) ) * HalfLambert154 ) + ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, (pow( saferPower177 , temp_cast_6 )* (0.001 + ( _AdditionalLightInfluence - 0.0 ) * ( 6.0 - 0.001 ) / ( 1.0 - 0.0 ) ) + RampOffset157).xy ) ) ) ) ).rgb;
				float3 Emission = 0;
				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				MetaInput metaInput = (MetaInput)0;
				metaInput.Albedo = BaseColor;
				metaInput.Emission = Emission;
				#ifdef EDITOR_VISUALIZATION
					metaInput.VizUV = input.VizUV.xy;
					metaInput.LightCoord = input.LightCoord;
				#endif

				return UnityMetaFragment(metaInput);
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "Universal2D"
			Tags { "LightMode"="Universal2D" }

			Blend One Zero, One Zero
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_2D

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_WORLD_POSITION
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_VERT_TANGENT
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_FRAG_SHADOWCOORDS
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#pragma shader_feature_local _ENABLESPECULARHIGHLIGHTS_ON
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#if UNITY_VERSION >= 60010000
			#pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
			#endif
			#if UNITY_VERSION >= 60010000
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
			#endif
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON
			#pragma shader_feature_local _ENABLECOLORTINT_ON
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				float4 lightmapUVOrVertexSH : TEXCOORD5;
				float4 dynamicLightmapUV : TEXCOORD6;
				float4 ase_texcoord7 : TEXCOORD7;
				float4 ase_texcoord8 : TEXCOORD8;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			TEXTURE2D(_Normal0);
			TEXTURE2D(_Splat0);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Control);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal1);
			TEXTURE2D(_Splat1);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal2);
			TEXTURE2D(_Splat2);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal3);
			TEXTURE2D(_Splat3);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_TerrainHolesTexture);
			SAMPLER(sampler_TerrainHolesTexture);
			TEXTURE2D(_TextureRamp3);
			SAMPLER(sampler_TextureRamp3);
			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			half3 ASEIndirectDiffuse( PackedVaryings input, half3 normalWS, float3 positionWS, half3 viewDirWS )
			{
			#if defined( DYNAMICLIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, 0, normalWS );
			#elif defined( LIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, 0, normalWS );
			#elif defined( PROBE_VOLUMES_L1 ) || defined( PROBE_VOLUMES_L2 )
				return SampleProbeVolumePixel( SampleSH( normalWS ), positionWS, normalWS, viewDirWS, input.positionCS.xy );
			#else
				return SampleSH( normalWS );
			#endif
			}
			
			half4 CalculateShadowMask1_g61188( half2 LightmapUV )
			{
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
				return SAMPLE_SHADOWMASK( LightmapUV.xy );
				#elif !defined (LIGHTMAP_ON)
				return unity_ProbesOcclusion;
				#else
				return half4( 1, 1, 1, 1 );
				#endif
			}
			
			float3 AdditionalLightsLambertMask17x( float3 WorldPosition, float2 ScreenUV, float3 WorldNormal, float4 ShadowMask )
			{
				#if ( UNITY_VERSION < 60010000 ) && !defined( USE_CLUSTER_LIGHT_LOOP )
					#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
				#endif
				#if ( UNITY_VERSION < 60010000 ) && !defined( CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK )
					#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
				#endif
				float3 Color = 0;
				#if defined(_ADDITIONAL_LIGHTS)
					#if ( UNITY_VERSION >= 60010000 )
						#define CALC_LAMBERT(Light) LightingLambert( AttLightColor, Light.direction, WorldNormal )
					#else
						#define CALC_LAMBERT(Light) ( dot( Light.direction, WorldNormal ) * 0.5 + 0.5 )* AttLightColor
					#endif
					#define SUM_LIGHTLAMBERT(Light)\
						half3 AttLightColor = Light.color * ( Light.distanceAttenuation * Light.shadowAttenuation );\
						Color += CALC_LAMBERT(Light);
					InputData inputData = (InputData)0;
					inputData.normalizedScreenSpaceUV = ScreenUV;
					inputData.positionWS = WorldPosition;
					uint meshRenderingLayers = GetMeshRenderingLayer();
					uint pixelLightCount = GetAdditionalLightsCount();	
					#if USE_CLUSTER_LIGHT_LOOP
					[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					}
					#endif
					LIGHT_LOOP_BEGIN( pixelLightCount )
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					LIGHT_LOOP_END
				#endif
				return Color;
			}
			
			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_TRANSFER_INSTANCE_ID( input, output );
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( output );

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				float2 break956_g61186 = _Control_ST.zw;
				float2 appendResult959_g61186 = (float2(( break956_g61186.x + 0.001 ) , ( break956_g61186.y + 0.0001 )));
				float2 vertexToFrag961_g61186 = ( ( input.ase_texcoord.xy * _Control_ST.xy ) + appendResult959_g61186 );
				output.ase_texcoord1.zw = vertexToFrag961_g61186;
				float3 ase_tangentWS = TransformObjectToWorldDir( input.tangentOS.xyz );
				output.ase_texcoord2.xyz = ase_tangentWS;
				float3 ase_normalWS = TransformObjectToWorldNormal( input.normalOS );
				output.ase_texcoord3.xyz = ase_normalWS;
				float ase_tangentSign = input.tangentOS.w * ( unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0 );
				float3 ase_bitangentWS = cross( ase_normalWS, ase_tangentWS ) * ase_tangentSign;
				output.ase_texcoord4.xyz = ase_bitangentWS;
				OUTPUT_LIGHTMAP_UV( input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy );
				float3 ase_positionWS = TransformObjectToWorld( ( input.positionOS ).xyz );
				#if !defined( OUTPUT_SH4 )
				OUTPUT_SH( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz );
				#elif UNITY_VERSION > 60000009
				OUTPUT_SH4( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz, output.probeOcclusion );
				#else
				OUTPUT_SH4( ase_positionWS, ase_normalWS, GetWorldSpaceNormalizeViewDir( ase_positionWS ), output.lightmapUVOrVertexSH.xyz );
				#endif
				#if defined( DYNAMICLIGHTMAP_ON )
				output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif
				float4 ase_positionCS = TransformObjectToHClip( ( input.positionOS ).xyz );
				float4 screenPos = ComputeScreenPos( ase_positionCS );
				output.ase_texcoord7 = screenPos;
				
				output.ase_texcoord1.xy = input.ase_texcoord.xy;
				output.ase_texcoord8.xy = input.texcoord1.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord2.w = 0;
				output.ase_texcoord3.w = 0;
				output.ase_texcoord4.w = 0;
				output.ase_texcoord8.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = vertexInput.positionCS;
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				float4 texcoord1 : TEXCOORD1;
				float4 texcoord2 : TEXCOORD2;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				output.texcoord1 = input.texcoord1;
				output.texcoord2 = input.texcoord2;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.ase_texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag(PackedVaryings input  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( input );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;

				float4 ControlFinal724_g61186 = float4( 1, 1, 1, 1 );
				float2 uv_Splat0 = input.ase_texcoord1.xy * _Splat0_ST.xy + _Splat0_ST.zw;
				float4 tex2DNode2_g61186 = SAMPLE_TEXTURE2D( _Normal0, sampler_linear_repeat, uv_Splat0 );
				float _HeightA279_g61186 = tex2DNode2_g61186.a;
				float smoothstepResult859_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult937_g61186 = clamp( smoothstepResult859_g61186 , 0.001 , 0.999 );
				float2 vertexToFrag961_g61186 = input.ase_texcoord1.zw;
				float4 tex2DNode5_g61186 = SAMPLE_TEXTURE2D( _Control, sampler_linear_repeat, vertexToFrag961_g61186 );
				float _MaskA481_g61186 = tex2DNode5_g61186.r;
				float clampResult879_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_878_0_g61186 = ( clampResult937_g61186 * clampResult879_g61186 );
				float4 tex2DNode4_g61186 = SAMPLE_TEXTURE2D( _Splat0, sampler_linear_repeat, uv_Splat0 );
				float _LayerAlphaA778_g61186 = tex2DNode4_g61186.a;
				float2 uv_Splat1 = input.ase_texcoord1.xy * _Splat1_ST.xy + _Splat1_ST.zw;
				float4 tex2DNode1_g61186 = SAMPLE_TEXTURE2D( _Normal1, sampler_linear_repeat, uv_Splat1 );
				float _HeightB280_g61186 = tex2DNode1_g61186.a;
				float smoothstepResult861_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult938_g61186 = clamp( smoothstepResult861_g61186 , 0.001 , 0.999 );
				float _MaskB482_g61186 = tex2DNode5_g61186.g;
				float clampResult881_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_875_0_g61186 = ( clampResult938_g61186 * clampResult881_g61186 );
				float4 tex2DNode3_g61186 = SAMPLE_TEXTURE2D( _Splat1, sampler_linear_repeat, uv_Splat1 );
				float _LayerAlphaB779_g61186 = tex2DNode3_g61186.a;
				float2 uv_Splat2 = input.ase_texcoord1.xy * _Splat2_ST.xy + _Splat2_ST.zw;
				float4 tex2DNode10_g61186 = SAMPLE_TEXTURE2D( _Normal2, sampler_linear_repeat, uv_Splat2 );
				float _HeightC281_g61186 = tex2DNode10_g61186.a;
				float smoothstepResult860_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult939_g61186 = clamp( smoothstepResult860_g61186 , 0.001 , 0.999 );
				float _MaskC483_g61186 = tex2DNode5_g61186.b;
				float clampResult880_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_876_0_g61186 = ( clampResult939_g61186 * clampResult880_g61186 );
				float4 tex2DNode6_g61186 = SAMPLE_TEXTURE2D( _Splat2, sampler_linear_repeat, uv_Splat2 );
				float _LayerAlphaC780_g61186 = tex2DNode6_g61186.a;
				float2 uv_Splat3 = input.ase_texcoord1.xy * _Splat3_ST.xy + _Splat3_ST.zw;
				float4 tex2DNode11_g61186 = SAMPLE_TEXTURE2D( _Normal3, sampler_linear_repeat, uv_Splat3 );
				float _HeightD282_g61186 = tex2DNode11_g61186.a;
				float smoothstepResult862_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult940_g61186 = clamp( smoothstepResult862_g61186 , 0.001 , 0.999 );
				float _MaskD484_g61186 = tex2DNode5_g61186.a;
				float clampResult882_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_877_0_g61186 = ( clampResult940_g61186 * clampResult882_g61186 );
				float4 tex2DNode7_g61186 = SAMPLE_TEXTURE2D( _Splat3, sampler_linear_repeat, uv_Splat3 );
				float _LayerAlphaD781_g61186 = tex2DNode7_g61186.a;
				float4 weightedBlendVar887_g61186 = ControlFinal724_g61186;
				float weightedBlend887_g61186 = ( weightedBlendVar887_g61186.x*( temp_output_878_0_g61186 * _LayerAlphaA778_g61186 ) + weightedBlendVar887_g61186.y*( temp_output_875_0_g61186 * _LayerAlphaB779_g61186 ) + weightedBlendVar887_g61186.z*( temp_output_876_0_g61186 * _LayerAlphaC780_g61186 ) + weightedBlendVar887_g61186.w*( temp_output_877_0_g61186 * _LayerAlphaD781_g61186 ) );
				float4 weightedBlendVar888_g61186 = ControlFinal724_g61186;
				float weightedBlend888_g61186 = ( weightedBlendVar888_g61186.x*temp_output_878_0_g61186 + weightedBlendVar888_g61186.y*temp_output_875_0_g61186 + weightedBlendVar888_g61186.z*temp_output_876_0_g61186 + weightedBlendVar888_g61186.w*temp_output_877_0_g61186 );
				float FinalSmoothness897_g61186 = ( weightedBlend887_g61186 / max( weightedBlend888_g61186, 0.001 ) );
				float Smoothness237 = FinalSmoothness897_g61186;
				float3 temp_output_201_0 = ( _SpecularColor.rgb * Smoothness237 * _SecondarySpecularIntensity );
				float3 ase_viewVectorWS = ( ( unity_OrthoParams.w == 0 ) ? _WorldSpaceCameraPos - PositionWS : UNITY_MATRIX_V[ 2 ].xyz );
				float3 ase_viewDirWS = normalize( ase_viewVectorWS );
				float3 normalizeResult4_g61184 = normalize( ( ase_viewDirWS + SafeNormalize( _MainLightPosition.xyz ) ) );
				float smoothstepResult810_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult933_g61186 = clamp( smoothstepResult810_g61186 , 0.001 , 0.999 );
				float clampResult830_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_829_0_g61186 = ( clampResult933_g61186 * clampResult830_g61186 );
				float3 break635_g61186 = tex2DNode2_g61186.rgb;
				float2 appendResult655_g61186 = (float2(( ( break635_g61186.x * 2.0 ) - 1.0 ) , ( ( break635_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult656_g61186 = (float3(appendResult655_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break635_g61186.x * break635_g61186.x ) + ( break635_g61186.y * break635_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalA720_g61186 = appendResult656_g61186;
				float smoothstepResult812_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult934_g61186 = clamp( smoothstepResult812_g61186 , 0.001 , 0.999 );
				float clampResult832_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_826_0_g61186 = ( clampResult934_g61186 * clampResult832_g61186 );
				float3 break657_g61186 = tex2DNode1_g61186.rgb;
				float2 appendResult664_g61186 = (float2(( ( break657_g61186.x * 2.0 ) - 1.0 ) , ( ( break657_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult673_g61186 = (float3(appendResult664_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break657_g61186.x * break657_g61186.x ) + ( break657_g61186.y * break657_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalB721_g61186 = appendResult673_g61186;
				float smoothstepResult811_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult935_g61186 = clamp( smoothstepResult811_g61186 , 0.001 , 0.999 );
				float clampResult831_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_827_0_g61186 = ( clampResult935_g61186 * clampResult831_g61186 );
				float3 break676_g61186 = tex2DNode10_g61186.rgb;
				float2 appendResult683_g61186 = (float2(( ( break676_g61186.x * 2.0 ) - 1.0 ) , ( ( break676_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult692_g61186 = (float3(appendResult683_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break676_g61186.x * break676_g61186.x ) + ( break676_g61186.y * break676_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalC722_g61186 = appendResult692_g61186;
				float smoothstepResult813_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult936_g61186 = clamp( smoothstepResult813_g61186 , 0.001 , 0.999 );
				float clampResult833_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_828_0_g61186 = ( clampResult936_g61186 * clampResult833_g61186 );
				float3 break695_g61186 = tex2DNode11_g61186.rgb;
				float2 appendResult702_g61186 = (float2(( ( break695_g61186.x * 2.0 ) - 1.0 ) , ( ( break695_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult711_g61186 = (float3(appendResult702_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break695_g61186.x * break695_g61186.x ) + ( break695_g61186.y * break695_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalD723_g61186 = appendResult711_g61186;
				float4 weightedBlendVar840_g61186 = ControlFinal724_g61186;
				float3 weightedBlend840_g61186 = ( weightedBlendVar840_g61186.x*( temp_output_829_0_g61186 * _UnpackedNormalA720_g61186 ) + weightedBlendVar840_g61186.y*( temp_output_826_0_g61186 * _UnpackedNormalB721_g61186 ) + weightedBlendVar840_g61186.z*( temp_output_827_0_g61186 * _UnpackedNormalC722_g61186 ) + weightedBlendVar840_g61186.w*( temp_output_828_0_g61186 * _UnpackedNormalD723_g61186 ) );
				float4 weightedBlendVar841_g61186 = ControlFinal724_g61186;
				float weightedBlend841_g61186 = ( weightedBlendVar841_g61186.x*temp_output_829_0_g61186 + weightedBlendVar841_g61186.y*temp_output_826_0_g61186 + weightedBlendVar841_g61186.z*temp_output_827_0_g61186 + weightedBlendVar841_g61186.w*temp_output_828_0_g61186 );
				float3 FinalNormal765_g61186 = ( weightedBlend840_g61186 / max( weightedBlend841_g61186, 0.001 ) );
				float3 temp_output_61_0_g61186 = FinalNormal765_g61186;
				float3 Normal236 = temp_output_61_0_g61186;
				float3 ase_tangentWS = input.ase_texcoord2.xyz;
				float3 ase_normalWS = input.ase_texcoord3.xyz;
				float3 ase_bitangentWS = input.ase_texcoord4.xyz;
				float3 tanToWorld0 = float3( ase_tangentWS.x, ase_bitangentWS.x, ase_normalWS.x );
				float3 tanToWorld1 = float3( ase_tangentWS.y, ase_bitangentWS.y, ase_normalWS.y );
				float3 tanToWorld2 = float3( ase_tangentWS.z, ase_bitangentWS.z, ase_normalWS.z );
				float3 tanNormal131 = Normal236;
				float3 worldNormal131 = normalize( float3( dot( tanToWorld0, tanNormal131 ), dot( tanToWorld1, tanNormal131 ), dot( tanToWorld2, tanNormal131 ) ) );
				float3 normalizeResult132 = normalize( worldNormal131 );
				float3 Normals227 = normalizeResult132;
				float dotResult185 = dot( normalizeResult4_g61184 , Normals227 );
				float temp_output_203_0 = ( 1.0 - _SecondarySmoothness );
				float3 temp_output_208_0 = saturate( ( saturate( (  (-1.0 + ( _SecondarySpecularSize - 0.0 ) * ( -0.5 - -1.0 ) / ( 1.0 - 0.0 ) ) + dotResult185 ) ) / ( ( 1.0 - temp_output_201_0 ) * temp_output_203_0 ) ) );
				float3 DirectSpecHighlights163 = ( (temp_output_201_0).xyz * temp_output_208_0 );
				float ase_lightIntensity = max( max( _MainLightColor.r, _MainLightColor.g ), _MainLightColor.b ) + 1e-7;
				float4 ase_lightColor = float4( _MainLightColor.rgb / ase_lightIntensity, ase_lightIntensity );
				float ase_lightAtten = 0;
				Light ase_mainLight = GetMainLight( ShadowCoord );
				ase_lightAtten = ase_mainLight.distanceAttenuation * ase_mainLight.shadowAttenuation;
				float3 bakedGI151 = ASEIndirectDiffuse( input, ase_normalWS, PositionWS, ase_viewDirWS );
				MixRealtimeAndBakedGI( ase_mainLight, ase_normalWS, bakedGI151, half4( 0, 0, 0, 0 ) );
				float4 HalfLambert154 = ( ( ase_lightColor * ase_lightAtten ) + float4( bakedGI151 , 0.0 ) );
				float4 SpecularHighlights228 = ( float4( DirectSpecHighlights163 , 0.0 ) * HalfLambert154 );
				#ifdef _ENABLESPECULARHIGHLIGHTS_ON
				float4 staticSwitch162 = SpecularHighlights228;
				#else
				float4 staticSwitch162 = float4( 0,0,0,0 );
				#endif
				float4 color136 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				#ifdef _ENABLECOLORTINT_ON
				float4 staticSwitch166 = _BaseTint;
				#else
				float4 staticSwitch166 = color136;
				#endif
				float smoothstepResult591_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult944_g61186 = clamp( smoothstepResult591_g61186 , 0.001 , 0.999 );
				float clampResult603_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_597_0_g61186 = ( clampResult944_g61186 * clampResult603_g61186 );
				float4 _LayerA287_g61186 = tex2DNode4_g61186;
				float smoothstepResult594_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult943_g61186 = clamp( smoothstepResult594_g61186 , 0.001 , 0.999 );
				float clampResult604_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_598_0_g61186 = ( clampResult943_g61186 * clampResult604_g61186 );
				float4 _LayerB300_g61186 = tex2DNode3_g61186;
				float smoothstepResult595_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult942_g61186 = clamp( smoothstepResult595_g61186 , 0.001 , 0.999 );
				float clampResult605_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_599_0_g61186 = ( clampResult942_g61186 * clampResult605_g61186 );
				float4 _LayerC301_g61186 = tex2DNode6_g61186;
				float smoothstepResult596_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult941_g61186 = clamp( smoothstepResult596_g61186 , 0.001 , 0.999 );
				float clampResult606_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_600_0_g61186 = ( clampResult941_g61186 * clampResult606_g61186 );
				float4 _LayerD302_g61186 = tex2DNode7_g61186;
				float4 weightedBlendVar619_g61186 = ControlFinal724_g61186;
				float4 weightedBlend619_g61186 = ( weightedBlendVar619_g61186.x*( temp_output_597_0_g61186 * _LayerA287_g61186 ) + weightedBlendVar619_g61186.y*( temp_output_598_0_g61186 * _LayerB300_g61186 ) + weightedBlendVar619_g61186.z*( temp_output_599_0_g61186 * _LayerC301_g61186 ) + weightedBlendVar619_g61186.w*( temp_output_600_0_g61186 * _LayerD302_g61186 ) );
				float4 weightedBlendVar620_g61186 = ControlFinal724_g61186;
				float weightedBlend620_g61186 = ( weightedBlendVar620_g61186.x*temp_output_597_0_g61186 + weightedBlendVar620_g61186.y*temp_output_598_0_g61186 + weightedBlendVar620_g61186.z*temp_output_599_0_g61186 + weightedBlendVar620_g61186.w*temp_output_600_0_g61186 );
				float4 FinalAlbedo479_g61186 = ( weightedBlend619_g61186 / max( weightedBlend620_g61186, 0.001 ) );
				float4 temp_output_60_0_g61186 = FinalAlbedo479_g61186;
				float4 localClipHoles100_g61186 = ( temp_output_60_0_g61186 );
				float2 uv_TerrainHolesTexture = input.ase_texcoord1.xy * _TerrainHolesTexture_ST.xy + _TerrainHolesTexture_ST.zw;
				float holeClipValue99_g61186 = SAMPLE_TEXTURE2D( _TerrainHolesTexture, sampler_TerrainHolesTexture, uv_TerrainHolesTexture ).r;
				float Hole100_g61186 = holeClipValue99_g61186;
				{
				#ifdef _ALPHATEST_ON
				clip(Hole100_g61186 == 0.0f ? -1 : 1);
				#endif
				}
				float4 Albedo235 = localClipHoles100_g61186;
				float dotResult144 = dot( Normals227 , SafeNormalize( _MainLightPosition.xyz ) );
				float RampScale158 = _RampScale;
				float RampOffset157 = _RampOffset;
				float CEL_Effect142 = saturate( (dotResult144*RampScale158 + RampOffset157) );
				float2 temp_cast_3 = (CEL_Effect142).xx;
				float3 WorldPosition288_g61187 = PositionWS;
				float3 WorldPosition305_g61187 = WorldPosition288_g61187;
				float4 screenPos = input.ase_texcoord7;
				float4 ase_positionSSNorm = screenPos / screenPos.w;
				ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
				float2 ScreenUV286_g61187 = (ase_positionSSNorm).xy;
				float2 ScreenUV305_g61187 = ScreenUV286_g61187;
				float3 WorldNormal281_g61187 = Normals227;
				float3 WorldNormal305_g61187 = WorldNormal281_g61187;
				half2 LightmapUV1_g61188 = (input.ase_texcoord8.xy*(unity_LightmapST).xy + (unity_LightmapST).zw);
				half4 localCalculateShadowMask1_g61188 = CalculateShadowMask1_g61188( LightmapUV1_g61188 );
				float4 ShadowMask360_g61187 = localCalculateShadowMask1_g61188;
				float4 ShadowMask305_g61187 = ShadowMask360_g61187;
				float3 localAdditionalLightsLambertMask17x305_g61187 = AdditionalLightsLambertMask17x( WorldPosition305_g61187 , ScreenUV305_g61187 , WorldNormal305_g61187 , ShadowMask305_g61187 );
				float3 saferPower177 = abs( saturate( localAdditionalLightsLambertMask17x305_g61187 ) );
				float3 temp_cast_6 = ( (0.001 + ( _AdditionalLightFalloff - 0.0 ) * ( 12.0 - 0.001 ) / ( 12.0 - 0.0 ) )).xxx;
				

				float3 BaseColor = ( staticSwitch162 + ( staticSwitch166 * ( ( ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, temp_cast_3 ) ) * HalfLambert154 ) + ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, (pow( saferPower177 , temp_cast_6 )* (0.001 + ( _AdditionalLightInfluence - 0.0 ) * ( 6.0 - 0.001 ) / ( 1.0 - 0.0 ) ) + RampOffset157).xy ) ) ) ) ).rgb;
				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				half4 color = half4(BaseColor, Alpha );

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				return color;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "DepthNormals"
			Tags { "LightMode"="DepthNormals" }

			ZWrite On
			Blend One Zero
			ZTest LEqual
			ZWrite On

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#pragma shader_feature _INSTANCEDTERRAINNORMALS_PIXEL
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_DEPTHNORMALSONLY
			//#define SHADERPASS SHADERPASS_DEPTHNORMALS

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				half4 texcoord : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			TEXTURE2D(_Normal0);
			TEXTURE2D(_Splat0);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Control);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal1);
			TEXTURE2D(_Splat1);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal2);
			TEXTURE2D(_Splat2);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal3);
			TEXTURE2D(_Splat3);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.texcoord );
				#endif
				
				float2 break956_g61186 = _Control_ST.zw;
				float2 appendResult959_g61186 = (float2(( break956_g61186.x + 0.001 ) , ( break956_g61186.y + 0.0001 )));
				float2 vertexToFrag961_g61186 = ( ( input.texcoord.xy * _Control_ST.xy ) + appendResult959_g61186 );
				output.ase_texcoord3.zw = vertexToFrag961_g61186;
				
				output.ase_texcoord3.xy = input.texcoord.xy;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			void frag(	PackedVaryings input
						, out half4 outNormalWS : SV_Target0
						#if defined( ASE_WRITE_DEPTH )
						,out float outputDepth : ASE_SV_DEPTH
						#endif
						#ifdef _WRITE_RENDERING_LAYERS
						#if ( UNITY_VERSION >= 60020000 )
						, out uint outRenderingLayers : SV_Target1
						#else
						, out float4 outRenderingLayers : SV_Target1
						#endif
						#endif
						 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_NEEDS_FRAG_SHADOWCOORDS)
					float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( input.positionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float4 ControlFinal724_g61186 = float4( 1, 1, 1, 1 );
				float2 uv_Splat0 = input.ase_texcoord3.xy * _Splat0_ST.xy + _Splat0_ST.zw;
				float4 tex2DNode2_g61186 = SAMPLE_TEXTURE2D( _Normal0, sampler_linear_repeat, uv_Splat0 );
				float _HeightA279_g61186 = tex2DNode2_g61186.a;
				float smoothstepResult810_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult933_g61186 = clamp( smoothstepResult810_g61186 , 0.001 , 0.999 );
				float2 vertexToFrag961_g61186 = input.ase_texcoord3.zw;
				float4 tex2DNode5_g61186 = SAMPLE_TEXTURE2D( _Control, sampler_linear_repeat, vertexToFrag961_g61186 );
				float _MaskA481_g61186 = tex2DNode5_g61186.r;
				float clampResult830_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_829_0_g61186 = ( clampResult933_g61186 * clampResult830_g61186 );
				float3 break635_g61186 = tex2DNode2_g61186.rgb;
				float2 appendResult655_g61186 = (float2(( ( break635_g61186.x * 2.0 ) - 1.0 ) , ( ( break635_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult656_g61186 = (float3(appendResult655_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break635_g61186.x * break635_g61186.x ) + ( break635_g61186.y * break635_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalA720_g61186 = appendResult656_g61186;
				float2 uv_Splat1 = input.ase_texcoord3.xy * _Splat1_ST.xy + _Splat1_ST.zw;
				float4 tex2DNode1_g61186 = SAMPLE_TEXTURE2D( _Normal1, sampler_linear_repeat, uv_Splat1 );
				float _HeightB280_g61186 = tex2DNode1_g61186.a;
				float smoothstepResult812_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult934_g61186 = clamp( smoothstepResult812_g61186 , 0.001 , 0.999 );
				float _MaskB482_g61186 = tex2DNode5_g61186.g;
				float clampResult832_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_826_0_g61186 = ( clampResult934_g61186 * clampResult832_g61186 );
				float3 break657_g61186 = tex2DNode1_g61186.rgb;
				float2 appendResult664_g61186 = (float2(( ( break657_g61186.x * 2.0 ) - 1.0 ) , ( ( break657_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult673_g61186 = (float3(appendResult664_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break657_g61186.x * break657_g61186.x ) + ( break657_g61186.y * break657_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalB721_g61186 = appendResult673_g61186;
				float2 uv_Splat2 = input.ase_texcoord3.xy * _Splat2_ST.xy + _Splat2_ST.zw;
				float4 tex2DNode10_g61186 = SAMPLE_TEXTURE2D( _Normal2, sampler_linear_repeat, uv_Splat2 );
				float _HeightC281_g61186 = tex2DNode10_g61186.a;
				float smoothstepResult811_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult935_g61186 = clamp( smoothstepResult811_g61186 , 0.001 , 0.999 );
				float _MaskC483_g61186 = tex2DNode5_g61186.b;
				float clampResult831_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_827_0_g61186 = ( clampResult935_g61186 * clampResult831_g61186 );
				float3 break676_g61186 = tex2DNode10_g61186.rgb;
				float2 appendResult683_g61186 = (float2(( ( break676_g61186.x * 2.0 ) - 1.0 ) , ( ( break676_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult692_g61186 = (float3(appendResult683_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break676_g61186.x * break676_g61186.x ) + ( break676_g61186.y * break676_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalC722_g61186 = appendResult692_g61186;
				float2 uv_Splat3 = input.ase_texcoord3.xy * _Splat3_ST.xy + _Splat3_ST.zw;
				float4 tex2DNode11_g61186 = SAMPLE_TEXTURE2D( _Normal3, sampler_linear_repeat, uv_Splat3 );
				float _HeightD282_g61186 = tex2DNode11_g61186.a;
				float smoothstepResult813_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult936_g61186 = clamp( smoothstepResult813_g61186 , 0.001 , 0.999 );
				float _MaskD484_g61186 = tex2DNode5_g61186.a;
				float clampResult833_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_828_0_g61186 = ( clampResult936_g61186 * clampResult833_g61186 );
				float3 break695_g61186 = tex2DNode11_g61186.rgb;
				float2 appendResult702_g61186 = (float2(( ( break695_g61186.x * 2.0 ) - 1.0 ) , ( ( break695_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult711_g61186 = (float3(appendResult702_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break695_g61186.x * break695_g61186.x ) + ( break695_g61186.y * break695_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalD723_g61186 = appendResult711_g61186;
				float4 weightedBlendVar840_g61186 = ControlFinal724_g61186;
				float3 weightedBlend840_g61186 = ( weightedBlendVar840_g61186.x*( temp_output_829_0_g61186 * _UnpackedNormalA720_g61186 ) + weightedBlendVar840_g61186.y*( temp_output_826_0_g61186 * _UnpackedNormalB721_g61186 ) + weightedBlendVar840_g61186.z*( temp_output_827_0_g61186 * _UnpackedNormalC722_g61186 ) + weightedBlendVar840_g61186.w*( temp_output_828_0_g61186 * _UnpackedNormalD723_g61186 ) );
				float4 weightedBlendVar841_g61186 = ControlFinal724_g61186;
				float weightedBlend841_g61186 = ( weightedBlendVar841_g61186.x*temp_output_829_0_g61186 + weightedBlendVar841_g61186.y*temp_output_826_0_g61186 + weightedBlendVar841_g61186.z*temp_output_827_0_g61186 + weightedBlendVar841_g61186.w*temp_output_828_0_g61186 );
				float3 FinalNormal765_g61186 = ( weightedBlend840_g61186 / max( weightedBlend841_g61186, 0.001 ) );
				float3 temp_output_61_0_g61186 = FinalNormal765_g61186;
				float3 Normal236 = temp_output_61_0_g61186;
				

				float3 Normal = Normal236;
				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				#if defined(_GBUFFER_NORMALS_OCT)
					float2 octNormalWS = PackNormalOctQuadEncode(NormalWS);
					float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
					half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
					outNormalWS = half4(packedNormalWS, 0.0);
				#else
					#if defined(_NORMALMAP)
						#if _NORMAL_DROPOFF_TS
							float3 normalWS = TransformTangentToWorld(Normal, half3x3(TangentWS, BitangentWS, NormalWS));
						#elif _NORMAL_DROPOFF_OS
							float3 normalWS = TransformObjectToWorldNormal(Normal);
						#elif _NORMAL_DROPOFF_WS
							float3 normalWS = Normal;
						#endif
					#else
						float3 normalWS = NormalWS;
					#endif
					outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
				#endif

				#ifdef _WRITE_RENDERING_LAYERS
					#if ( UNITY_VERSION >= 60020000 )
					outRenderingLayers = EncodeMeshRenderingLayer();
					#else
					uint renderingLayers = GetMeshRenderingLayer();
					outRenderingLayers = float4( EncodeMeshRenderingLayer( renderingLayers ), 0, 0, 0 );
					#endif
				#endif
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "GBuffer"
			Tags { "LightMode"="UniversalGBuffer" }

			Blend One Zero, One Zero
			ZWrite On
			ZTest LEqual
			Offset 0 , 0
			ColorMask RGBA
			

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#pragma shader_feature_local_fragment _RECEIVE_SHADOWS_OFF
			#pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#pragma shader_feature _INSTANCEDTERRAINNORMALS_PIXEL
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			// Deferred Rendering Path does not support the OpenGL-based graphics API:
			// Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
			#pragma exclude_renderers glcore gles3 webgpu 

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#if ( UNITY_VERSION >= 60000058 )
			#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
			#endif
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
			#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
			#if ( UNITY_VERSION >= 60030000 )
			#pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
			#endif
			#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			#pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
			#if ( UNITY_VERSION >= 60010000 )
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#endif

			#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#pragma multi_compile _ SHADOWS_SHADOWMASK
			#pragma multi_compile _ DIRLIGHTMAP_COMBINED
			#pragma multi_compile _ USE_LEGACY_LIGHTMAPS
			#pragma multi_compile _ LIGHTMAP_ON
			#if ( UNITY_VERSION >= 60010000 )
			#pragma multi_compile _ LIGHTMAP_BICUBIC_SAMPLING
			#endif
			#if ( UNITY_VERSION >= 60030000 )
			#pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
			#endif
			#pragma multi_compile _ DYNAMICLIGHTMAP_ON

			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SHADERPASS SHADERPASS_GBUFFER

			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if ( UNITY_VERSION >= 60030016 && UNITY_VERSION < 60040000 ) || ( UNITY_VERSION >= 60040010 )
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"
			#endif

			#if defined(LOD_FADE_CROSSFADE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

			#if defined( UNITY_INSTANCING_ENABLED ) && defined( ASE_INSTANCED_TERRAIN ) && ( defined(_TERRAIN_INSTANCED_PERPIXEL_NORMAL) || defined(_INSTANCEDTERRAINNORMALS_PIXEL) )
				#define ENABLE_TERRAIN_PERPIXEL_NORMAL
			#endif

			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES0
			#define ASE_NEEDS_FRAG_WORLD_VIEW_DIR
			#define ASE_NEEDS_WORLD_TANGENT
			#define ASE_NEEDS_FRAG_WORLD_TANGENT
			#define ASE_NEEDS_WORLD_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_NORMAL
			#define ASE_NEEDS_FRAG_WORLD_BITANGENT
			#define ASE_NEEDS_WORLD_POSITION
			#define ASE_NEEDS_FRAG_WORLD_POSITION
			#define ASE_NEEDS_FRAG_SHADOWCOORDS
			#define ASE_NEEDS_VERT_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
			#define ASE_NEEDS_TEXTURE_COORDINATES1
			#define ASE_NEEDS_FRAG_TEXTURE_COORDINATES1
			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#pragma shader_feature_local _ENABLESPECULARHIGHLIGHTS_ON
			#pragma shader_feature_local _ENABLECOLORTINT_ON
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				float4 tangentWS : TEXCOORD2; // holds terrainUV ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
				float4 lightmapUVOrVertexSH : TEXCOORD3;
				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					half4 fogFactorAndVertexLight : TEXCOORD4;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON)
					float2 dynamicLightmapUV : TEXCOORD5;
				#endif
				#if defined(USE_APV_PROBE_OCCLUSION)
					float4 probeOcclusion : TEXCOORD6;
				#endif
				float4 ase_texcoord7 : TEXCOORD7;
				float4 ase_texcoord8 : TEXCOORD8;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			TEXTURE2D(_Normal0);
			TEXTURE2D(_Splat0);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Control);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal1);
			TEXTURE2D(_Splat1);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal2);
			TEXTURE2D(_Splat2);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_Normal3);
			TEXTURE2D(_Splat3);

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif

#ifndef TSI_URP17_SHARED_SAMPLER
#define TSI_URP17_SHARED_SAMPLER
			SAMPLER(sampler_linear_repeat);
#endif
			TEXTURE2D(_TerrainHolesTexture);
			SAMPLER(sampler_TerrainHolesTexture);
			TEXTURE2D(_TextureRamp3);
			SAMPLER(sampler_TextureRamp3);
			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			#if ( UNITY_VERSION >= 60010000 )
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutput.hlsl"
			#else
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
			#endif

			half3 ASEIndirectDiffuse( PackedVaryings input, half3 normalWS, float3 positionWS, half3 viewDirWS )
			{
			#if defined( DYNAMICLIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, 0, normalWS );
			#elif defined( LIGHTMAP_ON )
				return SAMPLE_GI( input.lightmapUVOrVertexSH.xy, 0, normalWS );
			#elif defined( PROBE_VOLUMES_L1 ) || defined( PROBE_VOLUMES_L2 )
				return SampleProbeVolumePixel( SampleSH( normalWS ), positionWS, normalWS, viewDirWS, input.positionCS.xy );
			#else
				return SampleSH( normalWS );
			#endif
			}
			
			half4 CalculateShadowMask1_g61188( half2 LightmapUV )
			{
				#if defined(SHADOWS_SHADOWMASK) && defined(LIGHTMAP_ON)
				return SAMPLE_SHADOWMASK( LightmapUV.xy );
				#elif !defined (LIGHTMAP_ON)
				return unity_ProbesOcclusion;
				#else
				return half4( 1, 1, 1, 1 );
				#endif
			}
			
			float3 AdditionalLightsLambertMask17x( float3 WorldPosition, float2 ScreenUV, float3 WorldNormal, float4 ShadowMask )
			{
				#if ( UNITY_VERSION < 60010000 ) && !defined( USE_CLUSTER_LIGHT_LOOP )
					#define USE_CLUSTER_LIGHT_LOOP USE_FORWARD_PLUS
				#endif
				#if ( UNITY_VERSION < 60010000 ) && !defined( CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK )
					#define CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
				#endif
				float3 Color = 0;
				#if defined(_ADDITIONAL_LIGHTS)
					#if ( UNITY_VERSION >= 60010000 )
						#define CALC_LAMBERT(Light) LightingLambert( AttLightColor, Light.direction, WorldNormal )
					#else
						#define CALC_LAMBERT(Light) ( dot( Light.direction, WorldNormal ) * 0.5 + 0.5 )* AttLightColor
					#endif
					#define SUM_LIGHTLAMBERT(Light)\
						half3 AttLightColor = Light.color * ( Light.distanceAttenuation * Light.shadowAttenuation );\
						Color += CALC_LAMBERT(Light);
					InputData inputData = (InputData)0;
					inputData.normalizedScreenSpaceUV = ScreenUV;
					inputData.positionWS = WorldPosition;
					uint meshRenderingLayers = GetMeshRenderingLayer();
					uint pixelLightCount = GetAdditionalLightsCount();	
					#if USE_CLUSTER_LIGHT_LOOP
					[loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
					{
						CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					}
					#endif
					LIGHT_LOOP_BEGIN( pixelLightCount )
						Light light = GetAdditionalLight(lightIndex, WorldPosition, ShadowMask);
						#ifdef _LIGHT_LAYERS
						if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
						#endif
						{
							SUM_LIGHTLAMBERT( light );
						}
					LIGHT_LOOP_END
				#endif
				return Color;
			}
			
			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.texcoord );
				#endif
				
				float2 break956_g61186 = _Control_ST.zw;
				float2 appendResult959_g61186 = (float2(( break956_g61186.x + 0.001 ) , ( break956_g61186.y + 0.0001 )));
				float2 vertexToFrag961_g61186 = ( ( input.texcoord.xy * _Control_ST.xy ) + appendResult959_g61186 );
				output.ase_texcoord7.zw = vertexToFrag961_g61186;
				
				output.ase_texcoord7.xy = input.texcoord.xy;
				output.ase_texcoord8.xy = input.texcoord1.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				output.ase_texcoord8.zw = 0;
				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;
				input.tangentOS = input.tangentOS;

				#ifdef ASE_CUSTOM_MOTION_VECTOR
					// Declared so the Motion Vector output port surfaces on the master node; only consumed by the motion vector passes.
					float3 aseCustomMotionVector = float3(0, 0, 0);
				#endif

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );
				VertexNormalInputs normalInput = GetVertexNormalInputs( input.normalOS, input.tangentOS );

				OUTPUT_LIGHTMAP_UV(input.texcoord1, unity_LightmapST, output.lightmapUVOrVertexSH.xy);
				#if defined(DYNAMICLIGHTMAP_ON)
					output.dynamicLightmapUV.xy = input.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
				#endif
				OUTPUT_SH4(vertexInput.positionWS, normalInput.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.lightmapUVOrVertexSH.xyz, output.probeOcclusion);

				#if defined(ASE_FOG) || defined(_ADDITIONAL_LIGHTS_VERTEX)
					output.fogFactorAndVertexLight = 0;
					#if defined(ASE_FOG) && !defined(_FOG_FRAGMENT)
						// @diogo: no fog applied in GBuffer
					#endif
					#ifdef _ADDITIONAL_LIGHTS_VERTEX
						half3 vertexLight = VertexLighting( vertexInput.positionWS, normalInput.normalWS );
						output.fogFactorAndVertexLight.yzw = vertexLight;
					#endif
				#endif

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				output.normalWS = normalInput.normalWS;
				output.tangentWS = float4( normalInput.tangentWS, ( input.tangentOS.w > 0.0 ? 1.0 : -1.0 ) * GetOddNegativeScale() );

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					output.tangentWS.zw = input.texcoord.xy;
					output.tangentWS.xy = input.texcoord.xy * unity_LightmapST.xy + unity_LightmapST.zw;
				#endif
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 texcoord : TEXCOORD0;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					float4 texcoord1 : TEXCOORD1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					float4 texcoord2 : TEXCOORD2;
				#endif
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.texcoord = input.texcoord;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = input.texcoord1;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = input.texcoord2;
				#endif
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.texcoord = patch[0].texcoord * bary.x + patch[1].texcoord * bary.y + patch[2].texcoord * bary.z;
				#if defined(LIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES1)
					output.texcoord1 = patch[0].texcoord1 * bary.x + patch[1].texcoord1 * bary.y + patch[2].texcoord1 * bary.z;
				#endif
				#if defined(DYNAMICLIGHTMAP_ON) || defined(ASE_NEEDS_TEXTURE_COORDINATES2)
					output.texcoord2 = patch[0].texcoord2 * bary.x + patch[1].texcoord2 * bary.y + patch[2].texcoord2 * bary.z;
				#endif
				
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

		#if ( UNITY_VERSION >= 60010000 )
			GBufferFragOutput frag ( PackedVaryings input
		#else
			FragmentOutput frag ( PackedVaryings input
		#endif
								#if defined( ASE_WRITE_DEPTH )
								,out float outputDepth : ASE_SV_DEPTH
								#endif
								 )
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					float4 shadowCoord = TransformWorldToShadowCoord( input.positionWS );
				#else
					float4 shadowCoord = float4(0, 0, 0, 0);
				#endif

				// @diogo: mikktspace compliant
				float renormFactor = 1.0 / max( FLT_MIN, length( input.normalWS ) );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float3 ViewDirWS = GetWorldSpaceNormalizeViewDir( PositionWS );
				float4 ShadowCoord = shadowCoord;
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;
				float4 ScreenPos = ComputeScreenPos( ClipPos );
				float3 TangentWS = input.tangentWS.xyz * renormFactor;
				float3 BitangentWS = cross( input.normalWS, input.tangentWS.xyz ) * input.tangentWS.w * renormFactor;
				float3 NormalWS = input.normalWS * renormFactor;

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float2 sampleCoords = (input.tangentWS.zw / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
					NormalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
					TangentWS = -cross(GetObjectToWorldMatrix()._13_23_33, NormalWS);
					BitangentWS = cross(NormalWS, -TangentWS);
				#endif

				float4 ControlFinal724_g61186 = float4( 1, 1, 1, 1 );
				float2 uv_Splat0 = input.ase_texcoord7.xy * _Splat0_ST.xy + _Splat0_ST.zw;
				float4 tex2DNode2_g61186 = SAMPLE_TEXTURE2D( _Normal0, sampler_linear_repeat, uv_Splat0 );
				float _HeightA279_g61186 = tex2DNode2_g61186.a;
				float smoothstepResult859_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult937_g61186 = clamp( smoothstepResult859_g61186 , 0.001 , 0.999 );
				float2 vertexToFrag961_g61186 = input.ase_texcoord7.zw;
				float4 tex2DNode5_g61186 = SAMPLE_TEXTURE2D( _Control, sampler_linear_repeat, vertexToFrag961_g61186 );
				float _MaskA481_g61186 = tex2DNode5_g61186.r;
				float clampResult879_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_878_0_g61186 = ( clampResult937_g61186 * clampResult879_g61186 );
				float4 tex2DNode4_g61186 = SAMPLE_TEXTURE2D( _Splat0, sampler_linear_repeat, uv_Splat0 );
				float _LayerAlphaA778_g61186 = tex2DNode4_g61186.a;
				float2 uv_Splat1 = input.ase_texcoord7.xy * _Splat1_ST.xy + _Splat1_ST.zw;
				float4 tex2DNode1_g61186 = SAMPLE_TEXTURE2D( _Normal1, sampler_linear_repeat, uv_Splat1 );
				float _HeightB280_g61186 = tex2DNode1_g61186.a;
				float smoothstepResult861_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult938_g61186 = clamp( smoothstepResult861_g61186 , 0.001 , 0.999 );
				float _MaskB482_g61186 = tex2DNode5_g61186.g;
				float clampResult881_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_875_0_g61186 = ( clampResult938_g61186 * clampResult881_g61186 );
				float4 tex2DNode3_g61186 = SAMPLE_TEXTURE2D( _Splat1, sampler_linear_repeat, uv_Splat1 );
				float _LayerAlphaB779_g61186 = tex2DNode3_g61186.a;
				float2 uv_Splat2 = input.ase_texcoord7.xy * _Splat2_ST.xy + _Splat2_ST.zw;
				float4 tex2DNode10_g61186 = SAMPLE_TEXTURE2D( _Normal2, sampler_linear_repeat, uv_Splat2 );
				float _HeightC281_g61186 = tex2DNode10_g61186.a;
				float smoothstepResult860_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult939_g61186 = clamp( smoothstepResult860_g61186 , 0.001 , 0.999 );
				float _MaskC483_g61186 = tex2DNode5_g61186.b;
				float clampResult880_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_876_0_g61186 = ( clampResult939_g61186 * clampResult880_g61186 );
				float4 tex2DNode6_g61186 = SAMPLE_TEXTURE2D( _Splat2, sampler_linear_repeat, uv_Splat2 );
				float _LayerAlphaC780_g61186 = tex2DNode6_g61186.a;
				float2 uv_Splat3 = input.ase_texcoord7.xy * _Splat3_ST.xy + _Splat3_ST.zw;
				float4 tex2DNode11_g61186 = SAMPLE_TEXTURE2D( _Normal3, sampler_linear_repeat, uv_Splat3 );
				float _HeightD282_g61186 = tex2DNode11_g61186.a;
				float smoothstepResult862_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult940_g61186 = clamp( smoothstepResult862_g61186 , 0.001 , 0.999 );
				float _MaskD484_g61186 = tex2DNode5_g61186.a;
				float clampResult882_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_877_0_g61186 = ( clampResult940_g61186 * clampResult882_g61186 );
				float4 tex2DNode7_g61186 = SAMPLE_TEXTURE2D( _Splat3, sampler_linear_repeat, uv_Splat3 );
				float _LayerAlphaD781_g61186 = tex2DNode7_g61186.a;
				float4 weightedBlendVar887_g61186 = ControlFinal724_g61186;
				float weightedBlend887_g61186 = ( weightedBlendVar887_g61186.x*( temp_output_878_0_g61186 * _LayerAlphaA778_g61186 ) + weightedBlendVar887_g61186.y*( temp_output_875_0_g61186 * _LayerAlphaB779_g61186 ) + weightedBlendVar887_g61186.z*( temp_output_876_0_g61186 * _LayerAlphaC780_g61186 ) + weightedBlendVar887_g61186.w*( temp_output_877_0_g61186 * _LayerAlphaD781_g61186 ) );
				float4 weightedBlendVar888_g61186 = ControlFinal724_g61186;
				float weightedBlend888_g61186 = ( weightedBlendVar888_g61186.x*temp_output_878_0_g61186 + weightedBlendVar888_g61186.y*temp_output_875_0_g61186 + weightedBlendVar888_g61186.z*temp_output_876_0_g61186 + weightedBlendVar888_g61186.w*temp_output_877_0_g61186 );
				float FinalSmoothness897_g61186 = ( weightedBlend887_g61186 / max( weightedBlend888_g61186, 0.001 ) );
				float Smoothness237 = FinalSmoothness897_g61186;
				float3 temp_output_201_0 = ( _SpecularColor.rgb * Smoothness237 * _SecondarySpecularIntensity );
				float3 normalizeResult4_g61184 = normalize( ( ViewDirWS + SafeNormalize( _MainLightPosition.xyz ) ) );
				float smoothstepResult810_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult933_g61186 = clamp( smoothstepResult810_g61186 , 0.001 , 0.999 );
				float clampResult830_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_829_0_g61186 = ( clampResult933_g61186 * clampResult830_g61186 );
				float3 break635_g61186 = tex2DNode2_g61186.rgb;
				float2 appendResult655_g61186 = (float2(( ( break635_g61186.x * 2.0 ) - 1.0 ) , ( ( break635_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult656_g61186 = (float3(appendResult655_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break635_g61186.x * break635_g61186.x ) + ( break635_g61186.y * break635_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalA720_g61186 = appendResult656_g61186;
				float smoothstepResult812_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult934_g61186 = clamp( smoothstepResult812_g61186 , 0.001 , 0.999 );
				float clampResult832_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_826_0_g61186 = ( clampResult934_g61186 * clampResult832_g61186 );
				float3 break657_g61186 = tex2DNode1_g61186.rgb;
				float2 appendResult664_g61186 = (float2(( ( break657_g61186.x * 2.0 ) - 1.0 ) , ( ( break657_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult673_g61186 = (float3(appendResult664_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break657_g61186.x * break657_g61186.x ) + ( break657_g61186.y * break657_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalB721_g61186 = appendResult673_g61186;
				float smoothstepResult811_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult935_g61186 = clamp( smoothstepResult811_g61186 , 0.001 , 0.999 );
				float clampResult831_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_827_0_g61186 = ( clampResult935_g61186 * clampResult831_g61186 );
				float3 break676_g61186 = tex2DNode10_g61186.rgb;
				float2 appendResult683_g61186 = (float2(( ( break676_g61186.x * 2.0 ) - 1.0 ) , ( ( break676_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult692_g61186 = (float3(appendResult683_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break676_g61186.x * break676_g61186.x ) + ( break676_g61186.y * break676_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalC722_g61186 = appendResult692_g61186;
				float smoothstepResult813_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult936_g61186 = clamp( smoothstepResult813_g61186 , 0.001 , 0.999 );
				float clampResult833_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_828_0_g61186 = ( clampResult936_g61186 * clampResult833_g61186 );
				float3 break695_g61186 = tex2DNode11_g61186.rgb;
				float2 appendResult702_g61186 = (float2(( ( break695_g61186.x * 2.0 ) - 1.0 ) , ( ( break695_g61186.y * 2.0 ) - 1.0 )));
				float3 appendResult711_g61186 = (float3(appendResult702_g61186 , sqrt( max( 0.0, ( 1.0 - ( ( break695_g61186.x * break695_g61186.x ) + ( break695_g61186.y * break695_g61186.y ) ) ) ) )));
				float3 _UnpackedNormalD723_g61186 = appendResult711_g61186;
				float4 weightedBlendVar840_g61186 = ControlFinal724_g61186;
				float3 weightedBlend840_g61186 = ( weightedBlendVar840_g61186.x*( temp_output_829_0_g61186 * _UnpackedNormalA720_g61186 ) + weightedBlendVar840_g61186.y*( temp_output_826_0_g61186 * _UnpackedNormalB721_g61186 ) + weightedBlendVar840_g61186.z*( temp_output_827_0_g61186 * _UnpackedNormalC722_g61186 ) + weightedBlendVar840_g61186.w*( temp_output_828_0_g61186 * _UnpackedNormalD723_g61186 ) );
				float4 weightedBlendVar841_g61186 = ControlFinal724_g61186;
				float weightedBlend841_g61186 = ( weightedBlendVar841_g61186.x*temp_output_829_0_g61186 + weightedBlendVar841_g61186.y*temp_output_826_0_g61186 + weightedBlendVar841_g61186.z*temp_output_827_0_g61186 + weightedBlendVar841_g61186.w*temp_output_828_0_g61186 );
				float3 FinalNormal765_g61186 = ( weightedBlend840_g61186 / max( weightedBlend841_g61186, 0.001 ) );
				float3 temp_output_61_0_g61186 = FinalNormal765_g61186;
				float3 Normal236 = temp_output_61_0_g61186;
				float3 tanToWorld0 = float3( TangentWS.x, BitangentWS.x, NormalWS.x );
				float3 tanToWorld1 = float3( TangentWS.y, BitangentWS.y, NormalWS.y );
				float3 tanToWorld2 = float3( TangentWS.z, BitangentWS.z, NormalWS.z );
				float3 tanNormal131 = Normal236;
				float3 worldNormal131 = normalize( float3( dot( tanToWorld0, tanNormal131 ), dot( tanToWorld1, tanNormal131 ), dot( tanToWorld2, tanNormal131 ) ) );
				float3 normalizeResult132 = normalize( worldNormal131 );
				float3 Normals227 = normalizeResult132;
				float dotResult185 = dot( normalizeResult4_g61184 , Normals227 );
				float temp_output_203_0 = ( 1.0 - _SecondarySmoothness );
				float3 temp_output_208_0 = saturate( ( saturate( (  (-1.0 + ( _SecondarySpecularSize - 0.0 ) * ( -0.5 - -1.0 ) / ( 1.0 - 0.0 ) ) + dotResult185 ) ) / ( ( 1.0 - temp_output_201_0 ) * temp_output_203_0 ) ) );
				float3 DirectSpecHighlights163 = ( (temp_output_201_0).xyz * temp_output_208_0 );
				float ase_lightIntensity = max( max( _MainLightColor.r, _MainLightColor.g ), _MainLightColor.b ) + 1e-7;
				float4 ase_lightColor = float4( _MainLightColor.rgb / ase_lightIntensity, ase_lightIntensity );
				float ase_lightAtten = 0;
				Light ase_mainLight = GetMainLight( ShadowCoord );
				ase_lightAtten = ase_mainLight.distanceAttenuation * ase_mainLight.shadowAttenuation;
				float3 bakedGI151 = ASEIndirectDiffuse( input, NormalWS, PositionWS, ViewDirWS );
				MixRealtimeAndBakedGI( ase_mainLight, NormalWS, bakedGI151, half4( 0, 0, 0, 0 ) );
				float4 HalfLambert154 = ( ( ase_lightColor * ase_lightAtten ) + float4( bakedGI151 , 0.0 ) );
				float4 SpecularHighlights228 = ( float4( DirectSpecHighlights163 , 0.0 ) * HalfLambert154 );
				#ifdef _ENABLESPECULARHIGHLIGHTS_ON
				float4 staticSwitch162 = SpecularHighlights228;
				#else
				float4 staticSwitch162 = float4( 0,0,0,0 );
				#endif
				float4 color136 = IsGammaSpace() ? float4( 1, 1, 1, 1 ) : float4( 1, 1, 1, 1 );
				#ifdef _ENABLECOLORTINT_ON
				float4 staticSwitch166 = _BaseTint;
				#else
				float4 staticSwitch166 = color136;
				#endif
				float smoothstepResult591_g61186 = smoothstep( 0.0 , 1.0 , _HeightA279_g61186);
				float clampResult944_g61186 = clamp( smoothstepResult591_g61186 , 0.001 , 0.999 );
				float clampResult603_g61186 = clamp( _MaskA481_g61186 , 0.0 , 1.0 );
				float temp_output_597_0_g61186 = ( clampResult944_g61186 * clampResult603_g61186 );
				float4 _LayerA287_g61186 = tex2DNode4_g61186;
				float smoothstepResult594_g61186 = smoothstep( 0.0 , 1.0 , _HeightB280_g61186);
				float clampResult943_g61186 = clamp( smoothstepResult594_g61186 , 0.001 , 0.999 );
				float clampResult604_g61186 = clamp( _MaskB482_g61186 , 0.0 , 1.0 );
				float temp_output_598_0_g61186 = ( clampResult943_g61186 * clampResult604_g61186 );
				float4 _LayerB300_g61186 = tex2DNode3_g61186;
				float smoothstepResult595_g61186 = smoothstep( 0.0 , 1.0 , _HeightC281_g61186);
				float clampResult942_g61186 = clamp( smoothstepResult595_g61186 , 0.001 , 0.999 );
				float clampResult605_g61186 = clamp( _MaskC483_g61186 , 0.0 , 1.0 );
				float temp_output_599_0_g61186 = ( clampResult942_g61186 * clampResult605_g61186 );
				float4 _LayerC301_g61186 = tex2DNode6_g61186;
				float smoothstepResult596_g61186 = smoothstep( 0.0 , 1.0 , _HeightD282_g61186);
				float clampResult941_g61186 = clamp( smoothstepResult596_g61186 , 0.001 , 0.999 );
				float clampResult606_g61186 = clamp( _MaskD484_g61186 , 0.0 , 1.0 );
				float temp_output_600_0_g61186 = ( clampResult941_g61186 * clampResult606_g61186 );
				float4 _LayerD302_g61186 = tex2DNode7_g61186;
				float4 weightedBlendVar619_g61186 = ControlFinal724_g61186;
				float4 weightedBlend619_g61186 = ( weightedBlendVar619_g61186.x*( temp_output_597_0_g61186 * _LayerA287_g61186 ) + weightedBlendVar619_g61186.y*( temp_output_598_0_g61186 * _LayerB300_g61186 ) + weightedBlendVar619_g61186.z*( temp_output_599_0_g61186 * _LayerC301_g61186 ) + weightedBlendVar619_g61186.w*( temp_output_600_0_g61186 * _LayerD302_g61186 ) );
				float4 weightedBlendVar620_g61186 = ControlFinal724_g61186;
				float weightedBlend620_g61186 = ( weightedBlendVar620_g61186.x*temp_output_597_0_g61186 + weightedBlendVar620_g61186.y*temp_output_598_0_g61186 + weightedBlendVar620_g61186.z*temp_output_599_0_g61186 + weightedBlendVar620_g61186.w*temp_output_600_0_g61186 );
				float4 FinalAlbedo479_g61186 = ( weightedBlend619_g61186 / max( weightedBlend620_g61186, 0.001 ) );
				float4 temp_output_60_0_g61186 = FinalAlbedo479_g61186;
				float4 localClipHoles100_g61186 = ( temp_output_60_0_g61186 );
				float2 uv_TerrainHolesTexture = input.ase_texcoord7.xy * _TerrainHolesTexture_ST.xy + _TerrainHolesTexture_ST.zw;
				float holeClipValue99_g61186 = SAMPLE_TEXTURE2D( _TerrainHolesTexture, sampler_TerrainHolesTexture, uv_TerrainHolesTexture ).r;
				float Hole100_g61186 = holeClipValue99_g61186;
				{
				#ifdef _ALPHATEST_ON
				clip(Hole100_g61186 == 0.0f ? -1 : 1);
				#endif
				}
				float4 Albedo235 = localClipHoles100_g61186;
				float dotResult144 = dot( Normals227 , SafeNormalize( _MainLightPosition.xyz ) );
				float RampScale158 = _RampScale;
				float RampOffset157 = _RampOffset;
				float CEL_Effect142 = saturate( (dotResult144*RampScale158 + RampOffset157) );
				float2 temp_cast_3 = (CEL_Effect142).xx;
				float3 WorldPosition288_g61187 = PositionWS;
				float3 WorldPosition305_g61187 = WorldPosition288_g61187;
				float2 ScreenUV286_g61187 = (ScreenPosNorm).xy;
				float2 ScreenUV305_g61187 = ScreenUV286_g61187;
				float3 WorldNormal281_g61187 = Normals227;
				float3 WorldNormal305_g61187 = WorldNormal281_g61187;
				half2 LightmapUV1_g61188 = (input.ase_texcoord8.xy*(unity_LightmapST).xy + (unity_LightmapST).zw);
				half4 localCalculateShadowMask1_g61188 = CalculateShadowMask1_g61188( LightmapUV1_g61188 );
				float4 ShadowMask360_g61187 = localCalculateShadowMask1_g61188;
				float4 ShadowMask305_g61187 = ShadowMask360_g61187;
				float3 localAdditionalLightsLambertMask17x305_g61187 = AdditionalLightsLambertMask17x( WorldPosition305_g61187 , ScreenUV305_g61187 , WorldNormal305_g61187 , ShadowMask305_g61187 );
				float3 saferPower177 = abs( saturate( localAdditionalLightsLambertMask17x305_g61187 ) );
				float3 temp_cast_6 = ( (0.001 + ( _AdditionalLightFalloff - 0.0 ) * ( 12.0 - 0.001 ) / ( 12.0 - 0.0 ) )).xxx;
				
				float3 SpecularTint195 = _SpecularColor.rgb;
				

				float3 BaseColor = ( staticSwitch162 + ( staticSwitch166 * ( ( ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, temp_cast_3 ) ) * HalfLambert154 ) + ( Albedo235 * SAMPLE_TEXTURE2D( _TextureRamp3, sampler_TextureRamp3, (pow( saferPower177 , temp_cast_6 )* (0.001 + ( _AdditionalLightInfluence - 0.0 ) * ( 6.0 - 0.001 ) / ( 1.0 - 0.0 ) ) + RampOffset157).xy ) ) ) ) ).rgb;
				float3 Normal = Normal236;
				float3 Specular = ( _SpecularIntensity1 * SpecularTint195 * Smoothness237 );
				float Metallic = 0;
				float Smoothness = _Smoothness;
				float Occlusion = _Occlusion1;
				float3 Emission = 0;
				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
					float AlphaClipThresholdShadow = 0.5;
				#endif
				float3 BakedGI = 0;
				float3 RefractionColor = 1;
				float RefractionIndex = 1;
				float3 Transmission = 1;
				float3 Translucency = 1;

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#if defined( _ALPHATEST_ON )
					AlphaDiscard( Alpha, AlphaClipThreshold );
				#endif

				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS) && defined(ASE_CHANGES_WORLD_POS)
					ShadowCoord = TransformWorldToShadowCoord( PositionWS );
				#endif

				InputData inputData = (InputData)0;
				inputData.positionWS = PositionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalizedScreenSpaceUV = ScreenPosNorm.xy;
				inputData.shadowCoord = ShadowCoord;

				#ifdef _NORMALMAP
					#if _NORMAL_DROPOFF_TS
						inputData.normalWS = TransformTangentToWorld(Normal, half3x3( TangentWS, BitangentWS, NormalWS ));
					#elif _NORMAL_DROPOFF_OS
						inputData.normalWS = TransformObjectToWorldNormal(Normal);
					#elif _NORMAL_DROPOFF_WS
						inputData.normalWS = Normal;
					#endif
				#else
					inputData.normalWS = NormalWS;
				#endif

				inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
				inputData.viewDirectionWS = SafeNormalize( ViewDirWS );

				#ifdef ASE_FOG
					// @diogo: no fog applied in GBuffer
				#endif
				#ifdef _ADDITIONAL_LIGHTS_VERTEX
					inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				#endif

				#if defined( ENABLE_TERRAIN_PERPIXEL_NORMAL )
					float3 SH = SampleSH(inputData.normalWS.xyz);
				#else
					float3 SH = input.lightmapUVOrVertexSH.xyz;
				#endif

				#if defined(_SCREEN_SPACE_IRRADIANCE) && ( UNITY_VERSION >= 60030000 )
					#if ( UNITY_VERSION >= 60060000 )
						inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy, inputData.normalWS));
					#else
						inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
					#endif
				#elif defined(DYNAMICLIGHTMAP_ON)
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, input.dynamicLightmapUV.xy, SH, inputData.normalWS);
					inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);
				#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
					inputData.bakedGI = SAMPLE_GI(SH,
						GetAbsolutePositionWS(inputData.positionWS),
						inputData.normalWS,
						inputData.viewDirectionWS,
						input.positionCS.xy,
						input.probeOcclusion,
						inputData.shadowMask);
				#else
					inputData.bakedGI = SAMPLE_GI(input.lightmapUVOrVertexSH.xy, SH, inputData.normalWS);
					inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUVOrVertexSH.xy);
				#endif

				#ifdef ASE_BAKEDGI
					inputData.bakedGI = BakedGI;
				#endif

				#if defined(DEBUG_DISPLAY)
					#if defined(DYNAMICLIGHTMAP_ON)
						inputData.dynamicLightmapUV = input.dynamicLightmapUV.xy;
						#endif
					#if defined(LIGHTMAP_ON)
						inputData.staticLightmapUV = input.lightmapUVOrVertexSH.xy;
					#else
						inputData.vertexSH = SH;
					#endif
					#if defined(USE_APV_PROBE_OCCLUSION)
						inputData.probeOcclusion = input.probeOcclusion;
					#endif
				#endif

				#ifdef _DBUFFER
					ApplyDecal(input.positionCS,
						BaseColor,
						Specular,
						inputData.normalWS,
						Metallic,
						Occlusion,
						Smoothness);
				#endif

				BRDFData brdfData;
				InitializeBRDFData(BaseColor, Metallic, Specular, Smoothness, Alpha, brdfData);

				Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
				half4 color;
				MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI, inputData.shadowMask);

			#if ( UNITY_VERSION >= 60010000 )
				color.rgb = GlobalIllumination(brdfData, (BRDFData)0, 0,
                              inputData.bakedGI, Occlusion, inputData.positionWS,
                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
			#else
				color.rgb = GlobalIllumination(brdfData, inputData.bakedGI, Occlusion, inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS);
			#endif

				color.a = Alpha;

				#ifdef ASE_FINAL_COLOR_ALPHA_MULTIPLY
					color.rgb *= color.a;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

			#if ( UNITY_VERSION >= 60010000 )
				return PackGBuffersBRDFData(brdfData, inputData, Smoothness, Emission + color.rgb, Occlusion);
			#else
				return BRDFDataToGbuffer(brdfData, inputData, Smoothness, Emission + color.rgb, Occlusion);
			#endif
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "SceneSelectionPass"
			Tags { "LightMode"="SceneSelectionPass" }

			Cull Off
			AlphaToMask Off

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

			#define SCENESELECTIONPASS 1

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction(Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				output.ase_texcoord1 = input.ase_texcoord;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.ase_texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag( PackedVaryings input
				#if defined( ASE_WRITE_DEPTH )
				,out float outputDepth : ASE_SV_DEPTH
				#endif
				 ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;

				

				surfaceDescription.Alpha = 1;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				return half4( _ObjectId, _PassValue, 1.0, 1.0 );
			}

			ENDHLSL
		}

		
		Pass
		{
			
			Name "ScenePickingPass"
			Tags { "LightMode"="Picking" }

			AlphaToMask Off

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

		    #define SCENEPICKINGPASS 1

			#define ATTRIBUTES_NEED_NORMAL
			#define ATTRIBUTES_NEED_TANGENT
			#define SHADERPASS SHADERPASS_DEPTHONLY

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float4 ase_texcoord1 : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			struct SurfaceDescription
			{
				float Alpha;
				float AlphaClipThreshold;
			};

			PackedVaryings VertexFunction( Attributes input  )
			{
				PackedVaryings output;
				ZERO_INITIALIZE(PackedVaryings, output);

				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				output.ase_texcoord1 = input.ase_texcoord;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				input.normalOS = input.normalOS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( vertexInput.positionCS );
				output.positionWS = vertexInput.positionWS;
				return output;
			}

			#if defined(ASE_TESSELLATION)
			struct VertexControl
			{
				float4 positionOS : INTERNALTESSPOS;
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;

				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct TessellationFactors
			{
				float edge[3] : SV_TessFactor;
				float inside : SV_InsideTessFactor;
			};

			VertexControl vert ( Attributes input )
			{
				VertexControl output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				output.positionOS = input.positionOS;
				output.normalOS = input.normalOS;
				output.tangentOS = input.tangentOS;
				output.ase_texcoord = input.ase_texcoord;
				#if defined( ASE_INSTANCED_TERRAIN )
					TerrainApplyMeshModification( output.positionOS.xyz, output.normalOS, output.ase_texcoord );
				#endif
				
				return output;
			}

			TessellationFactors TessellationFunction (InputPatch<VertexControl,3> input)
			{
				TessellationFactors output;
				float4 tf = 1;
				float tessValue = _TessValue; float tessMin = _TessMin; float tessMax = _TessMax;
				float edgeLength = _TessEdgeLength; float tessMaxDisp = _TessMaxDisp;
				#if defined(ASE_FIXED_TESSELLATION)
				tf = FixedTess( tessValue );
				#elif defined(ASE_DISTANCE_TESSELLATION)
				tf = DistanceBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, tessValue, tessMin, tessMax, GetObjectToWorldMatrix(), _WorldSpaceCameraPos );
				#elif defined(ASE_LENGTH_TESSELLATION)
				tf = EdgeLengthBasedTess(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams );
				#elif defined(ASE_LENGTH_CULL_TESSELLATION)
				tf = EdgeLengthBasedTessCull(input[0].positionOS, input[1].positionOS, input[2].positionOS, edgeLength, tessMaxDisp, GetObjectToWorldMatrix(), _WorldSpaceCameraPos, _ScreenParams, unity_CameraWorldClipPlanes );
				#endif
				output.edge[0] = tf.x; output.edge[1] = tf.y; output.edge[2] = tf.z; output.inside = tf.w;
				return output;
			}

			[domain("tri")]
			[partitioning("fractional_odd")]
			[outputtopology("triangle_cw")]
			[patchconstantfunc("TessellationFunction")]
			[outputcontrolpoints(3)]
			VertexControl HullFunction(InputPatch<VertexControl, 3> patch, uint id : SV_OutputControlPointID)
			{
				return patch[id];
			}

			[domain("tri")]
			PackedVaryings DomainFunction(TessellationFactors factors, OutputPatch<VertexControl, 3> patch, float3 bary : SV_DomainLocation)
			{
				Attributes output = (Attributes) 0;
				output.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
				output.normalOS = patch[0].normalOS * bary.x + patch[1].normalOS * bary.y + patch[2].normalOS * bary.z;
				output.tangentOS = patch[0].tangentOS * bary.x + patch[1].tangentOS * bary.y + patch[2].tangentOS * bary.z;
				output.ase_texcoord = patch[0].ase_texcoord * bary.x + patch[1].ase_texcoord * bary.y + patch[2].ase_texcoord * bary.z;
				#if defined(ASE_PHONG_TESSELLATION)
				float3 pp[3];
				for (int i = 0; i < 3; ++i)
					pp[i] = output.positionOS.xyz - patch[i].normalOS * (dot(output.positionOS.xyz, patch[i].normalOS) - dot(patch[i].positionOS.xyz, patch[i].normalOS));
				float phongStrength = _TessPhongStrength;
				output.positionOS.xyz = phongStrength * (pp[0]*bary.x + pp[1]*bary.y + pp[2]*bary.z) + (1.0f-phongStrength) * output.positionOS.xyz;
				#endif
				UNITY_TRANSFER_INSTANCE_ID(patch[0], output);
				return VertexFunction(output);
			}
			#else
			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}
			#endif

			half4 frag( PackedVaryings input
				#if defined( ASE_WRITE_DEPTH )
				,out float outputDepth : ASE_SV_DEPTH
				#endif
				 ) : SV_Target
			{
				SurfaceDescription surfaceDescription = (SurfaceDescription)0;

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;

				

				surfaceDescription.Alpha = 1;
				#if defined( _ALPHATEST_ON )
					surfaceDescription.AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				return unity_SelectionID;
			}
			ENDHLSL
		}

		
		Pass
		{
			
			Name "MotionVectors"
			Tags { "LightMode"="MotionVectors" }

			ColorMask RG

			HLSLPROGRAM

			#define _ALPHATEST_ON
			#define _NORMAL_DROPOFF_TS 1
			#define ASE_TIME_BASED_MOTION_VECTORS
			#define ASE_FOG 1
			#pragma multi_compile_fragment _ DEBUG_DISPLAY
			#define ASE_TERRAIN
			#define _SPECULAR_SETUP 1
			#define ASE_FINAL_COLOR_ALPHA_MULTIPLY 1
			#define _NORMALMAP 1
			#define ASE_VERSION 19912
			#define ASE_SRP_VERSION 170100
			#define ASE_USING_SAMPLING_MACROS 1


			#pragma vertex vert
			#pragma fragment frag

			#if defined( _SPECULAR_SETUP ) && defined( ASE_LIGHTING_SIMPLE )
				#if defined( _SPECULARHIGHLIGHTS_OFF )
					#undef _SPECULAR_COLOR
				#else
					#define _SPECULAR_COLOR
				#endif
			#endif

            #define SHADERPASS SHADERPASS_MOTION_VECTORS

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
			#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
		    #include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#if defined(LOD_FADE_CROSSFADE)
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
			#endif

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"

			#define ASE_INSTANCED_TERRAIN
			#define ASE_NEEDS_VERT_POSITION
			#define ASE_NEEDS_VERT_NORMAL
			#define ASE_NEEDS_TEXTURE_COORDINATES0
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
			#define TERRAIN_SPLAT_FIRSTPASS 1
			#pragma editor_sync_compilation
			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _MASKMAP
			#pragma multi_compile_local __ _ALPHATEST_ON


			#if defined(ASE_WRITE_DEPTH_CONSERVATIVE) && (SHADER_TARGET >= 45)
				#define ASE_SV_DEPTH SV_DepthLessEqual
				#define ASE_SV_POSITION_QUALIFIERS linear noperspective centroid
			#else
				#define ASE_SV_DEPTH SV_Depth
				#define ASE_SV_POSITION_QUALIFIERS
			#endif

			#if ( UNITY_VERSION < 60010000 )
				#define APPLICATION_SPACE_WARP_MOTION APLICATION_SPACE_WARP_MOTION
			#endif

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 positionOld : TEXCOORD4;
				#if _ADD_PRECOMPUTED_VELOCITY
					float3 alembicMotionVector : TEXCOORD5;
				#endif
				half3 normalOS : NORMAL;
				half4 tangentOS : TANGENT;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct PackedVaryings
			{
				ASE_SV_POSITION_QUALIFIERS float4 positionCS : SV_POSITION;
				float4 positionCSNoJitter : TEXCOORD0;
				float4 previousPositionCSNoJitter : TEXCOORD1;
				float3 positionWS : TEXCOORD2;
				float4 ase_texcoord3 : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
			float4 _SpecularColor;
			float4 _Splat0_ST;
			float4 _Control_ST;
			float4 _Splat1_ST;
			float4 _Splat2_ST;
			float4 _Splat3_ST;
			float4 _BaseTint;
			float4 _TerrainHolesTexture_ST;
			float _SpecularIntensity1;
			float _AdditionalLightInfluence;
			float _AdditionalLightFalloff;
			float _RampOffset;
			float _SecondarySmoothness;
			float _Smoothness;
			float _SecondarySpecularSize;
			float _SecondarySpecularIntensity;
			float _RampScale;
			float _Occlusion1;
			float _AlphaClip;
			float _Cutoff;
			#ifdef ASE_TRANSMISSION
				float _TransmissionShadow;
			#endif
			#ifdef ASE_TRANSLUCENCY
				float _TransStrength;
				float _TransNormal;
				float _TransScattering;
				float _TransDirect;
				float _TransAmbient;
				float _TransShadow;
			#endif
			#ifdef ASE_TESSELLATION
				float _TessPhongStrength;
				float _TessValue;
				float _TessMin;
				float _TessMax;
				float _TessEdgeLength;
				float _TessMaxDisp;
			#endif
			CBUFFER_END

			#ifdef SCENEPICKINGPASS
				float4 _SelectionID;
			#endif

			#ifdef SCENESELECTIONPASS
				int _ObjectId;
				int _PassValue;
			#endif

			#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
				TEXTURE2D(_TerrainHeightmapTexture);//ASE Terrain Instancing
				TEXTURE2D( _TerrainNormalmapTexture);//ASE Terrain Instancing
				SAMPLER(sampler_TerrainNormalmapTexture);//ASE Terrain Instancing
			#endif//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_START( Terrain )//ASE Terrain Instancing
				UNITY_DEFINE_INSTANCED_PROP( float4, _TerrainPatchInstanceData )//ASE Terrain Instancing
			UNITY_INSTANCING_BUFFER_END( Terrain)//ASE Terrain Instancing
			CBUFFER_START( UnityTerrain)//ASE Terrain Instancing
				#ifdef UNITY_INSTANCING_ENABLED//ASE Terrain Instancing
					float4 _TerrainHeightmapRecipSize;//ASE Terrain Instancing
					float4 _TerrainHeightmapScale;//ASE Terrain Instancing
				#endif//ASE Terrain Instancing
			CBUFFER_END//ASE Terrain Instancing


			void TerrainApplyMeshModification( inout float3 position, inout half3 normal, inout float4 texcoord )
			{
			#ifdef UNITY_INSTANCING_ENABLED
				float2 patchVertex = position.xy;
				float4 instanceData = UNITY_ACCESS_INSTANCED_PROP( Terrain, _TerrainPatchInstanceData );
				float2 sampleCoords = ( patchVertex.xy + instanceData.xy ) * instanceData.z;
				float height = UnpackHeightmap( _TerrainHeightmapTexture.Load( int3( sampleCoords, 0 ) ) );
				position.xz = sampleCoords* _TerrainHeightmapScale.xz;
				position.y = height* _TerrainHeightmapScale.y;
				#ifdef ENABLE_TERRAIN_PERPIXEL_NORMAL
					normal = float3(0, 1, 0);
				#else
					normal = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb* 2 - 1;
				#endif
				texcoord.xy = sampleCoords* _TerrainHeightmapRecipSize.zw;
			#endif
			}
			

			// Applies the graph's vertex stage at a given time so the motion vector pass can
			// evaluate the current frame and re-evaluate the previous frame (procedural / time-based animation).
			Attributes ASEApplyVertexModification( Attributes input, float3 timeParameters, inout PackedVaryings output, out float3 customMotionVector  )
			{
				float3 currentTimeParameters = _TimeParameters.xyz;
				_TimeParameters.xyz = timeParameters;

				#if defined( ASE_INSTANCED_TERRAIN ) && !defined( ASE_TESSELLATION )
					TerrainApplyMeshModification( input.positionOS.xyz, input.normalOS, input.ase_texcoord );
				#endif
				
				output.ase_texcoord3 = input.ase_texcoord;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = input.positionOS.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					input.positionOS.xyz = vertexValue;
				#else
					input.positionOS.xyz += vertexValue;
				#endif

				customMotionVector = float3(0, 0, 0);

				_TimeParameters.xyz = currentTimeParameters;
				return input;
			}

			PackedVaryings VertexFunction( Attributes input )
			{
				PackedVaryings output = (PackedVaryings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				Attributes defaultInput = input;
				float3 currentMotionVector;
				input = ASEApplyVertexModification( input, _TimeParameters.xyz, output, currentMotionVector );

				VertexPositionInputs vertexInput = GetVertexPositionInputs( input.positionOS.xyz );

				#if defined(APPLICATION_SPACE_WARP_MOTION)
					float4 positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
					float4 positionCS = positionCSNoJitter;
				#else
					float4 positionCS = vertexInput.positionCS;
					float4 positionCSNoJitter = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, input.positionOS));
				#endif

				// Custom output and automatic time-based motion are mutually exclusive.
				#if defined(ASE_CUSTOM_MOTION_VECTOR)
					float3 prevPositionOS = ( unity_MotionVectorsParams.x == 1 ) ? input.positionOld : input.positionOS.xyz;
					prevPositionOS -= currentMotionVector;
				#else
					float3 prevPositionOS = ( unity_MotionVectorsParams.x == 1 ) ? input.positionOld : defaultInput.positionOS.xyz;
					#ifdef ASE_TIME_BASED_MOTION_VECTORS
						Attributes prevInput = defaultInput;
						prevInput.positionOS.xyz = prevPositionOS;
						PackedVaryings prevOutput = (PackedVaryings)0;
						float3 prevMotionVector;
						prevInput = ASEApplyVertexModification( prevInput, _LastTimeParameters.xyz, prevOutput, prevMotionVector );
						prevPositionOS = prevInput.positionOS.xyz;
					#endif
				#endif
				#if _ADD_PRECOMPUTED_VELOCITY
					prevPositionOS -= input.alembicMotionVector;
				#endif
				float4 previousPositionCSNoJitter = mul( _PrevViewProjMatrix, mul( UNITY_PREV_MATRIX_M, float4( prevPositionOS, 1 ) ) );

				output.positionCS = ASE_ADJUST_CLIP_POSITION( positionCS );
				output.positionCSNoJitter = ASE_ADJUST_CLIP_POSITION( positionCSNoJitter );
				output.previousPositionCSNoJitter = ASE_ADJUST_CLIP_POSITION( previousPositionCSNoJitter );
				output.positionWS = vertexInput.positionWS;

				return output;
			}

			PackedVaryings vert ( Attributes input )
			{
				return VertexFunction( input );
			}

			half4 frag(	PackedVaryings input
				#if defined( ASE_WRITE_DEPTH )
				,out float outputDepth : ASE_SV_DEPTH
				#endif
				 ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( input );

				float3 PositionWS = input.positionWS;
				float3 PositionRWS = GetCameraRelativePositionWS( PositionWS );
				float4 ScreenPosNorm = float4( GetNormalizedScreenSpaceUV( input.positionCS ), input.positionCS.zw );
				float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, input.positionCS.z ) * input.positionCS.w;

				

				float Alpha = 1;
				#if defined( _ALPHATEST_ON )
					float AlphaClipThreshold = _Cutoff;
				#endif

				#if defined( ASE_WRITE_DEPTH )
					input.positionCS.z = input.positionCS.z;
				#endif

				#ifdef _ALPHATEST_ON
					clip(Alpha - AlphaClipThreshold);
				#endif

				#if defined(ASE_CHANGES_WORLD_POS)
					float3 positionOS = mul( GetWorldToObjectMatrix(),  float4( PositionWS, 1.0 ) ).xyz;
					float3 previousPositionWS = mul( GetPrevObjectToWorldMatrix(),  float4( positionOS, 1.0 ) ).xyz;
					input.positionCSNoJitter = mul( _NonJitteredViewProjMatrix, float4( PositionWS, 1.0 ) );
					input.previousPositionCSNoJitter = mul( _PrevViewProjMatrix, float4( previousPositionWS, 1.0 ) );
				#endif

				#if defined(LOD_FADE_CROSSFADE)
					LODFadeCrossFade( input.positionCS );
				#endif

				#if defined( ASE_WRITE_DEPTH )
					outputDepth = input.positionCS.z;
				#endif

				#if defined(APPLICATION_SPACE_WARP_MOTION)
					return float4( CalcAswNdcMotionVectorFromCsPositions( input.positionCSNoJitter, input.previousPositionCSNoJitter ), 1 );
				#else
					return float4( CalcNdcMotionVectorFromCsPositions( input.positionCSNoJitter, input.previousPositionCSNoJitter ), 0, 0 );
				#endif
			}
			ENDHLSL
		}

	
	}
	

	

	CustomEditor "UnityEditor.ShaderGraphLitGUI"
	FallBack "Hidden/Shader Graph/FallbackError"
	
	Dependency "BaseMapShader"="Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)"

	Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
/*ASEBEGIN
Version=19912
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":123,"pos":[-4000,-832],"params":["Inherit","False","1166.792","305.3181","","7","169","148","147","146","145","144","143","CEL Effect","0.7960785,0.7215686,0,1","0","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":124,"pos":[-5152,-432],"params":["Inherit","False","1092","325","","5","232","161","132","131","129","Normals","0.6382856,0.4745098,1,1","0","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":125,"pos":[-5152,-832],"params":["Inherit","False","816","304","","5","153","152","151","150","149","Half Lambert","0.8078432,0.7294118,0,1","0","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":126,"pos":[-5152,0],"params":["Inherit","False","1044","323","","4","183","182","181","164","Specular Highlights","1,0,0.3882353,1","0","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":127,"pos":[-5152,1216],"params":["Inherit","False","2046.093","408.9518","Indirect","14","220","219","218","217","216","210","194","192","191","190","189","188","187","239","","1,0,0.390008,1","0","0"]}
{"type":"AmplifyShaderEditor.CommentaryNode, AmplifyShaderEditor","id":128,"pos":[-5152,336],"params":["Inherit","False","2048.144","858.4296","Direct","24","215","214","213","212","211","209","208","207","206","205","204","203","202","201","200","199","198","197","196","195","193","186","185","184","","1,0,0.390008,1","0","0"]}
{"type":"AmplifyShaderEditor.StickyNoteNode, AmplifyShaderEditor","id":119,"pos":[896,0],"params":["Inherit","False","383.6152","387.7935","URP","","0,0,0,1","***Additional Directives***\n#define TERRAIN_SPLAT_FIRSTPASS 1\n#pragma editor_sync_compilation\n#pragma shader_feature_local _NORMALMAP\n#pragma shader_feature_local _MASKMAP\n\n*** Custom SubShader Tags ***\nDisableBatching = False\nIgnoreProjector = True\nTerrainCompatible = True\n\nMaskMapR = Metallic\nMaskMapG = AO\nMaskMapB = Height\nMaskMapA = Smoothness\n\nAlwaysRenderMotionVectors = false\n\n\n","0","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":129,"pos":[-5056,-384],"params":["Inherit","False","236","Normal","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":130,"pos":[-4912,-1360],"params":["Inherit","False","TextureRamp","-1","True","1","0","SAMPLER2D","","False","1","SAMPLER2D","0"]}
{"type":"AmplifyShaderEditor.WorldNormalVector, AmplifyShaderEditor","id":131,"pos":[-4432,-336],"params":["Inherit","False","True","1","0","FLOAT3","0,0,1","False","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.NormalizeNode, AmplifyShaderEditor","id":132,"pos":[-4240,-336],"params":["Inherit","False","False","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor","id":133,"pos":[-1008,640],"params":["Inherit","True","Property","_TextureRamp1","Texture Ramp 1","0","1","[Header]","Create","True","1","Textures","0","0","False","1","Space(8)","False","","-1","None","None","True","0","False","white","Auto","False","Object","-1","Auto","Texture2D","False","8","0","SAMPLER2D","","False","1","FLOAT2","0,0","False","2","FLOAT","0","False","3","FLOAT2","0,0","False","4","FLOAT2","0,0","False","5","FLOAT","1","False","6","FLOAT","0","False","7","SAMPLERSTATE","","False","6","COLOR","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":134,"pos":[-496,448],"params":["Inherit","False","2","2","0","FLOAT4","0,0,0,0","False","1","COLOR","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":135,"pos":[-528,576],"params":["Inherit","False","154","HalfLambert","1","0","OBJECT","","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.ColorNode, AmplifyShaderEditor","id":136,"pos":[-944,0],"params":["Inherit","False","Constant","_DefaultTint","Default Tint","19","0","Create","True","0","0","0","False","0","False","Object","-1","","1,1,1,1","0,0,0,0","True","True","0","6","COLOR","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":137,"pos":[-304,448],"params":["Inherit","False","2","2","0","FLOAT4","0,0,0,0","False","1","COLOR","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":138,"pos":[96,224],"params":["Inherit","False","2","2","0","COLOR","0,0,0,0","False","1","FLOAT4","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":139,"pos":[-496,672],"params":["Inherit","False","2","2","0","FLOAT4","0,0,0,0","False","1","COLOR","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor","id":140,"pos":[-112,448],"params":["Inherit","True","2","2","0","FLOAT4","0,0,0,0","False","1","FLOAT4","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":141,"pos":[-1264,640],"params":["Inherit","False","130","TextureRamp","1","0","OBJECT","","False","1","SAMPLER2D","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":142,"pos":[-2768,-768],"params":["Inherit","False","CEL Effect","-1","True","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.WorldSpaceLightDirHlpNode, AmplifyShaderEditor","id":143,"pos":[-3952,-688],"params":["Inherit","False","True","1","0","FLOAT","0","False","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.DotProductOpNode, AmplifyShaderEditor","id":144,"pos":[-3680,-768],"params":["Inherit","False","2","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor","id":145,"pos":[-3264,-768],"params":["Inherit","False","3","0","FLOAT","1","False","1","FLOAT","0.5","False","2","FLOAT","0.5","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor","id":146,"pos":[-3008,-768],"params":["Inherit","False","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":147,"pos":[-3568,-624],"params":["Inherit","False","157","RampOffset","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":148,"pos":[-3568,-704],"params":["Inherit","False","158","RampScale","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.LightColorNode, AmplifyShaderEditor","id":149,"pos":[-5104,-784],"params":["Inherit","False","0","3","COLOR","0","FLOAT3","1","FLOAT","2"]}
{"type":"AmplifyShaderEditor.LightAttenuation, AmplifyShaderEditor","id":150,"pos":[-5104,-640],"params":["Inherit","False","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.IndirectDiffuseLighting, AmplifyShaderEditor","id":151,"pos":[-4768,-640],"params":["Inherit","False","Tangent","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":152,"pos":[-4768,-784],"params":["Inherit","False","2","2","0","COLOR","0,0,0,0","False","1","FLOAT","0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor","id":153,"pos":[-4496,-784],"params":["Inherit","False","2","2","0","COLOR","0,0,0,0","False","1","FLOAT3","0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":154,"pos":[-4272,-784],"params":["Inherit","False","HalfLambert","-1","True","1","0","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":155,"pos":[-1264,720],"params":["Inherit","False","142","CEL Effect","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor","id":156,"pos":[-1008,832],"params":["Inherit","True","Property","_TextureRamp2","Texture Ramp 1","0","1","[Header]","Create","True","1","Textures","0","0","False","1","Space(8)","False","","-1","None","None","True","0","False","white","Auto","False","Object","-1","Auto","Texture2D","False","8","0","SAMPLER2D","","False","1","FLOAT2","0,0","False","2","FLOAT","0","False","3","FLOAT2","0,0","False","4","FLOAT2","0,0","False","5","FLOAT","1","False","6","FLOAT","0","False","7","SAMPLERSTATE","","False","6","COLOR","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":157,"pos":[-4288,-1264],"params":["Inherit","False","RampOffset","-1","True","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":158,"pos":[-4288,-1360],"params":["Inherit","False","RampScale","-1","True","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":159,"pos":[-4608,-1360],"params":["Inherit","False","Property","_RampScale","Ramp Scale","15","0","Create","True","0","0","0","False","0","False","Object","-1","","0.5","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":160,"pos":[-4608,-1264],"params":["Inherit","False","Property","_RampOffset","Ramp Offset","16","0","Create","True","0","0","0","False","0","False","Object","-1","","0.5","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":161,"pos":[-5104,-272],"params":["Inherit","False","Property","_NormalScale","Normal Scale","17","0","Create","True","0","0","0","False","0","False","Object","-1","","1","1","0","5","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor","id":162,"pos":[-368,0],"params":["Inherit","False","Property","_EnableSpecularHighlights","Enable Specular Highlights","18","0","Create","True","0","0","0","False","2","Header(Highlights)","Space(8)","False","","0","1","1","True","","Toggle","2","Key0","Key1","Create","True","True","All","9","1","COLOR","0,0,0,0","False","0","COLOR","0,0,0,0","False","2","COLOR","0,0,0,0","False","3","COLOR","0,0,0,0","False","4","COLOR","0,0,0,0","False","5","COLOR","0,0,0,0","False","6","COLOR","0,0,0,0","False","7","COLOR","0,0,0,0","False","8","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":163,"pos":[-2832,640],"params":["Inherit","False","DirectSpecHighlights","-1","True","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":164,"pos":[-4736,112],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.TexturePropertyNode, AmplifyShaderEditor","id":165,"pos":[-5152,-1360],"params":["Inherit","True","Property","_TextureRamp3","Texture Ramp","14","3","[Header]","[NoScaleOffset]","[SingleLineTexture]","Create","True","1","Textures","0","0","False","1","Space (8)","False","","None","None","False","white","Auto","Texture2D","False","-1","0","2","SAMPLER2D","0","SAMPLERSTATE","1"]}
{"type":"AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor","id":166,"pos":[-368,224],"params":["Inherit","False","Property","_EnableColorTint","Enable Color Tint","12","0","Create","True","0","0","0","False","2","Header(Color)","Space(8)","False","","0","1","1","True","","Toggle","2","Key0","Key1","Create","True","True","All","9","1","COLOR","0,0,0,0","False","0","COLOR","0,0,0,0","False","2","COLOR","0,0,0,0","False","3","COLOR","0,0,0,0","False","4","COLOR","0,0,0,0","False","5","COLOR","0,0,0,0","False","6","COLOR","0,0,0,0","False","7","COLOR","0,0,0,0","False","8","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.ColorNode, AmplifyShaderEditor","id":167,"pos":[-944,224],"params":["Inherit","False","Property","_BaseTint","Base Tint","13","0","Create","True","0","0","0","False","1","Space(8)","False","Object","-1","","1,1,1,1","0,0,0,0","True","True","0","6","COLOR","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":168,"pos":[-1008,448],"params":["Inherit","False","235","Albedo","1","0","OBJECT","","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":169,"pos":[-3920,-784],"params":["Inherit","False","227","Normals","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":172,"pos":[-2144,976],"params":["Inherit","False","Property","_AdditionalLightFalloff","Additional Light Falloff","29","0","Create","True","0","0","0","False","0","False","Object","-1","","1","0","0","12","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":173,"pos":[-2144,1056],"params":["Inherit","False","Property","_AdditionalLightInfluence","Additional Light Influence","28","0","Create","True","0","0","0","False","0","False","Object","-1","","0.5","15","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.TFHCRemapNode, AmplifyShaderEditor","id":174,"pos":[-1856,1056],"params":["Inherit","False","5","0","FLOAT","0","False","1","FLOAT","0","False","2","FLOAT","1","False","3","FLOAT","0.001","False","4","FLOAT","6","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.TFHCRemapNode, AmplifyShaderEditor","id":175,"pos":[-1664,976],"params":["Inherit","False","5","0","FLOAT","0","False","1","FLOAT","0","False","2","FLOAT","12","False","3","FLOAT","0.001","False","4","FLOAT","12","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.PowerNode, AmplifyShaderEditor","id":177,"pos":[-1408,848],"params":["Inherit","False","True","2","0","FLOAT3","0,0,0","False","1","FLOAT","1","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor","id":178,"pos":[-1232,896],"params":["Inherit","False","3","0","FLOAT3","0,0,0","False","1","FLOAT","1","False","2","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":179,"pos":[-1472,1152],"params":["Inherit","False","157","RampOffset","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":180,"pos":[-1472,1072],"params":["Inherit","False","158","RampScale","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":181,"pos":[-5104,144],"params":["Inherit","False","154","HalfLambert","1","0","OBJECT","","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":182,"pos":[-5104,224],"params":["Inherit","False","221","IndirectSpecHighlights","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":183,"pos":[-5104,64],"params":["Inherit","False","163","DirectSpecHighlights","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":184,"pos":[-5040,992],"params":["Inherit","False","227","Normals","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.DotProductOpNode, AmplifyShaderEditor","id":185,"pos":[-4768,912],"params":["Inherit","False","2","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":186,"pos":[-5072,912],"params":["Inherit","False","Blinn-Phong Half Vector","-1","","61184","91a149ac9d615be429126c95e20753ce","0","0","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":187,"pos":[-3936,1504],"params":["Float","False","Property","_IndirectSpecularContribution","Indirect Specular Contribution","26","0","Create","True","0","0","0","False","0","False","Object","-1","","1","1","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":188,"pos":[-4256,1488],"params":["Inherit","False","Property","_SpecularOcclusion1","Specular Occlusion","25","0","Create","True","0","0","0","False","0","False","Object","-1","","1","0","0","12","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.IndirectSpecularLight, AmplifyShaderEditor","id":189,"pos":[-3936,1376],"params":["Inherit","False","World","3","0","FLOAT3","0,0,0","False","1","FLOAT","0.5","False","2","FLOAT","1","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.LerpOp, AmplifyShaderEditor","id":190,"pos":[-3600,1376],"params":["Inherit","False","3","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","2","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":191,"pos":[-3936,1280],"params":["Float","False","Constant","_Float6","Float 5","20","0","Create","True","0","0","0","False","0","False","Object","-1","","1","0","0","0","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":192,"pos":[-3328,1328],"params":["Inherit","False","2","2","0","FLOAT","0","False","1","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":193,"pos":[-4880,1088],"params":["Inherit","False","227","Normals","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.ViewDirInputsCoordNode, AmplifyShaderEditor","id":194,"pos":[-4880,1264],"params":["Inherit","False","World","True","0","4","FLOAT3","0","FLOAT","1","FLOAT","2","FLOAT","3"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":195,"pos":[-4448,384],"params":["Inherit","False","SpecularTint","-1","True","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":196,"pos":[-4800,592],"params":["Inherit","False","237","Smoothness","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor","id":197,"pos":[-4272,1072],"params":["Inherit","False","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.TFHCRemapNode, AmplifyShaderEditor","id":198,"pos":[-4464,880],"params":["Inherit","False","5","0","FLOAT","0","False","1","FLOAT","0","False","2","FLOAT","1","False","3","FLOAT","-1","False","4","FLOAT","-0.5","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor","id":199,"pos":[-4224,896],"params":["Inherit","False","2","2","0","FLOAT","0","False","1","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":200,"pos":[-3280,640],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":201,"pos":[-4448,480],"params":["Inherit","False","3","3","0","FLOAT3","0,0,0","False","1","FLOAT","1","False","2","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SwizzleNode, AmplifyShaderEditor","id":202,"pos":[-3888,480],"params":["Inherit","False","FLOAT3","0","1","2","3","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor","id":203,"pos":[-4240,736],"params":["Inherit","False","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.OneMinusNode, AmplifyShaderEditor","id":204,"pos":[-4240,576],"params":["Inherit","False","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":205,"pos":[-4000,576],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleDivideOpNode, AmplifyShaderEditor","id":206,"pos":[-3776,736],"params":["Inherit","False","2","0","FLOAT","0","False","1","FLOAT3","0.05,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor","id":207,"pos":[-4000,896],"params":["Inherit","False","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor","id":208,"pos":[-3600,736],"params":["Inherit","False","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor","id":209,"pos":[-3424,736],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":210,"pos":[-4640,1280],"params":["Inherit","False","SRP Additional Light","-1","","61185","6c86746ad131a0a408ca599df5f40861","3,6,2,351,1,23,0","6","2","FLOAT3","0,0,0","False","11","FLOAT3","0,0,0","False","345","FLOAT3","0,0,0","False","346","FLOAT3","0,0,0","False","347","FLOAT","0.5","False","32","FLOAT4","0,0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":211,"pos":[-4864,688],"params":["Inherit","False","Property","_SecondarySpecularIntensity","Secondary Specular Intensity","22","0","Create","True","0","0","0","False","1","Space(8)","False","Object","-1","","0.5","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":212,"pos":[-4560,736],"params":["Float","False","Property","_SecondarySmoothness","Secondary Smoothness","24","0","Create","True","0","0","0","False","0","False","Object","-1","","1","0.04","0.001","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":213,"pos":[-4864,784],"params":["Float","False","Property","_SecondarySpecularSize","Secondary Specular Size","23","0","Create","True","0","0","0","False","0","False","Object","-1","","0","-0.95","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.ColorNode, AmplifyShaderEditor","id":214,"pos":[-4800,384],"params":["Inherit","False","Property","_SpecularColor","Specular Color","19","1","[Header]","Create","True","1","Specular Highlights","0","0","False","1","Space(8)","False","Object","-1","","0,0,0,0","0,0,0,0","True","True","0","6","COLOR","0","FLOAT","1","FLOAT","2","FLOAT","3","FLOAT","4","FLOAT3","5"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":215,"pos":[-3888,1072],"params":["Inherit","False","SpecularSmoothness","-1","True","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":216,"pos":[-3600,1280],"params":["Inherit","False","237","Smoothness","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":217,"pos":[-4256,1408],"params":["Inherit","False","215","SpecularSmoothness","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":218,"pos":[-4256,1328],"params":["Inherit","False","227","Normals","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":219,"pos":[-4912,1504],"params":["Inherit","False","215","SpecularSmoothness","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":220,"pos":[-4912,1424],"params":["Inherit","False","195","SpecularTint","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":221,"pos":[-2832,1328],"params":["Inherit","False","IndirectSpecHighlights","-1","True","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":222,"pos":[96,432],"params":["Inherit","False","Property","_Occlusion1","Occlusion","27","1","[Header]","Create","True","1","Surface Options","0","0","False","1","Space (8)","False","Object","-1","","1","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":223,"pos":[96,352],"params":["Inherit","False","Property","_Smoothness","Smoothness","21","0","Create","True","0","0","0","False","0","False","Object","-1","","0","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor","id":224,"pos":[-112,672],"params":["Inherit","False","Property","_SpecularIntensity1","Specular Intensity","20","0","Create","True","0","0","0","False","0","False","Object","-1","","0","0","0","1","0","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":225,"pos":[208,672],"params":["Inherit","False","3","3","0","FLOAT","0","False","1","FLOAT3","0,0,0","False","2","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":226,"pos":[-112,752],"params":["Inherit","False","195","SpecularTint","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":227,"pos":[-4000,-336],"params":["Inherit","False","Normals","-1","True","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":228,"pos":[-4048,112],"params":["Inherit","False","SpecularHighlights","-1","True","1","0","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":229,"pos":[224,-256],"params":["Inherit","False","ToonScapes Terrain","0","","61186","839d5e6c5202c5f46a94cfc7415bf3bf","2,102,1,85,0","4","59","FLOAT4","0,0,0,0","False","60","FLOAT4","0,0,0,0","False","61","FLOAT3","0,0,0","False","58","FLOAT","0","False","3","FLOAT4","0","FLOAT3","14","FLOAT","45"]}
{"type":"AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor","id":230,"pos":[320,0],"params":["Inherit","False","2","2","0","COLOR","0,0,0,0","False","1","COLOR","0,0,0,0","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":231,"pos":[320,112],"params":["Inherit","False","236","Normal","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor","id":232,"pos":[-4736,-272],"params":["Inherit","False","2","2","0","FLOAT3","0,0,0","False","1","FLOAT","0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":233,"pos":[-112,832],"params":["Inherit","False","237","Smoothness","1","0","OBJECT","","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":234,"pos":[-656,0],"params":["Inherit","False","228","SpecularHighlights","1","0","OBJECT","","False","1","COLOR","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":235,"pos":[640,-256],"params":["Inherit","False","Albedo","-1","True","1","0","FLOAT4","0,0,0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":236,"pos":[640,-176],"params":["Inherit","False","Normal","-1","True","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor","id":237,"pos":[640,-96],"params":["Inherit","False","Smoothness","-1","True","1","0","FLOAT","0","False","1","FLOAT","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":171,"pos":[-1856,856],"params":["Inherit","False","SRP Additional Light","-1","","61187","6c86746ad131a0a408ca599df5f40861","3,6,1,351,1,23,0","6","2","FLOAT3","0,0,0","False","11","FLOAT3","0,0,0","False","345","FLOAT3","0,0,0","False","346","FLOAT3","0,0,0","False","347","FLOAT","0.5","False","32","FLOAT4","0,0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.SaturateNode, AmplifyShaderEditor","id":176,"pos":[-1600,856],"params":["Inherit","False","1","0","FLOAT3","0,0,0","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor","id":170,"pos":[-2080,800],"params":["Inherit","False","227","Normals","1","0","OBJECT","","False","1","FLOAT3","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":238,"pos":[-2144,880],"params":["Inherit","False","Shadow Mask","-1","","61188","b50f5becdd6b8504a861ba5b9b861159","0","1","3","FLOAT2","0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor","id":239,"pos":[-5128,1264],"params":["Inherit","False","Shadow Mask","-1","","61190","b50f5becdd6b8504a861ba5b9b861159","0","1","3","FLOAT2","0,0","False","1","FLOAT4","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":109,"pos":[645.0565,0],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","ExtraPrePass","0","0","ExtraPrePass","6","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","True","1","1","False","","0","False","","0","1","False","","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","0","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":111,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","ShadowCaster","0","2","ShadowCaster","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","True","False","False","False","False","0","False","","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","False","False","True","1","LightMode=ShadowCaster","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":112,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","DepthOnly","0","3","DepthOnly","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","True","True","False","False","False","0","False","","False","False","False","False","False","False","False","False","False","True","1","False","","False","False","False","True","1","LightMode=DepthOnly","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":113,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","Meta","0","4","Meta","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","2","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=Meta","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":114,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","Universal2D","0","5","Universal2D","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","1","LightMode=Universal2D","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":115,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","DepthNormals","0","6","DepthNormals","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","True","1","1","False","","0","False","","0","1","False","","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","False","","True","3","False","","False","False","True","1","LightMode=DepthNormals","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":116,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","GBuffer","0","7","GBuffer","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","1","LightMode=UniversalGBuffer","False","True","10","d3d11","gles","metal","vulkan","xboxone","xboxseries","playstation","ps4","ps5","switch","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":117,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","SceneSelectionPass","0","8","SceneSelectionPass","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","2","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=SceneSelectionPass","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":118,"pos":[645.0565,40.11808],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","ScenePickingPass","0","9","ScenePickingPass","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=Picking","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":110,"pos":[640,0],"params":["Float","False","True","-1","3","UnityEditor.ShaderGraphLitGUI","0","15","ToonScapes/URP/Terrain","94348b07e5e8bab40bd6c8a1e3df54cd","True","Forward","0","1","Forward","22","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","12","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=-100","UniversalMaterialType=Lit","DisableBatching=False=DisableBatching","IgnoreProjector=True","TerrainCompatible=True","MaskMapR=Metallic","MaskMapG=AO","MaskMapB=Height","MaskMapA=Smoothness","AlwaysRenderMotionVectors=false","True","5","True","14","all","0","False","True","1","1","False","","0","False","","1","1","False","","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","9","LightMode=UniversalForward","DisableBatching=False=DisableBatching","IgnoreProjector=True","TerrainCompatible=True","MaskMapR=Metallic","MaskMapG=AO","MaskMapB=Height","MaskMapA=Smoothness","AlwaysRenderMotionVectors=false","False","False","5","Include","","False","","Native","False","0","0","","Define","TERRAIN_SPLAT_FIRSTPASS 1","False","","Custom","False","0","0","","Pragma","editor_sync_compilation","False","","Custom","False","0","0","","Pragma","shader_feature_local _NORMALMAP","False","","Custom","False","0","0","","Pragma","shader_feature_local _MASKMAP","False","","Custom","False","0","0","","Hidden/Universal Render Pipeline/FallbackError","1","BaseMapShader=Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)","0","Standard","52","Category","1","638925196395876883","  Instanced Terrain Normals","2","638925196415800616","Lighting Model","0","0","Workflow","0","638925196442469153","Surface","0","0","  Keep Alpha","0","0","  Refraction Model","0","0","  Blend","0","0","Two Sided","1","0","Alpha Clipping","1","0","  Use Shadow Threshold","0","0","Fragment Normal Space","0","0","Forward Only","0","0","Transmission","0","0","  Transmission Shadow","0.5,False,","0","Translucency","0","0","  Translucency Strength","1,False,","0","  Normal Distortion","0.5,False,","0","  Scattering","2,False,","0","  Direct","0.9,False,","0","  Ambient","0.1,False,","0","  Shadow","0.5,False,","0","Cast Shadows","1","0","Receive Shadows","2","0","Specular Highlights","2","0","Environment Reflections","2","0","Receive SSAO","1","638925927321451838","Motion Vectors","1","0","  Additional Motion Vectors","1","0","  Alembic Motion Vectors","0","0","  XR Motion Vectors","0","0","GPU Instancing","0","638925196733682262","LOD CrossFade","0","638925196750770295","Built-in Fog","1","0","_FinalColorxAlpha","1","638925196808148775","Meta Pass","1","0","Override Baked GI","0","0","Extra Pre Pass","0","0","Tessellation","0","0","  Phong","0","0","  Strength","0.5,False,","0","  Type","0","0","  Tess","16,False,","0","  Min","10,False,","0","  Max","25,False,","0","  Edge Length","16,False,","0","  Max Displacement","25,False,","0","Write Depth","0","0","  Conservative","0","0","Vertex Position","1","0","Debug Display","1","0","Clear Coat","0","0","0","12","False","True","True","True","True","True","True","True","True","True","True","False","True","","True","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":121,"pos":[640,100],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","MotionVectors","0","10","MotionVectors","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","False","False","0","False","","False","False","False","False","False","False","False","False","False","False","False","False","False","True","1","LightMode=MotionVectors","False","False","0","","0","0","Standard","0","False","0"]}
{"type":"AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor","id":122,"pos":[640,100],"params":["Float","False","False","-1","3","UnityEditor.ShaderGraphLitGUI","0","1","New Amplify Shader","94348b07e5e8bab40bd6c8a1e3df54cd","True","XRMotionVectors","0","11","XRMotionVectors","0","False","False","False","False","False","False","False","False","False","False","False","False","True","0","False","","False","True","0","False","","False","False","False","False","False","False","False","False","False","True","False","0","False","","255","False","","255","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","True","1","False","","True","3","False","","True","True","0","False","","0","False","","False","True","4","RenderPipeline=UniversalPipeline","RenderType=Opaque=RenderType","Queue=Geometry=Queue=0","UniversalMaterialType=Lit","True","5","True","14","all","0","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","False","True","True","True","True","True","0","False","","False","False","False","False","False","False","False","True","True","1","False","","255","False","","1","False","","7","False","","3","False","","0","False","","0","False","","0","False","","0","False","","0","False","","0","False","","False","False","False","False","False","True","1","LightMode=XRMotionVectors","False","False","0","","0","0","Standard","0","False","0"]}
{"wire":[130,0,165,0]}
{"wire":[131,0,129,0]}
{"wire":[132,0,131,0]}
{"wire":[133,0,141,0]}
{"wire":[133,1,155,0]}
{"wire":[134,0,168,0]}
{"wire":[134,1,133,0]}
{"wire":[137,0,134,0]}
{"wire":[137,1,135,0]}
{"wire":[138,0,166,0]}
{"wire":[138,1,140,0]}
{"wire":[139,0,168,0]}
{"wire":[139,1,156,0]}
{"wire":[140,0,137,0]}
{"wire":[140,1,139,0]}
{"wire":[142,0,146,0]}
{"wire":[144,0,169,0]}
{"wire":[144,1,143,0]}
{"wire":[145,0,144,0]}
{"wire":[145,1,148,0]}
{"wire":[145,2,147,0]}
{"wire":[146,0,145,0]}
{"wire":[152,0,149,0]}
{"wire":[152,1,150,0]}
{"wire":[153,0,152,0]}
{"wire":[153,1,151,0]}
{"wire":[154,0,153,0]}
{"wire":[156,0,141,0]}
{"wire":[156,1,178,0]}
{"wire":[157,0,160,0]}
{"wire":[158,0,159,0]}
{"wire":[162,0,234,0]}
{"wire":[163,0,200,0]}
{"wire":[164,0,183,0]}
{"wire":[164,1,181,0]}
{"wire":[166,1,136,0]}
{"wire":[166,0,167,0]}
{"wire":[174,0,173,0]}
{"wire":[175,0,172,0]}
{"wire":[177,0,176,0]}
{"wire":[177,1,175,0]}
{"wire":[178,0,177,0]}
{"wire":[178,1,174,0]}
{"wire":[178,2,179,0]}
{"wire":[185,0,186,0]}
{"wire":[185,1,184,0]}
{"wire":[189,0,218,0]}
{"wire":[189,1,217,0]}
{"wire":[189,2,188,0]}
{"wire":[190,0,191,0]}
{"wire":[190,1,189,0]}
{"wire":[190,2,187,0]}
{"wire":[192,0,216,0]}
{"wire":[192,1,190,0]}
{"wire":[195,0,214,5]}
{"wire":[197,0,210,0]}
{"wire":[198,0,213,0]}
{"wire":[199,0,198,0]}
{"wire":[199,1,185,0]}
{"wire":[200,0,202,0]}
{"wire":[200,1,208,0]}
{"wire":[201,0,214,5]}
{"wire":[201,1,196,0]}
{"wire":[201,2,211,0]}
{"wire":[202,0,201,0]}
{"wire":[203,0,212,0]}
{"wire":[204,0,201,0]}
{"wire":[205,0,204,0]}
{"wire":[205,1,203,0]}
{"wire":[206,0,207,0]}
{"wire":[206,1,205,0]}
{"wire":[207,0,199,0]}
{"wire":[208,0,206,0]}
{"wire":[209,0,208,0]}
{"wire":[209,1,197,0]}
{"wire":[210,11,193,0]}
{"wire":[210,345,194,0]}
{"wire":[210,346,220,0]}
{"wire":[210,347,219,0]}
{"wire":[210,32,239,0]}
{"wire":[215,0,203,0]}
{"wire":[221,0,192,0]}
{"wire":[225,0,224,0]}
{"wire":[225,1,226,0]}
{"wire":[225,2,233,0]}
{"wire":[227,0,132,0]}
{"wire":[228,0,164,0]}
{"wire":[230,0,162,0]}
{"wire":[230,1,138,0]}
{"wire":[232,0,129,0]}
{"wire":[232,1,161,0]}
{"wire":[235,0,229,0]}
{"wire":[236,0,229,14]}
{"wire":[237,0,229,45]}
{"wire":[171,11,170,0]}
{"wire":[171,32,238,0]}
{"wire":[176,0,171,0]}
{"wire":[110,0,230,0]}
{"wire":[110,1,231,0]}
{"wire":[110,9,225,0]}
{"wire":[110,4,223,0]}
{"wire":[110,5,222,0]}
ASEEND*/
//CHKSM=AC5059B1C7B78E0699A03473FF1CE9332BC915DE