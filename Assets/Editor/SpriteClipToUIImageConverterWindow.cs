using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ChillAI.Editor
{
    public class SpriteClipToUIImageConverterWindow : EditorWindow
    {
        readonly List<AnimationClip> _clipInputs = new();

        DefaultAsset _inputFolder;
        DefaultAsset _outputFolder;
        Vector2 _scrollPos;

        [MenuItem("ChillAI/SpriteClip To UI Image Converter")]
        static void Open()
        {
            var window = GetWindow<SpriteClipToUIImageConverterWindow>("Sprite->UIImage");
            window.minSize = new Vector2(580f, 420f);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Batch Convert SpriteClip to UIImageClip", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool converts SpriteRenderer.m_Sprite animation bindings to UI.Image.m_Sprite.\n" +
                "Output always creates NEW AnimationClip assets and does not overwrite originals.",
                MessageType.Info);

            EditorGUILayout.Space(4f);
            DrawClipInputs();

            EditorGUILayout.Space(6f);
            DrawFolderInputs();

            EditorGUILayout.Space(8f);
            DrawRunSection();
        }

        void DrawClipInputs()
        {
            EditorGUILayout.LabelField("1) Source Animation Clips (optional)", EditorStyles.boldLabel);

            if (_clipInputs.Count == 0)
            {
                EditorGUILayout.HelpBox("No manual clips added. You can add clips below or use Input Folder.", MessageType.None);
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(160f));

            var removeIndex = -1;
            for (var i = 0; i < _clipInputs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _clipInputs[i] = (AnimationClip)EditorGUILayout.ObjectField(
                    $"Clip {i + 1}",
                    _clipInputs[i],
                    typeof(AnimationClip),
                    false);

                if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                _clipInputs.RemoveAt(removeIndex);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Clip Slot", GUILayout.Width(120f)))
            {
                _clipInputs.Add(null);
            }

            if (GUILayout.Button("Add Selected Clips", GUILayout.Width(140f)))
            {
                AddSelectedClips();
            }

            if (GUILayout.Button("Clear Manual List", GUILayout.Width(130f)))
            {
                _clipInputs.Clear();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawFolderInputs()
        {
            EditorGUILayout.LabelField("2) Folders", EditorStyles.boldLabel);
            _inputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Input Folder (optional)",
                _inputFolder,
                typeof(DefaultAsset),
                false);

            _outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Output Folder (optional)",
                _outputFolder,
                typeof(DefaultAsset),
                false);

            EditorGUILayout.HelpBox(
                "If Output Folder is empty, each new clip is saved beside the source clip.\n" +
                "UI Image target object is not required; clip paths are preserved from source bindings.",
                MessageType.None);
        }

        void DrawRunSection()
        {
            var collected = CollectSourceClipPaths();
            var hasInput = collected.Count > 0;

            EditorGUILayout.LabelField("3) Convert", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Detected clips to process: {collected.Count}");

            EditorGUI.BeginDisabledGroup(!hasInput);
            if (GUILayout.Button("Convert to UI Image Clips", GUILayout.Height(34f)))
            {
                RunConversion(collected);
            }
            EditorGUI.EndDisabledGroup();

            if (!hasInput)
            {
                EditorGUILayout.HelpBox("Please add clips or choose an input folder first.", MessageType.Warning);
            }
        }

        void AddSelectedClips()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is not AnimationClip clip)
                {
                    continue;
                }

                if (!_clipInputs.Contains(clip))
                {
                    _clipInputs.Add(clip);
                }
            }
        }

        List<string> CollectSourceClipPaths()
        {
            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var clip in _clipInputs)
            {
                if (clip == null)
                {
                    continue;
                }

                var clipPath = AssetDatabase.GetAssetPath(clip);
                if (!string.IsNullOrEmpty(clipPath))
                {
                    uniquePaths.Add(clipPath);
                }
            }

            var inputFolderPath = GetFolderPathOrNull(_inputFolder);
            if (!string.IsNullOrEmpty(inputFolderPath))
            {
                var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { inputFolderPath });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        uniquePaths.Add(path);
                    }
                }
            }

            return new List<string>(uniquePaths);
        }

        void RunConversion(List<string> clipPaths)
        {
            var outputFolderPath = GetFolderPathOrNull(_outputFolder);
            if (_outputFolder != null && string.IsNullOrEmpty(outputFolderPath))
            {
                Debug.LogWarning("[SpriteClipToUIImage] Output folder is invalid. Please select a project folder.");
                EditorUtility.DisplayDialog("Invalid Output Folder", "Please select a valid project folder for output.", "OK");
                return;
            }

            var successCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var createdPaths = new List<string>();

            try
            {
                for (var i = 0; i < clipPaths.Count; i++)
                {
                    var path = clipPaths[i];
                    EditorUtility.DisplayProgressBar("Converting Animation Clips", $"Processing {Path.GetFileName(path)}", (float)(i + 1) / clipPaths.Count);

                    var sourceClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (sourceClip == null)
                    {
                        failedCount++;
                        Debug.LogWarning($"[SpriteClipToUIImage] Failed to load clip: {path}");
                        continue;
                    }

                    if (!TryCreateConvertedClip(sourceClip, path, outputFolderPath, out var createdPath, out var convertedBindings, out var error))
                    {
                        if (error == null)
                        {
                            skippedCount++;
                            Debug.LogWarning($"[SpriteClipToUIImage] Skipped \"{sourceClip.name}\". No SpriteRenderer.m_Sprite curve found.");
                        }
                        else
                        {
                            failedCount++;
                            Debug.LogError($"[SpriteClipToUIImage] Failed \"{sourceClip.name}\": {error}");
                        }
                        continue;
                    }

                    successCount++;
                    createdPaths.Add(createdPath);
                    Debug.Log($"[SpriteClipToUIImage] Converted \"{sourceClip.name}\" with {convertedBindings} binding(s) -> {createdPath}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var title = "SpriteClip -> UIImageClip Done";
            var message =
                $"Success: {successCount}\n" +
                $"Skipped: {skippedCount}\n" +
                $"Failed: {failedCount}\n\n" +
                "See Console logs for details.";
            EditorUtility.DisplayDialog(title, message, "OK");

            if (createdPaths.Count > 0)
            {
                Debug.Log("[SpriteClipToUIImage] Created clips:\n" + string.Join("\n", createdPaths));
            }
        }

        bool TryCreateConvertedClip(
            AnimationClip sourceClip,
            string sourcePath,
            string outputFolderPath,
            out string createdPath,
            out int convertedBindings,
            out Exception error)
        {
            createdPath = "";
            convertedBindings = 0;
            error = null;

            try
            {
                var newClip = Instantiate(sourceClip);
                newClip.name = $"{sourceClip.name}_UI";

                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(newClip);
                foreach (var binding in bindings)
                {
                    if (binding.type != typeof(SpriteRenderer) || binding.propertyName != "m_Sprite")
                    {
                        continue;
                    }

                    var keyframes = AnimationUtility.GetObjectReferenceCurve(newClip, binding);
                    AnimationUtility.SetObjectReferenceCurve(newClip, binding, null);

                    var targetBinding = binding;
                    targetBinding.type = typeof(Image);
                    targetBinding.propertyName = "m_Sprite";
                    AnimationUtility.SetObjectReferenceCurve(newClip, targetBinding, keyframes);
                    convertedBindings++;
                }

                if (convertedBindings == 0)
                {
                    DestroyImmediate(newClip);
                    return false;
                }

                var targetFolder = !string.IsNullOrEmpty(outputFolderPath) ? outputFolderPath : Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");
                if (string.IsNullOrEmpty(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
                {
                    DestroyImmediate(newClip);
                    error = new InvalidOperationException($"Invalid target folder: {targetFolder}");
                    return false;
                }

                var outputPath = AssetDatabase.GenerateUniqueAssetPath($"{targetFolder}/{sourceClip.name}_UI.anim");
                AssetDatabase.CreateAsset(newClip, outputPath);
                createdPath = outputPath;
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        static string GetFolderPathOrNull(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
            {
                return null;
            }

            return path;
        }
    }
}
