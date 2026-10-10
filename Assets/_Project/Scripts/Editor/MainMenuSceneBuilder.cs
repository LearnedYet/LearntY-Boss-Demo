using System.IO;
using AdaptiveBossArena.Game;
using AdaptiveBossArena.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 以程序方式构建标题场景，并放在构建顺序首位。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 游戏入口是独立场景，而非竞技场上方的面板，只有玩家选择战斗时才构建战斗。
    /// 与其他场景一样由代码生成，便于审查和复现，而非不透明 YAML 大块数据。
    /// </para>
    /// <para>
    /// 此步骤还修正构建顺序：标题必须先加载，竞技场第二。显式设置完整列表而非追加，
    /// 因为追加无法保证首位。
    /// </para>
    /// </remarks>
    public static class MainMenuSceneBuilder
    {
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;

        private static readonly Color BackgroundColor = new Color(0.012f, 0.01f, 0.01f);
        private static readonly Color TitleColor = new Color(0.84f, 0.77f, 0.66f);
        private static readonly Color SubtitleColor = new Color(0.6f, 0.54f, 0.47f);
        private static readonly Color EmberColor = new Color(0.36f, 0.08f, 0.035f);

        /// <summary>创建标题场景，替换之前生成的场景。</summary>
        [MenuItem(EditorMenus.Setup + "Build Title Scene", priority = EditorMenus.SetupPriorityBuildScene + 1)]
        public static void BuildMainMenuScene()
        {
            // 保存提示是模态对话框，构建机器无法回答。
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildServices();
            BuildCamera();
            BuildInterface();

            AssetAuthoring.EnsureFolderExists(EditorMenus.GeneratedSceneFolder);
            EditorSceneManager.SaveScene(scene, EditorMenus.MainMenuScenePath);

            RegisterSceneOrder();

            Debug.Log($"[Adaptive Boss Arena] Title scene built at '{EditorMenus.MainMenuScenePath}'.");
        }

        /// <summary>
        /// 添加服务宿主，让标题场景可以加载和保存设置。
        /// </summary>
        /// <remarks>
        /// 标题是真正场景而非战斗面板，不继承战斗服务，因此需要自己的 <see cref="GameBootstrapper"/>
        /// 提供设置菜单使用的存档服务。它同时注册的战斗服务在此不被使用，没有额外影响。
        /// </remarks>
        private static void BuildServices()
        {
            var managers = new GameObject("--- Managers ---");
            managers.AddComponent<GameBootstrapper>();
        }

        /// <summary>添加简单摄像机，为 Overlay 画布清理背景。</summary>
        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
        }

        /// <summary>构建画布、标题、开始和退出按钮。</summary>
        private static void BuildInterface()
        {
            GameObject canvasObject = CreateCanvas();
            Transform root = canvasObject.transform;

            // 文字后使用渐熄余烬光晕而非纯暗底。标题是第一眼所见，黑底红色大写字会像占位画面。
            var glow = new GameObject("EmberGlow", typeof(RectTransform), typeof(Image));
            glow.transform.SetParent(root, false);
            var glowRect = glow.GetComponent<RectTransform>();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-200f, -300f);
            glowRect.offsetMax = new Vector2(200f, 100f);
            var glowImage = glow.GetComponent<Image>();
            glowImage.sprite = UiSprites.RadialGlow();
            glowImage.color = EmberColor;
            glowImage.raycastTarget = false;

            Text title = UiBuilder.CreateText(root, "Title", "自适应首领竞技场", 72, TextAnchor.MiddleCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 220f);
            title.rectTransform.sizeDelta = new Vector2(1800f, 100f);
            title.color = TitleColor;
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            var rule = new GameObject("TitleRule", typeof(RectTransform), typeof(Image));
            rule.transform.SetParent(root, false);
            var ruleRect = rule.GetComponent<RectTransform>();
            ruleRect.anchoredPosition = new Vector2(0f, 168f);
            ruleRect.sizeDelta = new Vector2(760f, 2f);
            rule.GetComponent<Image>().color = new Color(0.5f, 0.12f, 0.06f, 0.9f);

            Text subtitle = UiBuilder.CreateText(
                root, "Subtitle", "它会学会应对你的惯用招式。", 30, TextAnchor.MiddleCenter);
            subtitle.rectTransform.anchoredPosition = new Vector2(0f, 130f);
            subtitle.color = SubtitleColor;
            subtitle.fontStyle = FontStyle.Normal;

            // 绑定按钮前组件必须已存在，因为持久监听器会序列化对它的引用。
            var titleScreen = canvasObject.AddComponent<TitleScreen>();

            // 使用与战斗相同的设置面板，偏好设置可直接带入战斗。
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsGenerator.AssetPath);
            SettingsMenu settings = SettingsMenuBuilder.Build(root, actions);

            UiBuilder.CreateButton(root, "Start", "开始战斗", new Vector2(0f, -20f), titleScreen.StartGame);
            UiBuilder.CreateButton(root, "Settings", "设置", new Vector2(0f, -90f), settings.Open);

            // 无条件构建，浏览器运行时隐藏，因为页面无法自行关闭标签，按钮否则会无效地留在入口。
            Button quit = UiBuilder.CreateButton(
                root, "Quit", "退出游戏", new Vector2(0f, -160f), titleScreen.Quit);

            titleScreen.Bind(
                Path.GetFileNameWithoutExtension(EditorMenus.ArenaScenePath),
                quit != null ? quit.gameObject : null);

            BuildModifiers(root, canvasObject);

            // 按钮存在后将面板提到顶层。Unity 后面的同级对象覆盖前面的，
            // 先创建的面板否则会被标题按钮穿透显示。
            settings.RaiseToTop();
        }

        private const float ModifierColumnX = -640f;
        private const float ModifierToggleOffset = 232f;
        private const float ModifierRowSpacing = 52f;
        private static readonly Color ModifierAccent = new Color(0.72f, 0.2f, 0.08f);
        private static readonly Color ModifierHeaderColor = new Color(0.78f, 0.7f, 0.6f);

        /// <summary>
        /// 构建挑战选项列：每条规则一个带文字开关，修改时立即保存。
        /// </summary>
        /// <remarks>
        /// 直接显示在标题页而非藏在按钮后，让玩家不用主动寻找就知道战斗可变难，也可用于练习。
        /// </remarks>
        private static void BuildModifiers(Transform root, GameObject canvasObject)
        {
            float y = 120f;

            Text header = UiBuilder.CreateText(root, "ChallengesHeader", "挑战选项", 24, TextAnchor.MiddleLeft);
            header.rectTransform.anchoredPosition = new Vector2(ModifierColumnX, y);
            header.rectTransform.sizeDelta = new Vector2(360f, 34f);
            header.fontStyle = FontStyle.Bold;
            header.color = ModifierHeaderColor;
            y -= ModifierRowSpacing;

            Toggle fastLearner = ModifierToggle(root, "Fast Learner", "快速学习", ref y);
            Toggle noHealing = ModifierToggle(root, "No Healing", "禁止治疗", ref y);
            Toggle fragile = ModifierToggle(root, "Fragile", "脆弱之躯", ref y);
            Toggle training = ModifierToggle(root, "Training", "训练模式", ref y);

            var menu = canvasObject.AddComponent<RunModifierMenu>();
            menu.Bind(fastLearner, noHealing, fragile, training);

            UnityEventTools.AddPersistentListener(fastLearner.onValueChanged, menu.SetFastLearner);
            UnityEventTools.AddPersistentListener(noHealing.onValueChanged, menu.SetNoHealing);
            UnityEventTools.AddPersistentListener(fragile.onValueChanged, menu.SetFragile);
            UnityEventTools.AddPersistentListener(training.onValueChanged, menu.SetTraining);
        }

        /// <summary>创建左对齐文字及其开关，并推进当前 y。</summary>
        private static Toggle ModifierToggle(Transform parent, string name, string caption, ref float y)
        {
            Text label = UiBuilder.CreateText(parent, $"{name} Label", caption, 24, TextAnchor.MiddleLeft);
            label.rectTransform.anchoredPosition = new Vector2(ModifierColumnX, y);
            label.rectTransform.sizeDelta = new Vector2(300f, 34f);
            label.color = SubtitleColor;

            Toggle toggle = UiBuilder.CreateToggle(
                parent, $"{name} Toggle", new Vector2(ModifierColumnX + ModifierToggleOffset, y), ModifierAccent);

            y -= ModifierRowSpacing;
            return toggle;
        }

        /// <summary>创建缩放画布和读取新 Input System 的事件系统。</summary>
        private static GameObject CreateCanvas()
        {
            var canvasObject = new GameObject(
                "Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // 新 Input System 需要专用 UI 模块，否则按钮完全接收不到点击，看起来与连接失效相同。
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject(
                    "EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            }

            return canvasObject;
        }

        /// <summary>
        /// 设置构建顺序，使标题先加载、竞技场第二。
        /// </summary>
        /// <remarks>
        /// 整体写入而非追加。竞技场构建器会追加自身，但只有这里能保证标题首位，这也是标题场景的意义。
        /// </remarks>
        private static void RegisterSceneOrder()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(EditorMenus.MainMenuScenePath, true),
                new EditorBuildSettingsScene(EditorMenus.ArenaScenePath, true)
            };
        }
    }
}
