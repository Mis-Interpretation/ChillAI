using UnityEditor;
using UnityEngine;

namespace ChillAI.Editor
{
    public static class FixTransparencySettings
    {
        [MenuItem("ChillAI/Fix Transparency Settings")]
        static void Fix()
        {
            PlayerSettings.preserveFramebufferAlpha = true;
            Debug.Log($"[ChillAI] preserveFramebufferAlpha = {PlayerSettings.preserveFramebufferAlpha}");
            Debug.Log("[ChillAI] Transparency settings applied. Please rebuild.");
        }

        [MenuItem("ChillAI/Check Transparency Settings")]
        static void Check()
        {
            Debug.Log($"[ChillAI] preserveFramebufferAlpha = {PlayerSettings.preserveFramebufferAlpha}");
        }
    }
}
