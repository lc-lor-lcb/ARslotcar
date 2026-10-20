using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 左コントローラーのスティックを押し込み続けると(長押し)、車を手元に呼び戻し、
    /// 必要ならパーツ棚も自分の前に呼び直す。車やコースの場所が分からなくなった時のリセット用。
    ///
    /// OVRInput.Button.PrimaryThumbstick は左コントローラーのスティックの押し込みを指す
    /// (公式: Primary = 左コントローラー)。誤操作を避けるため、holdSeconds秒の長押しで発動する。
    /// 組み立て済みのコースは動かさない。
    /// </summary>
    public class SummonInput : MonoBehaviour
    {
        [Tooltip("手元に呼び戻す車")]
        [SerializeField] private CarController car;

        [Tooltip("あわせて自分の前に呼び直すパーツ棚(任意)。SpawnNearPlayerを割り当てる")]
        [SerializeField] private SpawnNearPlayer shelfSpawner;

        [Tooltip("押し込み続ける時間(秒)。誤操作を避けるための長押し")]
        [SerializeField] private float holdSeconds = 0.5f;

        private float heldTime;
        private bool fired;

        private void Update()
        {
            if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick))
            {
                heldTime += Time.deltaTime;
                if (!fired && heldTime >= holdSeconds)
                {
                    fired = true; // 押し続けている間に、何度も発動しないようにする
                    Summon();
                }
            }
            else
            {
                heldTime = 0f;
                fired = false;
            }
        }

        private void Summon()
        {
            if (car != null) car.ResetToPlayer();
            if (shelfSpawner != null) shelfSpawner.BringNow();
        }
    }
}