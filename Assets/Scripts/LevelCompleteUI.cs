using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    public class LevelCompleteUI : MonoBehaviour
    {
        [Header("Panel References")]
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private GameObject popupContent;

        [Header("Coin Display (TMP or Legacy Text)")]
        [SerializeField] private TextMeshProUGUI earnedCoinsTMP;
        [SerializeField] private Text earnedCoinsLegacy;

        [Header("Para/Can Bar Referansı")]
        [Tooltip("Bu SAHNEYE ait PlayerStatsBar instance'ı — GameManager'daki 'Win Fail Stats Bar' " +
                 "alanına sürüklediğin İLE AYNI objeyi buraya da sürükle. Coin uçuşma animasyonu " +
                 "hedefini ve gerçek coin ekleme çağrısını buradan yapıyoruz.")]
        [SerializeField] private PlayerStatsBar statsBar;

        [Header("Coin Animation Settings (DOTween)")]
        [SerializeField] private GameObject coinPrefab;
        [SerializeField] private Transform coinContainer;
        [SerializeField] private RectTransform coinStartPoint;
        [SerializeField] private RectTransform coinTargetPoint;
        [SerializeField] private int animatedCoinCount = 15;
        [SerializeField] private float moveDuration = 0.8f;
        [SerializeField] private float totalDelay = 0.4f;
        [SerializeField] private float randomOffsetRange = 75f;
        [SerializeField] private Ease moveEase = Ease.InBack;

        [Header("Sayaç Başlangıç Gecikmesi")]
        [Tooltip("Panel tam göründükten (fade-in bitince) sonra, coin doğması VE sayının sayması BİRLİKTE başlamadan önce ne kadar beklensin. 0 = panel görünür görünmez ikisi de anında başlar.")]
        [SerializeField] private float countStartDelay = 0f;

        [Header("Kutlama Karakteri (Level Sonu — Rastgele)")]
        [Tooltip("Her level bitişinde bu ikisinden RASTGELE biri aktif olur, diğeri pasif kalır.")]
        [SerializeField] private GameObject celebCharacter1;
        [SerializeField] private GameObject celebCharacter2;

        [Header("Animation Settings")]
        [SerializeField] private float fadeDuration = 0.35f;

        private Vector3 baseCoinTargetScale = Vector3.one;
        private bool baseCoinTargetScaleCached = false;

        private readonly List<GameObject> activeCoins = new();

        private void Awake()
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
                panelCanvasGroup.interactable = false;
                panelCanvasGroup.blocksRaycasts = false;
            }

            if (popupContent != null)
                popupContent.SetActive(false);
            
        }

        public void Show(int earnedAmount, PlayerStatsBar bar = null)
        {
            if (popupContent != null)
                popupContent.SetActive(true);

            // ÖNEMLİ: GameManager kendi elindeki (Win/Fail ekranına ait, doğru)
            // PlayerStatsBar referansını BURAYA parametre olarak geçiyor.
            // Bu sayede LevelCompleteUI ile GameManager ASLA farklı iki
            // instance'a bakmaz — "hangisi doğru obje" belirsizliği
            // tamamen ortadan kalkar. Parametre verilmezse (örn. bu ekranı
            // GameManager olmadan tek başına test ediyorsan) eski
            // davranışa (Inspector referansı / otomatik arama) düşer.
            if (bar != null)
            {
                statsBar = bar;
            }
            else if (statsBar == null)
            {
                // Aynı sebep: bar başlangıçta pasif olabileceğinden
                // FindObjectsInactive.Include ile arıyoruz.
                statsBar = FindFirstObjectByType<PlayerStatsBar>(FindObjectsInactive.Include);
            }

            // GÜVENLİK: Coin animasyonu pasif bir objeye StartCoroutine
            // atmasın diye burada da (GameManager zaten yapmış olsa bile)
            // garanti altına alıyoruz.
            if (statsBar != null)
            {
                statsBar.SetVisible(true);
                coinTargetPoint = statsBar.CoinTargetAnchor;
            }

            if(coinTargetPoint != null)
            {
                baseCoinTargetScale = coinTargetPoint.localScale;
                baseCoinTargetScaleCached = true;
            }

            // İSTEK: Her level bitişinde 2 karakterden RASTGELE biri
            // aktif, diğeri pasif olsun.
            ShowRandomCelebCharacter();

            // ÖNEMLİ: Text'i HEMEN "+0"a sıfırlıyoruz — Editor'de bırakılmış
            // eski bir değer (örn. "+40") panel açılır açılmaz bir an için
            // yanlışlıkla görünmesin diye. Coin animasyonu başlamadan önce
            // metin her zaman "+0"dan başlayacak.
            SetCoinText("+0");

            StartCoroutine(ShowSequenceRoutine(earnedAmount));
        }

        /// <summary>
        /// celebCharacter1 ve celebCharacter2'den rastgele birini aktif
        /// yapar, diğerini pasif bırakır. Her Show() çağrısında (yani her
        /// level bitişinde) yeniden rastgele seçilir.
        /// </summary>
        private void ShowRandomCelebCharacter()
        {
            if (celebCharacter1 == null && celebCharacter2 == null)
                return;

            bool showFirst = Random.value < 0.5f;

            if (celebCharacter1 != null) celebCharacter1.SetActive(showFirst);
            if (celebCharacter2 != null) celebCharacter2.SetActive(!showFirst);
        }

        private IEnumerator ShowSequenceRoutine(int earnedAmount)
        {
            // 1. Fade in panel
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.blocksRaycasts = true;
                panelCanvasGroup.alpha = 0f;
                // PAUSE-GÜVENLİ: SetUpdate(true) — GameManager, Win'den ~1sn
                // sonra Time.timeScale = 0 yapıyor. Bu tween scaled kalsaydı,
                // pause tam bu fade sırasında tetiklenirse yarım kalırdı.
                panelCanvasGroup.DOFade(1f, fadeDuration).SetUpdate(true);
                // PAUSE-GÜVENLİ: WaitForSecondsRealtime — aynı sebep.
                yield return new WaitForSecondsRealtime(fadeDuration);
                panelCanvasGroup.interactable = true;
            }

            // İSTEK: Panel tam göründükten sonra, coin doğması ile sayının
            // sayması AYNI ANDA başlasın — aralarında istersen (countStartDelay
            // ile) bir bekleme de ekleyebilirsin. Bu bekleme sırasında metin
            // hâlâ "+0" olarak duruyor (yukarıda Show()'da zaten ayarlandı).
            if (countStartDelay > 0f)
                yield return new WaitForSecondsRealtime(countStartDelay);

            // 2. Play Coin Fly Animation
            if (coinPrefab != null && coinStartPoint != null && coinTargetPoint != null)
            {
                PlayCoinFlyAnimation(earnedAmount);
            }
            else
            {
                SetCoinText($"+{earnedAmount}");
            }
        }

        private void PlayCoinFlyAnimation(int earnedAmount)
        {
            float delayPerCoin = totalDelay / Mathf.Max(1, animatedCoinCount);
            int displayedCoins = 0;
            int stepAmount = Mathf.Max(1, earnedAmount / animatedCoinCount);
            bool hasTriggerBarIncrement = false;
            Transform parent = coinContainer != null ? coinContainer : transform;

            for (int i = 0; i < animatedCoinCount; i++)
            {
                GameObject coin = Instantiate(coinPrefab, parent);
                activeCoins.Add(coin);

                RectTransform rect = coin.GetComponent<RectTransform>();
                Vector2 randomOffset = new Vector2(
                    Random.Range(-randomOffsetRange, randomOffsetRange),
                    Random.Range(-randomOffsetRange, randomOffsetRange)
                );

                rect.position = coinStartPoint.position + (Vector3)randomOffset;
                rect.localScale = Vector3.one * 0.1f;

                float delay = i * delayPerCoin;

                // Scale: küçük (0.1) -> büyük (1.3) -> küçük (0.6) — üç aşamalı.
                // Büyüme, hareketin ilk %40'ında biter; küçülme kalan %60'ında
                // gerçekleşip coin hedefe varırken küçülmüş halde tamamlanır.
                // PAUSE-GÜVENLİ: SetUpdate(true) — bu animasyon (totalDelay +
                // moveDuration, varsayılan ~1.2sn) GameManager'ın 1sn'lik
                // pause gecikmesini AŞABİLİR; scaled kalsaydı pause tam bu
                // sırada tetiklenirse coin havada donup kalırdı.
                DG.Tweening.Sequence scaleSequence = DOTween.Sequence();
                scaleSequence.SetUpdate(true);
                scaleSequence.SetDelay(delay);
                scaleSequence.Append(rect.DOScale(1.3f, moveDuration * 0.4f).SetEase(Ease.OutBack));
                scaleSequence.Append(rect.DOScale(0.6f, moveDuration * 0.6f).SetEase(Ease.InQuad));

                // Move to target
                rect.DOMove(coinTargetPoint.position, moveDuration)
                    .SetUpdate(true)
                    .SetDelay(delay)
                    .SetEase(moveEase)
                    .OnComplete(() =>
                    {
                        if (!hasTriggerBarIncrement)
                        {
                            hasTriggerBarIncrement = true;
                            if(statsBar != null)
                            {
                                statsBar.AddCoinsAnimated(earnedAmount);
                            }
                            else
                            {
                                PlayerData.AddCoins(earnedAmount);
                            }
                        }

                        displayedCoins = Mathf.Min(earnedAmount, displayedCoins + stepAmount);
                        SetCoinText($"+{displayedCoins}");
                        
                        if (coinTargetPoint != null)
                        {
                            // Kill any mid-animation drift and reset to original scale before punching
                            coinTargetPoint.DOKill();
                            if (baseCoinTargetScaleCached)
                                coinTargetPoint.localScale = baseCoinTargetScale;

                            coinTargetPoint.DOPunchScale(baseCoinTargetScale * 0.15f, 0.1f, 5, 1).SetUpdate(true);
                        }

                        activeCoins.Remove(coin);
                        Destroy(coin);
                    });
            }

           // Final text sync & target scale guarantee
            DOVirtual.DelayedCall(totalDelay + moveDuration + 0.15f, () =>
            {
                SetCoinText($"+{earnedAmount}");
                if (coinTargetPoint != null && baseCoinTargetScaleCached)
                {
                    coinTargetPoint.DOKill();
                    coinTargetPoint.localScale = baseCoinTargetScale;
                }
            }).SetUpdate(true);
        }

        private void SetCoinText(string text)
        {
            if (earnedCoinsTMP != null) earnedCoinsTMP.text = text;
            if (earnedCoinsLegacy != null) earnedCoinsLegacy.text = text;
        }

        public void OnContinueButtonPressed()
        {
            AudioEvents.PlayButtonClick();

            if (panelCanvasGroup != null)
                panelCanvasGroup.interactable = false;

            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.LoadMainMenuScene();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
    }
}