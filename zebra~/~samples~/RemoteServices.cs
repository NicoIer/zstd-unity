using YooAsset;

namespace zebra.samples
{
    public class RemoteServices : IRemoteServices
    {
        public string DefaultHostServer { get; private set; }
        public string FallbackHostServer { get; private set; }

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            DefaultHostServer = defaultHostServer;
            FallbackHostServer = fallbackHostServer;
        }

        public string GetRemoteMainURL(string fileName)
        {
            return $"{DefaultHostServer}/{fileName}";
        }

        public string GetRemoteFallbackURL(string fileName)
        {
            return $"{FallbackHostServer}/{fileName}";
        }
    }
}