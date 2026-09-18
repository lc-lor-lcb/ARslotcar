using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;

namespace ARSlotcar
{
    /// <summary>
    /// シーン内の左右のHandGrabInteractor(素手用・コントローラー用の両方の派生を含む)を
    /// 起動時に自動検出し、PointerEvent.Identifier(発生元インタラクターを一意に示す値)と
    /// 照合することで、「どちらの手が発生させたPointerEventか」を判定できるようにする。
    ///
    /// 片手につき複数種類のInteractor(素手トラッキング用のHandGrabInteractor、
    /// コントローラー用のControllerHandGrabInteractorなど)が存在するため、
    /// 片側1個だけでなく、同じ側のIDを全部まとめて記録する。
    ///
    /// Inspectorでの手動割り当ては不要。使い方: シーン内のどこか1箇所にこのスクリプトを置くだけ。
    /// </summary>
    public class HandednessRegistry : MonoBehaviour
    {
        public static HandednessRegistry Instance { get; private set; }

        private readonly HashSet<int> leftIdentifiers = new HashSet<int>();
        private readonly HashSet<int> rightIdentifiers = new HashSet<int>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Awake()ではなくStart()で検出する: HandGrabInteractor自身のHandはそのオブジェクトの
            // Awake()内で設定されるため、実行順序次第ではAwake()時点でまだnullな場合がある。
            // 全オブジェクトのAwake()完了が保証されているStart()まで待つことで確実に取得する。
            //
            // FindObjectsByType<HandGrabInteractor> は、ControllerHandGrabInteractorなどの
            // 派生クラスも一緒に拾う(型階層に含まれるため)。
            var interactors = FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                if (interactor.Hand == null)
                {
                    Debug.LogWarning($"[HandednessRegistry] {interactor.name}({interactor.GetType().Name}) のHandが未設定のためスキップしました。", interactor);
                    continue;
                }

                if (interactor.Hand.Handedness == Handedness.Left)
                {
                    leftIdentifiers.Add(interactor.Identifier);
                    Debug.Log($"[HandednessRegistry] 左手として検出: {interactor.name}({interactor.GetType().Name}), Id={interactor.Identifier}");
                }
                else if (interactor.Hand.Handedness == Handedness.Right)
                {
                    rightIdentifiers.Add(interactor.Identifier);
                    Debug.Log($"[HandednessRegistry] 右手として検出: {interactor.name}({interactor.GetType().Name}), Id={interactor.Identifier}");
                }
            }

            if (leftIdentifiers.Count == 0) Debug.LogWarning("[HandednessRegistry] 左手のInteractorが1つも見つかりませんでした。", this);
            if (rightIdentifiers.Count == 0) Debug.LogWarning("[HandednessRegistry] 右手のInteractorが1つも見つかりませんでした。", this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>指定のPointerEvent.Identifierが左手由来ならtrue、右手由来ならfalse、
        /// どちらとも一致しない場合はnullを返す。</summary>
        public bool? IsLeftHand(int pointerEventIdentifier)
        {
            if (leftIdentifiers.Contains(pointerEventIdentifier)) return true;
            if (rightIdentifiers.Contains(pointerEventIdentifier)) return false;
            return null;
        }
    }
}