using System;
using UnityEngine;

namespace DennokoWorks.Tool.AOBaker
{
    public static class BakeStore
    {
        public static BakeState State { get; private set; } = new BakeState();
        public static event Action OnStateChanged;

        public static void Dispatch(IAction action)
        {
            State = BakeReducer.Reduce(State, action);
            OnStateChanged?.Invoke();
        }
    }

    internal static class BakeReducer
    {
        public static BakeState Reduce(BakeState state, IAction action)
        {
            switch (action)
            {
                case SetTargetMeshesAction a:
                    return state.With(targetMeshes: a.Targets.AsReadOnly());

                case SetOccluderMeshesAction a:
                    return state.With(occluderMeshes: a.Occluders.AsReadOnly());
                case UpdateAOSettingsAction a:
                    return state.With(aoSettings: a.Settings);
                    
                case UpdateCurvatureSettingsAction a:
                    return state.With(curvatureSettings: a.Settings);

                case UpdateOutputSettingsAction a:
                    return state.With(outputSettings: a.Settings);
                case StartBakeAction _:
                    return state.With(status: BakeStatus.Baking, progress: 0f, statusMessage: "Status_BakingStarted");
                    
                case UpdateProgressAction a:
                    return state.With(status: a.Status, progress: a.Progress, statusMessage: a.Message);
                    
                case BakeCompletedAction _:
                    return state.With(status: BakeStatus.Completed, progress: 1f, statusMessage: "Status_CompletedSuccess");
                    
                case BakeErrorAction a:
                    return state.With(status: BakeStatus.Error, progress: 0f, statusMessage: a.ErrorMessage); // ErrorMessage is already localized or a key
                    
                default:
                    return state;
            }
        }
    }
}
