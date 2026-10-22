using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// パーツ内部で車が通る経路を、ワールド座標の点列として提供するコンポーネント。
    /// TrackPieceと同じGameObjectに付ける想定。
    ///
    /// - AutoBezier: 2つのコネクタの位置と向き(forward)だけから自動でベジェ曲線を計算する。
    ///   直線・カーブ・坂など、経路の始点と終点+その向きだけで自然な形状になるパーツ向け。
    ///   手動設定は不要。
    /// - Manual: ループなど、2点+接線だけでは再現できない形状のパーツ向け。
    ///   経路上の通過点を表す空オブジェクトを EntryConnector→ExitConnector の順に
    ///   ManualWaypoints へ登録して使う(ConnectorStart/Endと同じ「子オブジェクトを置く」感覚)。
    ///
    /// 経路の各点には、位置のほかに「コース面の上方向」(車体の傾き=バンク・勾配に使う)も持たせる。
    /// AutoBezierでは、両端のコネクタの上方向(transform.up)を滑らかに補間して求める。
    /// Manualでは途中の向きを決められない(ループでは車体が上下逆さまになる区間がある)ため、
    /// 「不明」(Vector3.zero)にして、車側で向きを連続させて求める。
    /// </summary>
    [RequireComponent(typeof(TrackPiece))]
    public class TrackPath : MonoBehaviour
    {
        public enum PathMode { AutoBezier, Manual }

        [Tooltip("このパーツの経路生成方式。直線/カーブ/坂は AutoBezier、ループなどは Manual")]
        public PathMode Mode = PathMode.AutoBezier;

        [Header("Manualモード用(ループなど)")]
        [Tooltip("経路の並び順の起点として扱うコネクタ")]
        public TrackConnector EntryConnector;
        [Tooltip("経路の並び順の終点として扱うコネクタ")]
        public TrackConnector ExitConnector;
        [Tooltip("EntryConnector→ExitConnectorの順で並べた、経路上の通過点(空オブジェクト)")]
        public Transform[] ManualWaypoints;

        [SerializeField] private TrackConfig config;

        /// <summary>
        /// from側から入ってto側へ抜ける経路を、ワールド座標の点列として返す(fromに近い順、両端を含む)。
        /// </summary>
        public List<Vector3> GetWorldPathPoints(TrackConnector from, TrackConnector to)
        {
            GetWorldPath(from, to, out List<Vector3> points, out _);
            return points;
        }

        /// <summary>
        /// 経路の点列(points)と、各点でのコース面の上方向(ups、pointsと同じ個数)を返す。
        /// upsの要素がVector3.zeroの点は「上方向が不明」を表す。
        /// </summary>
        public void GetWorldPath(TrackConnector from, TrackConnector to, out List<Vector3> points, out List<Vector3> ups)
        {
            if (Mode == PathMode.Manual && EntryConnector != null && ExitConnector != null)
            {
                GetManualPath(from, out points, out ups);
                return;
            }

            GetAutoBezierPath(from, to, out points, out ups);
        }

        private void GetAutoBezierPath(TrackConnector from, TrackConnector to, out List<Vector3> points, out List<Vector3> ups)
        {
            points = new List<Vector3>();
            ups = new List<Vector3>();

            int resolution = config != null ? Mathf.Max(2, config.PathResolution) : 16;
            float handleFactor = config != null ? config.AutoPathHandleFactor : 0.5f;

            Vector3 p0 = from.transform.position;
            Vector3 p3 = to.transform.position;
            float handleLength = Vector3.Distance(p0, p3) * handleFactor;

            // コネクタのforwardは「パーツの外側」を向いている前提(スナップ判定の仕様と同じ)。
            // 入口では-forward(パーツの内側へ向かう向き)、出口では+forward(そのまま外へ抜ける向き)
            // を接線として使うことで、隣のパーツと向きが滑らかに繋がる。
            Vector3 p1 = p0 - from.transform.forward * handleLength;
            Vector3 p2 = p3 - to.transform.forward * handleLength;

            Vector3 upFrom = from.transform.up;
            Vector3 upTo = to.transform.up;

            for (int i = 0; i <= resolution; i++)
            {
                float t = i / (float)resolution;
                points.Add(EvaluateCubicBezier(p0, p1, p2, p3, t));
                // 両端のコネクタの上方向を滑らかに補間して、勾配・バンクの変化を表す
                ups.Add(Vector3.Slerp(upFrom, upTo, t).normalized);
            }
        }

        private void GetManualPath(TrackConnector from, out List<Vector3> points, out List<Vector3> ups)
        {
            points = new List<Vector3>();
            bool reversed = (from == ExitConnector);

            points.Add(from.transform.position);

            if (ManualWaypoints != null)
            {
                if (!reversed)
                {
                    foreach (var wp in ManualWaypoints)
                        if (wp != null) points.Add(wp.position);
                }
                else
                {
                    for (int i = ManualWaypoints.Length - 1; i >= 0; i--)
                        if (ManualWaypoints[i] != null) points.Add(ManualWaypoints[i].position);
                }
            }

            TrackConnector to = reversed ? EntryConnector : ExitConnector;
            points.Add(to.transform.position);

            // Manualは途中の上方向を決められない(ループでは逆さまになる区間がある)ので、全部「不明」にする
            ups = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++) ups.Add(Vector3.zero);
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return (u * u * u) * p0
                 + (3f * u * u * t) * p1
                 + (3f * u * t * t) * p2
                 + (t * t * t) * p3;
        }

        // シーンビューで経路の形を確認しやすくする(AutoBezierのみ簡易プレビュー)
        private void OnDrawGizmosSelected()
        {
            var piece = GetComponent<TrackPiece>();
            if (piece == null) return;
            var connectors = piece.GetConnectors();
            if (connectors == null || connectors.Length < 2) return;

            GetWorldPath(connectors[0], connectors[1], out List<Vector3> points, out List<Vector3> ups);

            Gizmos.color = Color.yellow;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Gizmos.DrawLine(points[i], points[i + 1]);
            }

            // コース面の上方向(車体の傾きの基準)も短い線で表示する
            Gizmos.color = Color.cyan;
            for (int i = 0; i < points.Count; i++)
            {
                if (ups[i].sqrMagnitude < 0.5f) continue;
                Gizmos.DrawLine(points[i], points[i] + ups[i] * 0.05f);
            }
        }
    }
}