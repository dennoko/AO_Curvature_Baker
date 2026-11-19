using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// Fast AO Baker のウィンドウ。dennokoworks デザインシステムの画面パターン P1 (変換) で組む:
    ///   標準ヘッダー → B1 対象カード (ターゲット / 遮蔽物) → B2 設定カード (基本AO / ノイズ除去 / 仕上げ)
    ///   → 出力ドック → フッター → ステータス。
    /// 状態は BakeStore が持ち、UI は OnStateChanged で値を書き戻すだけ (変更はすべて Dispatch 経由)。
    /// </summary>
    public sealed class AOBakerWindow : EditorWindow
    {
        private const string ToolName = "Fast AO Baker";

        // UXML / USS の GUID (.meta と一致させる)。USS はテーマ → ブロック → ツール固有の順に読む
        private const string UXML_GUID       = "44b298b1c92c4630825a3386e89ccc25";
        private const string USS_GUID        = "7061579f741c4a14b0d6f6f685d3833b";
        private const string BLOCKS_USS_GUID = "99ee885fa97d4c4dbef460d6eefa0b7c";
        private const string TOOL_USS_GUID   = "3d9a21a2d6b64061afcc8fdc7c998550";

        private const string DropOverClass  = "fao-drop--over";
        private const string CollapsedClass = "dennoko-toggle-section--collapsed";

        private static readonly int[] ResolutionOptions = { 128, 256, 512, 1024, 2048, 4096 };

        private static string L(string key) => LocalizationManager.Get(key);
        private static string LF(string key, params object[] args) => string.Format(L(key), args);

        [SerializeField] private bool _advancedOpen;

        private CancellationTokenSource _bakeCts;
        private BakeStatus _lastStatus = BakeStatus.None;
        private bool _uiReady;

        private DennokoAppBar _appBar;
        private DennokoStatus _status;
        private Label _versionLabel;
        private DennokoVersionChecker.Result _versionResult = new DennokoVersionChecker.Result
        {
            State = DennokoVersionChecker.State.Checking,
            LocalVersion = "0.0.0"
        };
        private ScrollView _scroll;

        // B1 対象カード
        private readonly ObjectListCard _targetCard = new ObjectListCard();
        private readonly ObjectListCard _occluderCard = new ObjectListCard();
        private VisualElement _targetInfo;
        private Label _targetInfoCount;
        private Label _targetInfoUV;
        private VisualElement _targetWarning;
        private Label _targetWarningText;
        private Button _targetFixButton;
        private List<Mesh> _unreadable = new List<Mesh>();

        // B2 基本AO
        private Label _aoCardTitle;
        private Button _aoCardReset;
        private DropdownField _resolutionField;
        private DropdownField _uvChannelField;
        private List<int> _uvChannelValues = new List<int>();
        private VisualElement _uvWarning;
        private Label _uvWarningText;
        private Button _uvWarningFix;
        private Toggle _selfToggle;
        private Toggle _mutualToggle;
        private Toggle _lowResourceToggle;
        private HelpBox _lowResourceHelp;
        private VisualElement _advancedDetails;
        private Label _advancedTitle;
        private VisualElement _advancedChips;
        private SliderInt _rayCountSlider;
        private Slider _maxDistanceSlider;
        private Slider _biasSlider;
        private Toggle _ignoreBackfacesToggle;

        // B2 ノイズ除去
        private VisualElement _denoiseCard;
        private Toggle _denoiseToggle;
        private Button _denoiseCardReset;
        private SliderInt _denoiseIterationsSlider;
        private Slider _sigmaPosSlider;
        private Slider _sigmaNrmSlider;
        private Slider _sigmaLumSlider;

        // B2 仕上げ
        private Label _postCardTitle;
        private Button _postCardReset;
        private SliderInt _dilationSlider;
        private ColorField _shadowColorField;
        private Toggle _blurToggle;
        private VisualElement _blurGroup;
        private SliderInt _blurPassesSlider;

        // B5 出力ドック + フッター
        private VisualElement _outputDock;
        private Label _outputPathLabel;
        private TextField _outputPathField;
        private Button _outputBrowse;
        private Button _outputAuto;
        private Toggle _overwriteToggle;
        private Button _cancelButton;
        private Button _bakeButton;

        [MenuItem("dennokoworks/Fast AO Baker")]
        public static void ShowWindow()
        {
            var window = GetWindow<AOBakerWindow>(ToolName);
            window.titleContent = new GUIContent(ToolName);
            window.minSize = new Vector2(360, 480);
        }

        private void OnEnable()
        {
            LocalizationManager.Initialize();
            BakeStore.OnStateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            BakeStore.OnStateChanged -= OnStateChanged;
        }

        // Read/Write をインポート設定から直された場合などに備え、フォーカスで対象の情報を取り直す
        private void OnFocus()
        {
            if (_uiReady && !IsBaking) RefreshObjectInfo();
        }

        private static bool IsBaking
        {
            get
            {
                var s = BakeStore.State.Status;
                return s == BakeStatus.Baking || s == BakeStatus.Denoising;
            }
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _uiReady = false;

            // テーマ非依存のためのルートクラス。USS ロード失敗時も背景が明るくならないよう Surface0 を保証
            root.AddToClassList("dennoko-root");
            root.style.backgroundColor = (Color)new Color32(0x12, 0x12, 0x12, 0xFF);
            root.style.flexGrow = 1;

            // 標準フォント: OS のメイリオを全体に適用
            DennokoUIFont.Apply(root);

            AddStyleSheet(root, USS_GUID, "DennokoTheme");
            AddStyleSheet(root, BLOCKS_USS_GUID, "DennokoBlocks");
            AddStyleSheet(root, TOOL_USS_GUID, "FastAOBaker");

            var uxml = LoadAsset<VisualTreeAsset>(UXML_GUID, "AOBakerWindow t:VisualTreeAsset");
            if (uxml == null)
            {
                root.Add(new Label("UXML Asset が見つかりません。GUID を確認してください。"));
                return;
            }
            uxml.CloneTree(root);

            InitializeUI(root);
            _uiReady = true;

            _lastStatus = BakeStore.State.Status;
            ApplyLanguage();
            StartVersionCheck();
        }

        private static void AddStyleSheet(VisualElement root, string guid, string fallbackName)
        {
            var uss = LoadAsset<StyleSheet>(guid, fallbackName + " t:StyleSheet");
            if (uss != null) root.styleSheets.Add(uss);
            else Debug.LogWarning($"[{nameof(AOBakerWindow)}] USS が見つかりません。GUID を確認してください: {guid}");
        }

        /// <summary>GUID で読み、見つからなければ名前で探す (.meta を作り直されたときの保険)。</summary>
        private static T LoadAsset<T>(string guid, string searchFilter) where T : UnityEngine.Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }
            foreach (var g in AssetDatabase.FindAssets(searchFilter))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
                if (asset != null) return asset;
            }
            return null;
        }

        // ─── バインディング ─────────────────────────────────────────────

        private void InitializeUI(VisualElement root)
        {
            _status = new DennokoStatus(root.Q<Label>("status-label"), L("Status_Ready"));
            _scroll = root.Q<ScrollView>("main-scroll");

            // 標準ヘッダー
            _appBar = new DennokoAppBar(root, ToolName, english: LocalizationManager.CurrentLanguage == Language.English)
            {
                ManualUrl = ResolveManualUrl(),
                ResetAll  = ResetAllSettings,
            };
            _appBar.LanguageChanged += english =>
            {
                LocalizationManager.CurrentLanguage = english ? Language.English : Language.Japanese;
                ApplyLanguage();
            };
            _appBar.CheckUpdateRequested += () =>
            {
                FastAOBakerVersion.ForceRecheck();
                LoadVersionResultFromSessionState();
            };
            _versionLabel = root.Q<Label>("version-label");

            InitializeObjectCards(root);
            InitializeAOCard(root);
            InitializeDenoiseCard(root);
            InitializePostCard(root);
            InitializeOutput(root);

            // 開いたときにターゲットが空なら、Hierarchy の選択で埋める (指定済みの値は勝手に変えない)
            if (BakeStore.State.TargetMeshes.Count == 0 && !IsBaking)
            {
                var picked = Selection.gameObjects.Where(HasMeshComponent).Distinct().ToList();
                if (picked.Count > 0) BakeStore.Dispatch(new SetTargetMeshesAction(picked));
            }
        }

        private void InitializeObjectCards(VisualElement root)
        {
            _targetCard.Bind(root, "target", () => BakeStore.State.TargetMeshes,
                list => BakeStore.Dispatch(new SetTargetMeshesAction(list)), this);
            _occluderCard.Bind(root, "occluder", () => BakeStore.State.OccluderMeshes,
                list => BakeStore.Dispatch(new SetOccluderMeshesAction(list)), this);

            _targetInfo        = root.Q<VisualElement>("target-info");
            _targetInfoCount   = root.Q<Label>("target-info-count");
            _targetInfoUV      = root.Q<Label>("target-info-uv");
            _targetWarning     = root.Q<VisualElement>("target-warning");
            _targetWarningText = root.Q<Label>("target-warning-text");
            _targetFixButton   = root.Q<Button>("target-fix");
            if (_targetFixButton != null) _targetFixButton.clicked += FixReadability;
        }

        private void InitializeAOCard(VisualElement root)
        {
            _aoCardTitle           = root.Q<Label>("ao-card-title");
            _aoCardReset           = root.Q<Button>("ao-card-reset");
            _resolutionField       = root.Q<DropdownField>("resolution-field");
            _uvChannelField        = root.Q<DropdownField>("uv-channel-field");
            _uvWarning             = root.Q<VisualElement>("uv-warning");
            _uvWarningText         = root.Q<Label>("uv-warning-text");
            _uvWarningFix          = root.Q<Button>("uv-warning-fix");
            _selfToggle            = root.Q<Toggle>("self-occlusion-toggle");
            _mutualToggle          = root.Q<Toggle>("mutual-occlusion-toggle");
            _lowResourceToggle     = root.Q<Toggle>("low-resource-toggle");
            _lowResourceHelp       = root.Q<HelpBox>("low-resource-help");
            _advancedDetails       = root.Q<VisualElement>("advanced-details");
            _advancedTitle         = root.Q<Label>("advanced-title");
            _advancedChips         = root.Q<VisualElement>("advanced-chips");
            _rayCountSlider        = root.Q<SliderInt>("ray-count-slider");
            _maxDistanceSlider     = root.Q<Slider>("max-distance-slider");
            _biasSlider            = root.Q<Slider>("bias-slider");
            _ignoreBackfacesToggle = root.Q<Toggle>("ignore-backfaces-toggle");

            if (_resolutionField != null)
            {
                _resolutionField.choices = ResolutionOptions.Select(r => r.ToString()).ToList();
                _resolutionField.RegisterValueChangedCallback(evt =>
                {
                    if (int.TryParse(evt.newValue, out int r))
                        SetOutput(o => o.With(outputResolution: r));
                });
            }

            _uvChannelField?.RegisterValueChangedCallback(evt =>
            {
                int index = _uvChannelField.index;
                if (index >= 0 && index < _uvChannelValues.Count)
                    SetOutput(o => o.With(uvChannel: _uvChannelValues[index]));
            });
            if (_uvWarningFix != null) _uvWarningFix.clicked += () => SetOutput(o => o.With(uvChannel: -1));

            BindValue(_selfToggle,            v => SetAO(a => a.With(useSelfOcclusion: v)));
            BindValue(_mutualToggle,          v => SetAO(a => a.With(useMutualOcclusion: v)));
            BindValue(_lowResourceToggle,     v => SetAO(a => a.With(lowResourceMode: v)));
            BindValue(_rayCountSlider,        v => SetAO(a => a.With(rayCount: v)));
            BindValue(_maxDistanceSlider,     v => SetAO(a => a.With(maxDistance: v)));
            BindValue(_biasSlider,            v => SetAO(a => a.With(bias: v)));
            BindValue(_ignoreBackfacesToggle, v => SetAO(a => a.With(ignoreBackfaces: v)));

            DennokoBlockUI.BindDetails(_advancedDetails, _advancedOpen, open => _advancedOpen = open);

            BindCardReset(_aoCardReset, () =>
            {
                var d = new AOSettings();
                var od = new OutputSettings();
                SetAO(a => a.With(
                    useSelfOcclusion:   d.UseSelfOcclusion,
                    useMutualOcclusion: d.UseMutualOcclusion,
                    lowResourceMode:    d.LowResourceMode,
                    rayCount:           d.RayCount,
                    maxDistance:        d.MaxDistance,
                    bias:               d.Bias,
                    ignoreBackfaces:    d.IgnoreBackfaces));
                SetOutput(o => o.With(outputResolution: od.OutputResolution, uvChannel: od.UVChannel));
            });
        }

        private void InitializeDenoiseCard(VisualElement root)
        {
            _denoiseCard             = root.Q<VisualElement>("denoise-card");
            _denoiseToggle           = root.Q<Toggle>("denoise-toggle");
            _denoiseCardReset        = root.Q<Button>("denoise-card-reset");
            _denoiseIterationsSlider = root.Q<SliderInt>("denoise-iterations-slider");
            _sigmaPosSlider          = root.Q<Slider>("denoise-sigma-pos-slider");
            _sigmaNrmSlider          = root.Q<Slider>("denoise-sigma-nrm-slider");
            _sigmaLumSlider          = root.Q<Slider>("denoise-sigma-lum-slider");

            // トグル付きセクション: OFF で折りたたむ (折りたたみのクラスは SyncFromState が付け外しする)
            BindValue(_denoiseToggle,           v => SetAO(a => a.With(denoiseEnabled: v)));
            BindValue(_denoiseIterationsSlider, v => SetAO(a => a.With(denoiseIterations: v)));
            BindValue(_sigmaPosSlider,          v => SetAO(a => a.With(denoiseSigmaPos: v)));
            BindValue(_sigmaNrmSlider,          v => SetAO(a => a.With(denoiseSigmaNrm: v)));
            BindValue(_sigmaLumSlider,          v => SetAO(a => a.With(denoiseSigmaLum: v)));

            BindCardReset(_denoiseCardReset, () =>
            {
                var d = new AOSettings();
                SetAO(a => a.With(
                    denoiseEnabled:    d.DenoiseEnabled,
                    denoiseIterations: d.DenoiseIterations,
                    denoiseSigmaPos:   d.DenoiseSigmaPos,
                    denoiseSigmaNrm:   d.DenoiseSigmaNrm,
                    denoiseSigmaLum:   d.DenoiseSigmaLum));
            });
        }

        private void InitializePostCard(VisualElement root)
        {
            _postCardTitle    = root.Q<Label>("post-card-title");
            _postCardReset    = root.Q<Button>("post-card-reset");
            _dilationSlider   = root.Q<SliderInt>("dilation-slider");
            _shadowColorField = root.Q<ColorField>("shadow-color-field");
            _blurToggle       = root.Q<Toggle>("blur-toggle");
            _blurGroup        = root.Q<VisualElement>("blur-group");
            _blurPassesSlider = root.Q<SliderInt>("blur-passes-slider");

            if (_shadowColorField != null) _shadowColorField.showAlpha = false;

            BindValue(_dilationSlider,   v => SetOutput(o => o.With(dilationPixels: v)));
            BindValue(_shadowColorField, v => SetOutput(o => o.With(shadowColor: v)));
            BindValue(_blurToggle,       v => SetOutput(o => o.With(gaussianBlurEnabled: v)));
            BindValue(_blurPassesSlider, v => SetOutput(o => o.With(gaussianBlurPasses: v)));

            BindCardReset(_postCardReset, () =>
            {
                var d = new OutputSettings();
                SetOutput(o => o.With(
                    dilationPixels:      d.DilationPixels,
                    shadowColor:         d.ShadowColor,
                    gaussianBlurEnabled: d.GaussianBlurEnabled,
                    gaussianBlurPasses:  d.GaussianBlurPasses));
            });
        }

        private void InitializeOutput(VisualElement root)
        {
            _outputDock      = root.Q<VisualElement>("output-dock");
            _outputPathLabel = root.Q<Label>("output-path-label");
            _outputPathField = root.Q<TextField>("output-path");
            _outputBrowse    = root.Q<Button>("output-browse");
            _outputAuto      = root.Q<Button>("output-auto");
            _overwriteToggle = root.Q<Toggle>("overwrite-toggle");
            _cancelButton    = root.Q<Button>("cancel-button");
            _bakeButton      = root.Q<Button>("bake-button");

            if (_outputBrowse != null) _outputBrowse.clicked += BrowseOutputFolder;
            if (_outputAuto != null) _outputAuto.clicked += () => SetOutput(o => o.With(outputFolder: ""));
            BindValue(_overwriteToggle, v => SetOutput(o => o.With(overwriteExisting: v)));

            if (_bakeButton != null) _bakeButton.clicked += StartBake;
            if (_cancelButton != null)
            {
                _cancelButton.clicked += () =>
                {
                    _bakeCts?.Cancel();
                    _cancelButton.SetEnabled(false);
                };
            }
        }

        private void BrowseOutputFolder()
        {
            string current = BakeStore.State.OutputSettings.OutputFolder;
            string selected = EditorUtility.OpenFolderPanel(L("Label_OutputFolder"),
                string.IsNullOrEmpty(current) ? "Assets" : current, "");
            if (string.IsNullOrEmpty(selected)) return;

            string dataPath = Application.dataPath.Replace('\\', '/');
            selected = selected.Replace('\\', '/');
            if (!selected.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
            {
                _status.Set(L("Msg_SelectAssetsFolder"), DennokoStatusType.Warning);
                return;
            }
            string relative = "Assets" + selected.Substring(dataPath.Length);
            SetOutput(o => o.With(outputFolder: relative));
        }

        private static void SetAO(Func<AOSettings, AOSettings> change) =>
            BakeStore.Dispatch(new UpdateAOSettingsAction(change(BakeStore.State.AOSettings)));

        private static void SetOutput(Func<OutputSettings, OutputSettings> change) =>
            BakeStore.Dispatch(new UpdateOutputSettingsAction(change(BakeStore.State.OutputSettings)));

        private static void BindValue<T>(INotifyValueChanged<T> field, Action<T> apply)
        {
            field?.RegisterValueChangedCallback(evt => apply(evt.newValue));
        }

        /// <summary>カード見出しの ↺: そのカードだけを初期値に戻す (確認なし)。</summary>
        private void BindCardReset(Button button, Action reset)
        {
            if (button == null) return;
            DennokoIcons.Apply(button, DennokoIcons.Refresh, "↺");
            button.clicked += () =>
            {
                reset();
                _status.Set(L("Msg_CardReset"));
            };
        }

        /// <summary>⋮「設定を初期値に戻す…」(確認ダイアログは DennokoAppBar が出す)。対象は残す。</summary>
        private void ResetAllSettings()
        {
            if (IsBaking) return;
            BakeStore.Dispatch(new UpdateAOSettingsAction(new AOSettings()));
            BakeStore.Dispatch(new UpdateOutputSettingsAction(new OutputSettings()));
            _status.Set(L("Msg_SettingsReset"));
        }

        /// <summary>マニュアル (?) はパッケージ同梱の Docs/USAGE*.pdf を開く。</summary>
        private static string ResolveManualUrl()
        {
            string uxmlPath = AssetDatabase.GUIDToAssetPath(UXML_GUID);
            if (string.IsNullOrEmpty(uxmlPath)) return null;

            // <ツール>/Editor/UI/AOBakerWindow.uxml → <ツール>/Docs/
            string toolRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(uxmlPath)));
            if (string.IsNullOrEmpty(toolRoot)) return null;
            bool ja = LocalizationManager.CurrentLanguage == Language.Japanese;
            string file = Path.Combine(toolRoot, "Docs", ja ? "USAGE_ja.pdf" : "USAGE.pdf");
            if (!File.Exists(file)) return null;
            return "file:///" + Path.GetFullPath(file).Replace('\\', '/');
        }

        // ─── 状態 → UI ─────────────────────────────────────────────────

        private void OnStateChanged()
        {
            if (!_uiReady) return;
            SyncFromState();
            UpdateStatusFromState();
        }

        /// <summary>BakeStore の値を UI に書き戻す (コールバックは呼ばない)。</summary>
        private void SyncFromState()
        {
            var state = BakeStore.State;
            var ao = state.AOSettings;
            var output = state.OutputSettings;

            // B1: リストが入れ替わったときだけ行を組み直す
            bool listsChanged = _targetCard.Refresh(IsBaking) | _occluderCard.Refresh(IsBaking);
            if (listsChanged) RefreshObjectInfo();

            // B2 基本AO
            _resolutionField?.SetValueWithoutNotify(output.OutputResolution.ToString());
            RefreshUVChoices();
            _selfToggle?.SetValueWithoutNotify(ao.UseSelfOcclusion);
            _mutualToggle?.SetValueWithoutNotify(ao.UseMutualOcclusion);
            _lowResourceToggle?.SetValueWithoutNotify(ao.LowResourceMode);
            DennokoBlockUI.Show(_lowResourceHelp, ao.LowResourceMode);
            _rayCountSlider?.SetValueWithoutNotify(ao.RayCount);
            _maxDistanceSlider?.SetValueWithoutNotify(ao.MaxDistance);
            _biasSlider?.SetValueWithoutNotify(ao.Bias);
            _ignoreBackfacesToggle?.SetValueWithoutNotify(ao.IgnoreBackfaces);
            RefreshAdvancedChips();

            // B2 ノイズ除去
            _denoiseToggle?.SetValueWithoutNotify(ao.DenoiseEnabled);
            _denoiseCard?.EnableInClassList(CollapsedClass, !ao.DenoiseEnabled);
            _denoiseIterationsSlider?.SetValueWithoutNotify(ao.DenoiseIterations);
            _sigmaPosSlider?.SetValueWithoutNotify(ao.DenoiseSigmaPos);
            _sigmaNrmSlider?.SetValueWithoutNotify(ao.DenoiseSigmaNrm);
            _sigmaLumSlider?.SetValueWithoutNotify(ao.DenoiseSigmaLum);

            // B2 仕上げ
            _dilationSlider?.SetValueWithoutNotify(output.DilationPixels);
            _shadowColorField?.SetValueWithoutNotify(output.ShadowColor);
            _blurToggle?.SetValueWithoutNotify(output.GaussianBlurEnabled);
            DennokoBlockUI.Show(_blurGroup, output.GaussianBlurEnabled);
            _blurPassesSlider?.SetValueWithoutNotify(output.GaussianBlurPasses);

            // B5 出力ドック
            bool auto = string.IsNullOrEmpty(output.OutputFolder);
            _outputPathField?.SetValueWithoutNotify(auto ? L("Label_OutputAuto") : output.OutputFolder);
            if (_outputPathField != null) _outputPathField.tooltip = L("Tooltip_OutputFolder");
            DennokoBlockUI.Show(_outputAuto, !auto);
            _overwriteToggle?.SetValueWithoutNotify(output.OverwriteExisting);

            // 作業中は設定と出力を触らせない
            bool baking = IsBaking;
            _scroll?.SetEnabled(!baking);
            _outputDock?.SetEnabled(!baking);
            RefreshFooter(state);
        }

        private void RefreshFooter(BakeState state)
        {
            bool baking = IsBaking;
            if (_cancelButton != null)
            {
                DennokoBlockUI.Show(_cancelButton, baking);
                if (!baking) _cancelButton.SetEnabled(true);
            }

            if (_bakeButton == null) return;
            bool ready = state.TargetMeshes.Any(go => go != null);
            _bakeButton.text = baking
                ? LF("Button_Baking", Mathf.RoundToInt(state.Progress * 100f))
                : L("Button_BakeNow");
            _bakeButton.SetEnabled(ready && !baking);
            _bakeButton.tooltip = !baking && !ready ? L("Tooltip_NeedTarget") : null;
        }

        /// <summary>UV チャンネルの選択肢 = Auto + ターゲットにある UV (+ 今の指定がターゲットに無ければそれも)。</summary>
        private void RefreshUVChoices()
        {
            if (_uvChannelField == null) return;
            var state = BakeStore.State;
            int current = state.OutputSettings.UVChannel;

            var available = GetAvailableUVIndices(state.TargetMeshes);
            _uvChannelValues = new List<int> { -1 };
            _uvChannelValues.AddRange(available);
            if (current >= 0 && !_uvChannelValues.Contains(current))
            {
                _uvChannelValues.Add(current);
                _uvChannelValues.Sort();
            }

            _uvChannelField.choices = _uvChannelValues.Select(v => v < 0 ? "Auto" : $"UV{v}").ToList();
            _uvChannelField.SetValueWithoutNotify(current < 0 ? "Auto" : $"UV{current}");

            bool missing = current >= 0 && state.TargetMeshes.Any(go => go != null && !MeshHasUV(go, current));
            DennokoBlockUI.Show(_uvWarning, missing);
            if (_uvWarningText != null) _uvWarningText.tooltip = L("Help_UVChannelMissing");
        }

        /// <summary>「詳細」を閉じている間、初期値と違う項目だけをチップで見出し行に並べる。</summary>
        private void RefreshAdvancedChips()
        {
            if (_advancedChips == null) return;
            _advancedChips.Clear();
            var ao = BakeStore.State.AOSettings;
            var d = new AOSettings();

            void Add(string labelKey, string value) =>
                _advancedChips.Add(DennokoBlockUI.Chip(null, $"{L(labelKey).Trim()} {value}"));

            if (ao.RayCount != d.RayCount) Add("Label_RayCount", ao.RayCount.ToString());
            if (!Mathf.Approximately(ao.MaxDistance, d.MaxDistance)) Add("Label_MaxDistance", ao.MaxDistance.ToString("0.##"));
            if (!Mathf.Approximately(ao.Bias, d.Bias)) Add("Label_AOBias", ao.Bias.ToString("0.####"));
            if (ao.IgnoreBackfaces != d.IgnoreBackfaces)
                _advancedChips.Add(DennokoBlockUI.Chip(null, L("Label_IgnoreBackfaces")));
        }

        /// <summary>ターゲットの情報行と Read/Write の警告行を作り直す。</summary>
        private void RefreshObjectInfo()
        {
            var state = BakeStore.State;
            var targets = state.TargetMeshes.Where(go => go != null).ToList();
            bool hasTargets = targets.Count > 0;

            _unreadable = state.TargetMeshes.Concat(state.OccluderMeshes)
                .Where(go => go != null)
                .Select(GetMesh)
                .Where(m => m != null && !m.isReadable)
                .Distinct()
                .ToList();

            DennokoBlockUI.Show(_targetInfo, hasTargets && _unreadable.Count == 0);
            DennokoBlockUI.Show(_targetWarning, _unreadable.Count > 0);

            if (_targetInfoCount != null) _targetInfoCount.text = LF("Info_MeshCount", targets.Count);
            if (_targetInfoUV != null)
            {
                var uvs = GetAvailableUVIndices(targets);
                _targetInfoUV.text = uvs.Count > 0 ? string.Join(" / ", uvs.Select(i => $"UV{i}")) : L("Info_NoUV");
            }
            if (_targetWarningText != null)
            {
                _targetWarningText.text = LF("Warning_NotReadableCount", _unreadable.Count);
                _targetWarningText.tooltip = string.Join("\n", _unreadable.Select(m => "- " + m.name));
            }

            RefreshUVChoices();
        }

        private void UpdateStatusFromState()
        {
            var state = BakeStore.State;
            var prev = _lastStatus;
            _lastStatus = state.Status;

            var statusLabel = rootVisualElement.Q<Label>("status-label");
            if (statusLabel != null && state.Status != BakeStatus.Error) statusLabel.tooltip = null;

            switch (state.Status)
            {
                case BakeStatus.Baking:
                case BakeStatus.Denoising:
                    _status.Set(L(state.StatusMessage), DennokoStatusType.Busy, state.Progress);
                    break;

                case BakeStatus.Completed:
                    if (prev != BakeStatus.Completed)
                        _status.Set(L(state.StatusMessage), DennokoStatusType.Success);
                    break;

                case BakeStatus.Error:
                    if (prev != BakeStatus.Error) ShowError(L(state.StatusMessage));
                    break;

                default:
                    // キャンセル後は None に戻る
                    if (prev == BakeStatus.Baking || prev == BakeStatus.Denoising)
                        _status.Set(L(state.StatusMessage), DennokoStatusType.Warning);
                    break;
            }
        }

        /// <summary>ステータスは 1 行なので、複数行のエラーは 1 行目だけ出して全文をツールチップに入れる。</summary>
        private void ShowError(string message)
        {
            message = message ?? string.Empty;
            string[] lines = message.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string first = lines.Length > 0 ? lines[0].Trim() : message;
            if (lines.Length > 1) first += $"  (+{lines.Length - 1})";
            _status.Set(first, DennokoStatusType.Error);

            var label = rootVisualElement.Q<Label>("status-label");
            if (label != null) label.tooltip = message;
        }

        // ─── 言語適用 ───────────────────────────────────────────────────

        private void ApplyLanguage()
        {
            if (!_uiReady) return;
            var root = rootVisualElement;

            if (_appBar != null) _appBar.ManualUrl = ResolveManualUrl();
            if (_status != null) _status.ReadyText = L("Status_Ready");

            // B1
            _targetCard.ApplyLanguage(
                title: L("Section_Target"),
                empty: L("Msg_DropTargets"),
                hint: L("Hint_Target"),
                clear: L("Button_RemoveAll"), clearTooltip: L("Tooltip_ClearTargets"),
                fromSelection: L("Button_FromSelection"), fromSelectionTooltip: L("Tooltip_FromSelection"),
                dropTooltip: L("Tooltip_AddTarget"));
            _occluderCard.ApplyLanguage(
                title: L("Section_Occluder"),
                empty: L("Msg_DropOccluders"),
                hint: L("Hint_Occluder"),
                clear: L("Button_RemoveAll"), clearTooltip: L("Tooltip_ClearAll"),
                fromSelection: L("Button_FromSelection"), fromSelectionTooltip: L("Tooltip_FromSelection"),
                dropTooltip: L("Tooltip_AddOccluder"));
            SetText(_targetFixButton, L("Dialog_ReadWrite_Enable"));

            // B2 基本AO
            SetText(_aoCardTitle, L("Section_BasicAO"));
            SetField(_resolutionField, "Label_OutputRes", "Tooltip_OutputRes");
            SetField(_uvChannelField, "Label_UVChannel", "Tooltip_UVChannel");
            SetText(_uvWarningText, L("Warning_UVChannelMissing"));
            SetText(_uvWarningFix, "Auto");
            SetToggle(_selfToggle, "Label_SelfOcclusion", "Tooltip_SelfOcclusion");
            SetToggle(_mutualToggle, "Label_MutualOcclusion", "Tooltip_MutualOcclusion");
            SetToggle(_lowResourceToggle, "Label_LowResource", "Tooltip_LowResource");
            if (_lowResourceHelp != null) _lowResourceHelp.text = L("Help_LowResource");
            SetText(_advancedTitle, L("Section_Advanced"));
            SetField(_rayCountSlider, "Label_RayCount", "Tooltip_RayCount");
            SetField(_maxDistanceSlider, "Label_MaxDistance", "Tooltip_MaxDistance");
            SetField(_biasSlider, "Label_AOBias", "Tooltip_AOBias");
            SetToggle(_ignoreBackfacesToggle, "Label_IgnoreBackfaces", "Tooltip_IgnoreBackfaces");

            // B2 ノイズ除去
            SetToggle(_denoiseToggle, "Section_SVGF", null);
            SetField(_denoiseIterationsSlider, "Label_Iterations", "Tooltip_Iterations");
            SetField(_sigmaPosSlider, "Label_SigmaPos", "Tooltip_SigmaPos");
            SetField(_sigmaNrmSlider, "Label_SigmaNrm", "Tooltip_SigmaNrm");
            SetField(_sigmaLumSlider, "Label_SigmaLum", "Tooltip_SigmaLum");

            // B2 仕上げ
            SetText(_postCardTitle, L("Label_PostProcess"));
            SetField(_dilationSlider, "Label_Dilation", "Tooltip_Dilation");
            SetField(_shadowColorField, "Label_ShadowColor", "Tooltip_ShadowColor");
            SetToggle(_blurToggle, "Label_GaussianBlur", "Tooltip_GaussianBlur");
            SetField(_blurPassesSlider, "Label_BlurPasses", "Tooltip_BlurPasses");

            foreach (var reset in new[] { _aoCardReset, _denoiseCardReset, _postCardReset })
                if (reset != null) reset.tooltip = L("Tooltip_ResetCard");

            // 出力ドック + フッター
            SetText(_outputPathLabel, L("Label_OutputFolderShort"));
            if (_outputPathLabel != null) _outputPathLabel.tooltip = L("Tooltip_OutputFolder");
            SetText(_outputBrowse, L("Button_Browse"));
            SetText(_outputAuto, L("Button_Auto"));
            if (_outputAuto != null) _outputAuto.tooltip = L("Tooltip_OutputAuto");
            SetToggle(_overwriteToggle, "Label_Overwrite", "Tooltip_Overwrite");
            SetText(_cancelButton, L("Button_Cancel"));

            ApplyVersionLabel();

            _targetCard.MarkDirty();
            _occluderCard.MarkDirty();
            SyncFromState();
            if (!IsBaking) _status.Ready();
            else UpdateStatusFromState();
        }

        private static void SetText(TextElement element, string text)
        {
            if (element != null) element.text = text;
        }

        private static void SetToggle(Toggle toggle, string textKey, string tooltipKey)
        {
            if (toggle == null) return;
            toggle.text = L(textKey).Trim();
            toggle.tooltip = tooltipKey != null ? L(tooltipKey) : null;
        }

        private static void SetField<T>(BaseField<T> field, string labelKey, string tooltipKey)
        {
            if (field == null) return;
            field.label = L(labelKey).Trim();
            field.tooltip = L(tooltipKey);
        }

        // ─── バージョンチェック ─────────────────────────────────────────

        private void StartVersionCheck()
        {
            LoadVersionResultFromSessionState();
            FastAOBakerVersion.StartCheckBackgroundTask();
        }

        /// <summary>チェック結果 (SessionState) を読み、トップバーのバージョン表示に反映する。FastAOBakerVersion からも呼ばれる。</summary>
        internal void LoadVersionResultFromSessionState()
        {
            string local  = FastAOBakerVersion.Current;
            string latest = SessionState.GetString(FastAOBakerVersion.VerCheckLatestKey, string.Empty);
            bool   done   = SessionState.GetBool(FastAOBakerVersion.VerCheckDoneKey, false);
            bool   error  = SessionState.GetBool(FastAOBakerVersion.VerCheckErrorKey, false);

            DennokoVersionChecker.State state;
            if (!done)
                state = DennokoVersionChecker.State.Checking;
            else if (error || string.IsNullOrEmpty(latest))
                state = DennokoVersionChecker.State.Error;
            else if (DennokoVersionChecker.IsUpdateAvailable(latest, local))
                state = DennokoVersionChecker.State.UpdateAvailable;
            else
                state = DennokoVersionChecker.State.UpToDate;

            _versionResult = new DennokoVersionChecker.Result
            {
                State         = state,
                LocalVersion  = local,
                LatestVersion = latest,
                Url           = SessionState.GetString(FastAOBakerVersion.VerCheckUrlKey, string.Empty),
                Message       = SessionState.GetString(FastAOBakerVersion.VerCheckMessageKey, string.Empty)
            };
            ApplyVersionLabel();
        }

        private void ApplyVersionLabel()
        {
            if (_versionLabel == null) return;

            var r = _versionResult;
            string baseText = "v" + r.LocalVersion;
            string text;
            bool update = false, error = false;
            switch (r.State)
            {
                case DennokoVersionChecker.State.UpdateAvailable:
                    text = $"{baseText}  " + LF("Version_UpdateAvailable", r.LatestVersion);
                    update = true;
                    break;
                case DennokoVersionChecker.State.Error:
                    text = $"{baseText}  " + L("Version_Error");
                    error = true;
                    break;
                case DennokoVersionChecker.State.Checking:
                    text = $"{baseText}  " + L("Version_Checking");
                    break;
                default:
                    text = baseText;
                    break;
            }
            _versionLabel.text = text;
            _versionLabel.EnableInClassList("dennoko-version-label--update", update);
            _versionLabel.EnableInClassList("dennoko-version-label--error", error);

            // 更新ありのラベルは公開配布ページを開く。GitHub への直接の導線は作らない
            bool hasReleasePage = !string.IsNullOrEmpty(r.Url) &&
                                  r.Url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) < 0;
            if (_appBar != null)
            {
                _appBar.ReleaseUrl = hasReleasePage ? r.Url : null;
                _appBar.SetVersionClickable(update && hasReleasePage,
                    update && hasReleasePage ? L("Version_OpenReleasePage") : null);
            }
        }

        // ─── Read/Write ─────────────────────────────────────────────────

        private void FixReadability()
        {
            var failed = EnableReadWrite(_unreadable);
            RefreshObjectInfo();
            if (failed.Count == 0)
                _status.Set(L("Msg_ReadWriteEnabled"), DennokoStatusType.Success);
            else
                _status.Set(LF("Msg_ReadWriteFailed", failed.Count), DennokoStatusType.Error);
        }

        /// <summary>モデルからインポートしたメッシュの Read/Write を有効にする。変更できなかったメッシュを返す。</summary>
        private static List<Mesh> EnableReadWrite(IEnumerable<Mesh> meshes)
        {
            var failed = new List<Mesh>();
            var importers = new Dictionary<string, ModelImporter>();
            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;
                string path = AssetDatabase.GetAssetPath(mesh);
                var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { failed.Add(mesh); continue; }
                importers[path] = importer;
            }

            foreach (var importer in importers.Values)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            return failed;
        }

        /// <summary>ベイク前の確認。Read/Write が無効なメッシュがあれば有効化を提案する。false ならベイクしない。</summary>
        private bool EnsureReadable()
        {
            RefreshObjectInfo();
            if (_unreadable.Count == 0) return true;

            string names = string.Join("\n", _unreadable.Select(m => "- " + m.name));
            bool enable = EditorUtility.DisplayDialog(
                L("Dialog_ReadWrite_Title"),
                LF("Dialog_ReadWrite_BodyMulti", names),
                L("Dialog_ReadWrite_Enable"),
                L("Dialog_ReadWrite_Skip"));
            if (!enable) return false;

            var failed = EnableReadWrite(_unreadable);
            RefreshObjectInfo();
            if (failed.Count == 0) return true;

            _status.Set(LF("Msg_ReadWriteFailed", failed.Count), DennokoStatusType.Error);
            return false;
        }

        // ─── ベイク ─────────────────────────────────────────────────────

        private async void StartBake()
        {
            if (IsBaking) return;
            if (!BakeStore.State.TargetMeshes.Any(go => go != null))
            {
                _status.Set(L("Msg_NoTargetMeshes"), DennokoStatusType.Warning);
                return;
            }
            if (!EnsureReadable()) return;

            _bakeCts?.Dispose();
            _bakeCts = new CancellationTokenSource();
            var cts = _bakeCts;

            var snapshot = BakeStore.State;
            BakeStore.Dispatch(new StartBakeAction());

            try
            {
                await new BakeOrchestrator().ExecuteBakePipelineAsync(snapshot, cts.Token);
            }
            finally
            {
                if (ReferenceEquals(_bakeCts, cts))
                    _bakeCts = null;
                cts.Dispose();
                if (_uiReady) SyncFromState();
            }
        }

        // ─── メッシュの補助 ─────────────────────────────────────────────

        internal static bool HasMeshComponent(GameObject go)
        {
            return go != null &&
                   (go.GetComponentInChildren<MeshFilter>() != null ||
                    go.GetComponentInChildren<SkinnedMeshRenderer>() != null);
        }

        /// <summary>ベイクに使うメッシュ (BakeOrchestrator と同じく MeshFilter → SkinnedMeshRenderer の順に探す)。</summary>
        private static Mesh GetMesh(GameObject go)
        {
            if (go == null) return null;
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            return smr != null ? smr.sharedMesh : null;
        }

        private static bool MeshHasUV(GameObject go, int channel)
        {
            var mesh = GetMesh(go);
            if (mesh == null) return false;
            var uvs = new List<Vector2>();
            mesh.GetUVs(channel, uvs);
            return uvs.Count > 0;
        }

        private static List<int> GetAvailableUVIndices(IEnumerable<GameObject> targets)
        {
            var indices = new SortedSet<int>();
            var uvs = new List<Vector2>();
            foreach (var go in targets)
            {
                var mesh = GetMesh(go);
                if (mesh == null) continue;
                for (int i = 0; i < 8; i++)
                {
                    mesh.GetUVs(i, uvs);
                    if (uvs.Count > 0) indices.Add(i);
                }
            }
            return indices.ToList();
        }

        // ─── B1 対象カード (ターゲット / 遮蔽物で共通) ───────────────────

        /// <summary>
        /// GameObject を複数並べる対象カード。行は動的リストなので C# で組む (見た目は USS のクラスだけで決める)。
        /// 末尾の空き ObjectField は置かず、カード全体へのドロップと「選択から追加」で足す。
        /// </summary>
        private sealed class ObjectListCard
        {
            private AOBakerWindow _owner;
            private Func<IReadOnlyList<GameObject>> _get;
            private Action<List<GameObject>> _set;
            private VisualElement _card;
            private Label _title;
            private VisualElement _list;
            private VisualElement _empty;
            private Label _emptyMessage;
            private Label _hint;
            private Button _clear;
            private Button _fromSelection;
            private IReadOnlyList<GameObject> _rendered;
            private bool _renderedBaking;
            private string _removeTooltip;

            public void Bind(VisualElement root, string prefix, Func<IReadOnlyList<GameObject>> get,
                             Action<List<GameObject>> set, AOBakerWindow owner)
            {
                _owner = owner;
                _get = get;
                _set = set;
                _card          = root.Q<VisualElement>($"{prefix}-card");
                _title         = root.Q<Label>($"{prefix}-card-title");
                _list          = root.Q<VisualElement>($"{prefix}-list");
                _empty         = root.Q<VisualElement>($"{prefix}-empty");
                _emptyMessage  = root.Q<Label>($"{prefix}-empty-message");
                _hint          = root.Q<Label>($"{prefix}-hint");
                _clear         = root.Q<Button>($"{prefix}-clear");
                _fromSelection = root.Q<Button>($"{prefix}-from-selection");

                if (_clear != null) _clear.clicked += () => _set(new List<GameObject>());
                if (_fromSelection != null)
                {
                    _fromSelection.clicked += () =>
                    {
                        if (!Add(Selection.gameObjects))
                            _owner._status.Set(L("Msg_SelectInHierarchy"), DennokoStatusType.Warning);
                    };
                }
                SetupDropTarget();
            }

            public void ApplyLanguage(string title, string empty, string hint, string clear, string clearTooltip,
                                      string fromSelection, string fromSelectionTooltip, string dropTooltip)
            {
                if (_title != null) _title.text = title;
                if (_emptyMessage != null) _emptyMessage.text = empty;
                if (_empty != null) _empty.tooltip = dropTooltip;
                if (_hint != null) _hint.text = hint;
                if (_clear != null) { _clear.text = clear; _clear.tooltip = clearTooltip; }
                if (_fromSelection != null) { _fromSelection.text = fromSelection; _fromSelection.tooltip = fromSelectionTooltip; }
                _removeTooltip = L("Tooltip_Remove");
            }

            public void MarkDirty() => _rendered = null;

            /// <summary>リストが変わっていれば行を組み直す。組み直したら true。</summary>
            public bool Refresh(bool baking)
            {
                if (_list == null) return false;
                var items = _get();
                if (ReferenceEquals(items, _rendered) && baking == _renderedBaking) return false;
                _rendered = items;
                _renderedBaking = baking;

                _list.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    int index = i;
                    var row = new VisualElement();
                    row.AddToClassList("dennoko-target-row");

                    var field = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = true };
                    field.SetValueWithoutNotify(items[index]);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        var go = evt.newValue as GameObject;
                        var list = new List<GameObject>(_get());
                        if (go != null && !HasMeshComponent(go))
                        {
                            _owner._status.Set(LF("Msg_NoMeshComponent", go.name), DennokoStatusType.Warning);
                            field.SetValueWithoutNotify(evt.previousValue);
                            return;
                        }
                        if (go != null && list.Contains(go))
                        {
                            field.SetValueWithoutNotify(evt.previousValue);
                            return;
                        }
                        list[index] = go;
                        list.RemoveAll(g => g == null);
                        _set(list);
                    });

                    var remove = new Button(() =>
                    {
                        var list = new List<GameObject>(_get());
                        if (index < list.Count) list.RemoveAt(index);
                        _set(list);
                    });
                    remove.AddToClassList("dennoko-icon-button");
                    DennokoIcons.Apply(remove, DennokoIcons.Close, "×", _removeTooltip);

                    field.SetEnabled(!baking);
                    remove.SetEnabled(!baking);
                    row.Add(field);
                    row.Add(remove);
                    _list.Add(row);
                }

                bool hasItems = items.Count > 0;
                DennokoBlockUI.Show(_empty, !hasItems);
                DennokoBlockUI.Show(_clear, items.Count > 1);
                return true;
            }

            /// <summary>メッシュを持つ GameObject を足す。1 つでも足したら true。</summary>
            private bool Add(IEnumerable<GameObject> objects)
            {
                var current = new List<GameObject>(_get());
                var candidates = objects.Where(go => go != null && !current.Contains(go)).Distinct().ToList();
                var noMesh = candidates.Where(go => !HasMeshComponent(go)).ToList();
                var toAdd = candidates.Except(noMesh).ToList();

                if (noMesh.Count > 0)
                    _owner._status.Set(LF("Msg_NoMeshComponent", string.Join(", ", noMesh.Select(g => g.name))), DennokoStatusType.Warning);
                if (toAdd.Count == 0) return noMesh.Count > 0;

                current.AddRange(toAdd);
                _set(current);
                return true;
            }

            private void SetupDropTarget()
            {
                if (_card == null) return;

                _card.RegisterCallback<DragUpdatedEvent>(evt =>
                {
                    bool any = !IsBaking && DragAndDrop.objectReferences.OfType<GameObject>().Any();
                    DragAndDrop.visualMode = any ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    _card.EnableInClassList(DropOverClass, any);
                    evt.StopPropagation();
                });
                _card.RegisterCallback<DragLeaveEvent>(_ => _card.RemoveFromClassList(DropOverClass));
                _card.RegisterCallback<DragExitedEvent>(_ => _card.RemoveFromClassList(DropOverClass));
                _card.RegisterCallback<DragPerformEvent>(evt =>
                {
                    DragAndDrop.AcceptDrag();
                    _card.RemoveFromClassList(DropOverClass);
                    if (!IsBaking) Add(DragAndDrop.objectReferences.OfType<GameObject>());
                    evt.StopPropagation();
                });
            }
        }
    }
}
