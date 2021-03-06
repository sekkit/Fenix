//Fenix, Inc.
//

using System;
using System.Net;
using System.Net.NetworkInformation; 
using DotNetty.Buffers;  
using System.Collections.Concurrent; 
using Fenix.Common;
using Fenix.Common.Utils; 
using Fenix.Common.Attributes; 
using Basic = Fenix.Common.Utils.Basic; 
using System.Text; 
using TimeUtil = Fenix.Common.Utils.TimeUtil;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using MessagePack;
using System.Linq;
//using MessagePack;

namespace Fenix
{
    //一个内网IP，必须
    //一个外网IP

    public partial class Host : Entity
    {
        public string Tag { get; set; }

        public IPEndPoint InternalAddress { get; set; }

        public IPEndPoint ExternalAddress { get; set; }
        
        public bool IsClientMode { get; set; }

        public dynamic self => this;

        protected ConcurrentDictionary<ulong, Actor> actorDic = new ConcurrentDictionary<ulong, Actor>();

        Thread internalThread;

        protected Host(string name, string ip, string extIp, int port = 0, bool clientMode = false) : base()
        {
            this.IsClientMode = clientMode;

            Global.NetManager.OnConnect += OnConnect;
            Global.NetManager.OnReceive += OnReceive;
            Global.NetManager.OnClose += OnClose;
            Global.NetManager.OnException += OnExcept;
            Global.NetManager.OnHeartBeat += OnHeartBeat;

            //如果是客户端，则用本地连接做为id
            //如果是服务端，则从名称计算一个id, 方便路由查找
            if (!clientMode)
            {
                string _ip = ip;
                string _extIp = extIp;
                int _port = port;

                if (ip == "auto")
                    _ip = Basic.GetLocalIPv4(NetworkInterfaceType.Ethernet);

                if (extIp == "auto")
                    _extIp = Basic.GetLocalIPv4(NetworkInterfaceType.Ethernet);

                if (port == 0)
                    _port = Basic.GetAvailablePort(IPAddress.Parse(_ip));

                this.InternalAddress = new IPEndPoint(IPAddress.Parse(_ip), _port);
                this.ExternalAddress = new IPEndPoint(IPAddress.Parse(_extIp), port);

                //string addr = LocalAddress.ToIPv4String();

                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;

                this.Id = Basic.GenID64FromName(this.UniqueName);
                this.RegisterGlobalManager(this); 

                Global.NetManager.RegisterHost(this);
            }
            else
            {
                if (name == null)
                    this.UniqueName = Basic.GenID64().ToString();
                else
                    this.UniqueName = name;

                this.Id = Basic.GenID64FromName(this.UniqueName);

                this.RegisterGlobalManager(this);
                Global.NetManager.RegisterHost(this);
            }

            if (!this.IsClientMode)
            {
                Log.Info(string.Format("{0}(ID:{1}) is running at {2} as ServerMode", this.UniqueName, this.Id, InternalAddress.ToString()));
            }
            else
            {
                Log.Info(string.Format("{0}(ID:{1}) is running as ClientMode", this.UniqueName, this.Id));
            }

            this.AddRepeatedTimer(3000, 10000, () =>
            {
                Global.NetManager.PrintPeerInfo("All peers:");

                foreach(var a in this.actorDic.Values)
                {
                    Log.Info("===========Actor info", a.Id, a.UniqueName);
                }

                Log.Info("End of Print");
            });

            internalThread = new Thread(new ThreadStart(StartHost));
            internalThread.Start();
        }

        public static Host Create(string name, string ip, string extIp, int port, bool clientMode)
        {
            if (Global.Host != null)
                return Global.Host;
            try
            {
                var c = new Host(name, ip, extIp, port, clientMode);
                Global.Host = c;
                return Global.Host;
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString()); 
            }
            return null;
        }

        public static Host CreateClient()//string ip, int port)
        {
            return Create(null, "", "", 0, true); 
        }

        public static Host CreateServer(string name, string ip, string extIp, int port)
        {
            return Create(name, ip, extIp, port, false);
        }

        double lastTs = 0;

        //public override byte[] Pack()
        //{
        //    return null;
        //    //return MessagePackSerializer.Serialize<Host>(this);
        //}

