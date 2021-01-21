/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ImageMagick;
using Messages;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
using Microting.eForm.Helpers;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.WorkOrderBase.Infrastructure.Data;
using Microting.WorkOrderBase.Infrastructure.Data.Entities;
using Rebus.Handlers;
using ServiceWorkOrdersPlugin.Infrastructure.Helpers;
using CheckListValue = Microting.eForm.Infrastructure.Models.CheckListValue;
using Field = Microting.eForm.Infrastructure.Models.Field;
using FieldValue = Microting.eForm.Infrastructure.Models.FieldValue;
using System.Globalization;
using System.Threading;
using ServiceWorkOrdersPlugin.Resources;

namespace ServiceWorkOrdersPlugin.Handlers
{
    public class EFormCompletedHandler : IHandleMessages<eFormCompleted>
    {
        private readonly eFormCore.Core _sdkCore;
        private readonly WorkOrderPnDbContext _dbContext;
        private bool _s3Enabled;
        private bool _swiftEnabled;

        public EFormCompletedHandler(eFormCore.Core sdkCore, DbContextHelper dbContextHelper)
        {
            _dbContext = dbContextHelper.GetDbContext();
            _sdkCore = sdkCore;
        }

        public async Task Handle(eFormCompleted message)
        {
            Console.WriteLine("[INF] EFormCompletedHandler.Handle: called");

            try
            {
                _s3Enabled = _sdkCore.GetSdkSetting(Settings.s3Enabled).Result.ToLower() == "true";
                _swiftEnabled = _sdkCore.GetSdkSetting(Settings.swiftEnabled).Result.ToLower() == "true";
                string downloadPath = await _sdkCore.GetSdkSetting(Settings.fileLocationPdf);

                // Docx and PDF files
                string timeStamp = DateTime.UtcNow.ToString("yyyyMMdd") + "_" + DateTime.UtcNow.ToString("hhmmss");
                string docxFileName = $"{timeStamp}{message.SiteId}_temp.docx";
                string tempPDFFileName = $"{timeStamp}{message.SiteId}_temp.pdf";
                string tempPDFFilePath = Path.Combine(downloadPath, tempPDFFileName);


                string newTaskIdValue = _dbContext.PluginConfigurationValues
                    .SingleOrDefault(x => x.Name == "WorkOrdersBaseSettings:NewTaskId")?.Value;

                bool newTaskIdParseResult = int.TryParse(newTaskIdValue, out int newTaskId);
                if (!newTaskIdParseResult)
                {
                    const string errorMessage = "[ERROR] New task eform id not found in setting";
                    Console.WriteLine(errorMessage);
                    throw new Exception(errorMessage);
                }

                string taskListIdValue = _dbContext.PluginConfigurationValues
                    .SingleOrDefault(x => x.Name == "WorkOrdersBaseSettings:TaskListId")?.Value;

                bool taskListIdParseResult = int.TryParse(taskListIdValue, out int taskListId);
                if (!taskListIdParseResult)
                {
                    const string errorMessage = "[ERROR] Task list eform id not found in setting";
                    Console.WriteLine(errorMessage);
                    throw new Exception(errorMessage);
                }

                string folderIdValue = _dbContext.PluginConfigurationValues
                    .SingleOrDefault(x => x.Name == "WorkOrdersBaseSettings:FolderTasksId")?.Value;

                int? folderId;
                if (string.IsNullOrEmpty(folderIdValue) || folderIdValue == "0")
                {
                    folderId = null;
                }
                else
                {
                    bool folderIdParseResult = int.TryParse(folderIdValue, out int result);
                    if (!folderIdParseResult)
                    {
                        var errorMessage = $"[ERROR] Folder id parse error. folderIdValue: {folderIdValue}";
                        Console.WriteLine(errorMessage);
                        throw new Exception(errorMessage);
                    }

                    folderId = result;
                }
                await using MicrotingDbContext sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();

                if (message.CheckId == newTaskId)
                {
                    WorkOrder workOrder = new WorkOrder();
                    workOrder.MicrotingId = message.MicrotingId;
                    workOrder.CheckMicrotingUid = message.CheckUId;
                    workOrder.CheckId = message.CheckId;

                    Console.WriteLine("[INF] EFormCompletedHandler.Handle: message.CheckId == createNewTaskEFormId");

                    Language language = await sdkDbContext.Languages
                        .SingleAsync(x => x.LanguageCode == "da");
                    ReplyElement replyElement = await _sdkCore.CaseRead(message.MicrotingId, message.CheckUId, language);
                    var doneBy = sdkDbContext.Workers
                        .Single(x => x.Id == replyElement.DoneById).full_name();
                    CheckListValue checkListValue = (CheckListValue)replyElement.ElementList[0];
                    List<Field> fields = checkListValue.DataItemList.Select(di => di as Field).ToList();

                    var picturesOfTasks = new List<string>();
                    if (fields.Any())
                    {
                        // field[0] - picture of the task
                        if (!string.IsNullOrEmpty(fields[3]?.FieldValues[0]?.Value))
                        {
                            workOrder.Description = fields[3].FieldValues[0].Value;
                        }

                        if (!string.IsNullOrEmpty(fields[4]?.FieldValues[0]?.Value))
                        {
                            workOrder.CorrectedAtLatest = DateTime.Parse(fields[4].FieldValues[0].Value);
                        }

                        if(fields[2].FieldValues.Count > 0)
                        {
                            foreach(FieldValue fieldValue in fields[2].FieldValues)
                            {
                                if (fieldValue.UploadedDataObj != null)
                                {
                                    picturesOfTasks.Add($"{fieldValue.UploadedDataObj.Id}_700_{fieldValue.UploadedDataObj.Checksum}{fieldValue.UploadedDataObj.Extension}");
                                }
                            }
                        }
                    }

                    workOrder.CreatedAt = DateTime.UtcNow;
                    workOrder.CreatedByUserId = replyElement.SiteMicrotingUuid;
                    workOrder.WorkflowState = Constants.WorkflowStates.Created;
                    workOrder.AssignedArea = fields[0].FieldValues[0].ValueReadable;
                    workOrder.AssignedWorker = fields[1].FieldValues[0].ValueReadable;
                    await workOrder.Create(_dbContext);

                    foreach (var picturesOfTask in picturesOfTasks)
                    {
                        var pictureOfTask = new PicturesOfTask
                        {
                            FileName = picturesOfTask,
                            WorkOrderId = workOrder.Id,
                        };

                        await pictureOfTask.Create(_dbContext);
                    }

                    var folderResult = await _dbContext.PluginConfigurationValues.SingleAsync(x => x.Name == "WorkOrdersBaseSettings:FolderTasksId");
                    string folderMicrotingUid = sdkDbContext.Folders.Single(x => x.Id == folderId)
                        .MicrotingUid.ToString();
                    var resourceString = "ServiceWorkOrdersPlugin.Resources.Templates.page.html";
                    var assembly = Assembly.GetExecutingAssembly();
                    string html;
                    await using (var resourceStream = assembly.GetManifestResourceStream(resourceString))
                    {
                        using var reader = new StreamReader(resourceStream ?? throw new InvalidOperationException($"{nameof(resourceStream)} is null"));
                        html = await reader.ReadToEndAsync();
                    }

                    // Read docx stream
                    resourceString = "ServiceWorkOrdersPlugin.Resources.Templates.file.docx";
                    var docxFileResourceStream = assembly.GetManifestResourceStream(resourceString);
                    if (docxFileResourceStream == null)
                    {
                        throw new InvalidOperationException($"{nameof(docxFileResourceStream)} is null");
                    }

                    var docxFileStream = new MemoryStream();
                    await docxFileResourceStream.CopyToAsync(docxFileStream);
                    await docxFileResourceStream.DisposeAsync();
                    string basePicturePath = await _sdkCore.GetSdkSetting(Settings.fileLocationPicture);
                    var word = new WordProcessor(docxFileStream);
                    string imagesHtml = "";

                    foreach (var imagesName in picturesOfTasks)
                    {
                        Console.WriteLine($"Trying to insert image into document : {imagesName}");
                        imagesHtml = await InsertImage(imagesName, imagesHtml, 700, 650, basePicturePath);
                    }

                    html = html.Replace("{%Content%}", imagesHtml);

                    word.AddHtml(html);
                    word.Dispose();
                    docxFileStream.Position = 0;

                    // Build docx
                    await using (var docxFile = new FileStream(docxFileName, FileMode.Create, FileAccess.Write))
                    {
                        docxFileStream.WriteTo(docxFile);
                    }

                    // Convert to PDF
                    ReportHelper.ConvertToPdf(docxFileName, downloadPath);
                    File.Delete(docxFileName);

                    // Upload PDF
                    // string pdfFileName = null;
                    string hash = await _sdkCore.PdfUpload(tempPDFFilePath);
                    if (hash != null)
                    {
                        //rename local file
                        FileInfo fileInfo = new FileInfo(tempPDFFilePath);
                        fileInfo.CopyTo(downloadPath + hash + ".pdf", true);
                        fileInfo.Delete();
                        await _sdkCore.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf");

                        // TODO Remove from file storage?


                    }

                    List<AssignedSite> sites = await _dbContext.AssignedSites.Where(x => x.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync();
                    foreach (AssignedSite site in sites)
                    {
                        Site sdkSite = await sdkDbContext.Sites.SingleAsync(x => x.MicrotingUid == site.SiteMicrotingUid);
                        language = await sdkDbContext.Languages.SingleAsync(x => x.Id == sdkSite.LanguageId);
                        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(language.LanguageCode);
                        MainElement mainElement = await _sdkCore.ReadeForm(taskListId, language);
                        mainElement.Repeated = 1;
                        mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                        mainElement.StartDate = DateTime.Now.ToUniversalTime();
                        mainElement.CheckListFolderName = folderMicrotingUid;

                        DateTime startDate = new DateTime(2020, 1, 1);
                        mainElement.DisplayOrder = (workOrder.CorrectedAtLatest - startDate).Days;

                        DataElement dataElement = (DataElement)mainElement.ElementList[0];
                        mainElement.Label = fields[3].FieldValues[0].Value;
                        mainElement.PushMessageTitle = mainElement.Label;
                        mainElement.PushMessageBody = string.IsNullOrEmpty(fields[4].FieldValues[0].Value)
                            ? ""
                            : $"{Translations.DontAtTheLatst}: " + DateTime.Parse(fields[4].FieldValues[0].Value).ToString("dd-MM-yyyy");
                        dataElement.Label = fields[3].FieldValues[0].Value;
                        dataElement.Description.InderValue += (string.IsNullOrEmpty(fields[0].FieldValues[0].ValueReadable) || fields[0].FieldValues[0].ValueReadable == "null")
                            ? "" :
                            $"<strong>{Translations.Area}:</strong> {fields[0].FieldValues[0].ValueReadable}<br>";
                        dataElement.Description.InderValue += (string.IsNullOrEmpty(fields[1].FieldValues[0].ValueReadable) || fields[1].FieldValues[0].ValueReadable == "null")
                            ? ""
                            :$"<strong>{Translations.AssignedTo}:</strong> {fields[1].FieldValues[0].ValueReadable}<br>";
                        dataElement.Description.InderValue += $"<strong>{Translations.TaskCreatedBy}:</strong> {doneBy}<br>";
                        dataElement.Description.InderValue += $"<strong>{Translations.DontAtTheLatst}:</strong>"; // Needs i18n support "Corrected at the latest:"
                        dataElement.Description.InderValue += string.IsNullOrEmpty(fields[4].FieldValues[0].Value)
                            ? ""
                            : DateTime.Parse(fields[4].FieldValues[0].Value).ToString("dd-MM-yyyy");

                        dataElement.DataItemList[0].Description.InderValue = dataElement.Description.InderValue;
                        dataElement.DataItemList[0].Label = dataElement.Label;

                        // Read html and template


                        if (hash != null)
                        {
                            ((ShowPdf)dataElement.DataItemList[1]).Value = hash;
                        }

                        int? caseId = await _sdkCore.CaseCreate(mainElement, "", site.SiteMicrotingUid, folderId);
                        var wotCase = new WorkOrdersTemplateCase()
                        {
                            CheckId = message.CheckId,
                            CheckMicrotingUid = message.CheckUId,
                            WorkOrderId = workOrder.Id,
                            CaseId = (int) caseId,
                            CaseMicrotingUid = message.MicrotingId,
                            SdkSiteMicrotingUid = site.SiteMicrotingUid
                        };
                        await wotCase.Create(_dbContext);
                    }
                }
                else if (message.CheckId == taskListId)
                {
                    Console.WriteLine("[INF] EFormCompletedHandler.Handle: message.CheckId == createTaskListEFormId");

                    WorkOrdersTemplateCase workOrdersTemplate = await _dbContext.WorkOrdersTemplateCases.Where(x =>
                        x.CaseId == message.MicrotingId).FirstOrDefaultAsync();

                    WorkOrder workOrder = await _dbContext.WorkOrders.FindAsync(workOrdersTemplate.WorkOrderId);

                    Language language = await sdkDbContext.Languages
                        .SingleAsync(x => x.LanguageCode == "da");
                    ReplyElement replyElement = await _sdkCore.CaseRead(message.MicrotingId, message.CheckUId, language);
                    CheckListValue checkListValue = (CheckListValue)replyElement.ElementList[0];
                    List<Field> fields = checkListValue.DataItemList.Select(di => di as Field).ToList();

                    List<WorkOrdersTemplateCase> wotListToDelete = await _dbContext.WorkOrdersTemplateCases.Where(x =>
                                x.WorkOrderId == workOrdersTemplate.WorkOrderId &&
                                x.CaseId != message.MicrotingId).ToListAsync();

                    foreach(WorkOrdersTemplateCase wotToDelete in wotListToDelete)
                    {
                        await _sdkCore.CaseDelete(wotToDelete.CaseId);
                        wotToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                        await wotToDelete.Update(_dbContext);
                    }

                    if (fields.Any())
                    {
                        // field[3] - pictures of the done task
                        if (fields[3].FieldValues.Count > 0)
                        {
                            foreach (FieldValue fieldValue in fields[3].FieldValues)
                            {
                                if (fieldValue.UploadedDataObj != null)
                                {
                                    var pictureOfTask = new PicturesOfTaskDone
                                    {
                                        FileName = fieldValue.UploadedDataObj.FileName,
                                        WorkOrderId = workOrder.Id,
                                    };

                                    await pictureOfTask.Create(_dbContext);
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(fields[4]?.FieldValues[0]?.Value))
                        {
                            if (string.IsNullOrEmpty(workOrder.DescriptionOfTaskDone))
                            {
                                workOrder.DescriptionOfTaskDone = fields[4].FieldValues[0].Value;
                            }
                        }

                        // Add pictures, checkbox
                        if (workOrder.DoneAt == null)
                        {
                            workOrder.DoneBySiteId = message.SiteId;
                            workOrder.DoneAt = DateTime.UtcNow;
                        }
                        await workOrder.Update(_dbContext);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] ServiceWorkOrdersPlugin.CaseCompleted: Got the following error: {ex.Message}");
            }
        }

        private async Task<string> InsertImage(string imageName, string itemsHtml, int imageSize, int imageWidth, string basePicturePath)
        {
            var filePath = Path.Combine(basePicturePath, imageName);
            Stream stream;
            if (_swiftEnabled)
            {
                var storageResult = await _sdkCore.GetFileFromSwiftStorage(imageName);
                stream = storageResult.ObjectStreamContent;
            }
            else if (_s3Enabled)
            {
                var storageResult = await _sdkCore.GetFileFromS3Storage(imageName);
                stream = storageResult.ResponseStream;
            }
            else if (!File.Exists(filePath))
            {
                return null;
                // return new OperationDataResult<Stream>(
                //     false,
                //     _localizationService.GetString($"{imagesName} not found"));
            }
            else
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }

            using (var image = new MagickImage(stream))
            {
                var profile = image.GetExifProfile();
                // Write all values to the console
                foreach (var value in profile.Values)
                {
                    Console.WriteLine("{0}({1}): {2}", value.Tag, value.DataType, value.ToString());
                }
                //image.AutoOrient();
                // decimal currentRation = image.Height / (decimal)image.Width;
                // int newWidth = imageSize;
                // int newHeight = (int)Math.Round((currentRation * newWidth));
                //
                // image.Resize(newWidth, newHeight);
                // image.Crop(newWidth, newHeight);
                // if (newWidth > newHeight)
                // {
                     image.Rotate(90);
                // }
                var base64String = image.ToBase64();
                itemsHtml +=
                    $@"<p><img src=""data:image/png;base64,{base64String}"" width=""{imageWidth}px"" alt="""" /></p>";
            }

            await stream.DisposeAsync();

            return itemsHtml;
        }
    }
}
