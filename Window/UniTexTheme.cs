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

            TitleStyle = new GUIStyle(GUIStyle.none);
            TitleStyle.fontSize = 14;
            TitleStyle.fontStyle = FontStyle.Bold;
            ApplyTextColorAllStates(TitleStyle, TextPrimary);

            SectionHeaderStyle = new GUIStyle(GUIStyle.none);
            SectionHeaderStyle.fontSize = 10;
            SectionHeaderStyle.fontStyle = FontStyle.Bold;
            ApplyTextColorAllStates(SectionHeaderStyle, TextTertiary);
            SectionHeaderStyle.margin = new RectOffset(0, 0, 0, 2);

            ToggleSectionOnStyle = new GUIStyle(GUIStyle.none);
            ToggleSectionOnStyle.fontSize = 10;
            ToggleSectionOnStyle.fontStyle = FontStyle.Bold;
            ApplyTextColorAllStates(ToggleSectionOnStyle, TextPrimary);
            ToggleSectionOnStyle.margin = new RectOffset(0, 0, 0, 2);

            ToggleSectionOffStyle = new GUIStyle(GUIStyle.none);
            ToggleSectionOffStyle.fontSize = 10;
            ToggleSectionOffStyle.fontStyle = FontStyle.Bold;
            ApplyTextColorAllStates(ToggleSectionOffStyle, TextTertiary);
            ToggleSectionOffStyle.margin = new RectOffset(0, 0, 0, 2);

            SecondaryTextStyle = new GUIStyle(GUIStyle.none);
            SecondaryTextStyle.fontSize = 11;
            ApplyTextColorAllStates(SecondaryTextStyle, TextSecondary);
            SecondaryTextStyle.wordWrap = true;

            CaptionStyle = new GUIStyle(GUIStyle.none);
            CaptionStyle.fontSize = 10;
            ApplyTextColorAllStates(CaptionStyle, TextTertiary);

            // ── Buttons ──────────────────────────────────────────────────────

            ActionButtonStyle = new GUIStyle();
            ActionButtonStyle.normal.background  = _texAccentCard;
            ActionButtonStyle.normal.textColor   = TextPrimary;
            ActionButtonStyle.hover.background   = MakeTex(Color.Lerp(Surface2, Color.white, 0.07f));
            ActionButtonStyle.hover.textColor    = TextPrimary;
            ActionButtonStyle.active.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.15f));
            ActionButtonStyle.active.textColor   = TextPrimary;
            ActionButtonStyle.focused.background = ActionButtonStyle.normal.background;
            ActionButtonStyle.focused.textColor  = TextPrimary;
            ActionButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            ActionButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            ActionButtonStyle.padding    = new RectOffset(8, 8, 4, 4);
            ActionButtonStyle.fontSize   = 13;
            ActionButtonStyle.fontStyle  = FontStyle.Bold;
            ActionButtonStyle.fixedHeight = 34;
            ActionButtonStyle.alignment  = TextAnchor.MiddleCenter;

            SecondaryButtonStyle = new GUIStyle();
            SecondaryButtonStyle.normal.background = MakeBorderedTex(Surface1, Outline);
            SecondaryButtonStyle.normal.textColor  = TextSecondary;
            SecondaryButtonStyle.hover.background  = _texAccentCard;
            SecondaryButtonStyle.hover.textColor   = TextPrimary;
            SecondaryButtonStyle.active.background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f));
            SecondaryButtonStyle.active.textColor  = TextPrimary;
            SecondaryButtonStyle.focused.background = SecondaryButtonStyle.normal.background;
            SecondaryButtonStyle.focused.textColor  = TextSecondary;
            SecondaryButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            SecondaryButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            SecondaryButtonStyle.padding    = new RectOffset(6, 6, 3, 3);
            SecondaryButtonStyle.fontSize   = 11;
            SecondaryButtonStyle.fixedHeight = 26;
            SecondaryButtonStyle.alignment  = TextAnchor.MiddleCenter;

            MiniButtonStyle = new GUIStyle();
            MiniButtonStyle.normal.background = MakeBorderedTex(Surface1, Outline);
            MiniButtonStyle.hover.background = _texAccentCard;
            MiniButtonStyle.active.background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f));
            MiniButtonStyle.focused.background = MiniButtonStyle.normal.background;
            MiniButtonStyle.border = new RectOffset(1, 1, 1, 1);
            MiniButtonStyle.padding = new RectOffset(6, 6, 2, 2);
            MiniButtonStyle.margin = new RectOffset(0, 0, 0, 0);
            MiniButtonStyle.alignment = TextAnchor.MiddleCenter;
            MiniButtonStyle.fontSize = 10;
            MiniButtonStyle.fixedHeight = 18;
            ApplyTextColorAllStates(MiniButtonStyle, TextTertiary);
            MiniButtonStyle.hover.textColor = TextSecondary;
            MiniButtonStyle.active.textColor = TextPrimary;

            MiniButtonLeftStyle = new GUIStyle(MiniButtonStyle);
            MiniButtonLeftStyle.border = new RectOffset(1, 1, 1, 1);
            MiniButtonLeftStyle.margin = new RectOffset(0, 0, 0, 0);

            MiniButtonRightStyle = new GUIStyle(MiniButtonStyle);
            MiniButtonRightStyle.border = new RectOffset(1, 1, 1, 1);
            MiniButtonRightStyle.margin = new RectOffset(0, 0, 0, 0);

            // ── Status Bar ───────────────────────────────────────────────────

            var statusBase = new GUIStyle();
            statusBase.border  = new RectOffset(1, 1, 1, 1);
            statusBase.padding = new RectOffset(8, 8, 5, 5);
            statusBase.margin  = new RectOffset(4, 4, 2, 2);
            statusBase.fontSize = 11;

            StatusInfoStyle = new GUIStyle(statusBase);
            StatusInfoStyle.normal.background = _texSurface1;
            StatusInfoStyle.normal.textColor  = TextSecondary;

            StatusSuccessStyle = new GUIStyle(statusBase);
            StatusSuccessStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticSuccess, 0.3f));
            StatusSuccessStyle.normal.textColor  = SemanticSuccess;

            StatusErrorStyle = new GUIStyle(statusBase);
            StatusErrorStyle.normal.background = MakeTex(Color.Lerp(Surface1, SemanticError, 0.5f));
            StatusErrorStyle.normal.textColor  = new Color(1f, 0.65f, 0.65f);
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

        private static void ApplyTextColorAllStates(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }
    }
}
