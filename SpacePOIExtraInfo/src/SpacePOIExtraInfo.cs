using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static Localization;

namespace SpacePOIExtraInfo
{
    public class SpacePOIExtraInfoPatches
    {
        public static LocString LABEL_FORMAT = "<b>{0}</b>";
        public static LocString TOOLTIP_ENTRY_FORMAT = "{0}: {1}";

        // Hook into the method responsible for updating the info on the mass header (the label showing Mass Remaining)
        // Add our own rows here for info related to the POI as a whole (e.g. maximum mass, recharge rate, etc.)
        [HarmonyPatch(typeof(SpacePOISimpleInfoPanel))]
        [HarmonyPatch("RefreshMassHeader")]
        public class SpacePOIMassHeaderInfoPatch
        {
            private static SpacePOISimpleInfoPanel SpacePOIInfoPanel;
            private static GameObject MaxCapacityRow;
            private static GameObject RefillRateRow;
            private static GameObject TimeUntilFullRow;

            public static void Postfix(
                ref SpacePOISimpleInfoPanel __instance,
                HarvestablePOIStates.Instance harvestable,
                CollapsibleDetailContentPanel spacePOIPanel)
            {
                // Instantiate our new UI elements if there are none or the info panel got changed somehow
                if (!ReferenceEquals(SpacePOIInfoPanel, __instance))
                {
                    SimpleInfoScreen simpleInfoScreen = Traverse.Create(__instance).Field("simpleInfoRoot").GetValue() as SimpleInfoScreen;
                    GameObject iconLabelRow = simpleInfoScreen.iconLabelRow;
                    GameObject parentContainer = spacePOIPanel.Content.gameObject;

                    SpacePOIInfoPanel = __instance;
                    MaxCapacityRow = Util.KInstantiateUI(iconLabelRow, parentContainer);
                    TimeUntilFullRow = Util.KInstantiateUI(iconLabelRow, parentContainer);
                    RefillRateRow = Util.KInstantiateUI(iconLabelRow, parentContainer);
                }

                bool isHarvestable = (harvestable != null);

                MaxCapacityRow.SetActive(isHarvestable);
                RefillRateRow.SetActive(isHarvestable);
                TimeUntilFullRow.SetActive(isHarvestable);

                if (!isHarvestable)
                    return;

                var harvestableConfig = harvestable.configuration;
                float maxCapacity = harvestableConfig.GetMaxCapacity();
                float refillRate = maxCapacity / harvestableConfig.GetRechargeTime();
                float timeUntilFull = Mathf.RoundToInt((maxCapacity - harvestable.poiCapacity) / refillRate);

                HierarchyReferences refs;

                // Max capacity
                refs = MaxCapacityRow.GetComponent<HierarchyReferences>();
                refs.GetReference<LocText>("NameLabel").text = string.Format(LABEL_FORMAT, STRINGS.UI.MAX_MASS);
                refs.GetReference<LocText>("ValueLabel").text = GameUtil.GetFormattedMass(maxCapacity);
                refs.GetReference<LocText>("ValueLabel").alignment = TextAlignmentOptions.MidlineRight;

                // Refill rate
                refs = RefillRateRow.GetComponent<HierarchyReferences>();
                refs.GetReference<LocText>("NameLabel").text = string.Format(LABEL_FORMAT, STRINGS.UI.MASS_REFILL_RATE);
                refs.GetReference<LocText>("ValueLabel").text = GameUtil.GetFormattedMass(refillRate, GameUtil.TimeSlice.PerCycle);
                refs.GetReference<LocText>("ValueLabel").alignment = TextAlignmentOptions.MidlineRight;

                // Time until full
                TimeUntilFullRow.SetActive(timeUntilFull > 0);
                if (timeUntilFull > 0)
                {
                    refs = TimeUntilFullRow.GetComponent<HierarchyReferences>();
                    refs.GetReference<LocText>("NameLabel").text = string.Format(LABEL_FORMAT, STRINGS.UI.TIME_UNTIL_FULL_MASS);
                    refs.GetReference<LocText>("ValueLabel").text = GameUtil.GetFormattedCycles(timeUntilFull);
                    refs.GetReference<LocText>("ValueLabel").alignment = TextAlignmentOptions.MidlineRight;
                }
            }
        }

