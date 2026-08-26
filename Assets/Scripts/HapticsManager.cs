using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// HapticsEvents.VibrateRequested'ı dinleyip GameSettings.VibrationEnabled
    /// true ise gerçekten titreşim tetikler. AudioManager gibi
    /// DontDestroyOnLoad singleton — sahneler arası tek instance yeterli,
    /// ekstra bir kaynağa (AudioSource gibi) ihtiyacı yok.
    /// </summary>
    public class HapticsManager : MonoBehaviour
    {
        public static HapticsManager Instance { get; private set; }

        [Tooltip("Sahneler arası geçişte yok olmasın diye açık tut (genelde açık kalmalı).")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() => HapticsEvents.VibrateRequested += HandleVibrateRequested;

        private void OnDisable() => HapticsEvents.VibrateRequested -= HandleVibrateRequested;

        private void HandleVibrateRequested()
        {
            if (!GameSettings.VibrationEnabled)
                return;

#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}