        void StartHost()
        {
#if USE_REDIS_IDMANAGER
            while(true)
            {
                Thread.Sleep(500);
                this.RegisterGlobalManager(this);
                var actorRemoveList = new List<ulong>();
                foreach (var kv in this.actorDic)
                    if (kv.Value.IsAlive)
                        this.RegisterGlobalManagerAsync(kv.Value);
                    else
                        actorRemoveList.Add(kv.Key);
                foreach (var aId in actorRemoveList)
                    actorDic.TryRemove(aId, out var _);
            }
#else
            while (true)
            {
                Thread.Sleep(500);
                var actorRemoveList = new List<ulong>();
                foreach (var kv in this.actorDic)
                    if (!kv.Value.IsAlive)
                        actorRemoveList.Add(kv.Key);
                foreach (var aId in actorRemoveList)
                    actorDic.TryRemove(aId, out var _);
            }
#endif
        }

        public bool IsIdHost()
        {
            return UniqueName == "Id.App";
        }

        public HostRouteData ToRouteData()
        {
            var routeData = new HostRouteData();
            routeData.HostId = this.Id;
            routeData.HostIntAddr = InternalAddress.ToIPv4String();
            routeData.HostExtAddr = ExternalAddress.ToIPv4String();
            routeData.HostName = this.UniqueName;
            routeData.IsClient = this.IsClientMode;
            foreach(var aId in this.actorDic.Keys.ToArray())
            {
                routeData.ActorIds.Add(aId);
            }
            return routeData;
        }

        public sealed override void Update()
        {
            base.EntityUpdate();

            if (IsAlive == false)
                return;

            foreach (var aId in this.actorDic.Keys)
                if(this.actorDic.TryGetValue(aId, out var a))
                    a?.Update();

            Global.NetManager?.Update();
        }

        public override void Destroy()
        {
            //先销毁所有的actor, netpeer
            //再销毁自己 
            try
            {
                internalThread?.Abort();
                internalThread = null;
            }
            catch(Exception ex)
            {
            }

            foreach (var a in this.actorDic.Values)
                a.Destroy(); 
            this.actorDic.Clear();
            base.Destroy(); 
            Global.NetManager.Destroy();
            Global.NetManager = new NetManager();
        } 

        protected void OnReceive(NetPeer peer, IByteBuffer buffer)
        {
            if (!peer.IsActive)
                return;
            //Log.Debug(string.Format("RECV({0}) {1} {2} {3}", peer.netType, peer.ConnId, peer.RemoteAddress, StringUtil.ToHexString(buffer.ToArray())));
            if (buffer.ReadableBytes == 1)
            {
                byte opCode = buffer.ReadByte();
                //Log.Warn("RECV_PROTOCOL", opCode);

                if (opCode == (byte)OpCode.PING)
                {
                    //Log.Info(string.Format("Ping({0}) {1} FROM {2}", peer.netType, peer.ConnId, peer.RemoteAddress));
                    
                    peer.Pong();

#if USE_REDIS_IDMANAGER
                    if (peer != null && peer.RemoteAddress != null && Global.IdManager.IsClientHost(peer.ConnId))
                        Global.IdManager.ReregisterHost(peer.ConnId, peer.RemoteAddress.ToIPv4String());
#endif

#if !CLIENT

#if USE_REDIS_IDMANAGER
                    //如果peer是客户端，则代表
                    var clientActorId = Global.IdManager.GetClientActorId(peer.ConnId);
                    if (clientActorId != 0 && this.actorDic.ContainsKey(clientActorId))
                    {
                        Global.IdManager.RegisterClientActor(clientActorId, GetActor(clientActorId).UniqueName, peer.ConnId, peer.RemoteAddress.ToIPv4String());
                    }
#endif

#endif

                    Global.NetManager.OnPong(peer);
                    return;
                }
                else if(opCode == (byte)OpCode.PONG)
                {
#if CLIENT
                    //Log.Info("ping>>>" + (TimeUtil.GetTimeStampMS2() - lastTs).ToString());
#endif
                    peer.lastTickTime = TimeUtil.GetTimeStampMS2();
                    Global.NetManager.OnPong(peer); 
                    return;
                }
                else if (opCode == (byte)OpCode.GOODBYE)
                {
                    //删除这个连接
                    Global.NetManager.Deregister(peer); 
                    return;
                }

                return;
            } 

            int protoCode = buffer.ReadIntLE();

#if !CLIENT
            if (protoCode == OpCode.REGISTER_REQ)
            {
                var hostId = (ulong)buffer.ReadLongLE();
                var nameBytes = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(nameBytes);
                var hostName = Encoding.UTF8.GetString(nameBytes);

                var context = new RpcContext(null, peer);

                this.Register(hostId, hostName, (code, info)=> { }, context);

                return; 
            }
#endif

            if (protoCode == OpCode.PARTIAL)
            {
                var partialId = (ulong)buffer.ReadLongLE();
                var partIndex = buffer.ReadByte();
                var totPartCount = buffer.ReadByte();
                var payload = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(payload);

                var finalBytes = Global.NetManager.AddPartialRpc(partialId, partIndex, totPartCount, payload);
                if (finalBytes != null)
                {
                    var finalBuf = Unpooled.WrappedBuffer(finalBytes);
                    var _protoCode = finalBuf.ReadIntLE();
#if !CLIENT
                    if (_protoCode == OpCode.REGISTER_REQ)
                    {
                        ProcessRegisterProtocol(peer, _protoCode, finalBuf);
                    }
                    else
                    {
                        ProcessRpcProtocol(peer, _protoCode, finalBuf);
                    }
#else
                    ProcessRpcProtocol(peer, _protoCode, finalBuf);
#endif
                }
                return;
            } 
                
            ProcessRpcProtocol(peer, protoCode, buffer); 
        }

