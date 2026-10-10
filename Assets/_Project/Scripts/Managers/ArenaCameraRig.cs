using AdaptiveBossArena.Core.Services;
using AdaptiveBossArena.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 为战斗构图。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 初版被玩家形容为“随移动变得太快或太慢”，因此重写。原因是跟随点向首领混合了百分之三十，
    /// 导致只要<em>首领</em>移动，相机也会移动。不是玩家引起的运动会让人感觉相机在与自己对抗，
    /// 无论平滑多么柔和。
    /// </para>
    /// <para>
    /// 现在的规则：自由视角只响应玩家，不响应其他角色。只有玩家明确请求锁定时，首领才影响构图。
    /// </para>
    /// <para>
    /// 另外三点使相机感觉贴合，而非被拖着走：
    /// </para>
    /// <list type="bullet"><item><description>死区，让小幅调整只移动画面中的角色，而非拖动整个视图。
    /// </description></item><item><description>沿行进方向提前构图，让玩家看到要去的地方，
    /// 而非已经经过的地方。</description></item><item><description>位置与旋转分别阻尼。
    /// 共用速率会在位置调得有重量感时，使旋转显得迟钝。</description></item></list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ArenaCameraRig : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Arena configuration supplying the top-down framing values.")]
        private ArenaConfig _config;

        [Header("Targets")]
        [SerializeField]
        [Tooltip("The player. The camera answers this and, unless locked on, only this.")]
        private Transform _primaryTarget;

        [SerializeField]
        [Tooltip("The boss. Only influences framing while locked on.")]
        private Transform _secondaryTarget;

        [Header("Mode")]
        [SerializeField]
        [Tooltip("Vantage point the camera starts in.")]
        private CameraMode _mode = CameraMode.ThirdPerson;

        [SerializeField]
        [Tooltip("Generated input actions: camera orbit, lock-on and view cycling are read from here.")]
        private InputActionAsset _actions;

        [SerializeField]
        [Tooltip("Whether lock-on engages automatically at the start of a fight.")]
        private bool _lockOnByDefault;

        [Header("Orbit")]
        [SerializeField]
        [Range(0.02f, 0.5f)]
        [Tooltip("Degrees the camera turns per pixel of mouse movement, before the player's sensitivity setting.")]
        private float _mouseDegreesPerPixel = 0.22f;

        [SerializeField]
        [Range(30f, 400f)]
        [Tooltip("Degrees per second the camera turns with the right stick fully deflected.")]
        private float _stickDegreesPerSecond = 170f;

        [SerializeField]
        [Range(-30f, 0f)]
        [Tooltip("Lowest pitch the player can orbit to: looking up at the brute.")]
        private float _minimumPitchDegrees = -25f;

        [SerializeField]
        [Range(20f, 80f)]
        [Tooltip("Highest pitch the player can orbit to: looking steeply down.")]
        private float _maximumPitchDegrees = 60f;

        [SerializeField]
        [Range(0.02f, 0.5f)]
        [Tooltip("Half-life of the heading swinging round to the boss while locked on.")]
        private float _lockOnHeadingHalfLife = 0.08f;

        [Header("Third Person")]
        [SerializeField]
        [Range(1.5f, 12f)]
        [Tooltip("Distance behind the pivot at the player's shoulders.")]
        private float _distance = 2.8f;

        [SerializeField]
        [Range(0.8f, 3f)]
        [Tooltip("Height of the pivot above the player's feet: the shoulders.")]
        private float _pivotHeight = 1.55f;

        [SerializeField]
        [Range(0f, 1.5f)]
        [Tooltip("How far right of the pivot the camera sits, so it looks past the shoulder rather than through the body.")]
        private float _shoulderOffset = 0.55f;

        [SerializeField]
        [Range(-10f, 40f)]
        [Tooltip("Downward pitch the camera starts at.")]
        private float _pitchDegrees = 12f;

        [Header("Feel")]
        [SerializeField]
        [Range(0.01f, 0.6f)]
        [Tooltip("Half-life of the pivot following the player. Short: the camera is planted on the knight.")]
        private float _positionHalfLife = 0.05f;

        [SerializeField]
        [Range(0.02f, 0.6f)]
        [Tooltip("Half-life of the top-down view's rotation. The third-person and first-person views are never eased.")]
        private float _rotationHalfLife = 0.06f;

        [SerializeField]
        [Range(0.05f, 1f)]
        [Tooltip("Half-life of the boom easing back out after a column or wall pushed it in.")]
        private float _boomReturnHalfLife = 0.2f;

        [Header("First Person")]
        [SerializeField]
        [Range(1f, 2.5f)]
        [Tooltip("Eye height above the player's origin.")]
        private float _eyeHeight = 1.6f;

        private ITimeService _time;

        /// <summary>相机旋转支点：以较短半衰期跟随的玩家位置。</summary>
        private Vector3 _anchor;

        /// <summary>相机臂当前长度，障碍出现时立即缩短，之后平滑伸出。</summary>
        private float _boom;

        /// <summary>沿相机臂扫掠的球体半径，避免镜头擦入立柱。</summary>
        private const float BoomProbeRadius = 0.25f;

        /// <summary>玩家设置的灵敏度倍率。</summary>
        private float _sensitivity = 1f;

        /// <summary>鼠标上移时是否向下看。</summary>
        private bool _invertLook;

        /// <summary>用于判断是否倒地的骑士，从主目标解析。</summary>
        private Player.PlayerController _player;

        /// <summary>站立为零、倒地为一，平滑变化以免构图跳变。</summary>
        private float _downBlend;

        /// <summary>骑士倒地时的支点高度，指向身体而非原先肩膀位置。</summary>
        private const float DownPivotHeight = 0.7f;

        /// <summary>骑士倒地时的相机臂长度，拉远以看见整个身体。</summary>
        private const float DownDistance = 3.8f;

        private const float DownBlendHalfLife = 0.2f;

        /// <summary>柱墙缩短之前，构图希望采用的相机臂长度。</summary>
        private float CurrentDistance => Mathf.Lerp(_distance, DownDistance, _downBlend);

        /// <summary>本帧竞技场墙壁削减的相机臂长度，单位为米。</summary>
        private float _boomLoss;

        /// <summary>
        /// 相机与环形墙保持的距离，足以避免近裁剪平面碰到矮墙，使画面不会一半都被石材遮住。
        /// </summary>
        private const float WallClearance = 0.6f;

        /// <summary>墙壁每削减一米相机臂时，相机上升的米数，以俯看玩家。</summary>
        private const float RisePerLostMetre = 0.45f;

        /// <summary>相机臂完全收缩时额外增加的俯角，使升高后的相机仍能框住玩家。</summary>
        private const float PitchAtCollapsedBoom = 32f;
        private bool _isLockedOn;

        /// <summary>收尾双人镜头的剩余秒数；正常战斗镜头时为零。</summary>
        private float _finisherRemaining;

        /// <summary>首领入场镜头的剩余秒数，或零。</summary>
        private float _entranceRemaining;

        /// <summary>
        /// 入场最后一段时间，用来将镜头混合回普通构图。
        /// </summary>
        /// <remarks>
        /// 结束时恰好位于相机平时在骑士背后的位置，因此交还战斗时不会切镜或猛甩视角；
        /// 镜头已经稳定在玩家需要的位置。
        /// </remarks>
        private const float EntranceBlendSeconds = 0.9f;

        /// <summary>入场镜头位于首领前方的距离。</summary>
        private const float EntranceDistance = 5f;

        /// <summary>入场镜头注视点的高度，即巨兽胸口。</summary>
        private const float EntranceHeight = 1.9f;

        /// <summary>镜头低于胸口的高度，以仰视巨兽。</summary>
        private const float EntranceLensDrop = 0.6f;

        private InputAction _look;
        private InputAction _lockOn;
        private InputAction _cycleCamera;

        /// <summary>第三人称相机的朝向，绕竖直轴的角度。</summary>
        private float _yawDegrees;

        /// <summary>墙壁额外调整前，第三人称相机的向下俯角，单位为度。</summary>
        private float _orbitPitchDegrees;

        /// <summary>当前使用的视角。</summary>
        public CameraMode Mode => _mode;

        /// <summary>相机正将首领保持在画面内时为 true。</summary>
        public bool IsLockedOn => _isLockedOn && _secondaryTarget != null;

        private void Start()
        {
            if (_config == null)
            {
                Debug.LogError(
                    "[Adaptive Boss Arena] ArenaCameraRig has no ArenaConfig assigned and has been disabled.",
                    this);
                enabled = false;
                return;
            }

            ServiceRegistry.Current.TryGet(out _time);

            if (_primaryTarget != null)
            {
                _anchor = _primaryTarget.position;
            }

            // 开场即锁定。这里只有一个对手的决斗，因此保持双方都可见的构图应成为合理默认值，
            // 而非需要玩家自己发现的功能。
            _isLockedOn = _lockOnByDefault;

            ResolveActions();

            _orbitPitchDegrees = _pitchDegrees;
            _yawDegrees = HeadingDegrees(_primaryTarget != null ? _primaryTarget.forward : Vector3.forward);

            if (IsLockedOn)
            {
                _yawDegrees = HeadingDegrees(_secondaryTarget.position - _primaryTarget.position);
            }

            SnapToTarget();
        }

        private void Update()
        {
            // 逐帧读取，使设置菜单滑块一移动，设置就立即生效。
            SetLookSettings(LookSettings.Sensitivity, LookSettings.InvertY);

            if (_cycleCamera != null && _cycleCamera.WasPressedThisFrame())
            {
                CycleMode();
            }

            if (_lockOn != null && _lockOn.WasPressedThisFrame())
            {
                _isLockedOn = !_isLockedOn;
            }

            UpdateOrbit(Time.unscaledDeltaTime);
        }

        /// <summary>分配相机读取的输入动作，供场景生成器使用。</summary>
        /// <param name="actions">生成的输入动作资源。</param>
        public void SetInputActions(InputActionAsset actions) => _actions = actions;

        /// <summary>
        /// 用玩家鼠标或摇杆旋转环绕视角，锁定时则转向首领。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 此前没有读取鼠标：自由相机沿骑士朝向看，而移动又相对相机进行，所以按侧向键会旋转骑士、
        /// 再旋转相机、再改变按键方向，导致骑士绕圈。现在环绕视角完全由玩家控制。
        /// </para>
        /// <para>
        /// 只有捕获光标时鼠标才旋转相机，因此在菜单上移动指针不会甩动视角。采用未缩放时间，
        /// 使顿帧与慢动作期间仍可控制相机。
        /// </para>
        /// </remarks>
        private void UpdateOrbit(float deltaTime)
        {
            Vector2 turn = LookDelta(deltaTime);

            if (_invertLook)
            {
                turn.y = -turn.y;
            }

            _orbitPitchDegrees = Mathf.Clamp(_orbitPitchDegrees - turn.y, _minimumPitchDegrees, _maximumPitchDegrees);

            if (IsLockedOn)
            {
                Vector3 toBoss = _secondaryTarget.position - _primaryTarget.position;
                toBoss.y = 0f;

                if (toBoss.sqrMagnitude > Mathf.Epsilon)
                {
                    float target = HeadingDegrees(toBoss);
                    _yawDegrees = Mathf.LerpAngle(_yawDegrees, target, DampFactor(_lockOnHeadingHalfLife, deltaTime));
                }

                return;
            }

            _yawDegrees += turn.x;
        }

        /// <summary>本帧环绕输入，单位为度：x 改变朝向，y 抬高视角。</summary>
        private Vector2 LookDelta(float deltaTime)
        {
            if (_look == null)
            {
                return Vector2.zero;
            }

            Vector2 value = _look.ReadValue<Vector2>();

            if (_look.activeControl?.device is Mouse)
            {
                return CursorLock.IsCaptured ? value * (_mouseDegreesPerPixel * _sensitivity) : Vector2.zero;
            }

            return value * (_stickDegreesPerSecond * _sensitivity * deltaTime);
        }

        /// <summary>
        /// 应用玩家的视角设置。
        /// </summary>
        /// <param name="sensitivity">单位鼠标或摇杆输入的视角转动倍率，默认为一。</param>
        /// <param name="invert">鼠标上移时是否向下看。</param>
        public void SetLookSettings(float sensitivity, bool invert)
        {
            _sensitivity = Mathf.Clamp(sensitivity, 0.1f, 4f);
            _invertLook = invert;
        }

        private void ResolveActions()
        {
            InputActionMap map = _actions != null
                ? _actions.FindActionMap(Player.Controls.InputActionNames.GameplayMap, throwIfNotFound: false)
                : null;

            if (map == null)
            {
                return;
            }

            _look = map.FindAction(Player.Controls.InputActionNames.Look, throwIfNotFound: false);
            _lockOn = map.FindAction(Player.Controls.InputActionNames.LockOn, throwIfNotFound: false);
            _cycleCamera = map.FindAction(Player.Controls.InputActionNames.CycleCamera, throwIfNotFound: false);

            // 玩家读取器启用同一映射；此处也启用，以覆盖相机更早启动的情况。
            map.Enable();
        }

        /// <summary>水平方向的朝向角，世界前方为零。</summary>
        private static float HeadingDegrees(Vector3 direction) =>
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        private void LateUpdate()
        {
            if (_primaryTarget == null)
            {
                return;
            }

            // 入场期间战斗暂停，因此采用未缩放时间。
            if (_entranceRemaining > 0f && _secondaryTarget != null)
            {
                _entranceRemaining -= Time.unscaledDeltaTime;
                FrameEntrance();
                return;
            }

            // 采用未缩放时间：收尾镜头应在自身慢动作期间正常播放，不随之缓慢推进。
            if (_finisherRemaining > 0f)
            {
                _finisherRemaining -= Time.unscaledDeltaTime;

                if (_secondaryTarget != null)
                {
                    FrameFinisher(Time.unscaledDeltaTime);
                    return;
                }
            }

            float deltaTime = _time?.DeltaTime ?? Time.deltaTime;

            UpdateAnchor(deltaTime);

            if (_mode == CameraMode.FirstPerson || _mode == CameraMode.ThirdPerson)
            {
                // 直接放置，而非平滑。第一人称的任何延迟都会像世界在滑动。
                // 第三人称支点已经以短半衰期跟随骑士，视角转动量应与鼠标完全一致；
                // 再平滑旋转会使相机落后玩家手部动作并漂浮追赶。
                transform.SetPositionAndRotation(DesiredPosition(), DesiredRotation());
                return;
            }

            transform.position = MathUtil.Damp(
                transform.position, DesiredPosition(), _positionHalfLife, deltaTime);

            transform.rotation = Quaternion.Slerp(
                transform.rotation, DesiredRotation(), DampFactor(_rotationHalfLife, deltaTime));
        }

        /// <summary>
        /// 致命一击时切到双方同时入镜的镜头。
        /// </summary>
        /// <remarks>
        /// 从侧面低处近距离构图，使处决成为双方之间的瞬间，而非与战斗其他时刻一样越过骑士肩膀观察。
        /// </remarks>
        /// <param name="seconds">保持镜头的时长。</param>
        public void PlayFinisher(float seconds) => _finisherRemaining = Mathf.Max(_finisherRemaining, seconds);

        /// <summary>
        /// 开场聚焦首领：从其前方低机位观察，战斗开始时平滑返回骑士身后。
        /// </summary>
        /// <param name="seconds">完整入场时长，包含混合过渡。</param>
        public void PlayEntrance(float seconds) => _entranceRemaining = Mathf.Max(0f, seconds);

        /// <summary>立即停止入场镜头，使相机处于普通构图。</summary>
        public void EndEntrance()
        {
            if (_entranceRemaining > 0f)
            {
                _entranceRemaining = 0f;
                SnapToTarget();
            }
        }

        /// <summary>立即将相机放到构图位置。</summary>
        public void SnapToTarget()
        {
            if (_primaryTarget == null)
            {
                return;
            }

            _anchor = _primaryTarget.position;
            _boom = _distance;

            // 直接定位等于切镜，因此锁定相机直接切到首领方向，而非慢慢转过去。
            if (IsLockedOn)
            {
                Vector3 toBoss = _secondaryTarget.position - _primaryTarget.position;

                if (new Vector2(toBoss.x, toBoss.z).sqrMagnitude > Mathf.Epsilon)
                {
                    _yawDegrees = HeadingDegrees(toBoss);
                }
            }

            transform.SetPositionAndRotation(DesiredPosition(), DesiredRotation());
        }

        /// <summary>切换视角并立即重新构图。</summary>
        /// <param name="mode">要采用的模式。</param>
        public void SetMode(CameraMode mode)
        {
            _mode = mode;
            SnapToTarget();
        }

        /// <summary>按顺序切换到下一个视角。</summary>
        public void CycleMode() =>
            SetMode(_mode == CameraMode.FirstPerson ? CameraMode.TopDown : _mode + 1);

        /// <summary>分配构图目标，供场景生成器使用。</summary>
        /// <param name="primary">玩家。</param>
        /// <param name="secondary">首领，可以为 null。</param>
        public void SetTargets(Transform primary, Transform secondary)
        {
            _primaryTarget = primary;
            _secondaryTarget = secondary;
        }

        /// <summary>分配配置，供场景生成器使用。</summary>
        /// <param name="config">竞技场配置。</param>
        public void SetConfig(ArenaConfig config) => _config = config;

        /// <summary>
        /// 应用死区与提前构图，推进相机真正框取的锚点。
        /// </summary>
        /// <remarks>
        /// 锚点跟随玩家，但不等于玩家位置。在死区内它完全不动，避免小幅修正拖动整个视图，
        /// 这也是相机感觉贴合而非拖拽的主要区别。
        /// </remarks>
        /// <summary>
        /// 以较短半衰期让支点跟随玩家。
        /// </summary>
        /// <remarks>
        /// 以前有骑士可以自由移动的死区，以及沿骑士朝向的提前偏移。两者结合会让视角先漂移再追赶，
        /// 呈现游动感，且骑士每次转向都令视图侧滑。现在支点只保持在骑士位置，落后几百分之一秒，
        /// 让画面显得扎实。
        /// </remarks>
        private void UpdateAnchor(float deltaTime)
        {
            _anchor = MathUtil.Damp(_anchor, _primaryTarget.position, _positionHalfLife, deltaTime);
            _downBlend = MathUtil.Damp(_downBlend, IsPlayerDown() ? 1f : 0f, DownBlendHalfLife, deltaTime);
        }

        /// <summary>
        /// 骑士是否倒地或腾空：被击倒、抛飞或死亡。
        /// </summary>
        /// <remarks>
        /// 支点原本位于站立肩高；身体倒地时常向相机方向抛回，会让镜头进入骑士头盔。
        /// 倒地时支点降低到身体，相机臂拉远。读取的是观察者能看见的状态，与巨兽感知的快照相同。
        /// </remarks>
        private bool IsPlayerDown()
        {
            if (_player == null && _primaryTarget != null)
            {
                _player = _primaryTarget.GetComponent<Player.PlayerController>();
            }

            if (_player == null)
            {
                return false;
            }

            Core.Perception.ObservableActionState state = _player.CaptureObservation(Time.time).ActionState;

            return state == Core.Perception.ObservableActionState.KnockedDown ||
                   state == Core.Perception.ObservableActionState.Airborne ||
                   state == Core.Perception.ObservableActionState.Dead;
        }

        /// <summary>当前视角希望采用的相机位置。</summary>
        private Vector3 DesiredPosition()
        {
            switch (_mode)
            {
                case CameraMode.FirstPerson:
                    return _primaryTarget.position + Vector3.up * _eyeHeight;

                case CameraMode.TopDown:
                    return FocusPoint()
                           + new Vector3(0f, _config.CameraHeight, -_config.CameraDistance);

                default:
                    Vector3 pivot = _anchor + Vector3.up * Mathf.Lerp(_pivotHeight, DownPivotHeight, _downBlend);
                    Quaternion view = OrbitRotation(OrbitForward(), _orbitPitchDegrees);
                    Vector3 desired = ShoulderPosition(pivot, view, BoomLength(pivot, view), _shoulderOffset);

                    // 击退和墙壁推挤经常把玩家压到环形边界，此时相机臂曾让相机越过边界，看到墙背。
                    // 现在限制在边界内，按损失的距离升高，从玩家上方观察。
                    Vector3 confined = ConfineToArena(desired, pivot, _config.Radius - WallClearance, out _boomLoss);
                    confined.y += _boomLoss * RisePerLostMetre;

                    return confined;
            }
        }

        /// <summary>当前视角的方向。</summary>
        private Quaternion DesiredRotation()
        {
            switch (_mode)
            {
                case CameraMode.FirstPerson:
                    return Quaternion.LookRotation(OrbitForward(), Vector3.up);

                case CameraMode.TopDown:
                    return Quaternion.Euler(_config.CameraPitchDegrees, 0f, 0f);

                default:
                    float collapsed = _distance > 0f ? Mathf.Clamp01(_boomLoss / _distance) : 0f;
                    return OrbitRotation(OrbitForward(), _orbitPitchDegrees + PitchAtCollapsedBoom * collapsed);
            }
        }

        /// <summary>
        /// 越肩视角的相机位置：沿视线位于支点后方，并偏向一侧。
        /// </summary>
        /// <remarks>
        /// 相机视线与经过支点的直线平行，而非看向支点，因此骑士位于画面左侧，
        /// 前方巨兽所在空间占据其余区域。若从近处背后看向骑士中心，骑士背部会遮住玩家需要观察的一切。
        /// </remarks>
        /// <param name="pivot">视角旋转支点，即骑士肩膀。</param>
        /// <param name="view">相机方向。</param>
        /// <param name="boom">沿视线位于支点后方的距离。</param>
        /// <param name="shoulder">位于支点右侧的距离。</param>
        /// <returns>相机位置。</returns>
        public static Vector3 ShoulderPosition(Vector3 pivot, Quaternion view, float boom, float shoulder) =>
            pivot - view * Vector3.forward * boom + view * Vector3.right * shoulder;

        /// <summary>
        /// 立柱或墙壁挡在相机与骑士之间之前，相机臂允许的长度。
        /// </summary>
        /// <remarks>
        /// 出现遮挡时立即缩短，镜头进入立柱就看不到内容；之后平滑恢复，避免走过柱子时视角来回跳变。
        /// </remarks>
        private float BoomLength(Vector3 pivot, Quaternion view)
        {
            float distance = CurrentDistance;
            Vector3 full = ShoulderPosition(pivot, view, distance, _shoulderOffset);
            Vector3 along = full - pivot;
            float reach = along.magnitude;
            float allowed = distance;

            if (reach > Mathf.Epsilon &&
                Physics.SphereCast(pivot, BoomProbeRadius, along / reach, out RaycastHit hit, reach,
                    1 << Core.Constants.Layers.Arena, QueryTriggerInteraction.Ignore))
            {
                allowed = Mathf.Max(0.4f, distance * (hit.distance / reach));
            }

            _boom = allowed < _boom || _boom <= 0f
                ? allowed
                : MathUtil.Damp(_boom, allowed, _boomReturnHalfLife, Time.unscaledDeltaTime);

            return _boom;
        }

        /// <summary>
        /// 沿构图点到相机的直线，将离开竞技场的相机位置拉回边界内。
        /// </summary>
        /// <remarks>
        /// 沿相机臂而非直接朝圆心拉回，使相机保留原来的观察方向，只变得更近。高度不变；
        /// 损失距离应对应升高多少属于构图，由调用者负责。只限制水平面，因为墙壁是竖直环。
        /// </remarks>
        /// <param name="desired">相机希望采用的位置。</param>
        /// <param name="focus">构图目标点，假定在环内。</param>
        /// <param name="radius">相机必须保持其内的水平半径。</param>
        /// <param name="lost">水平相机臂被削减的长度，单位为米。</param>
        /// <returns>限制后的位置。</returns>
        public static Vector3 ConfineToArena(Vector3 desired, Vector3 focus, float radius, out float lost)
        {
            lost = 0f;

            var point = new Vector2(desired.x, desired.z);

            if (point.sqrMagnitude <= radius * radius)
            {
                return desired;
            }

            var from = new Vector2(focus.x, focus.z);
            Vector2 boom = point - from;

            // 相机臂离开圆的位置：|from + t * boom| = radius，取出口根。
            float a = Vector2.Dot(boom, boom);
            float b = 2f * Vector2.Dot(from, boom);
            float c = Vector2.Dot(from, from) - radius * radius;
            float discriminant = b * b - 4f * a * c;

            if (a <= Mathf.Epsilon || discriminant < 0f || c > 0f)
            {
                // 构图点本身已在环外；最佳位置是圆周上最近的点。
                Vector2 onRing = point.normalized * radius;
                lost = Vector2.Distance(point, onRing);
                return new Vector3(onRing.x, desired.y, onRing.y);
            }

            float t = Mathf.Clamp01((-b + Mathf.Sqrt(discriminant)) / (2f * a));
            Vector2 kept = from + boom * t;

            lost = boom.magnitude * (1f - t);

            return new Vector3(kept.x, desired.y, kept.y);
        }

        /// <summary>第三人称方向：沿朝向观察，再按俯角向下倾斜。</summary>
        /// <remarks>
        /// 先设置朝向，再在相机自身坐标系应用俯角。过去先绕世界 X 轴旋转，只有看向 Z 轴时才是俯仰；
        /// 看向 X 轴时相同旋转变为滚转，因此首领位于一侧时地平线会倾倒。
        /// </remarks>
        /// <param name="heading">要观察的水平方向。</param>
        /// <param name="pitchDegrees">向下俯视的角度，单位为度。</param>
        /// <returns>相机旋转。</returns>
        public static Quaternion OrbitRotation(Vector3 heading, float pitchDegrees) =>
            Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(pitchDegrees, 0f, 0f);

        /// <summary>
        /// 相机观察的方向。
        /// </summary>
        /// <remarks>
        /// 环绕朝向：自由视角由玩家控制，锁定时转为从玩家指向首领。绝不采用骑士朝向，否则会使他绕圈。
        /// </remarks>
        private Vector3 OrbitForward() => Quaternion.Euler(0f, _yawDegrees, 0f) * Vector3.forward;

        /// <summary>
        /// 当前构图的目标点。
        /// </summary>
        /// <remarks>
        /// 仅锁定时向首领偏移。自由视角只使用锚点，从而修复相机响应首领而非玩家的问题。
        /// </remarks>
        private Vector3 FocusPoint()
        {
            Vector3 focus = _anchor;

            if (IsLockedOn)
            {
                focus = Vector3.Lerp(focus, _secondaryTarget.position, _config.CameraBiasTowardBoss);
            }

            focus.y = _anchor.y;
            return focus;
        }

        /// <summary>
        /// 从首领前方仰视，并在入场最后一段混合回普通镜头。
        /// </summary>
        private void FrameEntrance()
        {
            Vector3 chest = _secondaryTarget.position + Vector3.up * EntranceHeight;
            Vector3 front = _secondaryTarget.forward;
            front.y = 0f;
            front = front.sqrMagnitude > Mathf.Epsilon ? front.normalized : Vector3.back;

            Vector3 shotPosition = ConfineToArena(
                chest + front * EntranceDistance - Vector3.up * EntranceLensDrop, chest,
                _config.Radius - WallClearance, out float _);
            Quaternion shotRotation = Quaternion.LookRotation(chest - shotPosition, Vector3.up);

            // 按直接定位方式计算普通构图，使混合精确到达目标。
            _anchor = _primaryTarget.position;
            Vector3 normalPosition = DesiredPosition();
            Quaternion normalRotation = DesiredRotation();

            float shot = Mathf.Clamp01(_entranceRemaining / EntranceBlendSeconds);
            float eased = shot * shot * (3f - 2f * shot);

            transform.SetPositionAndRotation(
                Vector3.Lerp(normalPosition, shotPosition, eased),
                Quaternion.Slerp(normalRotation, shotRotation, eased));
        }

        /// <summary>从侧面近距离、略低的位置保持双方入镜。</summary>
        private void FrameFinisher(float deltaTime)
        {
            Vector3 middle = (_primaryTarget.position + _secondaryTarget.position) * 0.5f + Vector3.up * FinisherHeight;
            Vector3 between = _secondaryTarget.position - _primaryTarget.position;
            between.y = 0f;

            Vector3 side = between.sqrMagnitude > Mathf.Epsilon
                ? Vector3.Cross(Vector3.up, between.normalized)
                : Vector3.right;

            Vector3 desired = middle + side * FinisherDistance;
            Vector3 confined = ConfineToArena(desired, middle, _config.Radius - WallClearance, out float _);

            transform.position = MathUtil.Damp(transform.position, confined, FinisherHalfLife, deltaTime);

            Vector3 toMiddle = middle - transform.position;

            if (toMiddle.sqrMagnitude > Mathf.Epsilon)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, Quaternion.LookRotation(toMiddle, Vector3.up),
                    DampFactor(FinisherHalfLife, deltaTime));
            }
        }

        /// <summary>收尾镜头相对于双方的侧向距离。</summary>
        private const float FinisherDistance = 4.2f;

        /// <summary>收尾镜头距角色脚部的高度，采用胸高而非俯视。</summary>
        private const float FinisherHeight = 1.4f;

        /// <summary>收尾镜头稳定到位的速度。</summary>
        private const float FinisherHalfLife = 0.12f;

        /// <summary>根据半衰期计算与帧率无关的插值系数。</summary>
        private static float DampFactor(float halfLifeSeconds, float deltaTime) =>
            1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, halfLifeSeconds));
    }
}
