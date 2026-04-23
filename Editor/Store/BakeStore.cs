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
                    
                case UpdateAOSettingsAction a:
                    return state.With(aoSettings: a.Settings);
                    
                case StartBakeAction _:
                    return state.With(status: BakeStatus.Baking, progress: 0f, statusMessage: "Baking started...");
                    
                case UpdateProgressAction a:
                    return state.With(status: a.Status, progress: a.Progress, statusMessage: a.Message);
                    
                case BakeCompletedAction _:
                    return state.With(status: BakeStatus.Completed, progress: 1f, statusMessage: "Bake Completed Successfully.");
                    
                case BakeErrorAction a:
                    return state.With(status: BakeStatus.Error, progress: 0f, statusMessage: $"Error: {a.ErrorMessage}");
                    
                default:
                    return state;
            }
        }
    }
}
