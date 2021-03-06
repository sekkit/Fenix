﻿//AUTOGEN, do not modify it!

using Fenix.Common.Utils;
using Fenix.Common;
using Fenix.Common.Attributes;
using Fenix.Common.Rpc;
using MessagePack; 
using System.ComponentModel;
using System; 

namespace Fenix.Common.Message
{
    [MessageType(OpCode.ON_ADD_CLIENT_HOST_ID_REQ)]
    [MessagePackObject]
    public class OnAddClientHostIdReq : IMessageWithCallback
    {
        [Key(0)]
        public global::System.UInt64 clientId { get; set; }

        [Key(1)]
        public global::System.String clientName { get; set; }

        [Key(2)]
        public global::System.String address { get; set; }

        [Key(3)]

        public Callback callback
        {
            get => _callback as Callback;
            set => _callback = value;
        } 

        [MessagePackObject]
        public class Callback : IMessage
        {
            [Key(0)]
            public global::System.Boolean arg0 { get; set; }

            public override byte[] Pack()
            {
                return MessagePackSerializer.Serialize<Callback>(this);
            }

            public new static Callback Deserialize(byte[] data)
            {
                return MessagePackSerializer.Deserialize<Callback>(data);
            }

            public override void UnPack(byte[] data)
            {
                var obj = Deserialize(data);
                Copier<Callback>.CopyTo(obj, this);
            }
        }

        public override byte[] Pack()
        {
            return MessagePackSerializer.Serialize<OnAddClientHostIdReq>(this);
        }

        public new static OnAddClientHostIdReq Deserialize(byte[] data)
        {
            return MessagePackSerializer.Deserialize<OnAddClientHostIdReq>(data);
        }

        public override void UnPack(byte[] data)
        {
            var obj = Deserialize(data);
            Copier<OnAddClientHostIdReq>.CopyTo(obj, this);
        }
    }
}

