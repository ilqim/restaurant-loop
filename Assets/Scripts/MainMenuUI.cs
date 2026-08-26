using UnityEngine;

namespace RestaurantLoop
{
    public class MainMenuUI : MonoBehaviour
    {
        public void OnPlayButtonPressed()
        {
            if(!PlayerData.HasHearts()) return;
            
            AudioEvents.PlayButtonClick();
            SceneFlowManager.Instance.LoadGameplayScene();
        }
    }
}