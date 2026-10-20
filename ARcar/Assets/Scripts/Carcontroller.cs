using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

namespace ARSlotcar
{
    /// <summary>
    /// 車。プレイヤーが車を手でつまんでコース上に置くと、その位置から一番近い経路上の点に吸着し、
    /// 置いた時の車の向きの方向に走れる状態になる(置いた直後は速度0。トリガーを引くと走り出す)。
    /// スタートパーツは不要で、コースを組み替えても車は今の位置に載ったまま、新しい経路に載り直す。
    ///
    /// 走行は物理演算を使わず、経路上を進んだ距離(弧長)をパラメータとして位置・向きを直接更新する方式。
    ///
    /// 【置き方】
    /// - 掴んでいる間は経路追従を止める。離した位置がコース上(パーツと同じ吸着距離以内)なら吸着する。
    ///   コース外に離した場合は、元の場所には戻さず、その場に置かれたままになる(走行不可・トリガーは無視)。
    /// - 車はコースとは独立した物体で、コースを動かしても引きずられない。載っていたパーツが
    ///   離れたら、車はコースから外れてその場に残る(再び載せるには、掴んでコース上に置き直す)。
    /// - 車の前方向(+Z)と経路の進行方向が逆向きなら、経路を反転して車の向きの方向に走らせる。
    ///
    /// 【スロットル入力】
    /// Meta Questコントローラーのトリガーはバネが弱く繊細なアナログ操作がしづらいため、
    /// 閾値を境にした二値的な挙動にしている:
    /// 閾値以上押し込んだら一定の加速度で加速、閾値未満なら加速度ゼロ(その後は慣性で減速)。
    /// 入力は ThrottleInput(0〜1)のみに抽象化してあり、別スクリプトからセットする想定。
    ///
    /// 【コースアウト】
    /// 最高速度に近い状態できついカーブに入り、その状態が猶予時間続くとコースアウトする。
    /// 猶予時間の間は車が揺れて警告し、トリガーを離して減速すれば回避できる。
    /// コースアウト時は、突っ込んだ速度に応じた整数回転(速いほど多く回る)でスピンし、
    /// ちょうど前を向いた状態で終わって、同じ場所から走行を再開する。
    ///
    /// 【Unity側の準備】
    /// このオブジェクトに「Add Grab Interaction」ウィザードでGrabbable一式(コライダー・Rigidbody含む)を付けておく。
    /// Rigidbodyは Is Kinematic をON、Grabbable の Throw When Unselected はOFFにする。
    ///
    /// 【行き止まり】
    /// 繋がっていない端(行き止まり)に着いたら、そこで止まって、トリガーを押しても進まない
    /// (勢いよくぶつかった場合は、ぶつかった揺れが出る)。コースを繋ぎ足して先に進めるように
    /// なれば、そのまま走り出せる。輪になっているコースでは、止まらずに周回し続ける。
    /// </summary>
    public class CarController : MonoBehaviour
    {
        [Header("置き方")]
        [Tooltip("設定すると、パーツと同じ吸着距離(TrackConfigのSnapRadius)を使う。" +
                 "パーツと車で感応距離を揃えるため、パーツと同じTrackConfigを割り当てる")]
        [SerializeField] private TrackConfig config;
        [Tooltip("上のConfigを設定していない場合に使う吸着距離(メートル)。" +
                 "Configが未設定でも、シーン内のパーツから自動でConfigを探して使う")]
        [SerializeField] private float placeSnapDistance = 0.08f;
        [Tooltip("コースの上に載っている車が、載っているパーツから、この距離(メートル)以上離れたら" +
                 "コースから外れたとみなす。コースを動かしても車が引きずられないようにするための値")]
        [SerializeField] private float stayOnTrackTolerance = 0.03f;

        [Header("行き止まり")]
        [Tooltip("行き止まりにこの速度(m/s)以上でぶつかったら、ぶつかった揺れを出す")]
        [SerializeField] private float deadEndBumpMinSpeed = 0.4f;
        [Tooltip("ぶつかった揺れの長さ(秒)。揺れの大きさは「警告の揺れ」の設定を使う")]
        [SerializeField] private float deadEndBumpDuration = 0.25f;

