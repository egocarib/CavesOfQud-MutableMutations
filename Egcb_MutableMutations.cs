using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using XRL.UI;

namespace XRL.World.Parts
{
    public class Egcb_MutableMutations : IPart
    {
        [NonSerialized] public static readonly string OptionsCategoryName = "Mutation Costs";
        [NonSerialized] public static readonly string ModOptionPrefix = "MutableMutationsMod:MutationCost:";
        [NonSerialized] public static readonly string ResetMutationValueOptionID = Egcb_MutableMutations.ModOptionPrefix + "RESETVALUES";
        private static UnityEngine.GameObject monitorObject; //used for activating monobehavior (different from Qud's typical GameObject type)
        private static bool bStarted;
        private static Dictionary<string, string> CachedOptionValues;
        private static List<string> OptionsList;
        private static List<MutationCategory> MutationData;
        private static Dictionary<string, MutationCategory> MutationCategoryDict;


        private static string _modDirectory = String.Empty;
        public static string ModDirectory //I haven't found a more convenient way to do this so far
        {
            get
            {
                if (Egcb_MutableMutations._modDirectory == String.Empty)
                {
                    //loop through the mod manager to get our mod's directory path
                    ModManager.ForEachMod(delegate (ModInfo mod)
                    {
                        foreach (string filePath in mod.ScriptFiles)
                        {
                            if (Path.GetFileName(filePath) == "Egcb_MutableMutations.cs")
                            {
                                //we found our mod
                                Egcb_MutableMutations._modDirectory = Path.GetDirectoryName(filePath);
                                return;
                            }
                        }
                    });
                }
                return Egcb_MutableMutations._modDirectory;
            }
        }

        public Egcb_MutableMutations()
        {
            if (Options.OptionsByCategory.Count <= 0 || Options.OptionsByID.Count <= 0)
            {
                Debug.Log("Mutable Mutations Mod - Failed to initialize mod: Options dictionary does not exist.");
                return;
            }
            Egcb_MutableMutations.Bootup();
        }

        private static void Bootup()
        {
            if (Egcb_MutableMutations.bStarted == true)
            {
                return;
            }
            Egcb_MutableMutations.bStarted = true;
            Egcb_MutableMutations.OptionsList = new List<string>();
            Egcb_MutableMutations.CachedOptionValues = new Dictionary<string, string>();
            Egcb_MutableMutations.MutationData = new List<MutationCategory>();
            Egcb_MutableMutations.MutationCategoryDict = new Dictionary<string, MutationCategory>();

            //Load mutation data from the default Mutations.xml file. Only vanilla mods are supported for now.
            //  While I considered loading data from other mods, it seems like that could
            //  cause issues if the user uninstalled one of those mods later and MutableMutations then tried to load data for that mod's mutations.
            //  A possible solution later would be to directly update the Mutations.xml file in that mod's folder, instead of generating an override
            //  (that would probably be necessary anyway because I don't think we can guarantee the order Mods are loaded.)
            Egcb_MutableMutations.LoadVanillaMutations();

            //Create our custom option list for each mutation, adding them to the game's Option list
            Egcb_MutableMutations.CreateOptionsList();

            //Generate a custom Mutations.xml file in our mod's directory. This is unfortunately necessary because the game reloads Mutations.xml
            //files every time that the player re-enters the Character Generation screen, so we can't just change the Costs in MutationFactory
            Egcb_MutableMutations.ApplyMutationOptions(true);

            //start the MonoBehavior Coroutine to poll for the options menu. We'll update Mutations.xml every time the user changes mutations options
            Egcb_MutableMutations.StartOptionsMonitor();
        }

        static private void StartOptionsMonitor()
        {
            if (!Egcb_MutableMutationsMonitor.IsActive)
            {
                //using UnityEngine.GameObject.AddComponent is the only way that I know of to "instantiate" an instance of a
                //class that derives from MonoBehavior. We need MonoBehavior's Coroutine functionality to spin off a separate
                //"thread" to poll for the Options menu, because there is no event available in the game API for a mod to hook into.
                Egcb_MutableMutations.monitorObject = new UnityEngine.GameObject();
                Egcb_MutableMutationsMonitor taskManager = monitorObject.AddComponent<Egcb_MutableMutationsMonitor>();
                taskManager.Initialize();
            }
            return;
        }

