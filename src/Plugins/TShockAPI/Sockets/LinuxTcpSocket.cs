/*
TShock, a server mod for Terraria
Copyright (C) 2011-2019 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Terraria;
using Terraria.Localization;
using Terraria.Net;
using Terraria.Net.Sockets;
using UnifiedServerProcess;

namespace TShockAPI.Sockets
{
	public class LinuxTcpSocket : ISocket
	{
		public byte[] _packetBuffer = new byte[1024];

		public int _packetBufferLength;

		public List<object> _callbackBuffer = new List<object>();

		public int _messagesInQueue;

		public TcpClient _connection;

		public RemoteAddress? _remoteAddress;

		public bool _isListening;

		public int MessagesInQueue
		{
			get
			{
				return this._messagesInQueue;
			}
		}

		public LinuxTcpSocket()
		{
			this._connection = new TcpClient();
			this._connection.NoDelay = true;
		}

		public LinuxTcpSocket(TcpClient tcpClient)
		{
			this._connection = tcpClient;
			this._connection.NoDelay = true;
			IPEndPoint iPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
			this._remoteAddress = new TcpAddress(iPEndPoint.Address, iPEndPoint.Port);
		}

		void ISocket.Close()
		{
			this._remoteAddress = null;
			this._connection.Close();
		}

		bool ISocket.IsConnected()
		{
			return this._connection != null && this._connection.Client != null && this._connection.Connected;
		}

		void ISocket.Connect(RemoteAddress address)
		{
			TcpAddress tcpAddress = (TcpAddress)address;
			this._connection.Connect(tcpAddress.Address, tcpAddress.Port);
			this._remoteAddress = address;
		}

		private void ReadCallback(IAsyncResult result)
		{
			Tuple<SocketReceiveCallback, object> tuple = (Tuple<SocketReceiveCallback, object>)result.AsyncState!;

			try
			{
				tuple.Item1(tuple.Item2, this._connection.GetStream().EndRead(result));
			}
			catch (InvalidOperationException)
			{
				// This is common behaviour during client disconnects
				((ISocket)this).Close();
			}
			catch (Exception ex)
			{
				TShock.Log.Error(ex.ToString());
			}
		}

		private void SendCallback(IAsyncResult result)
		{
			object[] expr_0B = (object[])result.AsyncState!;
			LegacyNetBufferPool.ReturnBuffer((byte[])expr_0B[1]);
			Tuple<SocketSendCallback, object> tuple = (Tuple<SocketSendCallback, object>)expr_0B[0];
			try
			{
				this._connection.GetStream().EndWrite(result);
				tuple.Item1(tuple.Item2);
			}
			catch (Exception)
			{
				((ISocket)this).Close();
			}
		}

		void ISocket.AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state)
		{
			byte[] array = LegacyNetBufferPool.RequestBuffer(data, offset, size);
			this._connection.GetStream().BeginWrite(array, 0, size, new AsyncCallback(this.SendCallback), new object[]
			{
				new Tuple<SocketSendCallback, object>(callback, state),
				array
			});
		}

		void ISocket.AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state)
		{
			this._connection.GetStream().BeginRead(data, offset, size, new AsyncCallback(this.ReadCallback), new Tuple<SocketReceiveCallback, object>(callback, state));
		}

		bool ISocket.IsDataAvailable()
		{
			return this._connection.GetStream().DataAvailable;
		}

		RemoteAddress? ISocket.GetRemoteAddress()
		{
			return this._remoteAddress;
		}

		bool ISocket.StartListening(RootContext root, SocketConnectionAccepted callback)
		{
			throw new NotImplementedException();
		}

		void ISocket.StopListening()
		{
			this._isListening = false;
		}

        public void AsyncSendNoCopy(byte[] data, int offset, int size, SocketSendCallback callback, object? state = null) {
            byte[] array = LegacyNetBufferPool.RequestBuffer(data, offset, size);
            this._connection.GetStream().BeginWrite(array, 0, size, new AsyncCallback(this.SendCallback), new object[]
            {
                new Tuple<SocketSendCallback, object>(callback, state),
                array
            });
        }

        public void AsyncSend(ReadOnlyMemory<byte> data, SocketSendCallback callback, object? state = null) {
            byte[] array = LegacyNetBufferPool.RequestBuffer(data.Length);
			data.CopyTo(array);
            this._connection.GetStream().BeginWrite(array, 0, data.Length, new AsyncCallback(this.SendCallback), new object[]
            {
                new Tuple<SocketSendCallback, object>(callback, state),
                array
            });
        }
    }
}
