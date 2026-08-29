using DG.Tweening;
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
        [Header("Görsel — Blocked iken saydamlaşacak renderer'lar (BUBBLE HARİÇ — Bubble artık ayrı yönetiliyor, aşağıya bak)")]
        [Tooltip("Boş bırakılırsa Awake'te GetComponentsInChildren<Renderer> ile otomatik doldurulur (Bubble'lar hariç tutulur).")]
        [SerializeField] private Renderer[] renderersToFade;

        [Range(0f, 1f)]
        [SerializeField] private float blockedAlpha = 0.35f;

        [Header("Customer Order Bubble")]
        [Tooltip("Normal (Blocked OLMAYAN) durumda gösterilen balon. Prefab içindeki adı 'Bubble' olan child obje otomatik bulunur.")]
        [SerializeField] private Transform orderBubble;

        [Tooltip("Blocked durumundayken orderBubble YERİNE gösterilecek ayrı bir balon objesi — ELLE ata (Inspector'dan sürükle). " +
                 "Boş bırakılırsa eski davranışa (Blocked'ta hiç balon gizlenmez, sadece orderBubble kalır) düşülür.")]
        [SerializeField] private Transform blockedOrderBubble;

        [Header("Blocked -> Normal Balon Zıplama (Punch Scale)")]
        [Tooltip("Blocked'tan Blocked-olmayan bir state'e geçildiği ANDA, orderBubble bu kadar 'zıplasın' (0 = hiç zıplama olmaz).")]
        [SerializeField] private float bubblePunchScale = 0.15f;

        [Tooltip("Zıplamanın toplam süresi (saniye) — bu süre sonunda otomatik söner, loop YOK.")]
        [SerializeField] private float bubblePunchDuration = 0.4f;

        [Tooltip("Kaç kez sallansın (titreşim sayısı) — büyütürsen daha 'titrek', küçültürsen daha yumuşak zıplar.")]
        [SerializeField] private int bubblePunchVibrato = 8;

        [Tooltip("Sekme/esneklik miktarı (0=hiç geri sekmez, 1=çok belirgin sekme).")]
        [Range(0f, 1f)]
        [SerializeField] private float bubblePunchElasticity = 0.7f;

        [Header("Debug")]
        [SerializeField] private CustomerState currentState = CustomerState.Blocked;
        [SerializeField] private bool verboseLogging = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Bubble'ın prefab üzerindeki gerçek scale değerini saklar.
        // Örneğin Inspector'da 0.4 ise burada 0.4 olarak tutulur.
        private Vector3 orderBubbleBaseScale = Vector3.one;
        private bool orderBubbleBaseScaleCached;

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
            // Prefab içindeki "Bubble" child'ını otomatik bul (blockedOrderBubble
            // için otomatik arama YOK — onu sen elle atıyorsun, farklı isimde/
            // yapıda olabileceği için otomatik bulmaya çalışmıyoruz).
            FindOrderBubble();

            // Bubble'ın prefab üzerindeki GERÇEK scale değerini cache'le.
            CacheOrderBubbleBaseScale();

            // Renderer'ları otomatik bul — BUBBLE'LARI (orderBubble ve
            // blockedOrderBubble) HARİÇ TUTARAK. Onlar artık alfa/karartma
            // sistemine hiç girmiyor, sadece aktif/pasif ediliyorlar.
            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = CollectRenderersExcludingBubbles();

            animator = GetComponentInChildren<Animator>();

            // MaterialPropertyBlock hazırla.
            mpb = new MaterialPropertyBlock();

            // Bubble'ları instantiate edilir edilmez kameraya hizala.
            AlignOrderBubbleToCamera();
        }

        /// <summary>
        /// Bubble'ın prefab üzerinde ayarlanmış gerçek localScale değerini
        /// bir kere saklar.
        /// </summary>
        private void CacheOrderBubbleBaseScale()
        {
            if (orderBubble == null)
                return;

            if (orderBubbleBaseScaleCached)
                return;

            orderBubbleBaseScale = orderBubble.localScale;
            orderBubbleBaseScaleCached = true;
        }

        /// <summary>
        /// GetComponentsInChildren&lt;Renderer&gt; ile bulunan tüm
        /// renderer'lardan, orderBubble ve blockedOrderBubble'ın ALTINDA
        /// kalanları çıkarır. Bu iki balon artık alfa ile soluklaştırılmıyor,
        /// tamamen ayrı bir GameObject aktif/pasif mantığıyla yönetiliyor.
        /// </summary>
        private Renderer[] CollectRenderersExcludingBubbles()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var result = new System.Collections.Generic.List<Renderer>(all.Length);

            foreach (var r in all)
            {
                if (r == null)
                    continue;

                if (orderBubble != null && r.transform.IsChildOf(orderBubble))
                    continue;

                if (blockedOrderBubble != null && r.transform.IsChildOf(blockedOrderBubble))
                    continue;

                result.Add(r);
            }

            return result.ToArray();
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

            // Bubble'ın gerçek scale'ini garantiye al.
            CacheOrderBubbleBaseScale();

            // Food/orderBubble tekrar bulunmuş olabileceği için renderer listesini
            // de tazeleyelim ki filtre (bubble hariç tutma) hep doğru kalsın.
            renderersToFade = CollectRenderersExcludingBubbles();

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            // Pool'dan gelen objede önceki bir punch-scale'den kalma
            // yarım kalmış bir ölçek olmasın diye GERÇEK BASE SCALE'e
            // sıfırlıyoruz.
            if (orderBubble != null)
            {
                orderBubble.DOKill();
                orderBubble.localScale = orderBubbleBaseScale;
            }

            CacheOriginalColors();
            ApplyVisual();
            UpdateBubbleVisibility();

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
            Camera cam = Camera.main;

            if (cam == null)
                return;

            Quaternion rot =
                Quaternion.LookRotation(
                    cam.transform.forward,
                    cam.transform.up
                );

            // İkisi de aynı açıya hizalanıyor — hangisi aktifse o görünür.
            if (orderBubble != null)
                orderBubble.rotation = rot;

            if (blockedOrderBubble != null)
                blockedOrderBubble.rotation = rot;
        }


        // ============================================================
        // BUBBLE AKTİF/PASİF (Blocked <-> Normal geçişi)
        // ============================================================

        private void UpdateBubbleVisibility()
        {
            bool isBlocked = currentState == CustomerState.Blocked;

            if (blockedOrderBubble != null)
            {
                blockedOrderBubble.gameObject.SetActive(isBlocked);

                if (orderBubble != null)
                    orderBubble.gameObject.SetActive(!isBlocked);
            }
            else if (orderBubble != null)
            {
                // blockedOrderBubble hiç atanmamışsa eski davranış:
                // balon her zaman aktif kalır.
                orderBubble.gameObject.SetActive(true);
            }
        }


        // ============================================================
        // BUBBLE PUNCH
        // ============================================================

        private void PlayBubbleUnblockPunch()
        {
            if (orderBubble == null)
                return;

            if (bubblePunchScale <= 0f)
                return;

            // Bubble'ın gerçek scale'ini garantiye al.
            CacheOrderBubbleBaseScale();

            // Önceki punch hâlâ sürüyorsa iptal edip GERÇEK base scale'e
            // dön.
            orderBubble.DOKill();
            orderBubble.localScale = orderBubbleBaseScale;

            // Vector3.one yerine base scale kullanıyoruz.
            //
            // Örneğin Bubble scale = 0.4 ise:
            // 0.4 -> punch -> tekrar 0.4
            //
            // Artık 0.4 -> 1.0 şeklinde büyümez.
            orderBubble.DOPunchScale(
                orderBubbleBaseScale * bubblePunchScale,
                bubblePunchDuration,
                bubblePunchVibrato,
                bubblePunchElasticity
            );
        }


        // ============================================================
        // TESLİMAT REZERVASYONU (Food VE Tray ORTAK)
        // ============================================================

        public bool TryReserveForDelivery(
            object source,
            FoodType requestedFood)
        {
            if (source == null)
                return false;

            // Zaten başka bir kaynak bu müşteriye yemek gönderiyor.
            if (incomingDeliverySource != null)
                return false;

            // Müşteri sadece Idle iken rezerve edilebilir.
            if (currentState != CustomerState.Idle)
                return false;

            // İstenen yemek ile gönderilen yemek eşleşmeli.
            if (DesiredFood != requestedFood)
                return false;

            // Müşteriyi bu kaynak için kilitle.
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
        /// </summary>
        public bool TryReserveForFood(Food food)
        {
            if (food == null)
                return false;

            return TryReserveForDelivery(
                food,
                food.FoodTypeValue
            );
        }

        /// <summary>
        /// Teslimat kaynağı rezervasyonu bırakır.
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
                    $"Session={OrderSessionId}) ReceiveFood çağrıldı."
                );
            }

            // Yemek müşteriye ulaştığı ANDA teslim sesi
            AudioEvents.PlayOrderDelivered();

            // Teslimat rezervasyonunu temizle
            incomingDeliverySource = null;

            // FOOD GELDİĞİ ANDA BUBBLE PUNCH
            PlayBubbleUnblockPunch();

            // Bubble punch devam ederken müşteri yok olma animasyonuna girsin.
            Despawn();
        }

        private System.Collections.IEnumerator WaitForVanishAndDespawnRoutine()
        {
            // Wait until next frame so the Animator enters the transition.
            yield return null;

            AnimatorStateInfo stateInfo =
                animator.GetCurrentAnimatorStateInfo(0);

            // If it's transitioning to the vanish state, get the next state duration.
            if (animator.IsInTransition(0))
            {
                stateInfo =
                    animator.GetNextAnimatorStateInfo(0);
            }

            float clipLength =
                stateInfo.length > 0f
                    ? stateInfo.length
                    : 0.5f;

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

            bool wasBlocked =
                currentState == CustomerState.Blocked;

            currentState = newState;

            ApplyVisual();
            UpdateBubbleVisibility();

            // Blocked'tan normale geçerken bubble punch.
            if (wasBlocked &&
                newState != CustomerState.Blocked)
            {
                PlayBubbleUnblockPunch();
            }
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

            if (orderBubble != null)
                orderBubble.DOKill();

            if (initialized && customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
                initialized = false;
            }

            // Önceki animasyonları temizle
            transform.DOKill();

            // Başlangıç scale'ini sakla
            Vector3 startScale = transform.localScale;

            // Yok olma animasyonu:
            // - Hafif yukarı çıkar
            // - Küçülür
            // - Sonunda pool'a döner
            Sequence vanishSequence = DOTween.Sequence();

            vanishSequence.Join(
                transform.DOScale(
                    startScale * 0.2f,
                    0.35f
                ).SetEase(Ease.InBack)
            );

            vanishSequence.Join(
                transform.DOMoveY(
                    transform.position.y + 0.4f,
                    0.35f
                ).SetEase(Ease.OutQuad)
            );

            vanishSequence.OnComplete(() =>
            {
                // Pool'a dönmeden önce scale'i eski haline getir
                transform.localScale = startScale;

                if (ObjectPool.Instance != null)
                {
                    ObjectPool.Instance.Return(gameObject);
                }
                else
                {
                    gameObject.SetActive(false);
                }
            });
        }


        // ============================================================
        // VISUAL
        // ============================================================

        /// <summary>
        /// Artık SADECE body/kafa gibi Bubble-dışı renderer'ları etkiler —
        /// Bubble'lar UpdateBubbleVisibility() ile ayrı yönetiliyor.
        /// </summary>
        private void ApplyVisual()
        {
            if (renderersToFade == null ||
                originalColors == null)
                return;

            float alpha =
                currentState == CustomerState.Blocked
                    ? blockedAlpha
                    : 1f;

            for (int i = 0;
                i < renderersToFade.Length;
                i++)
            {
                var r = renderersToFade[i];

                if (r == null)
                    continue;

                Color c = originalColors[i];
                c.a = alpha;

                if (r is SpriteRenderer sr)
                {
                    sr.color = c;
                    continue;
                }

                r.GetPropertyBlock(mpb);
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
                var r = renderersToFade[i];

                if (r == null)
                {
                    originalColors[i] = Color.white;
                    continue;
                }

                if (r is SpriteRenderer sr)
                {
                    originalColors[i] = sr.color;
                    continue;
                }

                var mat = r.sharedMaterial;

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
            if (orderBubble != null)
                orderBubble.DOKill();

            if (initialized &&
                customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
            }
        }
    }
}