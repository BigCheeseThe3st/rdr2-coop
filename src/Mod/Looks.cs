// A player's look as the list of meta-ped pieces (model parts, textures,
// colours) the game shows, captured from our character and put on puppets.
using System.Collections.Generic;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Looks
    {
        public static unsafe Appearance Capture(int ped)
        {
            var a = new Appearance { Model = ENTITY.GET_ENTITY_MODEL(ped), Tags = new List<MetaTag>() };
            int n = System.Math.Min(PED._GET_NUM_COMPONENTS_IN_PED(ped), Appearance.MaxTags);
            for (int i = 0; i < n; i++)
            {
                uint drawable = 0, albedo = 0, normal = 0, material = 0, palette = 0;
                int t0 = 0, t1 = 0, t2 = 0;
                if (!PED.GET_META_PED_ASSET_GUIDS(ped, i, &drawable, &albedo, &normal, &material)) continue;
                PED.GET_META_PED_ASSET_TINT(ped, i, &palette, &t0, &t1, &t2);
                a.Tags.Add(new MetaTag
                {
                    Drawable = drawable, Albedo = albedo, Normal = normal, Material = material, Palette = palette,
                    Tint0 = (byte)t0, Tint1 = (byte)t1, Tint2 = (byte)t2,
                });
            }
            return a;
        }

        // Replaces the puppet's random outfit with the player's pieces.
        public static void Apply(int ped, Appearance a)
        {
            if (a.Tags == null || a.Tags.Count == 0) return;
            PED._RESET_PED_COMPONENTS(ped);
            foreach (var t in a.Tags)
                PED._SET_META_PED_TAG(ped, t.Drawable, t.Albedo, t.Normal, t.Material, t.Palette, t.Tint0, t.Tint1, t.Tint2);
            PED._UPDATE_PED_VARIATION(ped, false, true, true, true, false);
        }

        // NPCs wear named outfits: put the same one on a copy.
        public static void EquipOutfit(int ped, uint outfit)
        {
            if (outfit == 0) return;
            PED._EQUIP_META_PED_OUTFIT(ped, outfit);
            PED._UPDATE_PED_VARIATION(ped, false, true, true, true, false);
        }
    }
}
