using System.Collections;
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
        [Tooltip("Slot doluyken gösterilecek TEK sprite — tüm food'lar için aynı sprite/renk kullanılır.")]
        [SerializeField] private Sprite occupiedSprite;

        [Header("Debug")]
        [SerializeField] private bool verboseFallbackLog = true;

        [Header("Sayı Etiketi")]
        [Tooltip("İçindeki food'un kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi).")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

        [Header("Uyarı Yanıp Sönme (tüm slotlar dolu olunca)")]
        [Tooltip("Yanıp sönecek 2D obje — sen bir child olarak ekleyip buraya sürükleyeceksin (SpriteRenderer taşımalı).")]
        [SerializeField] private SpriteRenderer warningFlashRenderer;

        [Tooltip("Kaç kez yanıp sönsün (min->max->min bir kez sayılır).")]
        [SerializeField] private int warningFlashCount = 3;

        [Tooltip("Alfa'nın ineceği minimum değer (0-1). Örn. 0 = tamamen görünmez.")]
        [Range(0f, 1f)]
        [SerializeField] private float warningFlashMinAlpha = 0f;

        [Tooltip("Alfa'nın çıkacağı maksimum değer (0-1). Örn. 0.6 = %60 opak.")]
        [Range(0f, 1f)]
        [SerializeField] private float warningFlashMaxAlpha = 0.6f;

        [Tooltip("Min'den max'a (ya da tersi) geçişin süresi (saniye) — küçültürsen hızlanır, büyütürsen yavaşlar. Bir tam yanıp-sönme (min->max->min) bunun 2 katı sürer.")]
        [SerializeField] private float warningFlashHalfCycleDuration = 0.15f;

        private SpriteRenderer spriteRenderer;
        private Sprite defaultSprite;
        private Color defaultColor;
        private Coroutine warningFlashRoutine;

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

            // Uyarı objesi de başta görünmez (min alfa) olsun.
            if (warningFlashRenderer != null)
            {
                Color c = warningFlashRenderer.color;
                c.a = warningFlashMinAlpha;
                warningFlashRenderer.color = c;
            }
        }

        private void Update()
        {
            // GÜVENLİK: Normal akışta slot, OnReenterRequested üzerinden
            // RemoveFood() çağrılarak boşalır. Ama food herhangi bir sebeple
            // (örn. Tray/pool tarafında beklenmedik bir Destroy, ya da event
            // zincirinin atlandığı bir edge-case) bu akışı hiç tetiklemeden
            // yok olursa, slot bunu fark edemez ve Occupied/eski sprite'ta
            // takılı kalır.
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
            food.ReenterConveyorRequested += OnReenterRequested;

            food.SetInFoodSlot();

            // FOOD VARKEN SLOT'UN KENDİ SPRITE'INI GİZLE.
            spriteRenderer.enabled = false;

            // Sprite yine atanıyor, fakat renderer kapalı olduğu için
            // ekranda görünmüyor. Food çıktığında default sprite'a dönüyor.
            spriteRenderer.sprite = occupiedSprite != null
                ? occupiedSprite
                : defaultSprite;

            // Capacity 0 (ya da altı) ise içinde gösterilecek bir sayı yok.
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
        /// boşalır.
        /// </summary>
        private void OnReenterRequested(Food food)
        {
            food.ReenterConveyorRequested -= OnReenterRequested;

            bool left = food.EnterConveyorFromSlot();

            if (left)
            {
                RemoveFood();
            }
            else
            {
                if (verboseFallbackLog)
                {
                    Debug.Log(
                        "Slot: Konveyöre çıkış başarısız, food slotta kaldı. " +
                        "Tekrar tıklanabilir olması için yeniden abone olunuyor."
                    );
                }

                // Tekrar tıklanabilsin diye event'e yeniden abone ol.
                food.ReenterConveyorRequested += OnReenterRequested;
            }
        }

        public void RemoveFood()
        {
            if (currentFood != null)
                currentFood.ReenterConveyorRequested -= OnReenterRequested;

            currentFood = null;
            currentState = SlotState.Empty;

            // FOOD GİTTİ → SLOT'UN SPRITE'INI TEKRAR GÖSTER.
            spriteRenderer.enabled = true;

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
            if (IsEmpty)
                return;

            CurrentFood?.ActivateFromTap();
        }

        // ============================================================
        // UYARI YANIP SÖNME — tüm slotlar dolu olduğunda SlotManager
        // TÜM slotlara bunu çağırır.
        // ============================================================

        /// <summary>
        /// warningFlashRenderer'ın alfasını warningFlashMinAlpha ile
        /// warningFlashMaxAlpha arasında warningFlashCount kez yanıp
        /// söndürür.
        /// </summary>
        public void PlayWarningFlash()
        {
            if (warningFlashRenderer == null)
                return;

            if (warningFlashRoutine != null)
                StopCoroutine(warningFlashRoutine);

            warningFlashRoutine = StartCoroutine(WarningFlashRoutine());
        }

        /// <summary>
        /// Halihazırda oynayan yanıp-sönmeyi durdurup min alfaya sıfırlar.
        /// </summary>
        public void StopWarningFlash()
        {
            if (warningFlashRoutine != null)
            {
                StopCoroutine(warningFlashRoutine);
                warningFlashRoutine = null;
            }

            if (warningFlashRenderer != null)
            {
                Color c = warningFlashRenderer.color;
                c.a = warningFlashMinAlpha;
                warningFlashRenderer.color = c;
            }
        }

        private IEnumerator WarningFlashRoutine()
        {
            Color baseColor = warningFlashRenderer.color;

            for (int i = 0; i < Mathf.Max(1, warningFlashCount); i++)
            {
                yield return FlashLerp(
                    warningFlashMinAlpha,
                    warningFlashMaxAlpha,
                    warningFlashHalfCycleDuration,
                    baseColor
                );

                yield return FlashLerp(
                    warningFlashMaxAlpha,
                    warningFlashMinAlpha,
                    warningFlashHalfCycleDuration,
                    baseColor
                );
            }

            // Bitince min alfada bırak.
            Color finalColor = baseColor;
            finalColor.a = warningFlashMinAlpha;
            warningFlashRenderer.color = finalColor;

            warningFlashRoutine = null;
        }

        private IEnumerator FlashLerp(
            float fromAlpha,
            float toAlpha,
            float duration,
            Color baseColor)
        {
            duration = Mathf.Max(0.01f, duration);

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);

                Color c = baseColor;
                c.a = Mathf.Lerp(fromAlpha, toAlpha, t);

                warningFlashRenderer.color = c;

                yield return null;
            }

            Color endColor = baseColor;
            endColor.a = toAlpha;
            warningFlashRenderer.color = endColor;
        }
    }
}