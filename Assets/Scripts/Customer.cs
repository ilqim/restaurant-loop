using UnityEditor.Animations;
using UnityEngine;

namespace RestaurantLoop
{
    public enum CustomerState
    {
        Blocked,
        Idle,
        Serving,
        Eating,
        HappyJump,
        Leaving,
        Angry
    }

    public class Customer : MonoBehaviour
    {
        [Header("Görsel — Blocked iken saydamlaşacak renderer'lar")]
        [Tooltip("Boş bırakılırsa Awake'te GetComponentsInChildren<Renderer> ile otomatik doldurulur.")]
        [SerializeField] private Renderer[] renderersToFade;

        [Range(0f, 1f)]
        [SerializeField] private float blockedAlpha = 0.35f;

        [Header("Customer Order Bubble")]
        [Tooltip("Prefab içindeki adı 'Bubble' olan child obje otomatik bulunur. Inspector'dan atamana gerek yok.")]
        [SerializeField] private Transform orderBubble;

        [Header("Debug")]
        [SerializeField] private CustomerState currentState = CustomerState.Blocked;
        [SerializeField] private bool verboseLogging = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Unity'nin Instance ID'si pool'dan geri dönüştürülen (SetActive
        // false/true) objelerde DEĞİŞMEZ — bu yüzden "aynı obje" ile "aynı
        // sipariş/müşteri" farklı şeylerdir. Bir Tray, Update() fazında bu
        // müşteriyi aday olarak bulduktan SONRA, başka bir Tray bu
        // müşteriyi servis edip Despawn edebilir ve pool aynı GameObject'i
        // TAMAMEN FARKLI bir müşteri için hemen yeniden kullanabilir. İlk
        // Tray ateş etme anına geldiğinde elindeki referans hâlâ aynı
        // objeye işaret eder ama artık BAŞKA bir sipariştir.
        //
        // OrderSessionId, HER Init() çağrısında (yani her gerçek yeni
        // müşteri/sipariş başladığında) artan, pool'dan tamamen bağımsız
        // bir sayaçtır. Bir Tray, ateş etmeden hemen önce bu ID'nin
        // Update()'te gördüğüyle hâlâ aynı olduğunu doğrular; değiştiyse
        // bu artık farklı bir müşteridir ve ateş edilmez.
        private static int nextOrderSessionId = 1;
        public int OrderSessionId { get; private set; }

        private CustomerManager customerManager;
        private MaterialPropertyBlock mpb;
        private Color[] originalColors;
        private bool initialized;

        // Bu müşteriye şu anda teslimat yapmakta olan kaynak.
        // Food.cs VEYA Tray.cs olabilir — kasıtlı olarak genel `object`
        // tutuluyor ki iki farklı teslimat sistemi (slot'taki Food ve
        // conveyor'daki Tray) AYNI rezervasyonu paylaşsın. Eskiden bu alan
        // sadece Food tipindeydi ve Tray kendi ayrı (static) rezervasyon
        // setini kullanıyordu; bu da iki sistemin birbirinden habersiz
        // kalıp AYNI müşteriye birden fazla yemek göndermesine yol açıyordu.
        private object incomingDeliverySource;

        public int Row { get; private set; }
        public int Col { get; private set; }
        public FoodType DesiredFood { get; private set; }
        public CustomerState CurrentState => currentState;
        private Animator animator;

        public bool IsWaiting =>
            currentState == CustomerState.Idle ||
            currentState == CustomerState.Blocked ||
            currentState == CustomerState.Serving;

        // Başka bir kaynak (Food ya da Tray) şu anda bu müşteriye
        // yemek gönderiyor mu?
        public bool IsReceivingFood => incomingDeliverySource != null;


        // ============================================================
        // AWAKE
        // ============================================================

        private void Awake()
        {
            // Renderer'ları otomatik bul.
            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = GetComponentsInChildren<Renderer>(true);

            animator = GetComponentInChildren<Animator>();

            // Prefab içindeki "Bubble" child'ını otomatik bul.
            FindOrderBubble();

            // MaterialPropertyBlock hazırla.
            mpb = new MaterialPropertyBlock();

            // Bubble'ı instantiate edilir edilmez kameraya hizala.
            AlignOrderBubbleToCamera();
        }


        // ============================================================
        // BUBBLE BULMA
        // ============================================================

        private void FindOrderBubble()
        {
            // Zaten atanmışsa tekrar arama.
            if (orderBubble != null)
                return;

            Transform[] children =
                GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children)
            {
                if (child.name == "Bubble")
                {
                    orderBubble = child;
                    return;
                }
            }

