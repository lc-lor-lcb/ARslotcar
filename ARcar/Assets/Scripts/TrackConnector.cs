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

        /// <summary>現在接続されている相手のコネクタ(未接続ならnull)</summary>
        public TrackConnector ConnectedTo { get; private set; }

        public bool IsConnected => ConnectedTo != null;

        /// <summary>コネクタの接続状態が変化した(接続または切断された)時に発火する。CarControllerなどが経路の再構築に使う。</summary>
        public static event System.Action OnConnectionsChanged;

        /// <summary>
        /// 2つのコネクタを接続する。どちらかが既に別の相手と接続済みの場合は、
        /// 何もせず false を返す(既存の接続はそのまま保持され、新しい接続は成立しない)。
        /// 両方とも未接続だった場合のみ、相互に接続してtrueを返す。
        /// </summary>
        public static bool Connect(TrackConnector a, TrackConnector b)
        {
            if (a.IsConnected || b.IsConnected)
            {
                Debug.LogWarning($"[TrackConnector] 接続を拒否しました: {a.name} または {b.name} が既に別の相手と接続済みです。", a);
                return false;
            }

            a.ConnectedTo = b;
            b.ConnectedTo = a;
            OnConnectionsChanged?.Invoke();
            return true;
        }

        /// <summary>接続を解除する(相手側の接続情報も一緒に解除する)</summary>
        public void Disconnect()
        {
            if (ConnectedTo == null) return;
            var other = ConnectedTo;
            ConnectedTo = null;
            other.ConnectedTo = null;
            OnConnectionsChanged?.Invoke();
        }

        /// <summary>
        /// パーツ(親)のRendererの境界(Bounds)から、このコネクタのローカルZ位置を自動計算して配置し直す。
        /// 「±0.15を手打ちしたつもりが実は違う値になっていた」というズレを防ぐための補助機能。
        /// Inspectorでこのコンポーネントを右クリック(または右上の三点メニュー) →
        /// 「境界(Bounds)に合わせてZ位置を自動配置」で実行できる。
        ///
        /// 現状は直方体に近い形状(直線パーツなど)向けの簡易実装。カーブ・坂・ループのように
        /// コネクタがパーツの中心軸から外れた位置にあるパーツでは、正しい値にならない場合があるため、
        /// その場合は今まで通り手動で調整するか、Unityの頂点スナップ(ドラッグ中にVキー押しっぱなし)で
        /// メッシュの頂点に直接吸着させる方法が確実。
        /// </summary>
        [ContextMenu("境界(Bounds)に合わせてZ位置を自動配置")]
        private void AutoAlignToBoundsZ()
        {
            var piece = GetComponentInParent<TrackPiece>();
            if (piece == null)
            {
                Debug.LogWarning($"[TrackConnector] {name}: 親にTrackPieceが見つからないため自動配置できません。", this);
                return;
            }

            var renderer = piece.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"[TrackConnector] {name}: パーツ内にRendererが見つからないため自動配置できません。", this);
                return;
            }

            // ワールド空間のAABB(renderer.bounds)の8頂点を、パーツのローカル空間に変換して
            // ローカルZの最小・最大を求める(直方体形状であれば正確、斜め形状は近似値になる)
            Bounds worldBounds = renderer.bounds;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = worldBounds.center + Vector3.Scale(worldBounds.extents, new Vector3(x, y, z));
                Vector3 localCorner = piece.transform.InverseTransformPoint(worldCorner);
                minZ = Mathf.Min(minZ, localCorner.z);
                maxZ = Mathf.Max(maxZ, localCorner.z);
            }

            // 現在このコネクタがプラス側・マイナス側どちらにあるかは維持したまま、端の値に合わせる
            Vector3 local = transform.localPosition;
            bool onPositiveSide = local.z >= 0f;
            local.z = onPositiveSide ? maxZ : minZ;
            transform.localPosition = local;

            Debug.Log($"[TrackConnector] {name} をローカルZ={local.z:F4}に自動配置しました。", this);
        }

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