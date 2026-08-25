using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    // Build-time aşamasında hesaplanıp Tray'e sunulacak eksen bilgisi
    public enum WaypointMoveAxis
    {
        None,
        Row,
        Col
    }

    [System.Flags]
    public enum AllowedShootDirections
    {
        None = 0,
        PosX = 1 << 0, // +X
        NegX = 1 << 1, // -X
        PosZ = 1 << 2, // +Z
        NegZ = 1 << 3, // -Z
        NormalFacing = 1 << 4 // Köşede değilken normal tepsi yönü
    }

    /// <summary>
    /// Köşeli bir waypoint listesini, her iç köşeyi quadratic Bezier
    /// yayla yumuşatarak "yuvarlak köşeli" bir listeye çevirir — VE aynı
    /// build-time geçişte, her çıktı noktası için bir "facing direction"
    /// (tepsinin bakması gereken yön) üretir.
    ///
    /// FACING MANTIĞI: grid merkeziyle HİÇ ilgisi yok. Her noktanın
    /// facing'i, o noktaya gelen segmentin yönü ile giden segmentin
    /// yönünün DİKİNE (perpendicular) göre hesaplanır. Düz bir uzantıda
    /// gelen/giden yön ~180° zıt olduğu için facingIn ≈ facingOut —
    /// yani facing HİÇ DEĞİŞMİYOR. Sadece gerçek bir köşede gelen/giden
    /// yön gerçekten farklılaştığı için, o köşenin yayı boyunca facing
    /// de facingIn'den facingOut'a yumuşakça (Slerp) dönüyor.
    ///
    /// Hepsi build-time'da (level yüklenirken) BİR KEZ hesaplanır —
    /// runtime'da hiçbir açı/yön hesabı yapılmaz, Tray sadece bu
    /// önceden hazırlanmış listeyi okur.
    /// </summary>
    public static class PathSmoothing
    {
        public static void RoundCorners(
            List<Vector3> positions,
            List<Vector2Int> cells,
            float radius,
            int segmentsPerCorner,
            bool invertFacingSide,
            out List<Vector3> outPositions,
            out List<Vector2Int> outCells,
            out List<Vector3> outFacingDirections,
            out List<AllowedShootDirections> outShootDirs,
            out List<bool> outIsConcave)
        {
            outPositions = new List<Vector3>();
            outCells = new List<Vector2Int>();
            outFacingDirections = new List<Vector3>();
            outShootDirs = new List<AllowedShootDirections>();
            outIsConcave = new List<bool>();

            int count = positions.Count;

            if (count < 2)
            {
                outPositions.AddRange(positions);
                outCells.AddRange(cells);
                for (int i = 0; i < count; i++) outFacingDirections.Add(Vector3.forward);
                return;
            }

            if (count < 3 || radius <= 0f || segmentsPerCorner < 1)
            {
                // Yumuşatma kapalı/yetersiz nokta — pozisyonlar/hücreler
                // olduğu gibi geçer, ama facing'i YİNE DE hesaplıyoruz
                // (rotasyon, cornerRadius=0 olsa bile çalışmalı).
                outPositions.AddRange(positions);
                outCells.AddRange(cells);
                for (int i = 0; i < count ; i++)
                {
                   outFacingDirections.Add(Vector3.forward);
                   outShootDirs.Add(AllowedShootDirections.NormalFacing);
                   outIsConcave.Add(false);
                }
                return;
            }

            // Base (ilk nokta) SABİT kalır — yuvarlatılmaz. Facing'i,
            // ilk segmentin (Base -> ilk köşe) yönünün dikine göre.
            outPositions.Add(positions[0]);
            outCells.Add(cells[0]);
            outFacingDirections.Add(Perp(Dir(positions[0], positions[1]), invertFacingSide));
            outShootDirs.Add(AllowedShootDirections.NormalFacing);
            outIsConcave.Add(false);

            for (int i = 1; i < count - 1; i++)
            {
                Vector3 prev = positions[i - 1];
                Vector3 curr = positions[i];
                Vector3 next = positions[i + 1];

                Vector3 toPrev = prev - curr;
                Vector3 toNext = next - curr;

                float legIn = toPrev.magnitude;
                float legOut = toNext.magnitude;

                Vector3 dirIn = Dir(prev, curr);   // gelen segment yönü
                Vector3 dirOut = Dir(curr, next);  // giden segment yönü
                Vector3 facingIn = Perp(dirIn, invertFacingSide);
                Vector3 facingOut = Perp(dirOut, invertFacingSide);

                float crossZ = (dirIn.x * dirOut.z) - (dirIn.z * dirOut.x);

                bool isConcave = invertFacingSide ? (crossZ > 0.01f) : (crossZ < -0.01f);

                AllowedShootDirections cornerShootDirs = AllowedShootDirections.NormalFacing;

                if (isConcave)
                {
                    cornerShootDirs = VectorToShootDir(dirIn) | VectorToShootDir(-dirOut);
                }

                if (legIn < 0.0001f || legOut < 0.0001f)
                {
                    outPositions.Add(curr);
                    outCells.Add(cells[i]);
                    outFacingDirections.Add(facingOut.sqrMagnitude > 0.0001f ? facingOut : facingIn);
                    outShootDirs.Add(cornerShootDirs);
                    outIsConcave.Add(isConcave);
                    continue;
                }

                // Yarıçap, komşu bacakların yarısından uzun olamaz —
                // bitişik köşelerin yayları birbirine taşmasın.
                float r = Mathf.Min(radius, legIn * 0.5f, legOut * 0.5f);

                Vector3 entry = curr + toPrev.normalized * r;
                Vector3 exit = curr + toNext.normalized * r;

                for (int s = 0; s <= segmentsPerCorner; s++)
                {
                    float t = s / (float)segmentsPerCorner;

                    Vector3 a = Vector3.Lerp(entry, curr, t);
                    Vector3 b = Vector3.Lerp(curr, exit, t);
                    Vector3 point = Vector3.Lerp(a, b, t);

                    outPositions.Add(point);
                    outCells.Add(cells[i]);

                    // Düz uzantıda facingIn≈facingOut -> Slerp sonucu
                    // pratikte SABİT kalır (görünür rotasyon yok).
                    // Gerçek bir köşede facingIn≠facingOut -> yay boyunca
                    // yumuşakça döner.
                    Vector3 facing = Vector3.Slerp(facingIn, facingOut, t);
                    outFacingDirections.Add(facing);

                    outShootDirs.Add(cornerShootDirs);
                    outIsConcave.Add(isConcave);
                }
            }

            // Exit (son nokta) SABİT kalır. Facing'i, son segmentin
            // (son köşe -> Exit) yönünün dikine göre.
            outPositions.Add(positions[count - 1]);
            outCells.Add(cells[count - 1]);
            outFacingDirections.Add(Perp(Dir(positions[count - 2], positions[count - 1]), invertFacingSide));
            outShootDirs.Add(AllowedShootDirections.NormalFacing);
            outIsConcave.Add(false);
        }

        private static AllowedShootDirections VectorToShootDir(Vector3 v)
        {
            if(Mathf.Abs(v.x) > Mathf.Abs(v.z))
            {
                return v.x > 0 ? AllowedShootDirections.PosX : AllowedShootDirections.NegX;
            }
            else
            {
                return v.z > 0 ? AllowedShootDirections.PosZ : AllowedShootDirections.NegZ;
            }
        }

        private static Vector3 Dir(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        /// <summary>
        /// Bir hareket yönünün (XZ düzleminde) 90° döndürülmüş hali —
        /// tepsinin "hangi tarafa" (içeri/dışarı) bakacağını belirler.
        /// invert=false ve invert=true birbirinin tam tersi — level
        /// tasarımına göre hangisinin doğru olduğunu Inspector'dan
        /// (Invert Facing Side) Play'e girmeden gizmo'da test edebilirsin.
        /// </summary>
        private static Vector3 Perp(Vector3 dir, bool invert)
        {
            Vector3 p = invert ? new Vector3(-dir.z, 0f, dir.x) : new Vector3(dir.z, 0f, -dir.x);
            return p.sqrMagnitude > 0.0001f ? p.normalized : Vector3.forward;
        }
    }
}