using System;
using System.Collections.Generic;
using System.Text;

namespace ServiceWorkOrdersPlugin.Messages
{
    public class eFormCompleted
    {
        public int MicrotingId { get; protected set; }
        public int CheckId { get; protected set; }

        public eFormCompleted(int microtingId, int checkId)
        {
            MicrotingId = microtingId;
            CheckId = checkId;
        }
    }
}
