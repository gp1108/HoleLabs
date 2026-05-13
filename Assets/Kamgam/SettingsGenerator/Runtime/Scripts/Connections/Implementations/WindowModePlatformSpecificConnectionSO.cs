using UnityEngine;

namespace Kamgam.SettingsGenerator
{
    [CreateAssetMenu(fileName = "WindowModePlatformSpecificConnection", menuName = "SettingsGenerator/Connection/WindowModePlatformSpecificConnection", order = 4)]
    public class WindowModePlatformSpecificConnectionSO : OptionConnectionSO
    {
        protected WindowModePlatformSpecificConnection _connection;

        public override IConnectionWithOptions<string> GetConnection()
        {
            if(_connection == null)
                Create();

            return _connection;
        }

        public void Create()
        {
            _connection = new WindowModePlatformSpecificConnection();
        }

        public override void DestroyConnection()
        {
            if (_connection != null)
                _connection.Destroy();

            _connection = null;
        }
    }
}