            Debug.LogWarning(
                $"Customer [{name}]: Child objeler arasında 'Bubble' isimli obje bulunamadı."
            );
        }


        // ============================================================
        // INIT
        // ============================================================

        public void Init(
            int row,
            int col,
            FoodType desiredFood,
            CustomerManager manager)
        {
            // Pool'dan geri dönüştürülmüş olsa bile bu ARTIK yeni/farklı
            // bir sipariştir — Instance ID aynı kalsa da OrderSessionId
            // burada mutlaka yeni bir değer alır.
            OrderSessionId = nextOrderSessionId++;

            Row = row;
            Col = col;
            DesiredFood = desiredFood;
            customerManager = manager;

            currentState = CustomerState.Blocked;

            incomingDeliverySource = null;

            // Pool'dan tekrar kullanılıyorsa Bubble referansını
            // garantiye al.
            FindOrderBubble();

            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade =
                    GetComponentsInChildren<Renderer>(true);

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            CacheOriginalColors();
            ApplyVisual();

            // Init sırasında da tekrar hizala.
            // Böylece pool'dan geri geldiğinde de doğru olur.
            AlignOrderBubbleToCamera();

            initialized = true;

            if (verboseLogging)
            {
                Debug.Log(
                    $"Customer [{name}] (ID={GetInstanceID()}, " +
                    $"Session={OrderSessionId}) " +
                    $"Init: Row={row}, Col={col}, Desired={desiredFood}"
                );
            }

            if (customerManager != null)
            {
                customerManager.RegisterCustomer(this);
            }
            else
            {
                Debug.LogWarning(
                    $"Customer [{name}]: CustomerManager atanmadı, state sistemi çalışmayacak."
                );
            }
        }


        // ============================================================
        // BUBBLE KAMERAYA HİZALAMA
        // ============================================================

        private void AlignOrderBubbleToCamera()
        {
            if (orderBubble == null)
                return;

            Camera cam = Camera.main;

            if (cam == null)
                return;

            /*
             * Bubble'ın yüzeyini kameranın görüş düzlemine paralel yapıyoruz.
             *
             * Kamera sabit olduğu için her frame billboard yapmıyoruz.
             * Sadece oluşturulurken / pool'dan geri alınırken bir kez
             * hizalanıyor.
             */
            orderBubble.rotation =
                Quaternion.LookRotation(
                    cam.transform.forward,
                    cam.transform.up
                );
        }


        // ============================================================
        // TESLİMAT REZERVASYONU (Food VE Tray ORTAK)
        // ============================================================

        /// <summary>
        /// Bir teslimat kaynağı (Food veya Tray) bu müşteriye yemek
        /// göndermeden önce müşteriyi rezerve eder. Aynı müşteriye
        /// ikinci bir teslimatın başlamasını engeller — kaynak fark
        /// etmeksizin (iki Food, iki Tray, ya da bir Food + bir Tray
        /// aynı anda hedeflemeye çalışsa bile).
        /// </summary>
        public bool TryReserveForDelivery(object source, FoodType requestedFood)
        {
            if (source == null)
                return false;

            // Zaten başka bir kaynak bu müşteriye yemek gönderiyor.
            if (incomingDeliverySource != null)
                return false;

            // Müşteri sadece Idle iken rezerve edilebilir. Serving,
            // Blocked, Eating, HappyJump, Leaving, Angry — hiçbiri
            // yeniden rezerve edilemez.
            if (currentState != CustomerState.Idle)
                return false;

            // İstenen yemek ile gönderilen yemek eşleşmeli.
            if (DesiredFood != requestedFood)
                return false;

            // Müşteriyi bu kaynak için kilitle. State'i de HEMEN
            // Serving'e çekiyoruz — böylece "rezerve edildi ama hâlâ
            // Idle görünüyor" penceresi tamamen ortadan kalkıyor; state
            // üzerinden bakan HER kontrol artık bunu doğru görür.
            incomingDeliverySource = source;
            SetState(CustomerState.Serving);

            if (verboseLogging)
            {
                Debug.Log(
                    $"Customer [{name}] (ID={GetInstanceID()}, " +
                    $"Session={OrderSessionId}) " +
                    $"RESERVED by {source} -> state=Serving " +
                    $"(SourceID={(source as Object)?.GetInstanceID()})"
                );
            }

            return true;
        }

        /// <summary>
        /// Geriye dönük uyumluluk için korunan Food-özel sarmalayıcı.
        /// İçeride ortak TryReserveForDelivery'yi çağırır.
        /// </summary>
        public bool TryReserveForFood(Food food)
        {
            if (food == null)
                return false;

            return TryReserveForDelivery(food, food.FoodTypeValue);
        }

