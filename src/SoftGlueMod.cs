using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace lionfox_softglue
{
    public class SoftGlueConfig
    {
        public bool enabled = true;
        public float hardenHours = 0f;
    }

    // Cooked pitch glue (glueportion-pitch-hot) ships with a Harden transition that turns
    // it into the cold variant after ~4 in-game hours. This mod rewrites that transition's
    // FreshHours at AssetsFinalize so the hot stack either takes a custom number of hours
    // to harden, or — if hardenHours <= 0 — effectively never hardens.
    //
    // Two things actually drive the timer on a real cooked-glue stack:
    //   1. The item template's TransitionableProps (used for items spawned directly, e.g.
    //      from the creative menu or the handbook preview).
    //   2. The cooking recipe's PerishableProps, which BlockCookingContainer.DoSmelt clones
    //      onto the freshly cooked stack via CarryOverFreshness. Vanilla ships two such
    //      recipes (glue.json, glue-reheat.json) and both bake freshHours=4 in.
    // We have to patch both — otherwise creative/handbook show 42k days but actually
    // cooking up glue still gives you a 4-hour stack.
    //
    // Config priority:
    //   1. If configlib is installed, it patches the shipped asset before AssetsLoaded fires,
    //      so reading the asset gives the user-configured values shown in the vsimgui panel.
    //   2. Otherwise, fall back to ModConfig/lionfoxsoftglue.json — a plain editable file
    //      that we create on first run with defaults.
    //
    // The patch is applied once at startup; changing config in-game requires a restart.
    public class SoftGlueMod : ModSystem
    {
        const string ConfigFileName = "lionfoxsoftglue.json";
        const string AssetConfigPath = "lionfoxsoftglue:config/lionfoxsoftglue.json";
        const string HotGlueItemCode = "game:glueportion-pitch-hot";
        // Large enough that no realistic playthrough will reach it (~11k in-game years),
        // small enough to avoid float-precision surprises in the transition math.
        const float NeverHardenHours = 1_000_000f;

        SoftGlueConfig config = new SoftGlueConfig();

        public override void AssetsFinalize(ICoreAPI api)
        {
            LoadConfig(api);
            if (!config.enabled)
            {
                api.Logger.Notification("[lionfox_softglue] Patch disabled by config; leaving glue at vanilla 4h.");
                return;
            }

            float targetHours = config.hardenHours > 0f ? config.hardenHours : NeverHardenHours;
            string label = config.hardenHours > 0f ? $"{config.hardenHours}h" : "never (large sentinel)";

            ApplyToGlueItem(api, targetHours, label);
            ApplyToCookingRecipes(api, targetHours, label);
        }

        void LoadConfig(ICoreAPI api)
        {
            if (api.ModLoader.IsModEnabled("configlib"))
            {
                var asset = api.Assets.TryGet(new AssetLocation(AssetConfigPath));
                if (asset != null)
                {
                    try
                    {
                        config = asset.ToObject<SoftGlueConfig>();
                        api.Logger.Notification($"[lionfox_softglue] configlib config: enabled={config.enabled}, hardenHours={config.hardenHours}");
                        return;
                    }
                    catch (Exception e)
                    {
                        api.Logger.Warning($"[lionfox_softglue] Failed to parse configlib asset, falling back to ModConfig: {e.Message}");
                    }
                }
                else
                {
                    api.Logger.Warning($"[lionfox_softglue] configlib is loaded but '{AssetConfigPath}' is missing, falling back to ModConfig.");
                }
            }

            // ModConfig fallback: plain editable JSON in the server/client's ModConfig folder.
            try
            {
                var loaded = api.LoadModConfig<SoftGlueConfig>(ConfigFileName);
                if (loaded == null)
                {
                    api.StoreModConfig(config, ConfigFileName);
                    api.Logger.Notification($"[lionfox_softglue] Wrote default ModConfig/{ConfigFileName} (enabled={config.enabled}, hardenHours={config.hardenHours}).");
                }
                else
                {
                    config = loaded;
                    api.Logger.Notification($"[lionfox_softglue] ModConfig: enabled={config.enabled}, hardenHours={config.hardenHours}");
                }
            }
            catch (Exception e)
            {
                api.Logger.Warning($"[lionfox_softglue] ModConfig read failed ({e.Message}); using defaults.");
            }
        }

        void ApplyToGlueItem(ICoreAPI api, float targetHours, string label)
        {
            var item = api.World.GetItem(new AssetLocation(HotGlueItemCode));
            if (item == null)
            {
                api.Logger.Warning($"[lionfox_softglue] Item '{HotGlueItemCode}' not found; nothing to patch on the item template.");
                return;
            }
            if (item.TransitionableProps == null || item.TransitionableProps.Length == 0)
            {
                api.Logger.Warning($"[lionfox_softglue] '{HotGlueItemCode}' has no TransitionableProps; nothing to patch.");
                return;
            }

            int patched = 0;
            foreach (var tp in item.TransitionableProps)
            {
                if (tp == null || tp.Type != EnumTransitionType.Harden) continue;
                tp.FreshHours = new NatFloat(targetHours, 0f, EnumDistribution.UNIFORM);
                patched++;
            }

            if (patched == 0)
            {
                api.Logger.Warning($"[lionfox_softglue] No Harden transition found on '{HotGlueItemCode}'.");
                return;
            }

            api.Logger.Notification($"[lionfox_softglue] Patched {patched} Harden transition(s) on item '{HotGlueItemCode}' to fresh-for {label}.");
        }

        // BlockCookingContainer.DoSmelt clones recipe.PerishableProps onto the freshly cooked
        // stack via CollectibleObject.CarryOverFreshness. Vanilla ships glue.json and
        // glue-reheat.json, both of which produce glueportion-pitch-hot via CooksInto and
        // carry their own freshHours=4 Harden block. Any modded recipe that also produces
        // hot pitch glue (e.g. an alt source) gets the same treatment.
        void ApplyToCookingRecipes(ICoreAPI api, float targetHours, string label)
        {
            List<CookingRecipe>? recipes = null;
            try
            {
                recipes = api.GetCookingRecipes();
            }
            catch (Exception e)
            {
                api.Logger.Warning($"[lionfox_softglue] Could not get cooking recipes ({e.Message}); skipping recipe patch.");
                return;
            }
            if (recipes == null)
            {
                api.Logger.Warning("[lionfox_softglue] Cooking recipe list is null; skipping recipe patch.");
                return;
            }

            var hotGlueLocation = new AssetLocation(HotGlueItemCode);
            int patched = 0;
            foreach (var recipe in recipes)
            {
                if (recipe?.PerishableProps == null) continue;
                if (recipe.PerishableProps.Type != EnumTransitionType.Harden) continue;
                if (recipe.CooksInto?.Code == null) continue;
                if (!recipe.CooksInto.Code.Equals(hotGlueLocation)) continue;

                recipe.PerishableProps.FreshHours = new NatFloat(targetHours, 0f, EnumDistribution.UNIFORM);
                patched++;
                api.Logger.VerboseDebug($"[lionfox_softglue] Patched cooking recipe '{recipe.Code}' PerishableProps.FreshHours to {label}.");
            }

            if (patched == 0)
            {
                api.Logger.Warning("[lionfox_softglue] No cooking recipe produces 'glueportion-pitch-hot' with a Harden transition; cooked-glue path NOT patched.");
                return;
            }

            api.Logger.Notification($"[lionfox_softglue] Patched {patched} cooking recipe(s) producing '{HotGlueItemCode}' to fresh-for {label}.");
        }
    }
}