        protected void OnClose(NetPeer peer)
        {
            Log.Info("OnClose", peer.ConnId, peer.IsRemoteClient);
#if !CLIENT
            if (peer.IsRemoteClient)
            {
                var aId = Global.IdManager.GetClientActorId(peer.ConnId); 
                if (aId != 0)
                {
                    var hId = Global.IdManager.GetHostIdByActorId(aId, true);
                    Log.Info("OnClose2", aId, hId);
                    if (this.actorDic.TryGetValue(aId, out var a)) 
                        a.OnClientDisable();

                    Global.IdManager.RemoveClientHost(hId);
                }
                else
                {
                    Global.IdManager.RemoveClientHost(peer.ConnId);
                }
            }
#endif
        }

        protected void OnExcept(NetPeer peer, Exception ex)
        {
            Log.Info("ONEXCEPT", peer.ConnId);
            Log.Error(ex);
        }

        protected void OnConnect(NetPeer peer)
        {

        }

        protected void OnHeartBeat()
        {
            if (!IsAlive)
                return;

            Global.IdManager.PrintInfo();

            if (IsClientMode) //客户端无法访问全局缓存
            {
                lastTs = TimeUtil.GetTimeStampMS2();
            }
            else
            { 
                
            }
        }

#if !CLIENT
        void ProcessRegisterProtocol(NetPeer peer, int protoCode, IByteBuffer buffer)
        {
            if (protoCode == OpCode.REGISTER_REQ)
            {
                var hostId = (ulong)buffer.ReadLongLE();
                var nameBytes = new byte[buffer.ReadableBytes];
                buffer.ReadBytes(nameBytes);
                var hostName = Encoding.UTF8.GetString(nameBytes);

                var context = new RpcContext(null, peer);

                this.Register(hostId, hostName, (code, info)=> { }, context);

                return;
            }
        }
#endif

        void ProcessRpcProtocol(NetPeer peer, int protoCode, IByteBuffer buffer)
        {
            var msgId = (ulong)buffer.ReadLongLE(); 
            var fromActorId = (ulong)buffer.ReadLongLE();
            var toActorId = (ulong)buffer.ReadLongLE();
            var bytes = new byte[buffer.ReadableBytes];
            buffer.ReadBytes(bytes);

            var packet = Packet.Create(msgId,
                protoCode,
                peer.ConnId,
                Global.Host.Id,
                fromActorId,
                toActorId,
                peer.netType,
                Global.TypeManager.GetMessageType(protoCode),
                bytes
            );

            Log.Info(string.Format("RECV2({0}): {1} {2} => {3} {4} >= {5} {6} => {7}",
                peer.netType,
                protoCode,
                packet.FromHostId,
                packet.ToHostId,
                packet.FromActorId,
                packet.ToActorId,
                peer.RemoteAddress.ToIPv4String(),
                peer.LocalAddress.ToIPv4String()));

            if (Math.Abs(protoCode) >= OpCode.CALL_ACTOR_METHOD && toActorId != 0)
            {
                this.CallActorMethod(packet);
            }
            else
            {
                this.CallMethod(packet);
            }
        }

