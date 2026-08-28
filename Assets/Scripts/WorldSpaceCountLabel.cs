using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    public class WorldSpaceCountLabel : MonoBehaviour
    {
        [Header("Referanslar")]
        [SerializeField] private TextMeshPro label;
        [SerializeField] private SpriteRenderer background;

        [Header("Kamera")]
        [SerializeField] private Camera cameraOverride;
        [SerializeField] private bool warnIfCameraMissing = true;
        [SerializeField] private bool hideWhenZeroOrLess = true;

        [Header("Katmanlama (Z-Fighting Önleme)")]
        [Tooltip("Text'in background'un önünde görünmesi için kameraya doğru olan local Z offset miktarı.")]
        [SerializeField] private float textZOffset = -0.02f;

        private Transform selfTransform;
        private Camera facingCamera;
        private bool loggedMissingCamera;

        private void Awake()
        {
            selfTransform = transform;
            if (label == null)
                label = GetComponentInChildren<TextMeshPro>(true);
            if (background == null)
                background = GetComponentInChildren<SpriteRenderer>(true);
            if (cameraOverride != null)
                facingCamera = cameraOverride;
        }

        private void Start()
        {
            // Text'in background içinde kaybolmaması için local pozisyonları merkeze hizala
            if (background != null)
            {
                background.transform.localPosition = Vector3.zero;
            }

            if (label != null)
            {
                label.transform.localPosition = new Vector3(0f, 0f, textZOffset);
                label.alignment = TextAlignmentOptions.Center;
                
                // Text'in sprite'ın önünde renderlanmasını garanti et
                if (background != null)
                {
                    label.sortingOrder = background.sortingOrder + 1;
                    label.sortingLayerID = background.sortingLayerID;
                }
            }
        }

        private void LateUpdate()
        {
            if (facingCamera == null)
            {
                facingCamera = cameraOverride != null ? cameraOverride : Camera.main;
                if (facingCamera == null)
                    facingCamera = FindFirstObjectByType<Camera>();
            }

            if (facingCamera == null)
            {
                if (warnIfCameraMissing && !loggedMissingCamera)
                {
                    loggedMissingCamera = true;
                    Debug.LogWarning($"WorldSpaceCountLabel [{gameObject.name}]: Kamera bulunamadı.", this);
                }
                return;
            }

            // Doğrudan kameranın rotasyonuna kitle (Orbit yapmadan kameraya dik bakar)
            selfTransform.rotation = facingCamera.transform.rotation;
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetCount(int count)
        {
            bool show = !(count <= 0 && hideWhenZeroOrLess);
            if (label != null)
                label.text = show ? count.ToString() : "";
            if (background != null)
                background.enabled = show;
        }

        public void Clear()
        {
            if (label != null)
                label.text = "";
            if (background != null)
                background.enabled = false;
        }
    }
}