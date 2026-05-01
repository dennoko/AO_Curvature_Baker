using System.Collections.Generic;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public enum CurvatureMode { Mean = 0, Gaussian = 1 }

    public class OutputSettings
    {
        /// <summary>Output texture resolution (128, 256, 512, 1024, 2048, 4096).</summary>
        public int    OutputResolution  { get; }
        /// <summary>Asset-relative output folder path (e.g. "Assets/Textures/BakedAO").</summary>
        public string OutputFolder      { get; }
        /// <summary>If true, overwrite existing files with the same name.</summary>
        public bool   OverwriteExisting { get; }
        /// <summary>Pixels to expand the AO outward from UV island edges (0 = disabled).</summary>
        public int    DilationPixels    { get; }
        /// <summary>Color of fully occluded (shadow) areas. Defaults to black.</summary>
        public Color  ShadowColor       { get; }
        /// <summary>Apply a 9x9 Gaussian blur to the AO output.</summary>
        public bool   GaussianBlurEnabled { get; }
        /// <summary>Number of Gaussian blur passes (higher = stronger blur).</summary>
        public int    GaussianBlurPasses  { get; }

        public OutputSettings(
            int    outputResolution    = 1024,
            string outputFolder        = "",
            bool   overwriteExisting   = false,
            int    dilationPixels      = 8,
            Color? shadowColor         = null,
            bool   gaussianBlurEnabled = true,
            int    gaussianBlurPasses  = 2)
        {
            OutputResolution    = outputResolution;
            OutputFolder        = outputFolder ?? "";
            OverwriteExisting   = overwriteExisting;
            DilationPixels      = Mathf.Clamp(dilationPixels, 0, 32);
            ShadowColor         = shadowColor ?? Color.black;
            GaussianBlurEnabled = gaussianBlurEnabled;
            GaussianBlurPasses  = Mathf.Clamp(gaussianBlurPasses, 1, 10);
        }

        public OutputSettings With(
            int?    outputResolution    = null,
            string  outputFolder        = null,
            bool?   overwriteExisting   = null,
            int?    dilationPixels      = null,
            Color?  shadowColor         = null,
            bool?   gaussianBlurEnabled = null,
            int?    gaussianBlurPasses  = null)
        {
            return new OutputSettings(
                outputResolution    ?? OutputResolution,
                outputFolder        ?? OutputFolder,
                overwriteExisting   ?? OverwriteExisting,
                dilationPixels      ?? DilationPixels,
                shadowColor         ?? ShadowColor,
                gaussianBlurEnabled ?? GaussianBlurEnabled,
                gaussianBlurPasses  ?? GaussianBlurPasses);
        }
    }

    public class CurvatureSettings
    {
        public bool          BakeEnabled { get; }
        public CurvatureMode Mode        { get; }
        public float         Strength    { get; }
        public float         Bias        { get; }

        public CurvatureSettings(
            bool          bakeEnabled = false,
            CurvatureMode mode        = CurvatureMode.Mean,
            float         strength    = 1.0f,
            float         bias        = 0.5f)
        {
            BakeEnabled = bakeEnabled;
            Mode        = mode;
            Strength    = strength;
            Bias        = bias;
        }

        public CurvatureSettings With(
            bool?          bakeEnabled = null,
            CurvatureMode? mode        = null,
            float?         strength    = null,
            float?         bias        = null)
        {
            return new CurvatureSettings(
                bakeEnabled ?? BakeEnabled,
                mode        ?? Mode,
                strength    ?? Strength,
                bias        ?? Bias);
        }
    }

    public class AOSettings
    {
        public bool  UseSelfOcclusion   { get; }
        public bool  UseMutualOcclusion { get; }
        public int   RayCount           { get; }
        public float MaxDistance        { get; }
        public bool  LowResourceMode    { get; }

        // SVGF denoising
        public bool  DenoiseEnabled    { get; }
        public int   DenoiseIterations { get; }
        public float DenoiseSigmaPos   { get; }
        public float DenoiseSigmaNrm   { get; }
        public float DenoiseSigmaLum   { get; }

        public AOSettings(
            bool  useSelfOcclusion   = true,
            bool  useMutualOcclusion = true,
            int   rayCount           = 64,
            float maxDistance        = 1.0f,
            bool  lowResourceMode    = false,
            bool  denoiseEnabled     = true,
            int   denoiseIterations  = 4,
            float denoiseSigmaPos    = 1.0f,
            float denoiseSigmaNrm    = 128f,
            float denoiseSigmaLum    = 4.0f)
        {
            UseSelfOcclusion   = useSelfOcclusion;
            UseMutualOcclusion = useMutualOcclusion;
            RayCount           = rayCount;
            MaxDistance        = maxDistance;
            LowResourceMode    = lowResourceMode;
            DenoiseEnabled     = denoiseEnabled;
            DenoiseIterations  = denoiseIterations;
            DenoiseSigmaPos    = denoiseSigmaPos;
            DenoiseSigmaNrm    = denoiseSigmaNrm;
            DenoiseSigmaLum    = denoiseSigmaLum;
        }

        public AOSettings With(
            bool?  useSelfOcclusion   = null,
            bool?  useMutualOcclusion = null,
            int?   rayCount           = null,
            float? maxDistance        = null,
            bool?  lowResourceMode    = null,
            bool?  denoiseEnabled     = null,
            int?   denoiseIterations  = null,
            float? denoiseSigmaPos    = null,
            float? denoiseSigmaNrm    = null,
            float? denoiseSigmaLum    = null)
        {
            return new AOSettings(
                useSelfOcclusion   ?? UseSelfOcclusion,
                useMutualOcclusion ?? UseMutualOcclusion,
                rayCount           ?? RayCount,
                maxDistance        ?? MaxDistance,
                lowResourceMode    ?? LowResourceMode,
                denoiseEnabled     ?? DenoiseEnabled,
                denoiseIterations  ?? DenoiseIterations,
                denoiseSigmaPos    ?? DenoiseSigmaPos,
                denoiseSigmaNrm    ?? DenoiseSigmaNrm,
                denoiseSigmaLum    ?? DenoiseSigmaLum
            );
        }
    }

    public class BakeState
    {
        public IReadOnlyList<GameObject> TargetMeshes      { get; }
        public IReadOnlyList<GameObject> OccluderMeshes    { get; }
        public AOSettings                AOSettings         { get; }
        public CurvatureSettings         CurvatureSettings  { get; }
        public OutputSettings            OutputSettings     { get; }
        public BakeStatus                Status             { get; }
        public float                     Progress           { get; }
        public string                    StatusMessage      { get; }

        public BakeState(
            IReadOnlyList<GameObject> targetMeshes     = null,
            IReadOnlyList<GameObject> occluderMeshes   = null,
            AOSettings               aoSettings        = null,
            CurvatureSettings        curvatureSettings = null,
            OutputSettings           outputSettings    = null,
            BakeStatus               status            = BakeStatus.None,
            float                    progress          = 0f,
            string                   statusMessage     = "Ready")
        {
            TargetMeshes      = targetMeshes     ?? new List<GameObject>().AsReadOnly();
            OccluderMeshes    = occluderMeshes   ?? new List<GameObject>().AsReadOnly();
            AOSettings        = aoSettings       ?? new AOSettings();
            CurvatureSettings = curvatureSettings ?? new CurvatureSettings();
            OutputSettings    = outputSettings   ?? new OutputSettings();
            Status            = status;
            Progress          = progress;
            StatusMessage     = statusMessage;
        }

        public BakeState With(
            IReadOnlyList<GameObject> targetMeshes     = null,
            IReadOnlyList<GameObject> occluderMeshes   = null,
            AOSettings               aoSettings        = null,
            CurvatureSettings        curvatureSettings = null,
            OutputSettings           outputSettings    = null,
            BakeStatus?              status            = null,
            float?                   progress          = null,
            string                   statusMessage     = null)
        {
            return new BakeState(
                targetMeshes      ?? TargetMeshes,
                occluderMeshes    ?? OccluderMeshes,
                aoSettings        ?? AOSettings,
                curvatureSettings ?? CurvatureSettings,
                outputSettings    ?? OutputSettings,
                status            ?? Status,
                progress          ?? Progress,
                statusMessage     ?? StatusMessage
            );
        }
    }
}
