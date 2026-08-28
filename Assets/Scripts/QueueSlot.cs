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
    /// GÖRSEL: Artık SpriteRenderer/renk YÖNETMİYOR — sadece tıklanabilir
    /// collider'ı ve sayı etiketini (countLabel) yönetiyor. Food'un kendi
    /// görseli (prefab'ı) zaten ayrı bir obje olarak duruyor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class QueueSlot : MonoBehaviour, IQueueClickable
    {
        private Food assignedFood;

        [Header("Sayı Etiketi")]
        [Tooltip("İçindeki food'un kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi).")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

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
            assignedFood.ActivateFromTap();
        }
    }
}