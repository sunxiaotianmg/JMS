﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace JMS.ServerCore
{
    public class MulitTcpListener
    {
        int _port;
        public int Port => _port;
        TcpListener _tcpListener;
        TcpListener _tcpListenerV6;
        public event EventHandler<Socket> Connected;
        public event EventHandler<Exception> OnError;
        bool _stopped = true;
        public MulitTcpListener(int port)
        {
            this._port = port;
           
        }

        public void Stop()
        {
            _stopped = true;
            _tcpListener?.Stop();
            _tcpListenerV6?.Stop();

            _tcpListener = null;
            _tcpListenerV6 = null;
        }

        public void Run()
        {
            if (_stopped == false)
                return;

            _stopped = false;
            _tcpListener = new TcpListener(IPAddress.Any, _port);
            _tcpListenerV6 = new TcpListener(IPAddress.IPv6Any, _port);

            new Thread(() => { runListener(_tcpListenerV6); }).Start();
            this.runListener(_tcpListener);
        }

        void runListener(TcpListener listener)
        {
            try
            {
                listener.Start();
                while (true)
                {
                    var socket = listener.AcceptSocket();
                    if (this.Connected != null)
                    {
                        this.Connected(this, socket);
                    }
                }
            }
            catch(Exception ex)
            {
                if (!_stopped)
                {
                    OnError?.Invoke(this, ex);
                }
            }
        }
    }
}