        public static void ReapplyOptions()
        {
            //the user selected the option to reset all mutation costs to default
            if (Options.GetOption(Egcb_MutableMutations.ResetMutationValueOptionID) == "Yes")
            {
                Options.SetOption(Egcb_MutableMutations.ResetMutationValueOptionID, "No");
                Egcb_MutableMutations.ResetMutationCosts();
                Egcb_MutableMutations.ApplyMutationOptions(false);
            }
            //check if any mutation cost settings were changed, and if so, rebuild Mutations.xml
            else if (!Egcb_MutableMutations.ValidateCache())
            {
                Egcb_MutableMutations.ApplyMutationOptions(false);
            }
        }

        public static void ResetMutationCosts()
        {
            foreach (MutationCategory mc in Egcb_MutableMutations.MutationData)
            {
                foreach (MutationEntry me in mc.Entries)
                {
                    //reset all mutation costs to the value specified in the core Mutations.xml file
                    string opName = Egcb_MutableMutations.GetOptionNameForMutationCost(me);
                    Options.SetOption(opName, Egcb_MutableMutations.GetDefaultCostForMutationValueArray(me));
                }
            }
            Debug.Log("Mutable Mutations Mod - Reset all mutation costs to default.");
        }

        public static bool ValidateCache()
        {
            if (Egcb_MutableMutations.CachedOptionValues.Count <= 0 || Egcb_MutableMutations.OptionsList.Count <= 0)
            {
                return false; //options haven't been initialized yet (probably should never happen)
            }
            foreach (KeyValuePair<string, string> opSet in Egcb_MutableMutations.CachedOptionValues)
            {
                string cachedVal = opSet.Value;
                string gameVal = Options.GetOption(opSet.Key);
                if (gameVal != cachedVal)
                {
                    return false; //one or more option values changed
                }
            }
            return true; //validated cache - no changes needed
        }

        private static void CreateOptionsList()
        {
            Egcb_MutableMutations.OptionsList.Clear();
            //create a reset option to restore game defaults
            GameOption resetOp = new GameOption();
            resetOp.ID = Egcb_MutableMutations.ResetMutationValueOptionID;
            resetOp.DisplayText = "Reset all mutation costs to default (after closing Options)";
            resetOp.Category = Egcb_MutableMutations.OptionsCategoryName;
            resetOp.Type = "Checkbox";
            resetOp.Default = "No";
            Egcb_MutableMutations.OptionsList.Add(resetOp.ID);
            Egcb_MutableMutations.AddNewGameOption(resetOp);
            foreach (MutationCategory mc in Egcb_MutableMutations.MutationData)
            {
                string mutationCategoryName = ConsoleLib.Console.ColorUtility.StripFormatting(mc.DisplayName).TrimEnd('s');
                foreach (MutationEntry me in mc.Entries)
                {
                    string optionName = Egcb_MutableMutations.GetOptionNameForMutationCost(me);
                    Egcb_MutableMutations.OptionsList.Add(optionName);
                    if (Options.OptionsByID.ContainsKey(optionName) || me.Cost == 0) //also skip 0 cost vanilla entries, if they exist, because we won't really know what Values range to use
                    {
                        continue;
                    }
                    //option doesn't exist, so we'll create it
                    GameOption gameOption = new GameOption();
                    gameOption.ID = optionName;
                    gameOption.DisplayText = Egcb_MutableMutations.GetMutationCostOptionDisplayText(me);
                    gameOption.Category = Egcb_MutableMutations.OptionsCategoryName;
                    gameOption.Type = "Combo";
                    gameOption.Values = Egcb_MutableMutations.GetCostValueArrayForMutation(me);
                    gameOption.Default = Egcb_MutableMutations.GetDefaultCostForMutationValueArray(me);
                    //add to game Options
                    Egcb_MutableMutations.AddNewGameOption(gameOption);
                }
            }
        }

