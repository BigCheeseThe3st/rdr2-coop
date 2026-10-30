// Story-mode co-op for up to 4 players through a relay server (server/relay.py,
// or the one built into this mod: BuiltInServer).
// Every game runs the area around its own player; when players are close, one
// game runs the shared area (the host's if the host is there, so its story and
// enRage world are the one you share) and the others show copies of its NPCs.
// Each player sees the others as puppets; hits go to the game that owns the NPC
// or, for NPC hits on a player, to that player.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;
using LinkState = RDR2Coop.Net.LinkState;
using Appearance = RDR2Coop.Net.Appearance;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    public class CoopScript : Script
    {
        sealed class Remote
        {
            public Interpolator Interp;
            public Puppet Puppet;
            public int FarBlip;            // their marker while too far for a puppet
            public long FarBlipMs;
            public PlayerState Last;
            public bool HasLast;
            public long HeardMs;           // their last position's arrival: silent too long = away (paused)
            public long LastDriveMs;
            public Appearance Look;
            public long LookAppliedMs = long.MinValue / 2;   // "long ago": now - long.MinValue overflows to a negative and the clothes never went on
            public bool HasLook;
            public uint AppliedLook;
            public uint AppliedWeapon;     // the weapon their puppet holds
            public long WeaponTryMs, WeaponWaitSince = -1;
            public uint LoggedWeapon;      // diagnostics (0.9.0): said once per weapon and once for aiming
            public bool LoggedAim;
            public Appearance MountLook;   // their own horse's look (saddle, coat...)
            public bool HasMountLook;
            public uint AppliedMountLook;
            public int AppliedMountHorse;
        }

        const int StaleWorldMs = 3000;    // bubbles: a linked game's world older than this is dropped
        const float PuppetRange = 400f;   // further than this: no puppet (the area isn't loaded)
        const ushort MountLookId = 0;     // EntityLook id meaning "the sender's own horse" (NPC ids start at 1)
        // The online room list (list-server/). ListServer= in the ini overrides; "off" turns it off.
        const string DefaultListServer = "https://rdr2coop-rooms.beese.workers.dev";

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Dictionary<byte, Remote> _remotes = new Dictionary<byte, Remote>();
        ServerLink _link;
        BuiltInServer _builtIn;
        ServerBrowser _browser;
        Updater _updater;
        Keys _browserKey;          // None: the status key pressed twice opens the browser
        int _lastStatusKey;
        string _iniPath, _name = "", _password = "";
        LogSender _logs;   // beta builds: opt-in log uploads (Net/LogShare.cs)
        bool _preferHost;
        // Our own server's settings, kept for "Host your own game here" after joining someone else's.
        int _hostPort;
        string _hostPassword = "", _roomName = "", _listServer = "";
        bool _upnpOn, _public;
        OwnedWorld _owned;
        MirrorWorld _mirror;
        // Bubbles ([Gameplay] Bubbles, src/Net/Bubbles.cs): one mirror per linked player, our own world always on.
        readonly Dictionary<byte, MirrorWorld> _bubbleMirrors = new Dictionary<byte, MirrorWorld>();
        readonly HashSet<byte> _linked = new HashSet<byte>();
        bool _bubbleMode;
        readonly DoublesSync _doubles = new DoublesSync();
        readonly BubbleCrowd _crowd = new BubbleCrowd();
        byte _owner;                 // whose game runs our area (our own id when ours)
        string _status = "";
        uint _appearAs;
        Keys _teleportKey, _statusKey, _spawnWagonKey, _spawnTrainKey, _partyKey, _clockKey, _pokerFoldKey, _pokerDeckKey, _pokerAwayKey;
        int _clockHour = 13;
        readonly MissionParty _party = new MissionParty();
        readonly DownState _down = new DownState();
        readonly RideDouble _ride = new RideDouble();   // sitting behind a partner on their horse
        readonly TableGames _tables = new TableGames();   // poker, blackjack...: co-op stands back while we play
        Keys _reviveKey, _travelKey, _nemesisKey;
        readonly CutsceneTest _cutsceneTest = new CutsceneTest();
        readonly SoftsaveProbe _softsave = new SoftsaveProbe();
        readonly Cutscenes _cutscenes = new Cutscenes();
        readonly MissionRoles _missionRoles = new MissionRoles();
        HostWorld _hostWorld;
        bool _travelPending;
        readonly MissionProbe _missionProbe = new MissionProbe();
        MissionRecorder _missionRec;   // beta logs on: the memory of game scripts that start while we play, for mapping them (Net/MissionTape.cs)
        readonly MissionBlock _missionBlock = new MissionBlock();
        readonly MissionAssist _assist = new MissionAssist();
        readonly Chat _chat = new Chat();
        readonly EnrageLink _enrage = new EnrageLink();   // run from the tick: waiting inside a key handler isn't safe
        readonly EnvSync _env = new EnvSync();
        readonly PartyList _partyList = new PartyList();
        readonly Scoreboard _scoreboard = new Scoreboard();   // the session's kills, downs, revives, winnings, contest wins
        readonly Contests _contests = new Contests();   // races, duels, shooting, hunting: the mod's contests
        readonly Spectator _spectate = new Spectator();   // watching a partner while we're down or dead
        readonly Pings _pings = new Pings(); readonly MoneyGifts _gifts = new MoneyGifts();   // "look here" pings, giving a partner money
        readonly EmoteMenu _emotes = new EmoteMenu();   // wave, point, laugh...: partners see them
        readonly Tables _modTables = new Tables();   // the mod's own card and dice tables (poker, blackjack...)
        readonly GamePoker _gamePoker = new GamePoker();   // guests in card players' seats at the game's own poker
        readonly Waypoints _waypoints = new Waypoints();
        readonly Explosions _explosions = new Explosions();
        readonly LawSync _law = new LawSync();
        readonly DoorSync _doors = new DoorSync();
        long _lastMountCheck, _lastMountSent;
        uint _mountSig;
        Vector3 _travelTo;
        string _travelName = "";
        long _lastRevivePrompt;
        bool _enrageNoticeShown;
        long _lastLookCheck;
        readonly LookSender _lookSender = new LookSender();
        int _sendEveryMs = 33, _smoothingMs = 70;
        uint _seq;
        long _lastSendMs, _lastTickMs;

        public CoopScript()
        {
            Interval = 0;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;

            Log($"RDR2 Co-op {BuildInfo.Version} (protocol {Protocol.Version}), ScriptHookRDR2DotNet {typeof(Script).Assembly.GetName().Version}, {Environment.OSVersion}");
            try { Start(); }
            catch (Exception e) { Log("start failed: " + e); _status = "Co-op couldn't start: " + e.Message + " (details in scripts\\RDR2Coop.log)"; }
        }

        void Start()
        {
            // The game runs .NET with SSL 3 and TLS 1.0 only (seen under Proton; Windows without strong
            // crypto set does the same): GitHub, where updates come from, wants TLS 1.2.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            // BaseDirectory is the folder the mod's DLL is in (scripts\); the settings may sit beside it or one level down.
            string iniPath = Path.Combine(BaseDirectory, "scripts", "RDR2Coop.ini");
            if (!File.Exists(iniPath)) iniPath = Path.Combine(BaseDirectory, "RDR2Coop.ini");
            if (!File.Exists(iniPath)) iniPath = Path.Combine(BaseDirectory, "..", "scripts", "RDR2Coop.ini");   // our .NET 9 loader: the DLL is in RDR2Coop\, the settings stay in scripts\
            if (!File.Exists(iniPath)) Log("settings file not found next to the game or in scripts\\; using defaults");
            var ini = Ini.Load(iniPath);
            _iniPath = iniPath;
            string name = ini.Get("Coop", "Name", Environment.UserName);
            string server = ini.Get("Coop", "Server", "127.0.0.1");
            int port = ini.Get("Coop", "Port", ServerLink.DefaultPort);
            string password = ini.Get("Coop", "Password", "");
            bool preferHost = ini.Get("Coop", "PreferHost", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);
            string appearAs = ini.Get("Coop", "AppearAs", "");
            string listServer = RoomList.ListServerFrom(ini.Get("Coop", "ListServer", ""), DefaultListServer, out string listNote);
            if (listNote != null) Log(listNote);
            _listServer = listServer;
            _public = ini.Get("Coop", "Public", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);
            _upnpOn = !ini.Get("Coop", "UPnP", "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
            _roomName = ini.Get("Coop", "RoomName", name + "'s game");
            bool runServer = ini.Get("Coop", "RunServer", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);
            _hostPort = runServer ? port : ini.Get("Coop", "HostPort", ServerLink.DefaultPort);
            _hostPassword = runServer ? password : ini.Get("Coop", "HostPassword", "");
            if (runServer)
            {
                _builtIn = StartOwnServer(password);
                server = "127.0.0.1";   // our own server (or whatever already runs on that port here)
            }
            _appearAs = string.IsNullOrWhiteSpace(appearAs) ? 0 : MISC.GET_HASH_KEY(appearAs.Trim());
            _teleportKey = ParseKey(ini.Get("Keys", "TeleportToPartner", "F9"), Keys.F9);
            _statusKey = ParseKey(ini.Get("Keys", "ShowStatus", "F10"), Keys.F10);
            _spawnWagonKey = ParseKey(ini.Get("Keys", "SpawnWagon", ""), Keys.None);   // testing aids: off unless set in the ini
            _spawnTrainKey = ParseKey(ini.Get("Keys", "SpawnTrain", ""), Keys.None);
            _clockKey = ParseKey(ini.Get("Keys", "SetClock", ""), Keys.None);
            _pokerFoldKey = ParseKey(ini.Get("Keys", "PokerForceFold", ""), Keys.None);
            _pokerDeckKey = ParseKey(ini.Get("Keys", "PokerDeckTest", ""), Keys.None);
            _pokerAwayKey = ParseKey(ini.Get("Keys", "PokerSendAway", ""), Keys.None);
            _cutsceneTest.Key = ParseKey(ini.Get("Keys", "CutsceneTest", ""), Keys.None);   // testing: plays [Testing] CutsceneLabel on its own (Mod/CutsceneTest.cs)
            _cutsceneTest.Configure(ini);
            _softsave.Key = ParseKey(ini.Get("Keys", "SoftsaveProbe", ""), Keys.None);
            _softsave.DumpKey = ParseKey(ini.Get("Keys", "SoftsaveDump", ""), Keys.None);   // testing: this world's save globals to scripts\RDR2Coop.worlddump.bin
            _softsave.InjectKey = ParseKey(ini.Get("Keys", "SoftsaveInject", ""), Keys.None);   // testing: that dump's world into this game, our own player data kept   // testing: the game's menu-less restore, round trip (Mod/SoftsaveProbe.cs)   // testing: a random card player at the game's poker leaves after this hand   // testing: the next hand at the game's poker comes from the mod's deck   // testing: the next card player to act at the game's poker folds
            PokerWatch.On = ini.Get("Testing", "PokerTrace", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);   // records the game's own poker (scripts\RDR2Coop.poker.log)   // testing aid: the hour [Testing] ClockHour= says (camp poker: 11-16 or 21-01)
            if (!int.TryParse(ini.Get("Testing", "ClockHour", "13"), out _clockHour) || _clockHour < 0 || _clockHour > 23) _clockHour = 13;
            _partyKey = ParseKey(ini.Get("Keys", "MissionParty", ""), Keys.None);   // F8 is ScriptHookRDR2DotNet's console: the co-op menu has it
            _reviveKey = ParseKey(ini.Get("Keys", "Revive", "F7"), Keys.F7);
            _chat.Key = ParseKey(ini.Get("Keys", "Chat", "T"), Keys.T);   // T: free in story mode (Red Dead Online's chat key)
            _ride.Key = ParseKey(ini.Get("Keys", "RideDouble", ""), Keys.None);   // the game's mount key by their horse does it too
            _travelKey = ParseKey(ini.Get("Keys", "TravelTo", ""), Keys.None);
            _nemesisKey = ParseKey(ini.Get("Keys", "Nemeses", "F11"), Keys.F11);
            _browserKey = ParseKey(ini.Get("Keys", "ServerBrowser", ""), Keys.None);   // Home, F6...: taken by the game
            _partyList.Key = ParseKey(ini.Get("Keys", "PartyList", ""), Keys.None);
            _scoreboard.Key = ParseKey(ini.Get("Keys", "Scoreboard", ""), Keys.None);
            _pings.Key = ParseKey(ini.Get("Keys", "Ping", ""), Keys.None);
            _emotes.Key = ParseKey(ini.Get("Keys", "EmoteMenu", ""), Keys.None);
            _browser = new ServerBrowser(listServer, IsOwnServer, () => _builtIn == null, () => _hostPassword,
                                         () => _link != null && _link.IAmHost ? (_party.IsHosting ? "Mission party: on (switch off)" : "Mission party: off (switch on)") : null,
                                         () => _party.Toggle(_link), _browserKey != Keys.None ? _browserKey : _statusKey,
                                         () => _updater?.MenuLabel, () => _updater?.Begin());
            _spectate.Key = _teleportKey; ServerBrowser.Extras.Add(new MenuExtra(() => _spectate.MenuLabel, () => _spectate.MenuAct(), help: () => "Watch a partner through their camera while you're down or dead."));
            ServerBrowser.Extras.Add(new MenuExtra(() => _emotes.MenuLabel(_link), () => _emotes.Open(), help: () => "Wave, tip your hat, point, clap and more. Your partners see it."));
            // Not while we're in the host's story as a guest: our own story comes back through this version's leave.
            ServerBrowser.Extras.Add(new MenuExtra(() => _hostWorld?.InHostStory == true ? null : _updater?.SwitchLabel, () => { if (_hostWorld?.InHostStory != true) _updater?.Switch(); }, MenuExtra.More, () => _updater?.SwitchHelp));
            ServerBrowser.Extras.Add(new MenuExtra(() => _hostWorld?.InHostStory == true ? null : _updater?.RcLabel, () => { if (_hostWorld?.InHostStory != true) _updater?.SwitchRc(); }, MenuExtra.More, () => _updater?.RcHelp));
            ServerBrowser.Extras.Add(new MenuExtra(() => _logs?.MenuLabel, () => _logs?.Toggle(), MenuExtra.More, () => "Sends your co-op logs to the developer so bugs get found and fixed sooner. Addresses and folders are removed first. Enter switches it on or off."));
            Contests.Register("race", () => new Race(), Races.Setup, Races.View);
            Contests.Register("hunting", () => new Hunting(), Hunts.Setup, Hunts.View);
            Contests.Register("shooting", () => new Shooting(), Shootings.Setup, Shootings.View);
            Func<byte, int> standIn = id => _remotes.TryGetValue(id, out var sr) && sr.Puppet != null && sr.Puppet.Exists ? sr.Puppet.Handle : 0;   // a partner's stand-in here (0: none), for the duel to aim at
            Contests.Register("duel", () => new Duel(), done => Duels.Setup(done, _link, standIn), (c, now) => Duels.View(c, now, standIn));
            ServerBrowser.Extras.Add(new MenuExtra(() => _pings.MenuLabel(_link), () => _pings.Request(), help: () => "Marks the spot you're looking at, so your partners can see where you mean."));
            ServerBrowser.Extras.Add(new MenuExtra(() => _gifts.MenuLabel(_link), () => _gifts.MenuAct(_link), help: () => "Give some of your money to a partner, up to $10,000 at a time."));
            ServerBrowser.Extras.Add(new MenuExtra(() => _gamePoker.MenuLabel(_link), () => _gamePoker.MenuAct(_link), help: () => "Take a card player's seat at the host's poker table: the game deals you in and you play their chips."));
            if (ini.Get("Games", "HighCard", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase)) Tables.Register("highcard", () => new HighCard());   // the tables' sample game
            Tables.Register("dice", () => new LiarsDice());
            Tables.Register("dominoes", () => new Dominoes());
            Tables.Register("blackjack", () => new Blackjack());
            Tables.Register("poker", () => new Poker());
            ServerBrowser.Extras.Add(new MenuExtra(() => "Support the mod (every tip goes to wildlife conservation)",
                () => Say("Co-op is free. Tips at ko-fi.com/rdr2_coop go, every one, to the Wildlife Conservation Society. Thank you!"), MenuExtra.More,
                () => "The mod is free. Every tip at ko-fi.com/rdr2_coop goes to the Wildlife Conservation Society."));
            ServerBrowser.Status = () => _link == null ? null
                : _link.State != LinkState.Connected ? "Not connected (" + _link.State + ")"
                : string.Join(", ", _link.Roster.Select(p => p.Name + (p.Role == PlayerRole.Host ? " (host)" : "")))
                  + "    this area: " + (_owner == 0 ? "-" : _owner == _link.MyId ? "your game" : _link.NameOf(_owner) + "'s game");
            string dll = Path.Combine(BaseDirectory, "RDR2Coop.dll");   // where ScriptHookRDR2DotNet loaded us from
            if (!File.Exists(dll)) dll = Path.Combine(BaseDirectory, "scripts", "RDR2Coop.dll");
            _updater = new Updater(dll,
                                   !ini.Get("Coop", "UpdateCheck", "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase));
            var to = ini.Get("Testing", "TravelTo", "-283,790,118").Split(',');
            _travelTo = to.Length == 3 && float.TryParse(to[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tx)
                && float.TryParse(to[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ty)
                && float.TryParse(to[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tz)
                ? new Vector3(tx, ty, tz) : new Vector3(-283f, 790f, 118f);
            _travelName = ini.Get("Testing", "TravelName", "Valentine");
            Visuals.Load(ini);
            Sync.Load(ini);
            Extras.Load(ini);
            Games.Load(ini);
            TraceOn = ini.Get("Testing", "TraceJoins", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);

            int sendHz = Clamp(ini.Get("Net", "SendHz", 30), 5, 60);
            int worldHz = Clamp(ini.Get("Net", "WorldHz", 20), 2, 30);
            _smoothingMs = Clamp(ini.Get("Net", "SmoothingMs", 70), 0, 500);
            Peds.RetaskEveryMs = Clamp(ini.Get("Net", "RetaskMs", 150), 50, 1000);
            MirrorWorld.DriveMode = ini.Get("Net", "DriveMode", MirrorWorld.DriveMode);
            MirrorWorld.VehicleMode = ini.Get("Net", "VehicleMode", MirrorWorld.VehicleMode).ToLowerInvariant();
            _sendEveryMs = 1000 / sendHz;
            Log($"settings {iniPath} (exists {File.Exists(iniPath)}): name {name}, server {server}:{port}, " +
                $"prefer host {preferHost}, send {sendHz} Hz, world {worldHz} Hz, smoothing {_smoothingMs} ms, retask {Peds.RetaskEveryMs} ms");
            if (password.Length == 0) Log("no Password set in RDR2Coop.ini; the server will refuse us");

            _owned = new OwnedWorld(worldHz);
            _hostWorld = new HostWorld(id => _remotes.TryGetValue(id, out var hr) && hr.HasLast ? hr.Last : (PlayerState?)null);
            _hostWorld.AtStart(_clock.ElapsedMilliseconds);
            _mirror = new MirrorWorld(_smoothingMs);
            _name = name;
            _logs = new LogSender(iniPath, ini, name, BuildInfo.Version, _clock.ElapsedMilliseconds, () => _clock.ElapsedMilliseconds);
            _missionRec = new MissionRecorder(_logs.On && !ini.Get("Testing", "RecordMissions", "Yes").Trim().Equals("No", StringComparison.OrdinalIgnoreCase),
                                              ini.Get("Testing", "KeepRecordings", "No").Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase),
                                              ini.Get("Testing", "RecordAllScripts", "No").Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase));
            _preferHost = preferHost;
            _password = password;
            if (!runServer && !Resolves(server))
            {
                // Server= isn't an address (often the room's name, as the browser shows it): stay on, not connected,
                // so F10 works, and look for a room by that name to join.
                Log($"Server={server} in RDR2Coop.ini isn't an address this PC can find: looking for a room called that");
                _status = $"Co-op: no server '{(server.Length > 20 ? server.Substring(0, 20) : server)}'. Looking for a game called that...";
                Say(_status);
                string wanted = server;
                System.Threading.Tasks.Task.Run(() =>
                {
                    var rooms = new List<Room>();
                    try { rooms.AddRange(RoomList.QueryLan(ServerLink.DefaultPort)); } catch (Exception e) { Log("room by name, home network: " + e.Message); }
                    try { if (_listServer != null) rooms.AddRange(RoomList.FetchOnline(_listServer)); } catch (Exception e) { Log("room by name, online list: " + e.Message); }
                    _roomByName = RoomList.FindByName(rooms, wanted) ?? new Room { Name = null };
                    _roomByNameWanted = wanted;
                });
                server = "127.0.0.1";
            }
            Connect(server, port, password);
        }

        volatile Room _roomByName;   // the room Server= named (Name null: none found), set by the lookup above
        string _roomByNameWanted, _byNameNote;   // the note: shown 3 times, 6 s apart, once you move after loading (and on the status key)
        int _byNameNoteLeft;
        long _byNameNoteMs;

        static bool Resolves(string host)
        {
            if (System.Net.IPAddress.TryParse(host, out _)) return true;
            try { return System.Net.Dns.GetHostAddresses(host).Length > 0; } catch (Exception) { return false; }
        }

        // The tick side of the lookup: join the room it found, or say how to pick one.
        void JoinRoomByName()
        {
            var room = _roomByName;
            if (room == null) return;
            _roomByName = null;
            if (room.Name == null)
            {
                Log($"no room called '{_roomByNameWanted}' found");
                string shortName = _roomByNameWanted.Length > 20 ? _roomByNameWanted.Substring(0, 20) : _roomByNameWanted;
                _byNameNote = $"Co-op: no game called '{shortName}'. Press {_statusKey} twice to pick one";   // subtitles cut off long lines
                _byNameNoteLeft = 3;
                return;
            }
            Log($"Server= named the room '{room.Name}': joining it");
            SwitchTo(room, _password);
        }

        void Connect(string server, int port, string password)
        {
            _link = new ServerLink(_name, _preferHost, password, server, port) { Build = BuildInfo.Version + " " + LogSender.Build };

            _link.Joined += () =>
            {
                Say($"Co-op: joined {server} as player {_link.MyId}");
                Log($"joined {server}:{port} as player {_link.MyId}, host {_link.HostId}");
            };
            _link.RosterChanged += OnRoster;
            _link.Disconnected += reason =>
            {
                ClearSession();
                Say("Co-op: " + reason);
                Log("disconnected: " + reason);
            };
            _link.StateReceived += (from, s, at) =>
            {
                var r = RemoteFor(from);
                r.Interp.Push(s, at);
                r.Last = s;
                r.HasLast = true;
                r.HeardMs = at;
            };
            _link.WorldReceived += (from, w, at) =>
            {
                if (_bubbleMode) { if (_linked.Contains(from)) BubbleMirror(from).Push(w, at); }
                else if (from == _owner && _owner != _link.MyId) _mirror.Push(w, at);
            };
            _link.HitReceived += OnHit;
            _link.ObjectivesReceived += (from, list) => _assist.OnObjectives(from, list, _link);   // a 0.7.x host's; ours come as a MissionStep notice
            _link.NoticeReceived += (from, n) =>
            {
                if (n.Kind == NoticeKind.Chat) { _chat.OnNotice(from, n, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Clock) { if (from == _link.HostId && !_link.IAmHost) _env.OnClock(n.Text, _owner == _link.HostId); return; }
                if (n.Kind == NoticeKind.Waypoint) { _waypoints.OnNotice(from, n.Text, _link); return; }
                if (n.Kind == NoticeKind.Explosion) { _explosions.OnNotice(from, n.Text, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Law) { _law.OnNotice(from, n.Text, _link, _owner, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Door) { _doors.OnNotice(from, n.Text, _link, _owner, _remotes.TryGetValue(from, out var dr) && dr.HasLast ? dr.Last : (PlayerState?)null, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.MissionStep) { _assist.OnObjectives(from, MissionMarkers.Parse(n.Text, MissionAssist.DefaultRadius), _link); return; }   // the host's mission markers
                if (n.Kind == NoticeKind.Score) { _scoreboard.OnNotice(from, n.Text, _link, _owner, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.EntityKeys) { _doubles.OnNotice(from, n.Text); return; }
                if (n.Kind == NoticeKind.WorldShare) { _hostWorld.OnNotice(from, n.Text, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Cutscene) { _cutscenes.OnNotice(from, n.Text, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Ping) { _pings.OnNotice(from, n.Text, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Money) { _gifts.OnNotice(from, n.Text, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Emote) { _emotes.OnNotice(from, n.Text, _link, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.Contest) { _contests.OnNotice(from, n.Text, _link, id => _remotes.TryGetValue(id, out var cr) && cr.HasLast ? cr.Last.Position : (Vec3?)null, _clock.ElapsedMilliseconds); return; }
                if (n.Kind == NoticeKind.GamePoker) { _gamePoker.OnNotice(from, n.Text, id => _remotes.TryGetValue(id, out var gp) && gp.HasLast ? gp.Last.Position : (Vec3?)null); return; }
                if (n.Kind == NoticeKind.Table) { _modTables.OnNotice(from, n.Text, _link, id => _remotes.TryGetValue(id, out var tr) && tr.HasLast ? tr.Last.Position : (Vec3?)null, _clock.ElapsedMilliseconds); return; }
                // One receiver for every activity: the tables seat them, and the line shows once (not at each table game's 5 s repeat).
                if (n.Kind == NoticeKind.Activity) { if (_tables.OnNotice(from, n.Text, _clock.ElapsedMilliseconds)) Activities.OnNotice(from, _link.NameOf(from), n.Text, _clock.ElapsedMilliseconds); return; }
                _enrage.OnNotice(from, n, _link, _clock.ElapsedMilliseconds);
            };
            _link.ActionReceived += (from, a) =>
            {
                if (_remotes.TryGetValue(from, out var r) && r.HasLast && (_bubbleMode ? _linked.Contains(from) : _owner == _link.MyId && r.Last.Following == _link.MyId))
                    _owned.ApplyAction(a, r.Puppet);
            };
            _link.EntityLookReceived += (from, id, a) =>
            {
                if (id == MountLookId) { var r = RemoteFor(from); r.MountLook = a; r.HasMountLook = true; return; }
                if (_bubbleMode) { if (_linked.Contains(from)) BubbleMirror(from).OnLook(id, a); }
                else if (from == _owner && _owner != _link.MyId) _mirror.OnLook(id, a);
            };
            _link.AppearanceReceived += (from, a) =>
            {
                var r = RemoteFor(from);
                r.Look = a;
                r.HasLook = true;
            };
            _link.PartyReceived += (from, m) =>
            {
                if (m.Kind == PartyKind.AtObjective) { if (!Activities.Busy) _assist.OnAtObjective(from, _link, _clock.ElapsedMilliseconds); return; }   // no moves mid-activity
                if (m.Kind == PartyKind.Revive)
                {
                    if (m.Members != null && m.Members.Contains(_link.MyId) && _down.IsDown)
                        _down.GetUp(null, $"{_link.NameOf(from)} revived you");
                    return;
                }
                _party.OnMessage(from, m, _link);
            };

            _status = $"Co-op: calling {server}:{port}";
            Log(_status);
            Say(_status);
        }

        // Everything that belonged to the session we were in.
        void ClearSession()
        {
            PickList.Close();
            _missionBlock.Restore();
            _party.Leave();
            _chat.Clear();
            if (_down.IsDown) _down.GetUp(null, "disconnected");
            foreach (var r in _remotes.Values) { r.Puppet?.Delete(); Blips.Remove(ref r.FarBlip); }
            PuppetAim.Release();
            _remotes.Clear();
            _assist.Clear();
            _ride.Clear();
            _enrage.Clear();
            _waypoints.Clear();
            _explosions.Clear();
            _law.Clear();
            _doors.Clear();
            _tables.Clear();
            _scoreboard.Clear();
            _contests.Clear(_link);
            _spectate.Stop();
            _pings.Clear(); _gifts.Clear();   // gifts nobody answered come back
            _emotes.Clear();
            _modTables.Clear(_link);
            _gamePoker.Clear();
            AppDomain.CurrentDomain.SetData("rdr2coop.puppets", null);   // stale handles could be reused by new peds
            AppDomain.CurrentDomain.SetData("rdr2coop.copies", null);
            AppDomain.CurrentDomain.SetData("rdr2coop.puppetNames", null);
            _sharedPuppets = new int[0]; _sharedCopies = new int[0]; _sharedNames = new string[0];
            _mirror.Clear();
            ClearBubbleMirrors();
            _owner = 0;
        }

        // From the server browser: leave this session (and stop our own server,
        // since we're joining someone else's) and join the picked room. The
        // choice is saved, so the next start goes straight back there.
        BuiltInServer StartOwnServer(string password)
        {
            var b = BuiltInServer.Start(_hostPort, password, _upnpOn, _roomName, _public && _listServer.Length > 0 ? _listServer : null, out string why);
            if (why != null) { Log(why); Say(why); }
            return b;
        }

        // From the server browser: leave the room we're in and host our own
        // game again (our server, our story world).
        void HostOwn(string password)
        {
            if (string.IsNullOrEmpty(password)) password = _hostPassword;   // Enter on an empty box keeps the saved one
            if (string.IsNullOrEmpty(password)) { Say("Co-op: your game needs a room password to host"); return; }
            Log("browser: hosting our own game again");
            _link?.Dispose();
            ClearSession();
            _builtIn?.Dispose();
            _hostPassword = _password = password;
            _builtIn = StartOwnServer(password);
            try
            {
                Ini.SetCoop(_iniPath, new Dictionary<string, string> { { "RunServer", "Yes" }, { "Server", "127.0.0.1" }, { "Port", _hostPort.ToString() },
                                                                       { "Password", password }, { "HostPassword", password }, { "HostPort", _hostPort.ToString() } });
            }
            catch (Exception e) { Log("couldn't save hosting to the settings file: " + e.Message); }
            Connect("127.0.0.1", _hostPort, password);
        }

        void SwitchTo(Room room, string password)
        {
            if (room.HostHere) { HostOwn(password); return; }
            Log($"browser: joining '{room.Name}' at {room.Address}:{room.Port}");
            _link?.Dispose();
            ClearSession();
            _builtIn?.Dispose();
            _builtIn = null;
            _password = password;
            try
            {
                // Password is now the room's: our own room's stays in HostPassword for coming back.
                Ini.SetCoop(_iniPath, new Dictionary<string, string> { { "Server", room.Address }, { "Port", room.Port.ToString() },
                                                                       { "Password", password }, { "RunServer", "No" },
                                                                       { "HostPassword", _hostPassword }, { "HostPort", _hostPort.ToString() } });
            }
            catch (Exception e) { Log("couldn't save the room to the settings file: " + e.Message); }
            Connect(room.Address, room.Port, password);
        }

        bool IsOwnServer(Room r)
        {
            var b = _builtIn;
            if (b == null || r.Port != b.Port) return false;
            if (!r.Lan) return r.Address == b.ListedAs;   // our own listing in the online list
            if (!System.Net.IPAddress.TryParse(r.Address, out var a)) return false;
            return System.Net.IPAddress.IsLoopback(a) ||
                   System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()).Any(x => x.Equals(a));
        }

        Remote RemoteFor(byte id)
        {
            if (!_remotes.TryGetValue(id, out var r))
                _remotes[id] = r = new Remote { Interp = new Interpolator(_smoothingMs) };
            return r;
        }

        void OnRoster()
        {
            StartTrace();
            Trace("roster");
            var ids = new HashSet<byte>(_link.Roster.Select(e => e.Id));
            foreach (var id in _remotes.Keys.Where(id => !ids.Contains(id)).ToList())
            {
                _hostWorld?.OnRosterLeft(id);
                _remotes[id].Puppet?.Delete();
                Blips.Remove(ref _remotes[id].FarBlip);
                _remotes.Remove(id);
            }
            _waypoints.OnRoster(ids);
            _tables.OnRoster(ids);
            _scoreboard.OnRoster(ids);
            _contests.OnRoster(ids, _link, _clock.ElapsedMilliseconds);
            _pings.OnRoster(ids);
            _modTables.OnRoster(ids, _link, _clock.ElapsedMilliseconds);
            _gamePoker.OnRoster(ids);
            string who = string.Join(", ", _link.Roster.Select(e => e.Name + (e.Role == PlayerRole.Host ? " (host)" : "")));
            Say("Co-op: " + who);
            Log("roster: " + who);
            _party.OnRoster(_link);
            _partyList.Show(_clock.ElapsedMilliseconds);
            _lookSender.Resend();   // newcomers need our look
        }

        // ScriptHookRDR2DotNet stops a script for good when its tick throws (F10 then
        // does nothing at all): log the error, show it, and carry on instead.
        void OnTick(object sender, EventArgs e)
        {
            try { _logs?.Tick(_clock.ElapsedMilliseconds); TickBody(); }
            catch (Exception ex) { TickFailed(ex); }
        }

        string _lastError = "";
        long _lastErrorMs;

        void TickFailed(Exception ex)
        {
            long now = _clock.ElapsedMilliseconds;
            string key = ex.GetType().Name + ": " + ex.Message;
            if (key == _lastError && now - _lastErrorMs < 60000) return;   // the same error every tick: once a minute
            _lastError = key;
            _lastErrorMs = now;
            Log("error in the tick: " + ex);
            Say("Co-op error: " + key + " (details in scripts\\RDR2Coop.log)");
            _logs?.OnError(now);
        }

        void TickBody()
        {
            if (_link == null) return;
            if (_travelPending) { _travelPending = false; TravelTo(_travelTo, _travelName); }
            if (_roomByName != null) JoinRoomByName();
            long now = _clock.ElapsedMilliseconds;
            long gap = _lastTickMs == 0 ? 0 : now - _lastTickMs;
            _lastTickMs = now;
            if (gap > 2000)
            {
                // The gap was ours (pause menu, alt-tab, loading): the receiver dropped the partners'
                // positions meanwhile, so give them a fresh 20 s before they count as away, and
                // models asked for just before it their 15 s again.
                Log($"tick gap {gap} ms (pause menu, alt-tab or loading)");
                foreach (var r in _remotes.Values) r.HeardMs = now;
                _mirror.AfterGap();
                foreach (var bm in _bubbleMirrors.Values) bm.AfterGap();
            }
            _missionProbe.Tick(now);
            _missionRoles.Tick(now);
            _missionRec?.Tick(now, h => _remotes.Values.Any(r => r.Puppet != null && r.Puppet.Handle == h) ? "partner" : null);
            _missionBlock.Tick(!Sync.OwnMissionsAsGuest && _link != null && _link.State == LinkState.Connected && !_link.IAmHost, now);
            OwnMissionWarning(now);
            _tables.Tick(_link, now);
            Activities.Tick(_link, now);   // after the tables: its Busy counts theirs
            string note = _builtIn?.TakeNote();
            if (note != null) Say(note);
            var meNow = Game.Player.Character;
            if (meNow != null && meNow.Exists()) { string un = _updater?.TakeNote(); if (un != null) Say(un); }   // not over a loading screen
            if (_byNameNoteLeft > 0 && meNow != null && meNow.Exists() && now > 15000 && now - _byNameNoteMs > 6000 && (_byNameNoteLeft < 3 || ENTITY.GET_ENTITY_SPEED(meNow.Handle) > 0.5f)) { _byNameNoteMs = now; _byNameNoteLeft--; Say(_byNameNote); }   // the game hides the text while you stand idle after loading: from the first move
            if (PickList.IsOpen) PickList.Tick();
            if (_browser.IsOpen)
            {
                var room = _browser.Tick(_password, out string pw);
                if (room != null) SwitchTo(room, pw);
            }
            _chat.Tick(_link, now);
            PokerWatch.Tick(now);
            _cutsceneTest.Tick(now);
            _softsave.Tick(now);
            PokerWatch.FrameTick();
            if (!_enrageNoticeShown && now > 20000)   // once, when the game is up: the loader found an enRage the co-op patch doesn't fit
            {
                _enrageNoticeShown = true;
                if (AppDomain.CurrentDomain.GetData("rdr2coop.enrageNotice") is string notice) { Say(notice); Log(notice); }
            }
            Trace("tick: poll");
            _link.Poll(now);
            if (gap > 2000) _explosions.Clear();   // partners' blasts kept while we were frozen: long over, not into the game now
            Trace("tick: host world");
            _hostWorld.Tick(_link, now);   // every frame, connected or not: a dropped link takes the guest's own story back
            if (_link.State != LinkState.Connected) return;

            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            byte myId = _link.MyId;
            var myPos = ToNet(me.Position);

            Trace("tick: authority");
            // Whose game runs our area?
            var others = new Dictionary<byte, OtherPlayer>();
            foreach (var kv in _remotes)
                if (kv.Value.HasLast) others[kv.Key] = new OtherPlayer { Position = kv.Value.Last.Position, Following = kv.Value.Last.Following,
                                                                         Away = now - kv.Value.HeardMs > Authority.AwayMs
                                                                                // A game in a cutscene or a loading fade goes quiet for longer: keep its area rather than
                                                                                // spawning our own people into it and theirs back on top (Kaz's "two of each NPC")
                                                                                && !((kv.Value.Last.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0 && now - kv.Value.HeardMs < Authority.CutsceneAwayMs) };
            byte owner = Authority.Pick(myId, myPos, _link.HostId, others, _owner == 0 ? myId : _owner);
            if (Activities.Busy && !Tables.Seated) owner = myId;   // in a mini-game or activity our own game runs our area (a mod table keeps the shared world)

            // Bubbles: our own world always runs; linked players' worlds show next to it. The old area owner stays only
            // as the lead (trains, doors, law, respawn) and for the host's story missions (strict follow, as before).
            bool hostInMission = _remotes.TryGetValue(_link.HostId, out var hm) && hm.HasLast && (hm.Last.Flags & StateFlags.InMission) != 0;
            bool nearHost = owner == _link.HostId;   // the host's game runs our area (Authority's own distance), as strict follow always meant
            // Every player in the session has to have it on (StateFlags.Bubbles), or the old shared area runs for everyone
            // (someone still joining, with no state yet, doesn't flip it off for a frame).
            bool everyone = _link.Roster.Where(p => p.Id != myId).All(p => !_remotes.TryGetValue(p.Id, out var br) || !br.HasLast || (br.Last.Flags & StateFlags.Bubbles) != 0);
            bool bubble = Sync.Bubbles && everyone && !Bubbles.StrictFollow(_link.IAmHost, _party.IsMember, hostInMission, nearHost);
            if (bubble != _bubbleMode)
            {
                Log(bubble ? "bubbles: on (our own world runs, linked players' worlds show alongside)"
                    : !Sync.Bubbles ? "bubbles: off" : !everyone ? "bubbles: off for now (not everyone in the session has [Gameplay] Bubbles=Yes)" : "bubbles: off for now (strict follow: the host's story mission)");
                if (bubble) _mirror.Clear(); else ClearBubbleMirrors();
                _bubbleMode = bubble;
                MirrorWorld.Bubbles = bubble;
            }
            if (bubble)
            {
                var was = new HashSet<byte>(_linked);
                _linked.Clear();
                foreach (var kv in others) if (Bubbles.Linked(myPos, kv.Value, was.Contains(kv.Key))) _linked.Add(kv.Key);
                foreach (var id in _linked.Where(id => !was.Contains(id))) Log($"bubbles: linked with {_link.NameOf(id)}");
                foreach (var id in was.Where(id => !_linked.Contains(id))) { Log($"bubbles: unlinked from {(_link.NameOf(id) is var who && who != "?" ? who : $"player {id} (left)")}"); _doubles.Forget(id); if (_bubbleMirrors.TryGetValue(id, out var gone)) { gone.Clear(); _bubbleMirrors.Remove(id); } }
            }
            if (owner != _owner)
            {
                if (!bubble) _mirror.Clear();
                if (_owner != 0 && !bubble) Say(owner == myId ? "Co-op: your game runs this area" : $"Co-op: sharing {_link.NameOf(owner)}'s world");
                string why = owner != myId || _link.IAmHost ? ""
                    : Activities.Busy && !Tables.Seated ? " (we're busy with an activity: our game runs it)"
                    : $" ({Authority.WhyOwn(myPos, _link.HostId, others, _owner == 0 ? myId : _owner)})";
                Log($"area owner {_owner} -> {owner}{why}");
                _owner = owner;
            }

            AimSender.CountShots(me.Handle);
            if (now - _lastSendMs >= _sendEveryMs)
            {
                _lastSendMs = now;
                Trace("tick: capture");
                var s = Capture(me, now);
                s.Following = owner == myId ? (byte)0 : owner;
                if (Sync.Bubbles) s.Flags |= StateFlags.Bubbles;
                int veh = Peds.VehicleOf(me.Handle);
                if (veh != 0)
                {
                    // Vehicle ids live in the area owner's id space.
                    if (bubble)
                    {
                        // Bubbles: every game has its own vehicles; say whose world this one is in.
                        s.VehicleOwner = myId;
                        s.VehicleId = 0;
                        if (OwnedWorld.Skip.Contains(veh))
                        {
                            foreach (var kv in _bubbleMirrors)
                            {
                                ushort vid = kv.Value.IdOfVehicle(veh);
                                if (vid != 0) { s.VehicleOwner = kv.Key; s.VehicleId = vid; break; }
                            }
                        }
                        else s.VehicleId = _owned.IdForVehicle(veh);
                    }
                    else { s.VehicleOwner = owner; s.VehicleId = owner == myId ? _owned.IdForVehicle(veh) : _mirror.IdOfVehicle(veh); }
                    s.Seat = Peds.SeatOf(veh, me.Handle);
                    if (s.Seat == -1)
                    {
                        // We drive: the game that owns the vehicle steers its real one after ours
                        // (a train by our seat alone: older versions would push it like a wagon).
                        if (!Sync.Trains || !TrainDrive.IsTrain(veh)) s.Flags |= StateFlags.Driving;
                        s.MountPosition = ToNet(Peds.Position(veh));
                        s.MountHeading = ENTITY.GET_ENTITY_ROTATION(veh, 2).Z;
                        s.Velocity = ToNet(ENTITY.GET_ENTITY_VELOCITY(veh, 0));
                    }
                }
                Activities.Outgoing(ref s);
                _link.SendState(s);
            }

            Trace("tick: look");
            // Our look, when it changes (and every 15 s for anyone who joined late).
            if (_appearAs == 0 && now - _lastLookCheck > 2000)
            {
                _lastLookCheck = now;
                var look = Looks.Capture(me.Handle);
                if (_lookSender.ShouldSend(look.Signature(), now, out bool changed))
                {
                    if (changed) Log($"our look: {look.Tags.Count} pieces");
                    _link.SendAppearance(look);
                }
            }

            // Our own horse's look, while we ride it (a horse's look is an EntityLook with id 0).
            if (now - _lastMountCheck > 2000)
            {
                _lastMountCheck = now;
                int mount = me.IsOnMount && me.CurrentMount != null && RideDouble.With == 0 ? me.CurrentMount.Handle : 0;   // riding double: not our horse
                if (mount != 0)
                {
                    var look = Looks.Capture(mount);
                    uint sig = look.Signature();
                    if (sig != _mountSig || now - _lastMountSent > 15000)
                    {
                        _mountSig = sig;
                        _lastMountSent = now;
                        _link.SendEntityLook(MountLookId, look);
                    }
                }
            }

            // Puppets for everyone close enough to be in a loaded area.
            var puppets = new Dictionary<byte, Puppet>();
            foreach (var kv in _remotes)
            {
                var r = kv.Value;
                if (!r.HasLast || Vec3.Distance(r.Last.Position, myPos) > PuppetRange)
                {
                    r.Puppet?.Delete();
                    r.Puppet = null;
                    FarMarker(r, kv.Key, now);
                    continue;
                }
                Blips.Remove(ref r.FarBlip);   // close enough: the puppet carries their marker
                var st = r.Interp.Sample(now);
                if (r.Puppet == null || r.Puppet.PedModel != st.PedModel)
                {
                    Trace($"puppet {kv.Key}: spawn");
                    r.Puppet?.Delete();
                    r.Puppet = Puppet.Spawn(st.PedModel, ENTITY.GET_ENTITY_MODEL(me.Handle), ToGame(st.Position), st.Heading, _link.NameOf(kv.Key));
                    r.AppliedLook = 0;
                    r.AppliedWeapon = 0;
                    if (r.Puppet == null) continue;   // model still streaming; try next tick
                }
                // Their clothes, once we have them (only for the same model).
                if (r.HasLook && r.Look.Model == r.Puppet.PedModel && r.Look.Signature() != r.AppliedLook && now - r.LookAppliedMs > 5000)   // rebuilding the clothes resets the pose: not more than once in 5 s
                {
                    Trace($"puppet {kv.Key}: look");
                    Looks.Apply(r.Puppet.Handle, r.Look);
                    r.AppliedLook = r.Look.Signature();
                    r.LookAppliedMs = now;
                    Log($"{_link.NameOf(kv.Key)}'s look applied ({r.Look.Tags.Count} pieces)");
                }
                // The weapon in their hand (the author: "add the showing of the weapons").
                if (r.Last.Weapon != 0 && r.Last.Weapon != r.LoggedWeapon) { r.LoggedWeapon = r.Last.Weapon; Log($"{_link.NameOf(kv.Key)} holds weapon {r.Last.Weapon:X8}"); }
                if ((r.Last.Pose & PoseFlags.Aiming) != 0 && !r.LoggedAim) { r.LoggedAim = true; Log($"{_link.NameOf(kv.Key)} aims (weapon {r.Last.Weapon:X8}; their stand-in holds {Peds.CurrentWeapon(r.Puppet.Handle):X8})"); }
                if (r.Last.Weapon != r.AppliedWeapon && now - r.WeaponTryMs > 300)
                {
                    r.WeaponTryMs = now;
                    if (r.Puppet.ShowWeapon(r.Last.Weapon)) { r.AppliedWeapon = r.Last.Weapon; r.WeaponWaitSince = -1; }
                    else if (r.WeaponWaitSince < 0) r.WeaponWaitSince = now;
                    else if (now - r.WeaponWaitSince > 5000) { Log($"weapon {r.Last.Weapon:X8} for {_link.NameOf(kv.Key)} not shown in 5 s (asset loaded {WEAPON._HAS_WEAPON_ASSET_LOADED(r.Last.Weapon)}, their stand-in holds {Peds.CurrentWeapon(r.Puppet.Handle):X8}); still trying"); r.WeaponWaitSince = long.MaxValue / 2; }
                }
                if (Visuals.Wear) Wear.Show(kv.Key, r.Puppet.Handle, r.AppliedLook, r.Last, now);   // wet, muddy or bloody clothes
                Trace($"puppet {kv.Key}: follow");
                int vehicle = VehicleFor(r.Last, kv.Key, owner, myId);
                // Sitting behind someone (ride double): seated, not steered; its shots still show.
                if (_ride.Carry(kv.Key, r.Puppet, st, myId, _remotes.TryGetValue(st.RidingWith, out var rider) ? rider.Puppet : null, now))
                    r.Puppet.ShowShots(st);
                else if (!_tables.Sit(kv.Key, r.Puppet, st, now)) r.Puppet.Follow(st, now, vehicle);   // at a card table: seated there, not steered
                r.Puppet.Carried.Show(r.Puppet, st, now);   // what they carry (Visuals.Carrying): no order of its own, it rides along whichever of those steered
                // Their horse's saddle and coat, once their horse is here (same breed only).
                int horse = r.Puppet.HorseHandle;
                if (horse != 0 && r.HasMountLook && r.MountLook.Model == r.Puppet.HorseModel
                    && (r.MountLook.Signature() != r.AppliedMountLook || horse != r.AppliedMountHorse))
                {
                    Looks.Apply(horse, r.MountLook);
                    r.AppliedMountLook = r.MountLook.Signature();
                    r.AppliedMountHorse = horse;
                    Log($"{_link.NameOf(kv.Key)}'s horse look applied ({r.MountLook.Tags.Count} pieces)");
                }
                // Our game runs the area and they drive one of our real vehicles:
                // it follows the copy they steer in their game (a train by its speed alone).
                bool ourVehicle = VehicleWorld(r.Last, kv.Key) == myId;   // the vehicle is ours: they drive its copy (or our area's one)
                bool train = ourVehicle && vehicle != 0 && TrainDrive.IsTrain(vehicle);
                if (train && Sync.Trains && VEHICLE.GET_PED_IN_VEHICLE_SEAT(vehicle, -1) == r.Puppet.Handle) TrainDrive.FollowDriver(vehicle, st);
                if (ourVehicle && vehicle != 0 && !train && (st.Flags & StateFlags.Driving) != 0
                    && VEHICLE.GET_PED_IN_VEHICLE_SEAT(vehicle, -1) == r.Puppet.Handle)
                {
                    var target = ToGame(st.MountPosition);
                    // A boat goes along on OUR water: the driver's game can have the water metres higher or lower (30 Oct).
                    if (VEHICLE.IS_THIS_MODEL_A_BOAT(ENTITY.GET_ENTITY_MODEL(vehicle)) && MirrorWorld.WaterAt(target, out float water)) target = new Vector3(target.X, target.Y, water);
                    VehicleSteer.Glide(vehicle, ENTITY.GET_ENTITY_MODEL(vehicle), target, Peds.Position(vehicle), ToGame(st.Velocity),
                        st.MountHeading, !VEHICLE.IS_THIS_MODEL_A_BOAT(ENTITY.GET_ENTITY_MODEL(vehicle)),
                        VEHICLE.IS_DRAFT_VEHICLE(vehicle), r.Puppet.Handle, ref r.LastDriveMs, now);
                }
                Trace($"puppet {kv.Key}: catch hits");
                r.Puppet.SetCatchHits(bubble ? _linked.Contains(kv.Key) : owner == myId && r.Last.Following == myId);
                puppets[kv.Key] = r.Puppet;
            }
            // The browser has the keys. In a mini-game or activity (a card table, fishing, the bath...) no ride starts
            // (and F stays the game's): it counts as being down.
            if (!_browser.IsOpen) _ride.Tick(me.Handle, _down.IsDown || Activities.Busy, puppets, id => _remotes[id].Last, _link, now);
            Trace("tick: share with enRage");
            ShareWithEnrage(puppets.Where(kv => kv.Value.Exists).Select(kv => (kv.Value.Handle, _link.NameOf(kv.Key))), (bubble ? _bubbleMirrors.Values.SelectMany(m => m.CopyHandles()) : _mirror.CopyHandles()).Concat(Carrying.Copies), now);

            if (bubble)
            {
                BubbleTick(me, myId, myPos, owner, puppets, others, now);
            }
            else if (owner == myId)
            {
                if (!_mirror.Empty) _mirror.Clear();   // copies belong to a follower only, whatever path made us the owner
                var followers = new Dictionary<byte, Puppet>();
                var positions = new List<Vec3>();
                foreach (var kv in puppets)
                {
                    if (_remotes[kv.Key].Last.Following != myId) continue;
                    followers[kv.Key] = kv.Value;
                    positions.Add(_remotes[kv.Key].Last.Position);
                }
                Trace("tick: owned area");
                _owned.Tick(_link, followers, puppets.Values, positions, now);
                _law.OwnerTick(_link, followers, now);
                _doors.Tick(_link, owner, positions, now);
                Trace("tick: take hits");
                foreach (var kv in followers)
                {
                    int damage = kv.Value.TakeHits(me.Handle, out int bone);
                    if (damage > 0) _link.SendHit(kv.Key, 0, (short)Math.Min(damage, short.MaxValue), bone);
                }
            }
            else
            {
                Trace("tick: mirror");
                _mirror.Tick(_link, owner, puppets, now);
                _doors.Tick(_link, owner, null, now);
            }
            Trace("tick: explosions");
            _explosions.Tick(_link, me, owner, puppets, id => _remotes.TryGetValue(id, out var x) && x.HasLast ? x.Last : (PlayerState?)null, now);

            PlayerState? hostState = _remotes.TryGetValue(_link.HostId, out var hr) && hr.HasLast ? hr.Last : (PlayerState?)null;
            Trace("tick: party");
            _party.Tick(_link, TableGames.Busy ? null : hostState, now);   // at a table: no fading or fetching us
            Trace("tick: cutscenes");
            if (_link.IAmHost) _cutscenes.HostTick(_link, _owned, _missionProbe.Running ?? MissionProbe.TestMission, _party.IsHosting, now);
            else _cutscenes.GuestTick(_party.IsMember && _party.Near, bubble ? BubbleMirror(_link.HostId) : _mirror, id => _remotes.TryGetValue(id, out var cr) && cr.Puppet != null && cr.Puppet.Exists ? cr.Puppet.Handle : 0, _link.HostId, now);
            Trace("tick: enRage link");
            _enrage.Tick(_link, _down.IsDown, now);
            if (bubble) { if (_linked.Contains(_link.HostId) && !TableGames.Busy) _enrage.DrawNames(_link.HostId, BubbleMirror(_link.HostId).HandleOf); }
            else if (owner != myId && !TableGames.Busy) _enrage.DrawNames(owner, _mirror.HandleOf);
            if (Visuals.Names && !TableGames.Busy && !Cutscenes.Playing) NameTags.Draw(puppets, _link.NameOf, me.Position);   // off the card game's view and cutscenes
            Trace("tick: waypoints");
            _waypoints.Tick(_link, now);
            Trace("tick: mission assist");
            if (_link.IAmHost) _env.HostTick(_link, now);
            if (_link.IAmHost) _assist.HostTick(_link, id => _remotes.TryGetValue(id, out var x) && x.HasLast ? x.Last : (PlayerState?)null, now);
            else _assist.GuestTick(_link, _party.IsMember, hostState, now);

            Trace("tick: down");
            // Down / revive.
            Vector3? ownerPos = owner != myId && _remotes.TryGetValue(owner, out var or) && or.HasLast ? ToGame(or.Last.Position) : (Vector3?)null;
            bool ownerHidden = owner != myId && _remotes.TryGetValue(owner, out var oh) && oh.HasLast && (oh.Last.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0;
            // Party down (26 Oct): everyone in the session can go down; the game's own death only once all are down.
            var partnerIds = _link.Roster.Where(p => p.Id != myId).Select(p => p.Id).ToList();
            bool allOthersDown = PartyDown.AllDown(partnerIds.Select(id => _remotes.TryGetValue(id, out var dr) && dr.HasLast && (dr.Last.Flags & (StateFlags.Downed | StateFlags.Dead)) != 0));
            bool fight = bubble ? _owned.CombatNear(me.Position, 60f, now) || _bubbleMirrors.Values.Any(m => m.CombatNear(me.Position, 150f))
                       : owner != myId ? _mirror.CombatNear(me.Position, 150f) : _owned.CombatNear(me.Position, 60f, now);
            if (!TableGames.Busy) _down.Tick(owner != myId, ownerPos, fight, ownerHidden, _link.NameOf(owner), partnerIds.Count, allOthersDown, now);
            _spectate.Tick(_down.IsDown || me.IsDead, _link, id => _remotes.TryGetValue(id, out var sr) && sr.Puppet != null && sr.Puppet.Exists ? sr.Puppet.Handle : 0, now);
            var downed = DownedNear(me.Position);
            if (downed != 0 && now - _lastRevivePrompt > 2000)
            {
                _lastRevivePrompt = now;
                Say($"Co-op: press {_reviveKey} to revive {_link.NameOf(downed)}");
            }
            Trace("tick: party list");
            if (!_browser.IsOpen) _partyList.Tick(_link, id =>   // hidden while the co-op menu is open: it sat over the menu's corner
                 _remotes.TryGetValue(id, out var pl) && pl.HasLast ? pl.Last : (PlayerState?)null, me, _down.IsDown, now);
            Kills.Tick(_link, bubble || owner == myId, me.Handle, puppets, now);
            _scoreboard.Tick(_link, now, _contests.Showing);   // the contest panel has the slot while it shows
            _contests.Tick(_link, myPos, now);
            _pings.Tick(_link, me, now); _gifts.Tick(_link, now);
            _emotes.Tick(_link, me.Handle, _down.IsDown, puppets, now);
            _gamePoker.Tick(_link, myPos, id => _remotes.TryGetValue(id, out var gt) && gt.HasLast ? gt.Last.Position : (Vec3?)null, now);
            _modTables.Tick(_link, myPos, id => _remotes.TryGetValue(id, out var tr) && tr.HasLast ? tr.Last.Position : (Vec3?)null, _down.IsDown, _browser.IsOpen, now);
            Trace("tick: end");
        }

        // enRage (with its co-op patch) reads these from the script domain: the
        // other players' characters count as "you" (with their names, for
        // per-player grudges), copies count as nobody. New arrays each time
        // (never edited in place); only when they change.
        long _lastShareMs;
        int[] _sharedPuppets = new int[0], _sharedCopies = new int[0];
        string[] _sharedNames = new string[0];

        void ShareWithEnrage(IEnumerable<(int handle, string name)> puppets, IEnumerable<int> copies, long now)
        {
            if (now - _lastShareMs < 250) return;
            _lastShareMs = now;
            var list = puppets.OrderBy(p => p.handle).ToList();
            var p2 = list.Select(p => p.handle).ToArray();
            var names = list.Select(p => p.name).ToArray();
            var c = copies.OrderBy(h => h).ToArray();
            AppDomain.CurrentDomain.SetData("rdr2coop.myName", _link.NameOf(_link.MyId));
            if (!names.SequenceEqual(_sharedNames)) { _sharedNames = names; AppDomain.CurrentDomain.SetData("rdr2coop.puppetNames", names); }
            if (!p2.SequenceEqual(_sharedPuppets)) { _sharedPuppets = p2; AppDomain.CurrentDomain.SetData("rdr2coop.puppets", p2); }
            if (!c.SequenceEqual(_sharedCopies)) { _sharedCopies = c; AppDomain.CurrentDomain.SetData("rdr2coop.copies", c); }
        }

        // A player who is down within reach of us, or 0.
        byte DownedNear(Vector3 at)
        {
            foreach (var kv in _remotes)
                if (kv.Value.HasLast && (kv.Value.Last.Flags & StateFlags.Downed) != 0 && kv.Value.Puppet != null
                    && kv.Value.Puppet.Exists && kv.Value.Puppet.Position.DistanceTo(at) < 2.5f)
                    return kv.Key;
            return 0;
        }

        // The local vehicle (real when our game runs the area, else a copy) a
        // remote player says they sit in, if we share the same area owner.
        MirrorWorld BubbleMirror(byte id)
        {
            if (!_bubbleMirrors.TryGetValue(id, out var m)) _bubbleMirrors[id] = m = new MirrorWorld(_smoothingMs);
            return m;
        }

        void ClearBubbleMirrors()
        {
            _doubles.ShowAll();
            _crowd.Clear();
            foreach (var m in _bubbleMirrors.Values) m.Clear();
            _bubbleMirrors.Clear();
            _linked.Clear();
            OwnedWorld.Skip.Clear();
        }

        // Bubbles: our own world for the linked players, their worlds as copies, the lead's jobs (law, doors) as before,
        // and the crowd rule: near a higher-priority linked player our game stops spawning ambient life and retires its
        // own off-screen ambient people there (never one on screen, fighting, fleeing, tied or dead).
        void BubbleTick(Ped me, byte myId, Vec3 myPos, byte lead, Dictionary<byte, Puppet> puppets, Dictionary<byte, OtherPlayer> others, long now)
        {
            var linkedPuppets = puppets.Where(kv => _linked.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            var positions = linkedPuppets.Keys.Select(id => _remotes[id].Last.Position).ToList();
            // A linked game whose world stopped arriving (a cutscene, a loading screen, a stall): its copies go and it
            // stops counting for the crowd and the trains, until its world comes again.
            var live = new HashSet<byte>();
            foreach (byte id in _linked)
            {
                var m = BubbleMirror(id);
                if (now - m.NewestMs <= StaleWorldMs) { live.Add(id); continue; }
                if (!m.Empty) { m.Clear(); Log($"bubbles: {_link.NameOf(id)}'s world stopped arriving: their copies removed until it does"); }
            }
            OwnedWorld.Skip.Clear();
            foreach (var m in _bubbleMirrors.Values) OwnedWorld.Skip.UnionWith(m.MadeHandles());
            OwnedWorld.Skip.UnionWith(_doubles.Hidden);   // our people hidden as doubles: never sent either
            Trace("tick: owned (bubbles)");
            _owned.Tick(_link, linkedPuppets, puppets.Values, positions, now);
            foreach (var kv in linkedPuppets)
            {
                int damage = kv.Value.TakeHits(me.Handle, out int bone, OwnedWorld.Skip);   // our NPCs hurting their stand-in: theirs to feel (copies' hits are their own games')
                if (damage > 0) _link.SendHit(kv.Key, 0, (short)Math.Min(damage, short.MaxValue), bone);
            }
            if (lead == myId) { _law.OwnerTick(_link, linkedPuppets, now); _doors.Tick(_link, lead, positions, now); }
            else _doors.Tick(_link, lead, null, now);
            Trace("tick: mirrors (bubbles)");
            foreach (byte id in live) BubbleMirror(id).Tick(_link, id, puppets, now);
            Trace("tick: doubles (bubbles)");
            _doubles.Tick(_link, _owned, live, BubbleMirror, myPos, now);

            var linkedOthers = others.Where(kv => live.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            int horse = Game.Player.ActiveHorse?.Handle ?? 0, mount = me.IsOnMount && me.CurrentMount != null ? me.CurrentMount.Handle : 0;
            var keep = new HashSet<int>(puppets.Values.SelectMany(p => new[] { p.Handle, p.HorseHandle })) { me.Handle, horse, mount, PED._GET_LAST_MOUNT(me.Handle) };
            Trace("tick: crowd (bubbles)");
            _crowd.Tick(myId, myPos, _link.HostId, linkedOthers, keep, new List<Vec3>(positions) { myPos }, now);
        }

        int VehicleFor(PlayerState st, byte remoteId, byte owner, byte myId)
        {
            if (st.VehicleId == 0) return 0;
            byte inWorld = VehicleWorld(st, remoteId);
            if (inWorld == myId) return _owned.VehicleHandle(st.VehicleId);   // one of ours: they sit in (or drive) our real one
            if (_bubbleMode) return _linked.Contains(inWorld) ? BubbleMirror(inWorld).VehicleHandle(st.VehicleId) : 0;
            return inWorld == owner ? _mirror.VehicleHandle(st.VehicleId) : 0;
        }

        // Whose world a player's vehicle id is in, in either mode (older versions send no owner: their area owner's).
        static byte VehicleWorld(PlayerState st, byte remoteId) => st.VehicleOwner != 0 ? st.VehicleOwner : st.Following != 0 ? st.Following : remoteId;

        // A hit on one of our NPCs (when our game runs the area), or an NPC in
        // someone else's game hitting our character (EntityId 0).
        long _hitLogMs;
        void OnHit(byte from, Hit hit)
        {
            if (!Hit.Sane(hit.Damage)) return;
            long nowMs = _clock.ElapsedMilliseconds;
            if (nowMs - _hitLogMs > 1000) { _hitLogMs = nowMs; Log($"hit from {_link.NameOf(from)}: entity {hit.EntityId}, damage {hit.Damage} (we run area {_owner}, they follow {(_remotes.TryGetValue(from, out var rr) && rr.HasLast ? rr.Last.Following : -1)})"); }
            if (hit.EntityId != 0)
            {
                // Only players in our area can shoot our NPCs.
                if (_remotes.TryGetValue(from, out var r) && r.HasLast && (_bubbleMode ? _linked.Contains(from) : _owner == _link.MyId && r.Last.Following == _link.MyId))
                {
                    int victim = _owned.PedOf(hit.EntityId);
                    // Burning here: our fire burns it (a partner's fire bottle is made here too), so their copy's burns would count twice.
                    if (Sync.Explosions && Peds.Exists(victim) && FIRE.IS_ENTITY_ON_FIRE(victim)) return;
                    _law.BeforeGuestHit(from, victim, _clock.ElapsedMilliseconds);   // the crime is theirs, not ours
                    _owned.ApplyHit(hit, r.Puppet);
                }
                return;
            }
            // Only the game that runs our area has NPCs that can reach us.
            if ((_bubbleMode ? !_linked.Contains(from) : from != _owner) || _down.IsDown || TableGames.Busy) return;   // the card games make us invincible at the table too
            var me = Game.Player.Character;
            if (me == null || !me.Exists() || me.IsDead) return;
            if (_down.WouldFinish(hit.Damage)) { _down.GoDown($"hit by an NPC in {_link.NameOf(from)}'s game"); return; }
            PED.APPLY_DAMAGE_TO_PED(me.Handle, hit.Damage, 0, hit.Bone, 0);
        }

        PlayerState Capture(Ped me, long now)
        {
            var s = new PlayerState
            {
                Seq = ++_seq,
                TimeMs = (int)now,
                PedModel = _appearAs != 0 ? _appearAs : ENTITY.GET_ENTITY_MODEL(me.Handle),
                Position = ToNet(me.Position),
                Heading = me.Heading,
                Velocity = ToNet(me.Velocity),
            };
            if (me.IsDead) s.Flags |= StateFlags.Dead;
            if (MissionProbe.InMissionScene) s.Flags |= StateFlags.InCutscene;
            if (MissionProbe.StoryMission) s.Flags |= StateFlags.InMission;
            if (!TableGames.Busy && !me.IsDead && !_down.IsDown && !CAM.IS_DEATH_FAIL_CAMERA_RUNNING() && (CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()))
                s.Flags |= StateFlags.ScreenFaded;   // a table game's fades aren't a mission's, nor is a death screen: partners don't black out for it
            if (_down.IsDown) s.Flags |= StateFlags.Downed;

            var horse = me.IsOnMount ? me.CurrentMount : null;
            if (horse != null && horse.Exists())
            {
                s.Flags |= StateFlags.Mounted;
                s.MountModel = ENTITY.GET_ENTITY_MODEL(horse.Handle);
                s.MountPosition = ToNet(horse.Position);
                s.MountHeading = horse.Heading;
                s.Velocity = ToNet(horse.Velocity);
            }
            _ride.Fill(ref s);   // behind a partner on their horse: sent on foot, RidingWith set
            s.Weapon = WeaponInHand(me.Handle);
            s.Health = _partyList.Health(me.Handle, now);
            s.Pose |= Stance.Read(me.Handle, now);
            AimSender.Capture(ref s);
            if (Visuals.Wear) Wear.Ours(me.Handle, now, ref s);
            if (Visuals.Emotes) s.Scenario = Emotes.Ours(me.Handle, now);
            Carrying.Capture(me.Handle, s.Mounted ? horse.Handle : 0, ref s, now);
            return s;
        }

        // A guest whose own game is already in a story mission (a save in chapter 1, which runs as one long
        // mission, or mid-mission): the block only stops missions starting, so that mission's people appear on top
        // of the host's copies (bug #4, Zaxxon's log). Said every 30 s while it lasts, logged once per mission.
        string _ownMissionWarned;
        long _ownMissionSaid;
        void OwnMissionWarning(long now)
        {
            string running = _link != null && _link.State == LinkState.Connected && !_link.IAmHost && !Sync.OwnMissionsAsGuest ? _missionProbe.Running : null;
            if (running == null) { _ownMissionWarned = null; return; }
            if (running != _ownMissionWarned)
            {
                _ownMissionWarned = running;
                _ownMissionSaid = long.MinValue / 2;
                Log($"own story mission running as a guest: {running}: its people appear on top of the host's (a save in chapter 1 or mid-mission)");
            }
            if (now - _ownMissionSaid < 30000) return;
            _ownMissionSaid = now;
            Say($"Co-op: your own save is in a story mission ({running}), so its people appear on top of {_link.NameOf(_link.HostId)}'s. Load a save outside a mission (after chapter 1) and rejoin.");
        }

        // The weapon in our hand (unarmed, or one in a holster, counts as none).
        static unsafe uint WeaponInHand(int ped)
        {
            uint w = 0;
            if (!WEAPON.GET_CURRENT_PED_WEAPON(ped, &w, true, 0, false)) return 0;
            return w == MISC.GET_HASH_KEY("WEAPON_UNARMED") ? 0 : w;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            TableGames.Answering = true;   // what our keys answer shows even at a card table
            if (_link == null)
            {
                // Start failed: the status key says why.
                if (e.KeyCode == (_statusKey == Keys.None ? Keys.F10 : _statusKey) && _status.Length > 0) Say(_status);
                return;
            }
            if (_chat.OnKey(e.KeyCode, _link, _browser.IsOpen || PickList.IsOpen, _clock.ElapsedMilliseconds)) return;
            if (PickList.IsOpen) { PickList.OnKey(e.KeyCode); return; }
            if (_browser.IsOpen && (e.KeyCode == _browserKey || e.KeyCode == _statusKey)) { _browser.Toggle(); return; }
            if (_browserKey != Keys.None && e.KeyCode == _browserKey) { _browser.Toggle(); return; }
            if (_browser.OnKey(e.KeyCode)) return;
            if (_logs != null && _logs.OnKey(e.KeyCode)) return;   // the beta-logs question comes before every other Y/N
            if (_hostWorld != null && _hostWorld.OnKey(e.KeyCode)) return;   // bring back the last session's checkpoint?
            if (TableGames.Busy && (e.KeyCode == Keys.Y || e.KeyCode == Keys.N)) return;   // the questions they answer wait too
            if (Activities.Busy && (e.KeyCode == _teleportKey || e.KeyCode == _travelKey || e.KeyCode == _ride.Key)) { Say("Co-op: finish what you're doing first"); return; }
            if (_partyKey != Keys.None && e.KeyCode == _partyKey) { _party.Toggle(_link); return; }
            if (_partyList.OnKey(e.KeyCode)) return;
            if (_scoreboard.OnKey(e.KeyCode)) return;
            if (_spectate.OnKey(e.KeyCode)) return;   // down or dead: the teleport key picks a partner to watch, Esc stops
            if (_pings.OnKey(e.KeyCode)) return;
            if (_cutsceneTest.OnKey(e.KeyCode, _clock.ElapsedMilliseconds)) return;
            if (_softsave.OnKey(e.KeyCode, _clock.ElapsedMilliseconds)) return;
            if (_emotes.OnKey(e.KeyCode)) return;
            if (e.KeyCode == _travelKey) { _travelPending = true; return; }
            if (e.KeyCode == _nemesisKey && _enrage.RequestList(_link)) { Say($"Co-op: asking {_link.NameOf(_link.HostId)}'s enRage about your nemeses"); return; }
            if (e.KeyCode == _reviveKey)
            {
                byte target = DownedNear(Game.Player.Character.Position);
                if (target == 0) { Say("Co-op: nobody down within reach"); return; }
                _link.SendParty(new PartyMsg { Kind = PartyKind.Revive, Members = new List<byte> { target } });
                Say($"Co-op: you revived {_link.NameOf(target)}");
                Stats.Add("revives");
                return;
            }
            if (_ride.OnKey(e.KeyCode)) return;
            if (!Activities.Busy && _assist.OnKey(e.KeyCode)) return;
            if (e.KeyCode == Keys.Y || e.KeyCode == Keys.N) _party.OnKey(e.KeyCode, _link);
            if (e.KeyCode == _statusKey)
            {
                if (_browserKey == Keys.None && Environment.TickCount - _lastStatusKey < 3000)
                {
                    _lastStatusKey = 0;
                    _browser.Toggle();
                    return;
                }
                _lastStatusKey = Environment.TickCount;
                _partyList.Show(_clock.ElapsedMilliseconds);
                string players = string.Join(", ", _link.Roster.Select(p => p.Name + (p.Role == PlayerRole.Host ? " (host)" : "")
                                                                            + _tables.Doing(p.Id, _link.MyId, _clock.ElapsedMilliseconds)));
                string area = _owner == 0 ? "-" : _owner == _link.MyId ? "your game" : _link.NameOf(_owner) + "'s game";
                Say((_byNameNote != null ? _byNameNote + " | " : "") + $"Co-op {BuildInfo.Version}: {_link.State} | players: {players} | this area: {area} | NPC copies: {_mirror.Count} | {_party.Status}" +
                    (_builtIn != null ? " | " + _builtIn.Status : "") + (_updater?.Available != null ? $" | {_updater.Available} is out: F10 twice > Update" : "") +
                    (_browserKey == Keys.None ? $" | {_statusKey} again: server browser" : "") + (_logs?.Status(_clock.ElapsedMilliseconds) is string ls ? " | " + ls : ""));
            }
            else if (e.KeyCode == _pokerFoldKey) Say(PokerWatch.ArmFold());
            else if (e.KeyCode == _pokerDeckKey) Say(PokerWatch.ArmDeck());
            else if (e.KeyCode == _pokerAwayKey) Say(PokerWatch.SendAway());
            else if (e.KeyCode == _clockKey)
            {
                CLOCK.SET_CLOCK_TIME(_clockHour, 0, 0);   // a partner hosting sends their clock again: this is for testing alone
                Say($"Co-op: the time is now {_clockHour:00}:00");
                Log($"testing: clock set to {_clockHour:00}:00");
            }
            else if (e.KeyCode == _spawnWagonKey)
            {
                // Testing aid: a coach (or cart) with its horses, 8 m ahead.
                var me = Game.Player.Character;
                var at = me.Position + me.ForwardVector * 8f;
                foreach (var name in new[] { "stagecoach001x", "cart01", "wagon02x" })
                {
                    uint model = MISC.GET_HASH_KEY(name);
                    if (!Peds.Load(model)) continue;
                    int veh = VEHICLE.CREATE_VEHICLE(model, at, me.Heading, false, false, false, false);
                    STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(model);
                    if (veh == 0) continue;
                    Say("Co-op: spawned " + name);
                    Log("spawned " + name);
                    return;
                }
                Say("Co-op: no wagon model could be loaded");
            }
            else if (e.KeyCode == _spawnTrainKey)
            {
                // Testing aid: a passenger train at our feet (stand on the rails), rolling at 8 m/s.
                var me = Game.Player.Character;
                int train = TrainConfigs.Create(TrainConfigs.Default, me.Position, true, true);
                if (train == 0) { Say("Co-op: no train - see RDR2Coop.log"); return; }
                VEHICLE.SET_TRAIN_SPEED(train, 8f);
                VEHICLE.SET_TRAIN_CRUISE_SPEED(train, 8f);
                me.Position = me.Position + new Vector3(0f, 0f, 0f) + me.RightVector * 6f;   // step off the track
                Say("Co-op: spawned a train");
                Log("spawned a test train");
            }
            else if (e.KeyCode == _teleportKey)
            {
                // To the nearest other player we can see.
                var me = Game.Player.Character;
                var target = _remotes.Values.Where(r => r.Puppet != null && r.Puppet.Exists)
                    .OrderBy(r => r.Puppet.Position.DistanceTo(me.Position)).FirstOrDefault();
                if (target == null) { Say("Co-op: nobody close enough to teleport to"); return; }
                var to = target.Puppet.Position + new Vector3(1.5f, 1.5f, 0f);
                ENTITY.SET_ENTITY_COORDS(RideDouble.Mover(me), to, false, false, false, false);
            }
        }

        void OnAborted(object sender, EventArgs e)
        {
            _hostWorld?.OnAbort();   // never leave the player frozen behind a restore's black screen
            PickList.Close();
            _logs?.OnStop();
            foreach (var r in _remotes.Values) { r.Puppet?.Delete(); Blips.Remove(ref r.FarBlip); }
            PuppetAim.Release();
            _mirror?.Clear();
            ClearBubbleMirrors();
            _waypoints.Clear();
            _explosions.Clear();   // nobody stays fire- and explosion-proof
            _assist?.Clear();
            _missionBlock.Restore();   // a guest's own missions back before the mod goes
            _doors.Clear();    // doors back as our own game had them
            _party?.Leave();   // never leave us faded, frozen or without control
            _contests.Clear(_link);   // the contest we host is called off; its blips and markers go
            _spectate.Stop();   // the game's own camera back
            _pings.Clear(); _gifts.Clear();   // gifts nobody answered come back
            _emotes.Clear();
            _modTables.Clear(_link);   // we get up; the table we host closes
            _chat.Clear();
            if (_down != null && _down.IsDown) _down.GetUp(null, "co-op stopped");
            _link?.Dispose();
            _builtIn?.Dispose();
            _updater?.Stop();
            AppDomain.CurrentDomain.SetData("rdr2coop.puppets", null);
            AppDomain.CurrentDomain.SetData("rdr2coop.copies", null);
        }

        // Too far away for a puppet: a named marker at where they are, moved once a
        // second, so the map shows everyone in the session wherever they ride.
        void FarMarker(Remote r, byte id, long now)
        {
            if (!r.HasLast) { Blips.Remove(ref r.FarBlip); return; }
            if (r.FarBlip == 0 || !MAP.DOES_BLIP_EXIST(r.FarBlip))
            {
                r.FarBlip = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY("BLIP_STYLE_FRIENDLY"), ToGame(r.Last.Position));
                if (r.FarBlip != 0) { MAP._SET_BLIP_NAME(r.FarBlip, _link.NameOf(id)); Blips.Mine.Add(r.FarBlip); }
                r.FarBlipMs = now;
            }
            else if (now - r.FarBlipMs > 1000)
            {
                MAP.SET_BLIP_COORDS(r.FarBlip, ToGame(r.Last.Position));
                r.FarBlipMs = now;
            }
        }

        // Testing aid: there in one go, horse and all, on the ground.
        static unsafe void TravelTo(Vector3 at, string name)
        {
            var me = Game.Player.Character;
            int mover = RideDouble.Mover(me);
            CAM.DO_SCREEN_FADE_OUT(300);
            for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Yield();
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 120 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Yield();
            float z = 0f;
            if (MISC.GET_GROUND_Z_FOR_3D_COORD(at.X, at.Y, at.Z + 20f, &z, false))
                ENTITY.SET_ENTITY_COORDS(mover, new Vector3(at.X, at.Y, z + 0.5f), false, false, false, false);
            CAM.DO_SCREEN_FADE_IN(600);
            Say("Co-op: travelled to " + name);
        }

        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));

        static Keys ParseKey(string s, Keys fallback) =>
            !string.IsNullOrWhiteSpace(s) && Enum.TryParse(s, true, out Keys k) ? k : fallback;

        internal static Vec3 ToNet(Vector3 v) => new Vec3(v.X, v.Y, v.Z);
        internal static Vector3 ToGame(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        internal static void Say(string text) { if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text); }

        // [Testing] TraceJoins=Yes: for 20 s after anyone joins or leaves, or a
        // story cutscene starts or ends, log a marker before each step of the
        // tick, so the last line before a ScriptHook error names the step that failed.
        internal static bool TraceOn;
        static int _traceUntil;
        internal static void Trace(string step)
        {
            if (TraceOn && Environment.TickCount - _traceUntil < 0) Log("trace " + step);
        }

        internal static void StartTrace() => _traceUntil = Environment.TickCount + 20000;

        static readonly object LogLock = new object();   // the server and UPnP threads log too

        internal static void Log(string line)
        {
            try
            {
                lock (LogLock)
                    File.AppendAllText(Path.Combine("scripts", "RDR2Coop.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine);
                MissionRecorder.OnLog(line);
            }
            catch (Exception) { }
        }
    }
}
