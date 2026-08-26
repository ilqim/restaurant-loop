using System;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Ayarlar menüsündeki müzik/sfx toggle durumlarını tutan tek merkez.
    ///
    /// MonoBehaviour DEĞİL, static bir sınıf — bu yüzden sahne geçişinde
    /// zaten hafızada kalır, ayrı bir DontDestroyOnLoad singleton kurmana
    /// gerek yok. Ayrıca PlayerPrefs'e yazdığı için oyunu tamamen kapatıp
    /// tekrar açsan bile son seçilen ayar geri gelir (sadece session içinde
    /// kalsın istiyorsan PlayerPrefs satırlarını silip sadece alanları
    /// (musicEnabled/sfxEnabled) tutman yeterli).
    ///
    /// Toggle UI'ları (SettingsToggleUI.cs) bu sınıfın MusicEnabled /
    /// SfxEnabled property'lerini set eder. AudioManager bu değişiklikleri
    /// dinleyip gerçek AudioSource'ları anında susturur/açar.
    /// </summary>
    public static class GameSettings
    {
        private const string MusicPrefKey = "Settings_MusicEnabled";
        private const string SfxPrefKey = "Settings_SfxEnabled";
        private const string VibrationPrefKey = "Settings_VibrationEnabled";

        private static bool? musicEnabled;
        private static bool? sfxEnabled;
        private static bool? vibrationEnabled;

        public static event Action<bool> MusicEnabledChanged;
        public static event Action<bool> SfxEnabledChanged;
        public static event Action<bool> VibrationEnabledChanged;

        public static bool MusicEnabled
        {
            get
            {
                musicEnabled ??= PlayerPrefs.GetInt(MusicPrefKey, 1) == 1;
                return musicEnabled.Value;
            }
            set
            {
                if (musicEnabled.HasValue && musicEnabled.Value == value) return;
                musicEnabled = value;
                PlayerPrefs.SetInt(MusicPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                MusicEnabledChanged?.Invoke(value);
            }
        }

        public static bool SfxEnabled
        {
            get
            {
                sfxEnabled ??= PlayerPrefs.GetInt(SfxPrefKey, 1) == 1;
                return sfxEnabled.Value;
            }
            set
            {
                if (sfxEnabled.HasValue && sfxEnabled.Value == value) return;
                sfxEnabled = value;
                PlayerPrefs.SetInt(SfxPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                SfxEnabledChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// Ana menüde kapatılırsa oyun içi ekranda da kapalı görünür (ve
        /// tersi) — ikisi de aynı değeri okur/yazar, ayrı bir "sync" koduna
        /// gerek yok.
        /// </summary>
        public static bool VibrationEnabled
        {
            get
            {
                vibrationEnabled ??= PlayerPrefs.GetInt(VibrationPrefKey, 1) == 1;
                return vibrationEnabled.Value;
            }
            set
            {
                if (vibrationEnabled.HasValue && vibrationEnabled.Value == value) return;
                vibrationEnabled = value;
                PlayerPrefs.SetInt(VibrationPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                VibrationEnabledChanged?.Invoke(value);
            }
        }
    }
}