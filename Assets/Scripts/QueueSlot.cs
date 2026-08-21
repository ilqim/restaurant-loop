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
    /// Not: Bu component sadece "burada hangi food var" bilgisini tutar,
    /// food'un state'i (Locked/Available) ile ilgilenmez — kilitli bir
    /// food'a tıklanırsa Food.ActivateFromTap() zaten kendi state'ine
    /// göre no-op yapar. Böylece QueueSlot'un state kontrolü bilmesine
    /// gerek kalmıyor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class QueueSlot : MonoBehaviour, IQueueClickable
    {
        private Food assignedFood;

        public Food AssignedFood => assignedFood;

        public void AssignFood(Food food) => assignedFood = food;
        public void ClearFood() => assignedFood = null;

        public void HandleClick()
        {
            if (assignedFood == null) return;
            assignedFood.ActivateFromTap();
        }
    }
}