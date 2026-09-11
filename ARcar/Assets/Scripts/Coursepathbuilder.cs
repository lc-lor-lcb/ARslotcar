using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// スタートパーツを起点に、コネクタの接続関係(TrackConnector.ConnectedTo)を辿って
    /// コース全体の経路をワールド座標の点列として1本に構築する。
    ///
    /// - 行き止まり(次の接続が無い)に着いたら、そこでコース終端として打ち切る
    ///   (仕様上、車側はここで停止ではなくスタート地点へテレポートする)
    /// - 周回コース(スタートパーツまで接続が戻ってくる)の場合は、そこで打ち切り、
    ///   Result.IsLooping を true にして呼び出し側に伝える
    /// </summary>
    public static class CoursePathBuilder
    {
        public class Result
        {
            public List<Vector3> Points = new List<Vector3>();
            public bool IsLooping;
        }

        public static Result Build(TrackPiece startPiece)
        {
            var result = new Result();
            if (startPiece == null)
            {
                Debug.LogWarning("[CoursePathBuilder] スタートパーツが指定されていません。");
                return result;
            }

            var startConnectors = startPiece.GetConnectors();
            if (startPiece.GetComponent<TrackPath>() == null || startConnectors == null || startConnectors.Length < 2)
            {
                Debug.LogWarning("[CoursePathBuilder] スタートパーツにTrackPath、または2つのコネクタが見つかりません。");
                return result;
            }

            // スタートパーツの2つのコネクタのうち、片方を走行開始点、もう片方を「出口」として扱う。
            // スタートパーツ自体の向きが、コースを走る進行方向の基準になる。
            TrackConnector entry = startConnectors[0];
            TrackConnector exit = startConnectors[1];

            var visitedPieces = new HashSet<TrackPiece> { startPiece };

            AppendPiecePath(startPiece, entry, exit, result.Points);

            TrackConnector current = exit;

            while (current.ConnectedTo != null)
            {
                TrackConnector nextEntry = current.ConnectedTo;
                TrackPiece nextPiece = nextEntry.OwnerPiece;

                if (nextPiece == startPiece)
                {
                    result.IsLooping = true;
                    break; // スタートパーツまで戻ってきたら周回コースとして終了
                }

                if (!visitedPieces.Add(nextPiece))
                {
                    Debug.LogWarning("[CoursePathBuilder] 同じパーツを2回通過しました。接続関係を確認してください。");
                    break;
                }

                TrackConnector nextExit = nextPiece.GetOtherConnector(nextEntry);
                if (nextPiece.GetComponent<TrackPath>() == null || nextExit == null)
                {
                    Debug.LogWarning($"[CoursePathBuilder] {nextPiece.name} にTrackPath、または対となるコネクタが見つかりません。");
                    break;
                }

                AppendPiecePath(nextPiece, nextEntry, nextExit, result.Points);
                current = nextExit;
            }

            return result;
        }

        private static void AppendPiecePath(TrackPiece piece, TrackConnector from, TrackConnector to, List<Vector3> into)
        {
            var path = piece.GetComponent<TrackPath>();
            var points = path.GetWorldPathPoints(from, to);

            // 前のパーツの終点=このパーツの始点が重複するため、2パーツ目以降は先頭の1点を飛ばす
            int startIndex = into.Count > 0 ? 1 : 0;
            for (int i = startIndex; i < points.Count; i++)
            {
                into.Add(points[i]);
            }
        }
    }
}