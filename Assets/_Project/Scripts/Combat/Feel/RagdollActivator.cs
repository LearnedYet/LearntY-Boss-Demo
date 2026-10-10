using UnityEngine;

namespace AdaptiveBossArena.Combat.Feel
{
    /// <summary>
    /// 角色死亡时将骨架身体交给物理系统，重试时取回。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 编辑器在预制体内构建身体，设为运动学并关闭碰撞体，因此角色存活时由 Animator 设置骨骼姿势，
    /// 物理系统忽略它们。死亡时反转：Animator 和两个表现驱动器停止，身体变为动态，
    /// 并将致命打击的方向施加到髋部与胸部，让倒下看起来由命中引起。
    /// </para>
    /// <para>
    /// 仅用于表现。角色根、控制器和受击盒保留原位置；布娃娃所在层只与世界和碎片碰撞，
    /// 因此尸体既不会阻挡活人，也不会受击。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RagdollActivator : MonoBehaviour
    {
        /// <summary>死亡时附加的向上速度，让来自水平地面的打击仍能将身体抬离地面。</summary>
        private const float LiftSpeed = 1.5f;

        /// <summary>抛出速度上限，巨大的击退不能让尸体飞越整个竞技场。</summary>
        private const float MaximumThrowSpeed = 9f;

        [SerializeField]
        [Tooltip("The physical bones, built by the editor. The first two are the hips and the chest.")]
        private Rigidbody[] _bodies = new Rigidbody[0];

        private Animator _animator;
        private CharacterAnimator _procedural;
        private CharacterAnimationBridge _bridge;

        private Vector3[] _previousPositions = new Vector3[0];
        private Quaternion[] _previousRotations = new Quaternion[0];
        private Vector3[] _linear = new Vector3[0];
        private Vector3[] _angular = new Vector3[0];
        private bool _hasPreviousPose;

        /// <summary>身体当前是否交由物理系统控制。</summary>
        public bool IsActive { get; private set; }

        /// <summary>物理骨骼，髋部在前。</summary>
        public Rigidbody[] Bodies => _bodies;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>(true);
            _procedural = GetComponent<CharacterAnimator>();
            _bridge = GetComponent<CharacterAnimationBridge>();
            AllocatePoseHistory();
        }

        /// <summary>
        /// Animator 仍设置姿势时，追踪各骨骼运动。
        /// </summary>
        /// <remarks>
        /// 在动画之后采样，因此测量的是实际绘制的姿势。没有时间推进的帧——顿帧，
        /// 正是致命打击会带来的效果——保留最后一次实际速度，而不是将冻结的身体视为静止。
        /// </remarks>
        private void LateUpdate()
        {
            if (IsActive || _bodies.Length == 0)
            {
                return;
            }

            float seconds = Time.deltaTime;

            for (int i = 0; i < _bodies.Length; i++)
            {
                if (_bodies[i] == null)
                {
                    continue;
                }

                Transform bone = _bodies[i].transform;

                if (_hasPreviousPose && seconds > Mathf.Epsilon)
                {
                    _linear[i] = PoseVelocity.Linear(_previousPositions[i], bone.position, seconds);
                    _angular[i] = PoseVelocity.Angular(_previousRotations[i], bone.rotation, seconds);
                }

                _previousPositions[i] = bone.position;
                _previousRotations[i] = bone.rotation;
            }

            _hasPreviousPose = true;
        }

        private void AllocatePoseHistory()
        {
            _previousPositions = new Vector3[_bodies.Length];
            _previousRotations = new Quaternion[_bodies.Length];
            _linear = new Vector3[_bodies.Length];
            _angular = new Vector3[_bodies.Length];
            _hasPreviousPose = false;
        }

        /// <summary>指定骨骼，由预制体构建器使用。</summary>
        /// <param name="bodies">物理骨骼，髋部和胸部在前。</param>
        public void Bind(Rigidbody[] bodies)
        {
            _bodies = bodies ?? new Rigidbody[0];
            AllocatePoseHistory();
        }

        /// <summary>若角色现在死亡，身体将从动画继承的速度。</summary>
        /// <param name="index"><see cref="Bodies"/> 中的索引。</param>
        /// <returns>继承的线速度。</returns>
        public Vector3 AnimatedVelocity(int index) =>
            index >= 0 && index < _linear.Length ? _linear[index] : Vector3.zero;

        /// <summary>让身体沿致命打击方向被抛出并倒下。</summary>
        /// <param name="throwVelocity">施加给躯干的速度，通常为致命打击的击退。</param>
        public void Activate(Vector3 throwVelocity)
        {
            if (IsActive || _bodies.Length == 0)
            {
                return;
            }

            IsActive = true;

            // 先停止所有写入骨骼 Transform 的对象，否则每帧都会与求解器争夺控制，
            // 导致尸体抖动着站起来。
            if (_procedural != null)
            {
                _procedural.enabled = false;
            }

            if (_bridge != null)
            {
                _bridge.enabled = false;
            }

            if (_animator != null)
            {
                _animator.enabled = false;
            }

            Vector3 thrown = Vector3.ClampMagnitude(throwVelocity, MaximumThrowSpeed) + Vector3.up * LiftSpeed;

            for (int i = 0; i < _bodies.Length; i++)
            {
                Rigidbody body = _bodies[i];

                if (body == null)
                {
                    continue;
                }

                SetCollidersEnabled(body, true);
                body.isKinematic = false;

                // 在动画原有运动之上加上打击：躯干承受完整打击，四肢承受一部分，
                // 让身体围绕命中位置折曲，而不是僵硬地滑走。
                body.linearVelocity = _linear[i] + (i < 2 ? thrown : thrown * 0.5f);
                body.angularVelocity = _angular[i];
            }
        }

        /// <summary>将身体交还 Animator，以站立姿态开始新尝试。</summary>
        public void Restore()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            _hasPreviousPose = false;

            foreach (Rigidbody body in _bodies)
            {
                if (body == null)
                {
                    continue;
                }

                // 仍为动态时清除速度；给运动学身体设置速度会报错。
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                SetCollidersEnabled(body, false);
            }

            if (_animator != null)
            {
                _animator.enabled = true;

                // 播放移动动画首帧前，将每根骨骼恢复到 Animator绑定时的姿势。
                // 否则重试会以尸体最后的姿势开始。
                _animator.Rebind();
                _animator.Update(0f);
            }

            if (_bridge != null)
            {
                _bridge.enabled = true;
            }

            if (_procedural != null)
            {
                _procedural.enabled = true;
            }
        }

        private static void SetCollidersEnabled(Rigidbody body, bool enabled)
        {
            // 只处理骨骼自己的碰撞体：子对象是具有自身身体的其他骨骼。
            if (body.TryGetComponent(out Collider collider))
            {
                collider.enabled = enabled;
            }
        }
    }
}
