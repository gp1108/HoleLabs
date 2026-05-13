using System.Collections.Generic;
using UnityEngine;

namespace Kamgam.SettingsGenerator
{
    public class WindowModePlatformSpecificConnection : WindowModeConnection
    {
        public override List<string> GetOptionLabels()
        {
            if (_labels.IsNullOrEmpty())
            { 
                _labels = new List<string>();

                _labels.Add("Full Screen");
                _labels.Add("Window");

#if UNITY_STANDALONE_WIN
                _labels.Add("Exclusive (Windows)");
#endif
                
#if UNITY_STANDALONE_OSX
                _labels.Add("Maximized (MacOS)");
#endif
            }

            return _labels;
        }

        protected override List<FullScreenMode> getWindowOptions()
        {
            if (_values.IsNullOrEmpty())
            {
                _values = new List<FullScreenMode>();

                _values.Add(FullScreenMode.FullScreenWindow);
                _values.Add(FullScreenMode.Windowed);
#if UNITY_STANDALONE_WIN
                _values.Add(FullScreenMode.ExclusiveFullScreen);
#endif
                
#if UNITY_STANDALONE_OSX
                _values.Add(FullScreenMode.MaximizedWindow);
#endif
            }

            return _values;
        }
    }
}