        protected void RegisterGlobalManager(Host host)
        {
            Global.IdManager.RegisterHost(host, this.InternalAddress.ToIPv4String(), this.ExternalAddress.ToIPv4String(), this.IsClientMode);
        }

        //protected void RegisterGlobalManagerAsync(Actor a)
        //{
        //    Task.Run(() =>
        //    {
        //        Global.IdManager.RegisterActor(a, this.Id, this.IsClientMode);
        //        Global.TypeManager.RegisterActorType(a);
        //    });
        //}

        protected void RegisterGlobalManager(Actor a)
        { 
            Global.IdManager.RegisterActor(a, this.Id, this.IsClientMode);
            Global.TypeManager.RegisterActorType(a); 
        }

        public void RemoveActor(string uid)
        {
            var aId = Global.IdManager.GetActorId(uid);
            RemoveActorById(aId);
        }

        public void RemoveActorById(ulong aId)
        { 
            Global.IdManager.RemoveActorId(aId);
            this.actorDic.TryRemove(aId, out var _);
        }

        public override void CallMethod(Packet packet)
        {
            bool isCallback = packet.ProtoCode < 0;
            //bool isCallback = rpcDic.ContainsKey(packet.Id);
            //if (!isCallback)
            //{
            //    isCallback = Global.IdManager.GetRpcId(packet.Id) != 0; 
            //    if(!isCallback) 
            //        isCallback =  rpcTimeoutDic.ContainsKey(packet.Id); 
            //}

            if (isCallback)
            {
                if (!rpcDic.TryGetValue(packet.Id, out var cmd))
                {
                    var aId = Global.IdManager.GetRpcId(packet.Id);
                    this.actorDic.TryGetValue(aId, out var actor);
                    cmd = actor.GetRpc(packet.Id);
                }

                RemoveRpc(cmd.Id);
                cmd.Callback(packet.Payload);
            }
            else
            {
                var cmd = RpcCommand.Create(packet, null, this);
                cmd.Call(() => {
                    RemoveRpc(cmd.Id);
                });
            }
        }

        //调用Actor身上的方法
        protected void CallActorMethod(Packet packet)
        {
            if (packet.ToActorId == 0)
            {
                this.CallMethod(packet);
                return;
            }

            var actor = this.actorDic[packet.ToActorId];
            actor.CallMethod(packet);
        }

#if !CLIENT

        [ServerOnly]
        public void CreateActor(string typename, string name, Action<DefaultErrCode, ActorInfo> callback, RpcContext ctx)
        {
            if (name == "" || name == null)
            {
                callback(DefaultErrCode.ERROR, null);
                return;
            }

            var actorId = Global.IdManager.GetActorId(name);
            if (this.actorDic.TryGetValue(actorId, out var a))
            {
                Log.Info("create_actor_exists", actorId, a);
                a.Activate();
                callback(DefaultErrCode.create_actor_already_exists, Global.IdManager.GetActorInfo(a.Id));
                return;
            }
#if !CLIENT
            var hostId = Global.IdManager.GetHostIdByActorId(actorId);
            if (hostId != 0 && Global.Host.Id != hostId)
            {
                //callback(DefaultErrCode.create_actor_remote_exists, name, actorId);
                //return;
                //迁移actor到本地
                Log.Info("create_actor_exists2", actorId, hostId);
                var remoteHost = this.GetHost(hostId);
                remoteHost.MigrateActor(actorId, (code, actorData, actorInfo) =>
                {
                    Global.IdManager.RegisterActorInfo(actorInfo);
                    var actor = CreateActorLocally(typename, actorData);
                    if (actor != null)
                        callback(DefaultErrCode.OK, Global.IdManager.GetActorInfo(actor.Id));
                    else
                        callback(DefaultErrCode.ERROR, null);
                });
                return;
            }
#endif
            a = CreateActorLocally(typename, name);
            Log.Info("actor_create_result", a != null); 
          
            if (a != null)
                callback(DefaultErrCode.OK, Global.IdManager.GetActorInfo(a.Id));
            else
                callback(DefaultErrCode.ERROR, null);
            Log.Info("actor_create_cb");
        }