        public static string GetOptionNameForMutationCost(MutationEntry me)
        {
            return Egcb_MutableMutations.ModOptionPrefix + me.DisplayName;
        }

        public static string GetMutationCostOptionDisplayText(MutationEntry me)
        {
            string part1 = me.DisplayName.Substring(0, Math.Min(me.DisplayName.Length, 28)).PadRight(29);
            string part2 = ConsoleLib.Console.ColorUtility.StripFormatting(me.Category.DisplayName).TrimEnd('s');
            return part1 + "&K" + part2;
        }

        public static List<string> GetCostValueArrayForMutation(MutationEntry me)
        {
            if (me.Cost == 0)
            {
                return new List<string> { " 0", " 0", " 0", " 0", " 0", " 0" };
            }
            else if (me.Cost == 1)
            {
                return new List<string> { "         0", " 1", " 2", " 3" };
            }
            else if (me.Cost == 2)
            {
                return new List<string> { "     0", " 1", " 2", " 3", " 4" };
            }
            else if (me.Cost == -1)
            {
                return new List<string> { "-4", "-3", "-2", "-1", " 0    " };
            }

            //else if (me.Cost > 0 && me.Cost <= 3)
            //{
            //    return new List<string> { " 0", " 1", " 2", " 3", " 4", " 5" };
            //}
            //else if (me.Cost < 0 && me.Cost >= -2)
            //{
            //    return new List<string> { "-5", "-4", "-3", "-2", "-1", " 0" };
            //}
            else
            {
                List<string> retVals = new List<string>(6);
                for (int i = me.Cost - 3; i <= me.Cost + 2; i++)
                {
                    int j = (me.Cost > 0) ? (i < 0 ? 0 : i) : (i > 0 ? 0 : i);
                    retVals.Add(j.ToString().PadLeft(2, ' '));
                }
                return retVals;
            }
        }

        public static string GetDefaultCostForMutationValueArray(MutationEntry me)
        {
            return me.Cost.ToString().PadLeft(2, ' ');
        }

        public static bool AddNewGameOption(GameOption gameOption)
        {
            if (!Options.OptionsByCategory.ContainsKey(gameOption.Category))
            {
                Options.OptionsByCategory.Add(gameOption.Category, new List<GameOption>());
            }
            if (!Options.OptionsByID.ContainsKey(gameOption.ID))
            {
                Options.OptionsByCategory[gameOption.Category].Add(gameOption);
                Options.OptionsByID.Add(gameOption.ID, gameOption);
                return true;
            }
            return false;
        }

        //Most of LoadVanillaMutations, LoadCategoryNode, and LoadMutationNode were adapted directly from the game's MutationFactory code
        private static void LoadVanillaMutations()
        {
            Egcb_MutableMutations.MutationData.Clear();
            Egcb_MutableMutations.MutationCategoryDict.Clear();
            using (XmlTextReader stream = DataManager.GetStreamingAssetsXMLStream("Mutations.xml"))
            {
                stream.WhitespaceHandling = WhitespaceHandling.None;
                while (stream.Read())
                {
                    if (stream.Name == "mutations")
                    {
                        while (stream.Read())
                        {
                            if (stream.Name == "category")
                            {
                                MutationCategory mutationCategory = Egcb_MutableMutations.LoadCategoryNode(stream);
                                if (mutationCategory.Name[0] == '-')
                                {
                                    if (Egcb_MutableMutations.MutationCategoryDict.ContainsKey(mutationCategory.Name.Substring(1)))
                                    {
                                        MutationCategory cat = MutationFactory.CategoriesByName[mutationCategory.Name.Substring(1)];
                                        Egcb_MutableMutations.MutationCategoryDict.Remove(mutationCategory.Name.Substring(1));
                                        Egcb_MutableMutations.MutationData.Remove(cat);
                                    }
                                }
                                else if (Egcb_MutableMutations.MutationCategoryDict.ContainsKey(mutationCategory.Name))
                                {
                                    Egcb_MutableMutations.MutationCategoryDict[mutationCategory.Name].MergeWith(mutationCategory);
                                }
                                else
                                {
                                    Egcb_MutableMutations.MutationCategoryDict.Add(mutationCategory.Name, mutationCategory);
                                    Egcb_MutableMutations.MutationData.Add(mutationCategory);
                                }
                            }
                            if (stream.NodeType == XmlNodeType.EndElement && (stream.Name == string.Empty || stream.Name == "mutations"))
                            {
                                break;
                            }
                        }
                    }
                    if (stream.NodeType == XmlNodeType.EndElement && (stream.Name == string.Empty || stream.Name == "mutations"))
                    {
                        break;
                    }
                }
                stream.Close();
            }
        }

