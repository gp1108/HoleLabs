using Kamgam.LocalizationForSettings;
using Kamgam.UGUIComponentsForSettings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kamgam.SettingsGenerator
{
    /// <summary>
    /// Searches for a text element on the game object and tries to display the specified binding for the given setting id.<br />
    /// It takes a KeyCombo setting (SettingKeyCombination) or an InputBinding setting (SettingString).
    /// </summary>
    public class SettingsBindingDisplay : SettingResolver
    {
        [Tooltip("If enabled then the configured provider will be used.")]
        public bool PreferConfiguredProvider = true;
        
        [System.NonSerialized]
        protected SettingData.DataType[] _supportedDataTypes;

        public override SettingData.DataType[] GetSupportedDataTypes()
        {
            if (_supportedDataTypes == null)
                _supportedDataTypes = new SettingData.DataType[] { 
                    SettingData.DataType.String,
                    SettingData.DataType.KeyCombination
                };

            return _supportedDataTypes;
        }

        protected SettingsProvider resolveSettingProvider()
        {
            if (SettingsProvider != null)
                return SettingsProvider;

            if (!PreferConfiguredProvider && SettingsProvider.LastUsedSettingsProvider != null)
                return SettingsProvider.LastUsedSettingsProvider;
                
            return SettingsGeneratorSettings.GetProvider();
        }

        public ISetting GetSetting()
        {
            return GetSetting(resolveSettingProvider(), ID);
        }
        
        private bool _searchedForText;
        private TMP_Text _textMeshProText;
        private Text _unityText;
        
        public void SetText(string text)
        {
            if (!_searchedForText)
            {
                _searchedForText = true;
                
                _textMeshProText = GetComponent<TMP_Text>();

                if (_textMeshProText == null)
                    _unityText = GetComponent<Text>();
            }

            if (_textMeshProText != null)
                _textMeshProText.text = text;
            else if (_unityText != null)
                _unityText.text = text;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            BindToSetting();
        }
        
        public override void OnDisable()
        {
            base.OnDisable();
            UnbindFromSetting();
        }

        public void BindToSetting()
        {
            var setting = GetSetting();
            if (setting != null)
                setting.OnSettingChanged += onValueChanged;
        }
        
        public void UnbindFromSetting()
        {
            var setting = GetSetting();
            if (setting != null)
                setting.OnSettingChanged -= onValueChanged;
        }

        private void onValueChanged(ISetting setting)
        {
            Refresh();
        }

        public override void Refresh()
        {
            string text = GetSettingBindingDisplayName(resolveSettingProvider(), ID, LocalizationProvider);
            SetText(text);
        }
        
        // Static API
        
        public static ISetting GetSetting(SettingsProvider provider, string id)
        {
            if (provider == null)
                return null;
            
            if (!provider.Settings.HasActiveID(id))
                return null;

            // Try new input binding setting first.
            ISetting setting = provider.Settings.GetSetting(id);
            
            // Try old input setting second
            if (setting == null)
                setting = provider.Settings.GetSetting(id);
            
            return setting;
        }

        
        /// <summary>
        /// Converts a setting value to a human readable text. Takes either a SettingKeyCombination (old input system) or a SettingString (new input system).
        /// </summary>
        /// <param name="provider">SettingKeyCombination (old input system) or a SettingString (new input system)</param>
        /// <param name="settingId"></param>
        /// <param name="localizationProvider">Optional, if specified it will be used to localized the text.</param>
        /// <returns></returns>
        public static string GetSettingBindingDisplayName(SettingsProvider provider, string settingId, LocalizationProvider localizationProvider = null)
        {
            var setting = GetSetting(provider, settingId);

            // Abort with ""
            if (setting == null)
                return "";

            // Get localization function if possible.
            // Get localized separator if possible (term = CompositeControlSeparator)
            string comboSeparator = " + ";
            System.Func<string, string> localizationFunc = null;
            if (localizationProvider != null)
            {
                localizationFunc = localizationProvider.GetLocalization().Get;

                string localizedSeparator = localizationProvider.GetLocalization().Get("CompositeControlSeparator");
                // Assign as new separator only if localized successfully.
                if (!string.IsNullOrEmpty(localizedSeparator) && localizedSeparator != "CompositeControlSeparator")
                    comboSeparator = localizedSeparator;
            }

            if (setting is SettingString settingString)
            {
                string bindingPath = settingString.GetValue();

                string displayName = InputUtils.BindingPathToDisplayName(bindingPath, localizationFunc, comboSeparator);
                return displayName;
            }
            else if (setting is SettingKeyCombination settingKeyCombo)
            {
                string displayName;

                var keyCombo = settingKeyCombo.GetValue();
                string key = InputUtils.UniversalKeyName(keyCombo.Key);
                string modifier = keyCombo.HasModifier ? InputUtils.UniversalKeyName(keyCombo.ModifierKey) : "";

                if (localizationFunc != null)
                {
                    if (keyCombo.HasModifier)
                    {
                        modifier = localizationFunc.Invoke(modifier);
                        if (string.IsNullOrEmpty(modifier)) // In case localization returns "" or null.
                            modifier = InputUtils.UniversalKeyName(keyCombo.ModifierKey);
                    }

                    key = localizationFunc.Invoke(key);
                    if (string.IsNullOrEmpty(key))
                        key = InputUtils.UniversalKeyName(keyCombo.Key);
                }

                if (keyCombo.HasModifier)
                {
                    displayName = modifier + comboSeparator + key;
                }
                else
                {
                    displayName = key;
                }

                return displayName;
            }

            return "";
        }
    }
}
