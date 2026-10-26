// Opens the built-in server's UDP port on the home router with UPnP (IGD), so
// friends can join without port forwarding by hand. Blocking: call it from a
// background thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;

namespace RDR2Coop.Net
{
    public sealed class Upnp
    {
        const string Description = "RDR2 Co-op";
        const int LeaseS = 7200;   // renewed while the game runs; expires on its own after a crash

        readonly string _control, _service;
        public string LocalIp { get; }
        public string PublicIp { get; private set; }

        Upnp(string control, string service, string localIp) { _control = control; _service = service; LocalIp = localIp; }

        // The router's port-mapping service, or null (no UPnP router answered).
        public static Upnp Find(int waitMs = 3000)
        {
            foreach (var location in Discover(waitMs))
            {
                try
                {
                    var (control, service) = ControlUrl(location);
                    if (control == null) continue;
                    LastSearch += "; using the one at " + new Uri(control).Host;
                    using (var probe = new UdpClient())
                    {
                        probe.Connect(new Uri(control).Host, 1900);   // no packet sent: just picks our LAN address
                        return new Upnp(control, service, ((IPEndPoint)probe.Client.LocalEndPoint).Address.ToString());
                    }
                }
                catch (Exception) { }
            }
            return null;
        }

        // Maps UDP port -> this PC. Returns null when done, else the router's error.
        public string Open(int port)
        {
            string err = Call("AddPortMapping", Args(port, LeaseS));
            if (err != null && err.Contains("725")) err = Call("AddPortMapping", Args(port, 0));   // OnlyPermanentLeasesSupported
            if (err == null)
            {
                var ip = Call("GetExternalIPAddress", "", out string reply);
                PublicIp = ip == null ? Tag(reply, "NewExternalIPAddress") : null;
            }
            return err;
        }

        public void Close(int port) =>
            Call("DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>", out _, 1500);

        string Args(int port, int lease) =>
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
            $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{LocalIp}</NewInternalClient><NewEnabled>1</NewEnabled>" +
            $"<NewPortMappingDescription>{Description}</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>";

        string Call(string action, string args) => Call(action, args, out _);

