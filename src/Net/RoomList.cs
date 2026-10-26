// The server browser's two sources: co-op servers on the home network (a
// broadcast query, answered by Relay and relay.py) and the online room list
// (list-server/, hosts with Public=Yes announce themselves there).
// Everything here blocks: call it from a background thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RDR2Coop.Net
{
    [DataContract]
    public sealed class Room
    {
        [DataMember(Name = "name")] public string Name = "";
        [DataMember(Name = "address")] public string Address = "";
        [DataMember(Name = "port")] public int Port;
        [DataMember(Name = "players")] public int Players;
        [DataMember(Name = "max")] public int Max;
        [DataMember(Name = "version")] public int Version;
        public bool Lan;
        public bool HostHere;   // the browser's "host your own game" row, not a server
        public bool Party;      // the browser's mission party row (host only)
        public bool Update;     // the browser's "Update to x" row
        public int Extra = -1;  // a row a feature added (its index in ServerBrowser.Extras), else -1

        // The online list is read with DataContractJsonSerializer, which skips field initialisers:
        // without this every online room had Extra 0 and Enter on it ran the first feature row
        // (the scoreboard) instead of asking for the password (0.8.0-beta.2.5 to 2.7).
        [OnDeserializing] void BeforeRead(StreamingContext context) { Extra = -1; }
    }

    public static class RoomList
    {
        [DataContract] sealed class Rooms { [DataMember(Name = "rooms")] public List<Room> List = new List<Room>(); }
        [DataContract] sealed class Listed { [DataMember(Name = "address")] public string Address = null; }

        [DataContract]
        sealed class Announcement
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "port")] public int Port;
            [DataMember(Name = "players")] public int Players;
            [DataMember(Name = "max")] public int Max;
            [DataMember(Name = "version")] public int Version;
            [DataMember(Name = "ipv4", EmitDefaultValue = false)] public string Ipv4;
        }

        static RoomList()
        {
            // The game's runtime may default to old TLS versions, which the list's host refuses.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        // Servers on this network: broadcast on the co-op port, collect answers for waitMs.
        // The browser's search: rooms whose name contains the text, any case. The menu rows always show.
        public static List<Room> Filter(List<Room> rooms, string text)
        {
            if (string.IsNullOrEmpty(text)) return rooms;
            return rooms.Where(r => r.HostHere || r.Party || r.Update || r.Extra >= 0 || (r.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        // What was typed into the search: no game formatting codes or control characters, trimmed, capped.
        public static string CleanSearch(string text, int max)
        {
            var t = new string((text ?? "").Where(c => c != '~' && !char.IsControl(c)).ToArray()).Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }

        public static List<Room> QueryLan(int port, int waitMs = 800)
        {
            var found = new List<Room>();
            using (var udp = new UdpClient(0) { EnableBroadcast = true })
            {
                ServerLink.IgnoreConnectionResets(udp);
                var query = Relay.QueryMagic.Concat(new byte[Relay.QuerySize - Relay.QueryMagic.Length]).ToArray();
                try { udp.Send(query, query.Length, new IPEndPoint(IPAddress.Broadcast, port)); } catch (SocketException) { }
                try { udp.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, port)); } catch (SocketException) { }   // a server on this PC
                var until = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < until)
                {
                    udp.Client.ReceiveTimeout = Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds);
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var a = udp.Receive(ref from);
                        if (a.Length < 11 || a[0] != 'R' || a[1] != 'D' || a[2] != 'C' || a[3] != 'A' || 11 + a[10] > a.Length) continue;
                        var room = new Room { Address = from.Address.ToString(), Port = from.Port, Version = BitConverter.ToInt32(a, 4),
                                              Players = a[8], Max = a[9], Name = Encoding.UTF8.GetString(a, 11, a[10]), Lan = true };
                        if (!found.Any(r => r.Port == room.Port && r.Name == room.Name && r.Players == room.Players))   // the same server via loopback and the LAN
                            found.Add(room);
                    }
                    catch (SocketException) { break; }
                }
            }
            return found;
        }

        /// <summary>The list server's JSON as rooms (its own fields only; the browser's row flags stay at their defaults).</summary>
        public static List<Room> ReadRooms(Stream s) =>
            ((Rooms)new DataContractJsonSerializer(typeof(Rooms)).ReadObject(s))?.List ?? new List<Room>();

        /// <summary>The online list's address from the ListServer= setting: "" = off, a web address as given, anything
        /// else (an IP address, "Yes": a player's guess) the default, with why in note.</summary>
        public static string ListServerFrom(string setting, string fallback, out string note)
        {
            note = null;
            string s = (setting ?? "").Trim();
            if (s.Length == 0) return fallback;
            if (s.Equals("off", StringComparison.OrdinalIgnoreCase)) return "";
            if (Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)) return s;
            note = $"ListServer={s} in RDR2Coop.ini isn't a web address (it's the online room list, not your server): using the mod's own list";
            return fallback;
        }

        // The online list; throws on network errors (the browser says so).
        public static List<Room> FetchOnline(string listServer)
        {
            var req = (HttpWebRequest)WebRequest.Create(listServer.TrimEnd('/') + "/v1/rooms");
            req.Timeout = req.ReadWriteTimeout = 5000;
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
                return ReadRooms(s);
        }

        // List (or refresh) a room; null when done (listedAs: the address the
        // list shows it under), else why not.
        public static string Announce(string listServer, string name, int port, int players, int max, string ipv4, out string listedAs)
        {
            listedAs = null;
            var body = new Announcement { Name = name, Port = port, Players = players, Max = max, Version = Protocol.Version, Ipv4 = ipv4 };
            string err = Post(listServer, "/v1/announce", body, out string reply);
            if (err == null)
                try { listedAs = ((Listed)new DataContractJsonSerializer(typeof(Listed)).ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(reply ?? "{}")))).Address; }
                catch (SerializationException) { }
            return err;
        }

        [DataContract] sealed class ErrorReply { [DataMember(Name = "error")] public string Error = null; }   // set by the deserializer

        /// <summary>The list server's refusal as its sentence ({"error": "..."}), or the reply as it came.</summary>
        public static string ErrorText(string reply)
        {
            try { return ((ErrorReply)new DataContractJsonSerializer(typeof(ErrorReply)).ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(reply ?? "")))).Error ?? reply; }
            catch (SerializationException) { return reply; }
        }

        public static void Remove(string listServer, int port) => Post(listServer, "/v1/remove", new Announcement { Port = port, Name = "" }, out _, 1500);

        static string Post(string listServer, string path, Announcement body, out string reply, int timeoutMs = 5000)
        {
            reply = null;
            var req = (HttpWebRequest)WebRequest.Create(listServer.TrimEnd('/') + path);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = req.ReadWriteTimeout = timeoutMs;
            try
            {
                using (var s = req.GetRequestStream()) new DataContractJsonSerializer(typeof(Announcement)).WriteObject(s, body);
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream())) reply = r.ReadToEnd();
                return null;
            }
            catch (WebException e) when (e.Response != null)
            {
                using (var r = new StreamReader(e.Response.GetResponseStream())) return ErrorText(r.ReadToEnd());
            }
            catch (Exception e) { return e.Message; }
        }
    }
}
