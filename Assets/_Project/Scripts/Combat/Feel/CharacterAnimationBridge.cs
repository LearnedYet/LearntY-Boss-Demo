using AdaptiveBossArena.Core.Perception;
using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 用程序化动画器读取的同一状态流驱动骨架角色的 Animator。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 角色状态机决定它正在做什么；这里仅决定该动作看起来怎样。
    /// 为可观察动作选择一个 Animator 状态——移动、翻滚、防御、硬直、
    /// 死亡或进行中的攻击——并在变化时交叉淡化。生成的控制器没有自身转换，
    /// 因此角色动作只有一个事实来源，且它不是 Animator。
    /// </para>
    /// <para>
    /// 攻击不按片段自身速度自由播放。片段根据攻击的经过时间通过 <see cref="AttackClipTimeWarp"/> 定
    /// 位，让刀刃在命中盒生效时落下，并在顿帧中冻结在命中姿势。仅用于表现：这里不向战斗或首领提供信息。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CharacterAnimationBridge : MonoBehaviour
    {
        private const float CrossFadeSeconds = 0.08f;

        /// <summary>从站立或移动混合到挥击。</summary>
        /// <remarks>
        /// 0.04 秒看起来像跳变：整个身体突然进入前摇首个姿势。十分之一秒仍处于挥击自身的前摇内，
        /// 而前摇由攻击时钟定位，因此不会损失时序，只会消除跳变。
        /// </remarks>
        private const float EnterAttackCrossFadeSeconds = 0.1f;

        /// <summary>从连段的一次挥击混合到下一次或翻滚：身体已在运动，因此应迅速。</summary>
        private const float ChainCrossFadeSeconds = 0.06f;

        /// <summary>生效窗口播放的接触帧周围片段时间，以片段比例表示。</summary>
        private const float ContactSpanFraction = 0.08f;

        private static readonly int SpeedParam = Animator.StringToHash(CharacterAnimatorParameters.Speed);
        private static readonly int MoveXParam = Animator.StringToHash(CharacterAnimatorParameters.MoveX);
        private static readonly int MoveZParam = Animator.StringToHash(CharacterAnimatorParameters.MoveZ);

        /// <summary>方向混合跟随方向变化的速度，避免转向使腿部跳变。</summary>
        private const float DirectionDampSeconds = 0.12f;

        private Vector3 _lastRootPosition;
        private bool _hasRootPosition;
        private Vector2 _blendPosition;
        private static readonly int AttackTimeParam = Animator.StringToHash(CharacterAnimatorParameters.AttackTime);
        private static readonly int ReactionTimeParam = Animator.StringToHash(CharacterAnimatorParameters.ReactionTime);

        private Animator _animator;
        private CharacterAnimationConfig _config;

        private string _currentState;
        private AttackDefinition _currentAttack;

        /// <summary>上半身受击层索引；控制器没有该层时为 -1。</summary>
        private int _hitLayer = -1;

        /// <summary>上次受击动作开始以来的秒数；没有播放时为负数。</summary>
        private float _hitElapsed = -1f;

        private float _hitLength;

        /// <summary>受击动作达到完整权重所需秒数，必须足够快以在命中帧呈现打击。</summary>
        private const float HitAttackSeconds = 0.05f;

        /// <summary>受击片段中反作用最深的进度，也是打击进入的位置。</summary>
        private const float HitPeakFraction = 0.2f;

        /// <summary>
        /// 受击动作中用于缓动回到底层姿态的比例，让躯干逐渐稳定，而不是突然恢复。
        /// </summary>
        private const float HitReleaseFraction = 0.45f;

        /// <summary>存在带 Animator 的骨架可供驱动时为 true。</summary>
        /// <remarks>
        /// Animator 关闭时为 false，布娃娃在死亡时正是如此处理：尸体由物理系统控制，
        /// 向禁用 Animator 请求交叉淡化只会记录警告。
        /// </remarks>
        public bool HasSkeleton =>
            _animator != null && _animator.enabled && _animator.runtimeAnimatorController != null;

        private void Awake()
        {
            // 只解析一次。美术资源通过重新生成预制体加入，而不是在运行时加入。
            _animator = GetComponentInChildren<Animator>(includeInactive: true);

            var procedural = GetComponent<CharacterAnimator>();
            _config = procedural != null ? procedural.Config : null;

            _hitLayer = _animator != null && _animator.runtimeAnimatorController != null
                ? _animator.GetLayerIndex(CharacterAnimatorParameters.HitLayer)
                : -1;
        }

        /// <summary>
        /// 在持械形态和徒手战斗的狂暴形态之间切换身体。
        /// </summary>
        /// <remarks>
        /// 两个控制器在同一骨架上使用相同状态名称，因此驱动此桥接组件的对象无需知道正在播放哪个：
        /// 下一状态请求进入新控制器，战斗继续。武器随之隐藏，因为狂暴片段采用空手。
        /// </remarks>
        /// <param name="frenzied">true 表示徒手战斗，false 表示返回持械形态。</param>
        public void SetFrenzy(bool frenzied)
        {
            RuntimeAnimatorController wanted = frenzied && _config != null && _config.FrenzyController != null
                ? _config.FrenzyController
                : _config?.AnimatorController;

            if (_animator == null || wanted == null || _animator.runtimeAnimatorController == wanted)
            {
                return;
            }

            _animator.runtimeAnimatorController = wanted;
            _frenzied = frenzied && wanted == _config.FrenzyController;

            // 层索引属于控制器，因此必须重新查找。
            _hitLayer = _animator.GetLayerIndex(CharacterAnimatorParameters.HitLayer);
            _currentState = null;

            SetWeaponVisible(!frenzied);
        }

        /// <summary>是否正在播放狂暴控制器；它按自身片段确定每次打击时机。</summary>
        private bool _frenzied;

        /// <summary>显示或隐藏挂接武器，狂暴形态已将其丢弃。</summary>
        private void SetWeaponVisible(bool visible)
        {
            if (_animator == null || !_animator.isHuman)
            {
                return;
            }

            Transform hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform weapon = hand != null ? hand.Find("Cleaver") : null;

            if (weapon != null)
            {
                weapon.gameObject.SetActive(visible);
            }
        }

        private void Update()
        {
            if (_hitElapsed < 0f || !HasSkeleton || _hitLayer < 0)
            {
                return;
            }

            if (_hitLength <= 0f)
            {
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(_hitLayer);
                _hitLength = info.IsName(CharacterAnimatorParameters.HitState) && info.length > 0.05f ? info.length : 0.6f;
            }

            // 使用游戏时间：顿帧让受击动作停在命中姿势，结束后播放释放动作。
            _hitElapsed += Time.deltaTime;

            float release = _hitLength * HitReleaseFraction;
            float weight = _hitElapsed < HitAttackSeconds
                ? _hitElapsed / HitAttackSeconds
                : Mathf.Clamp01((_hitLength - _hitElapsed) / Mathf.Max(0.01f, release));

            _animator.SetLayerWeight(_hitLayer, weight);

            if (_hitElapsed >= _hitLength)
            {
                _hitElapsed = -1f;
                _animator.SetLayerWeight(_hitLayer, 0f);
            }
        }

        /// <summary>推送本帧可观察状态，以及存在时的进行中攻击。</summary>
        /// <param name="state">旁观者看到角色正在进行的动作。</param>
        /// <param name="attackPhase">进行中攻击的阶段。</param>
        /// <param name="planarSpeed01">水平速度占最高速度的比例。</param>
        /// <param name="attack">进行中的攻击；也可为 null。</param>
        /// <param name="attackElapsedSeconds">自该攻击开始以来的时间。</param>
        /// <param name="reactionProgress01">从地面起身的进度；倒地时为零。</param>
        public void SetMotionState(
            ObservableActionState state,
            AttackPhase attackPhase,
            float planarSpeed01,
            AttackDefinition attack,
            float attackElapsedSeconds,
            float reactionProgress01 = 0f)
        {
            if (!HasSkeleton)
            {
                return;
            }

            _animator.SetFloat(SpeedParam, Mathf.Clamp01(planarSpeed01));
            _animator.SetFloat(ReactionTimeParam, Mathf.Clamp01(reactionProgress01));
            DriveDirection(planarSpeed01);

            bool attacking = attack != null && attackPhase != AttackPhase.Inactive && IsAttackState(state);
            string target = attacking ? AttackStateFor(attack) : StateFor(state);

            // 按游戏时间计时，让顿帧与其他对象一起保持咆哮。
            _flourishRemaining = Mathf.Max(0f, _flourishRemaining - Time.deltaTime);
            bool roaring = !attacking && _flourishRemaining > 0f && CanRoarOver(state);

            if (roaring)
            {
                target = CharacterAnimatorParameters.RoarState;
                _animator.SetFloat(AttackTimeParam, 1f - _flourishRemaining / Mathf.Max(0.01f, _flourishLength));
            }

            // 连击中的新一段可以播放与上一段相同的状态，因此攻击变化即使名称不变，也会重新开始状态。
            bool newAttack = attacking && !ReferenceEquals(attack, _currentAttack);

            if (target != BaseState(_currentState) || newAttack)
            {
                bool fromAttack = _currentAttack != null;

                // 重复上一片段的挥击会混合到该状态的副本：状态无法向自身交叉淡化，
                // 而重新开始会让身体从上次挥击末尾跳到下次开头。
                if (attacking && target == BaseState(_currentState))
                {
                    target = _currentState == target ? target + CharacterAnimatorParameters.AlternateSuffix : target;
                }

                float blend = !attacking && target != CharacterAnimatorParameters.RollState
                    ? CrossFadeSeconds
                    : fromAttack || target == CharacterAnimatorParameters.RollState
                        ? ChainCrossFadeSeconds
                        : EnterAttackCrossFadeSeconds;

                _animator.CrossFadeInFixedTime(target, blend);
                _currentState = target;
            }

            _currentAttack = attacking ? attack : null;

            if (attacking)
            {
                _animator.SetFloat(AttackTimeParam, WarpFor(attack).NormalizedTimeAt(attackElapsedSeconds));
            }
        }

        /// <summary>
        /// 播放一段时间咆哮，可覆盖站立与行走，但绝不覆盖攻击或受击反应。
        /// </summary>
        /// <remarks>
        /// 咆哮和攻击一样按进度定位，因此这里由独立倒计时驱动：无论片段多长，都在指定时间内播放一次。
        /// 没有咆哮片段的战斗者保留当前动作。
        /// </remarks>
        /// <param name="seconds">咆哮时长。</param>
        public void Flourish(float seconds)
        {
            if (!HasSkeleton || seconds <= 0f ||
                !_animator.HasState(0, Animator.StringToHash(CharacterAnimatorParameters.RoarState)))
            {
                return;
            }

            _flourishLength = seconds;
            _flourishRemaining = seconds;
        }

        /// <summary>咆哮剩余秒数；也可为零。</summary>
        private float _flourishRemaining;

        /// <summary>咆哮总时长，用于按进度完整播放一次。</summary>
        private float _flourishLength;

        /// <summary>咆哮能否覆盖战斗者原本所处的状态。</summary>
        private static bool CanRoarOver(ObservableActionState state) =>
            state == ObservableActionState.Idle || state == ObservableActionState.Moving;

        /// <summary>去掉副本后缀的状态名称，让一次挥击的两个副本视为同一状态。</summary>
        private static string BaseState(string state) =>
            state != null && state.EndsWith(CharacterAnimatorParameters.AlternateSuffix)
                ? state.Substring(0, state.Length - CharacterAnimatorParameters.AlternateSuffix.Length)
                : state;

        /// <summary>在上半身播放战斗者受击动作，叠加到当前动作上。</summary>
        /// <remarks>
        /// 不覆盖跌落、死亡或硬直：它们是具有自身片段的全身反应，在其上叠加受击动作会扭曲倒地的身体。
        /// </remarks>
        public void Recoil()
        {
            if (!HasSkeleton || _hitLayer < 0 ||
                _currentState == CharacterAnimatorParameters.DeathState ||
                _currentState == CharacterAnimatorParameters.AirborneState ||
                _currentState == CharacterAnimatorParameters.KnockedDownState ||
                _currentState == CharacterAnimatorParameters.StaggerState)
            {
                return;
            }

            // 在反作用峰值以完整权重进入，让接触帧立即呈现完整打击。
            // 按游戏时间渐入会让顿帧在受击动作开始之前冻结身体：冻结和命中先后播放，两者都缺少命中感。
            _animator.Play(CharacterAnimatorParameters.HitState, _hitLayer, HitPeakFraction);
            _animator.SetLayerWeight(_hitLayer, 1f);

            // Animator 在下次更新进入状态后，才能得知片段长度。
            _hitLength = 0f;
            _hitElapsed = HitAttackSeconds;
        }

        /// <summary>上半身受击动作的当前权重，从静止时的零到完整强度的一。</summary>
        public float HitWeight => _hitLayer >= 0 && _animator != null ? _animator.GetLayerWeight(_hitLayer) : 0f;

        /// <summary>
        /// 向方向混合提供战斗者相对于朝向的移动。
        /// </summary>
        /// <remarks>
        /// 读取角色根自身运动而不是传入，因此两个控制器都无需改动：移动器移动的是角色根，
        /// 而此视觉根还带有程序化倾斜和反作用。
        /// </remarks>
        private void DriveDirection(float planarSpeed01)
        {
            Transform root = transform.parent != null ? transform.parent : transform;
            Vector3 displacement = _hasRootPosition ? root.position - _lastRootPosition : Vector3.zero;
            _lastRootPosition = root.position;
            _hasRootPosition = true;

            _blendPosition = LocomotionDirection.BlendPosition(displacement, transform.rotation, planarSpeed01, _blendPosition);

            float delta = Time.deltaTime;
            _animator.SetFloat(MoveXParam, _blendPosition.x, DirectionDampSeconds, delta);
            _animator.SetFloat(MoveZParam, _blendPosition.y, DirectionDampSeconds, delta);
        }

        /// <summary>让骨架返回空闲状态，用于重试。</summary>
        public void ResetState()
        {
            _currentState = null;
            _currentAttack = null;
            _hitElapsed = -1f;
            _flourishRemaining = 0f;

            if (HasSkeleton && _hitLayer >= 0)
            {
                _animator.SetLayerWeight(_hitLayer, 0f);
            }

            if (HasSkeleton)
            {
                _animator.SetFloat(SpeedParam, 0f);
                _animator.SetFloat(MoveXParam, 0f);
                _animator.SetFloat(MoveZParam, 0f);
                _blendPosition = Vector2.zero;
                _hasRootPosition = false;
                _animator.Play(CharacterAnimatorParameters.LocomotionState, 0, 0f);
            }
        }

        private AttackClipTimeWarp WarpFor(AttackDefinition attack)
        {
            float contact = _config != null ? _config.ContactFractionFor(attack, _frenzied) : ClipContact.DefaultContact;

            // 归一化片段空间：将片段长度视为一，因此无需在任何位置保存片段长度。
            return new AttackClipTimeWarp(
                attack.StartupSeconds, attack.ActiveSeconds, attack.RecoverySeconds,
                1f, contact, ContactSpanFraction);
        }

        private string AttackStateFor(AttackDefinition attack) =>
            _config != null ? _config.AttackStateFor(attack) : CharacterAnimatorParameters.DefaultLightState;

        private static bool IsAttackState(ObservableActionState state) =>
            state == ObservableActionState.LightAttacking ||
            state == ObservableActionState.HeavyAttacking ||
            state == ObservableActionState.UsingAbility;

        private static string StateFor(ObservableActionState state)
        {
            switch (state)
            {
                case ObservableActionState.Dashing:
                    return CharacterAnimatorParameters.RollState;

                case ObservableActionState.Guarding:
                    return CharacterAnimatorParameters.GuardState;

                // 向前撞击，让招架呈现其本有的进攻性质，而不是持续防御。
                case ObservableActionState.Parrying:
                    return CharacterAnimatorParameters.ParryState;

                case ObservableActionState.Staggered:
                    return CharacterAnimatorParameters.StaggerState;

                case ObservableActionState.Dead:
                    return CharacterAnimatorParameters.DeathState;

                case ObservableActionState.Airborne:
                    return CharacterAnimatorParameters.AirborneState;

                case ObservableActionState.KnockedDown:
                    return CharacterAnimatorParameters.KnockedDownState;

                default:
                    return CharacterAnimatorParameters.LocomotionState;
            }
        }
    }
}
