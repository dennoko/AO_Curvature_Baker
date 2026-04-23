using System.Collections.Generic;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public class AOSettings
    {
        public bool UseSelfOcclusion { get; }
        public bool UseMutualOcclusion { get; }
        public int RayCount { get; }
        public float MaxDistance { get; }
        
        public AOSettings(bool useSelfOcclusion = true, bool useMutualOcclusion = true, int rayCount = 64, float maxDistance = 1.0f)
        {
            UseSelfOcclusion = useSelfOcclusion;
            UseMutualOcclusion = useMutualOcclusion;
            RayCount = rayCount;
            MaxDistance = maxDistance;
        }

        public AOSettings With(
            bool? useSelfOcclusion = null,
            bool? useMutualOcclusion = null,
            int? rayCount = null,
            float? maxDistance = null)
        {
            return new AOSettings(
                useSelfOcclusion ?? UseSelfOcclusion,
                useMutualOcclusion ?? UseMutualOcclusion,
                rayCount ?? RayCount,
                maxDistance ?? MaxDistance
            );
        }
    }

    public class BakeState
    {
        public IReadOnlyList<GameObject> TargetMeshes { get; }
        public AOSettings AOSettings { get; }
        public BakeStatus Status { get; }
        public float Progress { get; }
        public string StatusMessage { get; }

        public BakeState(
            IReadOnlyList<GameObject> targetMeshes = null,
            AOSettings aoSettings = null,
            BakeStatus status = BakeStatus.None,
            float progress = 0f,
            string statusMessage = "Ready")
        {
            TargetMeshes = targetMeshes ?? new List<GameObject>().AsReadOnly();
            AOSettings = aoSettings ?? new AOSettings();
            Status = status;
            Progress = progress;
            StatusMessage = statusMessage;
        }

        public BakeState With(
            IReadOnlyList<GameObject> targetMeshes = null,
            AOSettings aoSettings = null,
            BakeStatus? status = null,
            float? progress = null,
            string statusMessage = null)
        {
            return new BakeState(
                targetMeshes ?? TargetMeshes,
                aoSettings ?? AOSettings,
                status ?? Status,
                progress ?? Progress,
                statusMessage ?? StatusMessage
            );
        }
    }
}
