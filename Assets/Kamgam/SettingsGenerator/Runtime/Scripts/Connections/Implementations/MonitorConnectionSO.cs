using UnityEngine;

namespace Kamgam.SettingsGenerator
{
    [CreateAssetMenu(fileName = "MonitorConnection", menuName = "SettingsGenerator/Connection/MonitorConnection", order = 4)]
    public class MonitorConnectionSO : OptionConnectionSO
    {
        
        /// <summary>
        /// If set to true then it will try to trigger a refresh of all resolvers depending on display settings.
        /// </summary>
        [Tooltip("If set to true then it will try to trigger a refresh of all resolvers depending on display settings.")]
        public bool RefreshResolversAfterCompletion = true;
        
        /// <summary>
        /// If enabled then the game will be set to the closest resolution to the current one after monitor change.<br /><br />
        /// Why is this needed? Default Unity behaviour is to se the game to the fullscreen resolution upon monitor change which may be unexpected.
        /// This is disabled by default due to backwards compatibility.
        /// </summary>
        [Tooltip("If enabled then the game will be set to the closest resolution to the current one after monitor change.\n\n" +
                 "Why is this needed? Default Unity behaviour is to se the game to the fullscreen resolution upon monitor change which may be unexpected.\n\n" +
                 "NOTICE: If the old resolution is greater than the new monitor resolution then the max resolution of the new monitor will be used at avoid windows that are too big (only in windowed mode).")]
        public bool TryToPreserveResolutionOnMonitorChange = true;
        
        protected MonitorConnection _connection;

        public override IConnectionWithOptions<string> GetConnection()
        {
            if(_connection == null)
                Create();

            return _connection;
        }

        public void Create()
        {
            _connection = new MonitorConnection();
            _connection.RefreshResolversAfterCompletion = RefreshResolversAfterCompletion;
            _connection.TryToPreserveResolutionOnMonitorChange = TryToPreserveResolutionOnMonitorChange;
        }

        public override void DestroyConnection()
        {
            if (_connection != null)
                _connection.Destroy();

            _connection = null;
        }
    }
}
