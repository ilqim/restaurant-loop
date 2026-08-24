using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public class ResponsiveCamera : MonoBehaviour
{
    [Header("Reference Settings")]
    [Tooltip("Target design width (e.g., 1080)")]
    [SerializeField] private float referenceWidth = 1080f;

    [Tooltip("Target design height (e.g., 1920)")]
    [SerializeField] private float referenceHeight = 1920f;

    [Tooltip("Camera orthographic size configured for the reference resolution")]
    [SerializeField] private float referenceOrthoSize = 10f;

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        AdjustCamera();
    }

    private void Update()
    {
#if UNITY_EDITOR
        // Keeps camera synced during Game view resizing in Editor
        AdjustCamera();
#endif
    }

    public void AdjustCamera()
    {
        if (cam == null) cam = GetComponent<Camera>();
        if (!cam.orthographic) return;

        float targetAspect = referenceWidth / referenceHeight;
        float currentAspect = (float)Screen.width / Screen.height;

        // If the screen is narrower than the target aspect ratio (e.g. tablet vs taller phone),
        // expand the orthographic size to guarantee the horizontal width fits without cropping.
        if (currentAspect < targetAspect)
        {
            cam.orthographicSize = referenceOrthoSize * (targetAspect / currentAspect);
        }
        else
        {
            // If wider/taller than reference, maintain the reference orthographic size
            cam.orthographicSize = referenceOrthoSize;
        }
    }
}