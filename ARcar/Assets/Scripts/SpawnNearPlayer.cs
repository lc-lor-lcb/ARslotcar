using System.Collections;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 指定したオブジェクト(例: パーツ棚一式をまとめたShelfRoot)を、プレイヤーの前に移動させる。
    /// 起動後に少し待ってから1回出す(bringOnStart)ほか、BringNow()を呼べばいつでも呼び直せる。
    ///
    /// 起動直後はヘッドセットの位置がまだ正しく取れていない可能性があるため、
    /// 起動後すぐではなく startDelay 秒待ってから位置を計算する。
    /// 位置は頭の位置を基準にした「前方・目線からの相対の高さ」で決めるので、
    /// トラッキング原点の種類(Floor/Eye Level)に左右されない。
    ///
    /// 使い方: ShelfRootとは別の、常時アクティブなオブジェクトにこのスクリプトを付け、
    /// target にShelfRootを割り当てる(ShelfRoot自身に付けると、非表示の間は動かないため)。
    /// 組み立て済みのコースは動かさない(コースのパーツはShelfRootの子ではないため)。
    /// </summary>
    public class SpawnNearPlayer : MonoBehaviour
    {
        [Tooltip("プレイヤーの前に出す対象(例: ShelfRoot)。非表示(非アクティブ)の状態でも移動できる")]
        [SerializeField] private Transform target;

        [Tooltip("プレイヤーの前方に出す距離(メートル)")]
        [SerializeField] private float forwardDistance = 0.6f;

        [Tooltip("目線からの高さ(メートル)。マイナスで目線より下(胸〜机の高さ)")]
        [SerializeField] private float heightOffset = -0.35f;

        [Tooltip("対象の前方向(+Z)がプレイヤーの方を向くように回転させる")]
        [SerializeField] private bool faceThePlayer = true;

        [Tooltip("起動時に1回、自動で呼び出す")]
        [SerializeField] private bool bringOnStart = true;

        [Tooltip("起動してから呼び出すまでの待ち時間(秒)。ヘッドセットの位置が安定するのを待つ")]
        [SerializeField] private float startDelay = 1.5f;

        private IEnumerator Start()
        {
            if (!bringOnStart) yield break;
            yield return new WaitForSeconds(startDelay);
            BringNow();
        }

        /// <summary>今のプレイヤーの前に、対象を移動させる</summary>
        public void BringNow()
        {
            if (target == null)
            {
                Debug.LogWarning("[SpawnNearPlayer] target(移動させる対象)が設定されていません。", this);
                return;
            }

            if (!PlayerView.TryGetHead(out Vector3 head, out Vector3 flatForward)) return;

            Vector3 position = PlayerView.PointInFront(head, flatForward, forwardDistance, heightOffset);
            Quaternion rotation = faceThePlayer
                ? Quaternion.LookRotation(-flatForward, Vector3.up)
                : target.rotation;

            target.SetPositionAndRotation(position, rotation);
            Debug.Log($"[SpawnNearPlayer] {target.name} を、頭の位置{head}の前方{forwardDistance}mへ移動しました。");
        }
    }
}