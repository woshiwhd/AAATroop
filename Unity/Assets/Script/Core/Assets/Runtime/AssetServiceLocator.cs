namespace Script.Core.Assets
{
    public static class AssetServiceLocator
    {
        public static IAssetService Current { get; private set; }

        public static void Set(IAssetService service)
        {
            Current = service;
        }
    }
}
