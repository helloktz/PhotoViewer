using Avalonia.Controls;
using Avalonia.Media;
using PhotoViewer.Common.Extensions;
using PhotoViewer.Common.Localization;
using PhotoViewer.Common.Types;
using PhotoViewer.UI;
using PhotoViewer.UI.Viewer.Transitions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PhotoViewer.Common.Windows;

public abstract class SettingsPageView : PhControl
{

    protected SettingsViewModel VM { get; private set; } = null!;

    protected SettingsNavId NavId { get; private set; }

    protected LangId? PageLabel { get; private set; }

    private readonly List<Action> _langRefreshers = [];

    private bool _suppressColorStaging;

    protected void Initialize(SettingsViewModel vm, SettingsNavId navId, LangId? pageLabel)
    {
        VM = vm;
        NavId = navId;
        PageLabel = pageLabel;
        Build();
    }

    protected abstract void Build();

    protected override void OnIgLanguageChanged()
    {
        base.OnIgLanguageChanged();

        foreach (var refresh in _langRefreshers) refresh();
    }

    protected void AddLangRefresher(Action refresh)
    {
        _langRefreshers.Add(refresh);
        refresh();
    }

    protected void SetLocalizedText(PhButton btn, LangId key)
        => AddLangRefresher(() => btn.Text = Core.Lang[key]);

    protected static void DisableIfLocked(ConfigId id, params Control?[] targets)
    {
        if (!Config.IsConfigLocked(id)) return;

        foreach (var target in targets)
        {
            if (target is not null) target.IsEnabled = false;
        }
    }

    protected void RegisterSearchKey(Control target, LangId label, ConfigId? id, LangId? section)
    {
        VM.Registry.Register(new SettingItem
        {
            Id = id,
            Label = label,
            PageNavId = NavId,
            Page = PageLabel,
            Section = section,
            Target = target,
        });
    }

    protected void BindToggle(CheckBox chk, ConfigId id, LangId label, LangId? section, bool defaultValue = false)
    {
        chk.IsChecked = VM.GetValue(id, defaultValue);
        chk.IsCheckedChanged += (_, _) => VM.SetValue(id, chk.IsChecked ?? false);

        RegisterSearchKey(chk, label, id, section);
    }

