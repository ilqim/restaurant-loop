using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Settings panelindeki "Restart" ve "Leave" butonlarının davranışı.
    /// İkisi de SceneFlowManager ÜZERİNDEN gider (fade/loading ekranı ile) —
    /// asla doğrudan SceneManager.LoadScene çağrılmaz, akış her zaman tutarlı
    /// kalsın diye.
    ///
    /// RESTART: "Game" sahnesini YENİDEN yükler. Tüm level'lar zaten tek bir
    /// "Game" sahnesinde yaşadığı ve PlayerData.CurrentLevel burada
    /// DEĞİŞTİRİLMEDİĞİ için, sahneyi yeniden yüklemek = "aynı level'i
    /// baştan başlatmak" ile birebir aynı sonucu verir (LevelManager, sahne
    /// yüklenince yine aynı CurrentLevel'in LevelData'sını GridManager/
    /// QueueManager/LevelConservationChecker'a verecek).
    ///
    /// LEAVE: Main Menu sahnesine döner (yine SceneFlowManager ile, fade
    /// içinde) — level ilerlemesine (PlayerData.CurrentLevel) hiç dokunmaz,
    /// level tamamlanmadığı için bir sonraki level'e de geçilmez.
    /// </summary>
    public class SettingsPanelActions : MonoBehaviour
    {
        public void OnRestartButtonPressed()
        {
            AudioEvents.PlayButtonClick();

            if (SceneFlowManager.Instance != null)
            {
                SceneFlowManager.Instance.LoadGameplayScene();
            }
            else
            {
                Debug.LogWarning("SettingsPanelActions: SceneFlowManager.Instance bulunamadı — restart yapılamadı.");
            }
        }

        public void OnLeaveButtonPressed()
        {
            AudioEvents.PlayButtonClick();

            if (SceneFlowManager.Instance != null)
            {
                SceneFlowManager.Instance.LoadMainMenuScene();
            }
            else
            {
                Debug.LogWarning("SettingsPanelActions: SceneFlowManager.Instance bulunamadı — main menu'ye dönülemedi.");
            }
        }
    }
}