        // Hook into the method responsible for updating the info on the element list.
        // Go back and add our own tooltip to the UI elements created
        //  to show temperature, individual element capacity and refill rate.
        // What if this added collapisble panels instead of a tooltip?
        [HarmonyPatch(typeof(SpacePOISimpleInfoPanel))]
        [HarmonyPatch("RefreshElements")]
        public class SpacePOIElementsInfoPatch
        {
            public static void Postfix(
                ref SpacePOISimpleInfoPanel __instance,
                HarvestablePOIStates.Instance harvestable)
            {
                if (harvestable == null || harvestable.configuration == null ||
                    !(Traverse.Create(__instance).Field("elementRows").GetValue() is Dictionary<Tag, GameObject> elementRows))
                {
                    return;
                }

                Dictionary<SimHashes, float> elementWeights = harvestable.configuration.GetElementsWithWeights();

                float totalWeight = 0;
                foreach(KeyValuePair<SimHashes, float> entry in elementWeights)
                {
                    totalWeight += entry.Value;
                }

                foreach(KeyValuePair<SimHashes, float> entry in elementWeights)
                {
                    SimHashes elementHash = entry.Key;
                    float elementWeight = entry.Value;
                    Tag tag = elementHash.CreateTag();

                    if (elementRows.ContainsKey(tag) && elementRows[tag].activeInHierarchy)
                    {
                        Element elementDef = ElementLoader.FindElementByHash(elementHash);
                        float temperature = elementDef.defaultValues.temperature;
                        float ratio = elementWeight / totalWeight;
                        float currentMass = harvestable.poiCapacity * ratio;
                        float maxMass = harvestable.configuration.GetMaxCapacity() * ratio;
                        float refillRate = maxMass / harvestable.configuration.GetRechargeTime();

                        var TemperatureStr = string.Format(STRINGS.UI.EXTRACTABLE_MATERIAL_AT_TEMPERATURE, elementDef.name, GameUtil.GetFormattedTemperature(temperature));
                        var CurrentMassStr = string.Format(TOOLTIP_ENTRY_FORMAT, STRINGS.UI.MASS_REMAINING, GameUtil.GetFormattedMass(currentMass));
                        var MassCapacityStr = string.Format(TOOLTIP_ENTRY_FORMAT, STRINGS.UI.MAX_MASS, GameUtil.GetFormattedMass(maxMass));
                        var MassRefillStr = string.Format(TOOLTIP_ENTRY_FORMAT, STRINGS.UI.MASS_REFILL_RATE, GameUtil.GetFormattedMass(refillRate, GameUtil.TimeSlice.PerCycle));

                        var TooltipStr = string.Format("{0}\n\n{1}\n{2}\n{3}", TemperatureStr, CurrentMassStr, MassCapacityStr, MassRefillStr);

                        GameObject elementRow = elementRows[tag];
                        var tooltip = elementRow.GetComponent<ToolTip>();

                        if (tooltip == null)
                        {
                            tooltip = elementRow.AddComponent(typeof(ToolTip)) as ToolTip;
                        }

                        tooltip.SetSimpleTooltip(TooltipStr);
                    } else
                    {
                        // Something is wrong; these elements should definitely already be displayed in the UI
                        Debug.Log("[WARNING] SpacePOIExtraInfo couldn't add info to a space POI harvestable element. Please notify the author about this error.");
                    }
                }
            }
        }

        // Hook into the method responsible for refreshing artifact info at the POI and inject our own code:
        // * Edit the artifact row to display the actual artifact.
        // * Enable text-wrapping so long artifact names can fit without overlapping.
        // * Bump the time to recharge to a new line to make it more consistant and have the text fit.


        // TODO: Toolip 
        /**
         * > This object is a <b>{Terrestial|Space} Artifact</b>. It can be collected by a <link>Artifact Transport Module</link>.
         * >
         * > {0} artifacts have been collected here so far.
         * OR
         * > <red>Harvesting this artifact will delete this POI.</red>
        */

        [HarmonyPatch(typeof(SpacePOISimpleInfoPanel))]
        [HarmonyPatch("RefreshArtifacts")]
        public class SpacePOIArtifactInfoPatch
        {
            private static SpacePOISimpleInfoPanel SpacePOIInfoPanel;
            private static GameObject RechargeTimeRow;

