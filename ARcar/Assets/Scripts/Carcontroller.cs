using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 構築済みのコース経路(ワールド座標点列)に沿って車を走らせる。
    /// 物理演算は使わず、経路上を進んだ距離(弧長)をパラメータとして位置・向きを直接更新する方式。
    ///
    /// 【スロットル入力について】
    /// Meta Questコントローラーのトリガーはバネが弱く、実車のスロットカーのような
    /// 繊細なアナログ操作(ギリギリを攻める微調整)がしづらいため、
    /// アナログ値をそのまま速度に反映するのではなく、閾値を境にした二値的な挙動にしている:
    /// 閾値以上押し込んだら一定の加速度で加速、閾値未満なら加速度ゼロ(その後は慣性で減速)。
    ///
    /// 【コースアウトについて】
    /// カーブ半径ごとの厳密な安全速度計算はせず、「最高速度に近い状態できついカーブに入ったら
    /// コースアウトする」という緩い判定にしている。最高速度をやや高めに設定することで、
    /// 「速く走りたい(爽快感)」と「速すぎるとコースアウトする(リスク)」のトレードオフを作る狙い。
    /// コースアウトした場合は、その場で停止して一定時間後にスタート地点へ復帰する
    /// (「直近のチェックポイントへ復帰」という仕様だったが、チェックポイントの仕組み自体が
    /// まだ無いため、現状はスタート地点への復帰に簡略化している)。
    ///
    /// 入力は ThrottleInput(0〜1)のみに抽象化してある。実際のMeta XRコントローラーの
    /// ボタン取得処理はこのスクリプトに含めていない(別スクリプトからこのフィールドをセットする想定)。
    ///
    /// 未実装(次のステップで対応予定):
    /// - ループ区間で車体が上下反転する際の見た目の破綻対策(現状はワールドUp基準でLookRotationしている)
    /// - 「直近のチェックポイント」への復帰(現状はスタート地点固定)
    /// </summary>
    public class CarController : MonoBehaviour
    {
        [Header("経路")]
        [Tooltip("コースの起点となるスタートパーツ")]
        [SerializeField] private TrackPiece startPiece;

        [Header("走行パラメータ")]
        [Tooltip("最大速度(m/s)。高めに設定するほど爽快感が出るが、コースアウトのリスクも上がる")]
        [SerializeField] private float maxSpeed = 1.8f;
        [Tooltip("トリガーが閾値を超えている間の加速度(m/s^2)")]
        [SerializeField] private float acceleration = 3.0f;
        [Tooltip("トリガーが閾値未満の時の減速度(m/s^2)。慣性で減速するだけで後退はしない")]
        [SerializeField] private float deceleration = 1.5f;
        [Tooltip("この値以上トリガーを押し込んだら「全開」とみなす閾値(0〜1)。" +
                 "バネの弱いコントローラーでの微調整の難しさを考慮し、アナログではなく二値的に扱う")]
        [Range(0.05f, 1f)]
        [SerializeField] private float throttleThreshold = 0.3f;

        [Header("コースアウト判定")]
        [Tooltip("カーブのきつさを測るために、現在地からどれだけ先(メートル)を見て角度の変化を測るか。" +
                 "パーツ内部の分割数に依存しない、実距離ベースの判定にするための値")]
        [Range(0.05f, 0.5f)]
        [SerializeField] private float curveSampleDistance = 0.15f;
        [Tooltip("上記の距離を進む間に、進行方向がこの角度(度)以上変化したら「きついカーブ」とみなす")]
        [Range(2f, 90f)]
        [SerializeField] private float derailCurveAngleThreshold = 20f;
        [Tooltip("最大速度に対してこの割合以上の速度が出ている状態を「速すぎる」とみなす(0〜1)")]
        [Range(0.1f, 1f)]
        [SerializeField] private float derailSpeedFraction = 0.75f;
        [Tooltip("コースアウトしてからスタート地点へ復帰するまでの待ち時間(秒)")]
        [SerializeField] private float derailRecoveryDuration = 3f;

        /// <summary>0(離した)〜1(全開)。ボタン入力側のスクリプトから毎フレームセットする想定</summary>
        [Range(0f, 1f)]
        public float ThrottleInput;

        private List<Vector3> pathPoints = new List<Vector3>();
        private List<float> cumulativeLengths = new List<float>();
        private bool isLooping;
        private float currentSpeed;
        private float distanceTraveled;
        private float totalPathLength;
        private Vector3 currentUp = Vector3.up;

        private bool isDerailed;
        private float derailTimer;

        /// <summary>今走っているラップの経過時間(秒)。コースアウトすると0にリセットされる(記録されない)</summary>
        public float CurrentLapTime { get; private set; }
        /// <summary>このセッション中のベストラップタイム(秒)。まだ1周もしていなければ+∞</summary>
        public float BestLapTime { get; private set; } = float.PositiveInfinity;

        private void Start()
        {
            RebuildPath();
        }

        private void OnEnable()
        {
            // パーツの接続/切断のたびに、自動で経路を組み直す
            TrackConnector.OnConnectionsChanged += RebuildPath;
        }

        private void OnDisable()
        {
            TrackConnector.OnConnectionsChanged -= RebuildPath;
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
            currentUp = Vector3.up; // 経路が組み直されたら、上向きの基準もリセットする
            isDerailed = false;
            derailTimer = 0f;
            CurrentLapTime = 0f;
            BestLapTime = float.PositiveInfinity; // コースが変わったので、古い記録は無効にする

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

            if (isDerailed)
            {
                derailTimer -= Time.deltaTime;
                if (derailTimer <= 0f)
                {
                    Recover();
                }
                return; // 復帰待ちの間は走行処理を行わない
            }

            // 加減速: アナログではなく、閾値を境にした二値的な挙動
            bool isAccelerating = ThrottleInput >= throttleThreshold;
            float targetSpeed = isAccelerating ? maxSpeed : 0f;
            float rate = isAccelerating ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);
            currentSpeed = Mathf.Max(currentSpeed, 0f);

            distanceTraveled += currentSpeed * Time.deltaTime;
            CurrentLapTime += Time.deltaTime;

            if (distanceTraveled >= totalPathLength)
            {
                // 1周(または一本道を走破)完了。ベストタイムを更新してから、次のラップの計測に移る
                if (CurrentLapTime < BestLapTime) BestLapTime = CurrentLapTime;
                CurrentLapTime = 0f;

                if (isLooping)
                {
                    distanceTraveled -= totalPathLength; // 周回コースは先頭に戻って継続走行
                }
                else
                {
                    // 一本道の終端:停止ではなくスタート地点へテレポート
                    distanceTraveled = 0f;
                    currentSpeed = 0f;
                }
            }

            int segmentIndex = ApplyPositionAtDistance(distanceTraveled);

            // コースアウト判定: 速すぎる状態できついカーブに差し掛かったら
            float curveAngle = ComputeLocalCurveAngle(segmentIndex);
            bool tooFast = currentSpeed >= maxSpeed * derailSpeedFraction;
            if (tooFast && curveAngle >= derailCurveAngleThreshold)
            {
                Derail();
            }
        }
        /// <summary>コースアウトさせる: その場で停止し、一定時間後にスタート地点へ復帰する</summary>
        private void Derail()
        {
            isDerailed = true;
            derailTimer = derailRecoveryDuration;
            currentSpeed = 0f;
            CurrentLapTime = 0f; // コースアウトした挑戦は記録に残さない
        }

        /// <summary>コースアウトからの復帰: スタート地点へ戻す</summary>
        private void Recover()
        {
            isDerailed = false;
            distanceTraveled = 0f;
            currentSpeed = 0f;
            currentUp = Vector3.up;
            if (pathPoints.Count > 0)
            {
                ApplyPositionAtDistance(0f);
            }
        }

        /// <summary>指定の弧長位置に車の位置・向きを反映し、使用したセグメント番号を返す</summary>
        private int ApplyPositionAtDistance(float distance)
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
                // 前フレームの「上向き」を新しい進行方向に投影して引き継ぐことで、
                // 毎フレーム独立にワールドの真上から計算し直す場合に起きる急なねじれ・スナップを防ぐ
                Vector3 projectedUp = Vector3.ProjectOnPlane(currentUp, forward);
                if (projectedUp.sqrMagnitude < 0.0001f)
                {
                    // 進行方向が真上/真下に近く、投影が潰れてしまった場合のフォールバック
                    projectedUp = Vector3.ProjectOnPlane(Vector3.up, forward);
                    if (projectedUp.sqrMagnitude < 0.0001f) projectedUp = transform.right;
                }
                currentUp = projectedUp.normalized;

                transform.rotation = Quaternion.LookRotation(forward, currentUp);
            }

            return segmentIndex;
        }

        /// <summary>
        /// 指定セグメントの位置から、curveSampleDistance分だけ先の区間までの間に、
        /// 進行方向が何度変化したかを返す。パーツ内部の分割数(PathResolution)に依存しない、
        /// 実距離ベースの曲率の目安にするため、隣接セグメント同士ではなく一定距離先と比較する。
        /// </summary>
        private float ComputeLocalCurveAngle(int segmentIndex)
        {
            if (pathPoints.Count < 3 || segmentIndex + 1 >= pathPoints.Count) return 0f;

            Vector3 dirNow = (pathPoints[segmentIndex + 1] - pathPoints[segmentIndex]).normalized;

            float targetDistance = Mathf.Min(cumulativeLengths[segmentIndex] + curveSampleDistance, totalPathLength);
            int aheadIndex = FindSegmentIndex(targetDistance);
            int aheadNext = Mathf.Min(aheadIndex + 1, pathPoints.Count - 1);
            if (aheadIndex == aheadNext) return 0f;

            Vector3 dirAhead = (pathPoints[aheadNext] - pathPoints[aheadIndex]).normalized;
            return Vector3.Angle(dirNow, dirAhead);
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