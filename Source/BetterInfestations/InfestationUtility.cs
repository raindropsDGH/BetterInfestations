using RimWorld;
using UnityEngine;
using Verse;

namespace BetterInfestations
{
    public static class InfestationUtility
    {
        public static readonly SimpleCurve GenPointsFactorCurve = new SimpleCurve
        {
           new CurvePoint(0f, 0.7f),
           new CurvePoint(2000f, 0.55f),
           new CurvePoint(5000f, 0.45f)
        };

        public static readonly SimpleCurve HiveTimeFactorCurveDays = new SimpleCurve
        {
           new CurvePoint(0f, 0.3f),
           new CurvePoint(3f, 0.6f),
           new CurvePoint(10f, 0.9f),
           new CurvePoint(15f, 1.0f)
        };

        public static float CalculateHiveTimeFactor(float timeDays)
        {
            float factor;

            if (timeDays > 15f) factor = 1.0f;
            else factor = HiveTimeFactorCurveDays.Evaluate(timeDays);

            return factor;
        }
        public static float CalculateThreat(float baseThreat)
        {
            //Log.Message($"[BI] Base threat points: {baseThreat}");

            float curvedThreat = baseThreat * GenPointsFactorCurve.Evaluate(baseThreat);

            //Log.Message($"[BI] Curved threat points: {curvedThreat}");

            float threatScale = Find.Storyteller.difficulty.threatScale;

            //Log.Message($"[BI] Threat scale: {threatScale}");

            float finalThreat = curvedThreat * threatScale;

            //Log.Message($"[BI] FINAL threat points: {finalThreat}");

            return finalThreat;
        }
        public static int CalculatePawnsPerHive(float threatPoints, float hiveCount)
        {

            float pawnPointsPerHive = threatPoints / hiveCount;

            int pawnCount = Mathf.RoundToInt(pawnPointsPerHive / 40f);
            pawnCount = Mathf.Clamp(pawnCount, 3, 15);
            return pawnCount;
        }
        public static int CalculateHiveCount(float threatPoints)
        {

            int baseHives = Mathf.Clamp(
                Mathf.RoundToInt(threatPoints / IncidentWorker_Infestation.HivePoints),
                1,
                BetterInfestationsMod.settings.maxHivesPerMap
            );

            return Rand.RangeInclusive(baseHives, baseHives + 1);
        }

        public static Thing SpawnTunnels(int hiveCount, Map map, bool spawnAnywhereIfNoGoodCell = false, string questTag = null)
        {
            IntVec3 loc;
            if (!Patches.Patch_InfestationCellFinder_TryFindCell.TryFindCell(out loc, map))
            {
                if (!spawnAnywhereIfNoGoodCell)
                {
                    return null;
                }
                if (!RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(delegate (IntVec3 x)
                {
                    if (!x.Standable(map))
                    {
                        return false;
                    }
                    return true;
                }, map, out loc))
                {

                    return null;
                }
            }
            Thing thing = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.BI_TunnelHiveSpawner, null), loc, map, WipeMode.FullRefund);
            QuestUtility.AddQuestTag(thing, questTag);
            for (int i = 0; i < hiveCount - 1; i++)
            {
                loc = CompSpawnerHives.FindChildHiveLocation(thing.Position, map, ThingDefOf.BI_Hive, ThingDefOf.BI_Hive.GetCompProperties<CompProperties_SpawnerHives>(), true, true);
                if (loc.IsValid)
                {
                    thing = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.BI_TunnelHiveSpawner, null), loc, map, WipeMode.FullRefund);
                    QuestUtility.AddQuestTag(thing, questTag);
                }
            }
            return thing;
        }
    }
}