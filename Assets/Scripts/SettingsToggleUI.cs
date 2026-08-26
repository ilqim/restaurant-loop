using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// Settings panelindeki toggle'ları GameSettings'e bağlar.
    ///
    /// Aynı script HEM ana menü panelinde HEM oyun içi panelde kullanılır:
    /// - Ana menü: musicToggle + sfxToggle sürükle, vibrationToggle boş bırak.
    /// - Oyun içi: sfxToggle + vibrationToggle sürükle, musicToggle boş bırak
    ///   (oyun içinde müzik yok).
    ///
    /// Boş bırakılan alan için hiçbir şey yapılmaz (null-check var), o yüzden
    /// aynı script iki farklı panel için de sorunsuz çalışır. Hepsi Toggle
    /// component'i olmalı, Button değil.
    /// </summary>
    public class SettingsToggleUI : MonoBehaviour
    {
        [Tooltip("Sadece müziğin göründüğü panelde (ör. ana menü) sürükle. Müziğin olmadığı panelde boş bırak.")]
        [SerializeField] private Toggle musicToggle;

        [SerializeField] private Toggle sfxToggle;

        [Tooltip("Titreşim toggle'ı hangi panelde varsa oraya sürükle.")]
        [SerializeField] private Toggle vibrationToggle;

        private void OnEnable()
        {
            // Panel her açıldığında güncel durumu göster. SetIsOnWithoutNotify
            // kullanıyoruz çünkü "isOn = ..." yazmak onValueChanged'i de
            // tetikler; bu da GameSettings'e gereksiz yere aynı değeri
            // tekrar yazdırır (zararsız ama gereksiz).
            if (musicToggle != null)
            {
                musicToggle.SetIsOnWithoutNotify(GameSettings.MusicEnabled);
                musicToggle.onValueChanged.AddListener(OnMusicToggleChanged);
            }

            if (sfxToggle != null)
            {
                sfxToggle.SetIsOnWithoutNotify(GameSettings.SfxEnabled);
                sfxToggle.onValueChanged.AddListener(OnSfxToggleChanged);
            }

            if (vibrationToggle != null)
            {
                vibrationToggle.SetIsOnWithoutNotify(GameSettings.VibrationEnabled);
                vibrationToggle.onValueChanged.AddListener(OnVibrationToggleChanged);
            }
        }

        private void OnDisable()
        {
            if (musicToggle != null) musicToggle.onValueChanged.RemoveListener(OnMusicToggleChanged);
            if (sfxToggle != null) sfxToggle.onValueChanged.RemoveListener(OnSfxToggleChanged);
            if (vibrationToggle != null) vibrationToggle.onValueChanged.RemoveListener(OnVibrationToggleChanged);
        }

        private void OnMusicToggleChanged(bool isOn) => GameSettings.MusicEnabled = isOn;

        private void OnSfxToggleChanged(bool isOn) => GameSettings.SfxEnabled = isOn;

        private void OnVibrationToggleChanged(bool isOn) => GameSettings.VibrationEnabled = isOn;
    }
}