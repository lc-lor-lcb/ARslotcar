using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 右手コントローラーの人差し指トリガー(0.0〜1.0)の押し込み量を、
    /// そのまま CarController.ThrottleInput に渡す。
    ///
    /// OVRInputはMeta XR SDKの標準入力API
    /// (参照: https://developers.meta.com/horizon/documentation/unity/unity-ovrinput )。
    /// Meta XR Simulator上でもシミュレーターがOVRInputの値をエミュレートしてくれるため、
    /// 実機が無くてもこのスクリプトのままSimulatorで動作確認できる。
    ///
    /// グリップ(握る)側はパーツを掴む操作に既に使っているため、
    /// 操作が被らないよう人差し指トリガー側をスロットルに割り当てている。
    /// </summary>
    public class ControllerThrottleInput : MonoBehaviour
    {
        [SerializeField] private CarController carController;

        private void Update()
        {
            if (carController == null) return;
            carController.ThrottleInput = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
        }
    }
}