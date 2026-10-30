using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// パーツ・UI・コース操作まわりの効果音をまとめて鳴らす部品。シーンに1つ置く。
    ///
    /// 【音源の設定】
    /// 各効果音は、Inspectorで割り当てなくても、Assets/Resources/Sfx/ フォルダに同じ名前の
    /// ファイルを置けば自動で読み込む(Inspectorで割り当てた場合は、そちらを優先)。
    /// 後から本物の音に差し替える時は、同じ名前のファイルを上書きするだけでよい。
    ///   snap / blocked / trash / delete_all / shelf_open / shelf_close / summon
    ///
    /// 【鳴らし方】
    /// 音の出る位置(パーツの位置など)から鳴る3Dの音として再生する。
    /// 複数の音が重なってもよいように、再生用のAudioSourceをいくつか用意して順番に使い回す。
    /// シーンに置かなくても、呼び出し側はエラーにならない(単に鳴らないだけ)。
    /// </summary>
    public class SfxLibrary : MonoBehaviour
    {
        public static SfxLibrary Instance { get; private set; }

        [Header("効果音(未設定なら Resources/Sfx/ の同名ファイルを自動で読み込む)")]
        [Tooltip("パーツが繋がった(Resources/Sfx/snap)")]
        [SerializeField] private AudioClip snap;
        [Tooltip("置けない位置(赤いゴースト)になった(Resources/Sfx/blocked)")]
        [SerializeField] private AudioClip blocked;
        [Tooltip("パーツをゴミ箱に入れた(Resources/Sfx/trash)")]
        [SerializeField] private AudioClip trash;
        [Tooltip("全パーツを削除した(Resources/Sfx/delete_all)")]
        [SerializeField] private AudioClip deleteAll;
        [Tooltip("パーツ棚を開いた(Resources/Sfx/shelf_open)")]
        [SerializeField] private AudioClip shelfOpen;
        [Tooltip("パーツ棚を閉じた(Resources/Sfx/shelf_close)")]
        [SerializeField] private AudioClip shelfClose;
        [Tooltip("車を手元に呼び戻した(Resources/Sfx/summon)")]
        [SerializeField] private AudioClip summon;

        [Header("再生設定")]
        [Tooltip("効果音全体の音量(0〜1)")]
        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 1f;
        [Tooltip("毎回少しだけ音の高さをばらつかせる幅。同じ音が続いても、単調に聞こえにくくなる")]
        [Range(0f, 0.3f)]
        [SerializeField] private float pitchJitter = 0.05f;
        [Tooltip("0=頭の中で鳴る平坦な音、1=音の出た位置から鳴る立体的な音")]
        [Range(0f, 1f)]
        [SerializeField] private float spatialBlend = 0.9f;
        [Tooltip("同時に鳴らせる音の数")]
        [SerializeField] private int poolSize = 8;

        private AudioSource[] pool;
        private int nextIndex;

        // ---- 呼び出し側が使う窓口(シーンにSfxLibraryが無くてもエラーにならない) ----

        public static void Snap(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.snap, position, 1f); }
        public static void Blocked(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.blocked, position, 1f); }
        public static void Trash(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.trash, position, 1f); }
        public static void DeleteAll(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.deleteAll, position, 1f); }
        public static void ShelfOpen(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.shelfOpen, position, 1f); }
        public static void ShelfClose(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.shelfClose, position, 1f); }
        public static void Summon(Vector3 position) { if (Instance != null) Instance.PlayClip(Instance.summon, position, 1f); }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SfxLibrary] シーンにSfxLibraryが複数あります。後から有効になった方を使います。", this);
            }
            Instance = this;

            snap = Load(snap, "snap");
            blocked = Load(blocked, "blocked");
            trash = Load(trash, "trash");
            deleteAll = Load(deleteAll, "delete_all");
            shelfOpen = Load(shelfOpen, "shelf_open");
            shelfClose = Load(shelfClose, "shelf_close");
            summon = Load(summon, "summon");

            pool = new AudioSource[Mathf.Max(1, poolSize)];
            for (int i = 0; i < pool.Length; i++)
            {
                var child = new GameObject("Sfx_" + i);
                child.transform.SetParent(transform, false);

                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = spatialBlend;
                source.minDistance = 0.5f;
                source.maxDistance = 20f;
                pool[i] = source;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Inspectorで割り当て済みならそれを使い、未設定ならResources/Sfx/から読み込む</summary>
        private static AudioClip Load(AudioClip assigned, string fileName)
        {
            if (assigned != null) return assigned;

            var loaded = Resources.Load<AudioClip>("Sfx/" + fileName);
            if (loaded == null)
            {
                Debug.LogWarning($"[SfxLibrary] 効果音「{fileName}」が見つかりません。" +
                                 $"Assets/Resources/Sfx/{fileName}.wav を置くか、Inspectorで割り当ててください。");
            }
            return loaded;
        }

        private void PlayClip(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null || pool == null) return;

            var source = pool[nextIndex];
            nextIndex = (nextIndex + 1) % pool.Length;

            source.transform.position = position;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, Mathf.Clamp01(volume * masterVolume));
        }
    }
}