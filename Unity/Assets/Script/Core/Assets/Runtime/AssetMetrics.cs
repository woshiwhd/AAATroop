namespace Script.Core.Assets
{
    public class AssetMetrics
    {
        public int loadRequests;
        public int cacheHits;
        public int loadFailures;
        public int retries;

        public override string ToString()
        {
            return $"requests={loadRequests}, cacheHits={cacheHits}, failures={loadFailures}, retries={retries}";
        }
    }
}
