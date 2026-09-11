using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 検証用の仮スロットル入力。スペースキーを押している間 ThrottleInput = 1、離すと 0 にする。
    /// 実際のMeta XRコントローラーのボタン入力に差し替えるまでの、一時的な動作確認用スクリプト。
    ///
    /// もしスペースキーを押しても反応しない場合は、
    /// Edit > Project Settings > Player > Active Input Handling が
    /// 「Input Manager (Old)」または「Both」になっているか確認してください
    /// (Meta XR SDK導入時に「Input System Package (New)」のみに変更されている場合があります)。
    /// </summary>
    public class DebugThrottleInput : MonoBehaviour
    {
        [SerializeField] private CarController carController;
        [SerializeField] private KeyCode throttleKey = KeyCode.Space;

        private void Update()
        {
            if (carController == null) return;
            carController.ThrottleInput = Input.GetKey(throttleKey) ? 1f : 0f;
        }
    }
}