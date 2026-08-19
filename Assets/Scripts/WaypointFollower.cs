using System.Collections;
using UnityEngine;

namespace RestaurantLoop
{
    /// Bir objeyi GridManager'daki hesaplanmış waypoint listesinde,
    /// hücreden hücreye (discrete) hareket ettirir — spline değil, her
    /// adımda kısa bir tween ile bir sonraki waypoint'e geçer ve tam
    /// olarak orada durur. Servis kontrolü gibi "tam hücrede mi" sorularının
    /// her zaman kesin cevaplanabilmesi için bilerek böyle (spline'da
    /// obje asla tam bir hücrede durmaz, ara pozisyonlarda kalır).
    public class WaypointFollower : MonoBehaviour
    {
        [SerializeField] private GridManager gridManager;
        [Tooltip("Bir waypoint'ten diğerine geçiş süresi (saniye).")]
        [SerializeField] private float stepDuration = 0.3f;
        [Tooltip("Son waypoint'e (Exit) ulaşınca başa dönüp devam etsin mi — şimdilik test için.")]
        [SerializeField] private bool loop = true;

        private int currentIndex;
        private Coroutine moveRoutine;

        void Start()
        {
            if (gridManager == null)
            {
                Debug.LogError("WaypointFollower: Grid Manager atanmamış.");
                enabled = false;
                return;
            }

            if (gridManager.WaypointWorldPositions == null || gridManager.WaypointWorldPositions.Count == 0)
            {
                Debug.LogWarning("WaypointFollower: waypoint listesi boş — Base/Exit ayarlı mı ve path geçerli mi kontrol et.");
                enabled = false;
                return;
            }

            transform.position = gridManager.WaypointWorldPositions[0];
            currentIndex = 0;
            moveRoutine = StartCoroutine(MoveLoop());
        }

        private IEnumerator MoveLoop()
        {
            var waypoints = gridManager.WaypointWorldPositions;

            while (true)
            {
                int nextIndex = currentIndex + 1;

                if (nextIndex >= waypoints.Count)
                {
                    if (!loop)
                    {
                        Debug.Log("WaypointFollower: Exit'e ulaşıldı, hareket durdu.");
                        yield break;
                    }
                    nextIndex = 0; // başa dön — şimdilik test amaçlı basit loop
                }

                yield return StartCoroutine(StepTo(waypoints[nextIndex]));
                currentIndex = nextIndex;
            }
        }

        private IEnumerator StepTo(Vector3 target)
        {
            Vector3 start = transform.position;
            float t = 0f;
            while (t < stepDuration)
            {
                t += Time.deltaTime;
                transform.position = Vector3.Lerp(start, target, Mathf.Clamp01(t / stepDuration));
                yield return null;
            }
            transform.position = target; // tam hücrede durduğunu garanti et
        }
    }
}