using UnityEngine;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;

namespace ARSlotcar
{
    /// <summary>
    /// コントローラーを短く振動させる(パルス)ための部品。
    ///
    /// OVRInput.SetControllerVibration は、一度始めると止めるまで振動し続ける仕様のため
    /// (公式: 止めるには振幅・周波数を0にする)、「少し振動させて、時間が来たら自分で止める」処理を
    /// この部品が受け持つ。周波数は1にしておくのが公式の指定(値を変えても実際の周波数は変わらない)で、
    /// 強さを変えられるのは振幅だけ。
    ///
    /// 最初に使われた時に、見えない管理用オブジェクトを自動で作るので、シーンへの配置は不要。
    /// </summary>
    public class ControllerHaptics : MonoBehaviour
    {
        private static ControllerHaptics instance;
        private static bool pulseLogged;

        private float leftEndTime = -1f;
        private float rightEndTime = -1f;

        /// <summary>
        /// 指定したコントローラーを、duration秒だけ振動させる。LTouch/RTouch以外(Noneなど)は何もしない。
        /// </summary>
        public static void Pulse(OVRInput.Controller controller, float duration, float amplitude)
        {
            if (controller != OVRInput.Controller.LTouch && controller != OVRInput.Controller.RTouch) return;
            if (amplitude <= 0f) return;

            EnsureInstance();
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), controller);

            if (!pulseLogged)
            {
                pulseLogged = true;
                Debug.Log($"[ControllerHaptics] 初回の振動を送信しました: {controller} / 振幅={amplitude}");
            }

