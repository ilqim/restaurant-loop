using System;
using UnityEngine;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;

namespace RestaurantLoop
{
    public enum FoodState
    {
        LockedInQueue,
        AvailableInQueue,
        Launching,
        OnConveyor,
        InFoodSlot,
        Served
    }

    public class Food : MonoBehaviour
    {
        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private TrayManager trayManager;

        [Header("Bu yemeğin türü")]
        [SerializeField] private FoodType foodType;

        [Header("Kapasite")]
        [SerializeField] private int capacity = 10;

        [Header("Görsel Ayrımı (2D/3D)")]
        [Tooltip("Queue ve Slot'tayken aktif olan 2D Sprite/Image objesi.")]
        [SerializeField] private GameObject spriteVisual;
        [Tooltip("Uçuş ve conveyordayken aktif olan 3D objesi.")]
        [SerializeField] private GameObject modelVisual;

        [Header("Blocked/Locked Görsel (Çapraz Solma)")]
        [Tooltip("Blocked (kilitli) durumdayken görünecek SpriteRenderer — artık '2D Visual'ın child'ı OLMAK ZORUNDA DEĞİL, buraya elle sürükle.")]
        [SerializeField] private SpriteRenderer blockedSpriteRenderer;

        [Header("Parça Bazlı Uçuş Animasyonu")]
        [SerializeField] private float pieceJumpDuration = 0.35f;
        [SerializeField] private float pieceJumpPower = 1.2f;
        [SerializeField] private float pieceStaggerDelay = 0.035f;

        [Header("Tıklama Animasyonu (Click Punch)")]
        [Tooltip("Tıklanınca ölçeğin ineceği çarpan (1 = değişmez, 0.85 = %15 küçülür).")]
        [SerializeField] private float clickScaleDownFactor = 0.85f;
        [Tooltip("Küçülme VE büyüme adımlarının HER BİRİNİN süresi (toplam animasyon bunun 2 katı kadar sürer).")]
        [SerializeField] private float clickScaleDuration = 0.08f;

        private Sequence clickPunchSequence;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private static readonly int BlockedBaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock mpb;

        // 2D Visual'ın kendi SpriteRenderer'ı ve onun "Blocked" child'ının
        // SpriteRenderer'ı — Blocked/Available arası ÇAPRAZ SOLMA
        // (crossfade) için cache'leniyor. ApplyBlockedVisual (RGB karartma)
        // farklı bir mekanizma — bu ikisi karıştırılmasın.
        private SpriteRenderer twoDVisualRenderer;
        private SpriteRenderer blockedChildRenderer;
        private bool blockedRenderersCached;

        private bool queueStatePreset;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        /// <summary>
        /// Parça bazlı uçuş animasyonu sırasında HER parça konveyöre
        /// (tray'e) fırlatıldığında tetiklenir. İkinci parametre, o parça
        /// fırlatıldıktan SONRA kalan kapasiteyi verir (ör. capacity=10,
        /// ilk parça fırlayınca 9 gelir). Slot.cs gibi dinleyiciler bunu
        /// kullanarak, animasyon sürerken kapasite etiketini canlı olarak
        /// azaltabilir.
        /// </summary>
        public event Action<Food, int> PieceLaunched;

        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
            UpdateVisualMode();
        }

        public void PresetCapacity(int value)
        {
            capacity = Mathf.Max(0, value);
        }

        private void Awake()
        {
            if(spriteVisual == null)
            {
                var sr = GetComponentInChildren<SpriteRenderer>(true);
                if(sr != null) spriteVisual = sr.gameObject;
            }

            if(modelVisual == null)
            {
                var mr = GetComponentInChildren<MeshRenderer>(true);
                if(mr != null) modelVisual = mr.gameObject;
            }

            // Oyun başlar başlamaz (Blocked mı değil mi kontrol edilerek)
            // alfaları ANINDA doğru değere sabitliyoruz — Editor'de prefab
            // üzerinde bırakılmış olabilecek yanlış varsayılan alfa
            // değerlerine güvenmiyoruz.
            EnsureBlockedRenderersCached();
        }

