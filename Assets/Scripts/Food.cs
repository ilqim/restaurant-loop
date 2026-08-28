using System;
using UnityEngine;
using DG.Tweening;

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

        [Header("Zıplama Animasyonu")]
        [SerializeField] private float jumpDuration = 0.35f;
        [SerializeField] private float jumpPower = 1.2f;

        [Header("Kapasite Debug Etiketi")]
        [SerializeField] private bool showCapacityLabel = true;
        [SerializeField] private float labelHeight = 0.6f;
        [SerializeField] private int labelFontSize = 48;
        [SerializeField] private float labelCharacterSize = 0.12f;
        [SerializeField] private Color labelColor = Color.white;

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
        private TextMesh capacityLabel;
        private Camera labelFacingCamera;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
            UpdateVisualMode();
        }

        public void PresetCapacity(int value)
        {
            capacity = Mathf.Max(0, value);
            UpdateCapacityLabel();
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

            if (labelFacingCamera == null) labelFacingCamera = Camera.main;

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);

            UpdateVisualMode();
            CreateCapacityLabel();
        }

        private void LateUpdate()
        {
            if (capacityLabel == null) return;
            if (labelFacingCamera == null) labelFacingCamera = Camera.main;
            if (labelFacingCamera == null) return;
            capacityLabel.transform.rotation = Quaternion.LookRotation(capacityLabel.transform.position - labelFacingCamera.transform.position);
        }

        public void ActivateFromTap()
        {
            if (currentState != FoodState.AvailableInQueue && currentState != FoodState.InFoodSlot)
                return;

            if (currentState == FoodState.AvailableInQueue)
            {
                // Queue'deki food'u banda göndermek için tıklama. Ses SADECE
                // konveyörde gerçekten yer varsa (yemek fiilen çıkabildiyse)
                // çalar — her tıklamada değil.
                bool launched = TryLaunchWithAnimation();
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
        /// Slot, food'u konveyöre geri göndermek istediğinde çağırır.
        /// Dönüş değeri: food GERÇEKTEN konveyöre çıkabildi mi (true) yoksa
        /// konveyör dolu olduğu için olduğu yerde mi kaldı (false).
        /// Slot, bu sonuca göre kendini boşaltıp boşaltmayacağına karar verir.
        /// </summary>
        public bool EnterConveyorFromSlot()
        {
            return TryLaunchWithAnimation();
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
                modelVisual.SetActive(!isStaticInSlotOrQueue);
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

            // TEŞHİS — sorun bulununca bu satırı sil.
            Debug.Log($"[TEŞHİS] {gameObject.name} SetBlockedCrossfade(isBlocked={isBlocked}, duration={duration}) — " +
                      $"twoDVisualRenderer null mu={twoDVisualRenderer == null}, blockedChildRenderer null mu={blockedChildRenderer == null}", this);

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
                twoDVisualRenderer.DOFade(targetVisualAlpha, duration).OnUpdate(() =>
                {
                    Debug.Log($"[TEŞHİS] {gameObject.name} twoDVisualRenderer.color.a ŞU AN = {twoDVisualRenderer.color.a}", this);
                });
            }

            if (blockedChildRenderer != null)
            {
                blockedChildRenderer.DOKill();
                blockedChildRenderer.DOFade(targetBlockedAlpha, duration).OnUpdate(() =>
                {
                    Debug.Log($"[TEŞHİS] {gameObject.name} blockedChildRenderer.color.a ŞU AN = {blockedChildRenderer.color.a}", this);
                });
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

                // TEŞHİS — sorun bulununca bu satırı sil.
                Debug.Log($"[TEŞHİS] {gameObject.name} ApplyCrossfadeInstant: twoDVisualRenderer.color SONRASI = {twoDVisualRenderer.color}", this);
            }

            if (blockedChildRenderer != null)
            {
                Color c = blockedChildRenderer.color; // RGB'ye dokunulmuyor
                c.a = targetBlockedAlpha;
                blockedChildRenderer.color = c;

                // TEŞHİS — sorun bulununca bu satırı sil.
                Debug.Log($"[TEŞHİS] {gameObject.name} ApplyCrossfadeInstant: blockedChildRenderer.color SONRASI = {blockedChildRenderer.color}", this);
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
        private bool TryLaunchWithAnimation()
        {
            if (trayManager == null)
            {
                Debug.LogError("Food: TrayManager yok, tray başlatılamıyor.");
                return false;
            }

            if (!trayManager.CanLaunchTray())
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Konveyör dolu, tray başlatılamadı.");
                return false;
            }

            Vector3 targetPos = trayManager.GetWaypointPosition(0);

            ChangeState(FoodState.Launching);

            UpdateVisualMode();

            // Smooth DOTween jump to the conveyor starting position
            transform.DOJump(targetPos, jumpPower, 1, jumpDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    bool launched = trayManager.TryLaunchTray(foodType, capacity);
                    if (launched)
                    {
                        ChangeState(FoodState.OnConveyor);
                        DespawnSelf();
                    }
                    else
                    {
                        ChangeState(FoodState.AvailableInQueue);
                    }
                });

            return true;
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

        private void CreateCapacityLabel()
        {
            if (!showCapacityLabel) return;
            if (capacityLabel != null) return;

            var labelGO = new GameObject("CapacityLabel");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0, labelHeight, 0);

            capacityLabel = labelGO.AddComponent<TextMesh>();
            capacityLabel.anchor = TextAnchor.MiddleCenter;
            capacityLabel.alignment = TextAlignment.Center;
            capacityLabel.fontSize = labelFontSize;
            capacityLabel.characterSize = labelCharacterSize;
            capacityLabel.color = labelColor;

            UpdateCapacityLabel();
        }

        private void UpdateCapacityLabel()
        {
            if (capacityLabel != null) capacityLabel.text = capacity.ToString();
        }
    }
}