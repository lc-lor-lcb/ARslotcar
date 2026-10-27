using UnityEngine;

namespace ARSlotcar
{
    /// <summary>
    /// 車の速度に比例して、エンジン音の音量とピッチ(音の高さ)を上げる。
    /// 止まっている時は minVolume / minPitch、最高速度の時は maxVolume / maxPitch になり、その間は比例して変わる。
    ///
    /// 使い方: 車のオブジェクトにAudioSourceを付けてエンジン音のループ素材(Clip)を割り当て、
    /// このスクリプトも付ける(Loopと再生はこのスクリプトが設定するので、AudioSource側は
    /// Play On Awakeをオフにしておく)。3Dの音にしたい場合は、AudioSourceのSpatial Blendを1にする。
    ///
    /// AudioSourceへの依存は RequireComponent にしていない
    /// (車の見た目の分身=ゴーストを作る時に、AudioSourceを消せなくなるため)。
    /// </summary>
    public class CarEngineSound : MonoBehaviour
    {
        [Tooltip("速度を参照する車。未設定なら同じオブジェクトから探す")]
        [SerializeField] private CarController car;

        [Tooltip("エンジン音を鳴らすAudioSource。未設定なら同じオブジェクトから探す")]
        [SerializeField] private AudioSource source;

        [Header("速度に比例して変える範囲")]
        [Tooltip("止まっている時の音量(0〜1)。0なら止まると無音になる")]
        [Range(0f, 1f)]
        [SerializeField] private float minVolume = 0f;
        [Tooltip("最高速度の時の音量(0〜1)")]
        [Range(0f, 1f)]
        [SerializeField] private float maxVolume = 0.8f;
        [Tooltip("止まっている時のピッチ(1が素材そのままの高さ)")]
        [SerializeField] private float minPitch = 0.8f;
        [Tooltip("最高速度の時のピッチ")]
        [SerializeField] private float maxPitch = 2.0f;

        [Tooltip("音の変化を滑らかにする強さ。大きいほど、速度に素早く追従する")]
        [SerializeField] private float smoothing = 12f;

        private void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (source == null) source = GetComponent<AudioSource>();

            if (car == null) Debug.LogWarning("[CarEngineSound] CarControllerが見つかりません。", this);
            if (source == null)
            {
                Debug.LogWarning("[CarEngineSound] AudioSourceが見つかりません。車のオブジェクトにAudioSourceを付けてください。", this);
                return;
            }

            source.loop = true;
            source.playOnAwake = false;
        }

        private void Start()
        {
            if (source == null) return;

            if (source.clip == null)
            {
                Debug.LogWarning("[CarEngineSound] AudioSourceにエンジン音のClipが割り当てられていません。", this);
                return;
            }

            source.volume = minVolume;
            source.pitch = minPitch;
            source.Play();
        }

        private void Update()
        {
            if (source == null || car == null || source.clip == null) return;

            float ratio = car.SpeedRatio; // 0(停止)〜1(最高速度)
            float targetVolume = Mathf.Lerp(minVolume, maxVolume, ratio);
            float targetPitch = Mathf.Lerp(minPitch, maxPitch, ratio);

            // フレームレートに依存しない滑らかな追従
            float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            source.volume = Mathf.Lerp(source.volume, targetVolume, k);
            source.pitch = Mathf.Lerp(source.pitch, targetPitch, k);
        }
    }
}