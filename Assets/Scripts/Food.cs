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
        private Vector3 baseScale;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Surprise Food Settings")]
        [SerializeField] private GameObject surpriseVisual;
        [SerializeField] private float uncoverDuration;

        private bool isSurpriseFood;
        private static readonly int BlockedBaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock mpb;

        private SpriteRenderer twoDVisualRenderer;
        private SpriteRenderer blockedChildRenderer;
        private bool blockedRenderersCached;

        private bool queueStatePreset;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        // Dışarıdan yemeğin sürpriz olup olmadığını kontrol edebilmek için eklendi
        public bool IsSurpriseFood => isSurpriseFood;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;
        public event Action<Food, int> PieceLaunched;

        // Sürpriz açıldığı an dinleyicileri haberdar etmek için eklendi
        public event Action<Food> SurpriseUncovered;

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

        public void SetSurprise(bool surprise)
        {
            isSurpriseFood = surprise;
            if (surpriseVisual != null)
            {
                surpriseVisual.SetActive(isSurpriseFood);
            }
        }

        public void UncoverSurprise()
        {
            if (!isSurpriseFood)
                return;

            isSurpriseFood = false;

            // Sürpriz yemeğin ÖZEL açılma sesi
            AudioEvents.PlaySurpriseFood();

            SurpriseUncovered?.Invoke(this);

            if (surpriseVisual != null && surpriseVisual.activeSelf)
            {
                surpriseVisual.transform.DOKill();
                surpriseVisual.transform.DOScale(Vector3.zero, uncoverDuration)
                    .SetEase(Ease.InBack)
                    .OnComplete(() =>
                    {
                        surpriseVisual.SetActive(false);
                        surpriseVisual.transform.localScale = Vector3.one;
                    });
            }
        }

        private void Awake()
        {
            baseScale = transform.localScale;

            if (spriteVisual == null)
            {
                var sr = GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) spriteVisual = sr.gameObject;
            }

            if (modelVisual == null)
            {
                var mr = GetComponentInChildren<MeshRenderer>(true);
                if (mr != null) modelVisual = mr.gameObject;
            }

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

        private void PlayClickPunch(System.Action onComplete = null)
        {
            if (clickPunchSequence != null && clickPunchSequence.IsActive())
                clickPunchSequence.Kill();

            clickPunchSequence = DOTween.Sequence();
            clickPunchSequence.SetLink(gameObject);
            clickPunchSequence.Append(
                transform.DOScale(baseScale * clickScaleDownFactor, clickScaleDuration).SetEase(Ease.OutQuad));
            clickPunchSequence.Append(
                transform.DOScale(baseScale, clickScaleDuration).SetEase(Ease.OutBack));

            if (onComplete != null)
                clickPunchSequence.OnComplete(() => onComplete());
        }

        public bool CanEnterConveyorFromSlot()
        {
            return trayManager != null && trayManager.CanLaunchTray();
        }

        public bool EnterConveyorFromSlot()
        {
            return TryLaunchPiecesToConveyor();
        }

        public void SetInFoodSlot()
        {
            isSurpriseFood = false;
            if (surpriseVisual != null)
            {
                surpriseVisual.SetActive(false);
            }
            ChangeState(FoodState.InFoodSlot);
        }

        private void UpdateVisualMode()
        {
            bool isStaticInSlotOrQueue = (currentState == FoodState.AvailableInQueue ||
                                          currentState == FoodState.LockedInQueue ||
                                          currentState == FoodState.InFoodSlot);

            if (spriteVisual != null)
                spriteVisual.SetActive(isStaticInSlotOrQueue);

            if (modelVisual != null)
                modelVisual.SetActive(!isStaticInSlotOrQueue && currentState != FoodState.Launching);
        }

        public void ApplyBlockedVisual(bool isBlocked, float darkenFactor = 0.35f)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>(true);
            float factor = isBlocked ? Mathf.Clamp01(darkenFactor) : 1f;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                if (r is SpriteRenderer sr)
                {
                    Color c = sr.color;
                    c.r *= factor; c.g *= factor; c.b *= factor;
                    c.a = 1f;
                    sr.color = c;
                    continue;
                }

                var mat = r.sharedMaterial;
                if (mat == null || !mat.HasProperty(BlockedBaseColorId)) continue;

                r.GetPropertyBlock(mpb);
                Color baseColor = mat.GetColor(BlockedBaseColorId);
                baseColor.r *= factor; baseColor.g *= factor; baseColor.b *= factor;
                baseColor.a = 1f;
                mpb.SetColor(BlockedBaseColorId, baseColor);
                r.SetPropertyBlock(mpb);
            }
        }

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

        private void ApplyCrossfadeInstant(bool isBlocked)
        {
            float targetVisualAlpha = isBlocked ? 0f : 1f;
            float targetBlockedAlpha = isBlocked ? 1f : 0f;

            if (twoDVisualRenderer != null)
            {
                Color c = twoDVisualRenderer.color;
                c.a = targetVisualAlpha;
                twoDVisualRenderer.color = c;
            }

            if (blockedChildRenderer != null)
            {
                Color c = blockedChildRenderer.color;
                c.a = targetBlockedAlpha;
                blockedChildRenderer.color = c;
            }
        }

        private void EnsureBlockedRenderersCached()
        {
            if (blockedRenderersCached) return;

            if (spriteVisual != null && twoDVisualRenderer == null)
                twoDVisualRenderer = spriteVisual.GetComponent<SpriteRenderer>();

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

            bool isBlockedNow = currentState == FoodState.LockedInQueue;
            ApplyCrossfadeInstant(isBlockedNow);
        }

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

            if (spriteVisual != null) spriteVisual.SetActive(true);

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

                piece.transform.DOLocalJump(targetLocalPos, pieceJumpPower, 1, pieceJumpDuration)
                    .SetEase(Ease.OutQuad);

                int remainingAfterThisPiece = Mathf.Max(0, totalCount - (i + 1));
                PieceLaunched?.Invoke(this, remainingAfterThisPiece);

                if (pieceStaggerDelay > 0f)
                    yield return new WaitForSeconds(pieceStaggerDelay);
            }

            if (visualCount < totalCount)
                PieceLaunched?.Invoke(this, 0);

            yield return new WaitForSeconds(pieceJumpDuration);

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