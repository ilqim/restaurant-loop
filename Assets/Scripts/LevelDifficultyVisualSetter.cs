using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// Bir zorluk seviyesi (Easy/Hard/SuperHard) için gösterilecek 4
    /// görselin bir arada tutulduğu veri satırı. Inspector'da her zorluk
    /// için bir satır dolduruyorsun. NOT: Bu bir MonoBehaviour DEĞİL,
    /// sadece veri — Component olarak sahneye EKLENEMEZ. Sahneye eklemen
    /// gereken, aşağıdaki LevelDifficultyVisualSetter'dır.
    /// </summary>
    [Serializable]
    public struct DifficultyVisualSet
    {
        public LevelDifficulty difficulty;

        [Tooltip("Oyun zemini için — 2D SpriteRenderer'a atanacak sprite.")]
        public Sprite groundSprite;

        [Tooltip("Level indicator UI Image'ı için — Source Image olarak atanacak sprite.")]
        public Sprite levelIndicatorSprite;

        [Tooltip("Bottom bar UI Image'ı için — Source Image olarak atanacak sprite.")]
        public Sprite topBarSprite;

        [Tooltip("Settings (ayarlar) UI Image'ı için — Source Image olarak atanacak sprite.")]
        public Sprite settingsSprite;
    }

    /// <summary>
    /// Game sahnesi her yüklendiğinde (Awake), LevelManager'dan "şu anki
    /// level'in zorluğu ne" bilgisini sorup, o zorluğa karşılık gelen
    /// 4 görseli (zemin, level indicator, bottom bar, settings) ilgili
    /// SpriteRenderer/Image bileşenlerine uygular.
    ///
    /// KURULUM: Bu component'i (LevelDifficultyVisualSetter — struct'ı
    /// DEĞİL) boş bir GameObject'e ekle, 4 hedef referansı ata, Visual
    /// Sets listesine Easy/Hard/SuperHard için birer satır doldur.
    /// </summary>
    public class LevelDifficultyVisualSetter : MonoBehaviour
    {
        [Header("Hedef Bileşenler")]
        [Tooltip("Oyun zemini — 2D SpriteRenderer.")]
        [SerializeField] private SpriteRenderer groundRenderer;

        [Tooltip("Level indicator UI Image'ı.")]
        [SerializeField] private Image levelIndicatorImage;

        [Tooltip("Bottom bar UI Image'ı.")]
        [SerializeField] private Image bottomBarImage;

        [Tooltip("Settings (ayarlar) UI Image'ı.")]
        [SerializeField] private Image settingsImage;

        [Header("Zorluk Başına Görsel Setleri")]
        [Tooltip("Her zorluk (Easy/Hard/SuperHard) için ayrı bir satır doldur — her satırda zemin, level indicator, bottom bar ve settings sprite'ı.")]
        [SerializeField] private List<DifficultyVisualSet> visualSets = new();

        private void Awake()
        {
            ApplyVisualsForCurrentLevel();
        }

        /// <summary>
        /// Dışarıdan da (örn. runtime'da level değişince) tekrar
        /// çağrılabilir — public bırakıldı.
        /// </summary>
        public void ApplyVisualsForCurrentLevel()
        {
            if (LevelManager.Instance == null)
            {
                Debug.LogWarning("LevelDifficultyVisualSetter: LevelManager.Instance bulunamadı — görseller değiştirilemedi.");
                return;
            }

            if (!LevelManager.Instance.TryGetLevelDifficulty(LevelManager.Instance.CurrentLevel, out LevelDifficulty difficulty))
            {
                Debug.LogWarning($"LevelDifficultyVisualSetter: Level {LevelManager.Instance.CurrentLevel} için zorluk bilgisi bulunamadı.");
                return;
            }

            foreach (var set in visualSets)
            {
                if (set.difficulty != difficulty)
                    continue;

                ApplySet(set);
                return;
            }

            Debug.LogWarning($"LevelDifficultyVisualSetter: '{difficulty}' zorluğu için Visual Sets listesinde bir satır tanımlanmamış.");
        }

        private void ApplySet(DifficultyVisualSet set)
        {
            if (groundRenderer != null && set.groundSprite != null)
                groundRenderer.sprite = set.groundSprite;

            if (levelIndicatorImage != null && set.levelIndicatorSprite != null)
                levelIndicatorImage.sprite = set.levelIndicatorSprite;

            if (bottomBarImage != null && set.topBarSprite != null)
                bottomBarImage.sprite = set.topBarSprite;

            if (settingsImage != null && set.settingsSprite != null)
                settingsImage.sprite = set.settingsSprite;
        }
    }
}