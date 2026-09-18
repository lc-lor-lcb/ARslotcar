using UnityEngine;
using Oculus.Interaction;

namespace ARSlotcar
{
    /// <summary>
    /// 「全パーツ削除」ボタン。既存のGrab Interactionの仕組みをそのまま流用し、
    /// 掴む(Select)と、コース上に置かれている全TrackPieceを一括削除する。
    /// 実際に持ち運ぶボタンというよりは「触れたら発動するスイッチ」として使う想定。
    ///
    /// 棚に置いてあるテンプレート(PartShelfSlotが付いたままのもの)は、
    /// まだコースに置かれた「パーツ」ではないため削除対象から除外する。
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class DeleteAllButton : MonoBehaviour
    {
        private Grabbable grabbable;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
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
            if (evt.Type != PointerEventType.Select) return;

            var allPieces = FindObjectsByType<TrackPiece>(FindObjectsSortMode.None);
            foreach (var piece in allPieces)
            {
                if (piece.GetComponent<PartShelfSlot>() != null) continue; // 棚のテンプレートは消さない
                if (piece.IsStartPiece) continue;                          // スタートピースは消さない
                Destroy(piece.gameObject);
            }
        }
    }
}