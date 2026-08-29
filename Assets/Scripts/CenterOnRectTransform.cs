using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Bu objenin (örn. bir Particle System'in) pozisyonunu, verilen bir
    /// RectTransform'un (örn. Canvas'ın kendisi ya da tam ekran kaplayan
    /// bir panel) DÜNYA UZAYINDAKİ MERKEZİNE eşitler.
    ///
    /// NEDEN: Particle System'in kendi Transform'u Hierarchy'de nereye
    /// sürüklenirse sürüklensin (bir köşede, bir panelin içinde vb.),
    /// bu script sayesinde particle'ın GERÇEK spawn/gravity referans
    /// noktası HER ZAMAN o RectTransform'un tam ortası olur — Gravity
    /// Modifier zaten dünya -Y yönünde aşağı çekiyor, ama "aşağı çekilme"
    /// nereden BAŞLADIĞI (spawn merkezi) artık particle'ın kendi rastgele
    /// pozisyonuna değil, Canvas'ın merkezine bağlı.
    /// </summary>
    public class CenterOnRectTransform : MonoBehaviour
    {
        [Tooltip("Merkezine hizalanacak RectTransform — genelde Canvas'ın kendisi ya da tam ekran kaplayan bir panel.")]
        [SerializeField] private RectTransform targetRect;

        [Tooltip("Her frame güncellensin mi (Canvas boyutu/pozisyonu dinamik değişiyorsa), yoksa sadece bir kez Awake'te mi hizalansın.")]
        [SerializeField] private bool updateEveryFrame = false;

        private void Awake()
        {
            CenterNow();
        }

        private void LateUpdate()
        {
            if (updateEveryFrame)
                CenterNow();
        }

        /// <summary>Dışarıdan da (örn. Play butonuna basılmadan Editor'de test ederken) çağrılabilir.</summary>
        [ContextMenu("Center Now")]
        public void CenterNow()
        {
            if (targetRect == null)
            {
                Debug.LogWarning($"CenterOnRectTransform [{gameObject.name}]: Target Rect atanmamış.", this);
                return;
            }

            // GetWorldCorners: [0]=sol-alt, [1]=sol-üst, [2]=sağ-üst, [3]=sağ-alt
            // (dünya uzayında). Köşegen ortalaması = tam merkez — pivot
            // değeri ne olursa olsun (0.5,0.5 olmasa bile) doğru sonucu verir.
            Vector3[] corners = new Vector3[4];
            targetRect.GetWorldCorners(corners);

            Vector3 center = (corners[0] + corners[2]) * 0.5f;

            transform.position = center;
        }
    }
}