        [Header("リセット(手元に呼び戻す)")]
        [Tooltip("呼び戻した時に、プレイヤーの前方に出す距離(メートル)。手を伸ばして届く程度")]
        [SerializeField] private float summonDistance = 0.45f;
        [Tooltip("呼び戻した時の、目線からの高さ(メートル)。マイナスで目線より下")]
        [SerializeField] private float summonHeightOffset = -0.35f;
        [Tooltip("掴んでいる間、置かれる位置に出す半透明のゴースト用マテリアル(パーツのゴーストと同じものでOK)")]
        [SerializeField] private Material ghostMaterial;

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

        private Grabbable grabbable;

        // 経路
        private List<Vector3> pathPoints = new List<Vector3>();
        private List<float> cumulativeLengths = new List<float>();
        private bool isLooping;
        private float totalPathLength;
        private float distanceTraveled;
        private Vector3 currentUp = Vector3.up;
        private Vector3 travelForward = Vector3.forward; // 経路上での現在の進行方向(揺れやスピンの影響を受けない)

        // 状態
        private bool isOnTrack;
        private bool isHeld;
        private float currentSpeed;

        // 置く位置のゴースト
        private GameObject ghost;
        private float previewTimer;

        private float deadEndBumpTimer;
        private float validateTimer;

        // コースアウト
        private bool isDerailed;
        private float derailSpinElapsed;
        private float derailSpinDuration;
        private float derailTotalSpinAngle; // 必ず360の倍数
        private Quaternion derailStartRotation;
        private Vector3 derailSpinAxis;
        private float derailConditionTimer;

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            if (grabbable == null)
            {
                Debug.LogWarning("[CarController] Grabbableが見つかりません。「Add Grab Interaction」ウィザードで車に掴む機能を付けてください。", this);
            }

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        /// <summary>
        /// パーツと同じ吸着距離。Configが未設定なら、シーン内のパーツが使っているConfigを自動で探して使う
        /// (シーンに保存済みの古い値が使われて、パーツと感応距離がズレてしまうのを防ぐ)。
        /// </summary>
        private float PlaceSnapLimit
        {
            get
            {
                if (config == null)
                {
                    var anyPiece = FindFirstObjectByType<TrackPiece>();
                    if (anyPiece != null) config = anyPiece.Config;
                }
                return config != null ? config.SnapRadius : placeSnapDistance;
            }
        }

        private void Start()
        {
            Debug.Log($"[CarController] 吸着距離={PlaceSnapLimit:F3}m ({(config != null ? "TrackConfigのSnapRadiusを使用" : "Place Snap Distanceを使用")})");

            // シーン開始時に、すでにコースの上にいる場合は載せておく(コース外なら何もしない)
            TrySeatOnTrack(transform.position, transform.forward, true, PlaceSnapLimit);
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += HandlePointerEvent;
            // パーツの接続/切断/移動のたびに、今の位置を保ったまま新しいコースに載り直す
            TrackConnector.OnConnectionsChanged += HandleConnectionsChanged;
        }

        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            TrackConnector.OnConnectionsChanged -= HandleConnectionsChanged;
            HideGhost();
        }

        private void OnDestroy()
        {
            if (ghost != null) Destroy(ghost);
        }

        // ---- 掴む・置く ----

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    if (isHeld) break;
                    isHeld = true;

                    // 掴んだ時点で、走行・コースアウト中の状態は全部キャンセルする
                    isDerailed = false;
                    derailConditionTimer = 0f;
                    deadEndBumpTimer = 0f;
                    currentSpeed = 0f;
                    break;

                case PointerEventType.Unselect:
                    if (!isHeld) break;
                    isHeld = false;
                    HideGhost();

