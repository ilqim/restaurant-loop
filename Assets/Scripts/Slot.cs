using System.Collections;
using UnityEngine;

namespace RestaurantLoop
{
    public enum SlotState
    {
        Empty,
        Occupied
    }

    [RequireComponent(typeof(Collider))]
    public class Slot : MonoBehaviour, IQueueClickable
    {
        [Header("State")]
        [SerializeField] private SlotState currentState = SlotState.Empty;

        [Header("Food")]
        [SerializeField] private Food currentFood;

        [Header("Yerleşim")]
        [Tooltip("Food, slot pozisyonunun ne kadar yukarısına yerleştirilsin (dünya Y ekseni).")]
        [SerializeField] private float foodYOffset = 0.3f;

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

        private Coroutine warningFlashRoutine;

        public SlotState CurrentState => currentState;
        public Food CurrentFood => currentFood;

        public bool IsEmpty => currentState == SlotState.Empty;

        private void Awake()
        {
            countLabel?.SetVisible(false);

            if (warningFlashRenderer != null)
            {
                Color c = warningFlashRenderer.color;
                c.a = warningFlashMinAlpha;
                warningFlashRenderer.color = c;
            }
        }

        private void Update()
        {
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

            food.ReenterConveyorRequested += OnReenterRequested;

            food.SetInFoodSlot();

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
        ///
        /// ÖNEMLİ: countLabel artık food'un GERÇEKTEN konveyöre gidip
        /// gitmediğini (animasyonun bitmesini) BEKLEMİYOR — tıklanır
        /// tıklanmaz HEMEN kapanıyor. Food.EnterConveyorFromSlot() zaten
        /// SENKRON olarak true/false döner (asıl uçuş animasyonu arka
        /// planda ayrı devam eder). Sonuç başarısızsa (konveyör doluysa)
        /// aynı frame içinde countLabel geri açılıyor.
        /// </summary>
        private void OnReenterRequested(Food food)
        {
            food.ReenterConveyorRequested -= OnReenterRequested;

            countLabel?.SetVisible(false);

            bool left = food.EnterConveyorFromSlot();

            if (left)
            {
                RemoveFood();
            }
            else
            {
                if (food.Capacity > 0)
                {
                    countLabel?.SetVisible(true);
                    countLabel?.SetCount(food.Capacity);
                }

                if (verboseFallbackLog)
                {
                    Debug.Log(
                        "Slot: Konveyöre çıkış başarısız, food slotta kaldı. " +
                        "Tekrar tıklanabilir olması için yeniden abone olunuyor."
                    );
                }

                food.ReenterConveyorRequested += OnReenterRequested;
            }
        }

        public void RemoveFood()
        {
            if (currentFood != null)
                currentFood.ReenterConveyorRequested -= OnReenterRequested;

            currentFood = null;
            currentState = SlotState.Empty;

            countLabel?.Clear();
        }

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

        public void PlayWarningFlash()
        {
            if (warningFlashRenderer == null)
                return;

            if (warningFlashRoutine != null)
                StopCoroutine(warningFlashRoutine);

            warningFlashRoutine = StartCoroutine(WarningFlashRoutine());
        }

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