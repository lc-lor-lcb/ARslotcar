using System.Collections.Generic;
using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 指定したパーツ(seed)と繋がっているパーツ全体から、コース全体の経路を
    /// ワールド座標の点列として1本に構築する。スタートパーツは不要。
    ///
    /// - 繋がっているパーツの塊の中に「まだ何にも繋がっていないコネクタ(行き止まり)」があれば、
    ///   そこを経路の始点とする(一本道コース)。
    /// - 行き止まりが無く、全部が輪になっていれば、seedのパーツを起点に1周する経路を作る(周回コース)。
    /// - 車がどちら向きに走るかはここでは決めない。経路は常に「始点→終点」の順で返し、
    ///   向きの反転は呼び出し側(CarController)が車の向きを見て行う。
    ///
    /// 前提: 1パーツにつきコネクタは2つ(入口と出口)。
    /// </summary>
    public static class CoursePathBuilder
    {
        public class Result
        {
            public List<Vector3> Points = new List<Vector3>();
            public bool IsLooping;
        }

        public static Result Build(TrackPiece seed)
        {
            var result = new Result();
            if (seed == null) return result;

            var group = CollectConnectedGroup(seed);

            // 始点を探す: グループ内で、まだ何にも繋がっていないコネクタを持つパーツ
            TrackPiece startPiece = null;
            TrackConnector startEntry = null;
            foreach (var piece in group)
            {
                var connectors = piece.GetConnectors();
                if (connectors == null) continue;
                foreach (var c in connectors)
                {
                    if (c.IsConnected) continue;
                    startPiece = piece;
                    startEntry = c;
                    break;
                }
                if (startPiece != null) break;
            }

            // 行き止まりが無い = 輪になっている。seedのパーツの1つ目のコネクタを入口として1周する
            if (startPiece == null)
            {
                var connectors = seed.GetConnectors();
                if (connectors == null || connectors.Length < 2) return result;
                startPiece = seed;
                startEntry = connectors[0];
            }

            var visited = new HashSet<TrackPiece>();
            TrackPiece current = startPiece;
            TrackConnector entry = startEntry;

            while (current != null)
            {
                if (!visited.Add(current)) break;

                TrackConnector exit = current.GetOtherConnector(entry);
                var path = current.GetComponent<TrackPath>();
                if (exit == null || path == null)
                {
                    Debug.LogWarning($"[CoursePathBuilder] {current.name} にTrackPath、または対となるコネクタが見つかりません。");
                    break;
                }

                AppendPiecePath(path, entry, exit, result.Points);

                TrackConnector next = exit.ConnectedTo;
                if (next == null) break; // 行き止まり

                if (next == startEntry)
                {
                    result.IsLooping = true; // 始点の入口まで戻ってきた = 周回コース
                    break;
                }

                current = next.OwnerPiece;
                entry = next;
            }

            return result;
        }

        /// <summary>seedから、コネクタの接続を辿って繋がっている全パーツ(seed自身を含む)を集める</summary>
        private static List<TrackPiece> CollectConnectedGroup(TrackPiece seed)
        {
            var group = new List<TrackPiece>();
            var visited = new HashSet<TrackPiece> { seed };
            var queue = new Queue<TrackPiece>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                group.Add(current);

                var connectors = current.GetConnectors();
                if (connectors == null) continue;

                foreach (var c in connectors)
                {
                    if (!c.IsConnected) continue;
                    var neighbor = c.ConnectedTo.OwnerPiece;
                    if (neighbor == null || !visited.Add(neighbor)) continue;
                    queue.Enqueue(neighbor);
                }
            }

            return group;
        }

        private static void AppendPiecePath(TrackPath path, TrackConnector from, TrackConnector to, List<Vector3> into)
        {
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