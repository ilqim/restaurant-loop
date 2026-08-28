using UnityEngine;
using UnityEngine.EventSystems;
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
    ///
    /// ÖNEMLİ — İKİ AYRI ENGEL KONTROLÜ: Bir UI Canvas'ının (ör. Fail
    /// Panel) CanvasGroup.blocksRaycasts=true olması SADECE Unity'nin UI
    /// raycast sistemini (GraphicRaycaster) etkiler — bu script'in kendi
    /// yaptığı Physics.Raycast (3D dünya, Slot/QueueSlot collider'ları)
    /// bundan TAMAMEN bağımsızdır ve engellenmez. Bu yüzden burada
    /// AYRICA iki kontrol yapıyoruz:
    /// 1) EventSystem.current.IsPointerOverGameObject() — parmak/imleç
    ///    şu an HERHANGİ bir UI elemanının üzerindeyse (Fail/Win paneli,
    ///    Settings menüsü, herhangi bir buton vb.) 3D raycast'i hiç atma.
    /// 2) GameManager.Instance.IsPlaying — oyun Win/Fail durumundaysa
    ///    (panel henüz UI'ın tam üzerinde olmayan bir dokunuşla bile)
    ///    yine 3D raycast'i atma.
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
            // 1) Oyun Win/Fail durumundaysa (Playing değilse) hiç raycast atma.
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
                return;

            // 2) Dokunuş/tıklama şu an HERHANGİ bir UI elemanının üzerindeyse
            // (Fail/Win paneli, Settings menüsü, herhangi bir buton vb.)
            // yine raycast atma — bu, CanvasGroup'un blocksRaycasts'inin
            // kapsamadığı 3D dünya tıklamalarını da kapsıyor.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

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