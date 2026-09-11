using UnityEngine;
using Oculus.Interaction;

namespace ARSlotcar
{
    /// <summary>
    /// トラックパーツ本体。
    /// Meta XR Interaction SDKの Grabbable が発火する PointerEvent(Select/Unselect等)を購読し、
    /// 「離した瞬間(Unselect)」に近傍の空きコネクタを探してスナップさせる。
    ///
    /// 掴んでいる間(Select〜Unselectの間)、毎フレーム「今離したらどこに繋がるか」を判定し、
    /// その位置・向きに半透明のゴースト(分身)を表示する。
    ///
    /// 【向きの決め方】
    /// 接続時の回転は「自分のコネクタの前方 = 相手コネクタの前方の逆向き」という条件だけでは
    /// ロール方向(前方軸まわりの回転)が一意に決まらないため、以下のルールで固定する:
    ///   - 前方(forward)は相手コネクタの逆向きに完全固定(自由度なし)
    ///   - 上方向(up)は「相手コネクタのUpと同じ」か「その180度反転」の2択のみ
    ///   - どちらを選ぶかは、現在手で持っている向き(transform.up)に近い方を採用する
    ///     → 手首を180度ひねると、採用される上方向が切り替わる(=表裏反転)
    /// これにより、前方向で1軸、上方向の選択でもう1軸が固定され、
    /// 実質「1軸のみ180度刻みで切り替わる」動きになる。
    ///
    /// 【位置の決め方】
    /// ゴースト/スナップ後の位置は「自分のコネクタのローカルオフセット(パーツ原点からの相対位置)」
    /// を使って逆算する。myConnector.transform.position(ワールド座標)を直接使うと、
    /// 手を動かしただけでコネクタのワールド座標も変わってしまい、それに引きずられて
    /// プレビュー位置が動いてしまうため、ローカルオフセット基準にすることで
    /// 「相手コネクタの位置」だけを基準にした安定した位置になる。
    ///
    /// 前提:
    /// - このGameObjectには Building Blocks の「Add Grab Interaction」ウィザードで
    ///   Grabbable コンポーネントが追加済みであること
    /// - 子オブジェクトとして TrackConnector を1つ以上持つこと(端に配置)
    ///   ※ 名前は「ConnectorStart / ConnectorEnd」でなくても良い。役割の区別はコード上は無く、
    ///     単なる識別用の名前。Type が一致し、向きがほぼ逆であれば Start-Start, End-End,
    ///     Start-End のどの組み合わせでも接続可能。
    /// - Ghost Material に、半透明(Surface Type = Transparent)のマテリアルを割り当てること
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class TrackPiece : MonoBehaviour
    {
        [SerializeField] private TrackConfig config;
        [SerializeField] private Material ghostMaterial;

        private Grabbable grabbable;
        private TrackConnector[] connectors;
        private bool isHeld;
        private GameObject ghost;

        /// <summary>スナップ候補(自分側のコネクタと、接続先の相手コネクタのペア)</summary>
        private struct SnapCandidate
        {
            public TrackConnector My;
            public TrackConnector Target;
        }

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            connectors = GetComponentsInChildren<TrackConnector>();

            if (config == null)
            {
                Debug.LogWarning($"[TrackPiece] {name} に TrackConfig が設定されていません。Inspectorで割り当ててください。", this);
            }
        }

        private void OnEnable()
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void OnDisable()
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            isHeld = false;
            HideGhost();
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    isHeld = true;
                    break;