            public static void Postfix(ref SpacePOISimpleInfoPanel __instance,
                ArtifactPOIConfigurator artifactConfigurator,
                CollapsibleDetailContentPanel spacePOIPanel)
            {
                if (artifactConfigurator == null || spacePOIPanel == null)
                    return;

                // Add UI changes if they weren't set yet or the info panel got set to a new instance somehow
                if (!ReferenceEquals(SpacePOIInfoPanel, __instance))
                {
                    SimpleInfoScreen simpleInfoScreen = Traverse.Create(__instance).Field("simpleInfoRoot").GetValue() as SimpleInfoScreen;
                    GameObject iconLabelRow = simpleInfoScreen.iconLabelRow;
                    GameObject parentContainer = spacePOIPanel.Content.gameObject;

                    SpacePOIInfoPanel = __instance;
                    RechargeTimeRow = Util.KInstantiateUI(iconLabelRow, parentContainer);

                    // Setup word wrapping - this could really be optimized
                    GameObject artifactRow = Traverse.Create(__instance).Field("artifactRow").GetValue() as GameObject;
                    if (artifactRow != null)
                    {
                        HierarchyReferences refs = artifactRow.GetComponent<HierarchyReferences>();
                        refs.GetReference<LocText>("NameLabel").enableWordWrapping = true;
                        refs.GetReference<LocText>("NameLabel").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                        refs.GetReference<LocText>("ValueLabel").gameObject.GetComponent<LayoutElement>().ignoreLayout = false;
                    }
                }

                // Force the ui element to the bottom in case new element rows were added
                RechargeTimeRow.rectTransform().SetAsLastSibling();

                var smi = artifactConfigurator.GetSMI<ArtifactPOIStates.Instance>();

                string uniqueArtifactID = smi.configuration.GetArtifactID();
                bool isDestroyedOnHarvest = smi.configuration.DestroyOnHarvest();

                //int numHarvests = (int)Traverse.Create(smi).Field("numHarvests").GetValue();

                // Destroyable POI with a pre-assigned artifact (e.g. Russell's teapot)
                if (!string.IsNullOrEmpty(uniqueArtifactID) && isDestroyedOnHarvest)
                {
                    GameObject artifactRow = Traverse.Create(__instance).Field("artifactRow").GetValue() as GameObject;
                    GameObject artifactPrefab = Assets.GetPrefab(uniqueArtifactID);

                    if (artifactPrefab == null)
                        return;

                    HierarchyReferences refs = artifactRow.GetComponent<HierarchyReferences>();
                    refs.GetReference<LocText>("NameLabel").text = artifactPrefab.GetProperName();

                    var uisprite = Def.GetUISprite(uniqueArtifactID);
                    if (uisprite != null)
                    {
                        refs.GetReference<Image>("Icon").sprite = uisprite.first;
                        refs.GetReference<Image>("Icon").color = uisprite.second;
                    }
                }
                // Rechargeable POI with its next artifact assigned - a new artifact is generated each time one is harvested
                else if (!string.IsNullOrEmpty(smi.artifactToHarvest))
                {
                    GameObject artifactRow = Traverse.Create(__instance).Field("artifactRow").GetValue() as GameObject;
                    GameObject artifactPrefab = Assets.GetPrefab(smi.artifactToHarvest);

                    if (artifactPrefab == null)
                        return;

                    bool canHarvestArtifact = smi.HasArtifactAvailableInHexCell();

                    HierarchyReferences refs = artifactRow.GetComponent<HierarchyReferences>();
                    refs.GetReference<LocText>("NameLabel").text = artifactPrefab.GetProperName();
                    refs.GetReference<LocText>("ValueLabel").text = canHarvestArtifact ? STRINGS.UI.AVAILABLE : STRINGS.UI.UNAVAILABLE;

                    var uisprite = Def.GetUISprite(smi.artifactToHarvest);
                    if (uisprite != null)
                    {
                        refs.GetReference<Image>("Icon").sprite = uisprite.first;
                        refs.GetReference<Image>("Icon").color = uisprite.second;
                    }

                    RechargeTimeRow.SetActive(!canHarvestArtifact);
                    if (!canHarvestArtifact)
                    {
                        refs = RechargeTimeRow.GetComponent<HierarchyReferences>();
                        refs.GetReference<LocText>("NameLabel").text = string.Format(LABEL_FORMAT, STRINGS.UI.ARTIFACT_RECHARGE_TIME);
                        refs.GetReference<LocText>("ValueLabel").text = GameUtil.GetFormattedCycles(smi.RechargeTimeRemaining(), forceCycles: true);
                        refs.GetReference<LocText>("ValueLabel").alignment = TextAlignmentOptions.MidlineRight;
                    }
                }
            }
        }

        // Following Aki's translation guide:
        //  https://forums.kleientertainment.com/forums/topic/123339-guide-for-creating-translatable-mods/
        [HarmonyPatch(typeof(Localization), "Initialize")]
        public class LocalizationInitializePatch
        {
            public static void Postfix() => Translate(typeof(STRINGS));

            public static void Translate(Type root)
            {
                RegisterForTranslation(root);
                LoadStrings();
                LocString.CreateLocStringKeys(root, null);
                GenerateStringsTemplate(root, Path.Combine(GetTranslationsPath()));
            }

            private static void LoadStrings()
            {
                string path = Path.Combine(GetTranslationsPath(), GetLocale()?.Code + ".po");
                if (File.Exists(path))
                    OverloadStrings(LoadStringsFile(path, false));
            }

            private static string GetTranslationsPath()
            {
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "translations");

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                return path;
            }
        }
    }
}

