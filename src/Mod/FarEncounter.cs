// A nemesis from the host's enRage world coming for us while we're far from
// the host: the host's enRage picks him and sends a plan (model, outfit,
// weapons, crew); we stage him in our own area behind us, on horseback, and
// tell the host how it ended so enRage applies its own rules (cheating death,
// scars, rank). There's no enRage in our game, so the plan carries everything.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class FarEncounter
    {
        const int GiveUpMs = 8 * 60 * 1000;
        const float SlippedAway = 350f;
        const string Horse = "A_C_Horse_KentuckySaddle_Black";

        string _id, _name;
        int _leader;
        readonly List<int> _peds = new List<int>();   // leader, crew and their horses
        readonly List<int> _blips = new List<int>();
        long _since;
        uint _group;

        public bool Busy => _id != null;

        public void OnPlan(byte from, string text, ServerLink link, long now)
        {
            var f = Fields(text);
            if (Busy || Activities.Busy || !f.ContainsKey("id")) { Report(link, from, f.TryGetValue("id", out var i) ? i : "", "failed", false, false); return; }
            var me = Game.Player.Character;
            if (!TrySpawn(f, me, out string why))
            {
                CoopScript.Log("far encounter not staged: " + why);
                Report(link, from, f["id"], "failed", false, false);
                Clear();
                return;
            }
            _id = f["id"];
            _name = f.TryGetValue("name", out var n) ? n : "A nemesis";
            _since = now;
            RDR2.UI.Screen.DisplaySubtitle(_name + " has come for you.");
            CoopScript.Log($"far encounter: {_name} ({_id}) with {_peds.Count} peds");
        }

        public void Tick(ServerLink link, bool downed, long now)
        {
            if (!Busy) return;
            var me = Game.Player.Character;
            string outcome = null;
            bool byMe = false, headshot = false;
            if (!Peds.Exists(_leader)) outcome = "slipped";
            else if (ENTITY.IS_ENTITY_DEAD(_leader))
            {
                outcome = "killed";
                int src = PED.GET_PED_SOURCE_OF_DEATH(_leader);
                byMe = src == me.Handle || (me.IsOnMount && me.CurrentMount != null && src == me.CurrentMount.Handle);
                headshot = LastBone(_leader) == Hit.HeadBone;
            }
            else if (downed || me.IsDead) outcome = "beat";
            else if (Peds.Position(_leader).DistanceTo(me.Position) > SlippedAway || now - _since > GiveUpMs) outcome = "slipped";
            if (outcome == null) return;
            Report(link, link.HostId, _id, outcome, byMe, headshot);
            CoopScript.Log($"far encounter {_name}: {outcome}{(byMe ? " by us" : "")}{(headshot ? " (headshot)" : "")}");
            Clear();
        }

        // Peds left to the game (they wander off or despawn), never deleted in view.
        public unsafe void Clear()
        {
            foreach (int p in _peds)
            {
                int h = p;
                if (Peds.Exists(h)) ENTITY.SET_ENTITY_AS_NO_LONGER_NEEDED(&h);
            }
            _peds.Clear();
            foreach (int b in _blips) { int x = b; Blips.Remove(ref x); }
            _blips.Clear();
            _leader = 0;
            _id = null;
        }

        void Report(ServerLink link, byte to, string id, string outcome, bool byMe, bool headshot)
        {
            var at = Game.Player.Character.Position;
            link.SendNotice(to, NoticeKind.EncounterResult, string.Join("\n",
                "id=" + id, "outcome=" + outcome, "byGuest=" + (byMe ? "1" : "0"), "headshot=" + (headshot ? "1" : "0"),
                "x=" + at.X.ToString(CultureInfo.InvariantCulture), "y=" + at.Y.ToString(CultureInfo.InvariantCulture),
                "z=" + at.Z.ToString(CultureInfo.InvariantCulture)));
        }

        unsafe bool TrySpawn(Dictionary<string, string> f, Ped me, out string why)
        {
            why = null;
            uint model = UInt(f, "model"), outfit = UInt(f, "outfit"), hand = UInt(f, "hand"), longGun = UInt(f, "long");
            int crew = Math.Max(0, Math.Min(4, Int(f, "crew"))), health = Math.Max(100, Math.Min(1000, Int(f, "health")));
            int accuracy = Math.Max(10, Math.Min(80, Int(f, "accuracy")));
            if (!STREAMING.IS_MODEL_A_PED(model)) { why = "not a person model"; return false; }

            // Riders come up from behind, on the nearest road if there is one.
            double r = me.Heading * Math.PI / 180.0;
            var behind = me.Position + new Vector3((float)Math.Sin(r) * 120f, (float)-Math.Cos(r) * 120f, 0f);
            float* node = stackalloc float[6];   // the game writes a script vector: x, y and z in 8-byte slots (24 bytes)
            bool road = PATHFIND.GET_CLOSEST_VEHICLE_NODE(behind, (Vector3*)node, 1, 3f, 0f);
            var at = new Vector3(node[0], node[2], node[4]);
            if (!road || at.DistanceTo(behind) > 80f) at = behind;
            float z = 0f;
            if (MISC.GET_GROUND_Z_FOR_3D_COORD(at.X, at.Y, at.Z + 50f, &z, false)) at.Z = z;

            if (_group == 0)
            {
                uint g = 0;
                PED.ADD_RELATIONSHIP_GROUP("COOP_NEMESIS", &g);
                uint player = MISC.GET_HASH_KEY("PLAYER");
                PED.SET_RELATIONSHIP_BETWEEN_GROUPS(5, g, player);   // hate, both ways
                PED.SET_RELATIONSHIP_BETWEEN_GROUPS(5, player, g);
                _group = g;
            }
            uint horse = MISC.GET_HASH_KEY(Horse);
            for (int i = 0; i <= crew; i++)
            {
                var spot = at + new Vector3(i * 2.5f - crew * 1.25f, i % 2 * 2f, 0f);
                int ped = Peds.Spawn(model, spot, me.Heading);
                if (ped == 0) { why = "couldn't spawn"; return i > 0; }
                _peds.Add(ped);
                if (i == 0) { _leader = ped; if (outfit != 0) Looks.EquipOutfit(ped, outfit); }
                PED.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS(ped, false);   // they fight like anyone else
                PED.SET_PED_RELATIONSHIP_GROUP_HASH(ped, _group);
                ENTITY.SET_ENTITY_MAX_HEALTH(ped, i == 0 ? health : 250);
                ENTITY.SET_ENTITY_HEALTH(ped, i == 0 ? health : 250, 0);
                PED.SET_PED_ACCURACY(ped, accuracy);
                foreach (uint w in new[] { longGun, hand })
                    if (w != 0 && WEAPON.IS_WEAPON_VALID(w))
                        WEAPON.GIVE_WEAPON_TO_PED(ped, w, 200, true, false, 0, false, 0.5f, 1f, 0, true, 0f, false);
                int mount = Peds.Spawn(horse, spot + new Vector3(0f, 0f, 0.5f), me.Heading);
                if (mount != 0) { _peds.Add(mount); PED.SET_PED_ONTO_MOUNT(ped, mount, -1, true); }
                TASK.TASK_COMBAT_PED(ped, me.Handle, 0, 16);
                int blip = MAP.BLIP_ADD_FOR_ENTITY(MISC.GET_HASH_KEY("BLIP_STYLE_ENEMY"), ped);
                if (blip != 0) _blips.Add(blip);
            }
            return true;
        }

        static unsafe int LastBone(int ped)
        {
            int bone = 0;
            return PED.GET_PED_LAST_DAMAGE_BONE(ped, &bone) ? bone : 0;
        }

        static Dictionary<string, string> Fields(string text) =>
            (text ?? "").Split('\n').Where(l => l.Contains("="))
                        .GroupBy(l => l.Substring(0, l.IndexOf('=')))
                        .ToDictionary(g => g.Key, g => g.First().Substring(g.First().IndexOf('=') + 1));

        static uint UInt(Dictionary<string, string> f, string k) =>
            f.TryGetValue(k, out var v) && uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : 0;

        static int Int(Dictionary<string, string> f, string k) =>
            f.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}
