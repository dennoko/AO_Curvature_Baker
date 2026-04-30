using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DennokoWorks.Tool.AOBaker
{
    public class AOBakerWindow : EditorWindow
    {
        public enum StatusType { Info, Success, Error }
        private string     _statusMessage  = "Ready";
        private StatusType _statusType     = StatusType.Info;
        private double     _statusResetTime = -1.0;

        private GUIStyle actionButtonStyle;
        private Vector2 _scrollPosition;
        
        // Advanced settings toggle
        private bool _showAdvancedSettings = false;

        [MenuItem("dennokoworks/AO Curvature Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<AOBakerWindow>("AO/Curvature Baker");
            window.minSize = new Vector2(400, 600);
        }

        private void OnEnable()
        {
            BakeStore.OnStateChanged += RepaintWindowOnStateChange;
            UpdateStatusFromState();
        }

        private void OnDisable()
        {
            BakeStore.OnStateChanged -= RepaintWindowOnStateChange;
        }

        private void RepaintWindowOnStateChange()
        {
            UpdateStatusFromState();
            Repaint();
        }

        private void UpdateStatusFromState()
        {
            var state = BakeStore.State;
            StatusType type = StatusType.Info;
            if (state.Status == BakeStatus.Completed) type = StatusType.Success;
            if (state.Status == BakeStatus.Error) type = StatusType.Error;

            _statusMessage = state.StatusMessage;
            _statusType = type;
            
            // Auto reset for success based on theme defaults
            if (type == StatusType.Success)
            {
                _statusResetTime = EditorApplication.timeSinceStartup + 3.0;
            }
            else
            {
                _statusResetTime = -1.0;
            }
        }

        private void OnGUI()
        {
            if (_statusResetTime > 0 && EditorApplication.timeSinceStartup > _statusResetTime)
            {
                _statusMessage   = "Ready";
                _statusType      = StatusType.Info;
                _statusResetTime = -1.0;
            }

            UniTexTheme.Initialize();
            actionButtonStyle = UniTexTheme.ActionButtonStyle;

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), UniTexTheme.Surface0);

            var state = BakeStore.State;

            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            if (state.Status == BakeStatus.Baking || state.Status == BakeStatus.Denoising)
            {
                DrawProgressArea(state);
            }
            else
            {
                DrawSettingsArea(state);
            }
            
            EditorGUILayout.EndScrollView();

            if (state.Status != BakeStatus.Baking && state.Status != BakeStatus.Denoising)
            {
                DrawFooter(state);
            }
            
            DrawStatusBar();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Space(6);
            GUILayout.Label("AO / Curvature Baker", UniTexTheme.TitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawSeparator();
        }

        private void DrawSettingsArea(BakeState state)
        {
            GUILayout.BeginVertical();

            DrawSection("TARGET", () =>
            {
                // To keep it simple for now, we just present a list or single object slot.
                // Normally we'd use a serialized object or list, but for IMGUI direct we do this:
                GUILayout.Label($"Current Targets: {state.TargetMeshes.Count}", UniTexTheme.SecondaryTextStyle);
                
                if (GUILayout.Button("Select Meshes from Selection", UniTexTheme.SecondaryButtonStyle))
                {
                    List<GameObject> newTargets = new List<GameObject>();
                    foreach (var obj in Selection.gameObjects)
                    {
                        if (obj.GetComponentInChildren<MeshFilter>() != null || obj.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                        {
                            newTargets.Add(obj);
                        }
                    }
                    BakeStore.Dispatch(new SetTargetMeshesAction(newTargets));
                }
            });
            
            DrawSection("BASIC AO SETTINGS", () =>
            {
                EditorGUI.BeginChangeCheck();

                bool useSelf = EditorGUILayout.Toggle("Self Occlusion", state.AOSettings.UseSelfOcclusion);
                bool useMutual = EditorGUILayout.Toggle("Mutual Occlusion", state.AOSettings.UseMutualOcclusion);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(useSelfOcclusion: useSelf, useMutualOcclusion: useMutual)));
                }
            });

            DrawToggleSection("ADVANCED SETTINGS", ref _showAdvancedSettings, () =>
            {
                EditorGUI.BeginChangeCheck();

                int rayCount = EditorGUILayout.IntSlider("Ray Count", state.AOSettings.RayCount, 16, 512);
                float maxDistance = EditorGUILayout.Slider("Max Distance", state.AOSettings.MaxDistance, 0.1f, 100f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(rayCount: rayCount, maxDistance: maxDistance)));
                }
            }, onReset: () =>
            {
                BakeStore.Dispatch(new UpdateAOSettingsAction(new AOSettings()));
            });

            GUILayout.EndVertical();
        }

        private void DrawProgressArea(BakeState state)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);
            GUILayout.Label("BAKING IN PROGRESS...", UniTexTheme.SectionHeaderStyle);
            DrawSeparator();

            GUILayout.Label(state.StatusMessage, UniTexTheme.SecondaryTextStyle);
            
            Rect r = GUILayoutUtility.GetRect(18, 18, "TextField");
            EditorGUI.ProgressBar(r, state.Progress, $"{state.Progress * 100:0.0}%");
            
            GUILayout.EndVertical();
        }

        private void DrawFooter(BakeState state)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("OUTPUT: Ready", UniTexTheme.CaptionStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawSeparator();

            using (new EditorGUI.DisabledGroupScope(state.TargetMeshes.Count == 0))
            {
                if (GUILayout.Button("Bake Now", actionButtonStyle))
                {
                    BakeStore.Dispatch(new StartBakeAction());
                    // 実際には Orchestrator を呼ぶ。仮実装としてすぐにUpdateProgressActionなどを発火
                    RunFakeBakeProcess();
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            GUILayout.Box(_statusMessage, UniTexTheme.GetStatusStyle(this._statusType), GUILayout.ExpandWidth(true));
        }

        private void DrawSection(string title, System.Action content)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);
            GUILayout.Label(title, UniTexTheme.SectionHeaderStyle);
            DrawSeparator();
            content?.Invoke();
            GUILayout.EndVertical();
        }

        private void DrawToggleSection(string title, ref bool toggle, System.Action content, System.Action onReset = null)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);

            GUILayout.BeginHorizontal();

            var headerStyle = toggle ? UniTexTheme.ToggleSectionOnStyle : UniTexTheme.ToggleSectionOffStyle;

            EditorGUI.BeginChangeCheck();
            bool newToggle = EditorGUILayout.ToggleLeft(title, toggle, headerStyle, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                toggle = newToggle;
                Repaint();
            }

            if (onReset != null)
            {
                if (GUILayout.Button("Reset", UniTexTheme.MiniButtonStyle, GUILayout.Width(50)))
                {
                    onReset.Invoke();
                    GUI.FocusControl(null);
                }
            }

            GUILayout.EndHorizontal();

            DrawSeparator();

            using (new EditorGUI.DisabledGroupScope(!toggle))
            {
                content?.Invoke();
            }

            GUILayout.EndVertical();
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, UniTexTheme.Outline);
            EditorGUILayout.Space(4);
        }
        
        // Fake async bake for testing UI
        private async void RunFakeBakeProcess()
        {
            try
            {
                for (int i = 0; i <= 10; i++)
                {
                    await System.Threading.Tasks.Task.Delay(300);
                    if (EditorApplication.isPlayingOrWillChangePlaymode) break;
                    string msg = i < 5 ? "Computing AO rays..." : "Denoising via SVGF...";
                    BakeStatus s = i < 5 ? BakeStatus.Baking : BakeStatus.Denoising;
                    BakeStore.Dispatch(new UpdateProgressAction(s, i / 10f, msg));
                }
                
                BakeStore.Dispatch(new BakeCompletedAction());
            }
            catch (System.Exception ex)
            {
                BakeStore.Dispatch(new BakeErrorAction(ex.Message));
            }
        }
    }
}
