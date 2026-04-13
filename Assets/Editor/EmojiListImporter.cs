using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ChillAI.Core.Settings;
using UnityEditor;
using UnityEngine;

namespace ChillAI.Editor
{
    public class EmojiListImporter : EditorWindow
    {
        string _filePath = "";
        string _detectedFormat = "";
        EmojiListData _target;
        Vector2 _scrollPos;
        bool _replaceMode;

        readonly List<ParsedLine> _parsed = new();
        int _successCount;
        int _skippedCount;

        [MenuItem("ChillAI/Emoji List Importer")]
        static void Open()
        {
            var window = GetWindow<EmojiListImporter>("Emoji List Importer");
            window.minSize = new Vector2(480, 400);
        }

        // Also callable from right-click on an asset
        [MenuItem("Assets/Import Emojis into EmojiListData", true)]
        static bool ValidateAssetMenu() => Selection.activeObject is EmojiListData;

        [MenuItem("Assets/Import Emojis into EmojiListData")]
        static void OpenFromAsset()
        {
            var window = GetWindow<EmojiListImporter>("Emoji List Importer");
            window.minSize = new Vector2(480, 400);
            window._target = Selection.activeObject as EmojiListData;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            DrawFileSection();
            EditorGUILayout.Space(4);
            DrawPreviewSection();
            EditorGUILayout.Space(4);
            DrawImportSection();
        }

        // ── File Selection ──

        void DrawFileSection()
        {
            EditorGUILayout.LabelField("Source File", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _filePath = EditorGUILayout.TextField(_filePath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var path = EditorUtility.OpenFilePanel("Select Emoji File", "", "txt,csv");
                if (!string.IsNullOrEmpty(path))
                {
                    _filePath = path;
                    ParseFile();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_detectedFormat))
            {
                EditorGUILayout.HelpBox($"Format detected: {_detectedFormat}", MessageType.Info);
            }
        }

        // ── Preview Table ──

        void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (_parsed.Count == 0)
            {
                EditorGUILayout.HelpBox("No file loaded. Click Browse to select a .txt or .csv file.", MessageType.None);
                return;
            }

            // Stats
            EditorGUILayout.LabelField($"Parsed: {_successCount} / {_successCount + _skippedCount} lines  ({_skippedCount} skipped)");
            EditorGUILayout.Space(2);

            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("#", GUILayout.Width(30));
            GUILayout.Label("Emoji", GUILayout.Width(50));
            GUILayout.Label("Description", GUILayout.ExpandWidth(true));
            GUILayout.Label("Status", GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            // Rows
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

            for (var i = 0; i < _parsed.Count; i++)
            {
                var p = _parsed[i];
                var bg = GUI.backgroundColor;
                if (!p.ok) GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label((i + 1).ToString(), GUILayout.Width(30));
                GUILayout.Label(p.ok ? p.emoji : "???", GUILayout.Width(50));
                GUILayout.Label(p.ok ? p.description : p.rawLine, GUILayout.ExpandWidth(true));
                GUILayout.Label(p.ok ? "\u2713" : "\u2717", GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();

                GUI.backgroundColor = bg;
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Import Section ──

        void DrawImportSection()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);

            _target = (EmojiListData)EditorGUILayout.ObjectField(
                "Target EmojiListData", _target, typeof(EmojiListData), false);

            _replaceMode = EditorGUILayout.Toggle("Replace (clear existing)", _replaceMode);

            EditorGUI.BeginDisabledGroup(_target == null || _successCount == 0);
            EditorGUILayout.Space(4);
            if (GUILayout.Button($"Import {_successCount} Emojis", GUILayout.Height(30)))
                DoImport();
            EditorGUI.EndDisabledGroup();
        }

        // ── Parsing ──

        void ParseFile()
        {
            _parsed.Clear();
            _successCount = 0;
            _skippedCount = 0;
            _detectedFormat = "";

            if (!File.Exists(_filePath))
            {
                Debug.LogWarning($"[EmojiListImporter] File not found: {_filePath}");
                return;
            }

            var lines = File.ReadAllLines(_filePath);
            var formatVotes = new Dictionary<string, int>
            {
                { "comma", 0 }, { "tab", 0 }, { "dash", 0 }, { "space", 0 }, { "emoji-only", 0 }
            };

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var result = ParseLine(line);
                _parsed.Add(result);

                if (result.ok)
                {
                    _successCount++;
                    if (!string.IsNullOrEmpty(result.formatHint))
                        formatVotes[result.formatHint] = formatVotes.GetValueOrDefault(result.formatHint) + 1;
                }
                else
                {
                    _skippedCount++;
                }
            }

            // Detect dominant format
            var bestFormat = "mixed";
            var bestCount = 0;
            foreach (var kv in formatVotes)
            {
                if (kv.Value > bestCount) { bestFormat = kv.Key; bestCount = kv.Value; }
            }
            _detectedFormat = bestFormat;

            Repaint();
        }

        static ParsedLine ParseLine(string line)
        {
            // Scan for emoji grapheme clusters
            var emojiParts = new List<string>();
            var textParts = new List<string>();
            var currentText = new System.Text.StringBuilder();

            var enumerator = StringInfo.GetTextElementEnumerator(line);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                if (IsEmojiTextElement(element))
                {
                    if (currentText.Length > 0)
                    {
                        textParts.Add(currentText.ToString());
                        currentText.Clear();
                    }
                    emojiParts.Add(element);
                }
                else
                {
                    currentText.Append(element);
                }
            }
            if (currentText.Length > 0)
                textParts.Add(currentText.ToString());

            if (emojiParts.Count == 0)
                return new ParsedLine { ok = false, rawLine = line };

            // Take the first emoji as the entry emoji
            var emoji = emojiParts[0];

            // Join all text parts and clean up separators
            var rawDesc = string.Join(" ", textParts).Trim();
            var description = StripSeparators(rawDesc);

            // Detect format hint
            var formatHint = "emoji-only";
            if (!string.IsNullOrEmpty(rawDesc))
            {
                if (rawDesc.Contains('\t')) formatHint = "tab";
                else if (rawDesc.Contains(',')) formatHint = "comma";
                else if (rawDesc.Contains('-') || rawDesc.Contains('\u2014') || rawDesc.Contains('\u2013'))
                    formatHint = "dash";
                else formatHint = "space";
            }

            return new ParsedLine
            {
                ok = true,
                emoji = emoji,
                description = description,
                formatHint = formatHint,
                rawLine = line
            };
        }