        //迁移actor
        [ServerOnly]
        public void MigrateActor(ulong actorId, Action<DefaultErrCode, byte[], ActorInfo> callback, RpcContext ctx)
        {
            if(!this.actorDic.ContainsKey(actorId))
            {
                callback(DefaultErrCode.migrate_actor_not_exists, null, null);
                return;
            }

            this.actorDic.TryRemove(actorId, out var a);
            var actorData = a.Pack();
            if (a.Client != null)
            {
                var clientId = Global.IdManager.GetHostIdByActorId(actorId, isClient:true);
                if(clientId != 0)
                {
                    var peer = Global.NetManager.GetLocalPeerById(clientId, Global.Config.ClientNetwork);
                    Global.NetManager.Deregister(peer);
                }

                /* if a client's actor is created somewhere else
                 * it means the client is kicked out by another client
                 * so the old client shall be destroyed.
                 * IN SHORT: a client actor cannot migrate with server actor
                 */
            }

            a.Destroy();
            callback(DefaultErrCode.OK, actorData, Global.IdManager.GetActorInfo(a.Id));
        }

        [ServerOnly] //移除actor
        public void RemoveActor(ulong actorId, Action<DefaultErrCode> callback, RpcContext ctx)
        {
            this.RemoveActorById(actorId);
            callback(DefaultErrCode.OK);
        }

        [ServerOnly]
        public void Register(ulong hostId, string hostName, Action<DefaultErrCode, HostInfo> callback, RpcContext ctx)
        { 
            if (ctx.Peer.ConnId != hostId)
            {
                //修正一下peer的id 
                Global.NetManager.ChangePeerId(ctx.Peer.ConnId, hostId, hostName, ctx.Peer.RemoteAddress.ToIPv4String()); 
            }
            else
            {
                Global.IdManager.RegisterHost(ctx.Peer.ConnId, hostId, hostName, ctx.Peer.RemoteAddress.ToIPv4String(), ctx.Peer.RemoteAddress.ToIPv4String(), false);
            }

            callback(DefaultErrCode.OK, Global.IdManager.GetHostInfo(this.Id, false));
        } 

        [ServerApi]
        public void RegisterClient(ulong hostId, string hostName, Action<DefaultErrCode, HostInfo> callback, RpcContext ctx)
        {
            //var _oldId = Global.IdManager.GetHostId(ctx.Peer.RemoteAddress.ToIPv4String());
            //if (_oldId == hostId && Global.IdManager.GetHostAddr(hostId) != ctx.Peer.RemoteAddress.ToIPv4String())
            //{
            //    Global.NetManager.Deregister(ctx.Peer);
            //    callback(DefaultErrCode.client_host_already_exists, Global.IdManager.GetHostInfo(this.Id));
            //    return;
            //}

            Global.IdManager.AddAddressID(ctx.Peer.ConnId, hostId);

            if (ctx.Peer.ConnId != hostId)
            {
                Global.NetManager.ChangePeerId(ctx.Peer.ConnId, hostId,
                    hostName, ctx.Peer.RemoteAddress.ToIPv4String()
                );
            }

            Global.NetManager.RegisterClient(hostId, hostName, ctx.Peer);
            var hostInfo = Global.IdManager.GetHostInfo(this.Id, true);
            hostInfo.HostAddr = hostInfo.HostExtAddr;
            callback(DefaultErrCode.OK, hostInfo);
        }

        [ServerApi]
        public void SayHello(Action<DefaultErrCode, HostInfo> callback, RpcContext ctx)
        {
            callback(DefaultErrCode.OK, Global.IdManager.GetHostInfo(this.Id));
        }

        [ServerApi]
        public void BindClientActor(string actorName, Action<DefaultErrCode> callback, RpcContext ctx)
        {
            //首先这个actor一定是本地的
            //如果actor不在本地，则把请求转到目标host上去
            //TODO，等想到了应用场景再加

            //find actor.server
            var actorId = Global.IdManager.GetActorId(actorName);
            //var hostAddr = Global.IdManager.GetHostAddrByActorId(actorId, false);
            Global.IdManager.RegisterClientActor(actorId, actorName, ctx.Packet.FromHostId, ctx.Peer.RemoteAddress.ToIPv4String());

            //give actor.server hostId, ipaddr to client
            
            //Set actor.server's client property
            Log.Info("binding_client_actor", actorId);
            var a = Global.Host.GetActor(actorId);
            if (a != null)
            {
                callback(DefaultErrCode.OK);
                a.OnClientEnable();
            }
            else
            {
                callback(DefaultErrCode.ERROR);
            }
        }

