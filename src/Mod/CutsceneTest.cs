// Testing aid ([Keys] CutsceneTest=, [Testing] Cutscene*=): plays a story cutscene in this game on its own, outside
// its mission, the way a guest's game would have to play the host's. Story cutscenes are anim scenes named
// "cutscene@<label>" (the label is what MissionProbe logs as "story cutscene playing"), "NormalStart" is the stage
// engine's default playback list. Logs whether it loads, starts, how long it runs, whether its camera takes over,
// and which of the named parts ([Testing] CutsceneNames) the scene has a ped for (bound by us or made by the scene).
// Second press: stop and delete it.
using System;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;
using Keys = System.Windows.Forms.Keys;

namespace RDR2Coop.Mod
{
    sealed class CutsceneTest
    {
        public Keys Key = Keys.None;
        string _label = "MUD3_INT", _list = "NormalStart";
        string[] _names = new string[0];
        bool _bindPlayer = true, _asStory;
        string _mission;
        (string part, string model)[] _spawn = new (string, string)[0];   // [Testing] CutsceneSpawn=part:model,...: people made and cast, as a guest's copies would be
        readonly System.Collections.Generic.List<int> _made = new System.Collections.Generic.List<int>();
        int _scene, _step;   // 0 idle, 1 loading, 2 running
        long _stepMs, _lastLogMs;

        public void Configure(Ini ini)
        {
            _label = ini.Get("Testing", "CutsceneLabel", _label).Trim();
            _list = ini.Get("Testing", "CutscenePlayback", _list).Trim();
            _names = ini.Get("Testing", "CutsceneNames", "ARTHUR,player_zero").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            _bindPlayer = !ini.Get("Testing", "CutsceneBindPlayer", "Yes").StartsWith("n", StringComparison.OrdinalIgnoreCase);
            _asStory = ini.Get("Testing", "CutsceneAsStory", "No").StartsWith("y", StringComparison.OrdinalIgnoreCase);   // hosts send it to the party (Cutscenes.cs)
            _mission = ini.Get("Testing", "CutsceneMission", "mudtown3").Trim();   // whose mission map names its parts
            _spawn = ini.Get("Testing", "CutsceneSpawn", "").Split(',').Select(x => x.Split(':')).Where(x => x.Length == 2)
                        .Select(x => (x[0].Trim(), x[1].Trim())).ToArray();
        }