    protected void BindIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section, int defaultValue = 0)
    {
        box.Text = VM.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (int.TryParse(box.Text, out var v)) VM.SetValue(id, v);
        };

        RegisterSearchKey(box, label, id, section);
    }

    protected void BindUIntInput(PhTextBox box, ConfigId id, LangId label, LangId? section, uint defaultValue = 0)
    {
        box.Text = VM.GetValue(id, defaultValue).ToString(CultureInfo.InvariantCulture);
        box.TextChanged += (_, _) =>
        {
            if (uint.TryParse(box.Text, out var v)) VM.SetValue(id, v);
        };

        RegisterSearchKey(box, label, id, section);
    }

    protected void BindDoubleInput(PhTextBox box, ConfigId id, LangId label, LangId? section,
        double defaultValue = 0, double minValue = double.NegativeInfinity)
    {
        var initial = Math.Max(minValue, VM.GetValue(id, defaultValue));
        box.Text = initial.ToString(CultureInfo.InvariantCulture);

        box.TextChanged += (_, _) =>
        {
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                VM.SetValue(id, Math.Max(minValue, v));
        };

        if (double.IsNegativeInfinity(minValue))
        {
            RegisterSearchKey(box, label, id, section);
            return;
        }

        box.LostFocus += (_, _) =>
        {
            var isValid = double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            if (isValid && v >= minValue) return;

            box.Text = minValue.ToString(CultureInfo.InvariantCulture);
        };

        RegisterSearchKey(box, label, id, section);
    }

    protected void BindSlider(Slider slider, ConfigId id, LangId label, LangId? section,
        double defaultValue = 0, PhTextBlock? valueLabel = null, Func<double, string>? format = null)
    {
        format ??= v => v.ToString("0", CultureInfo.InvariantCulture);

        slider.Value = Math.Clamp(VM.GetValue(id, defaultValue), slider.Minimum, slider.Maximum);
        if (valueLabel is not null) valueLabel.LangParams = format(slider.Value);

        slider.ValueChanged += (_, _) =>
        {
            VM.SetValue(id, slider.Value);
            if (valueLabel is not null) valueLabel.LangParams = format(slider.Value);
        };

        RegisterSearchKey(slider, label, id, section);
    }

    protected void BindUIntSlider(Slider slider, ConfigId id, LangId label, LangId? section,
        uint defaultValue = 0, PhTextBlock? valueLabel = null)
    {
        var value = Math.Clamp((uint)VM.GetValue(id, defaultValue), (uint)slider.Minimum, (uint)slider.Maximum);
        slider.Value = value;
        if (valueLabel is not null) valueLabel.LangParams = value.ToString(CultureInfo.InvariantCulture);

        slider.ValueChanged += (_, _) =>
        {
            var v = (uint)Math.Round(slider.Value);
            VM.SetValue(id, v);
            if (valueLabel is not null) valueLabel.LangParams = v.ToString(CultureInfo.InvariantCulture);
        };

        RegisterSearchKey(slider, label, id, section);
    }

    protected void BindEnumDropdown<TEnum>(ComboBox combo, ConfigId id, TEnum defaultValue,
        LangId label, LangId? section) where TEnum : struct, Enum
    {
        var current = VM.GetValue(id, defaultValue);
        var selectedIndex = 0;

        var names = Enum.GetNames<TEnum>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var value = Enum.Parse<TEnum>(name);
            var item = new ComboBoxItem { Tag = value };

            BindComboItemText(item, Lang.GetKey($"{typeof(TEnum).Name}_{name}"), name);
            combo.Items.Add(item);
            if (EqualityComparer<TEnum>.Default.Equals(value, current)) selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: TEnum value }) VM.SetValue(id, value);
        };

        RegisterSearchKey(combo, label, id, section);
    }

    protected void BindComboItemText(ComboBoxItem item, LangId? key, string fallback)
        => AddLangRefresher(() => item.Content = key is { } k ? Core.Lang[k] : fallback);

    protected void BindLink(PhButton btn, LangId label, Action onClick)
    {
        SetLocalizedText(btn, label);
        btn.Click += (_, _) => onClick();

        RegisterSearchKey(btn, label, null, null);
    }

    protected void BindLink(PhButton btn, LangId label, string tooltip, Action onClick)
    {
        BindLink(btn, label, onClick);
        ToolTip.SetTip(btn, tooltip);
    }

    protected void BindColorPicker(PhColorPickerControl picker, ConfigId id, string defaultHex,
        LangId label, LangId? section)
    {
        picker.DefaultColor = BHelper.ColorFromHex(defaultHex);
        picker.SelectedColor = BHelper.ColorFromHex(VM.GetValue(id, defaultHex));

        picker.ColorChanged += (_, _) =>
        {
            if (!_suppressColorStaging) VM.SetValue(id, picker.SelectedColor.ToHex());
        };
        AddLangRefresher(() => picker.Title = Core.Lang[label]);

        RegisterSearchKey(picker, label, id, section);
    }

    protected void ResyncColorPicker(PhColorPickerControl picker, ConfigId id, string defaultHex)
    {
        _suppressColorStaging = true;
        picker.DefaultColor = BHelper.ColorFromHex(defaultHex);
        picker.SelectedColor = BHelper.ColorFromHex(VM.GetValue(id, defaultHex));
        _suppressColorStaging = false;
    }

    protected void BindTransitionEditor(ComboBox combo, PhTextBox durationBox, PhTextBlock errorText,
        PhButton openFolderBtn,
        ConfigId effectId, ConfigId durationId, uint defaultDuration, LangId section)
    {
        TransitionEffects.Reload();

        var current = VM.GetValue(effectId, TransitionEffects.NONE);
        bool IsCurrent(string id) => id.Equals(current, StringComparison.OrdinalIgnoreCase);

        var effectOptions = TransitionEffects.All
            .Select(e => TransitionEffectOption.FromFile(e.Id, e.FilePath!))
            .ToList();

        var isKnown = IsCurrent(TransitionEffects.NONE)
            || IsCurrent(TransitionEffects.RANDOM)
            || effectOptions.Any(o => IsCurrent(o.Id));
        if (!isKnown) effectOptions.Add(TransitionEffectOption.FromId(current));

        var options = new List<TransitionEffectOption> { TransitionEffectOption.FromId(TransitionEffects.NONE) };

        if (TransitionEffects.All.Count >= 2 || IsCurrent(TransitionEffects.RANDOM))
        {
            options.Add(TransitionEffectOption.FromId(TransitionEffects.RANDOM));
        }

        if (effectOptions.Count > 0)
        {
            options.Add(TransitionEffectOption.CreateDivider());
            options.AddRange(effectOptions);
        }

        combo.ItemsSource = options;
        combo.SelectedItem = options.Find(o => IsCurrent(o.Id)) ?? options[0];

        void UpdateEffectState()
        {
            var id = (combo.SelectedItem as TransitionEffectOption)?.Id ?? TransitionEffects.NONE;
            var isNone = id.Equals(TransitionEffects.NONE, StringComparison.OrdinalIgnoreCase);
            var isRandom = id.Equals(TransitionEffects.RANDOM, StringComparison.OrdinalIgnoreCase);

            var effect = isNone || isRandom ? null : TransitionEffects.Find(id);
            var hasError = effect is not null && effect.GetEffect() is null;

            errorText.IsVisible = hasError;
            errorText.Text = hasError
                ? $"{Core.Lang[LangId.Settings_TransitionCompileError]} {Path.GetFileName(effect!.FilePath)}\n{effect.CompileError}"
                : string.Empty;
            durationBox.IsEnabled = !isNone && !Config.IsConfigLocked(durationId);
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is TransitionEffectOption { IsDivider: false } option) VM.SetValue(effectId, option.Id);
            UpdateEffectState();
        };
        AddLangRefresher(() =>
        {
            options.ForEach(o => o.RefreshLang());
            UpdateEffectState();
        });
        RegisterSearchKey(combo, LangId.Settings_TransitionEffect, effectId, section);

        BindUIntInput(durationBox, durationId, LangId.Settings_TransitionDuration, section, defaultDuration);
        BindLink(openFolderBtn, LangId.Settings_OpenTransitionsFolder,
            () => BHelper.OpenFolderPath(BHelper.GetRealPlatformConfigDir(Dir.Transitions)));
    }

}