        static string StripSeparators(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var result = text.Trim();

            // Strip leading/trailing common separators
            var separators = new[] { ',', '-', '\u2014', '\u2013', ':', '\u00B7', '|', ';', '\t' };
            result = result.Trim(separators).Trim();

            return result;
        }

        // ── Import ──

        void DoImport()
        {
            Undo.RecordObject(_target, "Import Emojis");

            if (_replaceMode)
                _target.emojis.Clear();

            var imported = 0;
            foreach (var p in _parsed)
            {
                if (!p.ok) continue;

                _target.emojis.Add(new EmojiEntry
                {
                    emoji = p.emoji,
                    description = p.description
                });
                imported++;
            }

            EditorUtility.SetDirty(_target);
            AssetDatabase.SaveAssets();

            Debug.Log($"[EmojiListImporter] Imported {imported} emojis into {_target.name}");
            EditorUtility.DisplayDialog("Import Complete",
                $"Successfully imported {imported} emojis into \"{_target.name}\".",
                "OK");
        }

        // ── Emoji detection (adapted from EmojiChatPanelView) ──

        static bool IsEmojiTextElement(string textElement)
        {
            if (string.IsNullOrEmpty(textElement)) return false;

            for (var i = 0; i < textElement.Length; i++)
            {
                var codePoint = char.ConvertToUtf32(textElement, i);
                if (char.IsSurrogatePair(textElement, i)) i++;
                if (IsEmojiBaseCodePoint(codePoint)) return true;
            }
            return false;
        }

        static bool IsEmojiBaseCodePoint(int codePoint)
        {
            return (codePoint >= 0x1F000 && codePoint <= 0x1FAFF) ||
                   (codePoint >= 0x2600 && codePoint <= 0x27BF) ||
                   (codePoint >= 0x2300 && codePoint <= 0x23FF) ||
                   (codePoint >= 0x2B00 && codePoint <= 0x2BFF);
        }

        // ── Data ──

        struct ParsedLine
        {
            public bool ok;
            public string emoji;
            public string description;
            public string formatHint;
            public string rawLine;
        }
    }
}
