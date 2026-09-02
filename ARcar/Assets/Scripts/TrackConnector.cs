using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// パーツの端に配置する「接続ポイント」。
    /// Transformのforward(青軸)がコネクタの向き(接続先を探す方向)を表す。
    /// 物理演算(コライダー/OverlapSphere)は使わず、シーン内の全コネクタを
    /// 静的リストで管理して距離・角度をコードで比較する方式にしている。
    /// </summary>
    public class TrackConnector : MonoBehaviour
    {
        public static readonly List<TrackConnector> All = new List<TrackConnector>();

        [Tooltip("このコネクタの種類。異なる種類同士は接続不可")]
        public ConnectorType Type = ConnectorType.Standard;

        /// <summary>このコネクタを持つ親のTrackPiece</summary>
        public TrackPiece OwnerPiece { get; private set; }

        private void Awake()
        {
            OwnerPiece = GetComponentInParent<TrackPiece>();
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        // シーンビューでコネクタの位置と向きを視認しやすくする
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(transform.position, 0.01f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.05f);
        }
    }
}
