using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// プレイヤー(ヘッドセット)の現在の位置・向きを取る共通の部品。
    /// 「自分の前に呼び出す」処理(車のリセット、棚の呼び出し)で共通して使う。
    ///
    /// 頭の位置はMainCameraタグが付いたカメラ(Camera Rig配下のCenterEyeAnchorのカメラ)から取る。
    /// 水平方向の前方向だけを使い、上下を向いていても、前方に出す位置がズレないようにしている。
    /// </summary>
    public static class PlayerView
    {
        /// <summary>頭の位置と、水平方向の前方向(Y成分を除いて正規化)を返す。取得できなければfalse</summary>
        public static bool TryGetHead(out Vector3 position, out Vector3 flatForward)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                position = Vector3.zero;
                flatForward = Vector3.forward;
                Debug.LogWarning("[PlayerView] MainCameraタグのカメラが見つかりません。" +
                                 "Camera Rig配下のCenterEyeAnchorのカメラにMainCameraタグが付いているか確認してください。");
                return false;
            }

            position = cam.transform.position;

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                // ほぼ真上/真下を向いている: 頭の上方向を水平に投影して代用する
                // (下を向いている時は頭の上が前方、上を向いている時は頭の上が後方になる)
                Vector3 up = cam.transform.up;
                up.y = 0f;
                forward = cam.transform.forward.y < 0f ? up : -up;
            }

            flatForward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            return true;
        }

        /// <summary>頭の位置から、前方へdistance・高さheightOffset(目線からの相対)だけ離れた位置を返す</summary>
        public static Vector3 PointInFront(Vector3 headPosition, Vector3 flatForward, float distance, float heightOffset)
        {
            return headPosition + flatForward * distance + Vector3.up * heightOffset;
        }
    }
}