using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Main Menu sahnesine koyulur. Şu anki level'i ve sıradaki 3 level'i
    /// LevelManager'dan okuyup TMP text'lere yazar. Level numaraları
    /// otomatik hesaplanır (parametre vermene gerek yok) — current + 1, +2, +3.
    /// </summary>
    public class MainMenuLevelDisplay : MonoBehaviour
    {
        [Header("Level Yazıları")]
        [Tooltip("Şu an oynanacak (current) level'i gösteren text.")]
        [SerializeField] private TMP_Text currentLevelText;

        [Tooltip("Sıradaki 3 level'i gösteren text'ler, sırasıyla current+1, current+2, current+3.")]
        [SerializeField] private TMP_Text[] upcomingLevelTexts = new TMP_Text[3];

        [Tooltip("'Level {0}' formatı — {0} yerine level numarası gelir.")]
        [SerializeField] private string labelFormat = "Level {0}";

        [Tooltip("Toplam level sayısını aşan sıradaki level'ler için gösterilecek metin (boş bırakılabilir).")]
        [SerializeField] private string beyondLastLevelText = "";

        private void Start()
        {
            Refresh();
        }

        /// <summary>Level ilerlemesi değiştiyse (örn. bir level bitirildiyse) dışarıdan tekrar çağrılabilir.</summary>
        public void Refresh()
        {
            if (LevelManager.Instance == null)
            {
                Debug.LogWarning("MainMenuLevelDisplay: Sahnede/kalıcı olarak bir LevelManager bulunamadı.");
                return;
            }

            int current = LevelManager.Instance.CurrentLevel;
            int total = LevelManager.Instance.TotalLevelCount;

            if (currentLevelText != null)
                currentLevelText.text = string.Format(labelFormat, current);

            for (int i = 0; i < upcomingLevelTexts.Length; i++)
            {
                if (upcomingLevelTexts[i] == null)
                    continue;

                int levelNumber = current + i + 1;

                upcomingLevelTexts[i].text = levelNumber <= total
                    ? string.Format(labelFormat, levelNumber)
                    : beyondLastLevelText;
            }
        }
    }
}