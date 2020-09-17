using Castle.MicroKernel.Registration;
using Castle.MicroKernel.SubSystems.Configuration;
using Castle.Windsor;
using System;
using System.Collections.Generic;
using System.Text;
using Rebus.Handlers;
using ServiceWorkOrdersPlugin.Handlers;
using ServiceWorkOrdersPlugin.Messages;

namespace ServiceWorkOrdersPlugin.Installers
{
    public class RebusHandlerInstaller : IWindsorInstaller
    {
        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            container.Register(Component.For<IHandleMessages<eFormCompleted>>().ImplementedBy<EFormCompletedHandler>().LifestyleTransient());
        }
    }
}
