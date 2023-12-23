Shader "Hidden/Volumetric Light Shafts"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        #pragma shader_feature_local_fragment USE_FIXED_LENGTH

        uniform TEXTURE2D(_MainTex);
        uniform SAMPLER(sampler_MainTex);
        uniform float4 _MainTex_TexelSize;

        uniform TEXTURE2D(_JitterTexture);
        uniform SAMPLER(sampler_JitterTexture);
        uniform float4 _JitterTexture_TexelSize;

        uniform float2 _BlurCenterUV;
        uniform uint _BlurSamples;
        uniform float _BlurDistance;
        uniform float _Intensity;
        uniform float _AlignmentFalloff;
        uniform float _AlignmentLowerEdge;
        uniform float _AlignmentUpperEdge;
        uniform float _KawaseBlurStepRadius;
        uniform float _JitterFactor;

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float2 uv : TEXCOORD0;
            float4 vertex : SV_POSITION;
        };

        v2f vert(appdata v)
        {
            v2f o;
            o.vertex = TransformObjectToHClip(v.vertex);
            o.uv = v.uv;
            return o;
        }

        #define SAFE_NORMALIZE(vec) vec * rsqrt(max(FLT_MIN, dot(vec, vec)))

        #define OVERLAY(base, top) lerp(2.0f * base * top, 1.0f - 2.0f * (1.0f - base) * (1.0f - top), step(0.5f, base))

        ENDHLSL

        Pass
        {
            Name "Radial Blur"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_TARGET
            {
                float2 jitter = (SAMPLE_TEXTURE2D(_JitterTexture, sampler_JitterTexture, i.uv).xy * 2.0f - 1.0f);
                float2 blurCenter = _BlurCenterUV + jitter * _MainTex_TexelSize.xy * _JitterFactor;

                float2 displacement = i.uv - blurCenter;
                float2 displacementStep =
                #if USE_FIXED_LENGTH
                    SAFE_NORMALIZE(displacement) * _BlurDistance / _BlurSamples;
                #else
                    displacement * _BlurDistance / _BlurSamples;
				#endif

                //float4 sceneColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv); 
                float4 blurredSceneColor = 0.0f;

                [loop]
                for (uint index = 0; index < _BlurSamples; index++)
                {
                    float2 offset = displacementStep * index;
                    float2 sampleUV = i.uv + offset;

                    float isAtSkybox = step(0.999f, Linear01Depth(SampleSceneDepth(sampleUV), _ZBufferParams));
                    //Separate
                    blurredSceneColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV) * isAtSkybox;
                }

                blurredSceneColor *= _Intensity / _BlurSamples;

                float3 mainLightDirectionWS = _MainLightPosition.xyz;
                float3 mainLightDirectionVS = normalize(mul((float3x3)unity_WorldToCamera, mainLightDirectionWS));

                float mainLightAlignment = dot(mainLightDirectionVS, float3(0.0f, 0.0f, 1.0f)) * 0.5f + 0.5f;
                return blurredSceneColor
                       * pow(smoothstep(_AlignmentLowerEdge, _AlignmentUpperEdge, (mainLightAlignment)),
                             _AlignmentFalloff);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Kawase Blur"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_TARGET
            {
                #define NUM_OFFSETS 4
                static const float2 offsets[NUM_OFFSETS] =
                {
                    float2(-0.5f, 0.5f),
                    float2(0.5f, 0.5f),
                    float2(-0.5f, -0.5f),
                    float2(0.5f, -0.5f),
				};

                float4 avg = 0.0f;

                [unroll]
                for (uint j = 0; j < NUM_OFFSETS; j++)
                    avg += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                                            i.uv + (offsets[j] * (1.0f + 2.0f * _KawaseBlurStepRadius)) * _MainTex_TexelSize.xy);

                return avg / NUM_OFFSETS;
            }

            ENDHLSL
        }

        Pass
        {
            Name "Light Shafts Composite"

            Blend One One

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_TARGET
            {
				return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
			}

            ENDHLSL
        }
    }
}
