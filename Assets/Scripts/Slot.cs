using UnityEngine;

namespace RestaurantLoop
{
    public enum SlotState
    {
        Empty,
        Occupied
    }

    /// <summary>
    /// Food-slot (konveyör sonu). Tıklanabilir collider'ı DA burada taşıyor
    /// — ayrı bir "SlotClickTarget" component'ine gerek yok, Slot kendisi
    /// IQueueClickable implement ediyor. Food'un kendisinde collider yok.
    ///
    /// GÖRSEL: Ayrı bir prefab instantiate ETMİYORUZ. Bu objenin ÜZERİNDE
    /// zaten duran SpriteRenderer'ın sprite/color'ını, atanan food'a göre
    /// değiştiriyoruz. Obje sayısı hiç artmıyor, extra collider/script
    /// oluşmuyor, GC allocation yok — en ucuz yöntem.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class Slot : MonoBehaviour, IQueueClickable
    {
        [Header("State")]
        [SerializeField] private SlotState currentState = SlotState.Empty;

        [Header("Food")]
        [SerializeField] private Food currentFood;

        [Header("Yerleşim")]
        [Tooltip("Food, slot pozisyonunun ne kadar yukarısına yerleştirilsin (dünya Y ekseni).")]
        [SerializeField] private float foodYOffset = 0.3f;

        [Header("Görsel")]
        [Tooltip("Slot doluyken gösterilecek TEK sprite — food'a göre değişmez, tüm food'lar için aynı sprite kullanılır. Food'lar arası fark sadece renk (Food.SlotColor / hex) ile sağlanır.")]
        [SerializeField] private Sprite occupiedSprite;

        [Header("Debug")]
        [SerializeField] private bool verboseFallbackLog = true;

        [Header("Sayı Etiketi")]
        [Tooltip("İçindeki food'un kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi).")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

        private SpriteRenderer spriteRenderer;
        private Sprite defaultSprite;
        private Color defaultColor;

        public SlotState CurrentState => currentState;
        public Food CurrentFood => currentFood;

        public bool IsEmpty => currentState == SlotState.Empty;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            // Prefab'de baştan atanmış olan sprite+renk "boş slot" görünümü
            // kabul ediliyor — bu AYNEN korunuyor, food çıktığında buna
            // geri dönülüyor.
            defaultSprite = spriteRenderer.sprite;
            defaultColor = spriteRenderer.color;

            // Slot başlangıçta Empty — count label child'ı da baştan kapalı olsun.
            countLabel?.SetVisible(false);
        }

        private void Update()
        {
            // GÜVENLİK: Normal akışta slot, OnReenterRequested üzerinden
            // RemoveFood() çağrılarak boşalır. Ama food herhangi bir sebeple
            // (örn. Tray/pool tarafında beklenmedik bir Destroy, ya da event
            // zincirinin atlandığı bir edge-case) bu akışı hiç tetiklemeden
            // yok olursa, slot bunu fark edemez ve Occupied/eski sprite'ta
            // takılı kalır. Bu kontrol, her frame currentFood'un hâlâ var
            // olup olmadığına bakıp (Unity'de destroy edilmiş obje == null
            // döner) kendini otomatik olarak boşaltıyor.
            if (currentState == SlotState.Occupied && currentFood == null)
            {
                RemoveFood();
            }
        }

        public bool TryPlaceFood(Food food)
        {
            if (currentState == SlotState.Occupied)
                return false;

            if (food == null)
                return false;

            currentFood = food;
            currentState = SlotState.Occupied;

            Vector3 pos = transform.position;
            pos.y += foodYOffset;
            food.transform.position = pos;

            // Food'un "slottan çıkmak istiyorum" isteğine ABONE OLUYORUZ.
            // Bu, food'un elle çağırdığı, garanti sıralı bir istek —
            // eski "state değişti, umarım biri yakalar" mantığından farklı.
            food.ReenterConveyorRequested += OnReenterRequested;

            food.SetInFoodSlot();

            // ÖNCE sprite'ı "dolu" görünümüne çeviriyoruz (defaultSprite hâlâ
            // saklı duruyor, RemoveFood'da tekrar kullanılacak). SONRA bu
            // food'a özel rengi (hex'ten çevrilmiş) uyguluyoruz.
            spriteRenderer.sprite = occupiedSprite != null ? occupiedSprite : defaultSprite;
            spriteRenderer.color = food.SlotColor;

            // Capacity 0 (ya da altı) ise içinde gösterilecek bir sayı yok
            // demektir — countLabel'ı SetVisible(false) ile tamamen kapatıyoruz.
            // Pozitifse aktif edip sayıyı yazıyoruz.
            if (food.Capacity <= 0)
            {
                countLabel?.SetVisible(false);
            }
            else
            {
                countLabel?.SetVisible(true);
                countLabel?.SetCount(food.Capacity);
            }

            return true;
        }

        /// <summary>
        /// Food (slottayken) tıklanıp konveyöre dönmek istediğinde tetiklenir.
        /// SIRALAMA KESİN: slot ancak food GERÇEKTEN konveyöre çıkabildiyse
        /// boşalır — aksi halde (konveyör doluysa) food slotta kalmaya
        /// devam eder ve slot Occupied kalır.
        /// </summary>
        private void OnReenterRequested(Food food)
        {
            food.ReenterConveyorRequested -= OnReenterRequested; // tek seferlik, tekrar abone kalmasın

            bool left = food.EnterConveyorFromSlot();

            if (left)
            {
                RemoveFood();
            }
            else
            {
                if (verboseFallbackLog)
                    Debug.Log("Slot: Konveyöre çıkış başarısız, food slotta kaldı. Tekrar tıklanabilir olması için yeniden abone olunuyor.");

                // Tekrar tıklanabilsin diye event'e yeniden abone ol.
                food.ReenterConveyorRequested += OnReenterRequested;
            }
        }

        public void RemoveFood()
        {
            if (currentFood != null)
                currentFood.ReenterConveyorRequested -= OnReenterRequested; // güvenlik: her ihtimalde aboneliği temizle

            currentFood = null;
            currentState = SlotState.Empty;

            // Slot boşaldı, default sprite/renge dön.
            spriteRenderer.sprite = defaultSprite;
            spriteRenderer.color = defaultColor;

            countLabel?.Clear();
        }

        /// <summary>
        /// Eski SlotClickTarget'ın yaptığı iş — artık ayrı bir component
        /// değil, doğrudan Slot'un kendisi. IQueueClickable.HandleClick.
        /// </summary>
        public void HandleClick()
        {
            if (IsEmpty) return;
            CurrentFood?.ActivateFromTap();
        }
    }
}