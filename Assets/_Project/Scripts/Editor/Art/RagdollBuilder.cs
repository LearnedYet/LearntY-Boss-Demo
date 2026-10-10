using System.Collections.Generic;
using AdaptiveBossArena.Combat.Feel;
using AdaptiveBossArena.Core.Constants;
using UnityEngine;

namespace AdaptiveBossArena.Editor.Art
{
    /// <summary>
    /// 按表格在 Humanoid 骨架上构建布娃娃，不手动配置任何身体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 共十一个刚体：髋、胸、头、两侧上臂和前臂、两侧大腿和小腿。足以让身体在腰、膝、肘处弯折，也足够少，
    /// 使两具尸体轻松控制在 WebGL 物理预算内。按 Humanoid 角色查找骨骼，
    /// 因此同一表格适用于骑士和缩放后的巨兽；尺寸均从骨架测量，不手工输入。
    /// </para>
    /// <para>
    /// 全部以休眠状态构建：刚体设为运动学，碰撞体关闭；交给以下组件：<see cref="RagdollActivator"/>，
    /// 由它在死亡时唤醒。
    /// </para>
    /// </remarks>
    public static class RagdollBuilder
    {
        /// <summary>骨架缩放为一时的身体总质量，单位为千克。</summary>
        private const float BaseMass = 70f;

        /// <summary>
        /// 骨骼列表中髋和胸位于最前，激活器把完整冲击施加到这两处。关节限制以度计，刻意设得较紧：
        /// 松散的布娃娃会折成真实身体做不到的姿势，看起来像布团而非人倒下。
        /// </summary>
        private static readonly Part[] Parts =
        {
            new Part(HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.LastBone, Shape.Box, 0.20f, 0f, 0f, 0f),
            new Part(HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Hips, Shape.Box, 0.22f, -20f, 20f, 15f),
            new Part(HumanBodyBones.Head, HumanBodyBones.LastBone, HumanBodyBones.Chest, Shape.Sphere, 0.08f, -40f, 25f, 30f),
            new Part(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.Chest, Shape.Capsule, 0.04f, -70f, 10f, 50f),
            new Part(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.Chest, Shape.Capsule, 0.04f, -70f, 10f, 50f),
            new Part(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftUpperArm, Shape.Capsule, 0.03f, -90f, 0f, 5f),
            new Part(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightUpperArm, Shape.Capsule, 0.03f, -90f, 0f, 5f),
            new Part(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.Hips, Shape.Capsule, 0.11f, -20f, 70f, 30f),
            new Part(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.Hips, Shape.Capsule, 0.11f, -20f, 70f, 30f),
            new Part(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftUpperLeg, Shape.Capsule, 0.07f, -80f, 0f, 5f),
            new Part(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightUpperLeg, Shape.Capsule, 0.07f, -80f, 0f, 5f)
        };

        private enum Shape
        {
            Box,
            Capsule,
            Sphere
        }

        /// <summary>完整布娃娃包含的物理骨骼数量。</summary>
        public static int BodyCount => Parts.Length;

