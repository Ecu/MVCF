using System.Collections.Generic;
using System.Linq;
using MVCF.Comps;
using MVCF.Harmony;
using MVCF.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MVCF
{
    public class VerbManager
    {
        private readonly List<ManagedVerb> drawVerbs = new List<ManagedVerb>();
        private readonly List<TurretVerb> tickVerbs = new List<TurretVerb>();
        private readonly List<ManagedVerb> verbs = new List<ManagedVerb>();
        public Verb CurrentVerb;
        private Pawn pawn;
        public Verb SearchVerb;
        public ManagedVerb LongestRangedVerb { get; private set; }

        public IEnumerable<Verb> AllVerbs => verbs.Select(mv => mv.Verb);
        public IEnumerable<Verb> AllRangedVerbs => verbs.Select(mv => mv.Verb).Where(verb => !verb.IsMeleeAttack);

        public IEnumerable<Verb> AllRangedVerbsNoEquipment =>
            verbs.Where(mv => mv.Source != VerbSource.Equipment).Select(mv => mv.Verb);

        public IEnumerable<ManagedVerb> ManagedVerbs => verbs;

        public IEnumerable<Verb> AllRangedVerbsNoEquipmentNoApparel => verbs
            .Where(mv => mv.Source != VerbSource.Equipment && mv.Source != VerbSource.Apparel).Select(mv => mv.Verb);

        public void Initialize(Pawn pawn)
        {
            this.pawn = pawn;
            foreach (var verb in pawn.VerbTracker.AllVerbs)
                AddVerb(verb, VerbSource.RaceDef, pawn.TryGetComp<Comp_VerbGiver>()?.PropsFor(verb));
            if (pawn?.health?.hediffSet?.hediffs != null)
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    var comp = hediff.TryGetComp<HediffComp_VerbGiver>();
                    if (comp != null)
                    {
                        var extComp = comp as HediffComp_ExtendedVerbGiver;
                        foreach (var verb in comp.VerbTracker.AllVerbs)
                            AddVerb(verb, VerbSource.Hediff, extComp?.PropsFor(verb));
                    }
                }

            if (pawn?.apparel?.WornApparel != null)
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    var comp = apparel.TryGetComp<Comp_VerbGiver>();
                    if (comp == null) return;
                    foreach (var verb in comp.VerbTracker.AllVerbs)
                        AddVerb(verb, VerbSource.Apparel, comp.PropsFor(verb));
                }

            if (pawn?.equipment?.AllEquipmentListForReading != null)
                foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                {
                    var comp = eq.TryGetComp<CompEquippable>();
                    if (comp == null)
                    {
                        var extComp = eq.TryGetComp<Comp_VerbGiver>();
                        if (extComp == null) return;
                        foreach (var verb in extComp.VerbTracker.AllVerbs)
                            AddVerb(verb, VerbSource.Equipment, extComp.PropsFor(verb));
                    }
                    else
                    {
                        foreach (var verb in comp.VerbTracker.AllVerbs)
                            AddVerb(verb, VerbSource.Equipment, null);
                    }
                }
        }


        public void AddVerb(Verb verb, VerbSource source, AdditionalVerbProps props)
        {
            // Log.Message("AddVerb " + verb + ", " + source + ", " + props);
            ManagedVerb mv;
            if (props != null && props.canFireIndependently)
            {
                var tv = new TurretVerb(verb, source, props);
                tickVerbs.Add(tv);
                mv = tv;
            }
            else
            {
                mv = new ManagedVerb(verb, source, props);
            }

            verbs.Add(mv);
            if (props != null && props.draw) drawVerbs.Add(mv);
            if (LongestRangedVerb == null || verb.verbProps.range > LongestRangedVerb.Verb.verbProps.range)
                LongestRangedVerb = mv;
        }

        public void RemoveVerb(Verb verb)
        {
            var mv = verbs.Find(m => m.Verb == verb);
            if (LongestRangedVerb == mv)
            {
                var maxRange = verbs.Select(v => v.Verb.verbProps.range).Max();
                LongestRangedVerb = verbs.First(v => v.Verb.verbProps.range >= maxRange);
            }

            verbs.Remove(mv);
            if (drawVerbs.Contains(mv)) drawVerbs.Remove(mv);
            var idx = tickVerbs.FindIndex(tv => tv.Verb == verb);
            if (idx >= 0) tickVerbs.RemoveAt(idx);
        }

        public void DrawAt(Vector3 drawPos)
        {
            foreach (var mv in drawVerbs) mv.DrawOn(pawn, drawPos);
        }

        public void Tick()
        {
            foreach (var mv in tickVerbs) mv.Tick();
        }
    }

    public class ManagedVerb
    {
        public bool Enabled = true;
        public AdditionalVerbProps Props;
        public VerbSource Source;
        public Verb Verb;

        public ManagedVerb(Verb verb, VerbSource source, AdditionalVerbProps props)
        {
            Verb = verb;
            Source = source;
            Props = props;
            props?.Initialize();
        }

        public void DrawOn(Pawn p, Vector3 drawPos)
        {
            if (Props == null) return;
            if (!Props.draw) return;
            if (p.Dead || !p.Spawned) return;
            drawPos += Vector3.up;
            var target = PointingTarget(p);
            if (target != null && target.IsValid)
            {
                var a = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();

                DrawPointingAt(Props.DrawPos(p.def.defName, drawPos, p.Rotation),
                    (a - drawPos).MagnitudeHorizontalSquared() > 0.001f ? (a - drawPos).AngleFlat() : 0f, p.BodySize);
            }
            else
            {
                DrawPointingAt(Props.DrawPos(p.def.defName, drawPos, p.Rotation), p.Rotation.AsAngle, p.BodySize);
            }
        }

        public virtual LocalTargetInfo PointingTarget(Pawn p)
        {
            if (p.stances.curStance is Stance_Busy busy && !busy.neverAimWeapon && busy.focusTarg.IsValid)
                return busy.focusTarg;
            return null;
        }

        private void DrawPointingAt(Vector3 drawLoc, float aimAngle, float scale)
        {
            var num = aimAngle - 90f;
            Mesh mesh;
            if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                num -= 180f;
            }
            else
            {
                mesh = MeshPool.plane10;
            }

            num %= 360f;

            var matrix4X4 = new Matrix4x4();
            matrix4X4.SetTRS(drawLoc, Quaternion.AngleAxis(num, Vector3.up), Vector3.one * scale);

            Graphics.DrawMesh(mesh, matrix4X4, Props.Graphic.MatSingle, 0);
        }
    }

    public enum VerbSource
    {
        Apparel,
        Equipment,
        Hediff,
        RaceDef
    }

    public class TurretVerb : ManagedVerb
    {
        private readonly DummyCaster dummyCaster;
        private readonly Pawn pawn;
        private int cooldownTicksLeft;
        private LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
        private int warmUpTicksLeft;


        public TurretVerb(Verb verb, VerbSource source, AdditionalVerbProps props) : base(verb, source, props)
        {
            pawn = verb.CasterPawn;
            dummyCaster = new DummyCaster(pawn);
            dummyCaster.Tick();
            dummyCaster.SpawnSetup(pawn.Map, false);
            verb.caster = dummyCaster;
            verb.castCompleteCallback = () => cooldownTicksLeft = Verb.verbProps.AdjustedCooldownTicks(Verb, pawn);
        }

        public void Tick()
        {
            // Log.Message("TurretVerb Tick:");
            // Log.Message("  Bursting: " + Verb.Bursting);
            // Log.Message("  cooldown: " + cooldownTicksLeft);
            // Log.Message("  warmup: " + warmUpTicksLeft);
            // Log.Message("  currentTarget: " + currentTarget);
            Verb.VerbTick();
            if (Verb.Bursting) return;
            if (cooldownTicksLeft > 0)
            {
                cooldownTicksLeft--;
                Log.Message("Cooling down");
            }

            if (cooldownTicksLeft > 0) return;
            if (!currentTarget.IsValid || currentTarget.HasThing && currentTarget.ThingDestroyed)
            {
                Log.Message("Attempting to find a target");
                var man = pawn.Manager();
                man.SearchVerb = Verb;
                currentTarget = (LocalTargetInfo) (Thing) AttackTargetFinder.BestShootTargetFromCurrentPosition(pawn,
                    TargetScanFlags.NeedActiveThreat | TargetScanFlags.NeedLOSToAll |
                    TargetScanFlags.NeedAutoTargetable);
                man.SearchVerb = null;
                TryStartCast();
            }
            else if (warmUpTicksLeft == 0)
            {
                Log.Message("Starting cast!");
                TryCast();
            }
            else if (warmUpTicksLeft > 0)
            {
                Log.Message("Still warming up");
                warmUpTicksLeft--;
            }
            else
            {
                Log.Message("Firing again");
                TryStartCast();
            }
        }

        private void TryStartCast()
        {
            if (Verb.verbProps.warmupTime > 0)
                warmUpTicksLeft = (Verb.verbProps.warmupTime * pawn.GetStatValue(StatDefOf.AimingDelayFactor))
                    .SecondsToTicks();
            else
                TryCast();
        }

        private void TryCast()
        {
            warmUpTicksLeft = -1;
            Log.Message(Verb.TryStartCastOn(currentTarget) ? "Firing!" : "Failed to fire :(");
        }

        public override LocalTargetInfo PointingTarget(Pawn p)
        {
            return currentTarget;
        }
    }

    public class DummyCaster : Thing, IFakeCaster
    {
        private readonly Pawn pawn;

        public DummyCaster(Pawn pawn)
        {
            this.pawn = pawn;
            def = new ThingDef();
        }

        public override Vector3 DrawPos => pawn.DrawPos;

        public Thing RealCaster()
        {
            return pawn;
        }

        public override void Tick()
        {
            Position = pawn.Position;
        }

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }
    }
}