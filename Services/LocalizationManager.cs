using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace DennokoWorks.Tool.AOBaker
{
    public enum Language { Japanese, English }

    public static class LocalizationManager
    {
        private const string PrefKey = "DennokoWorks.AOBaker.Language";
        private static Language _currentLanguage = Language.Japanese;
        private static Dictionary<string, string> _translations = new Dictionary<string, string>();
        private static bool _initialized = false;

        public static Language CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value || !_initialized)
                {
                    _currentLanguage = value;
                    EditorPrefs.SetInt(PrefKey, (int)_currentLanguage);
                    LoadTranslations();
                }
            }
        }

        public static void Initialize()
        {
            if (_initialized) return;

            int savedLang = EditorPrefs.GetInt(PrefKey, (int)Language.Japanese);
            _currentLanguage = (Language)savedLang;
            LoadTranslations();
            _initialized = true;
        }

        private static void LoadTranslations()
        {
            string fileName = _currentLanguage == Language.Japanese ? "ja" : "en";
            string path = $"Assets/Editor/FastAOBaker/Source/Language/{fileName}.json";
            
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null)
            {
                var data = JsonUtility.FromJson<SerializationHelper>(asset.text);
                if (data != null && data.items != null)
                {
                    // JsonUtility does not support Dictionary directly.
                    // But if the JSON is a simple key-value object, we need a different approach or a wrapper.
                    // Actually, simple JSON objects can't be parsed into Dictionary easily with JsonUtility.
                    // I'll use a simple manual parser for this specific flat JSON structure if needed, 
                    // or I'll change the JSON format to be compatible with JsonUtility.
                }
                
                // Let's use a simpler way for flat JSON:
                _translations.Clear();
                try {
                    // Match "key": "value" patterns, handling escaped quotes and newlines
                    var matches = System.Text.RegularExpressions.Regex.Matches(asset.text, @"\""(.+?)\""\s*:\s*\""((?:[^\""\\]|\\.)*)\""");
                    foreach (System.Text.RegularExpressions.Match match in matches) {
                        string key = match.Groups[1].Value;
                        string val = match.Groups[2].Value;
                        // Unescape common characters
                        val = System.Text.RegularExpressions.Regex.Unescape(val);
                        _translations[key] = val;
                    }
                } catch (System.Exception e) {
                    Debug.LogError($"[AO Baker] Failed to parse localization file: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[AO Baker] Localization file not found: {path}");
            }
        }

        public static string Get(string key)
        {
            if (!_initialized) Initialize();
            if (_translations.TryGetValue(key, out string value))
            {
                return value;
            }
            return key;
        }

        [System.Serializable]
        private class SerializationHelper
        {
            public List<TranslationItem> items;
        }

        [System.Serializable]
        private class TranslationItem
        {
            public string key;
            public string value;
        }
    }
}
