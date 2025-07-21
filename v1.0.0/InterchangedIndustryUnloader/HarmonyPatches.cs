using HarmonyLib;
using Model.Ops;
using Serilog;
using StrangeCustoms.Tracks;
using System;
using System.Reflection;
using Track;

namespace UI
{
    [HarmonyPatch(typeof(DropdownLocationPickerRowData), nameof(DropdownLocationPickerRowData.From), new Type[] { typeof(IndustryComponent), typeof(Area) })]
    static class DropdownLocationPickerRowDataPatch
    {
        [HarmonyPostfix]
        static void Postfix(IndustryComponent industryComponent, Area area, ref DropdownLocationPickerRowData __result)
        {
            if (industryComponent is InterchangedIndustryUnloader interchangedIndustryUnloader)
            {
                __result.Title = "Sell " + interchangedIndustryUnloader.load.description + " via " + interchangedIndustryUnloader.DisplayName;
            }
        }
    }
}

namespace Model.Ops
{
    /*
    [HarmonyPatch(typeof(InterchangedIndustryLoader), MethodType.Constructor)]
    static class InterchangedIndustryLoaderPatch
    {
        [HarmonyPostfix]
        static void InterchangedIndustryLoaderPostfix(InterchangedIndustryLoader __instance)
        {
            Log.Information($"InterchangedIndustryLoader created. load is {(__instance.load == null ? "null" : __instance.load.id)}");
        }
    }
    [HarmonyPatch(typeof(InterchangedIndustryUnloader), MethodType.Constructor)]
    static class InterchangedIndustryUnloaderPatch
    {
        [HarmonyPostfix]
        static void InterchangedIndustryUnloaderPostfix(InterchangedIndustryUnloader __instance)
        {
            Log.Information($"InterchangedIndustryUnloader created. load is {(__instance.load == null ? "null" : __instance.load.id)}");
        }
    } */

    [HarmonyPatch(typeof(Interchange), nameof(Interchange.ServeInterchange))]
    static class InterchangePatch
    {
        [HarmonyPrefix]
        static bool ServeInterchangePrefix(IIndustryContext ctx, Interchange __instance)
        {
            __instance.ServeInterchangedIndustryUnloaders(ctx.Now);
            return true;
        }
    }
}

namespace StrangeCustoms
{
    [HarmonyPatch(typeof(SerializedComponent), MethodType.Constructor, [typeof(IndustryComponent)])]
    static class StrangeCustomsPatchConstructor
    {
        [HarmonyPostfix]
        static void Postfix(IndustryComponent component, SerializedComponent __instance)
        {
            InterchangedIndustryUnloader val8 = (InterchangedIndustryUnloader)(object)((component is InterchangedIndustryUnloader) ? component : null);
            if (val8 != null)
            {
                __instance.LoadId = val8.load.id;
            }
        }
    }

    [HarmonyPatch(typeof(SerializedComponent), "ApplyTo", [typeof(IndustryComponent), typeof(PatchingContext)])]
    static class StrangeCustomsPatchApply
    {
        [HarmonyPostfix]
        static void Postfix(IndustryComponent gameComponent, PatchingContext ctx, SerializedComponent __instance)
        {
            InterchangedIndustryUnloader val8 = (InterchangedIndustryUnloader)(object)((gameComponent is InterchangedIndustryUnloader) ? gameComponent : null);
            if (val8 != null)
            {
                if (__instance.TrackSpans.Length == 0)
                {
                    throw new SCPatchingException("At least one TrackSpan must be specified.", "trackSpans");
                }
                val8.load = ctx.GetLoad(__instance.LoadId ?? throw new SCPatchingException("No LoadId specified", "loadId"));
            }
        }
    }
}
