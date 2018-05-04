// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Infrastructure;

namespace Microsoft.AspNet.SignalR.WebSockets
{
    internal static class WebSocketMessageReader
    {
        private static readonly byte[] _emptyArray = new byte[0];

        private static byte[] BufferSliceToByteArray(byte[] buffer, int count)
        {
            if (count == 0) return _emptyArray;

            byte[] newArray = new byte[count];
            Buffer.BlockCopy(buffer, 0, newArray, 0, count);
            return newArray;
        }

        private static string BufferSliceToString(byte[] buffer, int count)
        {
            return Encoding.UTF8.GetString(buffer, 0, count);
        }

        public static async Task<WebSocketMessage> ReadMessageAsync(WebSocket webSocket, int bufferSize, int? maxMessageSize, CancellationToken disconnectToken)
        {
            var buffer = new byte[bufferSize];

            // Read with the real buffer
            var arraySegment = new ArraySegment<byte>(buffer);

            var receiveResult = await webSocket.ReceiveAsync(arraySegment, disconnectToken).PreserveCultureNotContext();

            if (TryGetMessage(receiveResult, buffer, out var message))
            {
                return message;
            }

            // for multi-fragment messages, we need to coalesce
            ByteBuffer bytebuffer = new ByteBuffer(maxMessageSize);
            bytebuffer.Append(BufferSliceToByteArray(buffer, receiveResult.Count));
            WebSocketMessageType originalMessageType = receiveResult.MessageType;

            while (true)
            {
                // loop until an error occurs or we see EOF
                receiveResult = await webSocket.ReceiveAsync(arraySegment, disconnectToken).PreserveCultureNotContext();

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    return WebSocketMessage.CloseMessage;
                }

                if (receiveResult.MessageType != originalMessageType)
                {
                    throw new InvalidOperationException("Incorrect message type");
                }

                bytebuffer.Append(BufferSliceToByteArray(buffer, receiveResult.Count));

                if (receiveResult.EndOfMessage)
                {
                    switch (receiveResult.MessageType)
                    {
                        case WebSocketMessageType.Binary:
                            return new WebSocketMessage(bytebuffer.GetByteArray(), WebSocketMessageType.Binary);

                        case WebSocketMessageType.Text:
                            return new WebSocketMessage(bytebuffer.GetString(), WebSocketMessageType.Text);

                        default:
                            throw new InvalidOperationException("Unknown message type");
                    }
                }
            }
        }

        private static bool TryGetMessage(WebSocketReceiveResult receiveResult, byte[] buffer, out WebSocketMessage message)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            message = null;

            if (receiveResult.MessageType == WebSocketMessageType.Close)
            {
                message = WebSocketMessage.CloseMessage;
            }
            else if (receiveResult.EndOfMessage)
            {
                // we anticipate that single-fragment messages will be common, so we optimize for them
                switch (receiveResult.MessageType)
                {
                    case WebSocketMessageType.Binary:
                        message = new WebSocketMessage(BufferSliceToByteArray(buffer, receiveResult.Count), WebSocketMessageType.Binary);
                        break;
                    case WebSocketMessageType.Text:
                        message = new WebSocketMessage(BufferSliceToString(buffer, receiveResult.Count), WebSocketMessageType.Text);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown message type");
                }
            }

            return message != null;
        }
    }
}
