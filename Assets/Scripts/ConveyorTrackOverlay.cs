using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Spawns discrete, rigid arrow sprites that glide along the conveyor path.
    /// Completely eliminates corner UV warping, pinched miters, and start/end cutoffs.
    /// </summary>
    public class ConveyorTrackOverlay : MonoBehaviour, ILevelDataReceiver
    {
        [Header("References")]
        [Tooltip("Leave empty to auto-find GridManager in the scene.")]
        [SerializeField] private GridManager gridManager;

        [Header("Arrow Visuals")]
        [Tooltip("The arrow / chevron sprite to display along the conveyor.")]
        [SerializeField] private Sprite arrowSprite;

        [Tooltip("Color tint of the arrows.")]
        [SerializeField] private Color arrowColor = Color.white;

        [Tooltip("Uniform size scale of each arrow.")]
        [SerializeField] private Vector2 arrowScale = new Vector2(0.55f, 0.55f);

        [Tooltip("If your arrow sprite points Right by default, set to -90. If it points Up, leave at 0.")]
        [SerializeField] private float spriteRotationOffset = 0f;

        [Header("Track Elevation & Density")]
        [Tooltip("Height elevation above the conveyor surface.")]
        [SerializeField] private float yOffset = 0.05f;

        [Tooltip("Distance in world units between consecutive arrows.")]
        [SerializeField] private float arrowSpacing = 0.75f;

        [Tooltip("Movement speed of the arrows along the track.")]
        [SerializeField] private float scrollSpeed = 1.5f;

        [Header("Trimming (Start & End Offsets)")]
        [Tooltip("Distance in world units to trim from the start (Base side).")]
        [SerializeField] private float startOffsetDistance = 0.6f;

        [Tooltip("Distance in world units to trim from the end (Exit side).")]
        [SerializeField] private float endOffsetDistance = 0.6f;

        [Tooltip("Distance over which arrows smoothly fade in at the start and fade out at the exit.")]
        [SerializeField] private float edgeFadeDistance = 0.35f;

        [Header("Sorting")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int orderInLayer = 5;

        private class ArrowInstance
        {
            public GameObject go;
            public Transform transform;
            public SpriteRenderer renderer;
            public float currentDistance;
        }

        private readonly List<ArrowInstance> arrows = new();
        private List<Vector3> cachedWaypoints = new();
        private float[] cumulativeDistances;
        private float totalTrackLength;
        private float activeStartDist;
        private float activeEndDist;
        private bool isInitialized;

        private void Awake()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();

            // Clean up any old LineRenderer component if present
            var lr = GetComponent<LineRenderer>();
            if (lr != null) Destroy(lr);
        }

        private void Start()
        {
            StartCoroutine(WaitAndBuildTrackRoutine());
        }

        public void SetLevelData(LevelData data)
        {
            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(WaitAndBuildTrackRoutine());
            }
        }

        private IEnumerator WaitAndBuildTrackRoutine()
        {
            yield return null; // Wait for GridManager initialization
            BuildTrack();
        }

        [ContextMenu("Rebuild Track Now")]
        public void BuildTrack()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();

            if (gridManager == null || gridManager.WaypointWorldPositions == null || gridManager.WaypointWorldPositions.Count < 2)
                return;

            cachedWaypoints = new List<Vector3>(gridManager.WaypointWorldPositions);

            // Compute cumulative distances along the path
            cumulativeDistances = new float[cachedWaypoints.Count];
            cumulativeDistances[0] = 0f;
            for (int i = 1; i < cachedWaypoints.Count; i++)
            {
                cumulativeDistances[i] = cumulativeDistances[i - 1] + Vector3.Distance(cachedWaypoints[i - 1], cachedWaypoints[i]);
            }

            totalTrackLength = cumulativeDistances[^1];
            activeStartDist = Mathf.Clamp(startOffsetDistance, 0f, totalTrackLength);
            activeEndDist = Mathf.Clamp(totalTrackLength - endOffsetDistance, activeStartDist + 0.1f, totalTrackLength);

            float playableLength = activeEndDist - activeStartDist;
            if (playableLength <= 0.1f || arrowSpacing <= 0.05f) return;

            // 1. Calculate an exact count of arrows so the loop has zero remainder
            int countNeeded = Mathf.Max(1, Mathf.RoundToInt(playableLength / arrowSpacing));

            // 2. Adjust spacing to precisely divide the track length evenly
            float effectiveSpacing = playableLength / countNeeded;

            // 3. Resize object pool
            while (arrows.Count < countNeeded)
            {
                arrows.Add(CreateArrowObject(arrows.Count));
            }

            while (arrows.Count > countNeeded)
            {
                int last = arrows.Count - 1;
                if (arrows[last].go != null) Destroy(arrows[last].go);
                arrows.RemoveAt(last);
            }

            // 4. Distribute arrows at exact, seamless intervals
            for (int i = 0; i < arrows.Count; i++)
            {
                arrows[i].currentDistance = activeStartDist + (i * effectiveSpacing);
                UpdateArrowTransform(arrows[i]);
            }

            isInitialized = true;
        }

        private ArrowInstance CreateArrowObject(int index)
        {
            var go = new GameObject($"ConveyorArrow_{index}");
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = arrowSprite;
            sr.color = arrowColor;
            sr.sortingLayerName = sortingLayerName;
            sr.sortingOrder = orderInLayer;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            return new ArrowInstance
            {
                go = go,
                transform = go.transform,
                renderer = sr,
                currentDistance = 0f
            };
        }

        private void Update()
        {
            if (!isInitialized) return;

            float playableLength = activeEndDist - activeStartDist;
            if (playableLength <= 0.01f) return;

            float deltaMove = scrollSpeed * Time.deltaTime;

            for (int i = 0; i < arrows.Count; i++)
            {
                var arrow = arrows[i];
                arrow.currentDistance += deltaMove;

                // Clean circular wrap without clustering
                while (arrow.currentDistance >= activeEndDist)
                {
                    arrow.currentDistance -= playableLength;
                }
                while (arrow.currentDistance < activeStartDist)
                {
                    arrow.currentDistance += playableLength;
                }

                UpdateArrowTransform(arrow);
            }
        }

        private void UpdateArrowTransform(ArrowInstance arrow)
        {
            GetPointAndTangentAtDistance(arrow.currentDistance, out Vector3 point, out Vector3 tangent);

            point.y += yOffset;
            arrow.transform.position = point;

            // ÖNEMLİ — ROTASYON DEĞİŞTİ: Eskiden Quaternion.LookRotation(tangent, Vector3.up)
            // ile ek bir Quaternion.Euler(90, offset, 0) ÇARPILIYORDU. Bu iki
            // rotasyonun birleşimi, bazı tangent yönlerinde (özellikle
            // köşelerde ani yön değişimi olduğunda) beklenmedik/garip
            // dönüşlere yol açabiliyordu (LookRotation'ın kendi iç "roll"
            // belirsizliği + üstüne binen sabit rotasyon çakışması —
            // sağ-alt köşedeki "çentik" gibi görünen okun sebebi buydu).
            //
            // Bunun yerine tangent'ten DOĞRUDAN dünya-Y ekseni etrafındaki
            // açıyı (yaw) hesaplayıp, sprite'ı sadece bu TEK, öngörülebilir
            // açıyla döndürüyoruz. X=90 sprite'ı yere yatık tutmak için sabit;
            // Y=hesaplanan açı+offset yatay yönü belirliyor. Artık HERHANGİ
            // bir tangent yönünde (düz segment ya da en keskin köşe fark
            // etmeksizin) ok her zaman GERÇEK hareket yönünü gösteriyor.
            float yawDegrees = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
            arrow.transform.rotation = Quaternion.Euler(90f, yawDegrees + spriteRotationOffset, 0f);

            arrow.transform.localScale = new Vector3(arrowScale.x, arrowScale.y, 1f);

            // Smooth alpha fade at entry and exit
            if (arrow.renderer != null)
            {
                float alpha = 1f;

                if (edgeFadeDistance > 0.01f)
                {
                    float distFromStart = arrow.currentDistance - activeStartDist;
                    float distFromEnd = activeEndDist - arrow.currentDistance;
                    float minEdgeDist = Mathf.Min(distFromStart, distFromEnd);

                    alpha = Mathf.Clamp01(minEdgeDist / edgeFadeDistance);
                }

                Color c = arrowColor;
                c.a *= alpha;
                arrow.renderer.color = c;
            }
        }

        private void GetPointAndTangentAtDistance(float targetDist, out Vector3 position, out Vector3 tangent)
        {
            targetDist = Mathf.Clamp(targetDist, 0f, totalTrackLength);

            for (int i = 0; i < cachedWaypoints.Count - 1; i++)
            {
                if (targetDist >= cumulativeDistances[i] && targetDist <= cumulativeDistances[i + 1])
                {
                    float segLen = cumulativeDistances[i + 1] - cumulativeDistances[i];
                    float t = segLen > 0.0001f ? (targetDist - cumulativeDistances[i]) / segLen : 0f;

                    position = Vector3.Lerp(cachedWaypoints[i], cachedWaypoints[i + 1], t);
                    tangent = (cachedWaypoints[i + 1] - cachedWaypoints[i]).normalized;
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;

                    return;
                }
            }

            position = cachedWaypoints[^1];
            tangent = (cachedWaypoints[^1] - cachedWaypoints[^2]).normalized;
            tangent.y = 0f;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && isInitialized)
            {
                BuildTrack();
            }
        }
#endif
    }
}