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
    /// 条件を満たし始めると、まず車が揺れて警告する(猶予時間)。その間にトリガーを離して減速すれば
    /// 回避でき、揺れが続いて猶予時間を超えるとコースアウトする。
    /// コースアウト時は、突っ込んだ速度に応じた整数回転(速いほど多く回る)でスピンし、
    /// ちょうど前を向いた状態で終わって、同じ場所から走行を再開する(スタート地点へは戻さない)。
    ///
    /// 入力は ThrottleInput(0〜1)のみに抽象化してある。実際のMeta XRコントローラーの
    /// ボタン取得処理はこのスクリプトに含めていない(別スクリプトからこのフィールドをセットする想定)。
    ///
    /// 未実装(次のステップで対応予定):
    /// - ループ区間で車体が上下反転する際の見た目の破綻対策(現状は前フレームのUpを引き継いでLookRotationしている)
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
        [SerializeField] private float derailCurveAngleThreshold = 32f;
        [Tooltip("最大速度に対してこの割合以上の速度が出ている状態を「速すぎる」とみなす(0〜1)")]
        [Range(0.1f, 1f)]
        [SerializeField] private float derailSpeedFraction = 0.9f;
        [Tooltip("「速すぎる状態できついカーブ」の条件を満たし続けた時間がこの秒数を超えたら、" +
                 "実際にコースアウトさせる。この間は車が揺れて警告し、トリガーを離せば回避できる")]
        [Range(0f, 1.5f)]
        [SerializeField] private float derailGracePeriod = 0.6f;

        [Header("警告の揺れ(コースアウト直前)")]
        [Tooltip("警告中の最大の揺れ角度(度)。猶予時間が進むほど、この値に向かって大きくなる")]
        [SerializeField] private float warningShakeAngle = 6f;
        [Tooltip("警告中の最大の横揺れ量(メートル)")]
        [SerializeField] private float warningShakeOffset = 0.01f;
        [Tooltip("揺れの速さ(1秒あたりの往復回数)")]
        [SerializeField] private float warningShakeFrequency = 12f;

        [Header("スピン演出")]
        [Tooltip("最高速度で突っ込んだ場合の最大回転数(整数の周回数で着地する)")]
        [Range(1, 5)]
        [SerializeField] private int derailMaxRotations = 3;
        [Tooltip("1回転あたりの所要時間(秒)")]
        [SerializeField] private float derailSecondsPerRotation = 0.4f;

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
        private float derailSpinElapsed; // スピン開始からの経過時間
        private float derailSpinDuration; // このスピンの所要時間(イージングが完了するまで)
        private float derailTotalSpinAngle; // このスピンの最終的な累計回転角度(必ず360の倍数)
        private Quaternion derailStartRotation;
        private Vector3 derailSpinAxis;
        private float derailConditionTimer; // 「速すぎる状態できついカーブ」が連続している時間

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
            derailSpinElapsed = 0f;
            derailTotalSpinAngle = 0f;
            derailConditionTimer = 0f;
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
                CurrentLapTime += Time.deltaTime; // スピン中もタイムは進む(コースアウトのペナルティとして自然に時間を失う)

                derailSpinElapsed += Time.deltaTime;
                float progress = derailSpinDuration > 0f ? Mathf.Clamp01(derailSpinElapsed / derailSpinDuration) : 1f;
                // イーズアウト(最初は勢いよく、だんだん収まる)。progress=1でちょうどderailTotalSpinAngleに到達するため、
                // 360の倍数で着地するよう回転数を整数にしてあるこの角度は、必ず元の向き(前方)と一致する
                float eased = 1f - (1f - progress) * (1f - progress);
                transform.rotation = Quaternion.AngleAxis(eased * derailTotalSpinAngle, derailSpinAxis) * derailStartRotation;

                if (progress >= 1f)
                {
                    Recover();
                }
                return; // 復帰待ちの間は通常の走行処理を行わない
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

            // コースアウト判定: 「速すぎる状態できついカーブ」の条件が、一瞬ではなく
            // derailGracePeriod秒以上連続して続いた場合にのみ実際にコースアウトさせる
            float curveAngle = ComputeLocalCurveAngle(segmentIndex);
            bool tooFast = currentSpeed >= maxSpeed * derailSpeedFraction;
            if (tooFast && curveAngle >= derailCurveAngleThreshold)
            {
                derailConditionTimer += Time.deltaTime;
                if (derailConditionTimer >= derailGracePeriod)
                {
                    Derail();
                }
            }
            else
            {
                derailConditionTimer = 0f;
            }

            // コースアウト直前の警告: 猶予時間が進むほど、揺れが大きくなる。
            // (ApplyPositionAtDistanceが毎フレーム経路から位置・向きを作り直すので、揺れは蓄積しない)
            if (!isDerailed && derailConditionTimer > 0f)
            {
                float intensity = derailGracePeriod > 0f ? Mathf.Clamp01(derailConditionTimer / derailGracePeriod) : 1f;
                ApplyWarningShake(intensity);
            }
        }

        /// <summary>経路に沿った位置・向きに、警告用の揺れ(左右の首振り・傾き・横ブレ)を上乗せする</summary>
        private void ApplyWarningShake(float intensity)
        {
            float phase = Time.time * warningShakeFrequency * Mathf.PI * 2f;
            float yaw = Mathf.Sin(phase) * warningShakeAngle * intensity;
            float roll = Mathf.Sin(phase * 1.3f + 1f) * warningShakeAngle * 0.5f * intensity;
            float sway = Mathf.Sin(phase * 1.7f) * warningShakeOffset * intensity;

            transform.rotation = transform.rotation * Quaternion.Euler(0f, yaw, roll);
            transform.position += transform.right * sway;
        }

        /// <summary>コースアウトさせる: 速度に応じた回転数(整数)でスピンさせ、同じ場所で走行を再開する</summary>
        private void Derail()
        {
            isDerailed = true;
            derailConditionTimer = 0f;

            // currentSpeedを0にする前に、回転数の計算に使う
            float speedRatio = maxSpeed > 0f ? Mathf.Clamp01(currentSpeed / maxSpeed) : 0f;
            int rotations = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, derailMaxRotations, speedRatio)));
            derailTotalSpinAngle = rotations * 360f; // 必ず360の倍数 = 終了時は必ず開始時と同じ向きになる
            derailSpinDuration = rotations * derailSecondsPerRotation;
            derailSpinElapsed = 0f;

            currentSpeed = 0f;
            derailStartRotation = transform.rotation;
            derailSpinAxis = currentUp.sqrMagnitude > 0.0001f ? currentUp : Vector3.up;
        }

        /// <summary>コースアウトからの復帰: 位置はそのまま(スタートへは戻さない)、速度だけ0から再開する</summary>
        private void Recover()
        {
            isDerailed = false;
            currentSpeed = 0f;
            if (pathPoints.Count > 0)
            {
                // distanceTraveledは変更しない(コースアウトした場所から再開するため)。
                // スピン演出で変わった向きを、経路に沿った正しい向きに戻すためだけに呼ぶ。
                ApplyPositionAtDistance(distanceTraveled);
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