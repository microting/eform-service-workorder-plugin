using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.WorkOrderBase.Infrastructure.Data;
using Microting.WorkOrderBase.Infrastructure.Data.Entities;
using Rebus.Handlers;
using ServiceWorkOrdersPlugin.Infrastructure.Helpers;
using ServiceWorkOrdersPlugin.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceWorkOrdersPlugin.Handlers
{
    public class EFormCompletedHandler : IHandleMessages<eFormCompleted>
    {
        private readonly eFormCore.Core _sdkCore;
        private readonly WorkOrderPnDbContext _dbContext;

        public EFormCompletedHandler(eFormCore.Core sdkCore, DbContextHelper dbContextHelper)
        {
            _dbContext = dbContextHelper.GetDbContext();
            _sdkCore = sdkCore;
        }

        public async Task Handle(eFormCompleted message)
        {
            Console.WriteLine("[INF] EFormCompletedHandler.Handle: called");

            int createNewTaskEFormId = 142108;
            int createTaskListEFormId = 142109;

            var createNewTaskEForm = _sdkCore.TemplateItemRead(createNewTaskEFormId);
            var createTaskListEForm = _sdkCore.TemplateItemRead(createTaskListEFormId);

            if (message.CheckId == createNewTaskEFormId)
            {
                var workOrder = new WorkOrder();

                Console.WriteLine("[INF] EFormCompletedHandler.Handle: message.CheckId == createNewTaskEFormId");
                var replyElement = await _sdkCore.CaseRead(message.MicrotingId, message.CheckId); 
                var checkListValue = (CheckListValue)replyElement.ElementList[0];
                var fields = checkListValue.DataItemList.Select(di => di as Field).ToList();

                if (fields.Any())
                {
                    // field[0] - picture of the task
                    if (!string.IsNullOrEmpty(fields[1]?.FieldValues[0]?.Value))
                    {
                        workOrder.Description = fields[1].FieldValues[0].Value;
                    }

                    if (!string.IsNullOrEmpty(fields[2]?.FieldValues[0]?.Value))
                    {
                        workOrder.CorrectedAtLatest = DateTime.Parse(fields[2].FieldValues[0].Value);
                    }

                    if(fields[0].FieldValues.Count > 0)
                    {
                        foreach(var fieldValue in fields[0].FieldValues)
                        {
                            
                        }
                    }
                }

                workOrder.CreatedAt = DateTime.UtcNow;
                workOrder.CreatedByUserId = replyElement.SiteMicrotingUuid;
                workOrder.WorkflowState = Constants.WorkflowStates.Created;
                await workOrder.Create(_dbContext);

                MainElement mainElement;
                mainElement = await _sdkCore.TemplateRead(createTaskListEFormId);
                mainElement.Label = fields[1].FieldValues[0].Value;
                mainElement.Repeated = 1;
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.StartDate = DateTime.Now.ToUniversalTime();

                var dataElement = (DataElement)mainElement.ElementList[0]; // 0 - desc, 1 - pict, ...
                dataElement.Description.InderValue = "Corrected at the latest: ";
                dataElement.Description.InderValue += string.IsNullOrEmpty(fields[2].FieldValues[0].Value.ToString())
                    ? ""
                    : DateTime.Parse(fields[2].FieldValues[0].Value).ToString("dd-MM-yyyy");

                var dataItem = (Text)dataElement.DataItemList[0];
                dataItem.Value = workOrder.Description;

                var sites = await _dbContext.AssignedSites.ToListAsync();
                var wotList = new List<WorkOrdersTemplateCases>();
                foreach (var site in sites)
                {
                    var caseId = await _sdkCore.CaseCreate(mainElement, "", site.Id, null);
                    wotList.Add(new WorkOrdersTemplateCases()
                    {
                        MicrotingId = message.MicrotingId,
                        WorkOrderId = workOrder.Id,
                        CaseId = (int)caseId
                    });
                }
                await _dbContext.WorkOrdersTemplateCases.AddRangeAsync(wotList);
            }
            else if (message.CheckId == createTaskListEFormId)
            {
                Console.WriteLine("[INF] EFormCompletedHandler.Handle: message.CheckId == createTaskListEFormId");

                var workOrdersTemplate = new WorkOrdersTemplateCases();
                workOrdersTemplate = await _dbContext.WorkOrdersTemplateCases.Where(x =>
                            x.CaseId == message.MicrotingId).FirstOrDefaultAsync();

                var workOrder = new WorkOrder();
                workOrder = await _dbContext.WorkOrders.FindAsync(workOrdersTemplate.WorkOrderId);

                var replyElement = await _sdkCore.CaseRead(message.MicrotingId, message.CheckId);
                var checkListValue = (CheckListValue)replyElement.ElementList[0];
                var fields = checkListValue.DataItemList.Select(di => di as Field).ToList();

                var wotListToDelete = await _dbContext.WorkOrdersTemplateCases.Where(x =>
                            x.WorkOrderId != workOrdersTemplate.WorkOrderId && 
                            x.MicrotingId == message.MicrotingId).ToListAsync();
                foreach(var wotToDelete in wotListToDelete)
                {
                    wotToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                    await wotToDelete.Update(_dbContext);
                }

                if (fields.Any())
                {
                    // field[3] - pictures of the done task
                    if (!string.IsNullOrEmpty(fields[4]?.FieldValues[0]?.Value))
                    {
                        workOrder.DescriptionOfTaskDone = fields[4].FieldValues[0].Value;
                    }

                    // Add pictures, checkbox 
                    workOrder.DoneBySiteId = replyElement.DoneById;
                    workOrder.DoneAt = DateTime.UtcNow;
                    await workOrder.Update(_dbContext);
                }
            }
        }
    }
}
