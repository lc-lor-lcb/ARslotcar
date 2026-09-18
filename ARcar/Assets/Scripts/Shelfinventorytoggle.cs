using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 左手コントローラーの人差し指トリガーを押している間だけ、
    /// 配下のパーツ棚(複製元パーツ一式をまとめた親オブジェクト)を表示・操作可能にする。
    /// 離すと非表示・操作不可になる。
    ///
    /// 使い方: 棚のパーツ(PartShelfSlot付き)を全部、空の親GameObject(shelfRoot)の子にまとめる。
    /// このスクリプトは、その親とは別の常時アクティブなオブジェクトに付ける
    /// (shelfRoot自身に付けると、非表示にした瞬間に監視処理も止まってしまい再表示できなくなるため)。
    /// </summary>
    public class ShelfInventoryToggle : MonoBehaviour
    {
        [Tooltip("パーツ棚一式をまとめた親オブジェクト")]
        [SerializeField] private Transform shelfRoot;

        [Tooltip("トリガーをどれだけ押し込んだら「表示」とみなすか(0〜1)")]
        [Range(0.05f, 1f)]
        [SerializeField] private float pressThreshold = 0.5f;

        private void Update()
        {
            if (shelfRoot == null) return;

            bool pressed = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) >= pressThreshold;
            if (shelfRoot.gameObject.activeSelf != pressed)
            {
                shelfRoot.gameObject.SetActive(pressed);
            }
        }
    }
}