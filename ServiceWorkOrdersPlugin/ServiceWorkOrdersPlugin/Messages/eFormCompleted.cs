namespace ServiceWorkOrdersPlugin.Messages
{
    public class eFormCompleted
    {
        public int MicrotingId { get; protected set; }
        public int CheckId { get; protected set; }
        public int CheckUId { get; protected set; }

        public eFormCompleted(int microtingId, int checkId, int checkUId)
        {
            MicrotingId = microtingId;
            CheckId = checkId;
            CheckUId = checkUId;
        }
    }
}
