#if KAMGAM_TND_UPSCALING && (TND_URP || TND_HDRP || TND_BIRP)
using UnityEngine;

namespace Kamgam.SettingsGenerator
{
    [CreateAssetMenu(fileName = "TheNakedDevUpscalerConnection", menuName = "SettingsGenerator/Connection/TheNakedDevUpscalerConnection", order = 4)]
    public class TheNakedDevUpscalerConnectionSO : OptionConnectionSO
    {
        protected TheNakedDevUpscalerConnection _connection;

        public override IConnectionWithOptions<string> GetConnection()
        {
            if(_connection == null)
                Create();

            return _connection;
        }

        public void Create()
        {
            _connection = new TheNakedDevUpscalerConnection(); 
        }

        public override void DestroyConnection()
        {
            if (_connection != null)
                _connection.Destroy();

            _connection = null;
        }
    }
}
#endif
