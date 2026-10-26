// The co-op server built into the mod (RunServer=Yes in RDR2Coop.ini): the same
// routing as server/relay.py, so a group can play without a separate server.
// Relay is the logic (no sockets, tests drive it); RelayHost runs it on a UDP
// port on its own thread.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RDR2Coop.Net
{
    public sealed class Relay
    {
        const int MaxPlayers = 4, MaxPending = 256, MaxPacket = 1400, MaxName = 32;
        const double TimeoutS = 10.0, ChallengeTtlS = 10.0;
        const double LostHostS = 120.0;   // a dropped host who prefers hosting gets the role back if it rejoins within this
        const byte Broadcast = 255, Guest = 0, Host = 1;

        sealed class Player
        {
            public byte Id, Role;
            public IPEndPoint Addr;
            public string Name, Build;
            public bool PreferHost;
            public double LastHeard;
        }

        // Server browser, home network: a game broadcasts a query ("RDCQ", padded
        // so the answer is never bigger than the question: no use for
        // amplification attacks) and servers answer "RDCA" + protocol version,
        // players, max players and the room name.
        public const int QuerySize = 160;
        public static readonly byte[] QueryMagic = { (byte)'R', (byte)'D', (byte)'C', (byte)'Q' };

        readonly string _roomName;
        readonly byte[] _key;
        readonly Func<double> _clock;
        readonly Action<string> _log;
        readonly Dictionary<IPEndPoint, Player> _players = new Dictionary<IPEndPoint, Player>();
        readonly Dictionary<IPEndPoint, (byte[] nonce, bool preferHost, string name, string build, double expires)> _pending =
            new Dictionary<IPEndPoint, (byte[], bool, string, string, double)>();
        readonly Dictionary<IPAddress, List<double>> _helloTimes = new Dictionary<IPAddress, List<double>>();
        readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        double _lastSweep;
        (IPAddress ip, double until) _lostHost;   // the last host dropped while preferring the role

        public Relay(string password, Func<double> clock, Action<string> log, string roomName = null)
        {
            _roomName = string.IsNullOrWhiteSpace(roomName) ? "RDR2 Co-op server" : roomName;
            _key = Encoding.UTF8.GetBytes(password ?? "");
            _clock = clock;
            _log = log ?? (_ => { });
        }

        public int PlayerCount => _players.Count;

        static byte[] Header(PacketType t, byte sender, byte to) =>
            new byte[] { (byte)'R', (byte)'D', (byte)'C', (byte)'O', (byte)t, sender, to };

        static byte[] ShortString(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > MaxName) Array.Resize(ref b, MaxName);
            return new[] { (byte)b.Length }.Concat(b).ToArray();
        }

        static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        Player HostPlayer() => _players.Values.FirstOrDefault(p => p.Role == Host);

        byte FreeId()
        {
            var used = new HashSet<byte>(_players.Values.Select(p => p.Id));
            for (byte i = 1; i < 255; i++) if (!used.Contains(i)) return i;
            return 0;
        }

        byte[] RosterPacket()
        {
            var body = new List<byte> { (byte)_players.Count };
            foreach (var p in _players.Values.OrderBy(p => p.Id))
            {
                body.Add(p.Id);
                body.Add(p.Role);
                body.AddRange(ShortString(p.Name));
            }
            return Cat(Header(PacketType.Roster, 0, Broadcast), body.ToArray());
        }

        List<(byte[], IPEndPoint)> ToAll(byte[] data, IPEndPoint except = null) =>
            _players.Values.Where(p => !p.Addr.Equals(except)).Select(p => (data, p.Addr)).ToList();

        static List<(byte[], IPEndPoint)> Reject(IPEndPoint addr, string reason) =>
            new List<(byte[], IPEndPoint)> { (Cat(Header(PacketType.Reject, 0, 0), ShortString(reason)), addr) };

        static readonly List<(byte[], IPEndPoint)> None = new List<(byte[], IPEndPoint)>();

        bool RateLimited(IPAddress ip, double now)
        {
            if (!_helloTimes.TryGetValue(ip, out var times)) _helloTimes[ip] = times = new List<double>();
            times.RemoveAll(t => now - t >= 10);
            times.Add(now);
            return times.Count > 20;
        }

        // One packet in, the packets to send back out.
        public List<(byte[] data, IPEndPoint to)> Handle(byte[] data, IPEndPoint addr)
        {
            double now = _clock();
            if (data.Length >= QuerySize && data[0] == 'R' && data[1] == 'D' && data[2] == 'C' && data[3] == 'Q')
                return new List<(byte[], IPEndPoint)> { (QueryAnswer(), addr) };
            if (data.Length < 7 || data.Length > MaxPacket || data[0] != 'R' || data[1] != 'D' || data[2] != 'C' || data[3] != 'O')
                return None;
            var type = (PacketType)data[4];
            byte to = data[6];

            if (!_players.TryGetValue(addr, out var player))
            {
                try
                {
                    if (type == PacketType.Hello) return OnHello(data, addr, now);
                    if (type == PacketType.Auth) return OnAuth(data, addr, now);
                }
                catch (Exception e) when (e is IndexOutOfRangeException || e is ArgumentException) { }
                return None;   // strangers get nothing else
            }

            player.LastHeard = now;
            if (type == PacketType.Hello) return new List<(byte[], IPEndPoint)> { (Welcome(player), addr) };   // our Welcome was lost
            if (type == PacketType.Bye) return Drop(player, "left");
            switch (type)
            {
                case PacketType.State: case PacketType.World: case PacketType.Party: case PacketType.Appearance:
                case PacketType.Objectives: case PacketType.EntityLook: case PacketType.Hit: case PacketType.Action:
                case PacketType.Notice:
                    break;
                default:
                    return None;
            }
            var output = (byte[])data.Clone();
            output[5] = player.Id;   // the server stamps the sender
            if (type != PacketType.Hit && type != PacketType.Action && !(type == PacketType.Notice && to != Broadcast))
                return ToAll(output, addr);
            var target = _players.Values.FirstOrDefault(p => p.Id == to && p != player);
            return target == null ? None : new List<(byte[], IPEndPoint)> { (output, target.Addr) };
        }

        List<(byte[], IPEndPoint)> OnHello(byte[] data, IPEndPoint addr, double now)
        {
            // Spoofed-address floods: per-address limit, and a cap on half-open logins.
            if (RateLimited(addr.Address, now) || (!_pending.ContainsKey(addr) && _pending.Count >= MaxPending)) return None;
            int version = BitConverter.ToInt32(data, 7);
            byte role = data[11];
            int n = data[12];
            if (13 + n > data.Length) return None;
            string name = new string(Encoding.UTF8.GetString(data, 13, n).Where(c => !char.IsControl(c)).ToArray());
            if (name.Length > 32) name = name.Substring(0, 32);
            string build = "";   // the mod version and build, from 0.9.0-beta.7 on; the join log shows who runs what
            int b = 13 + n;
            if (b < data.Length && b + 1 + data[b] <= data.Length)
                build = new string(Encoding.UTF8.GetString(data, b + 1, data[b]).Where(c => !char.IsControl(c)).Take(32).ToArray());
            if (version != Protocol.Version) return Reject(addr, $"version mismatch: server {Protocol.Version}, you {version}");
            // A repeated Hello (its Challenge lost or late) gets the same challenge, so whichever Auth arrives is right.
            byte[] nonce;
            if (_pending.TryGetValue(addr, out var had) && now <= had.expires) nonce = had.nonce;
            else { nonce = new byte[16]; _rng.GetBytes(nonce); }
            _pending[addr] = (nonce, role == 1, name.Length > 0 ? name : "player", build, now + ChallengeTtlS);
            return new List<(byte[], IPEndPoint)> { (Cat(Header(PacketType.Challenge, 0, 0), nonce), addr) };
        }

        List<(byte[], IPEndPoint)> OnAuth(byte[] data, IPEndPoint addr, double now)
        {
            if (!_pending.TryGetValue(addr, out var pend)) return None;
            _pending.Remove(addr);
            if (now > pend.expires || data.Length != 7 + 32) return None;
            byte[] want;
            using (var h = new HMACSHA256(_key)) want = h.ComputeHash(pend.nonce);
            int diff = 0;   // constant time
            for (int i = 0; i < 32; i++) diff |= want[i] ^ data[7 + i];
            if (diff != 0)
            {
                _log("wrong password from " + addr.Address);
                return Reject(addr, "wrong password");
            }
            if (_players.Count >= MaxPlayers) return Reject(addr, "server full (4 players)");
            // The player set up as the host (PreferHost=Yes) gets the role back when it rejoins soon after
            // being dropped (a dead link, a suspended machine); the stand-in host hands it over. Not on a
            // first join: a PreferHost player joining someone else's room stays a guest.
            var host = HostPlayer();
            bool takeHost = host == null || (pend.preferHost && !host.PreferHost && addr.Address.Equals(_lostHost.ip) && now <= _lostHost.until);
            if (takeHost && host != null) { host.Role = Guest; _log($"{host.Name} (id {host.Id}) hands the host role back to {pend.name}"); }
            var p = new Player { Id = FreeId(), Addr = addr, Name = pend.name, Role = takeHost ? Host : Guest,
                                 PreferHost = pend.preferHost, Build = pend.build, LastHeard = now };
            _players[addr] = p;
            _log($"{p.Name} joined as {(p.Role == Host ? "host" : "guest")} (id {p.Id}) from {addr.Address}" + (p.Build.Length > 0 ? $", mod {p.Build}" : ", mod older than 0.9.0-beta.7"));
            var output = new List<(byte[], IPEndPoint)> { (Welcome(p), addr) };
            output.AddRange(ToAll(RosterPacket()));
            return output;
        }

        byte[] QueryAnswer() =>
            Cat(new[] { (byte)'R', (byte)'D', (byte)'C', (byte)'A' }, BitConverter.GetBytes(Protocol.Version),
                new[] { (byte)_players.Count, (byte)MaxPlayers }, ShortString(_roomName));

        byte[] Welcome(Player p)
        {
            var h = HostPlayer();
            return Cat(Header(PacketType.Welcome, 0, p.Id), BitConverter.GetBytes(Protocol.Version), new[] { p.Id, h?.Id ?? (byte)0 });
        }

        List<(byte[], IPEndPoint)> Drop(Player player, string why)
        {
            _players.Remove(player.Addr);
            _log($"{player.Name} (id {player.Id}) {why}");
            if (player.Role == Host && player.PreferHost) _lostHost = (player.Addr.Address, _clock() + LostHostS);
            if (player.Role == Host && _players.Count > 0)
            {
                // Hand the world to whoever prefers hosting, else the longest-joined guest.
                var next = _players.Values.OrderBy(p => p.PreferHost ? 0 : 1).ThenBy(p => p.Id).First();
                next.Role = Host;
                _log($"{next.Name} (id {next.Id}) is now the host");
            }
            return ToAll(RosterPacket());
        }

        public List<(byte[] data, IPEndPoint to)> Expire()
        {
            double now = _clock();
            if (now - _lastSweep < 1.0) return None;   // a flood mustn't make every packet scan everything
            _lastSweep = now;
            foreach (var ip in _helloTimes.Where(kv => kv.Value.Count == 0 || now - kv.Value[kv.Value.Count - 1] >= 10).Select(kv => kv.Key).ToList())
                _helloTimes.Remove(ip);
            var output = new List<(byte[], IPEndPoint)>();
            foreach (var p in _players.Values.Where(p => now - p.LastHeard > TimeoutS).ToList())
                output.AddRange(Drop(p, "timed out"));
            foreach (var a in _pending.Where(kv => now > kv.Value.expires).Select(kv => kv.Key).ToList())
                _pending.Remove(a);
            return output;
        }
    }

    public sealed class RelayHost : IDisposable
    {
        readonly UdpClient _udp;
        readonly Relay _relay;
        readonly Thread _thread;
        volatile bool _running = true;

        public int Port { get; }
        public int PlayerCount => _relay.PlayerCount;

        // Throws SocketException when the port is taken.
        public RelayHost(int port, string password, Action<string> log, string roomName = null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _relay = new Relay(password, () => clock.Elapsed.TotalSeconds, log, roomName);
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port)) { Client = { ReceiveTimeout = 1000 } };
            ServerLink.IgnoreConnectionResets(_udp);
            Port = ((IPEndPoint)_udp.Client.LocalEndPoint).Port;
            _thread = new Thread(Loop) { IsBackground = true, Name = "RDR2Coop.Server" };
            _thread.Start();
        }

        void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                var output = new List<(byte[] data, IPEndPoint to)>();
                try
                {
                    var from = any;
                    var data = _udp.Receive(ref from);
                    lock (_relay) output.AddRange(_relay.Handle(data, from));
                }
                catch (SocketException) { }      // timeout, or a reset we couldn't switch off
                catch (ObjectDisposedException) { return; }
                lock (_relay) output.AddRange(_relay.Expire());
                foreach (var (data, to) in output)
                {
                    try { _udp.Send(data, data.Length, to); }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { return; }
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _udp.Close();
        }
    }
}