                case PointerEventType.Unselect:
                    isHeld = false;
                    if (TryFindSnapCandidate(out SnapCandidate candidate))
                    {
                        var (rotation, position) = ComputeSnapPose(candidate.My, candidate.Target);
                        transform.SetPositionAndRotation(position, rotation);
                    }
                    HideGhost();
                    break;
            }
        }

        private void Update()
        {
            if (!isHeld) return;

            if (TryFindSnapCandidate(out SnapCandidate candidate))
            {
                var (rotation, position) = ComputeSnapPose(candidate.My, candidate.Target);
                ShowGhostAt(position, rotation);
            }
            else
            {
                HideGhost();
            }
        }

        /// <summary>
        /// 自分の各コネクタについて、検知半径内・向きがほぼ逆・同じTypeの
        /// 最も近い相手コネクタを探す。見つかった場合はtrueを返す(実際の移動は行わない)。
        /// </summary>
        private bool TryFindSnapCandidate(out SnapCandidate candidate)
        {
            candidate = default;
            if (config == null || connectors == null || connectors.Length == 0) return false;

            foreach (var myConnector in connectors)
            {
                TrackConnector best = null;
                float bestDist = config.SnapRadius;

                foreach (var other in TrackConnector.All)
                {
                    if (other == myConnector) continue;
                    if (other.OwnerPiece == this) continue;               // 自分自身のパーツは除外
                    if (other.Type != myConnector.Type) continue;         // 種類が違えば接続不可

                    float dist = Vector3.Distance(myConnector.transform.position, other.transform.position);
                    if (dist > config.SnapRadius) continue;

                    float angle = Vector3.Angle(myConnector.transform.forward, other.transform.forward);
                    if (angle < 180f - config.SnapAngleTolerance) continue;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = other;
                    }
                }

                if (best != null)
                {
                    candidate = new SnapCandidate { My = myConnector, Target = best };
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// myConnector を target にちょうど重ねるための、ピース全体の最終的な回転・位置を計算する。
        /// 前方は完全固定、上方向は2択(180度違い)のみという制約付き。
        /// 手の位置・向きを直接使わず、ローカルオフセット/ローカル回転差分という
        /// 「パーツに固有の不変な値」を経由することで、手のジッターに影響されない結果にする。
        /// </summary>
        private (Quaternion rotation, Vector3 position) ComputeSnapPose(TrackConnector myConnector, TrackConnector target)
        {
            // 前方は相手の逆向きに完全固定
            Vector3 desiredForward = -target.transform.forward;

            // 上方向は「相手のUpと同じ」か「その180度反転」かの2択。
            // 現在の手の傾き(transform.up)に近い方を採用する。
            Vector3 upOptionA = target.transform.up;
            Vector3 upOptionB = -target.transform.up;
            Vector3 desiredUp = Vector3.Dot(transform.up, upOptionA) >= Vector3.Dot(transform.up, upOptionB)
                ? upOptionA
                : upOptionB;

            Quaternion targetOrientation = Quaternion.LookRotation(desiredForward, desiredUp);

            // myConnectorの「ピースに対する相対回転」(パーツ固有・手の向きに依存しない値)
            Quaternion myConnectorLocalRot = Quaternion.Inverse(transform.rotation) * myConnector.transform.rotation;
            Quaternion finalRotation = targetOrientation * Quaternion.Inverse(myConnectorLocalRot);

            // myConnectorの「ピースに対する相対位置」(同じくパーツ固有の不変値)
            Vector3 localOffset = transform.InverseTransformPoint(myConnector.transform.position);
            Vector3 finalPosition = target.transform.position - finalRotation * localOffset;

            return (finalRotation, finalPosition);
        }

        // ---- ここからゴースト(半透明プレビュー)関連 ----

        private void EnsureGhostCreated()
        {
            if (ghost != null) return;

            ghost = Instantiate(gameObject, transform.position, transform.rotation);
            ghost.name = name + "_Ghost";

            // 機能コンポーネントは全部消して、見た目だけの分身にする
            Destroy(ghost.GetComponent<TrackPiece>());
            var ghostGrabbable = ghost.GetComponent<Grabbable>();
            if (ghostGrabbable != null) Destroy(ghostGrabbable);
            var ghostRigidbody = ghost.GetComponent<Rigidbody>();
            if (ghostRigidbody != null) Destroy(ghostRigidbody);
            foreach (var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var conn in ghost.GetComponentsInChildren<TrackConnector>()) Destroy(conn);

            // 半透明マテリアルに差し替え
            if (ghostMaterial != null)
            {
                foreach (var renderer in ghost.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial = ghostMaterial;
                }
            }

            ghost.SetActive(false);
        }

        private void ShowGhostAt(Vector3 position, Quaternion rotation)
        {
            EnsureGhostCreated();
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(position, rotation);
        }

        private void HideGhost()
        {
            if (ghost != null) ghost.SetActive(false);
        }
    }
}