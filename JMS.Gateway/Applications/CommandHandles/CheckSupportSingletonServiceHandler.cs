﻿using JMS.Domains;
using System;
using System.Collections.Generic;
using System.Text;
using Way.Lib;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading;
using JMS.Dtos;
using System.Threading.Tasks;

namespace JMS.Applications.CommandHandles
{
    class CheckSupportSingletonServiceHandler : ICommandHandler
    {
        public CheckSupportSingletonServiceHandler(IServiceProvider serviceProvider)
        {
        }
        public CommandType MatchCommandType => CommandType.CheckSupportSingletonService;

        public void Handle(NetClient netclient, GatewayCommand cmd)
        {
            netclient.WriteServiceData(new InvokeResult { Success = true});
        }
    }
}