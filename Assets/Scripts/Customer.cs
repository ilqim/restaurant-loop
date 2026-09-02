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

        [Header("Surprise Customer Material (Blocked)")]
        [Tooltip("Sürpriz müşteri Blocked iken materyali değişecek olan Body/Renderer objesini buraya sürükle.")]
        [SerializeField] private Renderer surpriseBodyRenderer;

        [Tooltip("Sürpriz müşteri Blocked iken kullanılacak gizemli (gölge vb.) materyal.")]
        [SerializeField] private Material surpriseBlockedMaterial;
        
        private Material originalSurpriseBodyMaterial;

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

        [Header("Balon Kaybolma Animasyonu (Yemek Ulaştığında)")]
        [Tooltip("Yemek müşteriye ulaştığında (ReceiveFood), balon anında kaybolmak yerine bu süre boyunca küçülüp ÖYLE kaybolur.")]
        [SerializeField] private float bubbleVanishDuration = 0.25f;

        [Header("Surprise Customer (Level Designda İşaretlenir)")]
        [Tooltip("Blocked olduğu sürece (surprise ise) gösterilecek gizem/soru işareti ikonu — istediği yemeğin balonunun ÜSTÜNDE/YERİNDE durup onu gizler. LevelData'daki CustomerEntry.isSurprise değerinden Init() sırasında otomatik açılıp kapanır, elle SetActive etmene gerek yok.")]
        [SerializeField] private GameObject surpriseVisual;
        [Tooltip("Blocked'tan çıkıldığı AN, surpriseVisual'ın küçülüp kaybolarak gerçek balonu ortaya çıkarma süresi — Food.cs'teki uncoverDuration ile aynı mantık.")]
        [SerializeField] private float surpriseUncoverDuration = 0.3f;

        private bool isSurpriseCustomer;
        public bool IsSurpriseCustomer => isSurpriseCustomer;

        private Vector3 surpriseVisualBaseScale = Vector3.one;
        private bool surpriseVisualBaseScaleCached;

        private bool suppressInitialStateAnimations;

        [Header("Göz Kırpma (Blink) — 2 Materyal Arasında Geçiş")]
        [Tooltip("Kafa objesini buraya sürükle — üzerindeki Renderer otomatik bulunur, kırpma sırasında bu Renderer'ın materyali Eyes Open <-> Eyes Closed arasında değiştirilir.")]
        [SerializeField] private Transform headTransform;

        [Tooltip("Gözler AÇIKKEN kullanılacak materyal (normal görünüm).")]
        [SerializeField] private Material eyesOpenMaterial;

        [Tooltip("Gözler KAPALIYKEN (kırpma anında) kullanılacak materyal.")]
        [SerializeField] private Material eyesClosedMaterial;

        [Tooltip("Ortalama kaç saniyede bir göz kırpsın.")]
        [SerializeField] private float blinkInterval = 3f;

        [Tooltip("Aralığın rastgele ne kadar sapabileceği (+/-). Örn. interval=3, variance=1 ise her kırpma 2-4 saniye arasında rastgele olur.")]
        [SerializeField] private float blinkIntervalVariance = 1f;

        [Tooltip("Gözler KAPALI materyaliyle ne kadar süre gösterilsin (saniye) — kısa tut, gerçek bir kırpma anlık olur.")]
        [SerializeField] private float blinkClosedHoldDuration = 0.08f;

        private Renderer headRenderer;
        private Coroutine blinkRoutine;

        [Header("Debug")]
        [SerializeField] private CustomerState currentState = CustomerState.Blocked;
        [SerializeField] private bool verboseLogging = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Vector3 orderBubbleBaseScale = Vector3.one;
        private bool orderBubbleBaseScaleCached;

        private static int nextOrderSessionId = 1;
        public int OrderSessionId { get; private set; }

        private CustomerManager customerManager;
        private MaterialPropertyBlock mpb;
        private Color[] originalColors;
        private bool initialized;

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

        public bool IsReceivingFood => incomingDeliverySource != null;

        private void Awake()
        {
            FindOrderBubble();
            CacheOrderBubbleBaseScale();
            CacheSurpriseVisualBaseScale();

            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = CollectRenderersExcludingBubbles();

            animator = GetComponentInChildren<Animator>();

            if (headTransform != null)
            {
                headRenderer = headTransform.GetComponent<Renderer>();
                if (headRenderer == null)
                    headRenderer = headTransform.GetComponentInChildren<Renderer>();

                if (headRenderer != null && eyesOpenMaterial != null)
                    headRenderer.material = eyesOpenMaterial;
            }
            
            // Sürpriz müşteri için orijinal materyali sakla
            if (surpriseBodyRenderer != null)
            {
                originalSurpriseBodyMaterial = surpriseBodyRenderer.sharedMaterial;
            }

            mpb = new MaterialPropertyBlock();

            if (surpriseVisual != null)
                surpriseVisual.SetActive(false);

            AlignOrderBubbleToCamera();
        }

        private void CacheOrderBubbleBaseScale()
        {
            if (orderBubble == null || orderBubbleBaseScaleCached)
                return;

            orderBubbleBaseScale = orderBubble.localScale;
            orderBubbleBaseScaleCached = true;
        }

        private void CacheSurpriseVisualBaseScale()
        {
            if (surpriseVisual == null || surpriseVisualBaseScaleCached)
                return;

            surpriseVisualBaseScale = surpriseVisual.transform.localScale;
            surpriseVisualBaseScaleCached = true;
        }

        private Renderer[] CollectRenderersExcludingBubbles()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var result = new System.Collections.Generic.List<Renderer>(all.Length);

            foreach (var r in all)
            {
                if (r == null) continue;
                if (orderBubble != null && r.transform.IsChildOf(orderBubble)) continue;
                if (blockedOrderBubble != null && r.transform.IsChildOf(blockedOrderBubble)) continue;
                if (surpriseVisual != null && r.transform.IsChildOf(surpriseVisual.transform)) continue;

                result.Add(r);
            }
            return result.ToArray();
        }

        private void FindOrderBubble()
        {
            if (orderBubble != null)
                return;

            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == "Bubble")
                {
                    orderBubble = child;
                    return;
                }
            }
        }

        public void Init(
            int row,
            int col,
            FoodType desiredFood,
            CustomerManager manager,
            bool isSurprise = false)
        {
            OrderSessionId = nextOrderSessionId++;
            Row = row;
            Col = col;
            DesiredFood = desiredFood;
            customerManager = manager;

            currentState = CustomerState.Blocked;
            incomingDeliverySource = null;

            FindOrderBubble();
            CacheOrderBubbleBaseScale();
            CacheSurpriseVisualBaseScale();
            renderersToFade = CollectRenderersExcludingBubbles();

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            if (orderBubble != null)
            {
                orderBubble.DOKill();
                orderBubble.localScale = orderBubbleBaseScale;
            }

            CacheOriginalColors();
            ApplyVisual();

            SetSurprise(isSurprise);

            suppressInitialStateAnimations = true;
            initialized = true;

            if (customerManager != null)
                customerManager.RegisterCustomer(this);

            suppressInitialStateAnimations = false;

            AlignOrderBubbleToCamera();

            if (animator != null)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                animator.Play(stateInfo.fullPathHash, 0, Random.Range(0f, 1f));
            }

            if (headTransform != null)
            {
                if (headRenderer == null)
                {
                    headRenderer = headTransform.GetComponent<Renderer>();
                    if (headRenderer == null)
                        headRenderer = headTransform.GetComponentInChildren<Renderer>();
                }
                if (headRenderer != null && eyesOpenMaterial != null)
                    headRenderer.material = eyesOpenMaterial;
            }

            if (blinkRoutine != null)
                StopCoroutine(blinkRoutine);

            if (headRenderer != null && eyesOpenMaterial != null && eyesClosedMaterial != null)
                blinkRoutine = StartCoroutine(BlinkRoutine());
        }

        private System.Collections.IEnumerator BlinkRoutine()
        {
            yield return new WaitForSeconds(Random.Range(0f, Mathf.Max(0.01f, blinkInterval)));

            while (true)
            {
                if (headRenderer != null && eyesClosedMaterial != null)
                    headRenderer.material = eyesClosedMaterial;

                yield return new WaitForSeconds(Mathf.Max(0.01f, blinkClosedHoldDuration));

                if (headRenderer != null && eyesOpenMaterial != null)
                    headRenderer.material = eyesOpenMaterial;

                float wait = Mathf.Max(0.05f,
                    blinkInterval + Random.Range(-blinkIntervalVariance, blinkIntervalVariance));

                yield return new WaitForSeconds(wait);
            }
        }

        private void AlignOrderBubbleToCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            Quaternion rot = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

            if (orderBubble != null)
                orderBubble.rotation = rot;
            if (blockedOrderBubble != null)
                blockedOrderBubble.rotation = rot;
            if (surpriseVisual != null)
                surpriseVisual.transform.rotation = rot;
        }

        private void UpdateBubbleVisibility()
        {
            bool isBlocked = currentState == CustomerState.Blocked;
            bool hideRealBubblesForSurprise = isSurpriseCustomer && isBlocked;

            if (blockedOrderBubble != null)
            {
                blockedOrderBubble.gameObject.SetActive(isBlocked && !hideRealBubblesForSurprise);
                if (orderBubble != null)
                    orderBubble.gameObject.SetActive(!isBlocked && !hideRealBubblesForSurprise);
            }
            else if (orderBubble != null)
            {
                orderBubble.gameObject.SetActive(!hideRealBubblesForSurprise);
            }
        }
        
        private void UpdateSurpriseMaterial()
        {
            if (surpriseBodyRenderer == null || originalSurpriseBodyMaterial == null || surpriseBlockedMaterial == null)
                return;

            // 'sharedMaterial' kullanıyoruz ki Unity sürekli yeni instance üretmesin
            if (isSurpriseCustomer && currentState == CustomerState.Blocked)
            {
                surpriseBodyRenderer.sharedMaterial = surpriseBlockedMaterial;
            }
            else
            {
                surpriseBodyRenderer.sharedMaterial = originalSurpriseBodyMaterial;
            }
        }

        private void PlayBubbleUnblockPunch()
        {
            if (orderBubble == null || bubblePunchScale <= 0f)
                return;

            CacheOrderBubbleBaseScale();
            orderBubble.DOKill();
            orderBubble.localScale = orderBubbleBaseScale;
            orderBubble.DOPunchScale(
                orderBubbleBaseScale * bubblePunchScale,
                bubblePunchDuration,
                bubblePunchVibrato,
                bubblePunchElasticity
            );
        }

        public void SetSurprise(bool surprise)
        {
            isSurpriseCustomer = surprise;

            if (surpriseVisual != null)
            {
                CacheSurpriseVisualBaseScale();
                surpriseVisual.transform.DOKill();
                surpriseVisual.transform.localScale = surpriseVisualBaseScale;

                bool shouldShow = isSurpriseCustomer && currentState == CustomerState.Blocked;
                surpriseVisual.SetActive(shouldShow);
            }
            UpdateBubbleVisibility();
            UpdateSurpriseMaterial();
        }

        private void UncoverSurpriseVisual(bool instant = false)
        {
            if (!isSurpriseCustomer || surpriseVisual == null || !surpriseVisual.activeSelf)
                return;

            surpriseVisual.transform.DOKill();

            if (instant)
            {
                surpriseVisual.SetActive(false);
                surpriseVisual.transform.localScale = surpriseVisualBaseScale;
                return;
            }

            // Sürpriz müşterinin ÖZEL açılma sesi 
            AudioEvents.PlaySurpriseCustomer();

            surpriseVisual.transform.localScale = surpriseVisualBaseScale;
            surpriseVisual.transform.DOScale(Vector3.zero, surpriseUncoverDuration)
                .SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    surpriseVisual.SetActive(false);
                    surpriseVisual.transform.localScale = surpriseVisualBaseScale;
                });
        }

        private void VanishBubble(Transform bubble)
        {
            if (bubble == null || !bubble.gameObject.activeSelf)
                return;

            Vector3 startScale = bubble.localScale;
            bubble.DOKill();
            bubble.DOScale(Vector3.zero, bubbleVanishDuration)
                .SetEase(Ease.InBack)
                .OnComplete(() =>
                {
                    bubble.gameObject.SetActive(false);
                    bubble.localScale = startScale;
                });
        }

        public bool TryReserveForDelivery(object source, FoodType requestedFood)
        {
            if (source == null) return false;
            if (incomingDeliverySource != null) return false;
            if (currentState != CustomerState.Idle) return false;
            if (DesiredFood != requestedFood) return false;

            incomingDeliverySource = source;
            SetState(CustomerState.Serving);
            return true;
        }

        public bool TryReserveForFood(Food food)
        {
            if (food == null) return false;
            return TryReserveForDelivery(food, food.FoodTypeValue);
        }

        public void ReleaseDeliveryReservation(object source)
        {
            if (source == null || incomingDeliverySource != source)
                return;

            incomingDeliverySource = null;

            if (currentState == CustomerState.Serving)
            {
                SetState(CustomerState.Idle);
            }
        }

        public void ClearFoodReservation(Food food)
        {
            ReleaseDeliveryReservation(food);
        }

        public void ReceiveFood()
        {
            AudioEvents.PlayOrderDelivered();
            VanishBubble(orderBubble);
            VanishBubble(blockedOrderBubble);

            SetState(CustomerState.Eating);
            SetState(CustomerState.HappyJump);
            SetState(CustomerState.Leaving);
            Despawn();
        }

        public void SetState(CustomerState newState)
        {
            if (currentState == newState)
                return;

            bool wasBlocked = currentState == CustomerState.Blocked;
            currentState = newState;

            ApplyVisual();
            UpdateBubbleVisibility();
            UpdateSurpriseMaterial();

            if (wasBlocked && newState != CustomerState.Blocked)
            {
                UncoverSurpriseVisual(instant: suppressInitialStateAnimations);
                if (!suppressInitialStateAnimations)
                    PlayBubbleUnblockPunch();
            }
            else if (!wasBlocked && newState == CustomerState.Blocked)
            {
                if (isSurpriseCustomer && surpriseVisual != null)
                {
                    surpriseVisual.transform.DOKill();
                    surpriseVisual.transform.localScale = surpriseVisualBaseScale;
                    surpriseVisual.SetActive(true);
                }
            }
        }

        private void Despawn()
        {
            incomingDeliverySource = null;

            if (blinkRoutine != null)
            {
                StopCoroutine(blinkRoutine);
                blinkRoutine = null;
            }

            if (headRenderer != null && eyesOpenMaterial != null)
                headRenderer.material = eyesOpenMaterial;

            // Pool'a dönerken sürpriz materyalini sıfırla ki bir sonraki sefer normal müşteri olarak çıkarsa hatalı görünmesin
            if (surpriseBodyRenderer != null && originalSurpriseBodyMaterial != null)
            {
                surpriseBodyRenderer.sharedMaterial = originalSurpriseBodyMaterial;
                surpriseBodyRenderer.SetPropertyBlock(null); // Özel ayarı temizle
            }

            if (orderBubble != null)
                orderBubble.DOKill();

            if (surpriseVisual != null)
            {
                surpriseVisual.transform.DOKill();
                surpriseVisual.transform.localScale = surpriseVisualBaseScale;
                surpriseVisual.SetActive(false);
            }

            if (initialized && customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
                initialized = false;
            }

            transform.DOKill();
            Vector3 startScale = transform.localScale;
            Sequence vanishSequence = DOTween.Sequence();
            vanishSequence.SetLink(gameObject);

            vanishSequence.Join(transform.DOScale(startScale * 0.2f, 0.35f).SetEase(Ease.InBack));
            vanishSequence.Join(transform.DOMoveY(transform.position.y + 0.4f, 0.35f).SetEase(Ease.OutQuad));

            vanishSequence.OnComplete(() =>
            {
                transform.localScale = startScale;
                if (ObjectPool.Instance != null)
                    ObjectPool.Instance.Return(gameObject);
                else
                    gameObject.SetActive(false);
            });
        }

        private void ApplyVisual()
        {
            if (renderersToFade == null || originalColors == null)
                return;

            float alpha = currentState == CustomerState.Blocked ? blockedAlpha : 1f;
            bool hideOriginalBodyColor = isSurpriseCustomer && currentState == CustomerState.Blocked;

            for (int i = 0; i < renderersToFade.Length; i++)
            {
                var r = renderersToFade[i];
                if (r == null) continue;

                // BUG FIX: Sürpriz Blocked müşteri ise, mpb (MaterialPropertyBlock) ile eski rengi üstüne YAZMA.
                if (hideOriginalBodyColor && r == surpriseBodyRenderer)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }

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

        private void CacheOriginalColors()
        {
            originalColors = new Color[renderersToFade.Length];
            for (int i = 0; i < renderersToFade.Length; i++)
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
                originalColors[i] = mat != null && mat.HasProperty(BaseColorId)
                    ? mat.GetColor(BaseColorId)
                    : Color.white;
            }
        }

        private void OnDestroy()
        {
            if (orderBubble != null)
                orderBubble.DOKill();
            if (surpriseVisual != null)
                surpriseVisual.transform.DOKill();
            if (initialized && customerManager != null)
                customerManager.UnregisterCustomer(this);
        }
    }
}