using DG.Tweening;
using UnityEngine;

namespace RestaurantLoop
{
    public interface IQueueClickable
    {
        void HandleClick();
    }

    [RequireComponent(typeof(Collider))]
    public class QueueSlot : MonoBehaviour, IQueueClickable
    {
        private Food assignedFood;

        [Header("Sayı Etiketi")]
        [Tooltip("İçindeki food'un kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi).")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

        [Header("Tıklama Animasyonu (Click Punch) — Food ile SENKRON")]
        [Tooltip("Tıklanınca ölçeğin ineceği çarpan — Food.cs'teki değerle AYNI olmalı ki ikisi birebir senkron görünsün.")]
        [SerializeField] private float clickScaleDownFactor = 0.85f;
        [Tooltip("Küçülme VE büyüme adımlarının HER BİRİNİN süresi — Food.cs'teki değerle AYNI olmalı.")]
        [SerializeField] private float clickScaleDuration = 0.08f;

        private Sequence clickPunchSequence;
        private Vector3 baseCountLabelScale;
        private bool baseCountLabelScaleCached;

        public Food AssignedFood => assignedFood;

        public void AssignFood(Food food)
        {
            if (assignedFood != null)
            {
                assignedFood.SurpriseUncovered -= OnFoodSurpriseUncovered;
            }

            assignedFood = food;

            if (food != null)
            {
                // Food'un sürprizi kalktığında bizi haberdar etmesi için event dinleniyor
                food.SurpriseUncovered += OnFoodSurpriseUncovered;
                UpdateLabelVisibility();
            }
            else
            {
                ClearFood();
            }
        }

        private void OnFoodSurpriseUncovered(Food f)
        {
            UpdateLabelVisibility();
        }

        private void UpdateLabelVisibility()
        {
            if (countLabel == null) return;

            // Eğer atanan yemek Surprise modundaysa (Blocked durumundaysa), sayısı gözükmesin.
            if (assignedFood != null && assignedFood.IsSurpriseFood)
            {
                countLabel.gameObject.SetActive(false);
            }
            else if (assignedFood != null && !assignedFood.IsSurpriseFood)
            {
                countLabel.gameObject.SetActive(true);
                countLabel.SetCount(assignedFood.Capacity);
            }
        }

        public void ClearFood()
        {
            if (assignedFood != null)
            {
                assignedFood.SurpriseUncovered -= OnFoodSurpriseUncovered;
            }
            assignedFood = null;

            if (countLabel != null)
            {
                countLabel.Clear();
                // Pool'a dönerken açık bırakılır ki bir dahaki sefere normal yemek gelirse yanlışlıkla gizli kalmasın
                countLabel.gameObject.SetActive(true);
            }
        }

        public void HandleClick()
        {
            if (assignedFood == null) return;

            PlayCountLabelClickPunch();
            assignedFood.ActivateFromTap();
        }

        private void PlayCountLabelClickPunch()
        {
            if (countLabel == null || !countLabel.gameObject.activeSelf) return;

            Transform target = countLabel.transform;

            if (!baseCountLabelScaleCached)
            {
                baseCountLabelScale = target.localScale;
                baseCountLabelScaleCached = true;
            }

            if (clickPunchSequence != null && clickPunchSequence.IsActive())
                clickPunchSequence.Kill();

            clickPunchSequence = DOTween.Sequence();
            clickPunchSequence.SetLink(target.gameObject);
            clickPunchSequence.Append(
                target.DOScale(baseCountLabelScale * clickScaleDownFactor, clickScaleDuration).SetEase(Ease.OutQuad));
            clickPunchSequence.Append(
                target.DOScale(baseCountLabelScale, clickScaleDuration).SetEase(Ease.OutBack));
        }

        private void OnDestroy()
        {
            if (assignedFood != null)
            {
                assignedFood.SurpriseUncovered -= OnFoodSurpriseUncovered;
            }
        }
    }
}