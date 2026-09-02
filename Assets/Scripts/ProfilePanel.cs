using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// "Edit Profile" ekranını yönetir — isim VE avatar seçimiyle ilgili
    /// HER ŞEY tek bu class'ta. Ayrı bir "AvatarOptionButton" component'i
    /// yok — 9 avatar butonu, çerçeveleri ve ✓ ikonları burada, index'e
    /// göre paralel listelerle (avatarButtons[i] <-> selectedFrames[i] <->
    /// selectedCheckmarks[i] <-> avatarImages[i]) yönetiliyor.
    ///
    /// İSİM AKIŞI:
    /// - Panel açıldığında input field PlayerData.PlayerName ile doldurulur,
    ///   DÜZENLENEMEZ (interactable=false) durumda gösterilir.
    /// - Kalem butonuna basılınca alan interactable=true olur, caret açılır.
    ///
    /// AVATAR AKIŞI:
    /// - Panel açıldığında PlayerData.PlayerAvatarIndex'e denk gelen
    ///   seçenek highlight'lı (çerçeve + ✓) gösterilir, üstteki büyük
    ///   önizleme ikonu da o sprite ile güncellenir.
    /// - Bir avatara tıklanınca SADECE grid içindeki seçim (highlight +
    ///   üstteki önizleme) ANINDA değişir — henüz PlayerData'ya YAZILMAZ.
    ///
    /// SAVE:
    /// - Save butonuna basılınca (ya da isim alanında Enter/Done'a
    ///   basılınca) hem isim hem seçili avatar PlayerData'ya YAZILIR —
    ///   avatar yazımı PlayerAvatarIndexChanged'i tetikler, buna abone
    ///   olan HomeAvatarIcon (ayrı dosyada, HomeAvatarIcon.cs) ana
    ///   menüdeki ikonu otomatik günceller.
    /// </summary>
    public class ProfilePanel : MonoBehaviour
    {
        [Header("İsim")]
        [Tooltip("İsmin yazıldığı/gösterildiği input field.")]
        [SerializeField] private TMP_InputField nameInputField;
        [Tooltip("Kalem ikonunu taşıyan buton — basılınca düzenleme moduna geçilir.")]
        [SerializeField] private Button editButton;
        [Tooltip("Düzenleme moduna geçildiğinde mevcut metin otomatik olarak tamamen seçili hale gelsin mi.")]
        [SerializeField] private bool selectAllOnEdit = true;

        [Header("Avatar — Veritabanı")]
        [Tooltip("Tüm avatar sprite'larının kaynağı — index sırası aşağıdaki listelerle BİREBİR AYNI olmalı.")]
        [SerializeField] private AvatarDatabase avatarDatabase;

        [Header("Avatar — Grid (9 eleman, hepsi AYNI SIRADA)")]
        [Tooltip("Grid'deki her avatarın kendi Button'ı — index 0 = grid'deki ilk (sol üst) avatar.")]
        [SerializeField] private List<Button> avatarButtons = new();
        [Tooltip("Her avatarın görselini gösteren Image — Setup sırasında avatarDatabase'ten sprite atanır.")]
        [SerializeField] private List<Image> avatarImages = new();
        [Tooltip("Seçiliyken görünecek çerçeve/border objeleri (ör. turuncu highlight).")]
        [SerializeField] private List<GameObject> selectedFrames = new();
        [Tooltip("Seçiliyken görünecek yeşil onay (✓) ikonları.")]
        [SerializeField] private List<GameObject> selectedCheckmarks = new();

        [Header("Avatar — Önizleme")]
        [Tooltip("NAME kutusunun solundaki büyük önizleme ikonu — seçim değiştikçe (Save'e basmadan) anında güncellenir.")]
        [SerializeField] private Image previewAvatarImage;

        [Header("Ortak")]
        [Tooltip("Panelin altındaki Save butonu — hem ismi hem avatarı kaydeder.")]
        [SerializeField] private Button saveButton;

        private int selectedAvatarIndex;

        private void Awake()
        {
            if (nameInputField != null)
                nameInputField.characterLimit = PlayerData.PlayerNameMaxLength;

            for (int i = 0; i < avatarButtons.Count; i++)
            {
                if (avatarButtons[i] == null) continue;

                if (i < avatarImages.Count && avatarImages[i] != null && avatarDatabase != null)
                    avatarImages[i].sprite = avatarDatabase.GetSprite(i);

                int capturedIndex = i; // closure fix — for içinde i'yi doğrudan yakalamamak için
                avatarButtons[i].onClick.AddListener(() => OnAvatarButtonClicked(capturedIndex));
            }
        }

        private void OnEnable()
        {
            RefreshNameFromPlayerData();
            SetEditingEnabled(false);

            selectedAvatarIndex = PlayerData.PlayerAvatarIndex;
            RefreshAvatarHighlights();
            RefreshPreviewImage();

            if (editButton != null)
                editButton.onClick.AddListener(OnEditButtonPressed);

            if (saveButton != null)
                saveButton.onClick.AddListener(OnSaveButtonPressed);

            if (nameInputField != null)
                nameInputField.onEndEdit.AddListener(OnInputEndEdit);
        }

        private void OnDisable()
        {
            if (editButton != null)
                editButton.onClick.RemoveListener(OnEditButtonPressed);

            if (saveButton != null)
                saveButton.onClick.RemoveListener(OnSaveButtonPressed);

            if (nameInputField != null)
                nameInputField.onEndEdit.RemoveListener(OnInputEndEdit);
        }

        // ============================================================
        // İSİM
        // ============================================================

        private void RefreshNameFromPlayerData()
        {
            if (nameInputField != null)
                nameInputField.SetTextWithoutNotify(PlayerData.PlayerName);
        }

        private void OnEditButtonPressed()
        {
            SetEditingEnabled(true);

            nameInputField.Select();
            nameInputField.ActivateInputField();

            if (selectAllOnEdit)
            {
                nameInputField.caretPosition = nameInputField.text.Length;
                nameInputField.selectionAnchorPosition = 0;
                nameInputField.selectionFocusPosition = nameInputField.text.Length;
            }

            AudioEvents.PlayButtonClick();
        }

        private void OnInputEndEdit(string value)
        {
            CommitProfile();
        }

        private void SetEditingEnabled(bool isEnabled)
        {
            if (nameInputField == null) return;

            nameInputField.interactable = isEnabled;

            if (!isEnabled)
                nameInputField.DeactivateInputField();
        }

        // ============================================================
        // AVATAR
        // ============================================================

        /// <summary>Grid'de bir avatara tıklanınca — sadece ÖNİZLEME/seçim değişir, PlayerData henüz güncellenmez.</summary>
        /// <summary>Grid'de bir avatara tıklanınca — sadece ÖNİZLEME/seçim değişir, PlayerData henüz güncellenmez.</summary>
        private void OnAvatarButtonClicked(int index)
        {
            if (index == selectedAvatarIndex) return;

            // Eski seçili olanın frame/checkmark'ını KAPAT.
            SetHighlightActive(selectedAvatarIndex, false);

            selectedAvatarIndex = index;

            // Yeni seçilenin frame/checkmark'ını AÇ.
            SetHighlightActive(selectedAvatarIndex, true);

            RefreshPreviewImage();
            AudioEvents.PlayButtonClick();
        }

        private void SetHighlightActive(int index, bool isActive)
        {
            if (index < 0) return;

            if (index < selectedFrames.Count && selectedFrames[index] != null)
                selectedFrames[index].SetActive(isActive);

            if (index < selectedCheckmarks.Count && selectedCheckmarks[index] != null)
                selectedCheckmarks[index].SetActive(isActive);
        }

        /// <summary>
        /// Panel her açıldığında (OnEnable) TÜM grid'i PlayerData.PlayerAvatarIndex'e
        /// göre baştan kurmak için kullanılır — sadece selectedAvatarIndex'teki
        /// AÇIK, geri kalan HEPSİ KAPALI olacak şekilde tüm listeyi gezer.
        /// (Tıklama anında bu metod ARTIK çağrılmıyor — orada SetHighlightActive
        /// ile sadece değişen iki eleman güncelleniyor, performans için.)
        /// </summary>
        private void RefreshAvatarHighlights()
        {
            for (int i = 0; i < selectedFrames.Count; i++)
            {
                bool isSelected = i == selectedAvatarIndex;

                if (selectedFrames[i] != null)
                    selectedFrames[i].SetActive(isSelected);

                if (i < selectedCheckmarks.Count && selectedCheckmarks[i] != null)
                    selectedCheckmarks[i].SetActive(isSelected);
            }
        }

        private void RefreshPreviewImage()
        {
            if (previewAvatarImage == null || avatarDatabase == null) return;

            Sprite sprite = avatarDatabase.GetSprite(selectedAvatarIndex);
            if (sprite != null)
                previewAvatarImage.sprite = sprite;
        }

        // ============================================================
        // SAVE — isim + avatar TEK seferde kaydedilir
        // ============================================================

        private void OnSaveButtonPressed()
        {
            CommitProfile();
            AudioEvents.PlayButtonClick();
        }

        private void CommitProfile()
        {
            if (nameInputField != null)
            {
                // PlayerData.PlayerName setter'ı zaten boş/whitespace değerleri
                // DefaultPlayerName'e çeviriyor ve maksimum uzunluğu kırpıyor.
                PlayerData.PlayerName = nameInputField.text;

                // Sanitize edilmiş nihai değeri alana geri yansıt.
                nameInputField.SetTextWithoutNotify(PlayerData.PlayerName);
            }

            // Avatar index'i kaydet — değiştiyse PlayerAvatarIndexChanged
            // tetiklenir, HomeAvatarIcon buna abone olup kendini günceller.
            PlayerData.PlayerAvatarIndex = selectedAvatarIndex;

            SetEditingEnabled(false);
        }
    }
}