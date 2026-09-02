using UnityEngine;
using Oculus.Interaction;

namespace ARSlotcar
{
    /// <summary>
    /// トラックパーツ本体。
    /// Meta XR Interaction SDKの Grabbable が発火する PointerEvent(Select/Unselect等)を購読し、
    /// 「離した瞬間(Unselect)」に近傍の空きコネクタを探してスナップさせる。
    ///
    /// 前提:
    /// - このGameObjectには Building Blocks の「Add Grab Interaction」ウィザードで
    ///   Grabbable コンポーネントが追加済みであること
    /// - 子オブジェクトとして TrackConnector を1つ以上持つこと(端に配置)
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class TrackPiece : MonoBehaviour
    {
        [SerializeField] private TrackConfig config;

        private Grabbable grabbable;
        private TrackConnector[] connectors;

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
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            // 複数の指/複数インタラクターで掴んでいる場合、最後の1本が離れた時だけ判定したいが、
            // 今回はシンプルに「Unselectイベントを受け取ったら毎回試みる」実装にしている。
            if (evt.Type == PointerEventType.Unselect)
            {
                TrySnap();
            }
        }

        /// <summary>
        /// 自分の各コネクタについて、検知半径内・向きがほぼ逆・同じTypeの
        /// 最も近い相手コネクタを探し、見つかればそこにスナップする。
        /// </summary>
        private void TrySnap()
        {
            if (config == null || connectors == null || connectors.Length == 0) return;

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

                    // 向きがほぼ逆(180度)であることを確認
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
                    SnapTo(myConnector, best);
                    return; // 1回のリリースで1箇所だけ接続する(今回のシンプル版の割り切り)
                }
            }
        }

        /// <summary>
        /// myConnectorがtargetにちょうど重なるように、ピース全体(transform)を移動・回転させる。
        /// </summary>
        private void SnapTo(TrackConnector myConnector, TrackConnector target)
        {
            // 向きを合わせる: myConnectorのforwardが、targetのforwardの真逆を向くように回転
            Quaternion rotationDelta = Quaternion.FromToRotation(myConnector.transform.forward, -target.transform.forward);
            transform.rotation = rotationDelta * transform.rotation;

            // 位置を合わせる: myConnectorの位置がtargetの位置に一致するように平行移動
            Vector3 positionDelta = target.transform.position - myConnector.transform.position;
            transform.position += positionDelta;

            // TODO(保留事項): 同じ箇所への再接続防止、接続済みペアの記録(周回コース判定・ラップ計測に必要)
            //                 は次のステップで実装する
        }
    }
}
