// A stylised jelly surface for slime-like monsters.
//
// WHY THIS EXISTS. URP/Lit renders the Training Slime as opaque painted plastic: flat, very
// saturated, no sense that light is getting into it. What a jelly reads as is three things
// working together -- light leaking through the thin parts, a brighter core than rim, and a
// tight wet highlight -- and none of the three is a slider on URP/Lit.
//
// WHY IT IS NOT A REFRACTION SHADER. This is an MMO: a camp is six or seven of these on
// screen at once and a field could be dozens. Screen-space refraction costs a scene colour
// copy and reads it per pixel per monster; a depth-based thickness pass costs another. The
// look here is bought with a fresnel term and wrap lighting -- arithmetic on values the
// fragment already has, no extra textures, no extra passes, no grab pass. One shared
// material serves every slime in the world.
//
// WHAT IT DOES.
//   * Wrap lighting, so the shaded side stays luminous instead of going black, which is what
//     "light is getting through it" looks like on an opaque surface.
//   * Inverse fresnel tint: deeper blue towards the silhouette, brighter towards the middle.
//     A real gel is thicker where you are looking through more of it.
//   * A separate rim glow on top, which is the wet edge.
//   * A Blinn-Phong highlight, tight and bright, which is the surface being wet rather than
//     matte.
//   * A controlled alpha. Not a see-through window: enough to let the ground tint the body
//     and no more, so the eyes and the mouth stay readable against grass, road, dark earth
//     and sky. Back faces are culled and depth is written, so a slime never shows its own
//     inside and two slimes never sort through each other.
// GPU INSTANCING MUST STAY OFF ON MATERIALS USING THIS. Measured, not assumed: with
// "Enable GPU Instancing" ticked, every slime rendered solid black. The pass declares no
// UNITY_INSTANCING_BUFFER, so the per-material constants came through as zero and the body
// colour, the tint and the opacity all resolved to nothing. URP batches this through the SRP
// Batcher instead, which is the better path here anyway and needs no such buffer -- the
// CBUFFER below is named UnityPerMaterial and lists every non-texture property precisely so
// that it qualifies.
Shader "ChibiFantasy/Jelly"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Tint", Color) = (0.72, 0.90, 1.0, 1)
        _DeepColor("Edge Colour", Color) = (0.24, 0.52, 0.82, 1)
        _CoreColor("Core Colour", Color) = (0.86, 0.97, 1.0, 1)

        _Alpha("Opacity", Range(0.35, 1)) = 0.86
        _Thickness("Edge Thickness", Range(0.5, 6)) = 2.2
        _CoreStrength("Core Brightness", Range(0, 1)) = 0.45

        _RimColor("Rim Glow", Color) = (0.80, 0.96, 1.0, 1)
        _RimPower("Rim Sharpness", Range(1, 12)) = 4.5
        _RimStrength("Rim Strength", Range(0, 2)) = 0.55

        _Smoothness("Glossiness", Range(8, 256)) = 96
        _SpecStrength("Highlight", Range(0, 3)) = 1.1

        _Wrap("Light Wrap", Range(0, 1)) = 0.55
        _AmbientLift("Ambient Lift", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "JellyForward"
            Tags { "LightMode" = "UniversalForward" }

            // Depth is written on purpose. Two slimes overlapping, or one slime overlapping
            // itself, resolve by depth instead of by draw order -- which is the difference
            // between a jelly and a sorting fault.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half4  _DeepColor;
                half4  _CoreColor;
                half4  _RimColor;
                half   _Alpha;
                half   _Thickness;
                half   _CoreStrength;
                half   _RimPower;
                half   _RimStrength;
                half   _Smoothness;
                half   _SpecStrength;
                half   _Wrap;
                half   _AmbientLift;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // How much body the eye is looking through. Near the silhouette it is a lot,
                // so the colour deepens; facing the camera it is least, so the core shows.
                half facing = saturate(dot(normalWS, viewWS));
                half depth = pow(1.0h - facing, _Thickness);

                half3 body = albedo.rgb * _BaseColor.rgb;
                body = lerp(body, body * _DeepColor.rgb * 1.6h, depth);
                body = lerp(body, body + _CoreColor.rgb * 0.35h, facing * _CoreStrength);

                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // Wrap lighting. A jelly lit from one side is not black on the other, because
                // the light travels through it; this is the cheap way to say so.
                half ndotl = dot(normalWS, main.direction);
                half wrapped = saturate((ndotl + _Wrap) / (1.0h + _Wrap));
                half3 lit = body * main.color * (wrapped * main.shadowAttenuation + _AmbientLift);

                // Whatever the sky and the bounce are contributing, so it sits in the scene.
                lit += body * SampleSH(normalWS) * 0.6h;

                float3 halfWS = normalize(main.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfWS)), _Smoothness);
                lit += main.color * spec * _SpecStrength * main.shadowAttenuation;

                half rim = pow(1.0h - facing, _RimPower);
                lit += _RimColor.rgb * rim * _RimStrength;

                // Opaque towards the silhouette, where a real gel is thickest, and clearest
                // where it is thinnest -- but never clear enough to lose the face.
                half alpha = saturate(_Alpha + depth * (1.0h - _Alpha));

                lit = MixFog(lit, input.fogFactor);
                return half4(lit, alpha);
            }
            ENDHLSL
        }

        // Shadows and depth still behave, so a slime casts onto the ground and the depth
        // prepass does not see a hole where one is standing.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    Fallback "Universal Render Pipeline/Lit"
}
