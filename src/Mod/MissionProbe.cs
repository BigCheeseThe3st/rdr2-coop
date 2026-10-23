using System;
// Which story mission is running in this game, read from outside: each
// mission is its own game script, so a running thread of one of these names
// means that mission is on. Also checks once that global 35 holds the player's
// ped, as the public decompiled scripts for 1491.50 say, which tells us the
// globals in those scripts line up with the installed game.
using System.Collections.Generic;
using RDR2;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    sealed class MissionProbe
    {
        static readonly string[] Names =
        {
            "winter1", "winter2", "winter4", "mudtown1", "mudtown2", "mudtown3", "mudtown3b", "mudtown4", "mudtown5",
            "mary1", "mary3", "saloon1", "utopia1", "utopia2", "sean1", "bounty1", "reverend1", "hunting1", "feud1",
            "grays1", "grays2", "grays3", "braithwaites1", "braithwaites2", "braithwaites3", "trelawny1",
            "mob1", "mob2", "mob3", "mob4", "mob5", "odriscolls1", "odriscolls3", "odriscolls4", "odriscolls5",
            "industry1", "industry3", "saint_denis1", "susan1", "guama1", "guama2", "guama3", "fussar1", "fussar2",
            "smuggler2", "gang1", "gang2", "gang3", "cornwall1", "train_robbery1", "train_robbery2", "train_robbery3",
            "train_robbery4", "native1", "native2", "native3", "native_son1", "native_son2", "native_son3",
            "finale1", "finale2", "finale3", "marston1", "marston2", "marston4", "marston5_1", "marston5_2", "marston5_3",
            "laramie1", "marston6", "marston7", "marston8", "beechers2_2", "abigail2_1", "sadie2", "sadie3", "sadie4",
            "sadie5", "dreamanim",
        };

        readonly List<(string name, uint hash)> _missions = new List<(string, uint)>();
        long _lastCheckMs = -1000;
        bool _checkedGlobal;

        public string Running { get; private set; }   // null when no story mission runs

        // Global indices from the 1491.50 scripts (Global_N.f_k = N + k;
        // Global_N[i /*size*/].f_k = N + 1 + size * i + k). Only used once the
        // self-check says they fit this game.
        const int StoryMissionTable = 1835011, StoryMissionSize = 74;   // [79]: .f_3 script name
        const int StageInfo = 1572864;          // .f_8 mission record, .f_9 checkpoint, .f_10 next stage
        const int SceneHandle = 43800, SceneLabel = 43802;   // the anim scene story cutscenes play in

        public static bool GlobalsOk { get; private set; }
        public static bool InMissionScene { get; private set; }   // a story cutscene (anim scene) is playing
        public static bool StoryMission => _storyMission;
        static bool _storyMission;
        int _lastCheckpoint = -2;
        string _lastScene;
        const int PromptTable = 1945188, PromptSize = 18;   // [n]: .f_3 prompt, .f_4 control, .f_6 point, .f_9 radius, .f_11 ped
        long _lastPromptScan;
        string _lastPrompts = "";

        public MissionProbe()
        {
            foreach (var n in Names) _missions.Add((n, MISC.GET_HASH_KEY(n)));
        }

        // Returns true when the running mission changed.
        public bool Tick(long now)
        {
            if (!_checkedGlobal)
            {
                _checkedGlobal = true;
                int g = Game.GetGlobalPtr(35).GetInt(), me = PLAYER.PLAYER_PED_ID();
                string mud = GlobalText(StoryMissionTable + 1 + StoryMissionSize * 4 + 3, 32);
                GlobalsOk = g == me && mud == "mudtown1";
                CoopScript.Log($"script globals: global 35 = {g}, player ped = {me}, mission table [4] = '{mud}' -> " +
                               (GlobalsOk ? "decompiled 1491.50 globals match this game" : "globals DON'T match (different build?): global reads off"));
            }
            if (GlobalsOk) ReadStage();
            if (GlobalsOk && _storyMission && now - _lastPromptScan > 2000) { _lastPromptScan = now; ReadPrompts(); }
            if (now - _lastCheckMs < 1000) return false;
            _lastCheckMs = now;

            string running = null;
            foreach (var m in _missions)
                if (SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(m.hash) > 0) { running = m.name; break; }
            _storyMission = running != null;
            if (running == Running) return false;
            CoopScript.Log(running != null ? $"story mission running: {running}" : $"story mission ended: {Running}");
            Running = running;
            return true;
        }

        // Story cutscenes are anim scenes (not the game's "cutscene" system, so
        // Game.InCutscene misses them); the stage engine keeps its checkpoint in
        // globals. Read-only.
        void ReadStage()
        {
            int scene = Game.GetGlobalPtr(SceneHandle).GetInt();
            InMissionScene = scene != 0 && ANIMSCENE.DOES_ANIM_SCENE_EXIST(scene) && ANIMSCENE.IS_ANIM_SCENE_RUNNING(scene, false);
            string label = InMissionScene ? GlobalText(SceneLabel, 24) : null;
            if (label != _lastScene)
            {
                CoopScript.Log(label != null ? $"story cutscene playing: {label}" : $"story cutscene over: {_lastScene}");
                _lastScene = label;
            }
            int checkpoint = Game.GetGlobalPtr(StageInfo + 9).GetInt();
            if (checkpoint != _lastCheckpoint && checkpoint >= -1 && checkpoint < 64)
            {
                CoopScript.Log($"mission stage: record {Game.GetGlobalPtr(StageInfo + 8).GetInt()}, checkpoint {checkpoint}, next {Game.GetGlobalPtr(StageInfo + 10).GetInt()}");
                _lastCheckpoint = checkpoint;
            }
        }

        // Log-only for now: the mission's interaction prompts (talk, loot...)
        // as the shared prompt table holds them, to work out how a guest standing
        // at one could press it.
        void ReadPrompts()
        {
            int count = Math.Min(Game.GetGlobalPtr(PromptTable).GetInt(), 64);
            var parts = new List<string>();
            for (int n = 0; n < count; n++)
            {
                int b = PromptTable + 1 + PromptSize * n;
                int prompt = Game.GetGlobalPtr(b + 3).GetInt();
                if (prompt == 0 || !HUD._UI_PROMPT_IS_VALID(prompt)) continue;
                var at = Game.GetGlobalPtr(b + 6).GetVector3();
                parts.Add($"#{n} prompt {prompt} active {HUD._UI_PROMPT_IS_ACTIVE(prompt)} control {(uint)Game.GetGlobalPtr(b + 4).GetInt():X8} " +
                          $"at {at} r {Game.GetGlobalPtr(b + 9).GetFloat():0.0} ped {Game.GetGlobalPtr(b + 11).GetInt()}");
            }
            string line = string.Join("; ", parts);
            if (line == _lastPrompts) return;
            _lastPrompts = line;
            CoopScript.Log($"mission prompts ({parts.Count} of {count} slots): {line}");
        }

        // Text stored inline in globals (8-byte slots), as the scripts keep labels.
        static unsafe string GlobalText(int global, int maxBytes)
        {
            byte* p = (byte*)Game.GetGlobalPtr(global).MemoryAddress;
            if (p == null) return null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < maxBytes && p[i] != 0; i++) sb.Append((char)p[i]);
            return sb.ToString();
        }
    }
}
