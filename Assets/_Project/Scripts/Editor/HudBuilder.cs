using AdaptiveBossArena.Core.Events;
using AdaptiveBossArena.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.Editor
{
    /// <summary>
    /// 从代码构建 HUD 层级。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 界面层级很不适合手工写进场景：数十个嵌套 Transform 的锚点值在差异中难以理解，
    /// 每个 GUID 引用又可能悄然断裂。生成它可让布局可读、可审查、可复现。
    /// </para>
    /// <para>
    /// 文字使用 UiBuilder 随附的动态 Noto Sans SC 字体，中文无需额外导入 TextMeshPro 资源，
    /// 也不依赖玩家设备安装字体。
    /// </para>
    /// </remarks>
    internal static class HudBuilder
    {
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;

        /// <summary>
        /// 结束界面从上到下四个区域的竖直中心。
        /// </summary>
        /// <remarks>
        /// 集中定义，便于一眼检查间距。使用以下方法赋予的尺寸时：
        /// <c>BuildEndScreen</c> 标题占 [330, 430]，摘要占 [80, 310]，档案占 [-255, 45]，
        /// 按钮占 [-328, -272]，相邻区域互不重叠。
        /// </remarks>
        private const float HeadlineCentreY = 380f;

        private const float SummaryCentreY = 195f;

        private const float DossierCentreY = -105f;

        private const float OutcomeButtonY = -300f;

        /// <summary>
        /// 暂停菜单返回的标题场景。
        /// </summary>
        /// <remarks>
        /// 从生成场景路径推导而非再次手写，避免仅一处改名后 Main Menu 按钮指向不存在的场景。
        /// </remarks>
        private static readonly string MainMenuSceneName =
            System.IO.Path.GetFileNameWithoutExtension(EditorMenus.MainMenuScenePath);

        // 暗色、低饱和、铁质边框：原先裸四边形上的高饱和绿、黄、粉色条让游戏像玩具。
        // 各资源条仍保留不同色调以便辨识。
        private static readonly Color PlayerHealthColor = new Color(0.62f, 0.1f, 0.08f);
        private static readonly Color StaminaColor = new Color(0.66f, 0.6f, 0.42f);
        private static readonly Color BossHealthColor = new Color(0.52f, 0.05f, 0.05f);
        private static readonly Color PostureColor = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color FocusColor = new Color(0.85f, 0.5f, 0.16f);
        private static readonly Color TrailColor = new Color(0.95f, 0.78f, 0.55f, 0.4f);

        /// <summary>每个资源条的铁质边框。</summary>
        private static readonly Color FrameColor = new Color(0.16f, 0.13f, 0.11f, 0.95f);

        /// <summary>接近黑色的空槽，使填充损失清晰可见。</summary>
        private static readonly Color WellColor = new Color(0.03f, 0.02f, 0.02f, 0.9f);

        /// <summary>骨白色文字。</summary>
        private static readonly Color LetteringColor = new Color(0.78f, 0.72f, 0.62f);

        /// <summary>资源条上方带字距的首领名称。</summary>
        private const string BossTitle = "空洞巨兽";

        /// <summary>资源条铁边厚度，以参考像素计。</summary>
        private const float FrameThickness = 2f;
        private static readonly Color PanelColor = new Color(0.012f, 0.006f, 0.006f, 0.86f);

        /// <summary>构建完整界面，并返回控制器所需对象。</summary>
        /// <param name="actionsAsset">用于暂停控制的输入动作资产。</param>
        /// <param name="channels">
        /// 在替换场景前解析的通道。通过参数传入而非此处查找，因为新建场景会卸载资产，
        /// 之后即使文件存在且正确，按路径加载也可能返回 null。
        /// </param>
        /// <returns>生成组件的引用。</returns>
        public static HudReferences Build(
            UnityEngine.InputSystem.InputActionAsset actionsAsset,
            HudChannels channels)
        {
            GameObject canvasObject = CreateCanvas();
            Transform root = canvasObject.transform;

            var references = new HudReferences();

            BuildPlayerBars(root, references, channels);
            BuildWeaponDisplay(root, channels);
            BuildBossBar(root, references, channels);
            BuildAdaptationTell(root, references, channels);
            BuildRipostePrompt(root, references);

            // 在暂停菜单之前构建，便于暂停菜单绑定打开设置的按钮。
            references.SettingsMenu = SettingsMenuBuilder.Build(root, actionsAsset);

            BuildPauseMenu(root, references, actionsAsset, channels);
            BuildEndScreen(root, references);
            BuildRoundIntro(root, references);

            // 最后提到顶层，让设置面板绘制在打开它的暂停菜单之上；
            // Unity UI 中后面的同级对象覆盖前面的。
            references.SettingsMenu.RaiseToTop();

            return references;
        }

        /// <summary>
        /// 解析界面需要的所有通道。
        /// </summary>
        /// <remarks>
        /// 必须在替换场景前调用。提前统一解析并注入结果，也优于在构建过程中散布查找。
        /// </remarks>
        /// <returns>解析后的通道。</returns>
        public static HudChannels ResolveChannels() => new HudChannels
        {
            PlayerHealth = GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerHealthChanged"),
            PlayerStamina = GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerStaminaChanged"),
            BossHealth = GeneratedAssets.EventChannel<FloatEventChannel>("OnBossHealthChanged"),
            PlayerPosture = GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerPostureChanged"),
            PlayerFocus = GeneratedAssets.EventChannel<FloatEventChannel>("OnPlayerFocusChanged"),
            BossPosture = GeneratedAssets.EventChannel<FloatEventChannel>("OnBossPostureChanged"),
            BossPhase = GeneratedAssets.EventChannel<IntEventChannel>("OnBossPhaseChanged"),
            AdaptationAdopted = GeneratedAssets.EventChannel<StringEventChannel>("OnAdaptationAdopted"),
            WeaponDrawn = GeneratedAssets.EventChannel<StringEventChannel>("OnWeaponDrawn"),
            WeaponSwingCue = GeneratedAssets.EventChannel<StringEventChannel>("OnWeaponSwingCue"),
            PauseState = GeneratedAssets.EventChannel<BoolEventChannel>("OnPauseStateChanged"),
            PlayerDied = GeneratedAssets.EventChannel<VoidEventChannel>("OnPlayerDied"),
            BossDefeated = GeneratedAssets.EventChannel<VoidEventChannel>("OnBossDefeated"),
            Deflect = GeneratedAssets.EventChannel<VoidEventChannel>("OnDeflect"),
            PerfectDodge = GeneratedAssets.EventChannel<VoidEventChannel>("OnPerfectDodge"),
            Overbalance = GeneratedAssets.EventChannel<VoidEventChannel>("OnBossOverbalanced"),
            BossParried = GeneratedAssets.EventChannel<VoidEventChannel>("OnBossParried")
        };

        /// <summary>创建缩放画布及事件系统。</summary>
        private static GameObject CreateCanvas()
        {
            var canvasObject = new GameObject("--- HUD ---", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            // 按高度匹配，让资源条在超宽屏上保持固定屏幕占比；按宽度匹配会使它们过小。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject(
                    "EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            }

            return canvasObject;
        }

        /// <summary>在左下创建玩家生命与耐力条。</summary>
        private static void BuildPlayerBars(Transform root, HudReferences references, HudChannels channels)
        {
            GameObject group = CreatePanel(root, "PlayerVitals", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(40f, 40f), new Vector2(420f, 92f), Color.clear);

            references.PlayerHealth = CreateBar(
                group.transform, "Health", new Vector2(0f, 56f), new Vector2(420f, 26f),
                PlayerHealthColor,
                channels.PlayerHealth);

            references.PlayerStamina = CreateBar(
                group.transform, "Stamina", new Vector2(0f, 20f), new Vector2(420f, 14f),
                StaminaColor,
                channels.PlayerStamina);

            // 在生命与耐力之间放置细条。玩家弹反和完美闪避会使它填满；
            // 满条提示下一次特殊攻击将获得强化。
            references.PlayerFocus = CreateBar(
                group.transform, "Focus", new Vector2(0f, 38f), new Vector2(420f, 7f),
                FocusColor,
                channels.PlayerFocus);

            // 姿态条最靠近角色脚部，因为玩家承压时要观察它，判断还能否承受下一次格挡。
            references.PlayerPosture = CreateBar(
                group.transform, "Posture", new Vector2(0f, 2f), new Vector2(420f, 10f),
                PostureColor,
                channels.PlayerPosture);
        }

        /// <summary>在顶部中心创建首领资源条和阶段指示点。</summary>
        private static void BuildBossBar(Transform root, HudReferences references, HudChannels channels)
        {
            GameObject group = CreatePanel(root, "BossVitals", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(900f, 78f), Color.clear);

            Text title = CreateText(group.transform, "BossName", BossTitle, 22, TextAnchor.LowerCenter);
            title.rectTransform.anchoredPosition = new Vector2(0f, 44f);
            title.rectTransform.sizeDelta = new Vector2(900f, 30f);
            title.color = LetteringColor;
            title.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);

            references.BossHealth = CreateBar(
                group.transform, "Health", new Vector2(0f, -14f), new Vector2(900f, 22f),
                BossHealthColor,
                channels.BossHealth);

            // 放在生命条正下方，让两者读作同一组资源。姿态清空意味着可以处决，是每次弹反的回报。
            references.BossPosture = CreateBar(
                group.transform, "Posture", new Vector2(0f, -32f), new Vector2(900f, 10f),
                PostureColor,
                channels.BossPosture);

            var pipsRoot = new GameObject("PhasePips", typeof(RectTransform));
            pipsRoot.transform.SetParent(group.transform, false);

            var pips = new Image[BossPhaseIndicator.ExpectedPipCount];
            const float pipWidth = 44f;
            const float pipSpacing = 8f;
            float totalWidth = pips.Length * pipWidth + (pips.Length - 1) * pipSpacing;

            for (int i = 0; i < pips.Length; i++)
            {
                float x = -totalWidth * 0.5f + pipWidth * 0.5f + i * (pipWidth + pipSpacing);

                GameObject pip = CreatePanel(pipsRoot.transform, $"Pip_{i}",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, 14f), new Vector2(pipWidth, 6f), Color.white);

                pips[i] = pip.GetComponent<Image>();
            }

            var indicator = pipsRoot.AddComponent<BossPhaseIndicator>();
            indicator.Bind(channels.BossPhase, pips);
        }

        /// <summary>
        /// 创建首领跪地时提示可惩罚的文字。
        /// </summary>
        /// <remarks>
        /// 以前破防立即自动反击。现在玩家必须走近并按攻击，必须明确提示机会，
        /// 否则战斗最佳时刻会变成秘密。
        /// </remarks>
        private static void BuildRipostePrompt(Transform root, HudReferences references)
        {
            Text prompt = CreateText(root, "RipostePrompt", "处决", 30, TextAnchor.MiddleCenter);
            prompt.rectTransform.anchoredPosition = new Vector2(0f, -210f);
            prompt.rectTransform.sizeDelta = new Vector2(600f, 44f);
            prompt.color = new Color(0.93f, 0.84f, 0.6f);
            prompt.fontStyle = FontStyle.Bold;
            prompt.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            prompt.gameObject.SetActive(false);

            references.RipostePrompt = prompt.gameObject;
        }

        /// <summary>在屏幕上三分之一处创建策略适应提示横幅。</summary>
        private static void BuildAdaptationTell(Transform root, HudReferences references, HudChannels channels)
        {
            GameObject panel = CreatePanel(root, "AdaptationTell",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -150f), new Vector2(1100f, 60f), Color.clear);

            var group = panel.AddComponent<CanvasGroup>();
            Text label = CreateText(panel.transform, "Label", string.Empty, 34, TextAnchor.MiddleCenter);
            label.color = new Color(0.86f, 0.74f, 0.56f);
            label.fontStyle = FontStyle.Normal;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            var display = panel.AddComponent<AdaptationTellDisplay>();
            display.Bind(channels.AdaptationAdopted, label, group);

            references.AdaptationTell = display;
        }

        /// <summary>创建暂停面板。</summary>
        private static void BuildPauseMenu(
            Transform root,
            HudReferences references,
            UnityEngine.InputSystem.InputActionAsset actionsAsset,
            HudChannels channels)
        {
            GameObject panel = CreateFullScreenPanel(root, "PausePanel");

            CreateText(panel.transform, "Title", "已暂停", 64, TextAnchor.MiddleCenter)
                .rectTransform.anchoredPosition = new Vector2(0f, 140f);

            var menu = root.gameObject.AddComponent<PauseMenu>();

            CreateButton(panel.transform, "Resume", "继续游戏", new Vector2(0f, 90f), menu.Resume);
            CreateButton(panel.transform, "Restart", "重新挑战", new Vector2(0f, 20f), menu.Restart);

            if (references.SettingsMenu != null)
            {
                CreateButton(panel.transform, "Settings", "设置", new Vector2(0f, -50f),
                    references.SettingsMenu.Open);
            }

            // 没有此按钮，首次战斗开始后整个会话都无法回到标题场景，也无法访问挑战选项。
            CreateButton(panel.transform, "MainMenu", "返回主菜单", new Vector2(0f, -120f),
                menu.ReturnToMenu);

            // 无条件构建，运行时在退出无意义的平台隐藏；生成器不知道最终构建目标平台。
            Button quit = CreateButton(panel.transform, "Quit", "退出游戏", new Vector2(0f, -190f), menu.Quit);

            menu.Bind(
                actionsAsset,
                panel,
                channels.PauseState,
                references.SettingsMenu,
                quit != null ? quit.gameObject : null,
                MainMenuSceneName);

            panel.SetActive(false);
            references.PauseMenu = menu;
        }

        /// <summary>创建结果面板。</summary>
        private static void BuildEndScreen(Transform root, HudReferences references)
        {
            GameObject panel = CreateFullScreenPanel(root, "EndPanel");
            var group = panel.AddComponent<CanvasGroup>();

            // 明确划分不重叠区域，因为 UiBuilder 文本使用中心pivot：
            // y 处矩形范围为 [y - height/ 2, y + height/2]，顶部对齐块从矩形顶边而非y 开始绘制。
            // 遗漏这点曾让摘要首行正好叠在标题中心线上。下方每个区域都与下一块留有可见间隙。
            Text headline = CreateText(panel.transform, "Headline", string.Empty, 72, TextAnchor.MiddleCenter);
            headline.rectTransform.anchoredPosition = new Vector2(0f, HeadlineCentreY);

            // 高度足以容纳 72pt 字形。默认 80 比文字自身高度更小，原先仅因允许溢出才勉强渲染。
            headline.rectTransform.sizeDelta = new Vector2(1600f, 100f);
            headline.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            // 按结束界面最长摘要，即刷新个人纪录时的七行，预留空间，避免溢入下方档案。
            Text summary = CreateText(panel.transform, "Summary", string.Empty, 28, TextAnchor.UpperCenter);
            summary.rectTransform.anchoredPosition = new Vector2(0f, SummaryCentreY);
            summary.rectTransform.sizeDelta = new Vector2(900f, 230f);
            summary.color = new Color(0.84f, 0.79f, 0.7f);

            // 档案展示首领对玩家的分析。左对齐并留足空间，因为是一组句子，
            // 也是整个“它研究了你”机制的回报。
            Text dossier = CreateText(panel.transform, "Dossier", string.Empty, 24, TextAnchor.UpperLeft);
            dossier.rectTransform.anchoredPosition = new Vector2(0f, DossierCentreY);
            dossier.rectTransform.sizeDelta = new Vector2(760f, 300f);
            dossier.color = LetteringColor;
            dossier.fontStyle = FontStyle.Normal;

            var screen = root.gameObject.AddComponent<EndScreen>();
            CreateButton(
                panel.transform, "Retry", "再次挑战", new Vector2(-170f, OutcomeButtonY), screen.Restart);

            // 战斗结束是调整挑战选项的自然时刻；选项位于标题场景，此前从这里无法到达。
            if (references.PauseMenu != null)
            {
                CreateButton(panel.transform, "EndMainMenu", "返回主菜单", new Vector2(170f, OutcomeButtonY),
                    references.PauseMenu.ReturnToMenu);
            }

            screen.Bind(panel, group, headline, summary, dossier);
            panel.SetActive(false);

            references.EndScreen = screen;
        }

        /// <summary>在玩家资源条上方创建当前武器小标签。</summary>
        private static void BuildWeaponDisplay(Transform root, HudChannels channels)
        {
            Text label = CreateText(root, "WeaponName", "长剑", 24, TextAnchor.LowerLeft);

            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(44f, 140f);
            rect.sizeDelta = new Vector2(320f, 32f);
            label.color = LetteringColor;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);

            var display = root.gameObject.AddComponent<WeaponDisplay>();
            display.Bind(channels.WeaponDrawn, label);
        }

        /// <summary>创建每次尝试开始时的准备和开战横幅。</summary>
        /// <remarks>
        /// 与结束界面不同，它没有深色背景：只是覆盖在冻结战斗上的横幅，而非遮住战斗的面板。不带 Image，
        /// CanvasGroup 不阻挡射线，因此不会吞掉下方按钮点击。
        /// </remarks>
        private static void BuildRoundIntro(Transform root, HudReferences references)
        {
            var panel = new GameObject("RoundIntroPanel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            Stretch(panel.GetComponent<RectTransform>());

            var group = panel.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Text banner = CreateText(panel.transform, "Banner", string.Empty, 96, TextAnchor.MiddleCenter);
            banner.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            banner.fontStyle = FontStyle.Bold;

            var intro = root.gameObject.AddComponent<RoundIntro>();
            intro.Bind(panel, group, banner);
            panel.SetActive(false);

            references.RoundIntro = intro;
        }

        /// <summary>创建主填充后带延迟追赶填充的资源条。</summary>
        private static ResourceBar CreateBar(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            Color color,
            FloatEventChannel channel)
        {
            // 根对象图片是铁边，内部槽才是资源条本体，使按父级比例调整的填充始终留在边框内。
            GameObject barRoot = CreatePanel(parent, name, new Vector2(0f, 0f), new Vector2(0f, 0f),
                anchoredPosition, size, FrameColor);

            var well = new GameObject("Well", typeof(RectTransform), typeof(Image));
            well.transform.SetParent(barRoot.transform, false);
            var wellRect = well.GetComponent<RectTransform>();
            Stretch(wellRect);
            wellRect.offsetMin = new Vector2(FrameThickness, FrameThickness);
            wellRect.offsetMax = new Vector2(-FrameThickness, -FrameThickness);
            well.GetComponent<Image>().color = WellColor;

            Image trail = CreateFillImage(well.transform, "Trail", TrailColor);
            Image fill = CreateFillImage(well.transform, "Fill", color);
            fill.sprite = UiSprites.GaugeShading();

            var bar = barRoot.AddComponent<ResourceBar>();
            bar.Bind(channel, fill, trail);
            bar.SetImmediate(1f);

            return bar;
        }

        /// <summary>创建横向填充并拉伸覆盖父对象的图片。</summary>
        private static Image CreateFillImage(Transform parent, string name, Color color)
        {
            var imageObject = new GameObject(name, typeof(Image));
            imageObject.transform.SetParent(parent, false);

            var rect = imageObject.GetComponent<RectTransform>();
            Stretch(rect);

            // 通过拉伸矩形调整尺寸，而非 Image.fillAmount：主填充着色精灵随之拉伸；尾条没有精灵，
            // fillAmount 对它无效。
            var image = imageObject.GetComponent<Image>();
            image.color = color;
            image.type = Image.Type.Simple;

            return image;
        }

        /// <summary>创建指定位置、可带背景的面板。</summary>
        private static GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 size,
            Color background)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            if (background.a > 0f)
            {
                panel.AddComponent<Image>().color = background;
            }

            return panel;
        }

        /// <summary>创建覆盖全屏的面板。</summary>
        private static GameObject CreateFullScreenPanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = PanelColor;

            return panel;
        }

        /// <summary>使用共用随附中文字体创建文字元素。</summary>
        private static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            TextAnchor alignment) =>
            UiBuilder.CreateText(parent, name, content, fontSize, alignment);

        /// <summary>创建带标签并绑定回调的按钮。</summary>
        /// <returns>创建的按钮，供需要后续引用的调用者使用。</returns>
        private static Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            UnityEngine.Events.UnityAction onClick) =>
            UiBuilder.CreateButton(parent, name, label, anchoredPosition, onClick);

        /// <summary>拉伸矩形以填满父对象。</summary>
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// 界面绑定的事件通道，在替换场景前解析。
    /// </summary>
    /// <remarks>
    /// 此类型源于一个不明显的实际故障：替换打开场景会卸载资产，
    /// 之后 <c>AssetDatabase.LoadAssetAtPath</c> 对磁盘上存在且完全有效的资产也返回 null。
    /// 提前统一解析并带到新场景可绕过问题，也比散布查找更清晰。
    /// </remarks>
    internal sealed class HudChannels
    {
        /// <summary>归一化玩家生命。</summary>
        public FloatEventChannel PlayerHealth { get; set; }

        /// <summary>归一化玩家耐力。</summary>
        public FloatEventChannel PlayerStamina { get; set; }

        /// <summary>归一化玩家专注。</summary>
        public FloatEventChannel PlayerFocus { get; set; }

        /// <summary>归一化首领生命。</summary>
        public FloatEventChannel BossHealth { get; set; }

        /// <summary>归一化玩家姿态。</summary>
        public FloatEventChannel PlayerPosture { get; set; }

        /// <summary>归一化首领姿态。</summary>
        public FloatEventChannel BossPosture { get; set; }

        /// <summary>从零开始的首领阶段索引。</summary>
        public IntEventChannel BossPhase { get; set; }

        /// <summary>新采用应对策略的提示信息。</summary>
        public StringEventChannel AdaptationAdopted { get; set; }

        /// <summary>新拔出武器的名称。</summary>
        public StringEventChannel WeaponDrawn { get; set; }

        /// <summary>携带已装备武器的挥击音效标识，使武器声音不同。</summary>
        public StringEventChannel WeaponSwingCue { get; set; }

        /// <summary>游戏是否暂停。</summary>
        public BoolEventChannel PauseState { get; set; }

        /// <summary>玩家被击败时触发。</summary>
        public VoidEventChannel PlayerDied { get; set; }

        /// <summary>首领被击败时触发。</summary>
        public VoidEventChannel BossDefeated { get; set; }

        /// <summary>成功弹反时触发。</summary>
        public VoidEventChannel Deflect { get; set; }

        /// <summary>完美闪避时触发。</summary>
        public VoidEventChannel PerfectDodge { get; set; }

        /// <summary>首领投入攻击却挥空并失衡时触发。</summary>
        public VoidEventChannel Overbalance { get; set; }

        /// <summary>首领招架挥击时触发。</summary>
        public VoidEventChannel BossParried { get; set; }
    }

    /// <summary>场景构建器需要连接的生成界面组件。</summary>
    internal sealed class HudReferences
    {
        /// <summary>玩家生命条。</summary>
        public ResourceBar PlayerHealth { get; set; }

        /// <summary>玩家耐力条。</summary>
        public ResourceBar PlayerStamina { get; set; }

        /// <summary>玩家专注条。</summary>
        public ResourceBar PlayerFocus { get; set; }

        /// <summary>首领生命条。</summary>
        public ResourceBar BossHealth { get; set; }

        /// <summary>玩家姿态条。</summary>
        public ResourceBar PlayerPosture { get; set; }

        /// <summary>首领姿态条。</summary>
        public ResourceBar BossPosture { get; set; }

        /// <summary>策略适应提示横幅。</summary>
        public AdaptationTellDisplay AdaptationTell { get; set; }

        /// <summary>暂停菜单。</summary>
        public PauseMenu PauseMenu { get; set; }

        /// <summary>结果界面。</summary>
        public EndScreen EndScreen { get; set; }

        /// <summary>准备与开战开场横幅。</summary>
        public RoundIntro RoundIntro { get; set; }

        /// <summary>设置面板及其即时应用菜单。</summary>
        public SettingsMenu SettingsMenu { get; set; }

        /// <summary>首领跪地且可被惩罚时显示的提示。</summary>
        public GameObject RipostePrompt { get; set; }
    }
}
