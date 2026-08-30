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
    /// ÖNEMLİ — IsPointerOverGameObject() NEDEN Update()'DE:
    /// Unity'nin kendisi açıkça uyarıyor: "Calling IsPointerOverGameObject()
    /// from within event processing (such as from InputAction callbacks)
    /// will not work as expected". Bu kontrolü InputAction'ın "performed"
    /// callback'inin (OnTapped) İÇİNDEN çağırmak GÜVENİLİR DEĞİL — UI
    /// durumunu güncel olmayan bir anda sorgulayabiliyor. Bunun SOMUT
    /// SONUCU: "Continue" gibi bir UI butonuna basıldığında, tam o anda
    /// (button'ın kendi OnClick'i ile input system'in event sırası
    /// çakıştığı için) IsPointerOverGameObject() YANLIŞLIKLA false
    /// dönebiliyor — bu da 3D raycast'in butonun ARKASINDAKİ food/queue
    /// öğesine çarpıp onu da tetiklemesine yol açıyor (tam olarak
    /// yaşanan "Continue'ya basınca alttaki yemekler gidiyor" sorunu bu).
    ///
    /// ÇÖZÜM: OnTapped callback'i SADECE "bir tıklama oldu, ekran pozisyonu
    /// şu" bilgisini bir sonraki Update()'e devrediyor (bayrak + pozisyon).
    /// Asıl IsPointerOverGameObject() kontrolü VE raycast, normal bir
    /// Update() çağrısı içinde (Unity'nin garanti ettiği, güvenilir
    /// frame-processing bağlamında) yapılıyor.
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

        // OnTapped (InputAction callback) sadece bunları set eder — asıl
        // işlem Update()'de yapılır.
        private bool hasPendingTap;
        private Vector2 pendingTapScreenPos;

        private void OnEnable()
        {
            tapAction.Enable();
            tapAction.performed += OnTapped;
        }

        private void OnDisable()
        {
            tapAction.performed -= OnTapped;
            tapAction.Disable();
            hasPendingTap = false;
        }

        private void Start()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
        }

        /// <summary>
        /// InputAction'ın "performed" callback'i — BURADA HİÇBİR UI/raycast
        /// KONTROLÜ YAPILMIYOR, sadece "tıklama oldu + nerede" bilgisi
        /// bir sonraki Update()'e devrediliyor.
        /// </summary>
        private void OnTapped(InputAction.CallbackContext context)
        {
            pendingTapScreenPos = Pointer.current != null
                ? Pointer.current.position.ReadValue()
                : (Vector2)Mouse.current.position.ReadValue();

            hasPendingTap = true;
        }

        private void Update()
        {
            if (!hasPendingTap)
                return;

            hasPendingTap = false;

            ProcessTap(pendingTapScreenPos);
        }

        private void ProcessTap(Vector2 screenPos)
        {
            // 1) Oyun Win/Fail durumundaysa (Playing değilse) hiç raycast atma.
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
                return;

            // 2) Dokunuş/tıklama şu an HERHANGİ bir UI elemanının üzerindeyse
            // ARTIK GÜVENİLİR bir bağlamda (normal Update()) kontrol ediyoruz.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return;

            Ray ray = raycastCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, raycastDistance, raycastMask)) return;

            // Artık Food değil, ortak arayüz aranıyor — QueueSlot mu,
            // SlotClickTarget mı, ileride başka bir şey mi fark etmiyor.
            var clickable = hit.transform.GetComponentInParent<IQueueClickable>();
            clickable?.HandleClick();
        }
    }
}