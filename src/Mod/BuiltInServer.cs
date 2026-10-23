// RunServer=Yes: this game runs the co-op server (Net/Relay.cs) and, with
// UPnP=Yes, asks the router to open its port so friends can join over the
// internet. Router talk happens on a background thread; the game picks up
// what to tell the player with TakeNote() from its tick.
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class BuiltInServer : IDisposable
    {
        const int RenewMs = 30 * 60 * 1000;

        readonly RelayHost _host;
        readonly int _port;
        readonly ManualResetEvent _stop = new ManualResetEvent(false);
        volatile Upnp _upnp;
        volatile string _note;

        public string Status { get; private set; }

        BuiltInServer(RelayHost host, int port, bool upnp)
        {
            _host = host;
            _port = port;
            Status = $"server on UDP {port}";
            if (upnp) new Thread(OpenPort) { IsBackground = true, Name = "RDR2Coop.UPnP" }.Start();
            else Note($"Co-op server running (UDP {port}). UPnP is off: forward UDP {port} to this PC for friends over the internet.");
        }

        // null (and a note saying why) when it can't run.
        public static BuiltInServer Start(int port, string password, bool upnp, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(password)) { why = "Co-op: set a Password in RDR2Coop.ini to run the server"; return null; }
            try { return new BuiltInServer(new RelayHost(port, password, l => CoopScript.Log("server: " + l)), port, upnp); }
            catch (SocketException e)
            {
                why = $"Co-op: can't run the server, UDP {port} is in use ({e.SocketErrorCode}); joining whatever runs there";
                return null;
            }
        }

        public string TakeNote() => Interlocked.Exchange(ref _note, null);

        void Note(string text)
        {
            CoopScript.Log(text);
            _note = text;
        }

        void OpenPort()
        {
            try
            {
                var u = Upnp.Find();
                if (u == null)
                {
                    Status += ", no UPnP router";
                    Note($"Co-op server running. No UPnP router answered: forward UDP {_port} to this PC for friends over the internet (same network: they use this PC's address).");
                    return;
                }
                string err = u.Open(_port);
                if (err != null)
                {
                    Status += ", UPnP refused";
                    Note($"Co-op server running. The router refused to open UDP {_port} ({err}): forward it to {u.LocalIp} by hand.");
                    return;
                }
                _upnp = u;
                if (Shared(u.PublicIp))
                {
                    Status += $", port open but {u.PublicIp} is shared";
                    Note($"Co-op server running, but your internet address ({u.PublicIp}) is shared by your provider (CGNAT), so friends can't reach it. Use Tailscale or ZeroTier, or a server elsewhere.");
                }
                else
                {
                    Status += $", friends join {u.PublicIp}:{_port}";
                    Note($"Co-op server open to the internet: friends join {u.PublicIp} port {_port}");
                }
                while (!_stop.WaitOne(RenewMs)) u.Open(_port);   // keep the lease alive while we play
            }
            catch (Exception e) { CoopScript.Log("UPnP: " + e.Message); }
        }

        static bool Shared(string ip)
        {
            if (!IPAddress.TryParse(ip ?? "", out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] < 128);
        }

        public void Dispose()
        {
            _stop.Set();
            _host.Dispose();
            try { _upnp?.Close(_port); } catch (Exception) { }
        }
    }
}
