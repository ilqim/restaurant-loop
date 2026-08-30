using DG.Tweening;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Raycast ile tıklanabilen her şeyin implement ettiği ortak arayüz.
    /// InputManager sadece bu arayüzü arıyor — QueueSlot, Slot ya da
    /// ileride eklenecek başka bir tıklanabilir tip fark etmiyor,
    /// InputManager hiçbirine özel olarak bağlı değil.
    /// </summary>
    public interface IQueueClickable
    {
        void HandleClick();
    }

    /// <summary>
    /// Bir queue hücresinin tıklanabilir collider'ını taşır. Food'un
    /// KENDİSİNDE collider YOK — tıklanabilir alan burada, sabit bir
    /// grid hücresinde duruyor. QueueManager bir food'u bu hücreye
    /// "ışınladığında" AssignFood ile hangi food'un burada durduğunu
    /// bildiriyor.
    ///
    /// GÖRSEL: Arkada ayrı bir slot sprite'ı YOK (o Slot.cs'te var, burada
    /// değil) — ama bu QueueSlot'un kendi child'ı olan sayı etiketi
    /// (countLabel) var. Food tıklanınca küçülüp büyürken, üzerindeki bu
    /// sayı da AYNI ANDA senkron küçülüp büyüsün diye punch-scale burada
    /// SADECE countLabel'a uygulanıyor.
    /// </summary>
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

        // Aynı DOTween "kill mid-tween" sorununu önlemek için — Food.cs'teki
        // baseScale ile AYNI mantık, sadece hedef burada countLabel'ın
        // transform'u.
        private Vector3 baseCountLabelScale;
        private bool baseCountLabelScaleCached;

        public Food AssignedFood => assignedFood;

        public void AssignFood(Food food)
        {
            assignedFood = food;

            if (food != null)
            {
                countLabel?.SetCount(food.Capacity);
            }
            else
            {
                ClearFood();
            }
        }

        public void ClearFood()
        {
            assignedFood = null;
            countLabel?.Clear();
        }

        public void HandleClick()
        {
            if (assignedFood == null) return;

            PlayCountLabelClickPunch();
            assignedFood.ActivateFromTap();
        }

        /// <summary>
        /// Food.cs'teki PlayClickPunch ile BİREBİR AYNI mantık — sadece
        /// hedef bu slot'un countLabel'ının transform'u. Aynı anda
        /// tetiklenip aynı süre/oranla çalıştığı için Food ile senkron
        /// (birlikte küçülüp büyür) görünür.
        /// </summary>
        private void PlayCountLabelClickPunch()
        {
            if (countLabel == null) return;

            Transform target = countLabel.transform;

            // İlk çağrıda gerçek ölçeği BİR KEZ sabitliyoruz — sonraki
            // çağrılarda transform.localScale'in o anki (bir önceki
            // animasyon küçülme aşamasındayken kesilmiş olabilecek,
            // bozulmuş) haline hiç güvenmiyoruz.
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
    }
}