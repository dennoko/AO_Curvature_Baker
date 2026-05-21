using UnityEngine;
using UnityEditor;

namespace DennokoWorks.Tool.AOBaker
{
    /// <summary>
    /// dennoko.dev カラースキーマに基づくテーマ定義。
    /// colors_spec.md / design_reference.md の仕様を Unity IMGUI に変換する。
    /// OnGUI の先頭で Initialize() を呼び出すことで、スタイルを遅延初期化する。
    /// </summary>
    internal static class UniTexTheme
    {
        // ─── Colors ──────────────────────────────────────────────────────────

        // theme.surface (Neutral Layer)
        public static readonly Color Surface0 = Hex(0x121212); // app background
        public static readonly Color Surface1 = Hex(0x1e1e1e); // cards, inputs
        public static readonly Color Surface2 = Hex(0x2c2c2c); // hover, toolbar

        // theme.outline
        public static readonly Color Outline = Hex(0x3a3a3a);

        // theme.typography
        public static readonly Color TextPrimary   = Hex(0xffffff);
        public static readonly Color TextSecondary = Hex(0xcccccc);
        public static readonly Color TextTertiary  = Hex(0xaaaaaa);
        public static readonly Color TextDisabled  = Hex(0x555555);

        // theme.semantic
        public static readonly Color SemanticError   = Hex(0x9b1b30);
        public static readonly Color SemanticWarning = Hex(0xffb74d);
        public static readonly Color SemanticSuccess = Hex(0x4caf50);
        public static readonly Color SemanticInfo    = Hex(0x64b5f6);

        // theme.interaction
        public static readonly Color Accent       = Color.white;
        public static readonly Color HoverOverlay = new Color(1f, 1f, 1f, 0.05f);

        // ─── Cached Textures ─────────────────────────────────────────────────

        private static Texture2D _texSurface0;
        private static Texture2D _texSurface1;
        private static Texture2D _texSurface2;
        private static Texture2D _texCard;        // Surface1 fill + Outline border (3x3)
        private static Texture2D _texAccentCard;  // Surface2 fill + Outline border (3x3)

        // ─── Styles ──────────────────────────────────────────────────────────

        private static bool _initialized;

        // Layout / Container
        public static GUIStyle CardStyle      { get; private set; } // sections (padding あり)
        public static GUIStyle CardOuterStyle { get; private set; } // ツールバー付き外枠 (padding なし)
        public static GUIStyle ToolbarStyle   { get; private set; } // ツールバー行

        // Typography
        public static GUIStyle TitleStyle            { get; private set; } // ウィンドウタイトル
        public static GUIStyle SectionHeaderStyle    { get; private set; } // 非トグルセクション見出し
        public static GUIStyle ToggleSectionOnStyle  { get; private set; } // トグル ON 時の見出し
        public static GUIStyle ToggleSectionOffStyle { get; private set; } // トグル OFF 時の見出し
        public static GUIStyle SecondaryTextStyle    { get; private set; } // 説明文
        public static GUIStyle CaptionStyle          { get; private set; } // 補足・メタデータ

        // Buttons
        public static GUIStyle ActionButtonStyle     { get; private set; } // Primary Action
        public static GUIStyle SecondaryButtonStyle  { get; private set; } // Secondary Action
        public static GUIStyle MiniButtonStyle       { get; private set; }
        public static GUIStyle MiniButtonLeftStyle   { get; private set; }
        public static GUIStyle MiniButtonRightStyle  { get; private set; }

        // Status bar
        public static GUIStyle StatusInfoStyle    { get; private set; }
        public static GUIStyle StatusSuccessStyle { get; private set; }
        public static GUIStyle StatusErrorStyle   { get; private set; }

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>OnGUI の先頭で呼び出す。初回のみスタイルを構築する。</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EnsureTextures();
            BuildStyles();
        }

