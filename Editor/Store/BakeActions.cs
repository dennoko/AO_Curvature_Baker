using System.Collections.Generic;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public interface IAction {}

    public class SetTargetMeshesAction : IAction
    {
        public List<GameObject> Targets { get; }
        public SetTargetMeshesAction(List<GameObject> targets)
        {
            Targets = targets;
        }
    }

    public class UpdateAOSettingsAction : IAction
    {
        public AOSettings Settings { get; }
        public UpdateAOSettingsAction(AOSettings settings)
        {
            Settings = settings;
        }
    }

    public class StartBakeAction : IAction {}

    public class UpdateProgressAction : IAction
    {
        public BakeStatus Status { get; }
        public float Progress { get; }
        public string Message { get; }

        public UpdateProgressAction(BakeStatus status, float progress, string message)
        {
            Status = status;
            Progress = progress;
            Message = message;
        }
    }

    public class BakeCompletedAction : IAction {}
    
    public class BakeErrorAction : IAction
    {
        public string ErrorMessage { get; }
        public BakeErrorAction(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }
}
