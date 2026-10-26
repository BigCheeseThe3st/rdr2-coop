// The mod's emote menu (0.9.0, [Convenience] EmoteMenu; src/Mod/EmoteMenu.cs plays them). Story mode has
// no emote wheel, so these are the game's own gestures that its story scripts play by name with
// TASK_PLAY_ANIM (each one checked in the decompiled 1491.50 scripts: the file named beside it), so every
// copy of the game has them. Partners get only an index into this list ("play <index>", NoticeKind.Emote),
// never an animation name from the wire. Append only: an index must mean the same emote to every version.
// Pure: no game calls.
using System;
using System.Globalization;

namespace RDR2Coop.Net
{
    public static class EmoteList
    {
        public struct Emote { public string Label, Dict, Clip; public int Ms; }   // Ms -1: the clip once through

        public const int Flags = 8 | 16;   // upper body, secondary: legs keep walking or riding (R*'s flags for the wave and the point)

        public static readonly Emote[] All =
        {
            new Emote { Label = "Wave", Dict = "script_common@wave@male@unapproved", Clip = "wave_idle_b", Ms = 3000 },              // mob1
            new Emote { Label = "Tip your hat", Dict = "AI_GESTURES@ARTHUR@STANDING@SPEAKER@LT_HAND", Clip = "greet_hat_tip_r_001", Ms = -1 },   // native1
            new Emote { Label = "Point", Dict = "script_re@lost_drunk@player_point", Clip = "FWD_LEFT", Ms = -1 },                   // act_fishing01
            new Emote { Label = "Come here", Dict = "SCRIPT_COMMON@GESTURES@UNAPPROVED", Clip = "Come_Here", Ms = -1 },              // mudtown5
            new Emote { Label = "Clap", Dict = "script_shows@show_audience@ig_arthur_applause", Clip = "idle_08_maryjohnapplause_standing", Ms = -1 },   // the theatre shows
            new Emote { Label = "Laugh", Dict = "script_re@duel_winner@taunt", Clip = "laughing_ped01", Ms = -1 },                  // beat_duel_winner
            new Emote { Label = "Yes (nod)", Dict = "AI_GESTURES@GEN_MALE@STANDING@SPEAKER", Clip = "positive_nod_f_001", Ms = -1 }, // mudtown3_outro
            new Emote { Label = "No (wave it off)", Dict = "AI_GESTURES@ARTHUR@STANDING@SPEAKER", Clip = "annoyed_dismiss_r_003", Ms = -1 },   // mob1
        };

        public static string Line(int index) => "play " + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>The emote a partner's notice asks for: "play &lt;index&gt;" and nothing else, else -1.</summary>
        public static int Parse(string text)
        {
            var t = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length != 2 || t[0] != "play" || t[1].Length > 3) return -1;
            return int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < All.Length ? i : -1;
        }

        /// <summary>What a notice asks for, in words (FakePlayer's console).</summary>
        public static string NameOf(string text) { int i = Parse(text); return i < 0 ? "(not an emote: " + text + ")" : All[i].Label; }
    }
}
