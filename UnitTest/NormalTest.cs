﻿using JMS;
using JMS.Domains;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Natasha.CSharp;
using Org.BouncyCastle.Crypto.Engines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnitTest.ServiceHosts;

namespace UnitTest
{

    [TestClass]
    public class NormalTest
    {
        public int _gateWayPort = 9800;
        public int _gateWayPortCert = 9805;
        public int _clusterGateWayPort1 = 10001;
        public int _clusterGateWayPort2 = 10002;
        public int _UserInfoServicePort = 9801;
        public int _CrashServicePort = 9802;
        public int _UserInfoServicePort_forcluster = 9803;
        public bool _userInfoServiceReady = false;

        Gateway _clusterGateway1;
        Gateway _clusterGateway2;

        public void StartGateway()
        {
            Task.Run(() =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile("appsettings-gateway.json", optional: true, reloadOnChange: true);
                var configuration = builder.Build();

                JMS.GatewayProgram.Run(configuration, _gateWayPort,out Gateway g);
            });
        }

        public void StartGatewayWithCert()
        {
            Task.Run(() =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile("appsettings-gateway-cert.json", optional: true, reloadOnChange: true);
                var configuration = builder.Build();

                JMS.GatewayProgram.Run(configuration, _gateWayPortCert, out Gateway g);
            });
        }

        public void StartGateway_Cluster1()
        {
            Task.Run(() =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile("appsettings-gateway - cluster1.json", optional: true, reloadOnChange: true);
                var configuration = builder.Build();

                JMS.GatewayProgram.Run(configuration, _clusterGateWayPort1, out _clusterGateway1);
            });
        }

        public void StartGateway_Cluster2()
        {
            Task.Run(() =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile("appsettings-gateway - cluster2.json", optional: true, reloadOnChange: true);
                var configuration = builder.Build();

                JMS.GatewayProgram.Run(configuration, _clusterGateWayPort2, out _clusterGateway2);
            });
        }

        public void WaitGatewayReady(int port)
        {
            //等待网关就绪
            while (true)
            {
                try
                {
                    var client = new NetClient();
                    client.Connect("127.0.0.1", port);
                    client.Dispose();

                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(100);
                }
            }
        }


        public void StartUserInfoServiceHost()
        {
            Task.Run(() =>
            {

                WaitGatewayReady(_gateWayPort);
                ServiceCollection services = new ServiceCollection();

                var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddDebug();
                    loggingBuilder.AddConsole(); // 将日志输出到控制台
                    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
                });

                services.AddScoped<UserInfoDbContext>();
                var msp = new MicroServiceHost(services);
                msp.RetryCommitPath = "./$$JMS_RetryCommitPath" + _UserInfoServicePort;
                msp.ClientCheckCodeFile = "./code1.txt";
                msp.Register<TestUserInfoController>("UserInfoService");
                msp.Register<TestWebSocketController>("TestWebSocketService");
                msp.ServiceProviderBuilded += UserInfo_ServiceProviderBuilded;
                msp.Build(_UserInfoServicePort, gateways)
                    .Run();
            });
        }


        public void StartUserInfoServiceHost_ForClusterGateways()
        {
            Task.Run(() =>
            {

                WaitGatewayReady(_clusterGateWayPort1);
                WaitGatewayReady(_clusterGateWayPort2);

                ServiceCollection services = new ServiceCollection();

                var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _clusterGateWayPort1
                   },
                    new NetAddress{
                        Address = "localhost",
                        Port = _clusterGateWayPort2
                   }
                };

                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddDebug();
                    loggingBuilder.AddConsole(); // 将日志输出到控制台
                    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
                });

                services.AddScoped<UserInfoDbContext>();
                var msp = new MicroServiceHost(services);
                msp.RetryCommitPath = "./$$JMS_RetryCommitPath_Cluster_" + _UserInfoServicePort_forcluster;
                msp.Register<TestUserInfoController>("UserInfoService");
                msp.ServiceProviderBuilded += UserInfo_ServiceProviderBuilded;
                msp.Build(_UserInfoServicePort_forcluster, gateways)
                    .Run();
            });
        }

        public void StartCrashServiceHost(int port)
        {
            Task.Run(() =>
            {

                WaitGatewayReady(_gateWayPort);
                ServiceCollection services = new ServiceCollection();

                var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

                services.AddLogging(loggingBuilder =>
                {
                    loggingBuilder.AddDebug();
                    loggingBuilder.AddConsole(); // 将日志输出到控制台
                    loggingBuilder.SetMinimumLevel(LogLevel.Debug);
                });

                services.AddScoped<UserInfoDbContext>();
                var msp = new MicroServiceHost(services);
                msp.RetryCommitPath = "./$$JMS_RetryCommitPath" + port;
                msp.Register<TestCrashController>("CrashService");
                msp.ServiceProviderBuilded += UserInfo_ServiceProviderBuilded;
                msp.Build(port, gateways)
                    .Run();
            });
        }

        private void UserInfo_ServiceProviderBuilded(object? sender, IServiceProvider e)
        {
            _userInfoServiceReady = true;
            Debug.WriteLine("UserInfoService就绪");
        }

        [TestMethod]
        public void Commit()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            UserInfoDbContext.Reset();
            using (var client = new RemoteClient(gateways))
            {
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }

                client.BeginTransaction();
                serviceClient.Invoke("CheckTranId");
                serviceClient.Invoke("SetUserName", "Jack");
                serviceClient.Invoke("SetAge", 28);
                serviceClient.InvokeAsync("SetFather", "Tom");
                serviceClient.InvokeAsync("SetMather", "Lucy");

                client.CommitTransaction();
            }

            Debug.WriteLine($"结果：{UserInfoDbContext.FinallyUserName}");

            if (UserInfoDbContext.FinallyUserName != "Jack" ||
                UserInfoDbContext.FinallyAge != 28 ||
                UserInfoDbContext.FinallyFather != "Tom" ||
                UserInfoDbContext.FinallyMather != "Lucy")
                throw new Exception("结果不正确");
        }

        [TestMethod]
        public void Rollback()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            using (var client = new RemoteClient(gateways))
            {
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }

                client.BeginTransaction();
                serviceClient.Invoke("SetUserName", "Jack");
                serviceClient.Invoke("SetAge", 28);
                serviceClient.InvokeAsync("SetFather", "Tom");
                serviceClient.InvokeAsync("SetMather", "Lucy");
                client.RollbackTransaction();

            }

            if (UserInfoDbContext.FinallyUserName !=  null||
                UserInfoDbContext.FinallyAge != 0 ||
                UserInfoDbContext.FinallyFather !=null ||
                UserInfoDbContext.FinallyMather != null)
                throw new Exception("结果不正确");
        }

        bool RemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        [TestMethod]
        public void HttpsTest()
        {
            StartGatewayWithCert();

            //等待网关就绪
            WaitGatewayReady(_gateWayPortCert);

            JMS.NetClient client = new JMS.NetClient();
            client.Connect("127.0.0.1", _gateWayPortCert);
            client.AsSSLClient("127.0.0.1", RemoteCertificateValidationCallback);
            var content = @"GET /?GetAllServiceProviders HTTP/1.1
Host: 127.0.0.1
Connection: keep-alive
User-Agent: JmsInvoker
Accept: text/html
Accept-Encoding: deflate, br
Accept-Language: zh-CN,zh;q=0.9
Content-Length: 0

";
            client.Write(Encoding.UTF8.GetBytes(content));

            byte[] data = new byte[40960];
            var len = client.InnerStream.Read(data, 0, data.Length);
            var text = Encoding.UTF8.GetString(data, 0, len);
            client.Dispose();

            JMS.CertClient client2 = new JMS.CertClient(new X509Certificate2("../../../../pfx/client.pfx" , "123456"));
            client2.Connect("127.0.0.1", _gateWayPortCert);

            client2.Write(Encoding.UTF8.GetBytes(content));

           data = new byte[40960];
            len = client2.InnerStream.Read(data, 0, data.Length);
            text = Encoding.UTF8.GetString(data, 0, len);
            client2.Dispose();
        }

        [TestMethod]
        public void RollbackForError()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            try
            {
                using (var client = new RemoteClient(gateways))
                {
                    var serviceClient = client.TryGetMicroService("UserInfoService");
                    while (serviceClient == null)
                    {
                        Thread.Sleep(10);
                        serviceClient = client.TryGetMicroService("UserInfoService");
                    }

                    client.BeginTransaction();
                    serviceClient.Invoke("SetUserName", "Jack");
                    serviceClient.Invoke("SetAge", 28);

                    serviceClient.InvokeAsync("SetFather", "Tom");
                    serviceClient.InvokeAsync("SetMather", "Lucy");
                    serviceClient.InvokeAsync("BeError");//这个方法调用会有异常
                    client.CommitTransaction();

                }
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException.Message;
                if (msg != "有意触发错误")
                    throw ex;
            }

            if (UserInfoDbContext.FinallyUserName != null ||
                UserInfoDbContext.FinallyAge != 0 ||
                UserInfoDbContext.FinallyFather != null ||
                UserInfoDbContext.FinallyMather != null)
                throw new Exception("结果不正确");
        }

        /// <summary>
        /// 没有事务
        /// </summary>
        /// <exception cref="Exception"></exception>
        [TestMethod]
        public void NoTransaction()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            using (var client = new RemoteClient(gateways))
            {
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }

                serviceClient.Invoke("SetUserName", "Jack");
                serviceClient.Invoke("SetAge", 28);
                serviceClient.InvokeAsync("SetFather", "Tom");
                serviceClient.InvokeAsync("SetMather", "Lucy");
            }

            Debug.WriteLine($"结果：{UserInfoDbContext.FinallyUserName}");

            if (UserInfoDbContext.FinallyUserName != "Jack" ||
                UserInfoDbContext.FinallyAge != 28 ||
                UserInfoDbContext.FinallyFather != "Tom" ||
                UserInfoDbContext.FinallyMather != "Lucy")
                throw new Exception("结果不正确");
        }

        /// <summary>
        /// 测试提交时，有个服务宕机
        /// </summary>
        /// <exception cref="Exception"></exception>
        [TestMethod]
        public void TestCrash()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();
            StartCrashServiceHost(_CrashServicePort);

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            using (var client = new RemoteClient(gateways))
            {
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }

                var crashService = client.TryGetMicroService("CrashService");
                while (crashService == null)
                {
                    Thread.Sleep(10);
                    crashService = client.TryGetMicroService("CrashService");
                }

                client.BeginTransaction();

                serviceClient.Invoke("SetUserName", "Jack");
                crashService.Invoke("SetText", "abc");
                try
                {
                    client.CommitTransaction();
                }
                catch (Exception ex)
                {

                    Debug.WriteLine(ex.Message);
                }
               
            }

            Thread.Sleep(7000);//等待7秒，失败的事务

            if (UserInfoDbContext.FinallyUserName != "Jack" || TestCrashController.FinallyText != "abc")
                throw new Exception("结果不正确");
        }

        [TestMethod]
        public void TestCrashForLocal()
        {
          
            TestCrashController.CanCrash = true;
            try
            {
                Directory.Delete("./$$_JMS.Invoker.Transactions", true);
            }
            catch (Exception)
            {

            }
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();
            StartCrashServiceHost(_CrashServicePort);

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };

            ServiceCollection services = new ServiceCollection();

           services.AddLogging(loggingBuilder =>
           {
               loggingBuilder.AddDebug();
               loggingBuilder.AddConsole(); // 将日志输出到控制台
               loggingBuilder.SetMinimumLevel(LogLevel.Debug);
            });
            var serviceProvider = services.BuildServiceProvider();

            string tranid;
            using (var client = new RemoteClient(gateways,null, serviceProvider.GetService<ILogger<RemoteClient>>()))
            {
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }

                var crashService = client.GetMicroService("CrashService",new JMS.Dtos.RegisterServiceLocation { 
                    ServiceAddress = "127.0.0.1",
                    Port = _CrashServicePort
                } );               

                client.BeginTransaction();
                tranid = client.TransactionId;

                serviceClient.Invoke("SetUserName", "Jack");
                crashService.Invoke("SetText", "abc");
                try
                {
                    client.CommitTransaction();
                }
                catch (Exception ex)
                {

                    Debug.WriteLine(ex.Message);
                }

            }
            DateTime starttime = DateTime.Now;
            while(File.Exists($"./$$_JMS.Invoker.Transactions/{tranid}.json"))
            {
                if ((DateTime.Now - starttime).TotalSeconds > 80)
                    throw new Exception("超时");
                Thread.Sleep(1000);
            }

            if (UserInfoDbContext.FinallyUserName != "Jack" || TestCrashController.FinallyText != "abc")
                throw new Exception("结果不正确");

            ThreadPool.GetAvailableThreads(out int w, out int c);
        }

        /// <summary>
        /// 测试网关集群
        /// </summary>
        [TestMethod]
        public void TestGatewayCluster()
        {
            UserInfoDbContext.Reset();
            StartGateway_Cluster1();
            StartGateway_Cluster2();

            //等待网关就绪
            WaitGatewayReady(_clusterGateWayPort1);
            WaitGatewayReady(_clusterGateWayPort2);

            StartUserInfoServiceHost_ForClusterGateways();

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _clusterGateWayPort1
                   },new NetAddress{
                        Address = "localhost",
                        Port = _clusterGateWayPort2
                   }
                };

            var serviceProvider1 =(IServiceProvider) _clusterGateway1.GetType().GetProperty("ServiceProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(_clusterGateway1);
            var clusterGatewayConnector1 = serviceProvider1.GetService<ClusterGatewayConnector>();

            var serviceProvider2 = (IServiceProvider)_clusterGateway2.GetType().GetProperty("ServiceProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(_clusterGateway2);
            var clusterGatewayConnector2 = serviceProvider2.GetService<ClusterGatewayConnector>();

            Debug.WriteLine("等待决出主网关");
            while (clusterGatewayConnector1.IsMaster == false && clusterGatewayConnector2.IsMaster == false)
            {
                Thread.Sleep(100);
            }

            var masterGateway = clusterGatewayConnector1.IsMaster ? _clusterGateway1 : _clusterGateway2;
            var lockManager = (clusterGatewayConnector1.IsMaster ? serviceProvider1 : serviceProvider2).GetService<LockKeyManager>();

            using (var client = new RemoteClient(gateways))
            {
                Debug.WriteLine("查找服务");
                var serviceClient = client.TryGetMicroService("UserInfoService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("UserInfoService");
                }
                Debug.WriteLine("查找服务完毕");

                serviceClient.Invoke("LockName",  "abc" , "d","e","f" );

                if(lockManager.GetAllKeys().Any(k=>k.Key == "abc") == false)
                {
                    throw new Exception("找不到lock key");
                }
                serviceClient.Invoke("UnlockName", "d");
            }

            var slaveGateway = clusterGatewayConnector1.IsMaster ? _clusterGateway2 : _clusterGateway1;
            var slaveLockManager = (clusterGatewayConnector1.IsMaster ? serviceProvider2 : serviceProvider1).GetService<LockKeyManager>();
            while(slaveLockManager.GetAllKeys().Any(k => k.Key == "f") == false)
            {
                Thread.Sleep(1000);
            }

            //关闭主网关
            masterGateway.Dispose();

            var serviceProvider = (IServiceProvider)slaveGateway.GetType().GetProperty("ServiceProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(slaveGateway);
            var clusterGatewayConnector = serviceProvider.GetService<ClusterGatewayConnector>();

            //等待从网关成为主网关
            while(clusterGatewayConnector.IsMaster == false)
            {
                Thread.Sleep(100);
            }

            while(slaveLockManager.GetAllKeys().Any(m=>m.RemoveTime != null))
            {
                Thread.Sleep(100);
            }
            if (slaveLockManager.GetAllKeys().Length != 3)
            {
                throw new Exception("lock key数量不对");
            }
        }

        [TestMethod]
        public void TestWebsocket()
        {
            UserInfoDbContext.Reset();
            StartGateway();
            StartUserInfoServiceHost();

            //等待网关就绪
            WaitGatewayReady(_gateWayPort);

            var gateways = new NetAddress[] {
                   new NetAddress{
                        Address = "localhost",
                        Port = _gateWayPort
                   }
                };
            using (var client = new RemoteClient(gateways))
            {
                var serviceClient = client.TryGetMicroService("TestWebSocketService");
                while (serviceClient == null)
                {
                    Thread.Sleep(10);
                    serviceClient = client.TryGetMicroService("TestWebSocketService");
                }
            }
                var clientWebsocket = new ClientWebSocket();
            clientWebsocket.ConnectAsync(new Uri($"ws://127.0.0.1:{_gateWayPort}/TestWebSocketService?name=test"), CancellationToken.None).ConfigureAwait(true).GetAwaiter().GetResult();
            var text = clientWebsocket.ReadString().ConfigureAwait(true).GetAwaiter().GetResult();
            if (text != "hello")
                throw new Exception("error");
            clientWebsocket.SendString("test").ConfigureAwait(true).GetAwaiter().GetResult();
            text = clientWebsocket.ReadString().ConfigureAwait(true).GetAwaiter().GetResult();
            if (text != "test")
                throw new Exception("error");

            text = clientWebsocket.ReadString().ConfigureAwait(true).GetAwaiter().GetResult();
            if (text != null)
                throw new Exception("error");

            if(clientWebsocket.CloseStatus != WebSocketCloseStatus.NormalClosure)
                throw new Exception("error");

            if (clientWebsocket.CloseStatusDescription != "abc")
                throw new Exception("error");
        }
      
    }
}
