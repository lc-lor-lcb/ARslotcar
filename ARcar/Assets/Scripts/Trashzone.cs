using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// このColliderの範囲にパーツが触れた状態で離されると、そのパーツを削除する「ゴミ箱」エリア。
    /// 使い方: 空のGameObjectにColliderを追加し、Is Triggerを必ずONにしてこのスクリプトを付ける。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TrashZone : MonoBehaviour
    {
        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var piece = other.GetComponentInParent<TrackPiece>();
            if (piece == null) return;
            piece.MarkOverTrash(true);
        }

        private void OnTriggerExit(Collider other)
        {
            var piece = other.GetComponentInParent<TrackPiece>();
            if (piece == null) return;
            piece.MarkOverTrash(false);
        }
    }
}