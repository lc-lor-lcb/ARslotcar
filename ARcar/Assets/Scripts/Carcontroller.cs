using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 構築済みのコース経路(ワールド座標点列)に沿って車を走らせる。
    /// 物理演算は使わず、経路上を進んだ距離(弧長)をパラメータとして位置・向きを直接更新する方式。
    ///
    /// 入力は ThrottleInput(0〜1)のみに抽象化してある。実際のMeta XRコントローラーの
    /// ボタン取得処理はこのスクリプトに含めていない(別スクリプトからこのフィールドをセットする想定)。
    /// Meta XR Simulator上ではデバッグ用に別途キーボード入力スクリプトを繋いで検証できる。
    ///
    /// 未実装(次のステップで対応予定):
    /// - カーブ半径ごとの安全速度判定・コースアウト演出
    /// - ループ区間で車体が上下反転する際の見た目の破綻対策(現状はワールドUp基準でLookRotationしている)
    /// </summary>
    public class CarController : MonoBehaviour
    {
        [Header("経路")]
        [Tooltip("コースの起点となるスタートパーツ")]
        [SerializeField] private TrackPiece startPiece;

        [Header("走行パラメータ")]
        [Tooltip("最大速度(m/s)")]
        [SerializeField] private float maxSpeed = 1.5f;
        [Tooltip("ボタンを押している間の加速度(m/s^2)")]
        [SerializeField] private float acceleration = 2.0f;
        [Tooltip("ボタンを離した際の減速度(m/s^2)。慣性で減速するだけで後退はしない")]
        [SerializeField] private float deceleration = 1.0f;

        /// <summary>0(離した)〜1(全開)。ボタン入力側のスクリプトから毎フレームセットする想定</summary>
        [Range(0f, 1f)]
        public float ThrottleInput;

        private List<Vector3> pathPoints = new List<Vector3>();
        private List<float> cumulativeLengths = new List<float>();
        private bool isLooping;
        private float currentSpeed;
        private float distanceTraveled;
        private float totalPathLength;

        private void Start()
        {
            RebuildPath();
        }

        /// <summary>
        /// コース編集後(パーツのスナップ変更後)に呼び出して経路を再構築する。
        /// ビルドモード→レースモードへ切り替えるタイミングで呼ぶ想定。
        /// </summary>
        public void RebuildPath()
        {
            var result = CoursePathBuilder.Build(startPiece);
            pathPoints = result.Points;
            isLooping = result.IsLooping;
            distanceTraveled = 0f;
            currentSpeed = 0f;

            cumulativeLengths.Clear();
            totalPathLength = 0f;
            cumulativeLengths.Add(0f);
            for (int i = 1; i < pathPoints.Count; i++)
            {
                totalPathLength += Vector3.Distance(pathPoints[i - 1], pathPoints[i]);
                cumulativeLengths.Add(totalPathLength);
            }

            if (pathPoints.Count > 0)
            {
                transform.position = pathPoints[0];
            }
        }

        private void Update()
        {
            if (pathPoints.Count < 2) return;

            // 加減速(後退はしない)
            float targetSpeed = ThrottleInput * maxSpeed;
            float rate = ThrottleInput > 0f ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
            currentSpeed = Mathf.Max(currentSpeed, 0f);

            distanceTraveled += currentSpeed * Time.deltaTime;

            if (distanceTraveled >= totalPathLength)
            {
                if (isLooping)
                {
                    distanceTraveled -= totalPathLength; // 周回コースは先頭に戻って継続走行
                }
                else
                {
                    // 一本道の終端:仕様通り、停止ではなくスタート地点へテレポート
                    distanceTraveled = 0f;
                    currentSpeed = 0f;
                }
            }

            ApplyPositionAtDistance(distanceTraveled);
        }

        private void ApplyPositionAtDistance(float distance)
        {
            int segmentIndex = FindSegmentIndex(distance);
            Vector3 a = pathPoints[segmentIndex];
            Vector3 b = pathPoints[segmentIndex + 1];

            float segmentStart = cumulativeLengths[segmentIndex];
            float segmentLength = cumulativeLengths[segmentIndex + 1] - segmentStart;
            float t = segmentLength > 0f ? (distance - segmentStart) / segmentLength : 0f;

            Vector3 position = Vector3.Lerp(a, b, t);
            Vector3 forward = (b - a).normalized;

            transform.position = position;
            if (forward.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        private int FindSegmentIndex(float distance)
        {
            for (int i = 0; i < cumulativeLengths.Count - 1; i++)
            {
                if (distance <= cumulativeLengths[i + 1]) return i;
            }
            return Mathf.Max(0, cumulativeLengths.Count - 2);
        }
    }
}