using UnityEngine;
using Oculus.Interaction;

namespace ARSlotcar
{
    /// <summary>
    /// パーツ棚に置く「補充されるスロット」。
    /// このコンポーネントが付いたパーツを掴む(Select)と、
    /// - 掴んだ瞬間、同じ場所に自分自身のコピーを1個生成する(これが次の在庫になる)
    /// - 自分自身(今まさに掴まれている方)からはこのコンポーネントを外す
    ///   (以降はただの通常パーツとして扱われ、再度掴んでも複製されない)
    ///
    /// 使い方: 棚に置くパーツ(TrackPieceなどが付いた通常のパーツ)に、
    /// このコンポーネントも追加するだけ。プレハブを別途用意しなくても、
    /// このオブジェクト自身をテンプレートとして複製する。
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class PartShelfSlot : MonoBehaviour
    {
        [Tooltip("複製に使うプレハブ。未設定の場合は、このオブジェクト自身を複製する")]
        [SerializeField] private GameObject piecePrefabOverride;

        private Grabbable grabbable;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
        }

        private void OnEnable()
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void OnDisable()
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            if (evt.Type != PointerEventType.Select) return;

            GameObject template = piecePrefabOverride != null ? piecePrefabOverride : gameObject;
            // 親(ShelfRootなど)を明示的に引き継ぐことで、複製した先(次の在庫)も棚の表示/非表示の対象になり続けるようにする
            Instantiate(template, transform.position, transform.rotation, transform.parent);

            // 今掴まれている方(=これから持ち出される実体)は棚の階層から独立させる。
            // これをしないと、コースに置いた後のパーツまで、棚を非表示にするたびに巻き添えで消えてしまう。
            transform.SetParent(null, true);

            // 今掴まれている方は、もう棚のテンプレートではなく通常のパーツとして扱う
            Destroy(this);
        }
    }
}