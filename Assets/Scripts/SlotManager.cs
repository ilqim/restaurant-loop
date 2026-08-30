using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public class SlotManager : MonoBehaviour
    {
        [Header("Slots")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        [Header("Warning")]
        [Tooltip("Warning'ın başlaması için kaç slotun dolu olması gerektiği.")]
        [SerializeField] private int warningSlotCount = 4;

        public bool TryPlaceFood(Food food)
        {
            if (food == null)
                return false;

            // Soldan sağa sırayla ilk boş slotu bul.
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];

                if (slot == null)
                    continue;

                // Doluysa geç.
                if (!slot.IsEmpty)
                    continue;

                // İlk boş slota yerleştir.
                bool placed = slot.TryPlaceFood(food);

                if (placed)
                {
                    // Food yerleştirildikten sonra warning kontrolü yap.
                    CheckWarning();

                    return true;
                }
            }

            // Buraya geldiysek hiçbir slot boş değil.
            Debug.Log("SlotManager: TÜM SLOT'LAR DOLU!");

            // Tüm slotlarda warning oynat.
            PlayWarningOnAllSlots();

            // GameManager'a FAIL bildir.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.FailLevel();
            }

            return false;
        }

        private void CheckWarning()
        {
            int occupiedSlotCount = 0;

            // Kaç slotun dolu olduğunu hesapla.
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && !slots[i].IsEmpty)
                {
                    occupiedSlotCount++;
                }
            }

            // Belirlenen warning sınırına ulaşıldıysa
            // tüm slotlarda warning oynat.
            if (occupiedSlotCount >= warningSlotCount)
            {
                Debug.Log(
                    $"SlotManager: Warning! {occupiedSlotCount}/{slots.Count} slot dolu."
                );

                PlayWarningOnAllSlots();
            }
        }

        private void PlayWarningOnAllSlots()
        {
            HapticsEvents.Vibrate();
            
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].PlayWarningFlash();
                }
            }
        }
    }
}