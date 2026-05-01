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
        
        private bool _showAdvancedSettings  = false;
        private bool _showDenoiseSettings   = false;
        private bool _showCurvatureSettings = false;

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
                DrawTargetMeshSlots(state);
            });

            DrawSection("OCCLUDER MESHES", () =>
            {
                DrawOccluderMeshSlots(state);
            });
            
            DrawSection("BASIC AO SETTINGS", () =>
            {
                EditorGUI.BeginChangeCheck();

                bool useSelf   = EditorGUILayout.Toggle("Self Occlusion",   state.AOSettings.UseSelfOcclusion);
                bool useMutual = EditorGUILayout.Toggle("Mutual Occlusion", state.AOSettings.UseMutualOcclusion);
                bool lowRes    = EditorGUILayout.Toggle(
                    new GUIContent("Low Resource Mode",
                        "Reduces GPU load by using smaller dispatch tiles and syncing after every tile.\nBake time will be significantly longer."),
                    state.AOSettings.LowResourceMode);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(
                        state.AOSettings.With(useSelfOcclusion: useSelf, useMutualOcclusion: useMutual, lowResourceMode: lowRes)));
                }

                if (state.AOSettings.LowResourceMode)
                {
                    EditorGUILayout.HelpBox(
                        "Low Resource Mode: GPU dispatch tile size is fixed at 16 px and the GPU is flushed after every tile. Bake time will be significantly longer.",
                        MessageType.Info);
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

            DrawToggleSection("SVGF DENOISING", ref _showDenoiseSettings, () =>
            {
                EditorGUI.BeginChangeCheck();

                bool  enabled    = EditorGUILayout.Toggle("Enable Denoising", state.AOSettings.DenoiseEnabled);
                int   iterations = EditorGUILayout.IntSlider(
                    new GUIContent("Iterations", "Number of a-trous wavelet filter passes. Each pass doubles the effective kernel radius."),
                    state.AOSettings.DenoiseIterations, 1, 5);
                float sigmaPos   = EditorGUILayout.Slider(
                    new GUIContent("Sigma Position", "Position edge-stopping threshold (world units). Lower = sharper boundaries."),
                    state.AOSettings.DenoiseSigmaPos, 0.01f, 5f);
                float sigmaNrm   = EditorGUILayout.Slider(
                    new GUIContent("Sigma Normal", "Normal edge-stopping exponent. Higher = harder normal edges."),
                    state.AOSettings.DenoiseSigmaNrm, 1f, 256f);
                float sigmaLum   = EditorGUILayout.Slider(
                    new GUIContent("Sigma Luminance", "Luminance edge-stopping scale. Lower = preserve more AO contrast."),
                    state.AOSettings.DenoiseSigmaLum, 0.1f, 10f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(
                        denoiseEnabled:    enabled,
                        denoiseIterations: iterations,
                        denoiseSigmaPos:   sigmaPos,
                        denoiseSigmaNrm:   sigmaNrm,
                        denoiseSigmaLum:   sigmaLum)));
                }
            }, onReset: () =>
            {
                BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(
                    denoiseEnabled:    true,
                    denoiseIterations: 4,
                    denoiseSigmaPos:   1.0f,
                    denoiseSigmaNrm:   128f,
                    denoiseSigmaLum:   4.0f)));
            });

            DrawToggleSection("CURVATURE MAP", ref _showCurvatureSettings, () =>
            {
                EditorGUI.BeginChangeCheck();

                bool bakeEnabled = EditorGUILayout.Toggle(
                    new GUIContent("Bake Curvature", "Enable curvature map baking in addition to AO."),
                    state.CurvatureSettings.BakeEnabled);

                CurvatureMode mode = (CurvatureMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Mode",
                        "Mean Curvature: edge wear mask (smooth normals). " +
                        "Gaussian Curvature: saddle/dome detection."),
                    state.CurvatureSettings.Mode);

                float strength = EditorGUILayout.Slider(
                    new GUIContent("Strength", "Scales the curvature value before remapping. Higher = more contrast."),
                    state.CurvatureSettings.Strength, 0.01f, 10f);

                float bias = EditorGUILayout.Slider(
                    new GUIContent("Bias", "Output neutral point. 0.5 = flat surface maps to 50% gray."),
                    state.CurvatureSettings.Bias, 0f, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateCurvatureSettingsAction(
                        state.CurvatureSettings.With(
                            bakeEnabled: bakeEnabled,
                            mode:        mode,
                            strength:    strength,
                            bias:        bias)));
                }
            }, onReset: () =>
            {
                BakeStore.Dispatch(new UpdateCurvatureSettingsAction(new CurvatureSettings()));
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
                    var snapshot = BakeStore.State;
                    BakeStore.Dispatch(new StartBakeAction());
                    _ = new BakeOrchestrator().ExecuteBakePipelineAsync(snapshot);
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

        /// <summary>
        /// TARGET section: ObjectField slots for target meshes.
        /// Each slot can receive drag-and-drop directly.
        /// </summary>
        private void DrawTargetMeshSlots(BakeState state)
        {
            var list = new List<GameObject>(state.TargetMeshes);

            // Existing targets as ObjectField rows with remove button
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var newObj = (GameObject)EditorGUILayout.ObjectField(
                    list[i], typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newObj != null && !HasMeshComponent(newObj))
                    {
                        Debug.LogWarning($"[AO Baker] '{newObj.name}' has no MeshFilter or SkinnedMeshRenderer.");
                    }
                    else
                    {
                        list[i] = newObj;
                        list.RemoveAll(go => go == null);
                        BakeStore.Dispatch(new SetTargetMeshesAction(list));
                        GUIUtility.ExitGUI();
                    }
                }

                if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    list.RemoveAt(i);
                    BakeStore.Dispatch(new SetTargetMeshesAction(list));
                    GUIUtility.ExitGUI();
                }

                GUILayout.EndHorizontal();
            }

            // Spacing between existing items and the empty slot
            if (list.Count > 0)
                EditorGUILayout.Space(4);

            // Empty slot for adding a new target via drag-and-drop
            EditorGUI.BeginChangeCheck();
            var addObj = (GameObject)EditorGUILayout.ObjectField(
                "Add Target", null, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && addObj != null)
            {
                if (!HasMeshComponent(addObj))
                {
                    Debug.LogWarning($"[AO Baker] '{addObj.name}' has no MeshFilter or SkinnedMeshRenderer.");
                }
                else if (!list.Contains(addObj))
                {
                    list.Add(addObj);
                    BakeStore.Dispatch(new SetTargetMeshesAction(list));
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>
        /// OCCLUDER section: registered occluders shown as a list,
        /// with an empty ObjectField slot at the bottom for adding new ones.
        /// </summary>
        private void DrawOccluderMeshSlots(BakeState state)
        {
            var list = new List<GameObject>(state.OccluderMeshes);

            // Existing occluders as ObjectField rows with remove button
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                var newObj = (GameObject)EditorGUILayout.ObjectField(
                    list[i], typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newObj != null && !HasMeshComponent(newObj))
                    {
                        Debug.LogWarning($"[AO Baker] '{newObj.name}' has no MeshFilter or SkinnedMeshRenderer.");
                    }
                    else
                    {
                        list[i] = newObj;
                        list.RemoveAll(go => go == null);
                        BakeStore.Dispatch(new SetOccluderMeshesAction(list));
                        GUIUtility.ExitGUI();
                    }
                }

                if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    list.RemoveAt(i);
                    BakeStore.Dispatch(new SetOccluderMeshesAction(list));
                    GUIUtility.ExitGUI();
                }

                GUILayout.EndHorizontal();
            }

            // Spacing between existing items and the empty slot
            if (list.Count > 0)
                EditorGUILayout.Space(4);

            // Empty slot for adding a new occluder via drag-and-drop
            EditorGUI.BeginChangeCheck();
            var addObj = (GameObject)EditorGUILayout.ObjectField(
                "Add Occluder", null, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && addObj != null)
            {
                if (!HasMeshComponent(addObj))
                {
                    Debug.LogWarning($"[AO Baker] '{addObj.name}' has no MeshFilter or SkinnedMeshRenderer.");
                }
                else if (!list.Contains(addObj))
                {
                    list.Add(addObj);
                    BakeStore.Dispatch(new SetOccluderMeshesAction(list));
                    GUIUtility.ExitGUI();
                }
            }

            // Clear all button
            if (list.Count > 1)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Clear All", UniTexTheme.SecondaryButtonStyle))
                {
                    BakeStore.Dispatch(new SetOccluderMeshesAction(new List<GameObject>()));
                }
            }
        }

        private static bool HasMeshComponent(GameObject go)
        {
            return go.GetComponentInChildren<MeshFilter>() != null
                || go.GetComponentInChildren<SkinnedMeshRenderer>() != null;
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, UniTexTheme.Outline);
            EditorGUILayout.Space(4);
        }
        
    }
}
