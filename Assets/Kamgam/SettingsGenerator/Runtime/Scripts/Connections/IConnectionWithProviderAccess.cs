namespace Kamgam.SettingsGenerator
{
    /// <summary>
    /// Add it to a connection if that connection that needs to know which provider object it belongs too.<br />
    /// If this is added to a connection then the settings system will notice and call SetProvider() after load
    /// but before InitializeConnection().
    /// </summary>
    public interface IConnectionWithProviderAccess
    {
        public void SetProvider(SettingsProvider provider);
        public SettingsProvider GetProvider();
    }
}
