using System.Collections.Generic;
using System.Linq;
using MVCF.Comps;
using RimWorld;
using UnityEngine;
using Verse;

namespace MVCF.Utilities
{
    public static class PawnVerbGizmoUtility
    {
        private static readonly Dictionary<string, string> __truncateCache = new Dictionary<string, string>();

        public static IEnumerable<Gizmo> GetGizmosForVerb(this Verb verb, ManagedVerb man = null)
        {
            AdditionalVerbProps props = null;

            Thing ownerThing = null;
            switch (verb.DirectOwner)
            {
                case ThingWithComps twc when twc.TryGetComp<Comp_VerbGiver>() is Comp_VerbGiver giver:
                    ownerThing = twc;
                    props = giver.PropsFor(verb);
                    break;
                case Thing thing:
                    ownerThing = thing;
                    break;
                case Comp_VerbGiver comp:
                    ownerThing = comp.parent;
                    props = comp.PropsFor(verb);
                    break;
                case CompEquippable eq:
                    ownerThing = eq.parent;
                    break;
                case HediffComp_ExtendedVerbGiver hediffGiver:
                    props = hediffGiver.PropsFor(verb);
                    break;
            }

            var gizmo = new Command_VerbTarget();

            if (ownerThing != null)
            {
                gizmo.defaultDesc = FirstNonEmptyString(props?.description, ownerThing.def.LabelCap + ": " + ownerThing
                    .def.description
                    .Truncate(500, __truncateCache)
                    .CapitalizeFirst());
                gizmo.icon = verb.Icon(null, ownerThing);
            }
            else if (verb.DirectOwner is HediffComp_VerbGiver hediffGiver)
            {
                var hediff = hediffGiver.parent;
                gizmo.defaultDesc = FirstNonEmptyString(props?.description, hediff.def.LabelCap + ": " +
                                                                            hediff.def.description
                                                                                .Truncate(500, __truncateCache)
                                                                                .CapitalizeFirst());
                gizmo.icon = verb.Icon(null, null);
            }

            gizmo.tutorTag = "VerbTarget";
            gizmo.verb = verb;
            gizmo.defaultLabel = verb.Label(props);

            if (verb.caster.Faction != Faction.OfPlayer)
            {
                gizmo.Disable("CannotOrderNonControlled".Translate());
            }
            else if (verb.CasterIsPawn)
            {
                if (verb.CasterPawn.WorkTagIsDisabled(WorkTags.Violent))
                    gizmo.Disable("IsIncapableOfViolence".Translate(verb.CasterPawn.LabelShort,
                        verb.CasterPawn));
                else if (!verb.CasterPawn.drafter.Drafted)
                    gizmo.Disable("IsNotDrafted".Translate(verb.CasterPawn.LabelShort,
                        verb.CasterPawn));
            }

            yield return gizmo;

            if (props != null && props.canBeToggled && man != null && verb.caster.Faction == Faction.OfPlayer &&
                props.separateToggle)
                yield return new Command_ToggleVerbUsage(man);
        }

        public static Gizmo GetMainAttackGizmoForPawn(this Pawn pawn)
        {
            var verbs = pawn.Manager().ManagedVerbs;
            var gizmo = new Command_Action
            {
                defaultDesc = "Attack",
                hotKey = KeyBindingDefOf.Misc1,
                icon = TexCommand.SquadAttack,
                action = () =>
                {
                    Find.Targeter.BeginTargeting(TargetingParameters.ForAttackAny(), target =>
                    {
                        var manager = pawn.Manager();
                        manager.CurrentVerb = null;
                        var verb = pawn.BestVerbForTarget(target, verbs.Where(v => v.Enabled && !v.Verb.IsMeleeAttack),
                            manager);
                        verb.OrderForceTarget(target);
                    }, pawn, null, TexCommand.Attack);
                }
            };

            if (pawn.Faction != Faction.OfPlayer)
                gizmo.Disable("CannotOrderNonControlled".Translate());
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                gizmo.Disable("IsIncapableOfViolence".Translate((NamedArgument) pawn.LabelShort, (NamedArgument) pawn));
            else if (!pawn.drafter.Drafted)
                gizmo.Disable("IsNotDrafted".Translate((NamedArgument) pawn.LabelShort, (NamedArgument) pawn));

            return gizmo;
        }

        private static string VerbLabel(Verb verb, AdditionalVerbProps props = null)
        {
            return FirstNonEmptyString(props?.visualLabel, verb.verbProps.label,
                (verb as Verb_LaunchProjectile)?.Projectile.LabelCap, verb.caster?.def?.label);
        }

        public static string FirstNonEmptyString(params string[] strings)
        {
            foreach (var s in strings)
                if (!s.NullOrEmpty())
                    return s;
            return "";
        }

        public static string Label(this Verb verb, AdditionalVerbProps props = null)
        {
            return VerbLabel(verb, props).CapitalizeFirst();
        }

        public static Texture2D Icon(this Verb verb, AdditionalVerbProps props, Thing ownerThing)
        {
            if (props?.ToggleIcon != null && props.ToggleIcon != BaseContent.BadTex) return props.ToggleIcon;
            if (verb.UIIcon != null && verb.verbProps.commandIcon != null && verb.UIIcon != BaseContent.BadTex)
                return verb.UIIcon;
            if (verb is Verb_LaunchProjectile proj) return proj.Projectile.uiIcon;
            if (ownerThing != null) return ownerThing.def.uiIcon;
            return TexCommand.Attack;
        }
    }
}