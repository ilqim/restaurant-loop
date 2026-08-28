using TMPro;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Tray, Slot ve QueueSlot gibi objelerin üzerinde duran, içindeki yemek
    /// sayısını gösteren 3D metin etiketi — arkasında bir kapsayıcı görsel
    /// (badge/daire/kare vb.) de yönetir.
    ///
    /// YAPI: Bu script'in bulunduğu obje "billboard root"tur — her frame
    /// kameraya döner (LateUpdate). Text ve Background, bu objenin CHILD'ı
    /// olarak durur; parent döndükçe ikisi de otomatik olarak birlikte döner,
    /// script ikisini ayrı ayrı döndürmekle uğraşmaz.
    ///
    /// ÖNEMLİ — CANVAS KULLANMIYORUZ: Text (TextMeshPro, TextMeshProUGUI
    /// DEĞİL) ve Background (SpriteRenderer) ikisi de normal 3D obje —
    /// sahnedeki diğer her mesh gibi normal derinlik testine (Z-buffer)
    /// tabiler.
    ///
    /// TRAY GİBİ DÖNEN BİR PARENT'A BAĞLIYSA: Tray kendi path'ini takip
    /// etmek için dönmeye devam eder (buna hiç dokunmuyoruz, gerekli).
    /// Bu script her frame LateUpdate'te bu objenin WORLD rotasyonunu
    /// doğrudan kameraya bakacak şekilde ELLE set eder — Tray'in o
    /// frame'deki rotasyonu (Update fazında, coroutine içinde) zaten
    /// kesinleşmiş olduğu için, LateUpdate'teki bu override HER ZAMAN
    /// son sözü söyler ve child'ı görsel olarak sabit/kameraya dönük tutar.
    /// Eğer hâlâ parent ile birlikte dönüyorsa, kesin sebep: facingCamera
    /// hiç bulunamıyor (aşağıdaki override alanını kullan) ya da bu script
    /// hiç çalışmıyor (obje inaktif, ya da yanlış objeye eklenmiş).
    ///
    /// HİYERARŞİ ÖRNEĞİ:
    /// CountLabel (bu script burada, Tray/Slot/QueueSlot'un child'ı)
    ///   ├── Background (SpriteRenderer)
    ///   └── Text (TextMeshPro 3D)
    /// </summary>
    public class WorldSpaceCountLabel : MonoBehaviour
    {
        [Header("Referanslar")]
        [Tooltip("Sayıyı gösteren 3D TextMeshPro (TextMeshProUGUI DEĞİL). Boş bırakılırsa child'larda otomatik aranır.")]
        [SerializeField] private TextMeshPro label;

        [Tooltip("Text'in arkasında duran kapsayıcı görsel (badge/daire/kare vb.). Boş bırakılırsa child'larda otomatik aranır.")]
        [SerializeField] private SpriteRenderer background;

        [Header("Kamera")]
        [Tooltip("EN GÜVENİLİR YÖNTEM: Oyun kameranı buraya ELLE sürükle. Boş bırakırsan Camera.main " +
                 "(sahnedeki kameranın 'MainCamera' tag'ine sahip olmasına bağımlı, kırılgan) denenir, " +
                 "o da bulamazsa sahnedeki ilk kamera kullanılır. Billboard hâlâ çalışmıyorsa/parent ile " +
                 "birlikte dönüyorsa büyük ihtimalle bu üçü de kamerayı bulamıyordur — buraya elle ata.")]
        [SerializeField] private Camera cameraOverride;

        [Tooltip("Açarsan, kamera bulunamadığında Console'a bir kere uyarı basar (teşhis için).")]
        [SerializeField] private bool warnIfCameraMissing = true;

        [Tooltip("Sayı 0 veya altındaysa etiket VE background birlikte tamamen gizlensin mi?")]
        [SerializeField] private bool hideWhenZeroOrLess = true;

        private Transform selfTransform;
        private Camera facingCamera;
        private bool loggedMissingCamera;

        private void Awake()
        {
            selfTransform = transform;

            if (label == null)
                label = GetComponentInChildren<TextMeshPro>(true);

            if (background == null)
                background = GetComponentInChildren<SpriteRenderer>(true);

            if (cameraOverride != null)
                facingCamera = cameraOverride;
        }

        private void LateUpdate()
        {
            if (facingCamera == null)
            {
                facingCamera = Camera.main;

                if (facingCamera == null)
                    facingCamera = FindFirstObjectByType<Camera>();
            }

            if (facingCamera == null)
            {
                if (warnIfCameraMissing && !loggedMissingCamera)
                {
                    loggedMissingCamera = true;
                    Debug.LogWarning(
                        $"WorldSpaceCountLabel [{gameObject.name}]: HİÇBİR kamera bulunamadı " +
                        "(Camera Override boş, Camera.main null, sahnede hiç kamera yok). " +
                        "Billboard çalışmıyor, obje parent'ının rotasyonunu miras alıyor. " +
                        "Çözüm: bu component'in Inspector'ındaki 'Camera Override' alanına " +
                        "oyun kameranı elle sürükle.",
                        this
                    );
                }

                return;
            }

            // Billboard: kamera açılı baktığı için her frame kameraya dönüyor.
            // Bu, parent (Tray) her ne kadar kendi path'i için dönüyor olsa
            // bile bu objenin WORLD rotasyonunu ELLE, en son, LateUpdate'te
            // set eder — parent'ın o frame'deki rotasyonu zaten kesinleşmiş
            // olduğu için bu her zaman geçerli olur.
            selfTransform.rotation = Quaternion.LookRotation(
                selfTransform.position - facingCamera.transform.position,
                Vector3.up
            );
        }

        /// <summary>Etiketi (background dahil) tamamen aktif/pasif yapar — örn. tray konveyördeyken gizlemek için.</summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetCount(int count)
        {
            bool show = !(count <= 0 && hideWhenZeroOrLess);

            if (label != null)
                label.text = show ? count.ToString() : "";

            if (background != null)
                background.enabled = show;
        }

        public void Clear()
        {
            if (label != null)
                label.text = "";

            if (background != null)
                background.enabled = false;
        }
    }
}