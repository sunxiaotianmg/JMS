﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;

namespace JMS.ServerCore
{
    public class HttpHelper
    {
        /// <summary>
        /// 获取websocket响应串
        /// </summary>
        public static string GetWebSocketResponse(IDictionary<string, string> header)
        {
            string secWebSocketKey = header["Sec-WebSocket-Key"].ToString();
            string m_Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            string responseKey = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(secWebSocketKey + m_Magic)));

            StringBuilder response = new StringBuilder(); //响应串
            response.Append("HTTP/1.1 101 Web Socket Protocol JMS\r\n");

            //将请求串的键值转换为对应的响应串的键值并添加到响应串
            response.AppendFormat("Upgrade: {0}\r\n", header["Upgrade"]);
            response.AppendFormat("Connection: {0}\r\n", header["Connection"]);
            response.AppendFormat("Sec-WebSocket-Accept: {0}\r\n", responseKey);
            if (header.ContainsKey("Origin"))
            {
                response.AppendFormat("WebSocket-Origin: {0}\r\n", header["Origin"]);
            }
            if (header.ContainsKey("Host"))
            {
                response.AppendFormat("WebSocket-Location: {0}\r\n", header["Host"]);
            }

            response.Append("\r\n");

            return response.ToString();

        }

        /// <summary>
        /// 解析http请求头
        /// </summary>
        /// <param name="preRequestString"></param>
        /// <param name="stream"></param>
        /// <param name="headers"></param>
        /// <returns>请求的第一行语句</returns>
        /// <exception cref="SocketException"></exception>
        public static async Task<string> ReadHeaders(string preRequestString, Stream stream, IDictionary<string, string> headers)
        {
            List<byte> lineBuffer = new List<byte>(1024);
            string line = null;
            string requestPathLine = null;
            byte[] bData = new byte[1];
            int readed;
            int indexFlag;
            while (true)
            {
                readed = await stream.ReadAsync(bData, 0, 1);
                if (readed <= 0)
                    throw new SocketException();

                if (bData[0] == 10)
                {
                    if (lineBuffer.Count == 0)
                        break;

                    line = Encoding.UTF8.GetString(lineBuffer.ToArray());
                    lineBuffer.Clear();
                    if (requestPathLine == null)
                        requestPathLine = preRequestString + line;

                    if ((indexFlag = line.IndexOf(':',0)) > 0 && indexFlag < line.Length - 1)
                    {
                        var key = line.Substring(0, indexFlag);
                        var value = line.Substring(indexFlag + 1).Trim();
                        if (headers.ContainsKey(key) == false)
                        {
                            headers[key] = value;
                        }
                    }
                }
                else if (bData[0] != 13)
                {
                    lineBuffer.Add(bData[0]);
                }
            }
            return requestPathLine;
        }
    }
}