// Opens the built-in server's UDP port on the home router with UPnP (IGD), so
// friends can join without port forwarding by hand. Blocking: call it from a
// background thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        public static Upnp Find(int waitMs = 2500)
        {
            foreach (var location in Discover(waitMs))
            {
                try
                {
                    var (control, service) = ControlUrl(location);
                    if (control == null) continue;
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

        // SSDP: ask the LAN for internet gateways; their description URLs.
        static List<string> Discover(int waitMs)
        {
            var found = new List<string>();
            using (var udp = new UdpClient(0))
            {
                ServerLink.IgnoreConnectionResets(udp);
                var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                foreach (var st in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
                                           "urn:schemas-upnp-org:service:WANIPConnection:1", "urn:schemas-upnp-org:service:WANPPPConnection:1" })
                {
                    var q = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n");
                    try { udp.Send(q, q.Length, group); } catch (SocketException) { }
                }
                var until = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < until)
                {
                    udp.Client.ReceiveTimeout = Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds);
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        string text = Encoding.ASCII.GetString(udp.Receive(ref from));
                        var loc = text.Split('\n').Select(l => l.Trim())
                                      .FirstOrDefault(l => l.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase));
                        if (loc != null && !found.Contains(loc.Substring(9).Trim())) found.Add(loc.Substring(9).Trim());
                    }
                    catch (SocketException) { break; }   // timed out
                }
            }
            return found;
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
