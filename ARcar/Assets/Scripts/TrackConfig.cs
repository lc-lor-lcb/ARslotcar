using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// トラック全体の数値パラメータを一元管理するScriptableObject。
    /// 実機テストしながら調整できるよう、コード側にマジックナンバーを埋め込まず
    /// このアセットだけを見れば値を把握・変更できるようにする。
    /// </summary>
    [CreateAssetMenu(fileName = "TrackConfig", menuName = "ARSlotcar/Track Config")]
    public class TrackConfig : ScriptableObject
    {
        [Header("グリッド")]
        [Tooltip("1ユニットのサイズ(メートル)。仕様上は10cm = 0.1m")]
        public float GridUnit = 0.1f;

        [Header("コネクタ / スナップ")]
        [Tooltip("スナップが発生する検知半径(メートル)。仕様上は5〜10cm")]
        [Range(0.05f, 0.10f)]
        public float SnapRadius = 0.08f;

        [Tooltip("コネクタ同士の向きが「ほぼ逆(180度)」とみなす角度の許容誤差(度)")]
        [Range(1f, 45f)]
        public float SnapAngleTolerance = 20f;
    }
}
