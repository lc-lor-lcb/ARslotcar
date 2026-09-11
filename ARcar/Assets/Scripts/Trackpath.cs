using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// パーツ内部で車が通る経路をワールド座標の点列として提供するコンポーネント。
    /// TrackPieceと同じGameObjectに付ける想定。
    ///
    /// - AutoBezier: 2つのコネクタの位置と向き(forward)だけから自動でベジェ曲線を計算する。
    ///   直線・カーブ・坂など、経路の始点と終点+その向きだけで自然な形状になるパーツ向け。
    ///   手動設定は不要。
    /// - Manual: ループなど、2点+接線だけでは再現できない形状のパーツ向け。
    ///   経路上の通過点を表す空オブジェクトを EntryConnector→ExitConnector の順に
    ///   ManualWaypoints へ登録して使う(ConnectorStart/Endと同じ「子オブジェクトを置く」感覚)。
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
            if (Mode == PathMode.Manual && EntryConnector != null && ExitConnector != null)
            {
                return GetManualPathPoints(from);
            }

            return GetAutoBezierPoints(from, to);
        }

        private List<Vector3> GetAutoBezierPoints(TrackConnector from, TrackConnector to)
        {
            var points = new List<Vector3>();
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

            for (int i = 0; i <= resolution; i++)
            {
                float t = i / (float)resolution;
                points.Add(EvaluateCubicBezier(p0, p1, p2, p3, t));
            }

            return points;
        }

        private List<Vector3> GetManualPathPoints(TrackConnector from)
        {
            var points = new List<Vector3>();
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
            return points;
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

            var points = GetWorldPathPoints(connectors[0], connectors[1]);
            Gizmos.color = Color.yellow;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Gizmos.DrawLine(points[i], points[i + 1]);
            }
        }
    }
}