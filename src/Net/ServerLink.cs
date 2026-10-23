// One game's link to the relay server (server/relay.py). Receiving happens on
// a background thread; everything else (joining, keepalive, timeouts, events)
// runs when the game calls Poll() from its tick, so game code never sees
// other threads.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace RDR2Coop.Net
{
    public enum LinkState { Connecting, Connected, Closed }

    public sealed class ServerLink : IDisposable
    {
        public const int DefaultPort = 27460;
        public const int TimeoutMs = 5000;
        const int HelloEveryMs = 1000;      // while joining
        const int KeepaliveEveryMs = 2000;  // once in: the server answers with Welcome

        public LinkState State { get; private set; } = LinkState.Connecting;
        public string Name { get; }
        public byte MyId { get; private set; }
        public byte HostId { get; private set; }
        public bool IAmHost => MyId != 0 && MyId == HostId;
        public List<RosterEntry> Roster { get; private set; } = new List<RosterEntry>();
        public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint).Port;

        public event Action Joined;
        public event Action<string> Disconnected;
        public event Action RosterChanged;
        public event Action<byte, PlayerState, long> StateReceived;
        public event Action<byte, WorldSnapshot, long> WorldReceived;
        public event Action<byte, Hit> HitReceived;
        public event Action<byte, PartyMsg> PartyReceived;
        public event Action<byte, Appearance> AppearanceReceived;
        public event Action<byte, List<Objective>> ObjectivesReceived;
        public event Action<byte, GuestAction> ActionReceived;
        public event Action<byte, NoticeMsg> NoticeReceived;
        public event Action<byte, ushort, Appearance> EntityLookReceived;

        readonly UdpClient _udp;
        readonly IPEndPoint _server;
        readonly string _password;
        readonly bool _preferHost;
        readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> _inbox = new ConcurrentQueue<(IPEndPoint, byte[])>();
        readonly Thread _receiver;
        volatile bool _running = true;
        long _lastHeardMs, _lastHelloMs = -HelloEveryMs;
        uint _hitSeq;
        readonly HashSet<(byte, uint)> _seenHits = new HashSet<(byte, uint)>();
        readonly Queue<(byte, uint)> _seenOrder = new Queue<(byte, uint)>();
        readonly WorldAssembler _assembler = new WorldAssembler();

        public ServerLink(string name, bool preferHost, string password, string serverHost, int port = DefaultPort)
        {
            Name = name ?? "";
            _preferHost = preferHost;
            _password = password ?? "";
            var all = Dns.GetHostAddresses(serverHost);
            var ip = Array.Find(all, a => a.AddressFamily == AddressFamily.InterNetwork) ?? all[0];
            _server = new IPEndPoint(ip, port);
            _udp = new UdpClient(0, ip.AddressFamily);
            IgnoreConnectionResets(_udp);
            _receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "RDR2Coop.Receive" };
            _receiver.Start();
        }

        // Call once per game tick with a steady millisecond clock.
        public void Poll(long nowMs)
        {
            if (State == LinkState.Closed) return;

            while (_inbox.TryDequeue(out var msg))
                if (msg.from.Equals(_server)) Handle(msg.data, nowMs);

            int every = State == LinkState.Connected ? KeepaliveEveryMs : HelloEveryMs;
            if (nowMs - _lastHelloMs >= every)
            {
                _lastHelloMs = nowMs;
                Send(Protocol.Hello(Name, _preferHost));
            }

            if (State == LinkState.Connected && nowMs - _lastHeardMs > TimeoutMs)
                Drop("server not answering", nowMs);
        }

        public void SendState(PlayerState s) { if (State == LinkState.Connected) Send(Protocol.State(s)); }

        public void SendWorld(WorldSnapshot w)
        {
            if (State != LinkState.Connected) return;
            foreach (var packet in Protocol.World(w)) Send(packet);
        }

        // Hits matter more than positions: send each one twice, the receiver
        // drops the repeat by (sender, sequence).
        public void SendHit(byte to, ushort entityId, short damage, int bone, uint weapon = 0)
        {
            if (State != LinkState.Connected || to == 0 || to == MyId) return;
            var data = Protocol.HitPacket(new Hit { Seq = ++_hitSeq, EntityId = entityId, Damage = damage, Bone = bone, Weapon = weapon }, to);
            Send(data);
            Send(data);
        }

        public void SendParty(PartyMsg m) { if (State == LinkState.Connected) Send(Protocol.PartyPacket(m)); }

        public void SendAppearance(Appearance a) { if (State == LinkState.Connected) Send(Protocol.AppearancePacket(a)); }

        public void SendEntityLook(ushort id, Appearance a) { if (State == LinkState.Connected) Send(Protocol.EntityLookPacket(id, a)); }

        public void SendObjectives(List<Objective> o) { if (State == LinkState.Connected) Send(Protocol.ObjectivesPacket(o)); }

        public void SendNotice(byte to, NoticeKind kind, string text)
        {
            if (State == LinkState.Connected && to != MyId) Send(Protocol.NoticePacket(new NoticeMsg { Kind = kind, Text = text }, to));
        }

        // Like hits: twice, deduplicated by (sender, sequence) on arrival.
        public void SendAction(byte to, ActionKind kind, ushort entityId)
        {
            if (State != LinkState.Connected || to == 0 || to == MyId) return;
            var data = Protocol.ActionPacket(new GuestAction { Seq = ++_hitSeq, Kind = kind, EntityId = entityId }, to);
            Send(data);
            Send(data);
        }

        public string NameOf(byte id) => Roster.Find(e => e.Id == id).Name ?? "?";

        void Handle(byte[] data, long nowMs)
        {
            var p = Protocol.Read(data, data.Length);
            if (p == null) return;
            _lastHeardMs = nowMs;

            switch (p.Type)
            {
                case PacketType.Challenge:
                    Send(Protocol.Auth(p.Nonce, _password));
                    return;
                case PacketType.Welcome:
                    if (p.ProtocolVersion != Protocol.Version) return;
                    bool fresh = State != LinkState.Connected || MyId != p.MyId;
                    MyId = p.MyId;
                    HostId = p.HostId;
                    if (fresh)
                    {
                        _seenHits.Clear();
                        _seenOrder.Clear();
                        State = LinkState.Connected;
                        Joined?.Invoke();
                    }
                    return;
                case PacketType.Reject:
                    State = LinkState.Closed;
                    Disconnected?.Invoke("refused: " + p.Reason);
                    return;
                case PacketType.Roster:
                    foreach (var gone in Roster.Where(o => !p.Roster.Exists(n => n.Id == o.Id))) _assembler.Forget(gone.Id);
                    Roster = p.Roster;
                    var host = p.Roster.Find(e => e.Role == PlayerRole.Host);
                    HostId = host.Id;
                    RosterChanged?.Invoke();
                    return;
            }

            if (State != LinkState.Connected || p.From == 0 || p.From == MyId) return;
            if (p.Type == PacketType.State) StateReceived?.Invoke(p.From, p.State, nowMs);
            else if (p.Type == PacketType.World && _assembler.Add(p.From, p.World, out var whole)) WorldReceived?.Invoke(p.From, whole, nowMs);
            else if (p.Type == PacketType.Hit && FirstTime(p.From, p.Hit.Seq)) HitReceived?.Invoke(p.From, p.Hit);
            else if (p.Type == PacketType.Party) PartyReceived?.Invoke(p.From, p.Party);
            else if (p.Type == PacketType.Appearance) AppearanceReceived?.Invoke(p.From, p.Appearance);
            else if (p.Type == PacketType.Objectives) ObjectivesReceived?.Invoke(p.From, p.Objectives);
            else if (p.Type == PacketType.Action && FirstTime(p.From, p.Action.Seq)) ActionReceived?.Invoke(p.From, p.Action);
            else if (p.Type == PacketType.Notice) NoticeReceived?.Invoke(p.From, p.Notice);
            else if (p.Type == PacketType.EntityLook) EntityLookReceived?.Invoke(p.From, p.LookId, p.Appearance);
        }

        bool FirstTime(byte from, uint seq)
        {
            if (!_seenHits.Add((from, seq))) return false;
            _seenOrder.Enqueue((from, seq));
            if (_seenOrder.Count > 512) _seenHits.Remove(_seenOrder.Dequeue());
            return true;
        }

        void Drop(string reason, long nowMs)
        {
            State = LinkState.Connecting;
            MyId = 0;
            HostId = 0;
            Roster = new List<RosterEntry>();
            _lastHelloMs = nowMs - HelloEveryMs;
            Disconnected?.Invoke(reason);
        }

        void Send(byte[] data)
        {
            try { _udp.Send(data, data.Length, _server); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var from = any;
                    var data = _udp.Receive(ref from);
                    _inbox.Enqueue((from, data));
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }

        // Windows (and Wine) report an ICMP "port unreachable" as an error on
        // the next Receive; SIO_UDP_CONNRESET turns that off.
        static void IgnoreConnectionResets(UdpClient udp)
        {
            try { udp.Client.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (State == LinkState.Connected) Send(Protocol.Bye());
            State = LinkState.Closed;
            _running = false;
            _udp.Close();
        }
    }
}