        string Call(string action, string args, out string reply, int timeoutMs = 5000)
        {
            reply = null;
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                          "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                          $"<u:{action} xmlns:u=\"{_service}\">{args}</u:{action}></s:Body></s:Envelope>";
            var req = (HttpWebRequest)WebRequest.Create(_control);
            req.Method = "POST";
            req.Timeout = req.ReadWriteTimeout = timeoutMs;
            req.ContentType = "text/xml; charset=\"utf-8\"";
            req.Headers.Add("SOAPAction", $"\"{_service}#{action}\"");
            var bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream())) reply = r.ReadToEnd();
                return null;
            }
            catch (WebException e) when (e.Response != null)
            {
                using (var r = new StreamReader(e.Response.GetResponseStream())) reply = r.ReadToEnd();
                return "UPnP error " + (Tag(reply, "errorCode") ?? "?") + " " + (Tag(reply, "errorDescription") ?? "");
            }
            catch (Exception e) { return e.Message; }
        }

        static string Tag(string xml, string name)
        {
            if (xml == null) return null;
            int a = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
            if (a < 0) return null;
            a += name.Length + 2;
            int b = xml.IndexOf("</" + name + ">", a, StringComparison.Ordinal);
            return b < 0 ? null : xml.Substring(a, b - a).Trim();
        }

        static readonly string[] Targets = {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1", "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1", "upnp:rootdevice" };

        // What the last search found, for the log: "no UPnP router" alone doesn't say why.
        public static string LastSearch { get; private set; } = "";

        // SSDP: ask for internet gateways on every network adapter (with a VPN or a virtual adapter the
        // question could leave by the wrong one) and at each adapter's router directly (some routers
        // only answer that); asked twice, as UDP gets lost. Their description URLs, gateways first.
        static List<string> Discover(int waitMs)
        {
            var gateways = new List<string>();
            var others = new List<string>();
            var askers = new List<(UdpClient udp, IPAddress router)>();
            int answers = 0;
            try
            {
                foreach (var (local, router) in Adapters())
                {
                    try
                    {
                        var udp = new UdpClient(new IPEndPoint(local, 0));
                        udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                        ServerLink.IgnoreConnectionResets(udp);
                        askers.Add((udp, router));
                    }
                    catch (SocketException) { }
                }
                if (askers.Count == 0)
                {
                    var udp = new UdpClient(0);
                    ServerLink.IgnoreConnectionResets(udp);
                    askers.Add((udp, null));
                }
                var start = DateTime.UtcNow;
                bool askedAgain = false;
                Ask(askers);
                while ((DateTime.UtcNow - start).TotalMilliseconds < waitMs)
                {
                    if (!askedAgain && gateways.Count == 0 && (DateTime.UtcNow - start).TotalMilliseconds > 500) { Ask(askers); askedAgain = true; }
                    bool any = false;
                    foreach (var (udp, _) in askers)
                    {
                        while (udp.Available > 0)
                        {
                            any = true;
                            answers++;
                            var from = new IPEndPoint(IPAddress.Any, 0);
                            var (location, gateway) = ParseReply(Encoding.ASCII.GetString(udp.Receive(ref from)));
                            if (location == null || gateways.Contains(location) || others.Contains(location)) continue;
                            (gateway ? gateways : others).Add(location);
                        }
                    }
                    if (!any) Thread.Sleep(20);
                }
                var routers = askers.Where(a => a.router != null).Select(a => a.router.ToString()).Distinct().ToList();
                LastSearch = $"UPnP: asked on {askers.Count} network adapter(s)" + (routers.Count > 0 ? " and at router " + string.Join(", ", routers) : ", no router address known") +
                             $": {answers} answer(s), {gateways.Count} from internet gateways";
            }
            catch (Exception e) { LastSearch = "UPnP: the search failed: " + e.Message; }
            finally { foreach (var (udp, _) in askers) udp.Close(); }
            return gateways.Concat(others).ToList();
        }

        static void Ask(List<(UdpClient udp, IPAddress router)> askers)
        {
            var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            foreach (var st in Targets)
            {
                var q = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n");
                foreach (var (udp, router) in askers)
                {
                    try { udp.Send(q, q.Length, group); } catch (SocketException) { }
                    if (router != null) try { udp.Send(q, q.Length, new IPEndPoint(router, 1900)); } catch (SocketException) { }
                }
            }
        }

        // Each working adapter's IPv4 address and its router (null when it has none).
        static List<(IPAddress local, IPAddress router)> Adapters()
        {
            var list = new List<(IPAddress, IPAddress)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var ip = ni.GetIPProperties();
                    var router = ip.GatewayAddresses.Select(g => g.Address)
                                   .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                    foreach (var a in ip.UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add((a.Address, router));
                }
            }
            catch (Exception) { }   // no adapter list (seen under some Wine builds): one socket on all adapters instead
            return list;
        }

        // One SSDP answer: its description URL (http only), and whether it says it's an internet gateway.
        internal static (string location, bool gateway) ParseReply(string text)
        {
            string location = null;
            bool gateway = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) location = line.Substring(9).Trim();
                else if ((line.StartsWith("ST:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("USN:", StringComparison.OrdinalIgnoreCase) ||
                          line.StartsWith("NT:", StringComparison.OrdinalIgnoreCase)) &&
                         (line.IndexOf("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          line.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          line.IndexOf("WANPPPConnection", StringComparison.OrdinalIgnoreCase) >= 0)) gateway = true;
            }
            bool http = location != null && Uri.TryCreate(location, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
            return (http ? location : null, gateway);
        }

        // The WANIPConnection (or WANPPPConnection) control URL in a gateway's description.
        static (string control, string service) ControlUrl(string location)
        {
            var doc = new XmlDocument { XmlResolver = null };
            var req = (HttpWebRequest)WebRequest.Create(location);
            req.Timeout = req.ReadWriteTimeout = 3000;
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
                doc.Load(XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }));
            string baseUrl = doc.GetElementsByTagName("URLBase").Cast<XmlNode>().FirstOrDefault()?.InnerText.Trim();
            var root = new Uri(string.IsNullOrEmpty(baseUrl) ? location : baseUrl);
            // WANIPConnection first: routers often list an unused WANPPPConnection too.
            var services = doc.GetElementsByTagName("service").Cast<XmlNode>()
                .Select(n => (type: n["serviceType"]?.InnerText.Trim() ?? "", control: n["controlURL"]?.InnerText.Trim()))
                .Where(x => x.control != null && (x.type.Contains(":WANIPConnection:") || x.type.Contains(":WANPPPConnection:")))
                .OrderBy(x => x.type.Contains(":WANIPConnection:") ? 0 : 1).ToList();
            return services.Count == 0 ? (null, null) : (new Uri(root, services[0].control).ToString(), services[0].type);
        }
    }
}
