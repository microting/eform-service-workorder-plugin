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

using Microting.WindowsService.BasePn;
using Microting.WorkOrderBase.Infrastructure.Data;
using Microting.WorkOrderBase.Infrastructure.Data.Factories;

namespace ServiceWorkOrdersPlugin
{
    using System;
    using System.ComponentModel.Composition;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Castle.MicroKernel.Registration;
    using Castle.Windsor;
    using Infrastructure.Helpers;
    using Installers;
    using Messages;
    using Microsoft.EntityFrameworkCore;
    using Microting.eForm.Dto;
    using Rebus.Bus;

    [Export(typeof(ISdkEventHandler))]
    public class Core : ISdkEventHandler
    {
        private eFormCore.Core _sdkCore;
        private IWindsorContainer _container;
        private IBus _bus;
        private bool _coreThreadRunning = false;
        private bool _coreStatChanging;
        private bool _coreAvailable;
        private string _serviceLocation;
        private static int _maxParallelism = 1;
        private static int _numberOfWorkers = 1;
        private WorkOrderPnDbContext _dbContext;
        private DbContextHelper _dbContextHelper;

        public void CoreEventException(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void UnitActivated(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void eFormProcessed(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void eFormProcessingError(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void eFormRetrived(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void CaseCompleted(object sender, EventArgs args)
        {
            try
            {
                CaseDto trigger = (CaseDto) sender;

                if (trigger.MicrotingUId != null && trigger.CheckUId != null)
                {
                    int caseId = (int) trigger.MicrotingUId;
                    int checkListId = (int) trigger.CheckListId;
                    int checkUId = (int) trigger.CheckUId;
                    int siteId = (int) trigger.SiteUId;
                    _bus.SendLocal(new eFormCompleted(caseId, checkListId, checkUId, siteId));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERR] ServiceWorkOrdersPlugin.CaseCompleted: Got the following error: {ex.Message}");
            }
        }

        public void CaseDeleted(object sender, EventArgs args)
        {
            // Do nothing
        }

        public void NotificationNotFound(object sender, EventArgs args)
        {
            // Do nothing
        }

        public bool Start(string sdkConnectionString, string serviceLocation)
        {
            Console.WriteLine("ServiceWorkOrdersPlugin start called");
            try
            {
                var dbNameSection = Regex.Match(sdkConnectionString, @"(Database=\w*;)").Groups[0].Value;
                var dbPrefix = Regex.Match(sdkConnectionString, @"Database=(\d*)_").Groups[1].Value;

                var pluginDbName = $"Initial Catalog={dbPrefix}_eform-angular-work-orders-plugin;";
                var connectionString = sdkConnectionString.Replace(dbNameSection, pluginDbName);
                string rabbitmqHost = connectionString.Contains("frontend") ? $"frontend-{dbPrefix}-rabbitmq" :"localhost";


                if (!_coreAvailable && !_coreStatChanging)
                {
                    _serviceLocation = serviceLocation;
                    _coreStatChanging = true;

                    if (string.IsNullOrEmpty(_serviceLocation))
                        throw new ArgumentException("serviceLocation is not allowed to be null or empty");

                    if (string.IsNullOrEmpty(connectionString))
                        throw new ArgumentException("serverConnectionString is not allowed to be null or empty");

                    WorkOrderPnContextFactory contextFactory = new WorkOrderPnContextFactory();

                    _dbContext = contextFactory.CreateDbContext(new[] { connectionString });
                    _dbContext.Database.Migrate();

                    _dbContextHelper = new DbContextHelper(connectionString);

                    _coreAvailable = true;
                    _coreStatChanging = false;

                    StartSdkCoreSqlOnly(sdkConnectionString);

                    string temp = _dbContext.PluginConfigurationValues
                        .SingleOrDefault(x => x.Name == "WorkOrdersBaseSettings:MaxParallelism")?.Value;
                    _maxParallelism = string.IsNullOrEmpty(temp) ? 1 : int.Parse(temp);

                    temp = _dbContext.PluginConfigurationValues
                        .SingleOrDefault(x => x.Name == "WorkOrdersBaseSettings:NumberOfWorkers")?.Value;
                    _numberOfWorkers = string.IsNullOrEmpty(temp) ? 1 : int.Parse(temp);

                    _container = new WindsorContainer();
                    _container.Register(Component.For<IWindsorContainer>().Instance(_container));
                    _container.Register(Component.For<DbContextHelper>().Instance(_dbContextHelper));
                    _container.Register(Component.For<eFormCore.Core>().Instance(_sdkCore));
                    _container.Install(
                        new RebusHandlerInstaller()
                        , new RebusInstaller(connectionString, _maxParallelism, _numberOfWorkers, "admin", "password", rabbitmqHost)
                    );

                    _bus = _container.Resolve<IBus>();
                }
                Console.WriteLine("WorkOrdersPlugin started");
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("Start failed " + ex.Message);
                throw;
            }
        }

        public bool Stop(bool shutdownReallyFast)
        {
            try
            {
                if (_coreAvailable && !_coreStatChanging)
                {
                    _coreStatChanging = true;

                    _coreAvailable = false;

                    int tries = 0;
                    while (_coreThreadRunning)
                    {
                        Thread.Sleep(100);
                        _bus.Dispose();
                        tries++;
                    }
                    _sdkCore.Close();

                    _coreStatChanging = false;
                }
            }
            catch (ThreadAbortException)
            {
                //"Even if you handle it, it will be automatically re-thrown by the CLR at the end of the try/catch/finally."
                Thread.ResetAbort(); //This ends the re-throwning
            }

            return true;
        }

        public bool Restart(int sameExceptionCount, int sameExceptionCountMax, bool shutdownReallyFast)
        {
            return true;
        }

        public void StartSdkCoreSqlOnly(string sdkConnectionString)
        {
            _sdkCore = new eFormCore.Core();

            _sdkCore.StartSqlOnly(sdkConnectionString);
        }
    }
}