using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricLightShaftsRenderPassFeature : ScriptableRendererFeature
{
    class VolumetricLightShaftsRenderPass : ScriptableRenderPass
    {
        private const string VOLUMETRIC_LIGHT_SHAFTS_SHADER_PATH = "Hidden/Volumetric Light Shafts";
        private Material _lightShaftsMaterial;
        private static readonly string s_PassName = nameof(VolumetricLightShaftsRenderPass);
        private readonly ProfilingSampler _profilingSampler = new ProfilingSampler(s_PassName);

        private VolumetricLightShaftsRenderPassSettings _settings;

        private RenderTargetIdentifier _temporaryBuffer0;
        private static readonly int s_temporaryBuffer0ID = Shader.PropertyToID(s_PassName + nameof(_temporaryBuffer0));

        private RenderTargetIdentifier _temporaryBuffer1;
        private static readonly int s_temporaryBuffer1ID = Shader.PropertyToID(s_PassName +nameof(_temporaryBuffer1));

        private static readonly int s_blurSamplesID = Shader.PropertyToID("_BlurSamples");
        private static readonly int s_blurDistanceID = Shader.PropertyToID("_BlurDistance");
        private static readonly int s_blurCenterUVID = Shader.PropertyToID("_BlurCenterUV");
        private static readonly int s_intensityID = Shader.PropertyToID("_Intensity");
        private static readonly int s_alignmentFalloffID = Shader.PropertyToID("_AlignmentFalloff");
        private static readonly int s_alignmentLowerEdgeID = Shader.PropertyToID("_AlignmentLowerEdge");
        private static readonly int s_alignmentUpperEdgeID = Shader.PropertyToID("_AlignmentUpperEdge");
        private static readonly int s_kawaseBlurStepRadiusID = Shader.PropertyToID("_KawaseBlurStepRadius");
        private static readonly int s_jitterTextureID = Shader.PropertyToID("_JitterTexture");
        private static readonly int s_jitterFactorID = Shader.PropertyToID("_JitterFactor");

        private LocalKeyword _useFixedLengthKeyword;
        public VolumetricLightShaftsRenderPass(VolumetricLightShaftsRenderPassSettings settings)
        {
            _settings = settings;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            base.OnCameraSetup(cmd, ref renderingData);
            _lightShaftsMaterial = _lightShaftsMaterial == null
                                   ? CoreUtils.CreateEngineMaterial(VOLUMETRIC_LIGHT_SHAFTS_SHADER_PATH)
                                   : _lightShaftsMaterial;

            _lightShaftsMaterial.SetInteger(s_blurSamplesID, _settings.blurSamples);
            _lightShaftsMaterial.SetFloat(s_blurDistanceID, _settings.blurDistance);
            _lightShaftsMaterial.SetFloat(s_intensityID, _settings.intensity);
            _lightShaftsMaterial.SetFloat(s_alignmentFalloffID, _settings.alignmentFalloff);
            _lightShaftsMaterial.SetFloat(s_alignmentLowerEdgeID, _settings.alignmentLowerEdge);
            _lightShaftsMaterial.SetFloat(s_alignmentUpperEdgeID, _settings.alignmentUpperEdge);
            _lightShaftsMaterial.SetTexture(s_jitterTextureID, _settings.jitterTexture);
            _lightShaftsMaterial.SetFloat(s_jitterFactorID, _settings.jitterFactor);

            Camera camera = renderingData.cameraData.camera;
            Vector3 sunDirectionWS = RenderSettings.sun.transform.forward;
            Vector3 cameraPositionWS = camera.transform.position;
            Vector3 sunPositionWS = cameraPositionWS + sunDirectionWS;
            Vector3 sunPositionUV = camera.WorldToViewportPoint(sunPositionWS);
            _lightShaftsMaterial.SetVector(s_blurCenterUVID, sunPositionUV);

            _useFixedLengthKeyword = new LocalKeyword(_lightShaftsMaterial.shader, "USE_FIXED_LENGTH");
            _lightShaftsMaterial.SetKeyword(_useFixedLengthKeyword, _settings.useFixedLength);

            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.colorFormat = _settings.lowerTextureTo16Bit
                                     ? RenderTextureFormat.RGB565
                                     : RenderTextureFormat.RGB111110Float;
            int halveFactor = 1 << _settings.downsamples;
            descriptor.width /= halveFactor;
            descriptor.height /= halveFactor;

            cmd.GetTemporaryRT(s_temporaryBuffer0ID, descriptor, FilterMode.Bilinear);
            _temporaryBuffer0 = new RenderTargetIdentifier(s_temporaryBuffer0ID);

            cmd.GetTemporaryRT(s_temporaryBuffer1ID, descriptor, FilterMode.Bilinear);
            _temporaryBuffer1 = new RenderTargetIdentifier(s_temporaryBuffer1ID);

            ConfigureTarget(_temporaryBuffer0);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var colorTarget = renderingData.cameraData.renderer.cameraColorTarget;

            using (new ProfilingScope(cmd, _profilingSampler))
            {
                // Downsampling
                cmd.Blit(colorTarget, _temporaryBuffer1);
                cmd.Blit(_temporaryBuffer1, _temporaryBuffer0, _lightShaftsMaterial, 0);

                if (_settings.useAdditionalBlurring)
                {
                    int[] kawaseBlurRadii = new int[] { 0, 1, 2, 2, 3 };

                    for (int i = 0; i < kawaseBlurRadii.Length; i++)
                    {
                        cmd.SetGlobalFloat(s_kawaseBlurStepRadiusID, kawaseBlurRadii[i]);
                        cmd.Blit(_temporaryBuffer0, _temporaryBuffer1, _lightShaftsMaterial, 1);
                        cmd.Blit(_temporaryBuffer1, _temporaryBuffer0);
                    }
                }

                cmd.Blit(_temporaryBuffer0, colorTarget, _lightShaftsMaterial, 2);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            base.OnCameraCleanup(cmd);

            cmd ??= CommandBufferPool.Get();
            cmd.ReleaseTemporaryRT(s_temporaryBuffer0ID);
            cmd.ReleaseTemporaryRT(s_temporaryBuffer1ID);
        }
    }

    [Serializable]
    private struct VolumetricLightShaftsRenderPassSettings
    {
        [Min(1)] public int blurSamples;
        [Range(-1.0f, 1.0f)] public float blurDistance;
        [Range(0.0f, 1.0f)]  public float intensity;
        [Min(1e-5f)] public float alignmentFalloff;
        [Range(0.0f, 1.0f)] public float alignmentLowerEdge;
        [Range(0.0f, 1.0f)] public float alignmentUpperEdge;
        public Texture2D jitterTexture;
        [Min(0.0f)] public float jitterFactor;
        [Min(0)] public int downsamples;
        public bool lowerTextureTo16Bit;
        public bool useFixedLength;
        public bool useAdditionalBlurring;
    }

    [SerializeField]
    private VolumetricLightShaftsRenderPassSettings _settings = new VolumetricLightShaftsRenderPassSettings()
    {
        blurSamples = 8,
        blurDistance = -0.5f,
        intensity = 0.65f,
        alignmentFalloff = 2.0f,
        alignmentLowerEdge = 0.35f,
        alignmentUpperEdge = 0.65f,
    };

    private VolumetricLightShaftsRenderPass _volumetricLightShaftsRenderPass;

    public override void Create()
    {
        _volumetricLightShaftsRenderPass = new VolumetricLightShaftsRenderPass(_settings);

        _volumetricLightShaftsRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_volumetricLightShaftsRenderPass);
    }
}


