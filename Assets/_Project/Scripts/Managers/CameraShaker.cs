using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Utilities.Feel;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 受击时偏移相机，并将自身注册为游戏的震动接收器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 把局部偏移应用到相机装置的子对象，避免震动与构图相互干扰。直接震动装置会让跟随平滑追逐震动，
    /// 将其拖成漂移。
    /// </para>
    /// <para>
    /// 与跟随不同，刻意使用未缩放时间。顿帧冻结世界是为了强化命中感；若震动一起冻结，
    /// 就会移除表现冲击力最重要的运动。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CameraShaker : MonoBehaviour, IScreenShake
    {
        /// <summary>视野角冲击上限，避免连续闪避使视野过度膨胀。</summary>
        private const float MaxFovKick = 10f;

        [Header("Intensity")]
        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("Peak positional offset in world units at full trauma.")]
        private float _maxTranslation = 0.55f;

        [SerializeField]
        [Range(0f, 10f)]
        [Tooltip("Peak camera roll in degrees at full trauma.")]
        private float _maxRollDegrees = 2.5f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Player-facing intensity scale. Zero disables shake entirely, which some players " +
                 "need rather than merely prefer.")]
        private float _userIntensity = 1f;

        [Header("Response")]
        [SerializeField]
        [Range(0.5f, 5f)]
        [Tooltip("Trauma removed per second. Higher values make shakes shorter and snappier.")]
        private float _decayPerSecond = 1.8f;

        [SerializeField]
        [Range(5f, 40f)]
        [Tooltip("Noise sampling rate. Higher values feel sharper and more violent.")]
        private float _frequency = 24f;

        [Header("Punch")]
        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Largest dolly-in offset a single punch can reach, in world units.")]
        private float _maxPunch = 0.35f;

        [SerializeField]
        [Range(1f, 12f)]
        [Tooltip("How fast a punch springs back. Higher is snappier.")]
        private float _punchDecayPerSecond = 5f;

        [SerializeField]
        [Range(2f, 20f)]
        [Tooltip("How fast a field-of-view kick springs back.")]
        private float _fovDecayPerSecond = 7f;

        private TraumaShake _shake;
        private Camera _camera;
        private Vector3 _restLocalPosition;
        private float _baseFieldOfView;
        private float _punch;
        private float _fovKick;

        private void Awake()
        {
            _shake = new TraumaShake(_decayPerSecond, _frequency);
            _restLocalPosition = transform.localPosition;

            // 用于视野角冲击动画的透视相机。解析方式宽容：震动器通常直接挂在相机上，
            // 但相机位于装置其他位置时也能工作。
            _camera = GetComponent<Camera>() ?? GetComponentInChildren<Camera>() ?? Camera.main;
            if (_camera != null)
            {
                _baseFieldOfView = _camera.fieldOfView;
            }

            ServiceRegistry.Current.RegisterOrReplace<IScreenShake>(this);
        }

        private void LateUpdate()
        {
            float deltaTime = Time.unscaledDeltaTime;

            _shake.Tick(deltaTime);
            _punch = Mathf.Max(0f, _punch - _punchDecayPerSecond * deltaTime);

            // 视野角冲击从正负两侧弹回零；幅度越大恢复越快，并设置最小恢复量，确保最终精确静止，
            // 而非渐近停在零附近。
            _fovKick = Mathf.MoveTowards(
                _fovKick, 0f, _fovDecayPerSecond * (Mathf.Abs(_fovKick) + 1f) * deltaTime);

            if (_camera != null)
            {
                _camera.fieldOfView = _baseFieldOfView + _fovKick * _userIntensity;
            }

            if (!_shake.IsShaking && _punch <= 0f)
            {
                // 直接归零而非继续平滑，避免永久残留亚像素偏移，并在长时间战斗中累计。
                transform.localPosition = _restLocalPosition;
                transform.localRotation = Quaternion.identity;
                return;
            }

            Vector2 translation = Vector2.zero;
            float rollDegrees = 0f;

            if (_shake.IsShaking)
            {
                _shake.Sample(
                    _maxTranslation * _userIntensity,
                    _maxRollDegrees * _userIntensity,
                    out translation,
                    out rollDegrees);
            }

            // 冲击沿局部前方（+Z）推向相机构图目标，然后自行弹回。它与震动共用强度缩放，
            // 因此一个无障碍滑块即可同时减弱两者。
            float punch = _punch * _userIntensity;

            transform.localPosition = _restLocalPosition + new Vector3(translation.x, translation.y, punch);
            transform.localRotation = Quaternion.Euler(0f, 0f, rollDegrees);
        }

        /// <inheritdoc />
        public void AddTrauma(float amount)
        {
            if (_userIntensity <= 0f)
            {
                return;
            }

            _shake.AddTrauma(amount);
        }

        /// <inheritdoc />
        public void Punch(float amount)
        {
            if (_userIntensity <= 0f)
            {
                return;
            }

            _punch = Mathf.Min(_maxPunch, _punch + Mathf.Max(0f, amount));
        }

        /// <inheritdoc />
        public void PunchFov(float degrees)
        {
            if (_userIntensity <= 0f)
            {
                return;
            }

            // 限制幅度，避免快速连续闪避使视野膨胀；采用最大一次冲击，而非它们的总和。
            _fovKick = Mathf.Clamp(_fovKick + degrees, -MaxFovKick, MaxFovKick);
        }

        /// <inheritdoc />
        public void ClearTrauma()
        {
            _shake.Reset();
            _punch = 0f;
            _fovKick = 0f;

            if (_camera != null)
            {
                _camera.fieldOfView = _baseFieldOfView;
            }
        }

        /// <summary>从设置菜单设定面向玩家的强度缩放。</summary>
        /// <param name="intensity">零至一的缩放值，零表示关闭。</param>
        public void SetUserIntensity(float intensity) => _userIntensity = Mathf.Clamp01(intensity);
    }
}
