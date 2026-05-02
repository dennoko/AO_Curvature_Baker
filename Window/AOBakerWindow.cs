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
        private string L(string key) => LocalizationManager.Get(key);

        private GUIStyle actionButtonStyle;
        private Vector2 _scrollPosition;
        
        private bool _showAdvancedSettings  = false;

        [MenuItem("dennokoworks/Fast AO Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<AOBakerWindow>("Fast AO Baker");
            window.minSize = new Vector2(400, 600);
        }

        private void OnEnable()
        {
            LocalizationManager.Initialize();
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

            _statusMessage = L(state.StatusMessage);
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
                _statusMessage   = L("Status_Ready");
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
            GUILayout.Label("Fast AO Baker", UniTexTheme.TitleStyle);
            GUILayout.FlexibleSpace();

            // Language Switch
            DrawLanguageSwitch();

            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawSeparator();
        }

        private void DrawLanguageSwitch()
        {
            var current = LocalizationManager.CurrentLanguage;
            
            EditorGUI.BeginChangeCheck();
            
            GUILayout.BeginHorizontal();
            
            bool isJa = GUILayout.Toggle(current == Language.Japanese, "JA", UniTexTheme.MiniButtonLeftStyle, GUILayout.Width(30));
            bool isEn = GUILayout.Toggle(current == Language.English, "EN", UniTexTheme.MiniButtonRightStyle, GUILayout.Width(30));
            
            if (isJa && current != Language.Japanese)
            {
                LocalizationManager.CurrentLanguage = Language.Japanese;
            }
            else if (isEn && current != Language.English)
            {
                LocalizationManager.CurrentLanguage = Language.English;
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }
            
            GUILayout.EndHorizontal();
        }

        private void DrawSettingsArea(BakeState state)
        {
            GUILayout.BeginVertical();

            DrawSection(L("Section_Target"), () =>
            {
                DrawTargetMeshSlots(state);
            });

            DrawSection(L("Section_Occluder"), () =>
            {
                DrawOccluderMeshSlots(state);
            });
            
            DrawSection(L("Section_BasicAO"), () =>
            {
                EditorGUI.BeginChangeCheck();

                bool useSelf   = EditorGUILayout.Toggle(L("Label_SelfOcclusion"),   state.AOSettings.UseSelfOcclusion);
                bool useMutual = EditorGUILayout.Toggle(L("Label_MutualOcclusion"), state.AOSettings.UseMutualOcclusion);
                bool lowRes    = EditorGUILayout.Toggle(
                    new GUIContent(L("Label_LowResource"),
                        L("Tooltip_LowResource")),
                    state.AOSettings.LowResourceMode);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(
                        state.AOSettings.With(useSelfOcclusion: useSelf, useMutualOcclusion: useMutual, lowResourceMode: lowRes)));
                }

                if (state.AOSettings.LowResourceMode)
                {
                    EditorGUILayout.HelpBox(
                        L("Help_LowResource"),
                        MessageType.Info);
                }
            });

            DrawToggleSection(L("Section_Advanced"), _showAdvancedSettings, val => _showAdvancedSettings = val, () =>
            {
                EditorGUI.BeginChangeCheck();

                int rayCount = EditorGUILayout.IntSlider(L("Label_RayCount"), state.AOSettings.RayCount, 16, 512);
                float maxDistance = EditorGUILayout.Slider(L("Label_MaxDistance"), state.AOSettings.MaxDistance, 0.1f, 100f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(rayCount: rayCount, maxDistance: maxDistance)));
                }
            }, onReset: () =>
            {
                BakeStore.Dispatch(new UpdateAOSettingsAction(new AOSettings()));
            });

            DrawToggleSection(L("Section_SVGF"), state.AOSettings.DenoiseEnabled, 
                val => BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(denoiseEnabled: val))),
                () =>
            {
                EditorGUI.BeginChangeCheck();

                int   iterations = EditorGUILayout.IntSlider(
                    new GUIContent(L("Label_Iterations"), L("Tooltip_Iterations")),
                    state.AOSettings.DenoiseIterations, 1, 5);
                float sigmaPos   = EditorGUILayout.Slider(
                    new GUIContent(L("Label_SigmaPos"), L("Tooltip_SigmaPos")),
                    state.AOSettings.DenoiseSigmaPos, 0.01f, 5f);
                float sigmaNrm   = EditorGUILayout.Slider(
                    new GUIContent(L("Label_SigmaNrm"), L("Tooltip_SigmaNrm")),
                    state.AOSettings.DenoiseSigmaNrm, 1f, 256f);
                float sigmaLum   = EditorGUILayout.Slider(
                    new GUIContent(L("Label_SigmaLum"), L("Tooltip_SigmaLum")),
                    state.AOSettings.DenoiseSigmaLum, 0.1f, 10f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateAOSettingsAction(state.AOSettings.With(
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

            /* Curvature function is hidden for now, but logic is preserved for future use.
            DrawToggleSection(L("Section_Curvature"), state.CurvatureSettings.BakeEnabled, 
                val => BakeStore.Dispatch(new UpdateCurvatureSettingsAction(state.CurvatureSettings.With(bakeEnabled: val))),
                () =>
            {
                EditorGUI.BeginChangeCheck();

                CurvatureMode mode = (CurvatureMode)EditorGUILayout.EnumPopup(
                    new GUIContent(L("Label_Mode"),
                        L("Tooltip_Mode")),
                    state.CurvatureSettings.Mode);

                float strength = EditorGUILayout.Slider(
                    new GUIContent(L("Label_Strength"), L("Tooltip_Strength")),
                    state.CurvatureSettings.Strength, 0.01f, 10f);

                float bias = EditorGUILayout.Slider(
                    new GUIContent(L("Label_Bias"), L("Tooltip_Bias")),
                    state.CurvatureSettings.Bias, 0f, 1f);

                if (EditorGUI.EndChangeCheck())
                {
                    BakeStore.Dispatch(new UpdateCurvatureSettingsAction(
                        state.CurvatureSettings.With(
                            mode:        mode,
                            strength:    strength,
                            bias:        bias)));
                }
            }, onReset: () =>
            {
                BakeStore.Dispatch(new UpdateCurvatureSettingsAction(new CurvatureSettings()));
            });
            */

            DrawSection(L("Section_Output"), () =>
            {
                DrawOutputSettings(state);
            });

            GUILayout.EndVertical();
        }

        private void DrawProgressArea(BakeState state)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);
            GUILayout.Label(L("Status_Baking"), UniTexTheme.SectionHeaderStyle);
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
            GUILayout.Label(L("Label_OutputReady"), UniTexTheme.CaptionStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawSeparator();

            using (new EditorGUI.DisabledGroupScope(state.TargetMeshes.Count == 0))
            {
                if (GUILayout.Button(L("Button_BakeNow"), actionButtonStyle))
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

        private void DrawToggleSection(string title, bool toggle, System.Action<bool> onToggleChanged, System.Action content, System.Action onReset = null)
        {
            GUILayout.BeginVertical(UniTexTheme.CardStyle);

            GUILayout.BeginHorizontal();

            var headerStyle = toggle ? UniTexTheme.ToggleSectionOnStyle : UniTexTheme.ToggleSectionOffStyle;

            EditorGUI.BeginChangeCheck();
            bool newToggle = EditorGUILayout.ToggleLeft(title, toggle, headerStyle, GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                onToggleChanged?.Invoke(newToggle);
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
                L("Label_AddTarget"), null, typeof(GameObject), true);
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
                L("Label_AddOccluder"), null, typeof(GameObject), true);
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
                if (GUILayout.Button(L("Button_ClearAll"), UniTexTheme.SecondaryButtonStyle))
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

        private List<int> GetAvailableUVIndices(IReadOnlyList<GameObject> targets)
        {
            var indices = new HashSet<int>();
            if (targets == null) return new List<int>();

            foreach (var go in targets)
            {
                if (go == null) continue;
                
                Mesh mesh = null;
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                
                if (mesh == null)
                {
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null) mesh = smr.sharedMesh;
                }

                if (mesh == null) continue;

                for (int i = 0; i < 8; i++)
                {
                    var uvs = new List<Vector2>();
                    mesh.GetUVs(i, uvs);
                    if (uvs.Count > 0) indices.Add(i);
                }
            }
            
            var result = new List<int>(indices);
            result.Sort();
            return result;
        }

        // ---- Output Settings UI ----

        private static readonly int[] ResolutionOptions = { 128, 256, 512, 1024, 2048, 4096 };
        private static readonly string[] ResolutionLabels = { "128", "256", "512", "1024", "2048", "4096" };

        private bool MeshHasUV(GameObject go, int channel)
        {
            if (go == null) return false;
            Mesh mesh = null;
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            if (mesh == null)
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) mesh = smr.sharedMesh;
            }
            if (mesh == null) return false;
            var uvs = new List<Vector2>();
            mesh.GetUVs(channel, uvs);
            return uvs.Count > 0;
        }

        private void DrawOutputSettings(BakeState state)
        {
            var settings = state.OutputSettings;
            EditorGUI.BeginChangeCheck();

            // Resolution popup
            int currentIndex = System.Array.IndexOf(ResolutionOptions, settings.OutputResolution);
            if (currentIndex < 0) currentIndex = 3; // fallback to 1024
            int newIndex = EditorGUILayout.Popup(
                new GUIContent(L("Label_OutputRes"),
                    L("Tooltip_OutputRes")),
                currentIndex, ResolutionLabels);
            int newResolution = ResolutionOptions[newIndex];

            // UV channel selector: index 0 = Auto (-1), others = available UVs
            var availableIndices = GetAvailableUVIndices(state.TargetMeshes);
            var dynamicLabels = new List<string> { "Auto" };
            var dynamicValues = new List<int> { -1 };
            
            foreach (int idx in availableIndices)
            {
                dynamicLabels.Add($"UV{idx}");
                dynamicValues.Add(idx);
            }

            int currentUV = settings.UVChannel;
            int popupIndex = dynamicValues.IndexOf(currentUV);
            if (popupIndex < 0) popupIndex = 0; // Fallback to Auto if current is not in list

            int newPopupIndex = EditorGUILayout.Popup(
                new GUIContent(L("Label_UVChannel"), L("Tooltip_UVChannel")),
                popupIndex, dynamicLabels.ToArray());
            
            int newUVChannel = dynamicValues[newPopupIndex];

            // Warning if selected UV is missing from any target mesh
            if (newUVChannel >= 0 && state.TargetMeshes.Count > 0)
            {
                bool missingAny = false;
                foreach (var go in state.TargetMeshes)
                {
                    if (go != null && !MeshHasUV(go, newUVChannel))
                    {
                        missingAny = true;
                        break;
                    }
                }
                if (missingAny)
                {
                    EditorGUILayout.HelpBox(L("Help_UVChannelMissing"), MessageType.Warning);
                }
            }

            // Output folder
            GUILayout.BeginHorizontal();
            string displayFolder = string.IsNullOrEmpty(settings.OutputFolder) ? "(Auto)" : settings.OutputFolder;
            EditorGUILayout.TextField(
                new GUIContent(L("Label_OutputFolder"),
                    L("Tooltip_OutputFolder")),
                displayFolder);

            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert absolute path to Assets-relative path
                    string dataPath = Application.dataPath;
                    if (selected.StartsWith(dataPath))
                    {
                        selected = "Assets" + selected.Substring(dataPath.Length);
                    }
                    BakeStore.Dispatch(new UpdateOutputSettingsAction(
                        settings.With(outputFolder: selected)));
                    GUIUtility.ExitGUI();
                }
            }

            if (!string.IsNullOrEmpty(settings.OutputFolder))
            {
                if (GUILayout.Button("×", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    BakeStore.Dispatch(new UpdateOutputSettingsAction(
                        settings.With(outputFolder: "")));
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.EndHorizontal();

            // Overwrite toggle
            bool overwrite = EditorGUILayout.Toggle(
                new GUIContent(L("Label_Overwrite"),
                    L("Tooltip_Overwrite")),
                settings.OverwriteExisting);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(L("Label_PostProcess"), EditorStyles.boldLabel);

            // Dilation
            int dilation = EditorGUILayout.IntSlider(
                new GUIContent(L("Label_Dilation"),
                    L("Tooltip_Dilation")),
                settings.DilationPixels, 0, 32);

            // Shadow colour
            Color shadowColor = EditorGUILayout.ColorField(
                new GUIContent(L("Label_ShadowColor"),
                    L("Tooltip_ShadowColor")),
                settings.ShadowColor);

            // Gaussian blur
            bool blurEnabled = EditorGUILayout.Toggle(
                new GUIContent(L("Label_GaussianBlur"),
                    L("Tooltip_GaussianBlur")),
                settings.GaussianBlurEnabled);

            int blurPasses;
            using (new EditorGUI.DisabledGroupScope(!blurEnabled))
            {
                blurPasses = EditorGUILayout.IntSlider(
                    new GUIContent(L("Label_BlurPasses"),
                        L("Tooltip_BlurPasses")),
                    settings.GaussianBlurPasses, 1, 10);
            }

            if (EditorGUI.EndChangeCheck())
            {
                BakeStore.Dispatch(new UpdateOutputSettingsAction(
                    settings.With(
                        outputResolution:    newResolution,
                        overwriteExisting:   overwrite,
                        dilationPixels:      dilation,
                        shadowColor:         shadowColor,
                        gaussianBlurEnabled: blurEnabled,
                        gaussianBlurPasses:  blurPasses,
                        uvChannel:           newUVChannel)));
            }
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, UniTexTheme.Outline);
            EditorGUILayout.Space(4);
        }
        
    }
}