        [ServerApi]
        public async Task RemoveClientActor(ulong actorId, DisconnectReason reason, Action<DefaultErrCode> callback, RpcContext ctx)
        { 
            var clientId = Global.IdManager.GetHostIdByActorId(actorId, true);

            if(clientId == 0)
            {
                callback(DefaultErrCode.OK);
                return;
            }

            var hostId = Global.IdManager.GetHostIdByActorId(actorId);
            if (hostId != this.Id && hostId != 0)
            {
                //call remote host
                GetHost(hostId)?.RemoveClientActor(actorId, reason, callback);
                return;
            }

            var clientHost = this.GetHost(clientId);
            if (clientHost != null)
            {
                Log.Info("begin_notify_client_close", clientId);
                var result = await clientHost.OnBeforeDisconnectAsync(reason);
                Log.Info(result?.ToString());
            }

            var peer = Global.NetManager.GetLocalPeerById(clientId, Global.Config.ClientNetwork); 
            if (peer != null && !Global.NetManager.Deregister(peer))
            {
                Global.IdManager.RemoveClientHost(clientId);
                callback(DefaultErrCode.ERROR);
                return;
            }

            Global.IdManager.RemoveClientHost(clientId);
            callback(DefaultErrCode.OK);
        }

#endif

        [ClientApi]
        public void ReconnectServerActor(ulong hostId, string hostName, string hostIP, int hostPort, 
            ulong actorId, string actorName, string aTypeName,
            Action<DefaultErrCode> callback, RpcContext ctx)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse(hostIP), hostPort);
            string hostAddr = ep.ToIPv4String();

            Global.IdManager.RegisterHost(ctx.Peer.ConnId, hostId, hostName, hostAddr, hostAddr, false);
            Global.IdManager.RegisterActor(actorId, actorName, aTypeName, hostId, false);
             
            var avatarHost = GetHost(hostName, hostIP, hostPort);
            Global.NetManager.PrintPeerInfo("# Master.App: hostref created");
            avatarHost.BindClientActor(actorName, (code3) => {
                Global.NetManager.PrintPeerInfo("# Master.App: BindClientActor called");
                Log.Info("Avatar已经重新和服务端绑定");
            });
        }

        [ClientApi]
        public void OnBeforeDisconnect(DisconnectReason reason, Action callback, RpcContext ctx)
        {
            Log.Info("OnBeforeDisconnect", reason, this.InternalAddress, this.ExternalAddress);  

            foreach (var kv in actorDic)
                kv.Value.Destroy();
            actorDic.Clear();

            callback();
        }

        [ClientApi]
        public void OnServerActorEnable(string actorName, RpcContext ctx)
        {
#if CLIENT
            Log.Info("on_server_actor_enable", actorName);
            var actorId = Global.IdManager.GetActorId(actorName);
            //Set actor.server's client property
            var a = Global.Host.GetActor(actorId);
            a.OnServerEnable();
#endif
        }

        //[ClientApi]
        //public void Sync(ulong actorId, ulong dataKey, DataType dataType, byte[] data, RpcContext ctx)
        //{
        //    if(this.actorDic.TryGetValue(actorId, out var a))
        //    {
        //        //a.SyncData(dataType, data);
        //    }
        //}