        private static void EnsureTextures()
        {
            if (!_texSurface0)   _texSurface0   = MakeTex(Surface0);
            if (!_texSurface1)   _texSurface1   = MakeTex(Surface1);
            if (!_texSurface2)   _texSurface2   = MakeTex(Surface2);
            if (!_texCard)       _texCard       = MakeBorderedTex(Surface1, Outline);
            if (!_texAccentCard) _texAccentCard = MakeBorderedTex(Surface2, Outline);
        }

        private static void BuildStyles()
        {
            // ── Container ────────────────────────────────────────────────────

            CardStyle = new GUIStyle();
            CardStyle.normal.background = _texCard;
            CardStyle.border  = new RectOffset(1, 1, 1, 1);
            CardStyle.padding = new RectOffset(10, 10, 8, 8);
            CardStyle.margin  = new RectOffset(8, 8, 8, 8);   // 8px の余白でカード間を分離

            CardOuterStyle = new GUIStyle();
            CardOuterStyle.normal.background = _texCard;
            CardOuterStyle.border  = new RectOffset(1, 1, 1, 1);
            CardOuterStyle.padding = new RectOffset(0, 0, 0, 0);
            CardOuterStyle.margin  = new RectOffset(8, 8, 8, 8);

            ToolbarStyle = new GUIStyle();
            ToolbarStyle.normal.background = _texSurface2;
            ToolbarStyle.padding = new RectOffset(6, 6, 4, 4);
            ToolbarStyle.margin  = new RectOffset(0, 0, 0, 0);

            // ── Typography ───────────────────────────────────────────────────
            // 全 state (hover/active/focused/on*) を明示設定してライトモードの継承色を上書きする

            TitleStyle = new GUIStyle(EditorStyles.boldLabel);
            TitleStyle.fontSize = 14;
            FixAllTextColors(TitleStyle, TextPrimary);

            SectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            SectionHeaderStyle.fontSize = 10;
            FixAllTextColors(SectionHeaderStyle, TextTertiary);
            SectionHeaderStyle.margin = new RectOffset(0, 0, 0, 2);

            // onNormal が未設定だと ToggleLeft の ON 状態でライトモームの黒色が使われる
            ToggleSectionOnStyle = new GUIStyle(EditorStyles.boldLabel);
            ToggleSectionOnStyle.fontSize = 10;
            FixAllTextColors(ToggleSectionOnStyle, TextPrimary);
            ToggleSectionOnStyle.margin = new RectOffset(0, 0, 0, 2);

            ToggleSectionOffStyle = new GUIStyle(EditorStyles.boldLabel);
            ToggleSectionOffStyle.fontSize = 10;
            FixAllTextColors(ToggleSectionOffStyle, TextTertiary);
            ToggleSectionOffStyle.margin = new RectOffset(0, 0, 0, 2);

            SecondaryTextStyle = new GUIStyle(EditorStyles.label);
            FixAllTextColors(SecondaryTextStyle, TextSecondary);
            SecondaryTextStyle.wordWrap = true;

            CaptionStyle = new GUIStyle(EditorStyles.miniLabel);
            FixAllTextColors(CaptionStyle, TextTertiary);

            // ── Buttons ──────────────────────────────────────────────────────

            ActionButtonStyle = new GUIStyle();
            ActionButtonStyle.normal.background  = _texAccentCard;
            ActionButtonStyle.hover.background   = MakeTex(Color.Lerp(Surface2, Color.white, 0.07f));
            ActionButtonStyle.active.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.15f));
            ActionButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            ActionButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            ActionButtonStyle.padding    = new RectOffset(8, 8, 4, 4);
            ActionButtonStyle.fontSize   = 13;
            ActionButtonStyle.fontStyle  = FontStyle.Bold;
            ActionButtonStyle.fixedHeight = 34;
            ActionButtonStyle.alignment  = TextAnchor.MiddleCenter;
            FixAllTextColors(ActionButtonStyle, TextPrimary);

            SecondaryButtonStyle = new GUIStyle();
            SecondaryButtonStyle.normal.background = MakeBorderedTex(Surface1, Outline);
            SecondaryButtonStyle.hover.background  = _texAccentCard;
            SecondaryButtonStyle.active.background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f));
            SecondaryButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            SecondaryButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            SecondaryButtonStyle.padding    = new RectOffset(6, 6, 3, 3);
            SecondaryButtonStyle.fontSize   = 11;
            SecondaryButtonStyle.fixedHeight = 26;
            SecondaryButtonStyle.alignment  = TextAnchor.MiddleCenter;
            SecondaryButtonStyle.normal.textColor   = TextSecondary;
            SecondaryButtonStyle.hover.textColor    = TextPrimary;
            SecondaryButtonStyle.active.textColor   = TextPrimary;
            SecondaryButtonStyle.focused.textColor  = TextSecondary;
            SecondaryButtonStyle.onNormal.textColor  = TextSecondary;
            SecondaryButtonStyle.onHover.textColor   = TextPrimary;
            SecondaryButtonStyle.onActive.textColor  = TextPrimary;
            SecondaryButtonStyle.onFocused.textColor = TextSecondary;

            // onNormal が未設定だと GUILayout.Toggle の選択状態でライトモームの黒色が使われる
            MiniButtonStyle = new GUIStyle(EditorStyles.miniButton);
            MiniButtonStyle.normal.textColor  = TextTertiary;
            MiniButtonStyle.hover.textColor   = TextSecondary;
            MiniButtonStyle.active.textColor  = TextSecondary;
            MiniButtonStyle.focused.textColor = TextTertiary;
            MiniButtonStyle.onNormal.textColor  = TextPrimary;
            MiniButtonStyle.onHover.textColor   = TextPrimary;
            MiniButtonStyle.onActive.textColor  = TextPrimary;
            MiniButtonStyle.onFocused.textColor = TextPrimary;

            MiniButtonLeftStyle = new GUIStyle(EditorStyles.miniButtonLeft);
            MiniButtonLeftStyle.normal.textColor  = TextTertiary;
            MiniButtonLeftStyle.hover.textColor   = TextSecondary;
            MiniButtonLeftStyle.active.textColor  = TextSecondary;
            MiniButtonLeftStyle.focused.textColor = TextTertiary;
            MiniButtonLeftStyle.onNormal.textColor  = TextPrimary;
            MiniButtonLeftStyle.onHover.textColor   = TextPrimary;
            MiniButtonLeftStyle.onActive.textColor  = TextPrimary;
            MiniButtonLeftStyle.onFocused.textColor = TextPrimary;

            MiniButtonRightStyle = new GUIStyle(EditorStyles.miniButtonRight);
            MiniButtonRightStyle.normal.textColor  = TextTertiary;
            MiniButtonRightStyle.hover.textColor   = TextSecondary;
            MiniButtonRightStyle.active.textColor  = TextSecondary;
            MiniButtonRightStyle.focused.textColor = TextTertiary;
            MiniButtonRightStyle.onNormal.textColor  = TextPrimary;
            MiniButtonRightStyle.onHover.textColor   = TextPrimary;
            MiniButtonRightStyle.onActive.textColor  = TextPrimary;
            MiniButtonRightStyle.onFocused.textColor = TextPrimary;

            // ── Status Bar ───────────────────────────────────────────────────

            var statusBase = new GUIStyle(EditorStyles.helpBox);
            statusBase.border  = new RectOffset(1, 1, 1, 1);
            statusBase.padding = new RectOffset(8, 8, 5, 5);
            statusBase.margin  = new RectOffset(4, 4, 2, 2);
            statusBase.fontSize = 11;

            StatusInfoStyle = new GUIStyle(statusBase);
            StatusInfoStyle.normal.background = _texSurface1;
            FixAllTextColors(StatusInfoStyle, TextSecondary);

            StatusSuccessStyle = new GUIStyle(statusBase);
            StatusSuccessStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticSuccess, 0.3f));
            FixAllTextColors(StatusSuccessStyle, SemanticSuccess);

            StatusErrorStyle = new GUIStyle(statusBase);
            StatusErrorStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticError, 0.5f));
            FixAllTextColors(StatusErrorStyle, new Color(1f, 0.65f, 0.65f));
        }

        public static GUIStyle GetStatusStyle(AOBakerWindow.StatusType statusLevel)
        {
            return statusLevel switch
            {
                AOBakerWindow.StatusType.Success => StatusSuccessStyle,
                AOBakerWindow.StatusType.Error   => StatusErrorStyle,
                _ => StatusInfoStyle,
            };
        }

        // ─── Editor Style Override (Light Mode Fix) ──────────────────────────
        // Unity 組み込みコントロール（Toggle・ObjectField・Slider 等）は EditorStyles を使うため、
        // カスタム GUIStyle の修正だけではライトモードでテキスト色や背景色がスキン依存になる。
        // OnGUI の先頭で PushEditorTheme を呼び、finally ブロックで PopEditorTheme を呼ぶことで
        // この Window の描画スコープ内だけ EditorStyles を一時的に上書きする。

        private static bool _overrideActive;
        public static bool IsOverrideActive => _overrideActive;

        private class GUIStyleBackup
        {
            private readonly GUIStyle _style;
            private readonly Color _normalColor, _hoverColor, _activeColor, _focusedColor;
            private readonly Color _onNormalColor, _onHoverColor, _onActiveColor, _onFocusedColor;
            private readonly Texture2D _normalBg, _hoverBg, _activeBg, _focusedBg;
            private readonly Texture2D _onNormalBg, _onHoverBg, _onActiveBg, _onFocusedBg;
            private readonly RectOffset _border;
            private readonly RectOffset _padding;

            public GUIStyleBackup(GUIStyle style)
            {
                _style = style;
                _normalColor = style.normal.textColor;
                _hoverColor = style.hover.textColor;
                _activeColor = style.active.textColor;
                _focusedColor = style.focused.textColor;
                _onNormalColor = style.onNormal.textColor;
                _onHoverColor = style.onHover.textColor;
                _onActiveColor = style.onActive.textColor;
                _onFocusedColor = style.onFocused.textColor;

                _normalBg = style.normal.background;
                _hoverBg = style.hover.background;
                _activeBg = style.active.background;
                _focusedBg = style.focused.background;
                _onNormalBg = style.onNormal.background;
                _onHoverBg = style.onHover.background;
                _onActiveBg = style.onActive.background;
                _onFocusedBg = style.onFocused.background;

                _border = style.border;
                _padding = style.padding;
            }

            public void Restore()
            {
                _style.normal.textColor = _normalColor;
                _style.hover.textColor = _hoverColor;
                _style.active.textColor = _activeColor;
                _style.focused.textColor = _focusedColor;
                _style.onNormal.textColor = _onNormalColor;
                _style.onHover.textColor = _onHoverColor;
                _style.onActive.textColor = _onActiveColor;
                _style.onFocused.textColor = _onFocusedColor;

                _style.normal.background = _normalBg;
                _style.hover.background = _hoverBg;
                _style.active.background = _activeBg;
                _style.focused.background = _focusedBg;
                _style.onNormal.background = _onNormalBg;
                _style.onHover.background = _onHoverBg;
                _style.onActive.background = _onActiveBg;
                _style.onFocused.background = _onFocusedBg;

                _style.border = _border;
                _style.padding = _padding;
            }
        }

        private static GUIStyleBackup[] _backups;

        /// <summary>
        /// OnGUI 先頭で Initialize() の直後に呼ぶ。
        /// ライトモード時のみ EditorStyles をダークテーマ色に一時上書きする。
        /// PopEditorTheme を finally ブロックで必ず呼ぶこと。
        /// </summary>
        public static void PushEditorTheme()
        {
            // ダークモード（ProSkin）では組み込みスタイルが既に暗色なので上書き不要
            if (EditorGUIUtility.isProSkin) { _overrideActive = false; return; }
            _overrideActive = true;

            if (_backups == null)
            {
                _backups = new[]
                {
                    new GUIStyleBackup(EditorStyles.label),
                    new GUIStyleBackup(EditorStyles.objectField),
                    new GUIStyleBackup(EditorStyles.numberField),
                    new GUIStyleBackup(EditorStyles.textField),
                    new GUIStyleBackup(EditorStyles.popup),
                    new GUIStyleBackup(EditorStyles.toggle)
                };
            }

            // ─ テキスト色を固定
            FixAllTextColors(EditorStyles.label, TextSecondary);
            FixAllTextColors(EditorStyles.objectField, TextSecondary);
            FixAllTextColors(EditorStyles.numberField, TextSecondary);
            FixAllTextColors(EditorStyles.textField,   TextSecondary);
            FixAllTextColors(EditorStyles.popup,       TextSecondary);
            FixAllTextColors(EditorStyles.toggle,      TextSecondary);

            // ─ 背景テクスチャをすべての状態でダーク色に固定
            FixAllStateBackgrounds(EditorStyles.objectField, _texSurface1);
            FixAllStateBackgrounds(EditorStyles.numberField, _texSurface1);
            FixAllStateBackgrounds(EditorStyles.textField,   _texSurface1);

            // ポップアップは枠線付きカードテクスチャを使用し、9スライス境界を1pxに設定して引き伸ばし縞ノイズを解消
            FixAllStateBackgrounds(EditorStyles.popup, _texCard);
            EditorStyles.popup.border = new RectOffset(1, 1, 1, 1);
            EditorStyles.popup.padding = new RectOffset(6, 18, 4, 4);
        }

        /// <summary>
        /// OnGUI 末尾の finally ブロックで必ず呼ぶ。
        /// 上書きした EditorStyles を元の値に戻す。
        /// </summary>
        public static void PopEditorTheme()
        {
            if (!_overrideActive) return;
            _overrideActive = false;

            if (_backups != null)
            {
                foreach (var backup in _backups)
                {
                    backup.Restore();
                }
            }
        }

        private static void FixAllStateBackgrounds(GUIStyle style, Texture2D tex)
        {
            style.normal.background    = tex;
            style.hover.background     = tex;
            style.active.background    = tex;
            style.focused.background   = tex;
            style.onNormal.background  = tex;
            style.onHover.background   = tex;
            style.onActive.background  = tex;
            style.onFocused.background = tex;
        }

        // ─── Style Utilities ─────────────────────────────────────────────────

        /// <summary>
        /// GUIStyle の全 state の textColor を同一色に固定する。
        /// EditorStyles.* を継承したスタイルはライトモードの色を引き継ぐため、
        /// hover/active/focused/on* を含む全 state を明示設定して上書きする。
        /// </summary>
        private static void FixAllTextColors(GUIStyle style, Color color)
        {
            style.normal.textColor    = color;
            style.hover.textColor     = color;
            style.active.textColor    = color;
            style.focused.textColor   = color;
            style.onNormal.textColor  = color;
            style.onHover.textColor   = color;
            style.onActive.textColor  = color;
            style.onFocused.textColor = color;
        }

        // ─── Texture Utilities ───────────────────────────────────────────────

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D MakeBorderedTex(Color fillColor, Color borderColor)
        {
            const int size = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y,
                        (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                            ? borderColor
                            : fillColor);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >>  8) & 0xFF) / 255f,
            ( rgb        & 0xFF) / 255f);
    }
}
