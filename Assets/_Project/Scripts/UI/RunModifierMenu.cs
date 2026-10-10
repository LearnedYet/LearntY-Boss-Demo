using System;
using AdaptiveBossArena.Core.Constants;
using AdaptiveBossArena.Core.Services;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveBossArena.UI
{
    /// <summary>
    /// 标题界面中为下一轮选择挑战修正项的面板。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 把选择写入<see cref="RunSettings.Current"/>并持久保存，战斗加载后读取该静态设置。
    /// 菜单只记录意图，由遭遇战组装入口实际应用到双方，菜单不需要了解角色。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RunModifierMenu : MonoBehaviour
    {
        [SerializeField]
        private Toggle _fastLearnerToggle;

        [SerializeField]
        private Toggle _noHealingToggle;

        [SerializeField]
        private Toggle _fragileToggle;

        [SerializeField]
        private Toggle _trainingToggle;

        private ISaveService _saveService;
        private RunModifiers _modifiers = new RunModifiers();

        // 从存档同步开关时设置，避免程序回写被当作玩家点击再次保存。
        private bool _loading;

        private void Start()
        {
            _saveService = ServiceRegistry.Current != null &&
                           ServiceRegistry.Current.TryGet(out ISaveService service)
                ? service
                : null;

            _modifiers = Load();
            RunSettings.Current = _modifiers;
            ReflectToToggles();
        }

        /// <summary>切换快速学习修正，绑定对应开关。</summary>
        public void SetFastLearner(bool on) => Change(() => _modifiers.FastLearner = on);

        /// <summary>切换禁止治疗修正，绑定对应开关。</summary>
        public void SetNoHealing(bool on) => Change(() => _modifiers.NoHealing = on);

        /// <summary>切换脆弱修正，绑定对应开关。</summary>
        public void SetFragile(bool on) => Change(() => _modifiers.FragilePlayer = on);

        /// <summary>切换训练模式，绑定对应开关。</summary>
        public void SetTraining(bool on) => Change(() => _modifiers.TrainingMode = on);

        /// <summary>应用修改、发布到本轮并持久保存。</summary>
        private void Change(Action apply)
        {
            if (_loading)
            {
                return;
            }

            apply();
            RunSettings.Current = _modifiers;
            _saveService?.Save(SaveKeys.RunModifiers, _modifiers);
        }

        /// <summary>将已加载修正同步回开关，不再次保存。</summary>
        private void ReflectToToggles()
        {
            _loading = true;

            if (_fastLearnerToggle != null)
            {
                _fastLearnerToggle.isOn = _modifiers.FastLearner;
            }

            if (_noHealingToggle != null)
            {
                _noHealingToggle.isOn = _modifiers.NoHealing;
            }

            if (_fragileToggle != null)
            {
                _fragileToggle.isOn = _modifiers.FragilePlayer;
            }

            if (_trainingToggle != null)
            {
                _trainingToggle.isOn = _modifiers.TrainingMode;
            }

            _loading = false;
        }

        /// <summary>读取已保存修正，默认无修正。</summary>
        private RunModifiers Load()
        {
            if (_saveService != null &&
                _saveService.TryLoad(SaveKeys.RunModifiers, out RunModifiers loaded) &&
                loaded != null)
            {
                return loaded;
            }

            return new RunModifiers();
        }

        /// <summary>指定开关，供场景生成器使用。</summary>
        /// <param name="fastLearner">快速学习开关。</param>
        /// <param name="noHealing">禁止治疗开关。</param>
        /// <param name="fragile">脆弱开关。</param>
        /// <param name="training">训练开关。</param>
        public void Bind(Toggle fastLearner, Toggle noHealing, Toggle fragile, Toggle training)
        {
            _fastLearnerToggle = fastLearner;
            _noHealingToggle = noHealing;
            _fragileToggle = fragile;
            _trainingToggle = training;
        }
    }
}
