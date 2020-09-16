namespace ServiceWorkOrdersPlugin.Messages
{
    public class eFormCompleted
    {
        public int MicrotingId { get; protected set; }
        public int CheckId { get; protected set; }
        public int CheckUId { get; protected set; }
        public int SiteId { get; protected set; }

        public eFormCompleted(int microtingId, int checkId, int checkUId, int siteId)
        {
            MicrotingId = microtingId;
            CheckId = checkId;
            CheckUId = checkUId;
            SiteId = siteId;
        }
    }
}
