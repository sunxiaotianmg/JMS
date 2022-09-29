﻿using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;
using JMS.Domains;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Linq;
using JMS.Common;
using System.Diagnostics;
using Natasha.CSharp;
using JMS.Applications;
using JMS.Infrastructures;

namespace JMS
{
    public class GatewayProgram
    {
        static void Main(string[] args)
        {
            if(args.Length > 1&& args[0].EndsWith(".pfx") )
            {
                var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(args[0], args[1]);
                Console.WriteLine(cert.GetCertHashString());
                return;
            }
            //固定当前工作目录
            System.IO.Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            var builder = new ConfigurationBuilder();
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            var configuration = builder.Build();

            var port = configuration.GetValue<int>("Port");
            CommandArgParser cmdArg = new CommandArgParser(args);
            port = cmdArg.TryGetValue<int>("port", port);

            Run(configuration,port,out Gateway gatewayInstance);
        }

        public static void Run(IConfiguration configuration,int port,out Gateway gatewayInstance)
        {
           
            var sharefolder = configuration.GetValue<string>("ShareFolder");
            if (!System.IO.Directory.Exists(sharefolder))
            {
                System.IO.Directory.CreateDirectory(sharefolder);
            }

            var datafolder = configuration.GetValue<string>("DataFolder");
            if (!System.IO.Directory.Exists(datafolder))
            {
                System.IO.Directory.CreateDirectory(datafolder);
            }

            ServiceCollection services = new ServiceCollection();
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.AddConfiguration(configuration.GetSection("Logging"));
                loggingBuilder.AddConsole(); // 将日志输出到控制台
            });
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<ClusterGatewayConnector>();
            services.AddSingleton<TransactionStatusManager>();
            services.AddSingleton<IRequestReception, RequestReception>();
            services.AddSingleton<IRegisterServiceManager, RegisterServiceManager>();
            services.AddSingleton<ICommandHandlerRoute, CommandHandlerRoute>();
            services.AddSingleton<Gateway>();
            services.AddSingleton<LockKeyManager>();
            services.AddTransient<IMicroServiceReception, MicroServiceReception>();
            services.AddSingleton<FileChangeWatcher>();
            services.AddTransient<ListenFileChangeReception>();

            var assembly = Assembly.Load(configuration.GetValue<string>("ServiceProviderAllocator:Assembly"));
            var serviceProviderAllocatorType = assembly.GetType(configuration.GetValue<string>("ServiceProviderAllocator:FullName"));

            services.AddSingleton(typeof(IServiceProviderAllocator), serviceProviderAllocatorType);
            var serviceProvider = services.BuildServiceProvider();
            serviceProvider.GetService<LockKeyManager>();
            serviceProvider.GetService<FileChangeWatcher>();
            serviceProvider.GetService<TransactionStatusManager>();

           

            var gateway = serviceProvider.GetService<Gateway>();

            //SSL
            var certPath = configuration.GetValue<string>("SSL:Cert");
            if (!string.IsNullOrEmpty(certPath))
            {
                gateway.ServerCert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, configuration.GetValue<string>("SSL:Password"));
                gateway.AcceptCertHash = configuration.GetSection("SSL:AcceptCertHash").Get<string[]>();
            }

            gateway.ServiceProvider = serviceProvider;
            gatewayInstance = gateway;
            gateway.Run(port);
        }

    }
}
