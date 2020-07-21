﻿/*
 * RpcCommand
 */

using Fenix;
using Fenix.Common.Attributes;
using Fenix.Common.Rpc;
using Fenix.Common.Utils; 
using System; 
using System.Linq;
using System.Reflection; 

public class RpcCommand
{
    public ulong Id;

    public uint FromActorId;

    public uint ToActorId;

    public uint FromContainerId;

    public uint ToContainerId;

    public IMessage Msg;

    public Api RpcType;

    public uint ProtoCode;

    protected RpcModule mInvoker;

    protected Action<byte[]> callbackMethod;

    protected RpcCommand()
    {
        
    }
     
    public static RpcCommand Create(
        ulong protoId,
        uint fromContainerId,
        uint toContainerId,
        uint fromActorId,
        uint toActorId, 
        uint protoCode,
        IMessage msg,
        Action<byte[]> cb,
        RpcModule invoker)
    {
        var obj             = new RpcCommand();
        obj.Id = protoId;
        obj.Msg             = msg;
        obj.FromContainerId = fromContainerId;
        obj.ToContainerId   = toContainerId;
        obj.FromActorId     = fromActorId;
        obj.ToActorId       = toActorId;
        obj.mInvoker        = invoker;
        obj.RpcType         = invoker.GetRpcType(protoCode);
        obj.ProtoCode       = protoCode;
        obj.callbackMethod  = cb;
        return obj;
    }

    public T ToMessage<T>() where T : IMessage
    {
        return this.Msg as T;
        //Type type = Global.TypeManager.GetMessageType(this.ProtoCode);
        //var msgObj    = MessagePackSerializer.Deserialize(type, this.msg.Pack());
        //return (T)msgObj;
    }

    public void Call(Action callDone)
    {
        var args = new object[2];
        args[0] = this.Msg;

        if (!this.Msg.HasCallback())
        {
            callDone?.Invoke();
        }
        else
        {
            var cb = new Action<object>((cbMsg) =>
            {
                callDone?.Invoke();
                this.mInvoker.RpcCallback(this.Id, this.ProtoCode, this.ToActorId, this.FromActorId, cbMsg);
            });

            args[1] = cb;
        }

        if (Global.IsServer)
        {
            if (RpcType == Api.ServerApi)
                this.mInvoker.CallLocalMethod(this.ProtoCode, args.ToArray());
            else if (RpcType == Api.ServerOnly)
                this.mInvoker.CallLocalMethod(this.ProtoCode, args.ToArray());
        }
        else
        {
            if (RpcType == Api.ClientApi)
                this.mInvoker.CallLocalMethod(this.ProtoCode, args.ToArray());
        }
    }

    public void Callback(byte[] cbData)
    {
        this.callbackMethod?.Invoke(cbData); 
    }
}

