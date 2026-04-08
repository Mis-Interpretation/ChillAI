using UnityEngine;
using Zenject;

namespace ChillAI.View.Window
{
    /// <summary>
    /// Sets up the camera for transparent background in standalone builds.
    /// In Editor, uses a solid dark color for debugging comfort.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TransparentCameraView : MonoBehaviour
    {
        Camera _cam;

        void Awake()
        {
            _cam = GetComponent<Camera>();

#if UNITY_EDITOR
            // Dark background for comfortable Editor debugging
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f, 1f);
#else
            // Transparent background for standalone build
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
#endif
        }
    }
}
