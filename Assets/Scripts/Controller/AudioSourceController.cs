using UnityEngine;

public class AudioSourceController : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;

    [Header("Random Pitch")]
    [SerializeField] private bool randomizePitch = true;
    [SerializeField, Range(-3f, 3f)] private float minPitch = 0.9f;
    [SerializeField, Range(-3f, 3f)] private float maxPitch = 1.1f;

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    // 这个方法之后会被动画事件调用
    public void PlaySound()
    {
        if (audioSource == null) return;

        if (randomizePitch)
        {
            float lo = Mathf.Min(minPitch, maxPitch);
            float hi = Mathf.Max(minPitch, maxPitch);
            audioSource.pitch = Random.Range(lo, hi);
        }

        audioSource.Play();
    }
}
