using UnityEngine;
using UnityEngine.InputSystem;

namespace RestaurantLoop
{
    /// <summary>
    /// SAHNEDEKİ TEK global tap dinleyicisi. Food, QueueManager artık kendi
    /// InputAction'ını dinlemiyor — collider'lar sadece QueueSlot ve
    /// SlotClickTarget üzerinde, ikisi de IQueueClickable implement ediyor.
    ///
    /// Pointer.current kullanmak hem mobilde touch'ı hem editörde mouse'u
    /// aynı kod yoluyla karşılıyor — ayrı bir #if UNITY_EDITOR dalına
    /// gerek yok. Input Action binding'i "&lt;Pointer&gt;/press" olarak
    /// kurulmalı (bu hem Touchscreen hem Mouse hem Pen'i kapsıyor).
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        [Tooltip("Binding: <Pointer>/press — hem editörde mouse hem cihazda touch ile çalışır.")]
        [SerializeField] private InputAction tapAction;

        [Tooltip("Raycast için kullanılacak kamera. Boşsa Camera.main kullanılır.")]
        [SerializeField] private Camera raycastCamera;

        [Tooltip("Raycast'in hangi layer'ları görmezden geleceği (opsiyonel).")]
        [SerializeField] private LayerMask raycastMask = ~0;

        [Tooltip("Raycast maksimum mesafesi.")]
        [SerializeField] private float raycastDistance = 1000f;

        private void OnEnable()
        {
            tapAction.Enable();
            tapAction.performed += OnTapped;
        }

        private void OnDisable()
        {
            tapAction.performed -= OnTapped;
            tapAction.Disable();
        }

        private void Start()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
        }

        private void OnTapped(InputAction.CallbackContext context)
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return;

            Vector2 screenPos = Pointer.current != null
                ? Pointer.current.position.ReadValue()
                : (Vector2)Mouse.current.position.ReadValue();

            Ray ray = raycastCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, raycastDistance, raycastMask)) return;

            // Artık Food değil, ortak arayüz aranıyor — QueueSlot mu,
            // SlotClickTarget mı, ileride başka bir şey mi fark etmiyor.
            var clickable = hit.transform.GetComponentInParent<IQueueClickable>();
            clickable?.HandleClick();
        }
    }
}