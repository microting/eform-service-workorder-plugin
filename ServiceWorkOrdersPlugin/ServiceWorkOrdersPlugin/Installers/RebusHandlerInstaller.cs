using Castle.MicroKernel.Registration;
using Castle.MicroKernel.SubSystems.Configuration;
using Castle.Windsor;
using System;
using System.Collections.Generic;
using System.Text;

namespace ServiceWorkOrdersPlugin.Installers
{
    public class RebusHandlerInstaller : IWindsorInstaller
    {
        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            //container.Register(Component.For<IHandleMessages<ScheduledItemExecuted>>().ImplementedBy<ScheduledItemExecutedHandler>().LifestyleTransient());
            //container.Register(Component.For<IHandleMessages<eFormCompleted>>().ImplementedBy<EFormCompletedHandler>().LifestyleTransient());
            //container.Register(Component.For<IHandleMessages<eFormRetrieved>>().ImplementedBy<EFormRetrievedHandler>().LifestyleTransient());
            //container.Register(Component.For<IHandleMessages<ItemCaseCreate>>().ImplementedBy<ItemCaseCreateHandler>().LifestyleTransient());
        }
    }
}
