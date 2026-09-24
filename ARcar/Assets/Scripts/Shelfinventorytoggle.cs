using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// A/B/X/Yいずれかのボタンを押すたびに、配下のパーツ棚(複製元パーツ一式をまとめた親オブジェクト)の
    /// 表示/非表示をトグルする。
    ///
    /// OVRInput.Button.One は右手A・左手Xを、Button.Two は右手B・左手Yを、それぞれ左右まとめて
    /// 検知する(公式のVirtual Mapping)。この2つのGetDownを合わせることで、A/B/X/Yどれを押しても
    /// 反応するようにしている。
    ///
    /// 使い方: 棚のパーツ(PartShelfSlot付き)を全部、空の親GameObject(shelfRoot)の子にまとめる。
    /// このスクリプトは、その親とは別の常時アクティブなオブジェクトに付ける
    /// (shelfRoot自身に付けると、非表示にした瞬間に監視処理も止まってしまい再表示できなくなるため)。
    /// </summary>
    public class ShelfInventoryToggle : MonoBehaviour
    {
        [Tooltip("パーツ棚一式をまとめた親オブジェクト")]
        [SerializeField] private Transform shelfRoot;

        [Tooltip("開始時に棚を表示しておくか")]
        [SerializeField] private bool startVisible = false;

        private void Start()
        {
            if (shelfRoot != null) shelfRoot.gameObject.SetActive(startVisible);
        }

        private void Update()
        {
            if (shelfRoot == null) return;

            if (OVRInput.GetDown(OVRInput.Button.One) || OVRInput.GetDown(OVRInput.Button.Two))
            {
                shelfRoot.gameObject.SetActive(!shelfRoot.gameObject.activeSelf);
            }
        }
    }
}