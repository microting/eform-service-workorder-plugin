using Microting.WorkOrderBase.Infrastructure.Data;
using Microting.WorkOrderBase.Infrastructure.Data.Factories;

namespace ServiceWorkOrdersPlugin.Infrastructure.Helpers
{
    public class DbContextHelper
    {
        private string ConnectionString { get; }

        public DbContextHelper(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public WorkOrderPnDbContext GetDbContext()
        {
            WorkOrderPnContextFactory contextFactory = new WorkOrderPnContextFactory();

            return contextFactory.CreateDbContext(new[] { ConnectionString });
        }
    }
}