        private static MutationCategory LoadCategoryNode(XmlTextReader stream)
        {
            MutationCategory mutationCategory = new MutationCategory
            {
                Name = stream.GetAttribute("Name"),
                DisplayName = stream.GetAttribute("DisplayName"),
                Help = stream.GetAttribute("Help"),
                Stat = stream.GetAttribute("Stat"),
                Property = stream.GetAttribute("Property"),
                ForceProperty = stream.GetAttribute("ForceProperty")
            };
            while (stream.Read())
            {
                if (stream.Name == "mutation")
                {
                    MutationEntry mutationEntry = Egcb_MutableMutations.LoadMutationNode(stream);
                    mutationEntry.Category = mutationCategory;
                    if (!mutationEntry.Prerelease || Options.GetOption("OptionEnablePrereleaseContent", string.Empty) == "Yes")
                    {
                        mutationCategory.Entries.Add(mutationEntry);
                    }
                }
                if (stream.NodeType == XmlNodeType.EndElement && (stream.Name == string.Empty || stream.Name == "category"))
                {
                    return mutationCategory;
                }
            }
            return mutationCategory;
        }

        private static MutationEntry LoadMutationNode(XmlTextReader stream)
        {
            MutationEntry mutationEntry = new MutationEntry
            {
                DisplayName = stream.GetAttribute("Name"),
                Class = stream.GetAttribute("Class"),
                Constructor = stream.GetAttribute("Constructor"),
                Cost = (stream.GetAttribute("Cost") != null) ? Convert.ToInt32(stream.GetAttribute("Cost")) : -999,
                Stat = stream.GetAttribute("Stat") ?? String.Empty,
                Property = stream.GetAttribute("Property") ?? String.Empty,
                ForceProperty = stream.GetAttribute("ForceProperty") ?? String.Empty,
                BearerDescription = stream.GetAttribute("BearerDescription") ?? String.Empty,
                Maximum = (stream.GetAttribute("MaxSelected") != null) ? Convert.ToInt32(stream.GetAttribute("MaxSelected")) : -999,
                MaxLevel = (stream.GetAttribute("MaxLevel") != null) ? Convert.ToInt32(stream.GetAttribute("MaxLevel")) : -999,
                Exclusions = stream.GetAttribute("Exclusions"),
                MutationCode = stream.GetAttribute("Code")
            };
            if (stream.GetAttribute("Prerelease") != null)
            {
                mutationEntry.Prerelease = (stream.GetAttribute("Prerelease").ToUpper() == "TRUE");
            }
            while (stream.Read())
            {
                if (stream.NodeType == XmlNodeType.EndElement && (stream.Name == string.Empty || stream.Name == "mutation"))
                {
                    return mutationEntry;
                }
            }
            return mutationEntry;
        }

