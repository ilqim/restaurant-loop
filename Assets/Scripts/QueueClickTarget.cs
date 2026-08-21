using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Bir queue hücresinin tıklanabilir collider'ını taşır. QueueManager,
    /// bir food'u bu hücreye "ışınladığında" AssignFood ile hangi food'un
    /// burada durduğunu bildirir.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class QueueClickTarget : MonoBehaviour
    {
        private Food assignedFood;

        public void AssignFood(Food food) => assignedFood = food;
        public void ClearFood() => assignedFood = null;

        public void HandleClick()
        {
            if (assignedFood == null) return;
            assignedFood.ActivateFromTap();
        }
    }
}