            float endTime = Time.unscaledTime + Mathf.Max(0.01f, duration);
            if (controller == OVRInput.Controller.LTouch) instance.leftEndTime = endTime;
            else instance.rightEndTime = endTime;
        }

        /// <summary>
        /// 掴んだ操作(PointerEvent.Identifier)が、左右どちらのコントローラーによるものかを調べる。
        /// 最初の振動の直前に1回だけ呼ぶ想定(シーン内を探すため、毎フレームは呼ばない)。
        ///
        /// 次の順で判定する:
        ///  1. 掴んだ操作を発生させたInteractorを、IDで探す。近くで掴むHandGrabInteractorだけでなく、
        ///     遠くから掴むDistance系など、IInteractorViewを実装しているInteractorなら何でも対象にして、
        ///     そのInteractorの手(左右)を調べる。
        ///  2. 見つからない・左右が分からない場合は、今押されているグリップ/トリガーから推測する
        ///     (片方のコントローラーだけが握られている場合)。
        /// 判定できない場合は Controller.None を返す(振動しない)。
        /// </summary>
        public static OVRInput.Controller ResolveController(int pointerIdentifier)
        {
            // 振動は演出なので、判定中に何か問題が起きても、掴む・置くといった本来の操作を壊さないよう、
            // 例外はここで止めてログに残すだけにする
            try
            {
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var behaviour in behaviours)
                {
                    if (!(behaviour is Oculus.Interaction.IInteractorView view) || view.Identifier != pointerIdentifier) continue;

                    Handedness? handedness = GetHandedness(behaviour);
                    if (handedness == Handedness.Left)
                    {
                        Debug.Log($"[ControllerHaptics] 掴んだ手: 左 (Interactor={behaviour.GetType().Name} / {behaviour.name})");
                        return OVRInput.Controller.LTouch;
                    }
                    if (handedness == Handedness.Right)
                    {
                        Debug.Log($"[ControllerHaptics] 掴んだ手: 右 (Interactor={behaviour.GetType().Name} / {behaviour.name})");
                        return OVRInput.Controller.RTouch;
                    }

                    Debug.Log($"[ControllerHaptics] Interactor({behaviour.GetType().Name} / {behaviour.name})は見つかりましたが、左右を判定できませんでした。入力状態から推測します。");
                    break;
                }

                OVRInput.Controller guessed = GuessFromInput();
                Debug.Log($"[ControllerHaptics] IDで見つからなかったため、入力状態から推測しました: {guessed} (Identifier={pointerIdentifier})");
                return guessed;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ControllerHaptics] 掴んだ手の判定に失敗しました: {e.GetType().Name}: {e.Message}");
                return OVRInput.Controller.None;
            }
        }

        /// <summary>Interactorが、左右どちらの手のものかを調べる。分からなければnull</summary>
        private static Handedness? GetHandedness(MonoBehaviour interactor)
        {
            // 近くで掴むHandGrabInteractor(コントローラー用の派生を含む)
            if (interactor is HandGrabInteractor handGrab && handGrab.Hand != null)
            {
                return handGrab.Hand.Handedness;
            }

            // それ以外のInteractor: 親の階層にある手(IHand)から調べる
            var hand = interactor.GetComponentInParent<IHand>();
            if (hand != null) return hand.Handedness;

            // 最後の手段: 階層の名前に Left / Right のどちらか一方だけが含まれているか
            string path = HierarchyPath(interactor.transform).ToLowerInvariant();
            bool hasLeft = path.Contains("left");
            bool hasRight = path.Contains("right");
            if (hasLeft && !hasRight) return Handedness.Left;
            if (hasRight && !hasLeft) return Handedness.Right;
            return null;
        }

        private static string HierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        /// <summary>片方のコントローラーだけが握られている(グリップかトリガーが半分以上押されている)なら、その側を返す</summary>
        private static OVRInput.Controller GuessFromInput()
        {
            bool left = IsSqueezed(OVRInput.Controller.LTouch);
            bool right = IsSqueezed(OVRInput.Controller.RTouch);

            if (left && !right) return OVRInput.Controller.LTouch;
            if (right && !left) return OVRInput.Controller.RTouch;
            return OVRInput.Controller.None;
        }

        private static bool IsSqueezed(OVRInput.Controller controller)
        {
            // 公式: 個別のコントローラーを指定した場合、Primaryは常にその指定した手を指す
            const float threshold = 0.5f;
            return OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) > threshold
                || OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > threshold;
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;

            var go = new GameObject("ControllerHaptics");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ControllerHaptics>();
        }

        private void Update()
        {
            float now = Time.unscaledTime;

            if (leftEndTime >= 0f && now >= leftEndTime)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                leftEndTime = -1f;
            }
            if (rightEndTime >= 0f && now >= rightEndTime)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                rightEndTime = -1f;
            }
        }

        private void StopAll()
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            leftEndTime = -1f;
            rightEndTime = -1f;
        }

        // アプリが中断された時に、振動が出しっぱなしにならないようにする
        private void OnApplicationPause(bool paused)
        {
            if (paused) StopAll();
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }

    /// <summary>
    /// 「白いゴースト(置ける位置の表示)が更新されるたびに、掴んでいる方のコントローラーを振動させる」ための小さな部品。
    /// ゴーストの位置・向きが、前回振動させた時から一定以上変わった時だけパルスを出す
    /// (毎フレームではなく、動いた分だけ「カチカチ」と手応えが出る。ほぼ止まっている間は振動しない)。
    /// 赤いゴースト(置けない時)や、ゴーストが消えている間は使わず、Clear()を呼ぶ。
    /// </summary>
    public class GhostHapticFeedback
    {
        /// <summary>振動させる側のコントローラー(最初に振動させる時に、掴んだ操作から自動で決める)</summary>
        public OVRInput.Controller Controller = OVRInput.Controller.None;

        private int pointerIdentifier;
        private bool controllerResolved;

        /// <summary>
        /// 掴んだ時に呼ぶ。ここでは何も調べず、掴んだ操作のIDを覚えておくだけにする
        /// (掴んだ瞬間のイベントの中で重い処理や例外を出して、掴む操作を壊さないようにするため)。
        /// どちらの手かは、最初に振動させる時(Notify)に調べる。
        /// </summary>
        public void BeginHold(int pointerEventIdentifier)
        {
            pointerIdentifier = pointerEventIdentifier;
            controllerResolved = false;
            Controller = OVRInput.Controller.None;
            hasLast = false;
        }

        /// <summary>振動の強さ(0〜1)。0なら振動しない</summary>
        public float Amplitude = 0.35f;

        /// <summary>1回のパルスの長さ(秒)</summary>
        public float Duration = 0.04f;

        private const float MinMove = 0.005f; // これ以上(メートル)動いたら「更新された」とみなす
        private const float MinAngle = 2f;    // これ以上(度)回ったら「更新された」とみなす

        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private bool hasLast;

        /// <summary>ゴーストが消えた/赤になった時に呼ぶ。次に白いゴーストが出た時は、必ず1回振動する</summary>
        public void Clear()
        {
            hasLast = false;
        }

        /// <summary>白いゴーストを、この位置・向きに表示した時に呼ぶ。前回から十分に変わっていれば振動させる</summary>
        public void Notify(Vector3 position, Quaternion rotation)
        {
            bool changed = !hasLast
                || (position - lastPosition).sqrMagnitude > MinMove * MinMove
                || Quaternion.Angle(lastRotation, rotation) > MinAngle;
            if (!changed) return;

            // 基準は「振動させた時の位置」。ゆっくり動かし続けても、一定距離ごとに振動する
            lastPosition = position;
            lastRotation = rotation;
            hasLast = true;

            if (!controllerResolved)
            {
                controllerResolved = true;
                Controller = ControllerHaptics.ResolveController(pointerIdentifier);
            }

            ControllerHaptics.Pulse(Controller, Duration, Amplitude);
        }
    }
}