using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct SfxClip
    {
        public SfxId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Tek seferlik (one-shot) efektler")]
        [Tooltip("Her SfxId için bir AudioClip sürükle. Boş bırakılan bir id çağrıldığında Console'a uyarı basılır, oyun çökmez.")]
        [SerializeField]
        private List<SfxClip> clips = new()
        {
            new SfxClip { id = SfxId.ButtonClick, volume = 1f },
            new SfxClip { id = SfxId.NegativeButtonClick, volume = 1f },
            new SfxClip { id = SfxId.FoodClick, volume = 1f },
            new SfxClip { id = SfxId.OrderDelivered, volume = 1f },
            new SfxClip { id = SfxId.New, volume = 1f },
            new SfxClip { id = SfxId.LevelComplete, volume = 1f },
            new SfxClip { id = SfxId.LevelFail, volume = 1f },
            new SfxClip { id = SfxId.CoinEarn, volume = 1f },
            new SfxClip { id = SfxId.TimedCustomerFail, volume = 1f },
        };

        [Header("Süreli Müşteri Geri Sayım Sesi (loop — Start/Stop ile kontrol edilir, SFX kanalı sayılır)")]
        [SerializeField] private AudioClip timedCustomerCountdownClip;
        [Range(0f, 1f)][SerializeField] private float timedCustomerCountdownVolume = 1f;

        [Header("Müzik — Ana Menü (loop)")]
        [SerializeField] private AudioClip menuMusicClip;
        [Range(0f, 1f)][SerializeField] private float menuMusicVolume = 1f;

        [Header("Level Müzikleri — Zorluğa Göre (Game sahnesinde çalar")]
        [Tooltip("Level zorluğu Easy ise bu müzik çalar.")]
        [SerializeField] private AudioClip easyMusicClip;

        [Tooltip("Level zorluğu Hard ise bu müzik çalar.")]
        [SerializeField] private AudioClip hardMusicClip;

        [Tooltip("Level zorluğu SuperHard ise bu müzik çalar.")]
        [SerializeField] private AudioClip superHardMusicClip;

        [Range(0f, 1f)]
        [SerializeField] private float easyMusicVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float hardMusicVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float superHardMusicVolume = 1f;

        [Header("Fail Müziği (level kaybedilince)")]
        [Tooltip("Level FAIL olduğunda çalacak, level müziklerinden AYRI bir müzik/jingle.")]
        [SerializeField] private AudioClip failMusicClip;
        [Range(0f, 1f)][SerializeField] private float failMusicVolume = 1f;

        [Header("Kaynaklar")]
        [Tooltip("Aynı anda üst üste binen one-shot sesler için havuz boyutu. 3-4 genelde yeterli.")]
        [SerializeField] private int oneShotSourceCount = 4;
        [Tooltip("Opsiyonel — Audio Mixer kullanıyorsan SFX grubunu buraya sürükle. Boş bırakılabilir.")]
        [SerializeField] private AudioMixerGroup sfxMixerGroup;
        [Tooltip("Opsiyonel — Audio Mixer kullanıyorsan Müzik grubunu buraya sürükle. Boş bırakılabilir.")]
        [SerializeField] private AudioMixerGroup musicMixerGroup;

        [Header("Ayarlar")]
        [Tooltip("Sahneler arası geçişte AudioManager yok olmasın diye açık tut (genelde açık kalmalı).")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        private readonly Dictionary<SfxId, SfxClip> clipLookup = new();
        private AudioSource[] oneShotSources;
        private int nextSourceIndex;
        private AudioSource countdownSource;
        private AudioSource musicSource;
        private Coroutine coinEarnSequenceRoutine;

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

            clipLookup.Clear();
            foreach (var entry in clips)
                clipLookup[entry.id] = entry;

            BuildAudioSources();
        }

        private void BuildAudioSources()
        {
            oneShotSources = new AudioSource[Mathf.Max(1, oneShotSourceCount)];
            for (int i = 0; i < oneShotSources.Length; i++)
            {
                var go = new GameObject($"OneShotSource_{i}");
                go.transform.SetParent(transform, false);

                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                if (sfxMixerGroup != null) src.outputAudioMixerGroup = sfxMixerGroup;

                oneShotSources[i] = src;
            }

            var countdownGo = new GameObject("CountdownSource");
            countdownGo.transform.SetParent(transform, false);

            countdownSource = countdownGo.AddComponent<AudioSource>();
            countdownSource.playOnAwake = false;
            countdownSource.loop = true;
            countdownSource.mute = !GameSettings.SfxEnabled;
            if (sfxMixerGroup != null) countdownSource.outputAudioMixerGroup = sfxMixerGroup;

            var musicGo = new GameObject("MusicSource");
            musicGo.transform.SetParent(transform, false);

            musicSource = musicGo.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.mute = !GameSettings.MusicEnabled;
            if (musicMixerGroup != null) musicSource.outputAudioMixerGroup = musicMixerGroup;
        }

        private void OnEnable()
        {
            AudioEvents.SfxRequested += HandleSfxRequested;
            AudioEvents.TimedCustomerCountdownStartRequested += HandleCountdownStart;
            AudioEvents.TimedCustomerCountdownStopRequested += HandleCountdownStop;
            AudioEvents.MusicPlayRequested += HandlePlayMusic;
            AudioEvents.MusicStopRequested += HandleMusicStop;
            AudioEvents.MusicForDifficultyRequested += PlayMusicForDifficulty;
            AudioEvents.FailMusicRequested += PlayFailMusic;
            AudioEvents.CoinEarnSequenceRequested += HandleCoinEarnSequenceRequested;

            GameSettings.MusicEnabledChanged += HandleMusicEnabledChanged;
            GameSettings.SfxEnabledChanged += HandleSfxEnabledChanged;
        }

        private void OnDisable()
        {
            AudioEvents.SfxRequested -= HandleSfxRequested;
            AudioEvents.TimedCustomerCountdownStartRequested -= HandleCountdownStart;
            AudioEvents.TimedCustomerCountdownStopRequested -= HandleCountdownStop;
            AudioEvents.MusicPlayRequested -= HandlePlayMusic;
            AudioEvents.MusicStopRequested -= HandleMusicStop;
            AudioEvents.MusicForDifficultyRequested -= PlayMusicForDifficulty;
            AudioEvents.FailMusicRequested -= PlayFailMusic;
            AudioEvents.CoinEarnSequenceRequested -= HandleCoinEarnSequenceRequested;

            GameSettings.MusicEnabledChanged -= HandleMusicEnabledChanged;
            GameSettings.SfxEnabledChanged -= HandleSfxEnabledChanged;
        }

        private void HandleSfxRequested(SfxId id) => PlaySfx(id);

        public void PlaySfx(SfxId id)
        {
            if (!GameSettings.SfxEnabled)
                return;

            if (!clipLookup.TryGetValue(id, out var entry) || entry.clip == null)
            {
                Debug.LogWarning($"AudioManager: '{id}' için AudioClip atanmamış (Inspector'dan sürükle).");
                return;
            }

            var source = oneShotSources[nextSourceIndex];
            nextSourceIndex = (nextSourceIndex + 1) % oneShotSources.Length;

            source.PlayOneShot(entry.clip, entry.volume);
        }

        private void HandleCountdownStart()
        {
            if (timedCustomerCountdownClip == null)
            {
                Debug.LogWarning("AudioManager: Timed Customer Countdown Clip atanmamış.");
                return;
            }

            countdownSource.clip = timedCustomerCountdownClip;
            countdownSource.volume = timedCustomerCountdownVolume;
            countdownSource.mute = !GameSettings.SfxEnabled;

            if (!countdownSource.isPlaying)
                countdownSource.Play();
        }

        private void HandleCountdownStop()
        {
            if (countdownSource.isPlaying)
                countdownSource.Stop();
        }

        /// <summary>
        /// ANA MENÜ müziği. AudioEvents.PlayMusic() üzerinden çağrılır.
        /// Zaten çalıyorsa yeniden baştan başlatmaz.
        /// </summary>
        public void HandlePlayMusic()
        {
            if (menuMusicClip == null)
            {
                Debug.LogWarning("AudioManager: Menu Music Clip atanmamış (Inspector'dan sürükle).");
                return;
            }

            if (musicSource.isPlaying && musicSource.clip == menuMusicClip)
                return;

            musicSource.clip = menuMusicClip;
            musicSource.volume = menuMusicVolume;
            musicSource.mute = !GameSettings.MusicEnabled;
            musicSource.Play();
        }

        /// <summary>
        /// LEVEL MÜZİĞİ — LevelManager, Game sahnesi yüklendiğinde o anki
        /// level'in zorluğuna göre bunu çağırır. AYNI musicSource'u
        /// kullanır (menü müziğiyle aynı kanal) — zaten çalan clip
        /// istenenle aynıysa yeniden başlatmaz.
        /// </summary>
        public void PlayMusicForDifficulty(LevelDifficulty difficulty)
        {
            AudioClip clip = difficulty switch
            {
                LevelDifficulty.Easy => easyMusicClip,
                LevelDifficulty.Hard => hardMusicClip,
                LevelDifficulty.SuperHard => superHardMusicClip,
                _ => null
            };

            float volume = difficulty switch
            {
                LevelDifficulty.Easy => easyMusicVolume,
                LevelDifficulty.Hard => hardMusicVolume,
                LevelDifficulty.SuperHard => superHardMusicVolume,
                _ => 1f
            };

            if (clip == null)
            {
                Debug.LogWarning($"AudioManager: '{difficulty}' zorluğu için müzik clip'i atanmamış (Inspector'dan sürükle).");
                return;
            }

            if (musicSource.isPlaying && musicSource.clip == clip)
                return;

            musicSource.clip = clip;
            musicSource.volume = volume;
            musicSource.mute = !GameSettings.MusicEnabled;
            musicSource.Play();
        }

        /// <summary>
        /// FAIL MÜZİĞİ — level müziklerinden AYRI, kendine has bir müzik/
        /// jingle. AYNI musicSource'u kullanır (tek müzik kanalı), zaten
        /// çalan clip aynıysa yeniden başlatmaz.
        /// </summary>
        public void PlayFailMusic()
        {
            if (failMusicClip == null)
            {
                Debug.LogWarning("AudioManager: Fail Music Clip atanmamış (Inspector'dan sürükle).");
                return;
            }

            if (musicSource.isPlaying && musicSource.clip == failMusicClip)
                return;

            musicSource.clip = failMusicClip;
            musicSource.volume = failMusicVolume;
            musicSource.mute = !GameSettings.MusicEnabled;
            musicSource.Play();
        }

        private void HandleMusicStop()
        {
            if (musicSource.isPlaying)
                musicSource.Stop();
        }

        private void HandleMusicEnabledChanged(bool enabled) => musicSource.mute = !enabled;

        private void HandleSfxEnabledChanged(bool enabled)
        {
            countdownSource.mute = !enabled;
        }

        // ---- Coin Sesi — Ardışık Çalma (üst üste binmeden) ----

        private void HandleCoinEarnSequenceRequested(int times) => PlayCoinEarnSequence(times);

        /// <summary>
        /// Coin sesini 'times' kez ARDIŞIK çalar — biri TAM bitmeden
        /// diğeri başlamaz (aralarında clip uzunluğu kadar bekleniyor).
        /// Zaten çalışan bir dizi varsa onu durdurup baştan başlatır.
        /// </summary>
        public void PlayCoinEarnSequence(int times)
        {
            if (times <= 0)
                return;

            if (coinEarnSequenceRoutine != null)
                StopCoroutine(coinEarnSequenceRoutine);

            coinEarnSequenceRoutine = StartCoroutine(PlayCoinEarnSequenceRoutine(times));
        }

        private IEnumerator PlayCoinEarnSequenceRoutine(int times)
        {
            if (!clipLookup.TryGetValue(SfxId.CoinEarn, out var entry) || entry.clip == null)
            {
                Debug.LogWarning("AudioManager: 'CoinEarn' için AudioClip atanmamış (Inspector'dan sürükle).");
                coinEarnSequenceRoutine = null;
                yield break;
            }

            for (int i = 0; i < times; i++)
            {
                PlaySfx(SfxId.CoinEarn);

                // PAUSE-GÜVENLİ: WaitForSecondsRealtime — GameManager, Win'den
                // ~1sn sonra Time.timeScale = 0 yapıyor. Normal WaitForSeconds
                // kullansaydık, bu duraklama TAM aralardan birine denk
                // geldiğinde coroutine sonsuza kadar donup kalır, bu da
                // "3 kez çalması gerekirken 2'de takılı kalma" sorununa
                // yol açardı (tam olarak yaşanan buydu).
                yield return new WaitForSecondsRealtime(entry.clip.length);
            }

            coinEarnSequenceRoutine = null;
        }
    }
}