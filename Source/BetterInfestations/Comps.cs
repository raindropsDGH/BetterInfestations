using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Noise;
using Verse.Sound;

namespace BetterInfestations
{
    public class CompProperties_Maintainable : CompProperties
    {
        public int ticksHealthy = 1000;
        public int ticksNeedsMaintenance = 1000;
        public int damagePerTickRare = 10;

        public CompProperties_Maintainable()
        {
            compClass = typeof(CompMaintainable);
        }
    }
    public class CompMaintainable : ThingComp
    {
        public int ticksSinceMaintain;
        public CompProperties_Maintainable Props => (CompProperties_Maintainable)props;
        public MaintainableStage CurStage
        {
            get
            {
                if (ticksSinceMaintain < Props.ticksHealthy)
                {
                    return MaintainableStage.Healthy;
                }
                if (ticksSinceMaintain < Props.ticksHealthy + Props.ticksNeedsMaintenance)
                {
                    return MaintainableStage.NeedsMaintenance;
                }
                return MaintainableStage.Damaging;
            }
        }
        private bool Active => (parent as Hive)?.CompDormant.Awake ?? true;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref ticksSinceMaintain, "ticksSinceMaintain", 0);
        }
        public override void CompTick()
        {
            base.CompTick();
            if (Active)
            {
                ticksSinceMaintain++;
                if (Find.TickManager.TicksGame % 250 == 0)
                {
                    CheckTakeDamage();
                }
            }
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (Active)
            {
                ticksSinceMaintain += 250;
                CheckTakeDamage();
            }
        }
        private void CheckTakeDamage()
        {
            if (CurStage == MaintainableStage.Damaging)
            {
                parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, Props.damagePerTickRare));
            }
        }
        public void Maintained()
        {
            ticksSinceMaintain = 0;
        }
        public override string CompInspectStringExtra()
        {
            switch (CurStage)
            {
                case MaintainableStage.NeedsMaintenance:
                    return "DueForMaintenance".Translate();
                case MaintainableStage.Damaging:
                    return "DeterioratingDueToLackOfMaintenance".Translate();
                default:
                    return null;
            }
        }
    }
    public class CompProperties_SpawnerJelly : CompProperties
    {
        public ThingDef thingToSpawn;
        public int spawnCount = 1;
        public IntRange spawnIntervalRange = new IntRange(100, 100);
        public bool writeTimeLeftToSpawn;
        public bool showMessageIfOwned;
        public string saveKeysPrefix;

        public CompProperties_SpawnerJelly()
        {
            compClass = typeof(CompSpawnerJelly);
        }
    }
    public class CompSpawnerJelly : ThingComp
    {
        private int ticksUntilSpawn;
        public CompProperties_SpawnerJelly PropsSpawner => (CompProperties_SpawnerJelly)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                ResetCountdown();
            }
        }
        public override void CompTick()
        {
            TickInterval(1);
        }
        public override void CompTickRare()
        {
            TickInterval(250);
        }
        private void TickInterval(int interval)
        {
            if (!parent.Spawned)
            {
                return;
            }
            CompCanBeDormant comp = parent.GetComp<CompCanBeDormant>();
            if (comp != null)
            {
                if (!comp.Awake)
                {
                    return;
                }
            }
            else if (parent.Position.Fogged(parent.Map))
            {
                return;
            }
            ticksUntilSpawn -= interval;
            CheckShouldSpawn();
        }
        private void CheckShouldSpawn()
        {
            if (ticksUntilSpawn <= 0)
            {
                TryDoSpawn();
                ResetCountdown();
            }
        }
        public bool TryDoSpawn()
        {
            if (!parent.Spawned)
            {
                return false;
            }
            Hive hive = parent as Hive;
            if (hive == null) return false;

            int spawnCount = PropsSpawner.spawnCount + HiveUtility.AllHivePawns(hive).Count;
            if (TryFindSpawnCell(parent, PropsSpawner.thingToSpawn, spawnCount, out IntVec3 result))
            {
                Thing thing = ThingMaker.MakeThing(PropsSpawner.thingToSpawn);
                thing.stackCount = spawnCount;
                if (thing == null)
                {
                    Log.Error("Could not spawn anything for " + parent);
                }
                GenPlace.TryPlaceThing(thing, result, parent.Map, ThingPlaceMode.Near, out Thing lastResultingThing);
                lastResultingThing.SetForbidden(value: true);
                if (PropsSpawner.showMessageIfOwned && parent.Faction == Faction.OfPlayer)
                {
                    Messages.Message("MessageCompSpawnerSpawnedItem".Translate(PropsSpawner.thingToSpawn.LabelCap), thing, MessageTypeDefOf.PositiveEvent);
                }
                return true;
            }
            return false;
        }
        public static bool TryFindSpawnCell(Thing parent, ThingDef thingToSpawn, int spawnCount, out IntVec3 result)
        {
            foreach (IntVec3 item in GenAdj.CellsAdjacent8Way(parent).InRandomOrder())
            {
                if (item.Walkable(parent.Map))
                {
                    Building edifice = item.GetEdifice(parent.Map);
                    if (edifice == null || !thingToSpawn.IsEdifice())
                    {
                        Building_Door building_Door = edifice as Building_Door;
                        if ((building_Door == null || building_Door.FreePassage) && (parent.def.passability == Traversability.Impassable || GenSight.LineOfSight(parent.Position, item, parent.Map)))
                        {
                            bool flag = false;
                            List<Thing> thingList = item.GetThingList(parent.Map);
                            for (int i = 0; i < thingList.Count; i++)
                            {
                                Thing thing = thingList[i];
                                if (thing.def.category == ThingCategory.Item && (thing.def != thingToSpawn || thing.stackCount > thingToSpawn.stackLimit - spawnCount))
                                {
                                    flag = true;
                                    break;
                                }
                            }
                            if (!flag)
                            {
                                result = item;
                                return true;
                            }
                        }
                    }
                }
            }
            result = IntVec3.Invalid;
            return false;
        }
        private void ResetCountdown()
        {
            ticksUntilSpawn = PropsSpawner.spawnIntervalRange.RandomInRange;
        }
        public override void PostExposeData()
        {
            string str = PropsSpawner.saveKeysPrefix.NullOrEmpty() ? null : (PropsSpawner.saveKeysPrefix + "_");
            Scribe_Values.Look(ref ticksUntilSpawn, str + "ticksUntilSpawn", 0);
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Prefs.DevMode)
            {
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "DEBUG: Spawn " + PropsSpawner.thingToSpawn.label;
                command_Action.icon = TexCommand.DesirePower;
                command_Action.action = delegate
                {
                    TryDoSpawn();
                    ResetCountdown();
                };
                yield return command_Action;
            }
        }
        public override string CompInspectStringExtra()
        {
            if (PropsSpawner.writeTimeLeftToSpawn)
            {
                return "NextSpawnedItemIn".Translate(GenLabel.ThingLabel(PropsSpawner.thingToSpawn, null, PropsSpawner.spawnCount)) + ": " + ticksUntilSpawn.ToStringTicksToPeriod();
            }
            return null;
        }
    }
    public class CompProperties_SpawnerFilth : CompProperties
    {
        public ThingDef filthDef;
        public int spawnCountOnSpawn = 5;
        public float spawnMtbHours = 12f;
        public float spawnRadius = 3f;
        public float spawnEveryDays = -1f;
        public RotStage? requiredRotStage;

        public CompProperties_SpawnerFilth()
        {
            compClass = typeof(CompSpawnerFilth);
        }
    }
    public class CompSpawnerFilth : ThingComp
    {
        private int nextSpawnTimestamp = -1;
        private CompProperties_SpawnerFilth Props => (CompProperties_SpawnerFilth)props;

        private bool CanSpawnFilth
        {
            get
            {
                Hive hive = parent as Hive;
                if (hive != null && !hive.CompDormant.Awake)
                {
                    return false;
                }
                if (Props.requiredRotStage.HasValue && parent.GetRotStage() != Props.requiredRotStage)
                {
                    return false;
                }
                return true;
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref nextSpawnTimestamp, "nextSpawnTimestamp", -1);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                for (int i = 0; i < Props.spawnCountOnSpawn; i++)
                {
                    TrySpawnFilth();
                }
            }
        }
        public override void CompTick()
        {
            base.CompTick();
            TickInterval(1);
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            TickInterval(250);
        }
        private void TickInterval(int interval)
        {
            if (!CanSpawnFilth)
            {
                return;
            }
            if (Props.spawnMtbHours > 0f && Rand.MTBEventOccurs(Props.spawnMtbHours, 2500f, interval))
            {
                TrySpawnFilth();
            }
            if (Props.spawnEveryDays >= 0f && Find.TickManager.TicksGame >= nextSpawnTimestamp)
            {
                if (nextSpawnTimestamp != -1)
                {
                    TrySpawnFilth();
                }
                nextSpawnTimestamp = Find.TickManager.TicksGame + (int)(Props.spawnEveryDays * 60000f);
            }
        }
        public void TrySpawnFilth()
        {
            if (parent.Map != null && CellFinder.TryFindRandomReachableNearbyCell(parent.Position, parent.Map, Props.spawnRadius, TraverseParms.For(TraverseMode.NoPassClosedDoors), (IntVec3 x) => x.Standable(parent.Map), (Region x) => true, out IntVec3 result))
            {
                FilthMaker.TryMakeFilth(result, parent.Map, Props.filthDef);
            }
        }
    }
    public class CompProperties_SpawnerHives : CompProperties
    {
        public float HiveSpawnPreferredMinDist = 3.5f;
        public float HiveSpawnRadius = 10f;

        public CompProperties_SpawnerHives()
        {
            compClass = typeof(CompSpawnerHives);
        }
    }
    public class CompSpawnerHives : ThingComp
    {
        private int nextHiveSpawnTick = -1;
        public bool canSpawnHives = true;
        private bool wasActivated;
        private CompProperties_SpawnerHives Props => (CompProperties_SpawnerHives)props;

        private bool CanSpawnChildHive
        {
            get
            {
                if (canSpawnHives && BetterInfestationsMod.settings != null)
                {
                    return HiveUtility.TotalSpawnedHivesCount(parent.Map) < BetterInfestationsMod.settings.maxHivesPerMap;
                }
                return false;
            }
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (!respawningAfterLoad)
            {
                CalculateNextHiveSpawnTick();
            }
        }
        public override void CompTick()
        {
            base.CompTick();
            CompCanBeDormant comp = parent.GetComp<CompCanBeDormant>();
            if ((comp?.Awake ?? true) && !wasActivated)
            {
                CalculateNextHiveSpawnTick();
                wasActivated = true;
            }
            if ((comp == null || comp.Awake) && Find.TickManager.TicksGame >= nextHiveSpawnTick)
            {
                if (TrySpawnChildHive(out Hive newHive))
                {
                    Messages.Message("MessageHiveReproduced".Translate(), newHive, MessageTypeDefOf.NegativeEvent);
                }
                else
                {
                    CalculateNextHiveSpawnTick();
                }
            }
        }
        public override string CompInspectStringExtra()
        {
            if (!canSpawnHives)
            {
                return "DormantHiveNotReproducing".Translate();
            }
            if (CanSpawnChildHive)
            {
                return "HiveReproducesIn".Translate() + ": " + (nextHiveSpawnTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
            }
            return null;
        }
        public void CalculateNextHiveSpawnTick()
        {
            if (BetterInfestationsMod.settings == null) return;

            Room room = parent.GetRoom();
            int num = 0;
            int num2 = GenRadial.NumCellsInRadius(9f);
            for (int i = 0; i < num2; i++)
            {
                IntVec3 intVec = parent.Position + GenRadial.RadialPattern[i];
                if (intVec.InBounds(parent.Map) && intVec.GetRoom(parent.Map) == room && intVec.GetThingList(parent.Map).Any((Thing t) => t is Hive))
                {
                    num++;
                }
            }
            float days = Rand.Range(BetterInfestationsMod.settings.hiveReproductionMinSpawnInDays, BetterInfestationsMod.settings.hiveReproductionMaxSpawnInDays);
            float reproduceRateFactorFromNearbyHiveCount = days / (1f + (num * 0.01f));
            int ticks = (int)(reproduceRateFactorFromNearbyHiveCount * 60000);
            nextHiveSpawnTick = Find.TickManager.TicksGame + ticks;
        }
        public bool TrySpawnChildHive(out Hive newHive)
        {
            if (!CanSpawnChildHive)
            {
                //Log.Message("Can't spawn");
                newHive = null;
                return false;
            }
            IntVec3 loc = FindChildHiveLocation(parent.Position, parent.Map, parent.def, Props, ignoreRoofedRequirement: true, allowUnreachable: false);
            if (!loc.IsValid)
            {
                //Log.Message("Invalid location");
                newHive = null;
                return false;
            }
            newHive = (Hive)ThingMaker.MakeThing(parent.def);
            if (newHive.Faction != parent.Faction)
            {
                newHive.SetFaction(parent.Faction);
            }
            Hive hive = parent as Hive;
            if (hive != null)
            {
                if (hive.CompDormant.Awake)
                {
                    newHive.CompDormant.WakeUp();
                }
                newHive.questTags = hive.questTags;
            }
            GenSpawn.Spawn(newHive, loc, parent.Map, WipeMode.FullRefund);
            CalculateNextHiveSpawnTick();
            return true;
        }
        public static IntVec3 FindChildHiveLocation(IntVec3 pos, Map map, ThingDef parentDef, CompProperties_SpawnerHives props, bool ignoreRoofedRequirement, bool allowUnreachable)
        {
            IntVec3 result = IntVec3.Invalid;
            for (int i = 0; i < 3; i++)
            {
                float minDist = props.HiveSpawnPreferredMinDist;
                bool flag;
                if (i >= 2)
                {
                    flag = allowUnreachable && CellFinder.TryFindRandomCellNear(pos, map, (int)props.HiveSpawnRadius, (IntVec3 c) => CanSpawnHiveAt(c, map, pos, parentDef, minDist, ignoreRoofedRequirement), out result);
                }
                else
                {
                    if (i == 1)
                    {
                        minDist = 0f;
                    }
                    flag = CellFinder.TryFindRandomReachableNearbyCell(pos, map, props.HiveSpawnRadius, TraverseParms.For(TraverseMode.NoPassClosedDoors), (IntVec3 c) => CanSpawnHiveAt(c, map, pos, parentDef, minDist, ignoreRoofedRequirement), null, out result);
                }
                if (flag)
                {
                    result = CellFinder.FindNoWipeSpawnLocNear(result, map, parentDef, Rot4.North, 2, (IntVec3 c) => CanSpawnHiveAt(c, map, pos, parentDef, minDist, ignoreRoofedRequirement));
                    break;
                }
            }
            return result;
        }
        private static bool CanSpawnHiveAt(IntVec3 c, Map map, IntVec3 parentPos, ThingDef parentDef, float minDist, bool ignoreRoofedRequirement)
        {
		if ((!ignoreRoofedRequirement && !c.Roofed(map)) || !c.Walkable(map) || (minDist != 0f && !((float)c.DistanceToSquared(parentPos) >= minDist * minDist)) || c.GetFirstThing(map, RimWorld.ThingDefOf.InsectJelly) != null || c.GetFirstThing(map, RimWorld.ThingDefOf.GlowPod) != null)
		{
			return false;
		}
		for (int i = 0; i < 9; i++)
		{
			IntVec3 c2 = c + GenAdj.AdjacentCellsAndInside[i];
			if (!c2.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = c2.GetThingList(map);
			for (int j = 0; j < thingList.Count; j++)
			{
				if (thingList[j] is Hive || thingList[j] is TunnelHiveSpawner)
				{
					return false;
				}
			}
		}
		List<Thing> thingList2 = c.GetThingList(map);
		for (int k = 0; k < thingList2.Count; k++)
		{
			Thing thing = thingList2[k];
			if (thing.def.category == ThingCategory.Building && thing.def.passability == Traversability.Impassable && GenSpawn.SpawningWipes(parentDef, thing.def))
			{
				return true;
			}
		}
		return true;
	}
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Prefs.DevMode)
            {
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "Dev: Reproduce";
                command_Action.icon = TexCommand.GatherSpotActive;
                command_Action.action = delegate
                {
                    TrySpawnChildHive(out Hive _);
                };
                yield return command_Action;
            }
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref nextHiveSpawnTick, "nextHiveSpawnTick", 0);
            Scribe_Values.Look(ref canSpawnHives, "canSpawnHives", defaultValue: true);
            Scribe_Values.Look(ref wasActivated, "wasActivated", defaultValue: true);
        }
    }
    public class CompProperties_SpawnerPawns : CompProperties
    {
        public CompProperties_SpawnerPawns()
        {
            compClass = typeof(CompSpawnerPawns);
        }
    }
    public class CompSpawnerPawns : ThingComp
    {
        public float[] maxSpawnedPawnsPoints = { -1f, -1f, -1f};
        public int[] nextPawnSpawnTick = { -1, -1, -1};
        public HashSet<Pawn>[] spawnedPawns = { new(), new(), new()};
        public Lord[] Lord = { null, null, null};
        public bool canSpawnPawns = true;
        public bool queenSpawned = false;
        public Thing[] attackTarget = { null, null, null };
        public IntVec3[] patrolLoc = { IntVec3.Invalid, IntVec3.Invalid, IntVec3.Invalid };
        public LocomotionUrgency[] patrolLocomotion = { LocomotionUrgency.Walk, LocomotionUrgency.Walk, LocomotionUrgency.Walk };
        public enum GroupState { Idle, Patrolling, Returning, WaitForOrders }
        public GroupState[] groupState = { GroupState.Idle, GroupState.Idle, GroupState.Idle };
        public bool[] waitForOrders = { true, true, true };
        public int[] waitTicks = { 0, 0, 0 };
        public int reassignPawnTick = -1;
        public HiveData_MapComponent mapHiveData;



        public CompProperties_SpawnerPawns Props => (CompProperties_SpawnerPawns)props;

        public float SpawnedPawnsPoints(int index)
        {
            FilterOutDeadPawns(index);

            return spawnedPawns[index].Sum(x => Convert.ToSingle(x.kindDef.combatPower));
        }
        private void FilterOutDeadPawns(int index)
        {
            spawnedPawns[index].RemoveWhere(x => x.Dead || !x.Spawned);
        }
        public int GroupStrength(int index)
        {
            FilterOutDeadPawns(index);
            int num = 0;
            foreach (Pawn pawn in spawnedPawns[index])
            {
                if (pawn.jobs.posture != PawnPosture.LayingOnGroundNormal && pawn.mindState != null && pawn.mindState.duty != null)
                {
                    num += (int)pawn.kindDef.combatPower;
                }
            }
            return num;
        }
        public int TotalStrength()
        {
            int num = 0;
            for (int i = 0; i < 3; i++)
            {
                FilterOutDeadPawns(i);
                foreach (Pawn pawn in spawnedPawns[i])
                {
                    num += (int)pawn.kindDef.combatPower;
                }
            }
            return num;
        }
        public bool Active => parent.GetComp<CompCanBeDormant>()?.Awake ?? true;
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
        }
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            mapHiveData = parent.Map.GetComponent<HiveData_MapComponent>();
            mapHiveData.RebuildHiveGrid();
        }
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            mapHiveData.RebuildHiveGrid();
        }
        public static Lord CreateNewLord(Thing byThing, Type lordJobType)
        {
            if (!CellFinder.TryFindRandomCellNear(byThing.Position, byThing.Map, 5, (IntVec3 c) => c.Standable(byThing.Map) && byThing.Map.reachability.CanReach(c, byThing, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors)), out IntVec3 result))
            {
                result = IntVec3.Invalid;
            }
            return LordMaker.MakeNewLord(byThing.Faction, Activator.CreateInstance(lordJobType, null) as LordJob, byThing.Map);
        }
        public void SpawnInitialPawns()
        {
            if (BetterInfestationsMod.settings == null) return;

            float initialPawnsPoints = BetterInfestationsMod.settings.initialPawnsPoints;

            maxSpawnedPawnsPoints[0] = Math.Min(400f, initialPawnsPoints);
            if (BetterInfestationsMod.settings.hiveLevel > 0) maxSpawnedPawnsPoints[1] = Math.Min(400f, initialPawnsPoints);
            if (BetterInfestationsMod.settings.hiveLevel > 1) maxSpawnedPawnsPoints[2] = Math.Min(400f, initialPawnsPoints);

            for (int i = 0; i < 3; i++)
            {
                if (maxSpawnedPawnsPoints[i] == -1) continue;

                while (SpawnedPawnsPoints(i) < maxSpawnedPawnsPoints[i])
                {
                    if (!TrySpawnPawn(i, out Pawn _, RandomWeightedPawnKindDef(), BetterInfestationsMod.settings.newbornInsects)) break;
                }
            }


            maxSpawnedPawnsPoints[0] = 200f;
            if (BetterInfestationsMod.settings.hiveLevel > 0) maxSpawnedPawnsPoints[1] = 400f;
            if (BetterInfestationsMod.settings.hiveLevel > 1) maxSpawnedPawnsPoints[2] = 400f;
            //Log.Message($"Initial pawn points = {SpawnedPawnsPoints(0) + SpawnedPawnsPoints(1) + SpawnedPawnsPoints(2)}");
            CalculateNextPawnSpawnTick(0);
        }
        private void CalculateNextPawnSpawnTick(int index)
        {
            if (BetterInfestationsMod.settings == null) return;

            float days = Rand.Range(BetterInfestationsMod.settings.minSpawnInDays[index], BetterInfestationsMod.settings.maxSpawnInDays[index]);
            float dayTicks = 60000;
            if (HiveUtility.QueenActive(parent as Hive)) dayTicks = 42000;
            int ticks = (int)(days * dayTicks);
            nextPawnSpawnTick[index] = Find.TickManager.TicksGame + ticks;
        }
        public void UpdateMaxPawnLimits()
        {
            // Gates the creation of the next hunter group until the group before it is at full strength
            if (maxSpawnedPawnsPoints[1] < 400f && GroupStrength(0) > 200f) maxSpawnedPawnsPoints[1] = 400f;
            if (maxSpawnedPawnsPoints[2] < 400f && GroupStrength(1) > 400f) maxSpawnedPawnsPoints[2] = 400f;
        }
        private void ReassignNullDutyPawns()
        {
            foreach (Pawn p in parent.Map.mapPawns.SpawnedPawnsInFaction(parent.Faction))
            {
                if (p != null && !p.Downed && p.mindState.duty == null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Lord lord = Lord[i];
                        if (lord == null) continue;

                        if (lord.ownedPawns != null && !lord.ownedPawns.Contains(p) && spawnedPawns[i].Contains(p))
                        {
                            FieldInfo FI_curLordToil = typeof(Lord).GetField("curLordToil", Patches.allFlags);
                            LordToil lordToil = (LordToil)FI_curLordToil.GetValue(lord);
                            lord.AddPawn(p);
                            parent.Map.attackTargetsCache.UpdateTarget(p);
                            lordToil.UpdateAllDuties();
                        }
                    }
                }
            }
        }
        [Obsolete]
        public PawnKindDef RandomPawnKindDef()
        {
            IEnumerable<PawnKindDef> source;
            source = new List<PawnKindDef> { RimWorld.PawnKindDefOf.Megascarab, RimWorld.PawnKindDefOf.Spelopede, RimWorld.PawnKindDefOf.Megaspider };

            if (ModsConfig.IsActive("zal.vfeinsectoid"))
            {
                System.Random rand = new System.Random();
                int VFEIChance = rand.Next(1, 101);
                if (VFEIChance < 20)
                {

                    ((List<PawnKindDef>)source).Add(PawnKindDefOf.VFEI_Insectoid_RoyalMegaspider);
                    ((List<PawnKindDef>)source).Add(PawnKindDefOf.VFEI_Insectoid_Gigalocust);
                    ((List<PawnKindDef>)source).Add(PawnKindDefOf.VFEI_Insectoid_Megapede);

                }
            }

            if (source.TryRandomElement(out PawnKindDef result))
            {
                return result;
            }
            return null;
        }
        public PawnKindDef RandomWeightedPawnKindDef(float threatPoints = 0f)
        {
            var choices = new List<(PawnKindDef kind, float weight)>
    {
        (RimWorld.PawnKindDefOf.Megascarab, 1.0f),
        (RimWorld.PawnKindDefOf.Spelopede,  1.0f),
        (RimWorld.PawnKindDefOf.Megaspider, 1.0f)
    };


            if (ModsConfig.OdysseyActive)
            {
                choices.Add((PawnKindDefOf.Locust, 0.3f));
                choices.Add((PawnKindDefOf.Larva, 0.3f));
                //choices.Add((PawnKindDefOf.HiveQueen, 0.1f));
            }

            if (ModsConfig.IsActive("zal.vfeinsectoid"))
            {
                // Scale VFEI presence with threat if provided
                float vfeiWeight = threatPoints > 0f
            ? Mathf.Clamp01(threatPoints / 1200f) * 0.6f
            : 0.2f; // fallback when threat not available
                // Max ~60% of a vanilla insect's weight           

                if (vfeiWeight > 0.01f)
                {
                    choices.Add((PawnKindDefOf.VFEI_Insectoid_RoyalMegaspider, vfeiWeight));
                    choices.Add((PawnKindDefOf.VFEI_Insectoid_Gigalocust, vfeiWeight));
                    choices.Add((PawnKindDefOf.VFEI_Insectoid_Megapede, vfeiWeight));
                }
            }

            return choices.RandomElementByWeight(c => c.weight).kind;
        }
        public bool TrySpawnPawn(int index, out Pawn pawn, PawnKindDef chosenKind, bool newbornPawn)
        {
            if (chosenKind == null || BetterInfestationsMod.settings == null)
            {
                pawn = null;
                return false;
            }

            // Total strength check if the number of hives is more than half
            float totalPower = 0f;
            int validPawnCount = 0;
            foreach (Pawn p in parent.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfInsects))
            {
                if (!p.Dead)
                {
                    validPawnCount++;
                    totalPower += p.kindDef.combatPower;
                }
            }

            //Log.Message($"Total power = {totalPower} for {validPawnCount} pawns");
            //Log.Message($"{Math.Max(BetterInfestationsMod.settings.maxHivesPerMap * 1000f, 10000f) * (1.0f + (Find.Storyteller.difficulty.threatScale - 1.0f) / 6f)}");
            if (totalPower > Math.Max(BetterInfestationsMod.settings.maxHivesPerMap * 1000f, 10000f) * (1.0f + (Find.Storyteller.difficulty.threatScale - 1.0f) / 6f))
            {
                pawn = null;
                return false;
            }

            if (!canSpawnPawns) newbornPawn = false;
            Hive hive = parent as Hive;
            PawnGenerationRequest request = new PawnGenerationRequest(chosenKind, parent.Faction, PawnGenerationContext.NonPlayer, -1, true, newbornPawn);
            pawn = PawnGenerator.GeneratePawn(request);
            if (chosenKind == PawnKindDefOf.BI_Queen)
            {
                pawn.gender = Gender.Female;
            }
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(parent.Position, parent.Map, 2), parent.Map);
            spawnedPawns[index].Add(pawn);
            mapHiveData.AddPawnHiveData(pawn, (Hive)parent);

            Lord lord = Lord[index];
            if (lord == null)
            {
                Type lordJobType = null;
                if (index == 0) lordJobType = typeof(LordJob_DefendAndExpandHive);
                else lordJobType = typeof(LordJob_HiveHunters);
                lord = CreateNewLord(parent, lordJobType);
                Lord[index] = lord;
            }
            lord.AddPawn(pawn);
            SoundDef soundDef = SoundDefOf.Hive_Spawn;
            if (soundDef != null)
            {
                soundDef.PlayOneShot(parent);
            }
            return true;
        }
        public override void CompTick()
        {
            if (BetterInfestationsMod.settings == null) return;

            if (Find.TickManager.TicksGame >= reassignPawnTick)
            {
                ReassignNullDutyPawns();
                mapHiveData.RemovePawnHiveDataSweep();

                reassignPawnTick = Find.TickManager.TicksGame + 600;
            }

            if (!parent.Spawned || !Active)
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                if (i == 0 && nextPawnSpawnTick[i] == -1)
                {
                    SpawnInitialPawns();
                }
                else if (i != 0 && nextPawnSpawnTick[i] == -1)
                {
                    CalculateNextPawnSpawnTick(i);
                }
                if (Find.TickManager.TicksGame >= nextPawnSpawnTick[i])
                {
                    // Gate next spawn if hive too strong
                    if (TotalStrength() > InfestationUtility.CalculateHiveTimeFactor((Find.TickManager.TicksSinceSettle - parent.TickSpawned).TicksToDays()) * 1200f)
                    {
                        //Log.Message($"Total strength of {parent.ThingID} is {TotalStrength()}, greater than {InfestationUtility.CalculateHiveTimeFactor((Find.TickManager.TicksSinceSettle - parent.TickSpawned).TicksToDays()) * 1200f}!");
                        CalculateNextPawnSpawnTick(i);
                        return;
                    }

                    // Try spawn insect
                    if (canSpawnPawns && SpawnedPawnsPoints(i) < maxSpawnedPawnsPoints[i])
                    {
                        //Log.Message($"Trying to spawn pawn!");
                        if (TrySpawnPawn(i, out Pawn pawn, RandomWeightedPawnKindDef(), BetterInfestationsMod.settings.newbornInsects) && pawn.caller != null)
                        {
                            pawn.caller.DoCall();
                        }
                        UpdateMaxPawnLimits();
                    }

                    // Try spawn queen
                    if (i == 0 && canSpawnPawns && !queenSpawned && BetterInfestationsMod.settings.queensAllowed && Rand.Range(1, 100) <= 30 && TotalStrength() >= 900)
                    {
                        if (TrySpawnPawn(i, out Pawn q, PawnKindDefOf.BI_Queen, BetterInfestationsMod.settings.newbornInsects) && q.caller != null)
                        {
                            Messages.Message("A bug queen has spawned.", q, MessageTypeDefOf.NegativeEvent);
                            q.caller.DoCall();
                            queenSpawned = true;
                        }
                    }
                    CalculateNextPawnSpawnTick(i);
                }
                GroupController(i);
            }
        }
        public void GroupController(int index)
        {
            if (index == 0) return;
            IntVec3 pos;


            if (Find.TickManager.TicksGame > waitTicks[index])
            {
                float groupStrength = GroupStrength(index);
                if (groupStrength == 0)
                {
                    waitTicks[index] = 7200;
                    return;
                }
                //Log.Message($"groupStrength = {groupStrength}");

                Pawn pawn = null;
                foreach (Pawn p in spawnedPawns[index])
                {
                    pawn = p;
                    // if pawn is outside of patrol location and on the way, then regroup
                    if (IntVec3Utility.ManhattanDistanceFlat(p.Position, patrolLoc[index]) > 8)
                    {
                        if (p.jobs.curJob != null && (p.jobs.curJob.def == RimWorld.JobDefOf.GotoWander || pawn.jobs.curJob?.def == RimWorld.JobDefOf.Wait_Wander)) break;
                        
                        bool asleep = p.jobs.posture == PawnPosture.LayingOnGroundNormal;
                        bool atWork = p.jobs.curJob != null && p.jobs.curJob.targetA != null && p.jobs.curJob.targetA != patrolLoc[index];

                        if (!asleep && !atWork)
                        {
                            // Waits for group to regroup if a pawn is behind and not sleeping or busy
                            waitTicks[index] = Find.TickManager.TicksGame + 1200;
                            return;
                        }
                    }
                }

                switch (groupState[index])
                {
                    case GroupState.Idle:
                        //Log.Message($"Group {index} of {parent.ThingID} is idle! Group strength is {groupStrength}");

                        if (groupStrength > 120)
                        {
                            groupState[index] = GroupState.Patrolling;
                            patrolLocomotion[index] = LocomotionUrgency.Walk;
                            waitTicks[index] = Find.TickManager.TicksGame + 60;
                        }
                        else
                        {
                            // If not strong enough, wait in hive for more pawns to spawn
                            patrolLoc[index] = parent.Position;
                            patrolLocomotion[index] = LocomotionUrgency.Amble;
                            waitTicks[index] = Find.TickManager.TicksGame + 7200;
                        }
                        break;

                    case GroupState.Patrolling:

                        pos = HiveUtility.FindPathToPrey(pawn);
                        if (pos != IntVec3.Invalid && pawn.CanReserve(pos))
                        {
                            //Log.Message($"Group {index} of {parent.ThingID} is patrolling towards prey!");
                            // Patrol towards prey
                            patrolLoc[index] = pos;
                            waitTicks[index] = Find.TickManager.TicksGame + 1200;

                            // weaker groups wait more, stronger groups hunt better
                            if (groupStrength < 400f && Rand.Range(1, 100) <= 40) groupState[index] = GroupState.WaitForOrders;
                            if (groupStrength >= 400f && Rand.Range(1, 100) <= 20) groupState[index] = GroupState.WaitForOrders;

                            break;
                        }
                        else
                        {
                            // If no prey, patrol randomly
                            if (CellFinder.TryFindRandomReachableNearbyCell(pawn.Position, pawn.Map, 15f, TraverseMode.PassDoors, (c => c.Standable(pawn.Map)), null, out pos))
                            {
                                //Log.Message($"Group {index} of {parent.ThingID} is patrolling randomly!");
                                patrolLoc[index] = pos;
                                waitTicks[index] = Find.TickManager.TicksGame + 1800;

                                if (Rand.Range(1, 100) <= 25) groupState[index] = GroupState.WaitForOrders;
                            }
                            else
                            {
                                //patrolLoc[index] = parent.Position;
                                groupState[index] = GroupState.Returning;
                                waitTicks[index] = Find.TickManager.TicksGame + 60;
                            }
                        }

                        patrolLocomotion[index] = LocomotionUrgency.Walk;
                        break;

                    case GroupState.WaitForOrders:
                        //Log.Message($"Group {index} of {parent.ThingID} is waiting!");

                        waitTicks[index] = Find.TickManager.TicksGame + 1200;
                        groupState[index] = GroupState.Patrolling;
                        break;

                    case GroupState.Returning:
                        //Log.Message($"Group {index} of {parent.ThingID} is returning!");

                        patrolLocomotion[index] = LocomotionUrgency.Jog;
                        patrolLoc[index] = parent.Position;
                        waitTicks[index] = Find.TickManager.TicksGame + 3600; // 1 minute to return to hive

                        groupState[index] = GroupState.Idle;

                        break;
                }

                if (groupStrength < 120 && groupState[index] != GroupState.Idle)
                {
                    groupState[index] = GroupState.Returning;
                    waitTicks[index] = Find.TickManager.TicksGame + 60;
                }
                ;
            }
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Prefs.DevMode)
            {
                if (BetterInfestationsMod.settings == null)
                {
                    yield return null;
                }
                Command_Action command_Action = new Command_Action();
                command_Action.defaultLabel = "DEBUG: Spawn bug for defense group";
                command_Action.icon = TexCommand.ReleaseAnimals;
                command_Action.action = delegate
                {
                    TrySpawnPawn(0, out Pawn _, RandomWeightedPawnKindDef(), BetterInfestationsMod.settings.newbornInsects);
                };
                yield return command_Action;

                command_Action = new Command_Action();
                command_Action.defaultLabel = "DEBUG: Spawn bug for hunting party 1";
                command_Action.icon = TexCommand.ReleaseAnimals;
                command_Action.action = delegate
                {
                    TrySpawnPawn(1, out Pawn _, RandomWeightedPawnKindDef(), BetterInfestationsMod.settings.newbornInsects);
                };
                yield return command_Action;

                command_Action = new Command_Action();
                command_Action.defaultLabel = "DEBUG: Spawn bug for hunting party 2";
                command_Action.icon = TexCommand.ReleaseAnimals;
                command_Action.action = delegate
                {
                    TrySpawnPawn(2, out Pawn _, RandomWeightedPawnKindDef(), BetterInfestationsMod.settings.newbornInsects);
                };
                yield return command_Action;
            }
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            HashSet<Pawn> defenseGroup = spawnedPawns[0];
            HashSet<Pawn> huntingGroup1 = spawnedPawns[1];
            HashSet<Pawn> huntingGroup2 = spawnedPawns[2];
            int defenseGroupSpawnTick = nextPawnSpawnTick[0];
            int huntingGroup1SpawnTick = nextPawnSpawnTick[1];
            int huntingGroup2SpawnTick = nextPawnSpawnTick[2];
            Thing defenseGroupAttackTarget = attackTarget[0];
            Thing huntingGroup1AttackTarget = attackTarget[1];
            Thing huntingGroup2AttackTarget = attackTarget[2];
            IntVec3 defenseGroupPatrolLoc = patrolLoc[0];
            IntVec3 huntingGroup1PatrolLoc = patrolLoc[1];
            IntVec3 huntingGroup2PatrolLoc = patrolLoc[2];
            LocomotionUrgency defenseGroupPatrolLocomotion = patrolLocomotion[0];
            LocomotionUrgency huntingGroup1PatrolLocomotion = patrolLocomotion[1];
            LocomotionUrgency huntingGroup2PatrolLocomotion = patrolLocomotion[2];
            GroupState defenseGroupGroupState = groupState[0];
            GroupState huntingGroup1GroupState = groupState[1];
            GroupState huntingGroup2GroupState = groupState[2];
            bool defenseGroupWaitForOrders = waitForOrders[0];
            bool huntingGroup1WaitForOrders = waitForOrders[1];
            bool huntingGroup2WaitForOrders = waitForOrders[2];
            int defenseGroupWaitTicks = waitTicks[0];
            int huntingGroup1WaitTicks = waitTicks[1];
            int huntingGroup2WaitTicks = waitTicks[2];
            float defenseGroupMaxSpawnedPawnsPoints = maxSpawnedPawnsPoints[0];
            float huntingGroup1MaxSpawnedPawnsPoints = maxSpawnedPawnsPoints[1];
            float huntingGroup2MaxSpawnedPawnsPoints = maxSpawnedPawnsPoints[2];
            Lord defenseGroupLord = Lord[0];
            Lord huntingGroup1Lord = Lord[1];
            Lord huntingGroup2Lord = Lord[2];

            Scribe_Values.Look(ref defenseGroupSpawnTick, "defenseGroupSpawnTick", 0);
            Scribe_References.Look(ref defenseGroupAttackTarget, "defenseGroupAttackTarget");
            Scribe_Values.Look(ref defenseGroupPatrolLoc, "defenseGroupPatrolLoc", IntVec3.Invalid);
            Scribe_Values.Look(ref defenseGroupPatrolLocomotion, "defenseGroupPatrolLocomotion", LocomotionUrgency.Walk);
            Scribe_Values.Look(ref defenseGroupGroupState, "defenseGroupGroupState", GroupState.Idle);
            Scribe_Values.Look(ref defenseGroupWaitForOrders, "defenseGroupWaitForOrders", true);
            Scribe_Values.Look(ref defenseGroupWaitTicks, "defenseGroupWaitTicks", 0);
            Scribe_Values.Look(ref defenseGroupMaxSpawnedPawnsPoints, "defenseGroupMaxSpawnedPawnsPoints", 1000f);
            Scribe_Collections.Look(ref defenseGroup, "defenseGroup", LookMode.Reference);
            Scribe_References.Look(ref defenseGroupLord, "defenseGroupLord", false);

            Scribe_Values.Look(ref huntingGroup1SpawnTick, "huntingGroup1SpawnTick", 0);
            Scribe_References.Look(ref huntingGroup1AttackTarget, "huntingGroup1AttackTarget");
            Scribe_Values.Look(ref huntingGroup1PatrolLoc, "huntingGroup1PatrolLoc", IntVec3.Invalid);
            Scribe_Values.Look(ref huntingGroup1PatrolLocomotion, "huntingGroup1PatrolLocomotion", LocomotionUrgency.Walk);
            Scribe_Values.Look(ref huntingGroup1GroupState, "huntingGroup1GroupState", GroupState.Idle);
            Scribe_Values.Look(ref huntingGroup1WaitForOrders, "huntingGroup1WaitForOrders", true);
            Scribe_Values.Look(ref huntingGroup1WaitTicks, "huntingGroup1WaitTicks", 0);
            Scribe_Values.Look(ref huntingGroup1MaxSpawnedPawnsPoints, "huntingGroup1MaxSpawnedPawnsPoints", 1000f);
            Scribe_Collections.Look(ref huntingGroup1, "huntingGroup1", LookMode.Reference);
            Scribe_References.Look(ref huntingGroup1Lord, "huntingGroup1Lord", false);

            Scribe_Values.Look(ref huntingGroup2SpawnTick, "huntingGroup2SpawnTick", 0);
            Scribe_References.Look(ref huntingGroup2AttackTarget, "huntingGroup2AttackTarget");
            Scribe_Values.Look(ref huntingGroup2PatrolLoc, "huntingGroup2PatrolLoc", IntVec3.Invalid);
            Scribe_Values.Look(ref huntingGroup2PatrolLocomotion, "huntingGroup2PatrolLocomotion", LocomotionUrgency.Walk);
            Scribe_Values.Look(ref huntingGroup2GroupState, "huntingGroup2GroupState", GroupState.Idle);
            Scribe_Values.Look(ref huntingGroup2WaitForOrders, "huntingGroup2WaitForOrders", true);
            Scribe_Values.Look(ref huntingGroup2WaitTicks, "huntingGroup2WaitTicks", 0);
            Scribe_Values.Look(ref huntingGroup2MaxSpawnedPawnsPoints, "huntingGroup2MaxSpawnedPawnsPoints", -1f);
            Scribe_Collections.Look(ref huntingGroup2, "huntingGroup2", LookMode.Reference);
            Scribe_References.Look(ref huntingGroup2Lord, "huntingGroup2Lord", false);

            Scribe_Values.Look(ref canSpawnPawns, "canSpawnPawns", defaultValue: true);
            Scribe_Values.Look(ref queenSpawned, "queenSpawned", defaultValue: false);

            spawnedPawns[0] = defenseGroup;
            spawnedPawns[1] = huntingGroup1;
            spawnedPawns[2] = huntingGroup2;
            nextPawnSpawnTick[0] = defenseGroupSpawnTick;
            nextPawnSpawnTick[1] = huntingGroup1SpawnTick;
            nextPawnSpawnTick[2] = huntingGroup2SpawnTick;
            attackTarget[0] = defenseGroupAttackTarget;
            attackTarget[1] = huntingGroup1AttackTarget;
            attackTarget[2] = huntingGroup2AttackTarget;
            patrolLoc[0] = defenseGroupPatrolLoc;
            patrolLoc[1] = huntingGroup1PatrolLoc;
            patrolLoc[2] = huntingGroup2PatrolLoc;
            patrolLocomotion[0] = defenseGroupPatrolLocomotion;
            patrolLocomotion[1] = huntingGroup1PatrolLocomotion;
            patrolLocomotion[2] = huntingGroup2PatrolLocomotion;
            groupState[0] = defenseGroupGroupState;
            groupState[1] = huntingGroup1GroupState;
            groupState[2] = huntingGroup2GroupState;
            waitForOrders[0] = defenseGroupWaitForOrders;
            waitForOrders[1] = huntingGroup1WaitForOrders;
            waitForOrders[2] = huntingGroup2WaitForOrders;
            waitTicks[0] = defenseGroupWaitTicks;
            waitTicks[1] = huntingGroup1WaitTicks;
            waitTicks[2] = huntingGroup2WaitTicks;
            maxSpawnedPawnsPoints[0] = defenseGroupMaxSpawnedPawnsPoints;
            maxSpawnedPawnsPoints[1] = huntingGroup1MaxSpawnedPawnsPoints;
            maxSpawnedPawnsPoints[2] = huntingGroup2MaxSpawnedPawnsPoints;
            Lord[0] = defenseGroupLord;
            Lord[1] = huntingGroup1Lord;
            Lord[2] = huntingGroup2Lord;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                for (int i = 0; i < 3; i++)
                {
                    spawnedPawns[i].RemoveWhere(x => x == null);
                }
            }
        }
    }
}