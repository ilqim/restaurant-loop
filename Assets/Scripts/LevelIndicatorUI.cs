using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Game sahnesine koyulur. O anki level numarasını "Level X" formatında gösterir.
    /// </summary>
    public class LevelIndicatorUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private string labelFormat = "Level {0}";

        private void Start()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (levelText == null)
                return;

            if (LevelManager.Instance == null)
            {
                Debug.LogWarning("LevelIndicatorUI: Sahnede/kalıcı olarak bir LevelManager bulunamadı.");
                return;
            }

            levelText.text = string.Format(labelFormat, LevelManager.Instance.CurrentLevel);
        }
    }
}