#if !CLIENT

        Dictionary<ulong, ActorRef> HostRefDic = new Dictionary<ulong, ActorRef>();

        /*ID Route Info*/
        [ServerOnly]
        public async Task AddHostId(ulong hostId, string hostName, string intAddr, string extAddr, Action<bool, IdDataSet> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterHost(ctx.Peer.ConnId, hostId, hostName, intAddr, extAddr, false, noReg : true);
             
            //add hostref
            if (Global.IdManager.IsSameLocalhost(intAddr, this.InternalAddress.ToIPv4String()))
                HostRefDic[hostId] = this.GetHost(hostName, intAddr);
            else
                HostRefDic[hostId] = this.GetHost(hostName, extAddr);

            var hostInfo = Global.IdManager.GetHostInfo(hostId, true);

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if(h.toHostId != hostId)
                    await h.OnAddHostIdAsync(hostInfo, (code) => {
                        Log.Info("Notify(AddHostId):", h.toHostId);
                    });

            callback(result, Global.IdManager.GetIdAll());
        }

        /* 
         * When host disconnected, automatic deregisteration will happen, however this method is for
         * deregistering manually.
         */
        [ServerOnly]
        public async Task RemoveHostId(ulong hostId, string hostName, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RemoveHostId(hostId, noReg: true);
           
            HostRefDic.Remove(hostId);

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != hostId)
                    await h.OnRemoveHostIdAsync(hostId, (code) => {
                        Log.Info("Notify(RemoveHostId):", h.toHostId);
                    }); 

            callback(result); 
        }

        [ServerOnly]
        public async Task AddClientHostId(ulong fromHostId, ulong clientId, string clientName, string extAddr, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterClientHost(clientId, clientName, extAddr, noReg:true); 

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != fromHostId)
                    await h.OnAddClientHostIdAsync(clientId, clientName, extAddr, (code) => {
                        Log.Info("Notify(AddClientHostId):", h.toHostId);
                    });
            callback(result);
        }

        [ServerOnly]
        public async Task RemoveClientHostId(ulong fromHostId, ulong clientId, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RemoveClientHost(clientId, noReg: true);

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != fromHostId)
                    await h.OnRemoveClientHostIdAsync(clientId, (code) =>
                    {
                        Log.Info("Notify(RemoveClientHostId):", h.toHostId);
                    });
            callback(result);
        }

        [ServerOnly]
        public async Task AddClientActorId(ulong fromHostId, ulong clientId, ulong actorId, string actorName, string address, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterClientActor(actorId, actorName, clientId, address, noReg: true); 

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != fromHostId)
                    await h.OnAddClientActorIdAsync(clientId, actorId, actorName, address, (code) => {
                        Log.Info("Notify(AddClientActorId):", h.toHostId);
                    });

            callback(result);
        }

        [ServerOnly]
        public async Task AddActorId(ulong hostId, ulong actorId, string actorName, string aTypeName, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterActor(actorId, actorName, aTypeName, hostId, false, noReg: true); 

            var actorInfo = Global.IdManager.GetActorInfo(actorId);

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != hostId)
                    await h.OnAddActorIdAsync(actorInfo, (code) => {
                        Log.Info("Notify(AddActorId):", h.toHostId);
                    });
            callback(result);
        }

        [ServerOnly]
        public void RemoveActorId(ulong actorId, Action<bool> callback, RpcContext ctx)
        {
            var hostId = Global.IdManager.GetHostIdByActorId(actorId);
            var result = Global.IdManager.RemoveActorId(actorId, noReg: true);
            callback(result); 

            //notify all hosts
            foreach (var h in HostRefDic.Values)
                if (h.toHostId != hostId)
                    h.OnRemoveActorId(actorId, (code) => {
                        Log.Info("Notify(RemoveActorId):", h.toHostId);
                    });
        }

        [ServerOnly]
        public void OnAddHostId(HostInfo hostInfo, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterHostInfo(hostInfo);
            callback(result);
        }

        [ServerOnly]
        public void OnAddActorId(ActorInfo actorInfo, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterActorInfo(actorInfo);
            callback(result);
        }

        [ServerOnly]
        public void OnAddClientHostId(ulong clientId, string clientName, string address, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterClientHost(clientId, clientName, address, noReg: true);
            callback(result);
        }

        [ServerOnly]
        public void OnRemoveClientHostId(ulong clientId, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RemoveClientHost(clientId, noReg: true);
            callback(result);
        }

        [ServerOnly]
        public void OnAddClientActorId(ulong clientId, ulong actorId, string actorName, string address, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RegisterClientActor(actorId, actorName, clientId, address, noReg: true);
            callback(result);
        }

        [ServerOnly]
        public void OnRemoveHostId(ulong hostId, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RemoveHostId(hostId, noReg: true);
            callback(result);
        }

        [ServerOnly]
        public void OnRemoveActorId(ulong actorId, Action<bool> callback, RpcContext ctx)
        {
            var result = Global.IdManager.RemoveActorId(actorId, noReg: true);
            callback(result);
        }

        [ServerOnly]
        public void GetIdAll(ulong hostId, Action<bool, IdDataSet> callback, RpcContext ctx)
        {
            var idAll = Global.IdManager.GetIdAll();
            callback(true, idAll);
            //var hostInfoList = new List<HostInfo>();
            //foreach(var hId in Global.IdManager.GetHostIdList())
            //{
            //    hostInfoList.Add(Global.IdManager.GetHostInfo(hId));
            //}

            //callback(true, hostInfoList);
        }


        /*End of ID Route Info*/
#endif
    }
}
