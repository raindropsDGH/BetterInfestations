using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace BetterInfestations
{
    public static class ValidatorUtility
    {
        public static Predicate<Thing> pawnValidator(Pawn pawn, bool factionCheck = false, bool lookForDownedOnly = false, bool lookWithinHive = false, bool careIfWithinHive = false, bool careIfDowned = false) => delegate (Thing t)
        {
            Pawn p = t as Pawn;
            if (p == null) return false;
            if (careIfDowned)
            {
                if (lookForDownedOnly && !p.Downed) return false;
                else if (!lookForDownedOnly && p.Downed) return false;
            }
            if (!p.RaceProps.IsFlesh) return false;
            if (p.RaceProps.DeathActionWorker.DangerousInMelee) return false;
            if (p.IsBurning() || p.Fogged()) return false;
            if (factionCheck)
            {
                // if has a faction that is not of insects,
                Faction targetFaction = p.Faction;
                if (targetFaction != null && (targetFaction == pawn.Faction || targetFaction.def.defName == "VFEI_Insect")) return false;
            }

            if (careIfWithinHive)
            {
                if (lookWithinHive)
                {
                    // if not in hive and desired, return false
                    if (!HiveUtility.WithinHive(pawn, p, true)) return false;
                }
                else
                {
                    // if in hive and not desired, return false
                    if (HiveUtility.WithinHive(pawn, p, true)) return false;
                }
            }

            if (!pawn.CanReserve(p)) return false;

            return true;
        };

        public static Predicate<Thing> corpseValidator(Pawn pawn, bool factionCheck = false, bool lookWithinHive = false) => delegate (Thing t)
        {
            Corpse c = t as Corpse;
            if (c == null) return false;
            if (c.InnerPawn == null) return false;
            if (!c.InnerPawn.RaceProps.IsFlesh) return false;
            if (c.GetRotStage() == RotStage.Dessicated) return false;
            if (c.IsBurning() || c.Fogged()) return false;

            if (factionCheck)
            {
                // if has a faction that is not of insects,
                Faction targetFaction = c.InnerPawn.Faction;
                if (targetFaction != null && (targetFaction == pawn.Faction || targetFaction.def.defName == "VFEI_Insect")) return false;
            }

            if (lookWithinHive)
            {
                // if not in hive and desired, return false
                if (!HiveUtility.WithinHive(pawn, c, true)) return false; 
            }
            else
            {
                // if in hive and not desired, return false
                if (HiveUtility.WithinHive(pawn, c, true)) return false;
            }
            if (!pawn.CanReserve(c)) return false;

            return true;
        };

        public static Predicate<Thing> itemValidator(Pawn pawn, bool skipJelly = true, bool lookWithinHive = false) => delegate (Thing t)
        {
            if (skipJelly) { if (t.def == RimWorld.ThingDefOf.InsectJelly) return false; }

            if (t == null) return false;
            if (t.def.category != ThingCategory.Item) return false;
            if (t.def.IsCorpse) return false;
            if (!t.IngestibleNow) return false;
            if (t.IsBurning() || t.Fogged()) return false;

            if (lookWithinHive)
            {
                // if not in hive and desired, return false
                if (!HiveUtility.WithinHive(pawn, t, true)) return false;
            }
            else
            {
                // if in hive and not desired, return false
                if (HiveUtility.WithinHive(pawn, t, true)) return false;
            }
            if (!pawn.CanReserve(t)) return false;

            return true;
        };
    }
}
