using UnityEngine;
using UnityEngine.Serialization;

namespace Kamgam.SettingsGenerator
{
    /// <summary>
    /// Helper class to make buttons use the correct provider automatically.
    /// </summary>
    public class SettingsButtonActions : MonoBehaviour
    {
        [Tooltip("(Optional) Usually it's fine to leave this empty.\n" +
                 "If set the this settings provider will be used. Otherwise the last used provider (or the configured provider, depending on the flag below) will be used instead.")]
        public SettingsProvider SettingsProvider;

        [Tooltip("If enabled then the configured global provider will be used if the SettingsProvider on this component is NULL, otherwise the last used provider will be used as fallback.")]
        public bool FallBackOnConfiguredProvider = false;

        protected SettingsProvider getProvider()
        {
            if (SettingsProvider != null)
                return SettingsProvider;

            if (FallBackOnConfiguredProvider)
                return SettingsGeneratorSettings.GetProvider();
            else
                return SettingsProvider.LastUsedSettingsProvider;
        }
        
        public void SettingsSave()
        {
            var provider = getProvider();
            if (provider)
                provider.Save();
        }
        
        public void SettingsReset()
        {
            var provider = getProvider();
            if (provider)
                provider.Reset();
        }
        
        public void SettingsResetGroup(string group)
        {
            var provider = getProvider();
            if (provider)
                provider.ResetGroup(group);
        }
        
        public void SettingsResetControls()
        {
            var provider = getProvider();
            if (provider)
                provider.ResetControls();
        }

        public void SettingsApply()
        {
            var provider = getProvider();
            if (provider)
                provider.Apply();
        }
        
        public void SettingsApply(bool changedOnly)
        {
            var provider = getProvider();
            if (provider)
                provider.Apply(changedOnly);
        }

        public void SettingsResetToUnapplied()
        {
            var provider = getProvider();
            if (provider)
                provider.ResetToUnappliedValues();
        }
        
        public void SettingsResetToLastSaved()
        {
            var provider = getProvider();
            if (provider)
                provider.ResetToLastSave();
        }
    }
}
