﻿using JMS.Dtos;
using JMS.TransactionReporters;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Way.Lib;

namespace JMS
{
    public class InvokingInformation
    {
        public RegisterServiceLocation ServiceLocation { get; internal set; }
        public string ServiceName
        {
            get;
            internal set;
        }
        public string MethodName { get; internal set; }
        public object[] Parameters { get; internal set; }
    }
    class InvokeConnect : IInvokeConnect
    {

        NetClient _client;

        public InvokingInformation InvokingInfo { get; private set; }
        public InvokeCommand Command { get; private set; }
        public Invoker Invoker { get; private set; }
        public InvokeConnect(string serviceName, RegisterServiceLocation location, Invoker invoker)
        {
            InvokingInfo = new InvokingInformation();
            InvokingInfo.ServiceName = serviceName;
            InvokingInfo.ServiceLocation = location;
            Invoker = invoker;
        }


        public T Invoke<T>(string method, IRemoteClient tran, params object[] parameters)
        {
            return Invoke<T>(method, tran, tran.GetCommandHeader(), parameters);
        }

        public T Invoke<T>(string method, IRemoteClient tran, Dictionary<string, string> headers, params object[] parameters)
        {
            if (tran == null)
            {
                throw new ArgumentNullException("tran");
            }
            InvokingInfo.MethodName = method;
            InvokingInfo.Parameters = parameters;

            _client = NetClientPool.CreateClient(tran.ProxyAddress, InvokingInfo.ServiceLocation.ServiceAddress, InvokingInfo.ServiceLocation.Port, tran.ServiceClientCertificate);
            try
            {
                _client.ReadTimeout = tran.Timeout;
                Command = new InvokeCommand()
                {
                    Header = headers,
                    Service = InvokingInfo.ServiceName,
                    Method = method,
                    Parameters = parameters.Length == 0 ? null :
                                    parameters.GetStringArrayParameters()
                };


                _client.WriteServiceData(Command);
                var result = _client.ReadServiceObject<InvokeResult<T>>();
                if (result.Success == false)
                {
                    this.AddClientToPool();
                    throw new RemoteException(tran.TransactionId, result.Error);
                }
               

                if (result.SupportTransaction)
                    tran.AddConnect(this);
                else
                {
                    this.AddClientToPool();
                    this.Dispose();
                }

                return result.Data;
            }
            catch (ConvertException ex)
            {
                InvokeResult<string> otherObj = null;
                try
                {
                    otherObj = ex.Source.FromJson<InvokeResult<string>>();
                }
                catch
                {

                }

                if (otherObj.Success == false)
                {
                    throw new RemoteException(tran.TransactionId, otherObj.Error);
                }

                if (otherObj != null)
                    throw new ConvertException(otherObj.Data, $"无法将{ex.Source}里面的Data实例化为{typeof(T).FullName}");

                throw ex;
            }
            catch (Exception)
            {
                this.Dispose();
                throw;
            }


        }
        public Task<T> InvokeAsync<T>(string method, IRemoteClient tran, params object[] parameter)
        {
            var headers = tran.GetCommandHeader();
            return Task.Run(() => Invoke<T>(method, tran, headers, parameter));
        }

        public InvokeResult GoReadyCommit(IRemoteClient tran)
        {
            _client.WriteServiceData(new InvokeCommand()
            {
                Type = InvokeType.HealthyCheck,
            });
            return _client.ReadServiceObject<InvokeResult>();
        }

        public InvokeResult GoCommit(IRemoteClient tran)
        {
            _client.WriteServiceData(new InvokeCommand()
            {
                Type = InvokeType.CommitTranaction,
                Header = tran.GetCommandHeader()
            });
            return _client.ReadServiceObject<InvokeResult>();
        }

        public InvokeResult GoRollback(IRemoteClient tran)
        {
            _client.WriteServiceData(new InvokeCommand()
            {
                Type = InvokeType.RollbackTranaction,
                Header = tran.GetCommandHeader()
            });
            return _client.ReadServiceObject<InvokeResult>();
        }

        public void AddClientToPool()
        {
            NetClientPool.AddClientToPool(_client);
            _client = null;
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }

        public void RetryTranaction(NetAddress proxyAddress, RegisterServiceLocation serviceLocation, byte[] certData, string tranId)
        {
            X509Certificate2 cert = null;
            if (certData != null)
            {
                cert = new X509Certificate2(certData);
            }
            _client = NetClientPool.CreateClient(proxyAddress, serviceLocation.ServiceAddress, serviceLocation.Port, cert);
            var command = new InvokeCommand()
            {
                Type = InvokeType.RetryTranaction,
                Header = new Dictionary<string, string> {
                                    { "TranId" , tranId}
                                }
            };

            _client.WriteServiceData(command);
            var result = _client.ReadServiceObject<InvokeResult<int>>();
            if (result.Success && result.Data == 0)
            {
                TransactionReporterRoute.Logger?.LogInformation($"{serviceLocation.ServiceAddress}:{serviceLocation.Port}重新执行事务{tranId}成功");
            }

            this.AddClientToPool();
            if (result.Success == false || result.Data == -2)
            {
                if(string.IsNullOrEmpty(result.Error))
                    throw new Exception("重新执行事务失败");
                else
                    throw new Exception(result.Error);
            }
           
        }
    }
}