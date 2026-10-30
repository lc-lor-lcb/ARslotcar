using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 車まわりの効果音。車のオブジェクトに付ける。
    ///
    /// - 車をコースに置いた時: カチッ(car_seated)
    /// - 行き止まりにぶつかった時: ドン(car_bump)
    /// - コースアウトの警告中: カタカタ(warning_loop)。車の揺れが大きくなるほど音量が上がる
    /// - スピン中: キュルキュル(skid_loop)。回転が収まるにつれて、音量と音の高さが下がっていく
    ///
    /// 音源は、Inspectorで割り当てなくても、Assets/Resources/Sfx/ に同じ名前のファイルを置けば
    /// 自動で読み込む(Inspectorで割り当てた場合は、そちらを優先)。
    ///
    /// エンジン音(CarEngineSound)のAudioSourceとは別に、この部品が専用のAudioSourceを子オブジェクトに作る
    /// (エンジン音のAudioSourceは音量が速度に連動するため、同じものを使うと、止まっている時の効果音が消えてしまう)。
    /// </summary>
    public class CarEffectSounds : MonoBehaviour
    {
        [Tooltip("状態を参照する車。未設定なら同じオブジェクトから探す")]
        [SerializeField] private CarController car;

        [Header("音源(未設定なら Resources/Sfx/ の同名ファイルを自動で読み込む)")]
        [Tooltip("車をコースに置いた(Resources/Sfx/car_seated)")]
        [SerializeField] private AudioClip seated;
        [Tooltip("行き止まりにぶつかった(Resources/Sfx/car_bump)")]
        [SerializeField] private AudioClip bump;
        [Tooltip("コースアウト直前の警告のループ音(Resources/Sfx/warning_loop)")]
        [SerializeField] private AudioClip warningLoop;
        [Tooltip("スピン中のループ音(Resources/Sfx/skid_loop)")]
        [SerializeField] private AudioClip skidLoop;

        [Header("音量")]
        [Range(0f, 1f)] [SerializeField] private float oneShotVolume = 0.9f;
        [Tooltip("警告音の最大音量(揺れが最大の時)")]
        [Range(0f, 1f)] [SerializeField] private float warningMaxVolume = 0.6f;
        [Tooltip("スピン音の最大音量(スピン開始の瞬間)")]
        [Range(0f, 1f)] [SerializeField] private float skidMaxVolume = 0.7f;
        [Tooltip("スピン音の高さ。開始時→終了時に向かって、この範囲で下がっていく")]
        [SerializeField] private float skidPitchStart = 1.6f;
        [SerializeField] private float skidPitchEnd = 0.8f;
        [Tooltip("ループ音の音量変化を滑らかにする強さ")]
        [SerializeField] private float smoothing = 18f;

        private AudioSource oneShotSource;
        private AudioSource warningSource;
        private AudioSource skidSource;

        private void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (car == null) Debug.LogWarning("[CarEffectSounds] CarControllerが見つかりません。", this);

            seated = Load(seated, "car_seated");
            bump = Load(bump, "car_bump");
            warningLoop = Load(warningLoop, "warning_loop");
            skidLoop = Load(skidLoop, "skid_loop");

            // 専用のAudioSourceは、子オブジェクトに作る(エンジン音のAudioSourceと混ざらないようにする)
            var holder = new GameObject("CarEffectSources");
            holder.transform.SetParent(transform, false);

            oneShotSource = CreateSource(holder, null, false);
            warningSource = CreateSource(holder, warningLoop, true);
            skidSource = CreateSource(holder, skidLoop, true);
        }

        private void OnEnable()
        {
            if (car == null) return;
            car.Seated += OnSeated;
            car.DeadEndBumped += OnBumped;
        }

        private void OnDisable()
        {
            if (car == null) return;
            car.Seated -= OnSeated;
            car.DeadEndBumped -= OnBumped;
        }

        private void Update()
        {
            if (car == null) return;

            // 警告: 揺れの強さ(0〜1)に比例して、音量を上げる
            float warning = car.WarningIntensity;
            UpdateLoop(warningSource, warning * warningMaxVolume, 1f + 0.3f * warning);

            // スピン: 進行(0〜1)につれて、音量と音の高さを下げる。スピン中でなければ無音
            float spin = car.SpinProgress;
            if (spin >= 0f)
            {
                float remaining = 1f - spin;
                UpdateLoop(skidSource, remaining * skidMaxVolume, Mathf.Lerp(skidPitchStart, skidPitchEnd, spin));
            }
            else
            {
                UpdateLoop(skidSource, 0f, skidPitchEnd);
            }
        }

        private void OnSeated() { PlayOneShot(seated); }
        private void OnBumped() { PlayOneShot(bump); }

        private void PlayOneShot(AudioClip clip)
        {
            if (clip == null || oneShotSource == null) return;
            oneShotSource.PlayOneShot(clip, oneShotVolume);
        }

        /// <summary>ループ音の音量を目標へ滑らかに近づけ、ほぼ無音になったら再生を止める</summary>
        private void UpdateLoop(AudioSource source, float targetVolume, float pitch)
        {
            if (source == null || source.clip == null) return;

            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            source.volume = Mathf.Lerp(source.volume, targetVolume, k);
            source.pitch = pitch;

            if (targetVolume > 0.01f)
            {
                if (!source.isPlaying) source.Play();
            }
            else if (source.isPlaying && source.volume < 0.01f)
            {
                source.Stop();
            }
        }

        private static AudioSource CreateSource(GameObject holder, AudioClip clip, bool loop)
        {
            var source = holder.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.5f;
            source.maxDistance = 20f;
            source.volume = loop ? 0f : 1f;
            return source;
        }

        private static AudioClip Load(AudioClip assigned, string fileName)
        {
            if (assigned != null) return assigned;

            var loaded = Resources.Load<AudioClip>("Sfx/" + fileName);
            if (loaded == null)
            {
                Debug.LogWarning($"[CarEffectSounds] 効果音「{fileName}」が見つかりません。" +
                                 $"Assets/Resources/Sfx/{fileName}.wav を置くか、Inspectorで割り当ててください。");
            }
            return loaded;
        }
    }
}