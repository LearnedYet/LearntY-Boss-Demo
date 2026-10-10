using System;
using System.Collections.Generic;
using AdaptiveBossArena.Combat;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 绘制战斗中的全部打击部位和受击盒，用于对照视觉检查命中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认关闭；命令行传入 <c>-debugHits</c> 或浏览器地址携带 <c>?debughits</c>才启用。
    /// 玩家曾反馈攻击没有碰到自己却造成伤害；现在命中由实际挥动的部位决定，
    /// 此视图让玩家或逐帧截图看到命中发生时用于判定的胶囊体。
    /// </para>
    /// <para>
    /// 攻击有效时打击部位显示红色，否则为灰色；受击盒为绿色。仅负责表现：读取位置，不接触战斗逻辑。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HitboxDebugView : MonoBehaviour
    {
        private const string CommandLineFlag = "-debugHits";
        private const string QueryFlag = "debughits";

        /// <summary>最后一帧有效攻击后继续高亮的时间，确保单帧攻击也可见。</summary>
        private const float LiveHoldSeconds = 0.12f;

        private static readonly Color LiveColour = new Color(1f, 0.15f, 0.1f, 0.65f);
        private static readonly Color IdleColour = new Color(0.8f, 0.8f, 0.8f, 0.2f);
        private static readonly Color HurtboxColour = new Color(0.2f, 1f, 0.3f, 0.2f);

        [SerializeField]
        [Tooltip("Translucent, unlit material the shapes are drawn with. Assigned by the scene generator.")]
        private Material _material;

        private readonly List<(StrikeVolume Part, Renderer Shape)> _parts = new List<(StrikeVolume, Renderer)>();
        private MaterialPropertyBlock _block;

        /// <summary>是否请求启用此视图。</summary>
        /// <param name="arguments">进程命令行参数。</param>
        /// <param name="url">浏览器页面地址。</param>
        /// <returns>任一处携带启用标记时为 true。</returns>
        public static bool IsRequested(string[] arguments, string url)
        {
            if (arguments != null)
            {
                foreach (string argument in arguments)
                {
                    if (string.Equals(argument, CommandLineFlag, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            int query = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');

            return query >= 0 && url.Substring(query).IndexOf(QueryFlag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>分配材质，供场景生成器使用。</summary>
        /// <param name="material">半透明无光照材质。</param>
        public void SetMaterial(Material material) => _material = material;

        private void Awake()
        {
            if (!IsRequested(Environment.GetCommandLineArgs(), Application.absoluteURL) || _material == null)
            {
                enabled = false;
            }
        }

        private void Start()
        {
            _block = new MaterialPropertyBlock();

            foreach (StrikeVolume part in FindObjectsByType<StrikeVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                _parts.Add((part, MakeShape(PrimitiveType.Capsule, transform, "Strike " + part.Part)));
            }

            foreach (Hurtbox hurtbox in FindObjectsByType<Hurtbox>(FindObjectsSortMode.None))
            {
                ShowHurtbox(hurtbox);
            }
        }

        private void LateUpdate()
        {
            foreach ((StrikeVolume part, Renderer shape) in _parts)
            {
                bool shown = part != null && part.IsAvailable;
                shape.enabled = shown;

                if (!shown)
                {
                    continue;
                }

                part.Segment(out Vector3 start, out Vector3 end);
                Vector3 along = end - start;
                float length = along.magnitude;

                // 胶囊原始体沿自身向上方向高两个单位，宽一个单位。
                shape.transform.SetPositionAndRotation(
                    (start + end) * 0.5f,
                    length > 0.0001f ? Quaternion.FromToRotation(Vector3.up, along / length) : Quaternion.identity);
                shape.transform.localScale = new Vector3(
                    part.Radius * 2f, (length + part.Radius * 2f) * 0.5f, part.Radius * 2f);

                bool live = part.LastLiveTime >= 0f && Time.time - part.LastLiveTime <= LiveHoldSeconds;
                Tint(shape, live ? LiveColour : IdleColour);
            }
        }

        /// <summary>原位绘制受击盒碰撞体，并设为子对象以跟随拥有者。</summary>
        private void ShowHurtbox(Hurtbox hurtbox)
        {
            switch (hurtbox.GetComponent<Collider>())
            {
                case SphereCollider sphere:
                {
                    Renderer shape = MakeShape(PrimitiveType.Sphere, hurtbox.transform, "Hurtbox");
                    shape.transform.localPosition = sphere.center;
                    shape.transform.localScale = Vector3.one * sphere.radius * 2f;
                    Tint(shape, HurtboxColour);
                    break;
                }

                case CapsuleCollider capsule:
                {
                    Renderer shape = MakeShape(PrimitiveType.Capsule, hurtbox.transform, "Hurtbox");
                    shape.transform.localPosition = capsule.center;
                    shape.transform.localRotation = capsule.direction == 0
                        ? Quaternion.Euler(0f, 0f, 90f)
                        : capsule.direction == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                    shape.transform.localScale = new Vector3(
                        capsule.radius * 2f, Mathf.Max(capsule.height, capsule.radius * 2f) * 0.5f, capsule.radius * 2f);
                    Tint(shape, HurtboxColour);
                    break;
                }
            }
        }

        private Renderer MakeShape(PrimitiveType type, Transform parent, string shapeName)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = "[Debug] " + shapeName;

            // 只绘制，不参与交互：原始体自身的碰撞体会加入战斗物理判定。
            Destroy(shape.GetComponent<Collider>());

            shape.transform.SetParent(parent, false);

            var renderer = shape.GetComponent<Renderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return renderer;
        }

        private void Tint(Renderer shape, Color colour)
        {
            _block.SetColor("_BaseColor", colour);
            shape.SetPropertyBlock(_block);
        }
    }
}