        public bool OnKey(Keys k, long now)
        {
            if (Key == Keys.None || k != Key) return false;
            if (_step != 0) { Stop("stopped by the key"); return true; }
            string dict = "cutscene@" + _label;
            _scene = ANIMSCENE._CREATE_ANIM_SCENE(dict, 256 | 2048, _list, false, true);
            if (_scene == 0 || !ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { CoopScript.Log($"cutscene test: couldn't create {dict}"); CoopScript.Say("Co-op test: no such cutscene " + dict); _scene = 0; return true; }
            if (_bindPlayer)
                foreach (var n in new[] { "ARTHUR", "player_zero" }) ANIMSCENE.SET_ANIM_SCENE_ENTITY(_scene, n, PLAYER.PLAYER_PED_ID(), 0);
            var me = Game.Player.Character;
            foreach (var (part, model) in _spawn)
            {
                uint hash = MISC.GET_HASH_KEY(model);
                if (!Peds.Load(hash)) { CoopScript.Log($"cutscene test: model {model} won't load"); continue; }
                int ped = STREAMING.IS_MODEL_A_VEHICLE(hash) ? VEHICLE.CREATE_VEHICLE(hash, me.Position + me.ForwardVector * 8f, me.Heading, false, false, false, false)
                                                              : Peds.Spawn(hash, me.Position + me.ForwardVector * 3f, me.Heading);
                if (ped == 0) continue;
                _made.Add(ped);
                ANIMSCENE.SET_ANIM_SCENE_ENTITY(_scene, part, ped, 0);
            }
            ANIMSCENE.LOAD_ANIM_SCENE(_scene);
            _step = 1; _stepMs = now;
            CoopScript.Log($"cutscene test: {dict} list {_list} created ({_scene}), player bound {_bindPlayer}, cast made: {_made.Count}, loading");
            CoopScript.Say("Co-op test: loading " + dict);
            return true;
        }

        public unsafe void Tick(long now)
        {
            if (_step == 0) return;
            if (!ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { CoopScript.Log("cutscene test: the scene is gone"); _step = 0; _scene = 0; MissionProbe.TestScene = 0; return; }
            if (_step == 1)
            {
                if (ANIMSCENE.IS_ANIM_SCENE_LOADED(_scene, true, false))
                {
                    var pos = new RDR2.Math.Vector3(); var rot = new RDR2.Math.Vector3();
                    ANIMSCENE.GET_ANIM_SCENE_ORIGIN(_scene, &pos, &rot, 2);
                    CoopScript.Log($"cutscene test: loaded after {now - _stepMs} ms, duration {ANIMSCENE._GET_ANIM_SCENE_DURATION(_scene):0.0}, origin {pos} rot {rot}, play list exists {ANIMSCENE._DOES_ANIM_SCENE_PLAY_LIST_EXIST(_scene, _list)}");
                    ANIMSCENE.START_ANIM_SCENE(_scene);
                    if (_asStory) { MissionProbe.TestScene = _scene; MissionProbe.TestLabel = _label; MissionProbe.TestMission = _mission; }
                    _step = 2; _stepMs = now; _lastLogMs = 0;
                }
                else if (now - _stepMs > 30000) Stop("didn't load in 30 s");
                return;
            }
            if (now - _lastLogMs >= 1000)
            {
                _lastLogMs = now;
                var parts = _names.Select(n =>
                {
                    int ped = ANIMSCENE._GET_ANIM_SCENE_PED(_scene, n, false), veh = ANIMSCENE._GET_ANIM_SCENE_VEHICLE(_scene, n, false), obj = ANIMSCENE._GET_ANIM_SCENE_OBJECT(_scene, n, false);
                    int h = ped != 0 ? ped : veh != 0 ? veh : obj;
                    return h == 0 ? $"{n}:-" : $"{n}:{h}{(h == PLAYER.PLAYER_PED_ID() ? "(me)" : "")} {ENTITY.GET_ENTITY_MODEL(h):X8} at {ENTITY.GET_ENTITY_COORDS(h, false, false)}";
                });
                CoopScript.Log($"cutscene test: t {ANIMSCENE._GET_ANIM_SCENE_TIME(_scene):0.0} phase {ANIMSCENE.GET_ANIM_SCENE_PHASE(_scene):0.00} running {ANIMSCENE.IS_ANIM_SCENE_RUNNING(_scene, false)} " +
                               $"finished {ANIMSCENE.IS_ANIM_SCENE_FINISHED(_scene, false)} cams {ANIMSCENE.GET_ANIM_SCENE_CURRENT_ACTIVE_CAMERA_COUNT(_scene)} | {string.Join("; ", parts)}");
            }
            if (ANIMSCENE.IS_ANIM_SCENE_FINISHED(_scene, false) || ANIMSCENE.HAS_ANIM_SCENE_EXITED(_scene, false)) Stop($"finished after {now - _stepMs} ms");
            else if (now - _stepMs > 300000) Stop("still running after 5 minutes");
        }

        unsafe void Stop(string why)
        {
            CoopScript.Log("cutscene test: " + why);
            CoopScript.Say("Co-op test: cutscene " + why);
            if (_scene != 0 && ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { ANIMSCENE.ABORT_ANIM_SCENE(_scene, false); ANIMSCENE._DELETE_ANIM_SCENE(_scene); }
            _scene = 0; _step = 0;
            MissionProbe.TestScene = 0;
            foreach (int p in _made) { int h = p; ENTITY.SET_ENTITY_AS_MISSION_ENTITY(h, true, true); ENTITY.DELETE_ENTITY(&h); }
            _made.Clear();
        }
    }
}