        /// <summary>在骨架上构建布娃娃，并把激活器挂到视觉根对象。</summary>
        /// <param name="animator">骨架的 Humanoid Animator。</param>
        /// <param name="visualRoot">承载角色表现组件的对象。</param>
        /// <param name="rigScale">骨架的统一缩放，用于缩放质量。</param>
        /// <returns>所有骨骼均成功找到并构建时返回 true。</returns>
        public static bool Build(Animator animator, GameObject visualRoot, float rigScale)
        {
            if (animator == null || !animator.isHuman)
            {
                return false;
            }

            var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
            var ordered = new List<Rigidbody>();
            float mass = BaseMass * rigScale * rigScale * rigScale;

            foreach (Part part in Parts)
            {
                Transform bone = animator.GetBoneTransform(part.Bone);

                // 部分骨架没有独立胸骨，使用上段脊柱替代。
                if (bone == null && part.Bone == HumanBodyBones.Chest)
                {
                    bone = animator.GetBoneTransform(HumanBodyBones.Spine);
                }

                if (bone == null)
                {
                    Debug.LogWarning("[Adaptive Boss Arena] Rig has no " + part.Bone + "; the ragdoll was not built.");
                    return false;
                }

                bone.gameObject.layer = Layers.Ragdoll;

                Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = mass * part.MassFraction;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;

                AddCollider(animator, part, bone).enabled = false;

                if (part.Parent != HumanBodyBones.LastBone && bodies.TryGetValue(part.Parent, out Rigidbody parent))
                {
                    AddJoint(bone.gameObject, parent, part);
                }

                bodies[part.Bone] = body;
                ordered.Add(body);
            }

            var activator = visualRoot.GetComponent<RagdollActivator>();

            if (activator == null)
            {
                activator = visualRoot.AddComponent<RagdollActivator>();
            }

            activator.Bind(ordered.ToArray());

            return true;
        }

        private static Collider AddCollider(Animator animator, Part part, Transform bone)
        {
            Transform towards = part.Towards != HumanBodyBones.LastBone ? animator.GetBoneTransform(part.Towards) : null;

            // 指向下一骨骼的局部向量，用于确定本骨骼的长度和方向。
            Vector3 span = towards != null
                ? bone.InverseTransformPoint(towards.position)
                : Vector3.up * (0.12f / Mathf.Max(0.01f, bone.lossyScale.y));

            float length = span.magnitude;

            switch (part.Shape)
            {
                case Shape.Sphere:
                {
                    SphereCollider sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = length * 0.9f;
                    sphere.center = span * 0.5f;
                    return sphere;
                }

                case Shape.Box:
                {
                    BoxCollider box = bone.gameObject.AddComponent<BoxCollider>();
                    int axis = DominantAxis(span);
                    Vector3 size = Vector3.one * (length * 0.9f);
                    size[axis] = length;
                    box.size = size;
                    box.center = span * 0.5f;
                    return box;
                }

                default:
                {
                    CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.direction = DominantAxis(span);
                    capsule.height = length;
                    capsule.radius = length * 0.2f;
                    capsule.center = span * 0.5f;
                    return capsule;
                }
            }
        }

        private static void AddJoint(GameObject bone, Rigidbody parent, Part part)
        {
            CharacterJoint joint = bone.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.enableProjection = true;
            joint.enablePreprocessing = false;

            joint.lowTwistLimit = new SoftJointLimit { limit = part.LowTwist };
            joint.highTwistLimit = new SoftJointLimit { limit = part.HighTwist };
            joint.swing1Limit = new SoftJointLimit { limit = part.Swing };
            joint.swing2Limit = new SoftJointLimit { limit = part.Swing };
        }

        private static int DominantAxis(Vector3 vector)
        {
            var magnitude = new Vector3(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
            return magnitude.x >= magnitude.y && magnitude.x >= magnitude.z ? 0 : magnitude.y >= magnitude.z ? 1 : 2;
        }

        /// <summary>一根物理骨骼：类型、连接对象，以及允许的弯曲范围。</summary>
        private readonly struct Part
        {
            public Part(HumanBodyBones bone, HumanBodyBones towards, HumanBodyBones parent, Shape shape,
                float massFraction, float lowTwist, float highTwist, float swing)
            {
                Bone = bone;
                Towards = towards;
                Parent = parent;
                Shape = shape;
                MassFraction = massFraction;
                LowTwist = lowTwist;
                HighTwist = highTwist;
                Swing = swing;
            }

            public HumanBodyBones Bone { get; }

            public HumanBodyBones Towards { get; }

            public HumanBodyBones Parent { get; }

            public Shape Shape { get; }

            public float MassFraction { get; }

            public float LowTwist { get; }

            public float HighTwist { get; }

            public float Swing { get; }
        }
    }
}
