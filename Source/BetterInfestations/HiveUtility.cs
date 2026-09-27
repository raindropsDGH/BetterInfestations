using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace BetterInfestations
{
    public static class HiveUtility
    {
        public static int TotalSpawnedHivesCount(Map map)
        {
            return map.listerThings.ThingsOfDef(ThingDefOf.BI_Hive).Count;
        }
        public static bool AnyHivePreventsClaiming(Thing thing)
        {
            if (!thing.Spawned) return false;

            int num = GenRadial.NumCellsInRadius(2f);
            for (int i = 0; i < num; i++)
            {
                IntVec3 c = thing.Position + GenRadial.RadialPattern[i];
                if (c.InBounds(thing.Map) && c.GetFirstThing(thing.Map, thing.def) != null) return true;
            }
            return false;
        }
        public static void NotifyAttackedNearbyHives(Hive hive, Lord lord, string memo)
        {
            if (hive != null && hive.Spawned)
            {
                if (lord != null)
                {
                    lord.ReceiveMemo(memo);
                }
                foreach (Thing thing in hive.Map.listerThings.ThingsOfDef(ThingDefOf.BI_Hive))
                {
                    Hive otherHive = thing as Hive;
                    if (otherHive != null && hive != otherHive)
                    {
                        if (IntVec3Utility.ManhattanDistanceFlat(hive.Position, otherHive.Position) <= 24)
                        {
                            if (otherHive.CompSpawnerPawns != null)
                            {
                                Lord lord2 = otherHive.CompSpawnerPawns.Lord[0];
                                if (lord2 != null)
                                {
                                    lord.ReceiveMemo(memo);
                                }
                            }
                        }
                    }
                }
            }
        }
        public static Hive GetHive(Pawn pawn)
        {
            if (!pawn.DestroyedOrNull() && pawn.Spawned)
            {
                HiveData_MapComponent mapHiveData = pawn.Map.GetComponent<HiveData_MapComponent>();
                if (mapHiveData == null) return null;

                mapHiveData.pawnToHiveDict.TryGetValue(pawn, out Hive hive);
                return hive;
            }
            return null;
        }
        public static Hive GetLordHive(Lord lord)
        {
            if (lord == null) return null;

            foreach (Thing thing in lord.Map.listerThings.ThingsOfDef(ThingDefOf.BI_Hive))
            {
                Hive hive = thing as Hive;
                if (hive != null)
                {
                    CompSpawnerPawns comp = hive.CompSpawnerPawns;
                    if (comp == null) return null;

                    for (int i = 0; i < 3; i++)
                    {
                        if (comp.Lord[i] == lord)
                        {
                            return hive;
                        }
                    }
                }
            }
            return null;
        }
        public static bool PawnFromHive(Hive hive, Pawn pawn)
        {
            if (hive.DestroyedOrNull() || pawn.DestroyedOrNull()) return false;

            for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
            {
                if (hive.CompSpawnerPawns.spawnedPawns[i].Contains(pawn))
                {
                    return true;
                }
            }

            return false;
        }
        public static bool WithinHive(Pawn pawn, Thing thing, bool NearOtherHive)
        {
            if (pawn == null || thing == null) return false;

            HiveData_MapComponent mapHiveData = pawn.Map.GetComponent<HiveData_MapComponent>();

            if (mapHiveData.pawnToHiveDict.TryGetValue(pawn, out Hive hive))
            {
                if (thing.Position.DistanceTo(hive.Position) <= 8) return true;
            }
            if (NearOtherHive)
            {
                if (mapHiveData.withinHiveGrid[thing.Position] == true) return true;
            }
            return false;
        }
        public static HashSet<Pawn> AllHivePawns(Hive hive)
        {
            if (hive == null) return null;

            HashSet<Pawn> pawns = new HashSet<Pawn>();
            for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
            {
                pawns.AddRange(hive.CompSpawnerPawns.spawnedPawns[i]);
            }
            return pawns;
        }
        public static void CallReinforcements(Pawn pawn, Thing thing)
        {
            foreach (Pawn p in pawn.Map.mapPawns.PawnsInFaction(Faction.OfInsects))
            {
                if (!p.Downed && p.CurJob != null && p.CurJob.def != RimWorld.JobDefOf.AttackMelee && !JobsGivenRecentTick(p, "AttackMelee"))
                {
                    if (IntVec3Utility.ManhattanDistanceFlat(p.Position, pawn.Position) <= 8)
                    {
                        Job job = new Job(RimWorld.JobDefOf.AttackMelee, thing);
                        job.canBashDoors = true;
                        job.canBashFences = true;
                        job.attackDoorIfTargetLost = true;
                        p.jobs.StartJob(job, JobCondition.InterruptForced);
                        Hive hive = GetHive(p);
                        if (hive != null)
                        {
                            SetPatrolSpot(p, hive, thing.Position, LocomotionUrgency.Sprint);
                        }
                    }
                }
            }
        }
        public static IntVec3 GetPatrolSpot(Pawn pawn, Hive hive, out LocomotionUrgency locomotionUrgency)
        {
            if (!pawn.DestroyedOrNull() && !hive.DestroyedOrNull())
            {
                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    if (hive.CompSpawnerPawns.spawnedPawns[i].Contains(pawn))
                    {
                        locomotionUrgency = hive.CompSpawnerPawns.patrolLocomotion[i];
                        return hive.CompSpawnerPawns.patrolLoc[i];
                    }
                }
            }
            locomotionUrgency = LocomotionUrgency.Walk;
            return IntVec3.Invalid;
        }
        public static void SetPatrolSpot(Pawn pawn, Hive hive, IntVec3 cell, LocomotionUrgency locomotionUrgency)
        {
            if (pawn != null && hive != null)
            {
                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    if (hive.CompSpawnerPawns.spawnedPawns[i].Contains(pawn))
                    {
                        hive.CompSpawnerPawns.patrolLoc[i] = cell;
                        hive.CompSpawnerPawns.patrolLocomotion[i] = locomotionUrgency;
                        return;
                    }
                }
            }
        }
        public static Thing GetAttackTarget(Pawn pawn, Hive hive)
        {
            if (pawn != null && hive != null)
            {
                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    foreach (Pawn p in hive.CompSpawnerPawns.spawnedPawns[i])
                    {
                        if (p == pawn) return hive.CompSpawnerPawns.attackTarget[i];
                    }
                }
            }
            return null;
        }
        public static void SetAttackTarget(Pawn pawn, Hive hive, Thing thing)
        {
            if (pawn != null && hive != null)
            {
                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    foreach (Pawn p in hive.CompSpawnerPawns.spawnedPawns[i])
                    {
                        if (p == pawn)
                        {
                            hive.CompSpawnerPawns.attackTarget[i] = thing;
                            return;
                        }
                    }
                }
            }
        }
        public static float GetGroupStrength(Pawn pawn, Hive hive)
        {
            if (pawn != null && hive != null)
            {
                float points = 0;
                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    foreach (Pawn p in hive.CompSpawnerPawns.spawnedPawns[i])
                    {
                        if (p.jobs.posture != PawnPosture.LayingOnGroundNormal && p.mindState != null && p.mindState.duty != null)
                        {
                            points += p.kindDef.combatPower;
                        }
                    }
                }
                return points;
            }
            return 0f;
        }
        public static IntVec3 GetColonyStockpileSpot(Map map)
        {
            // not implemented

            return IntVec3.Invalid;
        }
        public static IntVec3 FindPathToPrey(Pawn pawn)
        {
            if (pawn != null && pawn.Downed) return IntVec3.Invalid;
            List<Thing> targetList = new List<Thing>();

            foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                if (ValidatorUtility.itemValidator(pawn, true, false)(t)) targetList.Add(t);
            }
            foreach (Pawn p in pawn.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (ValidatorUtility.pawnValidator(pawn, true, false, false, true, false)(p)) targetList.Add(p);
            }
            foreach (Corpse c in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                if (ValidatorUtility.corpseValidator(pawn, true, false)(c)) targetList.Add(c);
            }

            if (targetList.NullOrEmpty()) return IntVec3.Invalid;

            Thing result = null;

            //Log.Message($"Finding thing in list of {targetList.Count}");
            result = GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, targetList, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassAllDestroyableThings, Danger.Deadly, false), pawn.Map.Size.LengthHorizontal);
            if (result == null) return IntVec3.Invalid;

            //Log.Message($"Finding path to {result.ThingID}");
            using (PawnPath pawnPath = pawn.Map.pathFinder.FindPathNow(start: pawn.Position, target: result.Position, traverseParms: TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings, false), peMode: PathEndMode.OnCell))
            {
                List<IntVec3> cells = pawnPath.NodesReversed;
                if (!cells.NullOrEmpty())
                {
                    //Log.Message(cells.Count);
                    foreach (IntVec3 cell in cells)
                    {
                        if (IntVec3Utility.ManhattanDistanceFlat(pawn.Position, cell) <= 24)
                        {
                            return cell;
                        }
                    }
                }
            }
            //Log.Message($"No path found!");

            return IntVec3.Invalid;
        }
        public static bool JobsGivenRecentTick(Pawn pawn, string JobName)
        {
            FieldInfo FI_jobsGivenRecentTicksTextual = typeof(Pawn_JobTracker).GetField("jobsGivenRecentTicksTextual", Patches.allFlags);
            List<string> jobs = (List<string>)FI_jobsGivenRecentTicksTextual.GetValue(pawn.jobs);
            if (!jobs.NullOrEmpty())
            {
                foreach (string job in jobs)
                {
                    if (job.Contains(JobName)) return true;
                }
            }
            FieldInfo FI_jobsGivenThisTickTextual = typeof(Pawn_JobTracker).GetField("jobsGivenThisTickTextual", Patches.allFlags);
            string job2 = (string)FI_jobsGivenThisTickTextual.GetValue(pawn.jobs);
            if (!job2.NullOrEmpty())
            {
                if (job2.Contains(JobName)) return true;
            }
            return false;
        }
        public static bool AttackTargetTooFarAway(Pawn pawn, Thing thing)
        {
            if (pawn != null && thing != null)
            {
                Hive hive = GetHive(pawn);
                if (hive == null) return false;

                for (int i = 0; i < hive.CompSpawnerPawns.spawnedPawns.Length; i++)
                {
                    foreach (Pawn p in hive.CompSpawnerPawns.spawnedPawns[i])
                    {
                        if (p == pawn)
                        {
                            bool closeToTarget = false;
                            foreach (Pawn pawn2 in hive.CompSpawnerPawns.spawnedPawns[i])
                            {
                                if (IntVec3Utility.ManhattanDistanceFlat(pawn2.Position, thing.Position) <= 12) closeToTarget = true;
                            }
                            if (!closeToTarget) return true;
                        }
                    }
                }
            }
            return false;
        }
        public static Thing GetFoodAtHive(Pawn pawn)
        {
            Hive hive = GetHive(pawn);
            if (hive == null) return null;

            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(hive.Position, hive.Rotation, new IntVec2(8, 8)))
            {
                IEnumerable<Thing> things = hive.Map.thingGrid.ThingsAt(cell);
                foreach (Thing thing in things)
                {
                    if (thing.def == RimWorld.ThingDefOf.InsectJelly && !thing.IsBurning() && !thing.Fogged() && pawn.CanReserve(thing, 1, 75) && pawn.CanReach(thing, PathEndMode.OnCell, Danger.Deadly, false, false, TraverseMode.NoPassClosedDoors)) return thing;
                }
            }
            return null;
        }
        public static bool QueenActive(Hive hive)
        {
            if (hive == null) return false;
            foreach (Pawn p in hive.CompSpawnerPawns.spawnedPawns[0])
            {
                Queen q = p as Queen;
                if (q != null && q.Spawned) return true;
            }
            return false;
        }

        public static void SpawnRandomCorpses(Hive hive)
        {
            if (hive == null) return;

            // --------------------------------------------------
            // Factions
            // --------------------------------------------------

            Faction pirateFaction = FactionByDef("Pirate");
            Faction mechanoidFaction = FactionByDef("Mechanoid");
            Faction tribeSavageFaction = FactionByDef("TribeSavage");
            Faction tribeRoughFaction = FactionByDef("TribeRough");
            Faction tribeCivilFaction = FactionByDef("TribeCivil");
            Faction outlanderRoughFaction = FactionByDef("OutlanderRough");
            Faction outlanderCivilFaction = FactionByDef("OutlanderCivil");

            // --------------------------------------------------
            // PawnKind groups (data-driven, mod-safe)
            // --------------------------------------------------

            var piratePawnKinds = PawnKinds(pk =>
                pk.defaultFactionDef?.defName == "Pirate" &&
                !pk.defName.Contains("Boss"));

            var mercenaryPawnKinds = PawnKinds(pk =>
                pk.defName.StartsWith("Mercenary_") ||
                pk.defName.StartsWith("Grenadier_"));

            var tribeSavagePawnKinds = PawnKinds(pk =>
                pk.defaultFactionDef?.defName == "TribeSavage");

            var tribeRoughPawnKinds = PawnKinds(pk =>
                pk.defaultFactionDef?.defName == "TribeRough");

            var tribeCivilPawnKinds = PawnKinds(pk =>
                pk.defaultFactionDef?.defName == "TribeCivil");

            var outlanderPawnKinds = PawnKinds(pk =>
                pk.defaultFactionDef?.defName?.StartsWith("Outlander") == true);

            var mechanoidPawnKinds = PawnKinds(pk =>
                pk.RaceProps?.IsMechanoid == true);

            // Explicit special roles
            PawnKindDef pirateBoss = DefDatabase<PawnKindDef>.GetNamedSilentFail("PirateBoss");
            PawnKindDef tribalChiefMelee = DefDatabase<PawnKindDef>.GetNamedSilentFail("Tribal_ChiefMelee");
            PawnKindDef tribalChiefRanged = DefDatabase<PawnKindDef>.GetNamedSilentFail("Tribal_ChiefRanged");

            // --------------------------------------------------
            // Choose two distinct factions (original intent)
            // --------------------------------------------------

            var possibleFactions = new List<Faction>
        {
            null,
            null,
            Faction.OfAncients,
            Faction.OfAncientsHostile,
            mechanoidFaction,
            pirateFaction,
            pirateFaction,
            tribeRoughFaction,
            tribeSavageFaction,
            tribeCivilFaction,
            outlanderRoughFaction,
            outlanderCivilFaction
        };

            Faction faction1 = possibleFactions.RandomElement();
            possibleFactions.Remove(faction1);
            Faction faction2 = possibleFactions.RandomElement();

            // --------------------------------------------------
            // Ancients
            // --------------------------------------------------

            TryAncients(hive, faction1, faction2, Faction.OfAncients);
            TryAncients(hive, faction1, faction2, Faction.OfAncientsHostile);

            // --------------------------------------------------
            // Mechanoids
            // --------------------------------------------------

            TryFactionGroup(
                hive,
                mechanoidFaction,
                mechanoidPawnKinds,
                faction1,
                faction2,
                factionChance: 0.30f);

            // --------------------------------------------------
            // Pirates
            // --------------------------------------------------

            if (Rand.Chance(0.40f) && faction1 == pirateFaction)
            {
                SpawnGroup(hive, pirateFaction, piratePawnKinds, 3, 0.5f);
                TrySpawn(pirateBoss, pirateFaction, hive, 0.15f, IntRange.One);
            }

            if (Rand.Chance(0.40f) && faction2 == pirateFaction)
            {
                SpawnGroup(hive, pirateFaction, mercenaryPawnKinds, 3, 0.5f);
            }

            // --------------------------------------------------
            // Tribes / Outlanders
            // --------------------------------------------------

            TryFactionGroup(hive, tribeRoughFaction, tribeRoughPawnKinds, faction1, faction2, 0.25f);
            TryFactionGroup(hive, tribeSavageFaction, tribeSavagePawnKinds, faction1, faction2, 0.25f, tribalChiefMelee, tribalChiefRanged);
            TryFactionGroup(hive, tribeCivilFaction, tribeCivilPawnKinds, faction1, faction2, 0.25f);
            TryFactionGroup(hive, outlanderRoughFaction, outlanderPawnKinds, faction1, faction2, 0.25f);
            TryFactionGroup(hive, outlanderCivilFaction, outlanderPawnKinds, faction1, faction2, 0.25f);

            // --------------------------------------------------
            // Civilians / refugees
            // --------------------------------------------------

            if (Rand.Chance(0.30f) && faction1 == null)
                SpawnGroup(hive, null, PawnKindDefOf.Drifter, 3, 0.5f);

            if (Rand.Chance(0.30f) && faction2 == null)
                SpawnGroup(hive, null, PawnKindDefOf.SpaceRefugee, 3, 0.5f);
        }


        // =====================================================================
        // Helpers
        // =====================================================================

        static Faction FactionByDef(string defName)
            => Find.FactionManager.AllFactions.FirstOrDefault(f => f.def.defName == defName);

        static List<PawnKindDef> PawnKinds(System.Func<PawnKindDef, bool> selector)
            => DefDatabase<PawnKindDef>.AllDefsListForReading.Where(selector).ToList();

        static void TryAncients(Hive hive, Faction f1, Faction f2, Faction ancients)
        {
            if (!Rand.Chance(0.15f) || (f1 != ancients && f2 != ancients))
                return;

            for (int i = 0; i < 3; i++)
                TrySpawn(PawnKindDefOf.AncientSoldier, ancients, hive, 0.5f, new IntRange(1, 6));
        }

        static void TryFactionGroup(
            Hive hive,
            Faction faction,
            List<PawnKindDef> pawnKinds,
            Faction f1,
            Faction f2,
            float factionChance,
            params PawnKindDef[] specialLeaders)
        {
            if (faction == null || pawnKinds.NullOrEmpty()) return;
            if (!Rand.Chance(factionChance) || (f1 != faction && f2 != faction))
                return;

            SpawnGroup(hive, faction, pawnKinds, 3, 0.5f);

            foreach (var leader in specialLeaders)
                TrySpawn(leader, faction, hive, 0.15f, IntRange.One);
        }

        static void SpawnGroup(
            Hive hive,
            Faction faction,
            List<PawnKindDef> pawnKinds,
            int rolls,
            float chancePerRoll)
        {
            for (int i = 0; i < rolls; i++)
                TrySpawn(pawnKinds.RandomElement(), faction, hive, chancePerRoll, new IntRange(1, 6));
        }

        static void SpawnGroup(
            Hive hive,
            Faction faction,
            PawnKindDef kind,
            int rolls,
            float chancePerRoll)
        {
            for (int i = 0; i < rolls; i++)
                TrySpawn(kind, faction, hive, chancePerRoll, new IntRange(1, 6));
        }

        static void TrySpawn(
            PawnKindDef kind,
            Faction faction,
            Hive hive,
            float chance,
            IntRange count)
        {
            if (kind == null || hive == null || !Rand.Chance(chance))
                return;

            SpawnCorpsesNearHive(kind, faction, count.RandomInRange, hive);
        }



        public class HiveLootEntry
        {
            public Func<ThingDef, bool> Selector;
            public float Weight;
            public IntRange CountRange = IntRange.One;
        }

        public static bool TrySpawnFromPool(Hive hive, IEnumerable<HiveLootEntry> entries, float rollChance)
        {
            if (hive == null || !Rand.Chance(rollChance))
                return false;

            var valid = new List<(ThingDef def, HiveLootEntry entry)>();

            foreach (var entry in entries)
            {
                foreach (var def in DefDatabase<ThingDef>.AllDefs)
                {
                    if (entry.Selector(def))
                        valid.Add((def, entry));
                }
            }

            if (valid.Count == 0)
                return false;

            var chosen = valid.RandomElementByWeight(v => v.entry.Weight);
            SpawnItemsNearHive(
                chosen.def,
                chosen.entry.CountRange.RandomInRange,
                hive);

            return true;
        }


        static readonly List<HiveLootEntry> WeaponLoot = new()
{
    new HiveLootEntry
    {
        Selector = def =>
            def.IsWeapon &&
            def.weaponTags?.Contains("Gun") == true,
        Weight = 12f
    },
    new HiveLootEntry
    {
        Selector = def =>
            def.IsWeapon &&
            def.weaponTags?.Contains("IndustrialGunAdvanced") == true,
        Weight = 6f
    },
    new HiveLootEntry
    {
        Selector = def =>
            def.IsWeapon &&
            def.weaponTags?.Contains("SpacerGun") == true,
        Weight = 3f
    },
     new HiveLootEntry
    {
        Selector = def =>
            def.IsWeapon &&
            def.weaponTags?.Contains("Zal") == true,
        Weight = 1f
    }
};

        static readonly List<HiveLootEntry> ApparelLoot = new()
{
    new HiveLootEntry
    {
        Selector = def =>
            def.IsApparel &&
            def.apparel?.layers.Contains(ApparelLayerDefOf.Overhead) == true,
        Weight = 8f
    },
    new HiveLootEntry
    {
        Selector = def =>
            def.IsApparel &&
            (def.tradeTags?.Contains("Armor") == true || def.tradeTags?.Contains("HiTechArmor") == true),
        Weight = 5f
    },
    new HiveLootEntry
    {
        Selector = def =>
            def.IsApparel &&
            def.apparel?.layers.Contains(ApparelLayerDefOf.Overhead) == true &&
            (def.tradeTags?.Contains("Armor") == true || def.tradeTags?.Contains("HiTechArmor") == true),
        Weight = 5f
    }
};

        static readonly List<HiveLootEntry> ResourceLoot = new()
{
    new HiveLootEntry
    {
        Selector = def =>
            def.IsStuff && def.tradeTags?.Contains("LJO_JoyItem") == false &&
            def.BaseMarketValue > 5f && def.techLevel < TechLevel.Archotech,
        Weight = 15f,
        CountRange = new IntRange(2, 5)
    },
     new HiveLootEntry
    {
        Selector = def =>
            def.defName == "Silver",
        Weight = 6f,
        CountRange = new IntRange(3, 5)
    },
     new HiveLootEntry
    {
        Selector = def =>
            def.defName == "Gold",
        Weight = 3f,
        CountRange = new IntRange(1, 3)
    },
};


        static readonly List<HiveLootEntry> ExoticLoot = new()
        {
        new HiveLootEntry
    {
        Selector = def =>
            def.tradeTags?.Contains("ExoticMisc") == true,
        Weight = 5f,
        CountRange = new IntRange(1, 2)
    },
    new HiveLootEntry
    {
        Selector = def =>
            def.tradeTags?.Contains("Artifact") == true,
        Weight = 1f,
        CountRange = new IntRange(1, 2)
    }
        };

        public static void SpawnRandomItems(Hive hive)
        {
            TrySpawnFromPool(hive, WeaponLoot, 0.40f);
            TrySpawnFromPool(hive, ApparelLoot, 0.40f);
            TrySpawnFromPool(hive, ResourceLoot, 0.40f);
            TrySpawnFromPool(hive, ExoticLoot, 0.40f);
        }

        public static void SpawnCorpsesNearHive(PawnKindDef pawnKindDef, Faction faction, int num, Hive hive)
        {
            if (pawnKindDef == null || hive == null) return;

            for (int i = 0; i < num; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKindDef, faction, PawnGenerationContext.NonPlayer, -1, true));
                if (pawn != null)
                {
                    IntVec3 c = CellFinder.RandomClosewalkCellNear(hive.Position, hive.Map, 12);
                    GenSpawn.Spawn(pawn, c, hive.Map);
                    pawn.Kill(null);
                    foreach (Thing thing in hive.Map.thingGrid.ThingsAt(c))
                    {
                        Corpse corpse = thing as Corpse;
                        if (corpse != null)
                        {
                            CompRottable compRottable = corpse.GetComp<CompRottable>();
                            if (compRottable != null)
                            {
                                compRottable.RotProgress = 999999;
                            }
                        }
                    }
                }
            }
        }
        public static void SpawnItemsNearHive(ThingDef thingDef, int numOfStacks, Hive hive)
        {
            if (thingDef == null || hive == null) return;

            IntVec3 c = CellFinder.RandomClosewalkCellNear(hive.Position, hive.Map, 12);
            for (int i = 0; i < numOfStacks; i++)
            {
                ThingDef stuff = null;
                if (thingDef.MadeFromStuff)
                {
                    if (Rand.Range(1, 100) <= 30) stuff = RimWorld.ThingDefOf.Plasteel;
                    else stuff = RimWorld.ThingDefOf.Steel;
                }
                Thing thing = GenSpawn.Spawn(ThingMaker.MakeThing(thingDef, stuff), c, hive.Map);
                if (thing != null)
                {
                    thing.stackCount = Rand.Range(1, thing.def.stackLimit);
                }
            }
        }
        public static bool SwarmingHordeCheck(Map map)
        {
            if (map != null)
            {
                int count = 0;
                foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.BI_Hive))
                {
                    foreach (Pawn p in AllHivePawns(t as Hive))
                    {
                        Lord lord = p.GetLord();
                        if (!map.IsPlayerHome || (lord != null && lord.LordJob != null && lord.LordJob.GetType() == typeof(LordJob_AssaultColony)))
                        {
                            return false;
                        }
                        count++;
                    }
                    if (count >= 150) return true;
                }
            }
            return false;
        }
        public static Thing ExecuteSwarmingHorde(Map map)
        {
            if (map != null)
            {
                Thing result = null;
                Lord lord = LordMaker.MakeNewLord(Faction.OfInsects, Activator.CreateInstance(typeof(LordJob_AssaultColony), null) as LordJob, map);
                foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.BI_Hive))
                {
                    foreach (Pawn p in AllHivePawns(t as Hive))
                    {
                        Queen q = p as Queen;
                        if ((q != null && Rand.Range(1,100) <= 25) || (q == null && Rand.Range(1,100) <= 75))
                        {
                            Lord lord2 = p.GetLord();
                            if (lord != null && lord2 != null)
                            {
                                lord2.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
                                lord.AddPawn(p);
                                result = p as Thing;
                            }
                        }
                    }
                }
                return result;
            }
            return null;
        }
    }
}