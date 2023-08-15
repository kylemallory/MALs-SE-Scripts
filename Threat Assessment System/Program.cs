using EmptyKeys.UserInterface.Generated.StoreBlockView_Bindings;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GUI;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.EntityComponents.Blocks;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.GameServices;
using VRage.Scripting;
using VRageMath;



namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        public static string basePattern = "TARS";
        public static string debugPattern = "DEBUG";
        public static string actionPattern = "Action Runner";

        public static float MaxDetectedDistance = 25000f;

        public static float distanceNear = 250f;
        public static float distanceFar = 7500f;
        public static float distanceCurve = 1f;
        public static float distanceWeight = 1f;

        public static float speedSlow = 10f;
        public static float speedFast = 250f;
        public static float speedCurve = 1f;
        public static float speedWeight = 1f;

        public static float approachNear = 1000f;
        public static float approachFar = 4500f;
        public static float approachCurve = 1f;
        public static float approachWeight = 1f;

        public static float offRatMax = 75;
        public static float offRatCurve = 1;
        public static float offRatWeight = 1f;

        public static float dpsCurve = 1f;
        public static float dpsWeight = 1f;

        public static float interceptWeight = 1f;

        // internal variables
        bool isInitialized = false;
        double activeThreatLevel = 0.0; // this is the current total threat level (sum of all detected threats)
        int nextScan = 5;
        int defcon = 5;
        int lastDefcon = -1;
        int lastHash = 0;
        long lastShieldHP = 0;

        bool showAllTargets = false;

        float totalGridDamage = 0f;
        float lastDamage = 0f;
        bool hasNewDamage = false;
        long lastDamageTick = 0;

        bool useSubgridTurrets = false; //Search turrets on subgrids.
        string turretsTag = ""; //Tag for turrets. Must be <""> if you don't want to use certain turrets
        string spinner = "|/-\\";
        int spinnerIdx = 0;

        //LCD Panels settings
        bool showDetails = true;
        string lcdFontFamily = "Debug";
        float lcdFontSize = 0.6f;
        Color lcdFontColor = new Color(255, 130, 0);
        TextAlignment lcdFontAlignment = TextAlignment.LEFT;

        IMyTextSurface[] graphicLCDs = new IMyTextSurface[0];
        IMyTextSurface[] debugLCDs = new IMyTextSurface[0];
        List<IMyProgrammableBlock> actionBlocks = new List<IMyProgrammableBlock>(); // these are programmable blocks that will be called with an argument when the Defcon level changes


        Dictionary<MyDetectedEntityInfo, Threat> detectedThreats = new Dictionary<MyDetectedEntityInfo, Threat>();

        List<IWeapon> weapons = new List<IWeapon>();
        static WcPbApi wcApi;
        static DsPbApi dsApi;

        // The LCD of the Programmable Block that this script is running on...
        IMyTextSurface debugDisplay;

        StringBuilder debugContent = new StringBuilder();
        StringBuilder history = new StringBuilder();

        public Program()
        {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.

            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            // get the LCD display of the programmable block that I'm running on.
            debugDisplay = Me.GetSurface(0);
            debugDisplay.ContentType = ContentType.TEXT_AND_IMAGE;
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.

            // if ((updateSource & UpdateType.Update10) == 0)
            //    return; //only run on the update1 flag

            // if (argument.Length != 0)
            //    Echo(argument);

            
            // we don't need to scan turrets every run... 
            if (!isInitialized) { 
                isInitialized = initialize();
                if (isInitialized)
                    Runtime.UpdateFrequency = UpdateFrequency.Update10;
                else
                    return;
            }
            debugContent.Clear();
            history.Clear();

            // we don't need to scan turrets every run... 
            if (nextScan-- < 0)
            {
                nextScan = 10;
                SearchTurrets();
                SearchLCDs();
                SearchProgrammableBlocks();
            }

            if (weapons.Count < 1)
            {
                debugContent.AppendLine("Error: No turrents found on grid or subgrids.");
            }
            else
            {
                spinnerIdx = (spinnerIdx + 1) % 4;
                debugContent.Append($"Searching for Threats... {spinner[spinnerIdx]}\n");

                scanGridForNewDamage();
                AssessThreats();

                defcon = CalculateDefenseCondition();
                debugContent.Insert(0, $">>>> DEFCON {defcon} <<<<\n\n");

                // check if our defcon/threat hash has changed; if so, add an event log entry
                if ((getThreatHash() != lastHash) || (lastDefcon != defcon))
                {
                    addEventLog();
                    lastHash = getThreatHash();
                    lastDefcon = defcon;
                }


                if (detectedThreats.Count == 0)
                {
                    debugContent.Append("\n\n  ==  No Viable Threats Detected.  ==  \n\n");
                }
                else
                {
                    foreach (var threat in detectedThreats.Values)
                    {
                        IGC.SendBroadcastMessage("IGC_IFF_MSG", threat.GetIIFTuple());
                        debugContent.Append(threat.ToString(2));
                    }
                }

            }

            debugDisplay.WriteText(debugContent, false);

            // write out all the "threat detail" to the debug LCDs
            foreach (IMyTextSurface lcd in debugLCDs)
                if (lcd != null)
                    lcd.WriteText(debugContent, false);
        }


        /*
         * Identifies and categorizes threats within the defined parameters
         * Returns the number of threats detected
         */
        public int AssessThreats()
        {
            detectedThreats.Clear();

            Dictionary<MyDetectedEntityInfo, float> threats = new Dictionary<MyDetectedEntityInfo, float>();
            wcApi.GetSortedThreats(Me, threats);
            foreach (MyDetectedEntityInfo k in threats.Keys)
            {
                if (!k.IsEmpty())
                {
                    Threat thisThreat = new Threat(wcApi, Me, k, threats[k]);
                    if ((thisThreat.effectiveDPS == 0.0) || (thisThreat.offenseRating == 0.0))
                        continue;

                    detectedThreats.Add(k, thisThreat);
                }
            }

            return detectedThreats.Count;
        }

        public int CalculateDefenseCondition()
        {
            if (dsApi != null)
            {
                long curShieldHP = (long)(dsApi.GetCharge() * dsApi.GetMaxHpCap());
                if (curShieldHP < lastShieldHP)
                {
                    lastShieldHP = curShieldHP;
                    debugContent.AppendLine($"Shield is taking damage! (was {lastShieldHP} is now {curShieldHP})");
                    return 1; // our shield is actively taking damage... go straight to defcon 1
                }
            }

            if (hasNewDamage)
            {
                debugContent.AppendLine($"Grid is taking damage! ({hasNewDamage})");
                return 1; // we are detecting damage on the grid
            }

            // int insertPoint = debugContent.Length; // we want to insert the total activeThreatLevel above the list of threats, so keep this reference until we're done below
            activeThreatLevel = 0;
            foreach (Threat t in detectedThreats.Values)
            {
                if (t.effectiveDPS == 0.0)
                    continue;

                Echo($"  {t.Name} :: {t.getOverallFactor():0.#####}");
                activeThreatLevel += t.getOverallFactor();
            }
            Echo($"Active Threat Level: >>> {activeThreatLevel:0.######} <<<");


            if (activeThreatLevel > 5.0)
                return 1;
            if (activeThreatLevel > 3.0)
                return 2;
            if (activeThreatLevel > 1)
                return 3;
            if (activeThreatLevel > 0.05)
                return 4;

            return 5;
        }

        /**
         * Adds a new event entry to CustomData
         **/
        public void addEventLog()
        {
            System.DateTime now = System.DateTime.UtcNow;
            history.AppendLine($"======================");
            history.AppendLine($"{now}: >>> DEFCON {defcon} ({activeThreatLevel:0.###}) <<<");

            foreach (IMyProgrammableBlock pb in actionBlocks)
                if (pb != null)
                    pb.TryRun($"DEFCON_{defcon}");

            // let's keep a history of the detected threats and their threat-level
            foreach (Threat threat in detectedThreats.Values)
            {
                history.AppendLine($"  > {threat}");
                debugContent.AppendLine(threat.ToString(1, null));
            }

            // limit the customData's history to only the top 20 lines (as new lines should be at the top)
            history.Append(Me.CustomData);
            String[] lines = history.ToString().Split('\n');

            history.Clear();
            for (int i = 0; i < Math.Min(100, lines.Length); i++)
                history.AppendLine(lines[i]);

            Me.CustomData = history.ToString();
        }


        void SearchTurrets()
        {
            weapons.Clear();

            List<IMyTerminalBlock> moddedTurrets = new List<IMyTerminalBlock>();
            List<IMyLargeTurretBase> vanillaTurrets = new List<IMyLargeTurretBase>();
            List<IMyTurretControlBlock> customTurrets = new List<IMyTurretControlBlock>();

            List<MyDefinitionId> tempIds = new List<MyDefinitionId>();
            wcApi.GetAllCoreTurrets(tempIds);
            List<string> defSubIds = new List<string>();
            tempIds.ForEach(x => defSubIds.Add(x.SubtypeName));

            if (!useSubgridTurrets)
            {
                // only use turrets on My grid
                GridTerminalSystem.GetBlocksOfType(vanillaTurrets, b => b.CubeGrid == Me.CubeGrid);
                GridTerminalSystem.GetBlocksOfType(customTurrets, b => b.CubeGrid == Me.CubeGrid);
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
                    moddedTurrets, b => b.CubeGrid == Me.CubeGrid &&
                        defSubIds.Contains(b.BlockDefinition.SubtypeName));
            }
            else
            {
                // use turrets from all connected grids
                GridTerminalSystem.GetBlocksOfType(vanillaTurrets);
                GridTerminalSystem.GetBlocksOfType(customTurrets);
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
                    moddedTurrets, b => defSubIds.Contains(b.BlockDefinition.SubtypeName));
            }

            if (turretsTag != "")
            {
                // filter lists of turrets to only those with matching tags
                var groupedBlocks = SearchGrouppedTurrets();

                vanillaTurrets = vanillaTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                vanillaTurrets.AddRange(groupedBlocks.ConvertAll(x => (IMyLargeTurretBase)x));
                vanillaTurrets = vanillaTurrets.Distinct().ToList();

                moddedTurrets = moddedTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                moddedTurrets.AddRange(groupedBlocks); // FIXME: this is kind of flawed, since we are adding to moddedTurrets all groupped turrets, with no type filtering, which includes the other two types which are filtered.
                moddedTurrets = moddedTurrets.Distinct().ToList();

                customTurrets = customTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                customTurrets.AddRange(groupedBlocks.ConvertAll(x => (IMyTurretControlBlock)x));
                customTurrets = customTurrets.Distinct().ToList();
            }

            // convert discovered turrets to "Weapons"
            foreach (var vanillaTurret in vanillaTurrets)
            {
                VanillaWeapon weapon = new VanillaWeapon(vanillaTurret);
                weapons.Add(weapon);
            }

            foreach (var moddedTurret in moddedTurrets)
            {
                ModdedWeapon weapon = new ModdedWeapon(moddedTurret);
                weapons.Add(weapon);
            }

            Echo($"Turrets found on grid:\n  Vanilla: {vanillaTurrets.Count}\n   Modded: {moddedTurrets.Count}\n   Custom: {customTurrets.Count}*\n    Total: {weapons.Count}");
        }

        List<IMyTerminalBlock> SearchGrouppedTurrets()
        {
            Echo("Searching for Grouped Turrets on this grid.");

            // NOTE: This function actually isn't turret specific, its just returns a list of IMyTerminalBlocks that belong to a group, which matches the turretsTag
            // This then returns a complete list of blocks, which are then filtered by type, in the SearchTurrets() function.

            List<IMyTerminalBlock> turrets = new List<IMyTerminalBlock>();
            List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(groups, g => g.Name.Contains(turretsTag));

            foreach (var group in groups)
            {
                List<IMyTerminalBlock> turretsList = new List<IMyTerminalBlock>();
                group.GetBlocks(turretsList);
                turrets.AddRange(turretsList);
            }

            return turrets;
        }

        void SearchLCDs()
        {
/*            if (!isLCDenabled)
            {
                return;
            }
*/
            // first look for debug LCDs
            List<IMyTerminalBlock> tmp_lcds = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(tmp_lcds, b => b.CubeGrid == Me.CubeGrid && ((b is IMyTextSurfaceProvider && (b as IMyTextSurfaceProvider).SurfaceCount > 0) || b is IMyTextSurface) && b.CustomName.Contains(basePattern) && b.CustomName.Contains(debugPattern));
            debugLCDs = new IMyTextSurface[tmp_lcds.Count];

            for (int i = tmp_lcds.Count; i-- > 0;)
            {
                if (tmp_lcds[i] is IMyTextSurfaceProvider)
                {
/*
                    bool cust_si = false;
                    if (tmp_lcds[i].CustomName.Length > (lcdTag.Length + 2) && tmp_lcds[i].CustomName[lcdTag.Length] == '[' && tmp_lcds[i].CustomName[lcdTag.Length + 2] == ']')
                    {
                        int srf_idx = (int)tmp_lcds[i].CustomName[lcdTag.Length + 1] - 48;
                        if ((cust_si = srf_idx > 0 && srf_idx < 10 && (tmp_lcds[i] as IMyTextSurfaceProvider).SurfaceCount > srf_idx))
                            lcds[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(srf_idx);
                    }
                    if (!cust_si)
*/
                    debugLCDs[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(0);
                }
                else debugLCDs[i] = (IMyTextSurface)tmp_lcds[i];

                debugLCDs[i].ContentType = (ContentType)1;
                debugLCDs[i].Font = lcdFontFamily;
                debugLCDs[i].FontSize = lcdFontSize;
                debugLCDs[i].FontColor = lcdFontColor;
                debugLCDs[i].Alignment = lcdFontAlignment;
                debugLCDs[i].ContentType = ContentType.TEXT_AND_IMAGE;
            }
        }

        void SearchProgrammableBlocks()
        {
            GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(actionBlocks, b => b.CubeGrid == Me.CubeGrid && b.CustomName.Contains(actionPattern));
        }

        int getThreatHash()
        {
            unchecked
            {
                int hash = 19;
                foreach (var threat in detectedThreats.Values)
                {
                    hash = hash * 31 + threat.GetHashCode();
                }
                return hash;
            }
        }

        bool scanGridForNewDamage()
        {
            totalGridDamage = 0;
            // maxGridIntegrity = 0;
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                var cube = block.CubeGrid.GetCubeBlock(block.Position);
                totalGridDamage += cube.CurrentDamage;
                // maxGridIntegrity += cube.MaxIntegrity;
            }

            Echo($"Checking grid damage: {totalGridDamage}");
            hasNewDamage = (totalGridDamage - lastDamage) > 0.1;
            lastDamage = totalGridDamage;
            if (hasNewDamage)
            {
                Echo($"Grid is taking damage! (was {lastDamage})");
                lastDamageTick = System.DateTime.UtcNow.Ticks;
            }
            return hasNewDamage;
        }

        public void MyEcho(string msg)
        {
            Echo(msg);
            if (debugDisplay != null)
            {
                debugDisplay.WriteText(msg + "\n", true);
            }
        }

        public bool initialize()
        {
            Echo("Reinitializing APIs...");
            try
            {
                dsApi = new DsPbApi(Me);

                wcApi = new WcPbApi();
                wcApi.Activate(Me);
            } catch (Exception ex)
            {
                return false;
            }

            return true;
        }

        public interface IWeapon
        {
            string Name { get; }
            bool HasTarget();
            MyDetectedEntityInfo getTarget();
        }

        class VanillaWeapon : IWeapon
        {
            IMyLargeTurretBase turret;

            string IWeapon.Name
            {
                get
                {
                    return turret.DisplayName;
                }
            }

            public VanillaWeapon(IMyLargeTurretBase turretBase)
            {
                turret = turretBase;
            }

            public bool HasTarget()
            {
                return turret.IsWorking ? turret.IsShooting : false;
            }
            public MyDetectedEntityInfo getTarget()
            {
                return turret.GetTargetedEntity();
            }

        }

        class ModdedWeapon : IWeapon
        {
            IMyTerminalBlock turret;
            public ModdedWeapon(IMyTerminalBlock turretBlock)
            {
                turret = turretBlock;
            }
            string IWeapon.Name
            {
                get
                {
                    return turret.DisplayNameText;
                }
            }

            public bool HasTarget()
            {
                return !wcApi.GetWeaponTarget(turret, 0).Value.IsEmpty();
            }
            public MyDetectedEntityInfo getTarget()
            {
                return (MyDetectedEntityInfo)wcApi.GetWeaponTarget(turret, 0);
            }
        }


    }
}