        private void Start()
        {
            if (trayManager == null) trayManager = FindFirstObjectByType<TrayManager>();
            if (trayManager == null) Debug.LogError("Food: Sahnede bir TrayManager bulunamadı.");

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);

            UpdateVisualMode();
        }

        public void ActivateFromTap()
        {
            if (currentState != FoodState.AvailableInQueue && currentState != FoodState.InFoodSlot)
                return;

            if (currentState == FoodState.AvailableInQueue)
            {
                // Queue'deki food'u banda göndermek için tıklama. Ses SADECE
                // konveyörde gerçekten yer varsa (yemek fiilen çıkabildiyse)
                // çalar — her tıklamada değil. Punch animasyonu ARTIK BURADA
                // DEĞİL, TryLaunchPiecesToConveyor() içinde — çünkü orada
                // parça uçuş animasyonuyla TAM SENKRON başlatılması gerekiyor.
                bool launched = TryLaunchPiecesToConveyor();
                if (launched)
                    AudioEvents.PlayFoodClick();
            }
            else
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");
                ReenterConveyorRequested?.Invoke(this);
            }
        }

        /// <summary>
        /// Tıklanınca çalan "buton hissi" animasyonu — ölçek anında küçülüp
        /// hemen ardından yumuşakça (hafif zıplayarak) orijinal boyutuna
        /// döner. onComplete verilirse, animasyon TAM BİTTİĞİ anda çağrılır
        /// (ör. food'un görselini o an gizlemek için).
        /// </summary>
        private void PlayClickPunch(System.Action onComplete = null)
        {
            // Sadece KENDİ önceki punch sekansımızı öldürüyoruz — transform
            // üzerindeki başka (pozisyon vb.) tween'lere dokunmuyoruz.
            if (clickPunchSequence != null && clickPunchSequence.IsActive())
                clickPunchSequence.Kill();

            Vector3 originalScale = transform.localScale;

            clickPunchSequence = DOTween.Sequence();
            clickPunchSequence.SetLink(gameObject);
            clickPunchSequence.Append(
                transform.DOScale(originalScale * clickScaleDownFactor, clickScaleDuration).SetEase(Ease.OutQuad));
            clickPunchSequence.Append(
                transform.DOScale(originalScale, clickScaleDuration).SetEase(Ease.OutBack));

            if (onComplete != null)
                clickPunchSequence.OnComplete(() => onComplete());
        }

        /// <summary>
        /// Konveyöre GERÇEKTEN çıkmadan, sadece "şu an çıkabilir mi" diye
        /// kontrol eder (hiçbir state değiştirmez, hiçbir animasyon
        /// başlatmaz). Slot.cs, tıklama anında (HandleClick içinde)
        /// countLabel'ı gizleyip gizlememeye bu kontrolle karar veriyor —
        /// böylece "önce gizle, olmazsa tekrar göster" titremesi hiç
        /// yaşanmıyor; sadece GERÇEKTEN gidebilecekse gizleniyor.
        /// </summary>
        public bool CanEnterConveyorFromSlot()
        {
            return trayManager != null && trayManager.CanLaunchTray();
        }

        /// <summary>
        /// Slot, food'u konveyöre geri göndermek istediğinde çağırır.
        /// Dönüş değeri: food GERÇEKTEN konveyöre çıkabildi mi (true) yoksa
        /// konveyör dolu olduğu için olduğu yerde mi kaldı (false).
        /// Slot, bu sonuca göre kendini boşaltıp boşaltmayacağına karar verir.
        /// </summary>
        public bool EnterConveyorFromSlot()
        {
            return TryLaunchPiecesToConveyor();
        }

        public void SetInFoodSlot()
        {
            ChangeState(FoodState.InFoodSlot);
        }

        private void UpdateVisualMode()
        {
            bool isStaticInSlotOrQueue = (currentState == FoodState.AvailableInQueue ||
                                          currentState == FoodState.LockedInQueue ||
                                          currentState == FoodState.InFoodSlot);

            // Kuyrukta ve slotta 2D Sprite göster, fırlatma/uçuş anında ve konveyörde 3D modeli göster
            if (spriteVisual != null)
                spriteVisual.SetActive(isStaticInSlotOrQueue);

            if (modelVisual != null)
                modelVisual.SetActive(!isStaticInSlotOrQueue && currentState != FoodState.Launching);
        }

        /// <summary>
        /// Queue'da locked (henüz sırası gelmemiş) durumdaki görünüm.
        /// ÖNEMLİ: Saydamlık (alpha) DEĞİL, RGB karartma kullanıyor —
        /// materyal Opaque kalmaya devam ediyor, bu yüzden altındaki 2D
        /// queue slot sprite'ıyla derinlik/sıralama çakışması olmuyor.
        /// isBlocked=false ile çağırırsan orijinal renge geri döner.
        /// </summary>
        public void ApplyBlockedVisual(bool isBlocked, float darkenFactor = 0.35f)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>(true);
            float factor = isBlocked ? Mathf.Clamp01(darkenFactor) : 1f;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                // SpriteRenderer varsa (Customer.cs'deki Bubble fix'iyle
                // aynı mantık) direkt .color kullan — _BaseColor sprite
                // shader'ında yok.
                if (r is SpriteRenderer sr)
                {
                    Color c = sr.color;
                    c.r *= factor; c.g *= factor; c.b *= factor;
                    c.a = 1f; // ALFAYA DOKUNMA — Opaque kalmalı
                    sr.color = c;
                    continue;
                }

                var mat = r.sharedMaterial;
                if (mat == null || !mat.HasProperty(BlockedBaseColorId)) continue;

                r.GetPropertyBlock(mpb);
                Color baseColor = mat.GetColor(BlockedBaseColorId);
                baseColor.r *= factor; baseColor.g *= factor; baseColor.b *= factor;
                baseColor.a = 1f; // ALFAYA DOKUNMA — Opaque kalmalı
                mpb.SetColor(BlockedBaseColorId, baseColor);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// "2D Visual" (parent, kendi SpriteRenderer'ı — Available/normal
        /// görünüm) ile onun "Blocked" child'ının SpriteRenderer'ı (Locked
        /// görünüm) arasında ÇAPRAZ SOLMA yapar:
        /// - isBlocked=true  -> Blocked child alfa=1, 2D Visual'ın kendi alfa=0
        /// - isBlocked=false -> 2D Visual'ın kendi alfa=1, Blocked child alfa=0
        ///
        /// duration=0 verilirsen ANINDA (animasyonsuz) uygulanır — ilk
        /// spawn/kurulum için. duration>0 verilirsen DOTween ile o sürede
        /// YUMUŞAKÇA geçiş yapar — QueueManager'daki "öne kayma" (shift)
        /// animasyonuyla AYNI süreyi vererek, pozisyon değişimiyle TAM
        /// SENKRON bir Blocked->Available geçişi elde edilir.
        /// </summary>
        public void SetBlockedCrossfade(bool isBlocked, float duration = 0f)
        {
            EnsureBlockedRenderersCached();

            if (duration <= 0f)
            {
                ApplyCrossfadeInstant(isBlocked);
                return;
            }

            float targetVisualAlpha = isBlocked ? 0f : 1f;
            float targetBlockedAlpha = isBlocked ? 1f : 0f;

            if (twoDVisualRenderer != null)
            {
                twoDVisualRenderer.DOKill();
                twoDVisualRenderer.DOFade(targetVisualAlpha, duration);
            }

            if (blockedChildRenderer != null)
            {
                blockedChildRenderer.DOKill();
                blockedChildRenderer.DOFade(targetBlockedAlpha, duration);
            }
        }

        /// <summary>
        /// Alfayı ANINDA (animasyonsuz) set eder — RGB'ye ASLA dokunmaz,
        /// sadece .color.a değiştirir. Hem ilk kurulum (Awake) hem
        /// SetBlockedCrossfade'in duration=0 dalı bunu kullanır — tek bir
        /// yerden, tutarlı şekilde.
        /// </summary>
        private void ApplyCrossfadeInstant(bool isBlocked)
        {
            float targetVisualAlpha = isBlocked ? 0f : 1f;
            float targetBlockedAlpha = isBlocked ? 1f : 0f;

            if (twoDVisualRenderer != null)
            {
                Color c = twoDVisualRenderer.color; // RGB'ye dokunulmuyor
                c.a = targetVisualAlpha;
                twoDVisualRenderer.color = c;
            }

            if (blockedChildRenderer != null)
            {
                Color c = blockedChildRenderer.color; // RGB'ye dokunulmuyor
                c.a = targetBlockedAlpha;
                blockedChildRenderer.color = c;
            }
        }

        private void EnsureBlockedRenderersCached()
        {
            if (blockedRenderersCached) return;

            if (spriteVisual != null && twoDVisualRenderer == null)
                twoDVisualRenderer = spriteVisual.GetComponent<SpriteRenderer>();

            // ÖNCELİK: Elle sürüklenen blockedSpriteRenderer — artık
            // "2D Visual"ın child'ı olmak ZORUNDA DEĞİL, Inspector'dan
            // doğrudan atanıyor. Boş bırakılırsa (geri uyumluluk için)
            // eski "2D Visual altında 'Blocked' adında bir child ara"
            // yöntemine düşülür.
            if (blockedChildRenderer == null)
            {
                if (blockedSpriteRenderer != null)
                {
                    blockedChildRenderer = blockedSpriteRenderer;
                }
                else if (spriteVisual != null)
                {
                    Transform blockedT = spriteVisual.transform.Find("Blocked");
                    if (blockedT != null)
                        blockedChildRenderer = blockedT.GetComponent<SpriteRenderer>();
                    else
                        Debug.LogWarning($"Food [{gameObject.name}]: 'Blocked Sprite Renderer' atanmamış ve '2D Visual' altında 'Blocked' adında bir child da bulunamadı.", this);
                }
            }

            blockedRenderersCached = true;

            // OYUN BAŞLAR BAŞLAMAZ: o anki gerçek state Blocked mi (LockedInQueue)
            // değil mi kontrol edilip, alfalar ANINDA (animasyonsuz) doğru
            // değere sabitleniyor — Editor'de prefab üzerinde bırakılmış
            // olabilecek YANLIŞ varsayılan alfa değerlerine (ör. ikisi de
            // 255/tam opak) güvenmiyoruz.
            bool isBlockedNow = currentState == FoodState.LockedInQueue;
            ApplyCrossfadeInstant(isBlockedNow);
        }

        /// <summary>
        /// Konveyöre (tray olarak) çıkışı dener. Başarılıysa food'u despawn eder
        /// ve true döner. Konveyör doluysa hiçbir şey değiştirmeden false döner.
        /// </summary>
        private bool TryLaunchPiecesToConveyor()
        {
            if (trayManager == null)
            {
                Debug.LogError("Food: TrayManager yok, tray başlatılamıyor.");
                return false;
            }

            if (!trayManager.CanLaunchTray())
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Konveyör dolu, tray başlatılamadı.");
                // Launch başarısız olsa bile tıklamanın "hissedilmesi" için
                // punch animasyonu yine de oynasın (food görselini gizlemeden
                // — hiçbir şey gerçekten olmadı, olduğu yerde kalıyor).
                PlayClickPunch();
                return false;
            }

            Tray upcomingTray = trayManager.PrepareUpcomingTray();
            if (upcomingTray == null)
            {
                PlayClickPunch();
                return false;
            }

            ChangeState(FoodState.Launching);

            // ÖNEMLİ FIX: ChangeState() kendi içinde UpdateVisualMode()'u
            // çağırıyor ve bu, Launching state'i için spriteVisual'ı HEMEN
            // (punch animasyonu daha başlamadan) kapatıyordu — bu yüzden
            // Queue'da punch hiç GÖRÜNMÜYORDU, food anında kayboluyormuş
            // gibi görünüyordu. Burada spriteVisual'ı ELLE tekrar açıyoruz;
            // gerçek gizleme işini aşağıdaki PlayClickPunch'ın OnComplete'i
            // yapacak — böylece Slot'takiyle birebir aynı davranış.
            if (spriteVisual != null) spriteVisual.SetActive(true);

            // Punch animasyonu (küçülüp büyüme) İLE parça uçuş animasyonu
            // AYNI ANDA başlıyor. Food'un kendi görseli punch TAM BİTENE
            // kadar görünür kalıyor, sonra (OnComplete) gizleniyor.
            PlayClickPunch(() =>
            {
                if (spriteVisual != null) spriteVisual.SetActive(false);
                if (modelVisual != null) modelVisual.SetActive(false);
            });

            StartCoroutine(AnimatePiecesToTrayRoutine(upcomingTray));

            return true;
        }

        private IEnumerator AnimatePiecesToTrayRoutine(Tray tray)
        {
            var config = trayManager.GetVisualConfig(foodType);
            GameObject piecePrefab = config.stackPiecePrefab;

            int totalCount = capacity;
            int visualCount = Mathf.Min(capacity, Mathf.Max(0, config.maxVisualPieces));
            int piecesPerLayer = 4;
            float half = config.pieceSpacing * 0.5f;

            Vector3 spawnOrigin = transform.position;
            Transform trayModelTransform = tray.ModelTransform;

            List<GameObject> spawnedPieces = new();

            for (int i = 0; i < visualCount; i++)
            {
                int layer = i / piecesPerLayer;
                int posInLayer = i % piecesPerLayer;

                float xOffset = (posInLayer == 0 || posInLayer == 2) ? -half : half;
                float zOffset = (posInLayer == 0 || posInLayer == 1) ? half : -half;
                float yOffset = config.foodBaseYOffset + layer * config.pieceHeightSpacing;

                Vector3 targetLocalPos = new Vector3(xOffset, yOffset, zOffset);

                GameObject piece = (ObjectPool.Instance != null && piecePrefab != null)
                    ? ObjectPool.Instance.Get(piecePrefab, spawnOrigin, piecePrefab.transform.rotation, trayModelTransform)
                    : (piecePrefab != null ? Instantiate(piecePrefab, spawnOrigin, piecePrefab.transform.rotation, trayModelTransform) : new GameObject("Piece"));

                piece.transform.position = spawnOrigin;
                spawnedPieces.Add(piece);

                // DOJump directly to the target local point relative to the tray model
                piece.transform.DOLocalJump(targetLocalPos, pieceJumpPower, 1, pieceJumpDuration)
                    .SetEase(Ease.OutQuad);

                // Bu parça "fırlatıldı" — dinleyicilere (ör. Slot.cs'in
                // kapasite etiketi) kalan miktarı bildiriyoruz.
                int remainingAfterThisPiece = Mathf.Max(0, totalCount - (i + 1));
                PieceLaunched?.Invoke(this, remainingAfterThisPiece);

                if (pieceStaggerDelay > 0f)
                    yield return new WaitForSeconds(pieceStaggerDelay);
            }

            // Eğer visualCount, totalCount'tan azsa (maxVisualPieces sınırı
            // yüzünden), kalan miktarı en sonda 0'a tamamlayarak son bir
            // bildirim daha yapıyoruz — dinleyici kesin olarak 0'da bitsin.
            if (visualCount < totalCount)
                PieceLaunched?.Invoke(this, 0);

            // Wait for the final piece to complete its jump
            yield return new WaitForSeconds(pieceJumpDuration);

            // Finalize Tray launch
            trayManager.FinalizeTrayLaunch(tray, foodType, totalCount, spawnedPieces);

            ChangeState(FoodState.OnConveyor);
            DespawnSelf();
        }

        private void DespawnSelf()
        {
            var pooled = GetComponent<PooledObject>();
            if (pooled != null && pooled.SourcePrefab != null && ObjectPool.Instance != null)
                ObjectPool.Instance.Return(gameObject);
            else
                Destroy(gameObject);
        }

        private void ChangeState(FoodState newState)
        {
            currentState = newState;
            UpdateVisualMode();
            if (verboseLogging) Debug.Log($"Food [{gameObject.name}] State: {currentState}");
            StateChanged?.Invoke(this, newState);
        }
    }
}