using AdaptiveBossArena.Core.ScriptableObjects;
using UnityEngine;

namespace AdaptiveBossArena.Game
{
    /// <summary>
    /// 竞技场尺寸、出生位置和相机构图配置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 编辑器场景构建器读取此资源生成竞技场，因此场景由这些数值产生，而非悄然偏离配置的手动布局。
    /// 修改半径后重新生成，就是完整流程。
    /// </para>
    /// <para>
    /// 竞技场尺寸属于战斗设计，而非场景装饰。它限定玩家后退的最大距离，
    /// 使首领的偏好距离适应有明确的应对对象。
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "ArenaConfig",
        menuName = "Adaptive Boss Arena/Game/Arena Config",
        order = 0)]
    public sealed class ArenaConfig : IdentifiableSO
    {
        [Header("Dimensions")]
        [SerializeField]
        [Range(8f, 40f)]
        [Tooltip("Radius of the fighting surface. Large enough to permit spacing play, small enough " +
                 "that the player can never simply run away.")]
        private float _radius = 16f;

        [SerializeField]
        [Tooltip("When true, two swings that meet head-on refuse each other instead of both " +
                 "landing. Off makes simultaneous attacks a damage race again, which is worth " +
                 "being able to compare in a play test without a recompile.")]
        private bool _clashEnabled = true;

        [SerializeField]
        [Range(1f, 8f)]
        [Tooltip("Height of the boundary wall.")]
        private float _wallHeight = 3f;

        [SerializeField]
        [Range(8, 64)]
        [Tooltip("Number of wall segments approximating the circle. Higher values are smoother and " +
                 "cost more colliders.")]
        private int _wallSegments = 32;

        [Header("Environment")]
        [SerializeField]
        [Tooltip("Optional set-dressing prefab instantiated at the arena centre. Empty today, so the " +
                 "arena is the procedural floor and wall ring; drop a dark-fantasy environment in to " +
                 "dress it, with the same collision kept underneath.")]
        private GameObject _environmentPrefab;

        [SerializeField]
        [Tooltip("When an environment prefab is supplied, hide the procedural floor and wall meshes " +
                 "but keep their colliders. Leave off to show both, on to let the imported set fully " +
                 "replace the placeholder look while the proven collision still bounds the fight.")]
        private bool _hideProceduralMeshesWithEnvironment = true;

        [Header("Spawns")]
        [SerializeField]
        [Tooltip("Player spawn position, relative to the arena centre.")]
        private Vector3 _playerSpawn = new Vector3(0f, 0f, -6f);

        [SerializeField]
        [Tooltip("Boss spawn position, relative to the arena centre.")]
        private Vector3 _bossSpawn = new Vector3(0f, 0f, 6f);

        [Header("Camera")]
        [SerializeField]
        [Range(6f, 40f)]
        [Tooltip("Height of the camera above the arena floor. Together with the distance below this " +
                 "sets how large the characters read; the two should keep the ratio implied by the " +
                 "pitch, or the camera will not be looking at what it is framing.")]
        private float _cameraHeight = 10.5f;

        [SerializeField]
        [Range(0f, 20f)]
        [Tooltip("How far back the camera sits from what it is framing. Zero looks straight down.")]
        private float _cameraDistance = 6f;

        [SerializeField]
        [Range(30f, 90f)]
        [Tooltip("Downward pitch of the camera. Around sixty degrees reads as top-down while still " +
                 "giving characters visible silhouettes.")]
        private float _cameraPitchDegrees = 60f;

        [SerializeField]
        [Range(0.05f, 1f)]
        [Tooltip("Half-life of the camera's follow easing. Shorter is tighter and more responsive.")]
        private float _cameraFollowHalfLife = 0.18f;

        [SerializeField]
        [Range(0f, 0.6f)]
        [Tooltip("How far the camera biases from the player toward the boss, keeping both framed.")]
        private float _cameraBiasTowardBoss = 0.3f;

        /// <summary>在竞技场中心实例化的可选环境预制体，或 null。</summary>
        public GameObject EnvironmentPrefab => _environmentPrefab;

        /// <summary>两次同时挥击是否互相弹开，而非同时命中。</summary>
        public bool ClashEnabled => _clashEnabled;

        /// <summary>存在环境时，是否隐藏程序化地面和墙面网格。</summary>
        public bool HideProceduralMeshesWithEnvironment => _hideProceduralMeshesWithEnvironment;

        /// <summary>战斗地面的半径。</summary>
        public float Radius => _radius;

        /// <summary>边界墙的高度。</summary>
        public float WallHeight => _wallHeight;

        /// <summary>用于近似圆形边界的分段数。</summary>
        public int WallSegments => _wallSegments;

        /// <summary>玩家相对于竞技场中心的出生位置。</summary>
        public Vector3 PlayerSpawn => _playerSpawn;

        /// <summary>首领相对于竞技场中心的出生位置。</summary>
        public Vector3 BossSpawn => _bossSpawn;

        /// <summary>相机距地面的高度。</summary>
        public float CameraHeight => _cameraHeight;

        /// <summary>相机位于构图目标后方的距离。</summary>
        public float CameraDistance => _cameraDistance;

        /// <summary>相机向下俯仰角，单位为度。</summary>
        public float CameraPitchDegrees => _cameraPitchDegrees;

        /// <summary>相机跟随平滑的半衰期。</summary>
        public float CameraFollowHalfLife => _cameraFollowHalfLife;

        /// <summary>相机构图从玩家向首领偏移的程度。</summary>
        public float CameraBiasTowardBoss => _cameraBiasTowardBoss;

        /// <summary>将世界位置限制在竞技场边界内。</summary>
        /// <param name="position">要限制的位置。</param>
        /// <param name="agentRadius">被限制角色的半径，确保它与墙保持间距。</param>
        /// <returns>边界内的位置。</returns>
        public Vector3 ClampToArena(Vector3 position, float agentRadius = 0.5f)
        {
            float usableRadius = Mathf.Max(0f, _radius - agentRadius);
            Vector2 planar = new Vector2(position.x, position.z);

            if (planar.sqrMagnitude <= usableRadius * usableRadius)
            {
                return position;
            }

            planar = planar.normalized * usableRadius;
            return new Vector3(planar.x, position.y, planar.y);
        }
    }
}
