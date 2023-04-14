﻿
using System;
using System.Collections.Generic;
using System.Text;
using Way.Lib;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading;
using JMS.Dtos;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Web;
using Microsoft.CodeAnalysis;
using System.Reflection.PortableExecutable;
using System.Net;
using System.Buffers;
using static System.Runtime.InteropServices.JavaScript.JSType;
using JMS.ServerCore;

namespace JMS.Applications.CommandHandles
{
    /// <summary>
    /// 处理http请求
    /// </summary>
    class HttpRequestHandler
    {
        public async Task WebSocketProxy(NetClient client,NetClient proxyClient, GatewayCommand cmd)
        {
            readSend(client, proxyClient);
            await readSend(proxyClient, client);
        }
        public async Task Handle(NetClient client, GatewayCommand cmd)
        {

            if (cmd.Header == null)
            {
                cmd.Header = new Dictionary<string, string>();
            }

            var requestPathLine = await JMS.ServerCore.HttpHelper.ReadHeaders(null, client.InnerStream, cmd.Header);

            if (cmd.Header.TryGetValue("Host", out string host) == false)
                return;

            var config = HttpProxyServer.ProxyConfigs.FirstOrDefault(m =>string.Equals( m.Host , host, StringComparison.OrdinalIgnoreCase));
            if (config == null)
                return;

            int inputContentLength = 0;
            if (cmd.Header.ContainsKey("Content-Length"))
            {
                int.TryParse(cmd.Header["Content-Length"], out inputContentLength);
            }

            var ip = ((IPEndPoint)client.Socket.RemoteEndPoint).Address.ToString();
            if (cmd.Header.TryGetValue("X-Forwarded-For", out string xff))
            {
                if (xff.Contains(ip) == false)
                    xff += $", {ip}";
            }
            else
            {
                cmd.Header["X-Forwarded-For"] = ip;
            }

            if (cmd.Header.TryGetValue("Connection", out string connection) && string.Equals(connection, "keep-alive", StringComparison.OrdinalIgnoreCase))
            {
                client.KeepAlive = true;
            }
            else if (cmd.Header.ContainsKey("Connection") == false)
            {
                client.KeepAlive = true;
            }

            

            var targetUri = new Uri(config.Target);

            cmd.Header["Host"] = targetUri.Authority;

            StringBuilder buffer = new StringBuilder();
            buffer.AppendLine(requestPathLine);
            foreach (var pair in cmd.Header)
            {
                buffer.AppendLine($"{pair.Key}: {pair.Value}");
            }
            buffer.AppendLine("");
            var data = Encoding.UTF8.GetBytes(buffer.ToString());

            var proxyClient = await NetClientPool.CreateClientAsync(null, new NetAddress(targetUri.Host, targetUri.Port) { 
                UseSsl = string.Equals(targetUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(targetUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase),
                CertDomain = targetUri.Host
            });

            try
            {
                proxyClient.InnerStream.Write(data, 0, data.Length);

                if (string.Equals(connection, "Upgrade", StringComparison.OrdinalIgnoreCase)
                   && cmd.Header.TryGetValue("Upgrade", out string upgrade)
                   && string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase))
                {
                    await WebSocketProxy(client, proxyClient, cmd);
                    return;
                }

                if (inputContentLength > 0)
                {
                    //发送upload数据到服务器
                    await HttpHelper.ReadAndSend(client, proxyClient, inputContentLength);

                }
                else if (cmd.Header.TryGetValue("Transfer-Encoding", out string transferEncoding) && transferEncoding == "chunked")
                {
                    while (true)
                    {
                        var line = await client.ReadLineAsync();
                        proxyClient.WriteLine(line);
                        inputContentLength = Convert.ToInt32(line, 16);
                        if (inputContentLength == 0)
                        {
                            line = await client.ReadLineAsync();
                            proxyClient.WriteLine(line);
                            break;
                        }
                        else
                        {
                            await HttpHelper.ReadAndSend(client, proxyClient, inputContentLength);

                            line = await client.ReadLineAsync();
                            proxyClient.WriteLine(line);
                        }
                    }
                }

                //读取服务器发回来的头部
                cmd.Header.Clear();
                requestPathLine = await JMS.ServerCore.HttpHelper.ReadHeaders(null, proxyClient.InnerStream, cmd.Header);
                inputContentLength = 0;
                if (cmd.Header.ContainsKey("Content-Length"))
                {
                    int.TryParse(cmd.Header["Content-Length"], out inputContentLength);
                }

                buffer.Clear();
                buffer.AppendLine(requestPathLine);

                foreach (var pair in cmd.Header)
                {
                    buffer.AppendLine($"{pair.Key}: {pair.Value}");
                }

                buffer.AppendLine("");
                data = Encoding.UTF8.GetBytes(buffer.ToString());
                //发送头部给浏览器
                client.Write(data);

                if (inputContentLength > 0)
                {
                    await HttpHelper.ReadAndSend(proxyClient, client, inputContentLength);
                }
                else if (cmd.Header.TryGetValue("Transfer-Encoding", out string transferEncoding) && transferEncoding == "chunked")
                {
                    while (true)
                    {
                        var line = await proxyClient.ReadLineAsync();
                        client.WriteLine(line);
                        inputContentLength = Convert.ToInt32(line, 16);
                        if (inputContentLength == 0)
                        {
                            line = await proxyClient.ReadLineAsync();
                            client.WriteLine(line);
                            break;
                        }
                        else
                        {
                            await HttpHelper.ReadAndSend(proxyClient, client, inputContentLength);

                            line = await proxyClient.ReadLineAsync();
                            client.WriteLine(line);
                        }
                    }
                }

                if (client.KeepAlive)
                {
                    NetClientPool.AddClientToPool(proxyClient);
                }
                else
                {
                    proxyClient.Dispose();
                }
            }
            catch
            {
                proxyClient.Dispose();
            }
        }

        async Task readSend(NetClient sender, NetClient reader)
        {
            int len = 10240;
            var buffer = ArrayPool<byte>.Shared.Rent(len);

            try
            {
                while (true)
                {
                    var readed = await reader.InnerStream.ReadAsync(buffer, 0, len);
                    if (readed <= 0)
                    {
                        reader.Dispose();
                        sender.Dispose();
                        return;
                    }
                    await sender.InnerStream.WriteAsync(buffer, 0, readed);
                }
            }
            catch
            {
                reader.Dispose();
                sender.Dispose();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