                    if (TrySeatOnTrack(transform.position, transform.forward, true, PlaceSnapLimit))
                    {
                        currentSpeed = 0f; // 置いた直後は止まっている
                    }
                    else
                    {
                        // コース外に離した: 元の場所には戻さず、その場に置いたままにする(走行不可)
                        LeaveTrack();
                    }
                    break;
            }
        }

        /// <summary>コースが変化した時: 掴まれていなければ、今の位置・進行方向を保ったまま載り直す</summary>
        private void HandleConnectionsChanged()
        {
            if (isHeld || !isOnTrack) return;

            // 載っているパーツが動かされてコースが変わった場合、車は引きずられない:
            // 今の位置のすぐそば(stayOnTrackTolerance以内)にコースがあれば載り直し、無ければその場に残る
            if (!TrySeatOnTrack(transform.position, travelForward, false, stayOnTrackTolerance))
            {
                LeaveTrack();
            }
        }

        /// <summary>コースから外れた状態にする: 位置はそのまま、走行・コースアウト・行き止まりの状態を解除する</summary>
        private void LeaveTrack()
        {
            isOnTrack = false;
            currentSpeed = 0f;
            isDerailed = false;
            derailConditionTimer = 0f;
            deadEndBumpTimer = 0f;
        }

        /// <summary>
        /// 車を、今のプレイヤーの目の前(手が届く位置)に呼び戻す。コースからは外れた状態になるので、
        /// 掴んで好きなコースに置き直す。車の場所が分からなくなった時のリセット用。
        /// </summary>
        public void ResetToPlayer()
        {
            if (isHeld) return; // 手に持っている最中は何もしない
            if (!PlayerView.TryGetHead(out Vector3 head, out Vector3 flatForward)) return;

            LeaveTrack();
            HideGhost();

            Vector3 position = PlayerView.PointInFront(head, flatForward, summonDistance, summonHeightOffset);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(flatForward, Vector3.up));
        }

        // ---- コースへの吸着 ----

        /// <summary>車を載せる位置・経路の計算結果。計算だけで、車の状態は変更しない</summary>
        private struct SeatResult
        {
            public List<Vector3> Points;
            public List<float> Lengths;
            public float Total;
            public float Along;
            public bool IsLooping;
        }

        /// <summary>
        /// worldPos に一番近いパーツを探し、そのパーツと繋がっているコース全体の経路上での
        /// 載せる位置を計算する。facing の向きが経路の進行方向と逆なら、経路を反転して
        /// facing の方向に走る経路にする。状態は一切変更しない(ゴースト表示にも使う)。
        /// </summary>
        private bool TryComputeSeat(Vector3 worldPos, Vector3 facing, float maxDistance, out SeatResult seat)
        {
            seat = default;

            TrackPiece seed = FindNearestPiece(worldPos, out float distance);
            if (seed == null || distance > maxDistance) return false;

            var result = CoursePathBuilder.Build(seed);
            if (result.Points.Count < 2) return false;

            List<Vector3> points = result.Points;
            List<float> lengths = ComputeCumulativeLengths(points);
            float total = lengths[lengths.Count - 1];
            if (total <= 0.0001f) return false;

            float along = ProjectOntoPath(points, lengths, worldPos, out Vector3 tangent);

            // 車の向きが経路の進行方向と逆なら、経路を反転する
            if (facing.sqrMagnitude > 0.0001f && Vector3.Dot(facing.normalized, tangent) < 0f)
            {
                points.Reverse();
                lengths = ComputeCumulativeLengths(points);
                along = total - along;
            }

            seat = new SeatResult
            {
                Points = points,
                Lengths = lengths,
                Total = total,
                Along = Mathf.Clamp(along, 0f, total),
                IsLooping = result.IsLooping
            };
            return true;
        }

        /// <summary>計算した位置に、実際に車を載せる。成功した場合のみ状態を更新する</summary>
        private bool TrySeatOnTrack(Vector3 worldPos, Vector3 facing, bool resetUp, float maxDistance)
        {
            if (!TryComputeSeat(worldPos, facing, maxDistance, out SeatResult seat)) return false;

            pathPoints = seat.Points;
            cumulativeLengths = seat.Lengths;
            totalPathLength = seat.Total;
            isLooping = seat.IsLooping;
            distanceTraveled = seat.Along;
            isOnTrack = true;
            if (resetUp) currentUp = Vector3.up;

            ApplyPositionAtDistance(distanceTraveled);
            return true;
        }

        /// <summary>シーン内のパーツ(棚のテンプレートと削除済みを除く)のうち、worldPosに一番近い経路を持つものを返す</summary>
        private static TrackPiece FindNearestPiece(Vector3 worldPos, out float bestDistance)
        {
            bestDistance = float.PositiveInfinity;
            TrackPiece best = null;

            var pieces = FindObjectsByType<TrackPiece>(FindObjectsSortMode.None);
            foreach (var piece in pieces)
            {
                if (piece.IsDeleted) continue;
                if (piece.GetComponent<PartShelfSlot>() != null) continue;

                var path = piece.GetComponent<TrackPath>();
                var connectors = piece.GetConnectors();
                if (path == null || connectors == null || connectors.Length < 2) continue;

                var points = path.GetWorldPathPoints(connectors[0], connectors[1]);
                float d = DistanceToPolyline(points, worldPos);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = piece;
                }
            }

            return best;
        }

        private static float DistanceToPolyline(List<Vector3> points, Vector3 p)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 closest = ClosestPointOnSegment(points[i], points[i + 1], p, out _);
                best = Mathf.Min(best, Vector3.Distance(closest, p));
            }
            return best;
        }

        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p, out float t)
        {
            Vector3 ab = b - a;
            float sqr = ab.sqrMagnitude;
            t = sqr > 0.000001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / sqr) : 0f;
            return a + ab * t;
        }

        /// <summary>経路上でworldPosに一番近い点の「始点からの距離」と、そこでの進行方向を返す</summary>
        private static float ProjectOntoPath(List<Vector3> points, List<float> lengths, Vector3 p, out Vector3 tangent)
        {
            float bestSqr = float.PositiveInfinity;
            float bestAlong = 0f;
            tangent = Vector3.forward;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 closest = ClosestPointOnSegment(points[i], points[i + 1], p, out float t);
                float sqr = (closest - p).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    Vector3 dir = points[i + 1] - points[i];
                    bestSqr = sqr;
                    bestAlong = lengths[i] + t * dir.magnitude;
                    tangent = dir.sqrMagnitude > 0.000001f ? dir.normalized : tangent;
                }
            }

            return bestAlong;
        }

        private static List<float> ComputeCumulativeLengths(List<Vector3> points)
        {
            var lengths = new List<float>(points.Count) { 0f };
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector3.Distance(points[i - 1], points[i]);
                lengths.Add(total);
            }
            return lengths;
        }

        // ---- 走行 ----

        private void Update()
        {
            if (isHeld)
            {
                UpdatePlacementGhost();
                return; // 掴んでいる間は経路追従しない
            }
            if (!isOnTrack || pathPoints.Count < 2) return;

            // 載っているパーツが動かされて離れていないか、定期的に確認する
            // (離れていたら、車は引きずられずにコースから外れてその場に残る)
            validateTimer -= Time.deltaTime;
            if (validateTimer <= 0f)
            {
                validateTimer = 0.25f;
                FindNearestPiece(transform.position, out float distanceToCourse);
                if (distanceToCourse > stayOnTrackTolerance)
                {
                    LeaveTrack();
                    return;
                }
            }

            if (isDerailed)
            {
                derailSpinElapsed += Time.deltaTime;
                float progress = derailSpinDuration > 0f ? Mathf.Clamp01(derailSpinElapsed / derailSpinDuration) : 1f;
                // イーズアウト(最初は勢いよく、だんだん収まる)。progress=1でちょうどderailTotalSpinAngleに到達する。
                // 回転数は整数なので、終了時は必ず元の向き(前方)と一致する
                float eased = 1f - (1f - progress) * (1f - progress);
                transform.rotation = Quaternion.AngleAxis(eased * derailTotalSpinAngle, derailSpinAxis) * derailStartRotation;

                if (progress >= 1f)
                {
                    Recover();
                }
                return; // スピン中は通常の走行処理を行わない
            }

            // 行き止まりで止まっている間: トリガーを押しても進まない。
            // コースが繋ぎ足されて先に進めるようになれば(経路が組み直されて終端でなくなれば)、また走れる
            if (!isLooping && distanceTraveled >= totalPathLength - 0.001f)
            {
                currentSpeed = 0f;
                derailConditionTimer = 0f;
                ApplyPositionAtDistance(distanceTraveled);
                UpdateDeadEndBump();
                return;
            }

            // 加減速: アナログではなく、閾値を境にした二値的な挙動
            bool isAccelerating = ThrottleInput >= throttleThreshold;
            float targetSpeed = isAccelerating ? maxSpeed : 0f;
            float rate = isAccelerating ? acceleration : deceleration;
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
                    // 行き止まり: 終端で止まる。勢いよくぶつかった場合は、ぶつかった揺れを出す
                    distanceTraveled = totalPathLength;
                    if (currentSpeed >= deadEndBumpMinSpeed) deadEndBumpTimer = deadEndBumpDuration;
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

            UpdateDeadEndBump();
        }

        /// <summary>行き止まりにぶつかった時の、短い揺れ(だんだん収まる)</summary>
        private void UpdateDeadEndBump()
        {
            if (deadEndBumpTimer <= 0f) return;

            deadEndBumpTimer -= Time.deltaTime;
            float intensity = deadEndBumpDuration > 0f ? Mathf.Clamp01(deadEndBumpTimer / deadEndBumpDuration) : 0f;
            ApplyWarningShake(intensity);
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

        /// <summary>コースアウトからの復帰: 位置はそのまま、速度だけ0から再開する</summary>
        private void Recover()
        {
            isDerailed = false;
            currentSpeed = 0f;
            // distanceTraveledは変更しない(コースアウトした場所から再開するため)。
            // スピン演出で変わった向きを、経路に沿った正しい向きに戻すためだけに呼ぶ。
            ApplyPositionAtDistance(distanceTraveled);
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
                travelForward = forward;

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
            return FindSegmentIndex(cumulativeLengths, distance);
        }

        private static int FindSegmentIndex(List<float> lengths, float distance)
        {
            for (int i = 0; i < lengths.Count - 1; i++)
            {
                if (distance <= lengths[i + 1]) return i;
            }
            return Mathf.Max(0, lengths.Count - 2);
        }

        /// <summary>任意の経路上の、指定した距離での位置と進行方向を返す(車の状態は変更しない)</summary>
        private static void EvaluatePath(List<Vector3> points, List<float> lengths, float distance,
            out Vector3 position, out Vector3 forward)
        {
            int i = FindSegmentIndex(lengths, distance);
            Vector3 a = points[i];
            Vector3 b = points[i + 1];
            float segLength = lengths[i + 1] - lengths[i];
            float t = segLength > 0f ? (distance - lengths[i]) / segLength : 0f;

            position = Vector3.Lerp(a, b, t);
            forward = (b - a).normalized;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        }

        // ---- 置く位置のゴースト ----

        /// <summary>掴んでいる間、離したらどこに載るかを半透明の分身で見せる(載れない場所なら消す)</summary>
        private void UpdatePlacementGhost()
        {
            // 毎フレームだと経路の再計算が重いので、少し間引く
            previewTimer -= Time.deltaTime;
            if (previewTimer > 0f) return;
            previewTimer = 0.03f;

            if (TryComputeSeat(transform.position, transform.forward, PlaceSnapLimit, out SeatResult seat))
            {
                EvaluatePath(seat.Points, seat.Lengths, seat.Along, out Vector3 position, out Vector3 forward);

                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
                Quaternion rotation = up.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(forward, up.normalized)
                    : Quaternion.LookRotation(forward);

                ShowGhostAt(position, rotation);
            }
            else
            {
                HideGhost();
            }
        }

        private void EnsureGhostCreated()
        {
            if (ghost != null) return;

            ghost = Instantiate(gameObject, transform.position, transform.rotation);
            ghost.name = name + "_Ghost";

            // 見た目だけの分身にする: スクリプト → Rigidbody → Collider の順に全部消す
            foreach (var behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(behaviour);
            var body = ghost.GetComponent<Rigidbody>();
            if (body != null) Destroy(body);
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) Destroy(col);

            if (ghostMaterial != null)
            {
                foreach (var meshRenderer in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = new Material[meshRenderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = ghostMaterial;
                    meshRenderer.sharedMaterials = materials;
                }
            }

            ghost.SetActive(false);
        }

        private void ShowGhostAt(Vector3 position, Quaternion rotation)
        {
            EnsureGhostCreated();
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(position, rotation);
        }

        private void HideGhost()
        {
            if (ghost != null) ghost.SetActive(false);
            previewTimer = 0f;
        }
    }
}