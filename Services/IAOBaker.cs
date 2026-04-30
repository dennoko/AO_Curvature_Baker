using System;
using System.Threading.Tasks;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public interface IAOBaker
    {
        Task<RenderTexture> ComputeAOAsync(
            BakeContext context,
            AOSettings settings,
            IProgress<(float progress, string message)> progress);
    }
}
