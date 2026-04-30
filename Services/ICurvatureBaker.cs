using System;
using System.Threading.Tasks;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public interface ICurvatureBaker
    {
        Task<RenderTexture> ComputeCurvatureAsync(
            BakeContext context,
            CurvatureSettings settings,
            IProgress<(float progress, string message)> progress);
    }
}
