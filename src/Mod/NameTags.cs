// Partners' names over their heads (0.8.0, Visuals.Names): drawn every frame in the
// game's font over each partner's stand-in that is on screen within NameTag.Range of
// you, smaller further away. Not during story cutscenes, nor while the screen is
// faded (mission transitions, loading, dying).
using System;
using System.Collections.Generic;
using System.Drawing;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class NameTags
    {
        static readonly Color Colour = Color.FromArgb(230, 255, 255, 255);

        public static unsafe void Draw(IDictionary<byte, Puppet> puppets, Func<byte, string> nameOf, Vector3 me)
        {
            if (puppets.Count == 0 || MissionProbe.InMissionScene || CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) return;
            foreach (var kv in puppets)
            {
                var p = kv.Value;
                if (!p.Exists) continue;
                var at = p.Position + new Vector3(0f, 0f, 1.1f);   // just over their head, on foot or riding
                float d = at.DistanceTo(me);
                if (d > NameTag.Range || !ENTITY.IS_ENTITY_ON_SCREEN(p.Handle)) continue;
                float x = 0f, y = 0f;
                if (!GRAPHICS.GET_SCREEN_COORD_FROM_WORLD_COORD(at, &x, &y)) continue;
                string name = NameTag.Clean(nameOf(kv.Key));
                float scale = NameTag.Scale(d);
                // Roughly centred over them, sitting on that point (text hangs down from y).
                Ui.Text(name, x - name.Length * 0.00875f * scale, y - 0.07f * scale, scale, Colour);
            }
        }
    }
}