        /// <summary>
        /// Teslimat kaynağı (Food veya Tray) rezervasyonu bırakır —
        /// sadece rezervasyonun sahibi olan kaynak bırakabilir. Teslimat
        /// TAMAMLANMADAN (ör. klon kayboldu) rezervasyon iptal edilirse,
        /// müşteri tekrar servis edilebilir olsun diye state Idle'a
        /// geri döner.
        /// </summary>
        public void ReleaseDeliveryReservation(object source)
        {
            if (source == null)
                return;

            if (incomingDeliverySource != source)
                return;

            incomingDeliverySource = null;

            if (currentState == CustomerState.Serving)
            {
                SetState(CustomerState.Idle);

                if (verboseLogging)
                {
                    Debug.Log(
                        $"Customer [{name}] (ID={GetInstanceID()}, " +
                        $"Session={OrderSessionId}) " +
                        $"rezervasyon iptal -> state=Idle"
                    );
                }
            }
        }

        /// <summary>
        /// Geriye dönük uyumluluk için korunan Food-özel sarmalayıcı.
        /// </summary>
        public void ClearFoodReservation(Food food)
        {
            ReleaseDeliveryReservation(food);
        }


        // ============================================================
        // FOOD RECEIVED
        // ============================================================

        public void ReceiveFood()
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"Customer [{name}] (ID={GetInstanceID()}, " +
                    $"Session={OrderSessionId}) " +
                    $"ReceiveFood çağrıldı."
                );
            }

            // Banttaki food müşteriyle eşleştiğinde.
            AudioEvents.PlayOrderDelivered();

            orderBubble.gameObject.SetActive(false);

            SetState(CustomerState.Eating);

            SetState(CustomerState.HappyJump);

            SetState(CustomerState.Leaving);

            if(animator != null)
            {
                animator.SetTrigger("Vanish");
                StartCoroutine(WaitForVanishAndDespawnRoutine());
            }
            else
            {
                Despawn();
            }
        }

        public void OnVanishAnimationComplete()
        {
            Despawn();
        }

        private System.Collections.IEnumerator WaitForVanishAndDespawnRoutine()
        {
            // Wait until next frame so the Animator enters the transition
            yield return null;

            // Get the length of the currently playing/next clip
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
    
            // If it's transitioning to the vanish state, get the next state duration
            if (animator.IsInTransition(0))
            {
                stateInfo = animator.GetNextAnimatorStateInfo(0);
            }

            float clipLength = stateInfo.length > 0f ? stateInfo.length : 0.5f;

            yield return new WaitForSeconds(clipLength);

            Despawn();
        }


        // ============================================================
        // STATE
        // ============================================================

        public void SetState(CustomerState newState)
        {
            if (currentState == newState)
                return;

            currentState = newState;

            ApplyVisual();
        }


        // ============================================================
        // DESPAWN
        // ============================================================

        private void Despawn()
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"Customer [{name}] (ID={GetInstanceID()}, " +
                    $"Session={OrderSessionId}) despawn."
                );
            }

            incomingDeliverySource = null;

            if (initialized && customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
                initialized = false;
            }

            if (ObjectPool.Instance != null)
            {
                ObjectPool.Instance.Return(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }


        // ============================================================
        // VISUAL
        // ============================================================

        private void ApplyVisual()
        {
            if (renderersToFade == null ||
                originalColors == null)
                return;

            float alpha =
                currentState == CustomerState.Blocked
                    ? blockedAlpha
                    : 1f;

            for (int i = 0; i < renderersToFade.Length; i++)
            {
                var r = renderersToFade[i];

                if (r == null)
                    continue;

                r.GetPropertyBlock(mpb);

                Color c = originalColors[i];
                c.a = alpha;

                mpb.SetColor(BaseColorId, c);

                r.SetPropertyBlock(mpb);
            }
        }


        // ============================================================
        // ORIGINAL COLORS
        // ============================================================

        private void CacheOriginalColors()
        {
            originalColors =
                new Color[renderersToFade.Length];

            for (int i = 0;
                i < renderersToFade.Length;
                i++)
            {
                var mat =
                    renderersToFade[i] != null
                        ? renderersToFade[i].sharedMaterial
                        : null;

                originalColors[i] =
                    mat != null &&
                    mat.HasProperty(BaseColorId)
                        ? mat.GetColor(BaseColorId)
                        : Color.white;
            }
        }


        // ============================================================
        // DESTROY
        // ============================================================

        private void OnDestroy()
        {
            if (initialized &&
                customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
            }
        }
    }
}