        private static void ApplyMutationOptions(bool bInitialLoad)
        {
            if (bInitialLoad)
            {
                Debug.Log("Mutable Mutations Mod - Generating initial mutation cost settings...");
            }
            else
            {
                Debug.Log("Mutable Mutations Mod - Regenerating mutation cost settings...");
            }
            Egcb_MutableMutations.CachedOptionValues.Clear();
            using (StreamWriter xmlStream = new StreamWriter(Path.Combine(Egcb_MutableMutations.ModDirectory, "Mutations.xml"), false))
            {
                XmlWriterSettings xmlSettings = new XmlWriterSettings { Indent = true };
                using (XmlWriter xmlWriter = XmlWriter.Create(xmlStream, xmlSettings))
                {
                    xmlWriter.WriteStartDocument();
                    xmlWriter.WriteStartElement("mutations");
                    foreach (MutationCategory mc in Egcb_MutableMutations.MutationData)
                    {
                        xmlWriter.WriteStartElement("category");
                        xmlWriter.WriteAttributeString("Name", mc.Name);
                        xmlWriter.WriteAttributeString("DisplayName", mc.DisplayName);
                        if (!String.IsNullOrEmpty(mc.Help))
                        {
                            xmlWriter.WriteAttributeString("Help", mc.Help);
                        }
                        if (!String.IsNullOrEmpty(mc.Stat))
                        {
                            xmlWriter.WriteAttributeString("Stat", mc.Stat);
                        }
                        if (!String.IsNullOrEmpty(mc.Property))
                        {
                            xmlWriter.WriteAttributeString("Property", mc.Property);
                        }
                        if (!String.IsNullOrEmpty(mc.ForceProperty))
                        {
                            xmlWriter.WriteAttributeString("ForceProperty", mc.ForceProperty);
                        }
                        foreach (MutationEntry me in mc.Entries)
                        {
                            xmlWriter.WriteStartElement("mutation");
                            string mutationOptionName = Egcb_MutableMutations.GetOptionNameForMutationCost(me);
                            string mutationOptionCost = Options.GetOption(mutationOptionName, String.Empty).Trim();
                            mutationOptionCost = (String.IsNullOrEmpty(mutationOptionCost) ? me.Cost.ToString() : mutationOptionCost);
                            Egcb_MutableMutations.CachedOptionValues.Add(mutationOptionName, mutationOptionCost);

                            xmlWriter.WriteAttributeString("Name", me.DisplayName);
                            xmlWriter.WriteAttributeString("Cost", mutationOptionCost);
                            if (!String.IsNullOrEmpty(me.Stat))
                            {
                                xmlWriter.WriteAttributeString("Stat", me.Stat);
                            }
                            if (me.Maximum != -999)
                            {
                                xmlWriter.WriteAttributeString("MaxSelected", me.Maximum.ToString());
                            }
                            xmlWriter.WriteAttributeString("Class", me.Class);
                            if (!String.IsNullOrEmpty(me.Constructor))
                            {
                                xmlWriter.WriteAttributeString("Constructor", me.Constructor);
                            }
                            xmlWriter.WriteAttributeString("Exclusions", me.Exclusions);
                            if (me.MaxLevel != -999)
                            {
                                xmlWriter.WriteAttributeString("MaxLevel", me.MaxLevel.ToString());
                            }
                            if (!String.IsNullOrEmpty(me.Property))
                            {
                                xmlWriter.WriteAttributeString("Property", me.Property);
                            }
                            if (!String.IsNullOrEmpty(me.ForceProperty))
                            {
                                xmlWriter.WriteAttributeString("ForceProperty", me.ForceProperty);
                            }
                            if (!String.IsNullOrEmpty(me.BearerDescription))
                            {
                                xmlWriter.WriteAttributeString("BearerDescription", me.BearerDescription);
                            }
                            xmlWriter.WriteAttributeString("Code", me.MutationCode);
                            if (me.Prerelease == true)
                            {
                                xmlWriter.WriteAttributeString("Prerelease", "true");
                            }
                            xmlWriter.WriteFullEndElement(); //important to write FullEndElement, game can't parse self-closing element in Mutations.xml yet
                        }
                        xmlWriter.WriteFullEndElement();
                    }
                    xmlWriter.WriteEndDocument();
                    xmlWriter.Flush();
                    xmlWriter.Close();
                }
                xmlStream.Flush();
                xmlStream.Close();
            }
        }
    }
}
