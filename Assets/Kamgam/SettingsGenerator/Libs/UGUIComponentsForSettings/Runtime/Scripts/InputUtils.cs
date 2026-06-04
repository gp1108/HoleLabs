using System.Text.RegularExpressions;

namespace Kamgam.UGUIComponentsForSettings
{
    public static partial class InputUtils
    {
        // Most of the actual implementations are in
        //   InputUtils.Input.cs ......... for the old Input API.
        //   InputUtils.InputSystem.cs ... for the new InputSystem.

        public static string BindingPathToDisplayName(string path, System.Func<string, string> localizeFunc = null, string localizedSeparator = " + " )
        {
            // If it's a composite then translate each part separately.
            if (path.IndexOf(InputBindingForInputSystem.CompositeControlSeparator) >= 0)
            {
                // TODO: Avoid GC here (optimize for only two items as that is the most common case).
                var paths = path.Split(InputBindingForInputSystem.CompositeControlSeparator);
                
                if (string.IsNullOrEmpty(localizedSeparator))
                    localizedSeparator = " + "; // Fallback
                
                string localizedDisplayName = "";
                for (int i = 0; i < paths.Length; i++)
                {
                    if (i == 0)
                        localizedDisplayName = SingleBindingPathToDisplayName(paths[i], localizeFunc);
                    else
                        localizedDisplayName += localizedSeparator + SingleBindingPathToDisplayName(paths[i], localizeFunc);
                }
                return localizedDisplayName;
            }
            else
            {
                return SingleBindingPathToDisplayName(path, localizeFunc);
            }
        }
        
        /// <summary>
        /// Convert paths like "&lt;Keyboard&gt;/s" to "S";
        /// </summary>
        /// <param name="path"></param>
        /// <param name="localizeFunc"></param>
        /// <returns></returns>
        public static string SingleBindingPathToDisplayName(string path, System.Func<string, string> localizeFunc = null)
        {
            if (path == null)
                return "";

            if (localizeFunc != null)
                return localizeFunc(path);
            
            // Will convert paths like "<Keyboard>/s" to "S";
            path = Regex.Replace(path, "<[^>]*>/", "");
            if (path.Length < 6)
                path = path.ToUpper();

            return path;
        }
    }
}
