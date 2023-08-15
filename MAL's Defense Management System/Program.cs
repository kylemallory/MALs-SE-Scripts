using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
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

        /**
         * MAL's Defense Management System
         * Based on Foltast's Turret Alarm V2 (v2022.1.5).
         * 
         * This script uses WeaponCore to monitor potential threats within a defined range.  In addition to making a threat assessment based on WeaponCore's "threat level",
         * this script also attempts to monitor and make decisions based on:
         *    - Threat trajectory (advancing vs retreating) [value from -1 (immediate retreat) to 1 (direct advance)]
         *    - Defense perimeter intersection given current trajectory
         *    - Threat trajectory deviation (changing course to intercept)
         *    - Active damage monitoring to the local grids (incoming damage from an undetected source)
         *    
         *    
         * The system provides 4 level of defensive status.
         *    - None     : There are no Ships/grids/structures which pose any level of threat (below the minimal threat level)
         *    - Low      : These are grids which are within the maximum detection range AND are above the minimum WC threat level AND outside the medium threat distance
         *                  - OR
         *                  - or which are departing the defense perimeter.
         *                  - This level is usually sufficient to power on short-range/low-power defensive weapons, power up shields, and generally notify players to be on the look-out.
         *                  
         *    - Moderate : These are grids which have sufficient weapons to pose a moderate risk, are within the defense perimeter, and are or will be intersecting the defense
         * 
         * 
         * Development Thoughts & Notes:
         *  - Speed of advance/departure is a factor to consider, but perhaps importantly is whether they are accellerating or decellerating.  For example a Reaver ship will float slowly
         *  until they detect a signal but will turn aggressively and accellerate directly for the target when detected.  When this occurs the Threat Trajectory will go to 1, as it makes
         *  a direct advance toward the base. However, the speed of approach should be an additional factor which should further raise the threat level
         *      (probably by a factor: 0m/s speed = 1x, 100m/s = 10x, 1000m/s = 100x)
         *      
         *  - Intersection Apex (the point in space when a linearly traveling body is no longer advancing but retreating) and specifically its distance to the ship should also be a
         *  consideration.  A warship that comes on the radar at 6k, but reaches the intersection apex at 5k, despite being a high threat, should possibly be deprioritized. This should be
         *  reflected in a low Trajectory Rating, because the closer the 
         *    
         **/


        float minThreatDistance = 5000.0F; // anything farther out than this won't trigger, regardless of threat level
        float minThreatLevel = 0.001F; // anything above this, and below the mid-level will trigger a low-level alert

        float midThreatDistance = 3500.0F; // anything closer than this distance is an automatic mid-level alert
        float midThreatLevel = 0.01F; // anything above this, and below the high-level will trigger a mid-level alert

        float maxThreatDistance = 1500.0F; // anything close that this distance is an automatic high-level alert
        float maxThreatLevel = 0.1F;  // anything above this will trigger a high-level alert

        string threatHighTriggerName = "HighAlertTrigger"; // Name for timer to start on when one or more high-level threat target is detected
        string threatMidTriggerName = "MidAlertTrigger"; // Name for timer to start on when one or more mid-level threat target is detected
        string threatLowTriggerName = "LowAlertTrigger"; // Name for timer to start on when only low-threat targets are detected
        string lostTriggerName = "AllClearTrigger"; // Name for timer to start on when target is lost or destroyed

        string lcdTag = "[TA LCD]"; //Tag for LCD panels intended for displaying information
        string turretsTag = "Base Defense"; //Tag for turrets. Must be <""> if you don't want to use certain turrets

        // Set 'false' for disable delay or 'true' to enable. By default 'false'
        bool detectionTriggerDelay = false;
        bool lostTriggerDelay = false;

        //Time for delays (in seconds)
        float detectionTriggerDelayTime = 3f;
        float lostTriggerDelayTime = 300f;

        // Set 'false' for disable auto refreshing or 'true' to enable. By default 'true'
        bool refreshEnabled = true;

        //How often the script will refresh the block lists, 100 = 1 sec. By default 600
        int checkRate = 600;

        //Search turrets on subgrids. Can be set to TRUE ('1') or FALSE ('0')
        int searchInMultipleGrids = 0;

        //When enalbed, the enhanced search will be use the WeaponCore method of sorting targets
        //It may help if you are experiencing problems with target detecting by WC blocks in standard mode
        //WARNING: This mode is not support the using of turrets tags
        //by default 'false'
        bool enhancedSearchWCTargets = true;

        //Mesh mode. Can be set to 'DISABLED' ('0'), 'TOWER' ('1'), 'BASE' ('2') where
        //BASE is your main grid (where you want to recieve the info) and TOWER is remote grid with turrets
        //by default set as '0'
        int meshMode = 0;

        // Keep unique id for every grid mesh!
        string broadcastTag = "";

        //Additional debug info
        bool debugMode = true;

        //LCD Panels settings
        string lcdFontFamily = "Monospace";
        float lcdFontSize = 0.8f;
        Color lcdFontColor = new Color(255, 130, 0);
        TextAlignment lcdFontAlignment = TextAlignment.CENTER;

        // The LCD of the Programmable Block that this script is running on...
        IMyTextSurface textDisplay;

        /// <summary>
        /// Do not touch below this line
        /// </summary>
        /// -------------------------------------------------------------------- ///
        IMyTimerBlock timerOnDetection;
        IMyTimerBlock timerOnLost;
        static WcPbApi api;

        List<IWeapon> weapons = new List<IWeapon>();
        Dictionary<MyDetectedEntityInfo, float> threats = new Dictionary<MyDetectedEntityInfo, float>();
        IMyTextSurface[] lcds = new IMyTextSurface[0];

        AlarmStatus currentStatus;
        AlarmStatus previousStatus;
        AlarmStatus lastBroadcastedStatus;

        bool isWCUsed = false;
        bool isInit = false;
        bool isLCDenabled = true;

        string version = "2022.1.5";
        string refreshPass;

        int currentCheckPass = 0;
        int maxInstr = 0;

        public Dictionary<string, Action> actions = new Dictionary<string, Action>();
        public Dictionary<string, string> defaultSettings = new Dictionary<string, string>() {
    {"highAlertTriggerName","HighDetectionTrigger"},
    {"midAlertTriggerName","MidDetectionTrigger"},
    {"lowAlertTriggerName","LowDetectionTrigger"},
    {"allClearTriggerName","AllClearTrigger"},
    {"lostTriggerName","LostTrigger"},
    {"lcdTag","[TA LCD]"},
    {"turretsTag",""},
    {"detectionTriggerDelay","false"},
    {"lostTriggerDelay","false"},
    {"detectionTriggerDelayTime","3"},
    {"lostTriggerDelayTime","3"},
    {"refreshEnabled","true"},
    {"checkRate","600"},
    {"searchInMultipleGrids","0"},
    {"enhancedSearchWCTargets","false"},
    {"meshMode","0"},
    {"broadcastTag",""},
    {"debugMode","false"}
};

        string lastSettingsRawData;

        public Dictionary<string, string> currentSettings = new Dictionary<string, string>();

        IMyBroadcastListener myBroadcastListener;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            api = new WcPbApi();

            currentSettings = defaultSettings;

            if (!ReadSettings())
            {
                ResetSettings();
            }
            else
            {
                ApplySettings();
            }

            Initialize();

            actions = new Dictionary<string, Action>
    {
        {"refresh", Initialize},
        {"refresh switch", delegate{refreshEnabled = !refreshEnabled; } },
        {"lcd switch", delegate{isLCDenabled = !isLCDenabled; } },
        {"debug", delegate{debugMode = !debugMode; } },
        {"settings reset", ResetSettings }
    };

            if (meshMode > 0)
            {
                myBroadcastListener = IGC.RegisterBroadcastListener(broadcastTag);
            }

            textDisplay = Me.GetSurface(0);
        }

        void Initialize()
        {
            RefreshBlocks();
            isInit = true;
        }

        public void Main(string argument)
        {
            Echo($"Turret Alarm v2 by Foltast\nVersion: {version}\n");

            ArgumentHandler(argument);

            if (!isWCUsed)
            {
                try
                {
                    isWCUsed = api.Activate(Me);
                }
                catch (Exception ex)
                {
                    Echo($"ERROR: {ex.Message}");
                }
            }

            if (refreshEnabled)
            {
                refreshPass = (currentCheckPass / 100 + 1).ToString();
                Echo($"Next refresh in: {refreshPass}");
                currentCheckPass--;

                if (currentCheckPass <= 0)
                {
                    if (lastSettingsRawData != Me.CustomData)
                    {
                        ReadSettings();
                        ApplySettings();
                    }

                    RefreshBlocks();
                    currentCheckPass = checkRate;
                }
            }

            currentStatus = GetCurrentStatus();

            if (meshMode > 0)
            {
                Echo("MeshMode is enabled");

                if (meshMode == 2)
                {
                    Echo("MeshMode: Base");
                    MeshListner();
                }
                else
                {
                    Echo("MeshMode: Tower");
                    MeshSender();
                }
            }

            if (previousStatus == AlarmStatus.detected && currentStatus == AlarmStatus.idle)
                currentStatus = AlarmStatus.lost;

            UpdateLCDs();
            SwitchCurrentStatus();

            previousStatus = currentStatus;

            if (debugMode)
            {
                WriteDebug();
            }
        }

        private void MeshListner()
        {
            while (myBroadcastListener.HasPendingMessage)
            {
                MyIGCMessage message = myBroadcastListener.AcceptMessage();

                if (message.Tag == broadcastTag)
                {
                    lastBroadcastedStatus = (AlarmStatus)message.Data;

                    if (lastBroadcastedStatus > currentStatus)
                    {
                        currentStatus = lastBroadcastedStatus;
                    }
                }
            }
        }

        void MeshSender()
        {
            if (previousStatus != currentStatus)
            {
                IGC.SendBroadcastMessage(broadcastTag, (int)currentStatus);
            }
        }

        void RefreshBlocks()
        {
            CheckTriggers();
            SearchLCDs();
            SearchTurrets();
        }

        void ArgumentHandler(string arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                return;
            }

            if (actions.ContainsKey(arg))
            {
                actions[arg]();
            }
        }

        void WriteDebug()
        {
            int instructions = Runtime.CurrentInstructionCount;

            if (maxInstr < instructions)
            {
                maxInstr = instructions;
            }

            Echo("\nDebug info"
                + "\ninstr lmt: " + Runtime.MaxInstructionCount.ToString()
                + "\ninstr cur: " + instructions
                + "\ninstr max: " + maxInstr
                + "\nturrets tag: " + turretsTag
                + "\ncurrent status: " + currentStatus
                + "\nprevious status: " + previousStatus
                + "\nlast broadcasted status: " + lastBroadcastedStatus
                + "\nturrets :" + weapons.Count
                + "\nlcds: " + lcds.Length.ToString()
                + "\ninit: " + isInit.ToString()
                + "\nblocks refreshing: " + refreshEnabled.ToString()
                + "\ncheck rate: " + checkRate.ToString()
                + "\ncurrent pass: " + currentCheckPass.ToString()
                );
        }

        bool IsHaveTriggers()
        {
            if (timerOnDetection == null || timerOnLost == null)
                return false;
            return true;
        }

        public int Step(int value, int step)
        {
            bool ceil = value % step >= step / 2;
            int result = step * (value / step);
            if (ceil)
                result += step;
            return result;
        }

        public void MyEcho(string msg)
        {
            Echo(msg);
            textDisplay.WriteText(msg + "\n", true);
        }

        AlarmStatus GetCurrentStatus()
        {
            textDisplay.WriteText("----====   Threat List   ====----\n", false); // clear the PB's LCD display
            if (weapons.Count < 1)
            {
                if (meshMode == 2)
                {
                    return lastBroadcastedStatus;
                }

                MyEcho("ERROR: No turrets detected");
                return AlarmStatus.idle;
            }

            foreach (var weapon in weapons)
            {
                if (weapon.HaveTargets())
                {
                    //Echo("Status: Enemy detected");
                    //return AlarmStatus.detected;
                }
            }

            bool showSummary = true;
            int alertLevel = 0; // NONE
            if (true)
            {
                //Thanks to @Chuckination for the idea and the ready-made implementation

                threats.Clear();
                api.GetSortedThreats(Me, threats);
                if (threats.Count > 0)
                {
                    foreach (MyDetectedEntityInfo k in threats.Keys)
                    {
                        if (!k.IsEmpty())
                        {
                            Vector3D dirVec = k.BoundingBox.Center - Me.GetPosition();
                            double distance = Step((int)(dirVec.Length() * 1.1), 1);
                            string threatLevelStr = "NONE";

                            if (distance > 8000)
                                continue;

                            if ((distance <= minThreatDistance) && (threats[k] >= minThreatLevel))
                            {
                                threatLevelStr = "LOW";
                                alertLevel = (alertLevel < 1) ? alertLevel = 1 : alertLevel;

                                //return AlarmStatus.detected;
                                if (distance <= midThreatDistance || (threats[k] >= midThreatLevel))
                                {
                                    threatLevelStr = "MEDIUM";
                                    alertLevel = (alertLevel < 2) ? alertLevel = 2 : alertLevel;
                                    // return AlarmStatus.detected;
                                }

                                if (distance <= maxThreatDistance || (threats[k] >= maxThreatLevel))
                                {
                                    threatLevelStr = "HIGH";
                                    alertLevel = (alertLevel < 3) ? alertLevel = 3 : alertLevel;
                                    // return AlarmStatus.detected;
                                }
                            }

                            if (showSummary == true)
                            {
                                MyEcho($"{k.Name} : {threatLevelStr} ({distance.ToString()})");
                            }
                            else
                            {
                                MyEcho("---------------------------");
                                MyEcho("Name: " + k.Name);
                                MyEcho("Threat Level: " + threats[k].ToString() + " " + threatLevelStr);
                                MyEcho("Distance: " + distance.ToString());
                                if (alertLevel > 0)
                                {
                                    MyEcho("EntityID: " + k.EntityId.ToString());
                                    MyEcho("X: " + k.BoundingBox.Center.X.ToString());
                                    MyEcho("Y: " + k.BoundingBox.Center.Y.ToString());
                                    MyEcho("Z: " + k.BoundingBox.Center.Z.ToString());
                                }
                                MyEcho("");
                            }

                        }
                    }
                }
            }
            if (alertLevel > 0)
                return AlarmStatus.detected;

            MyEcho("\n\n  ==  No Viable Threats Detected.  ==  \n\n");
            return AlarmStatus.idle;
        }

        void SwitchCurrentStatus()
        {
            if (meshMode == 1)
            {
                Echo("WARNING: Local triggers are not used in tower mode");
                return;
            }

            if (!IsHaveTriggers())
            {
                Echo("ERROR: No triggers detected");
                currentStatus = AlarmStatus.idle;
                return;
            }

            switch (currentStatus)
            {
                case AlarmStatus.detected:
                    if (previousStatus == AlarmStatus.detected)
                        break;

                    timerOnLost.StopCountdown();

                    if (detectionTriggerDelay)
                    {
                        if (!timerOnDetection.IsCountingDown)
                        {
                            timerOnDetection.StartCountdown();
                        }
                    }
                    else
                    {
                        timerOnLost.StopCountdown();
                        timerOnDetection.Trigger();
                    }
                    break;
                case AlarmStatus.lost:
                    if (lostTriggerDelay)
                    {
                        if (!timerOnLost.IsCountingDown)
                            timerOnLost.StartCountdown();
                    }
                    else
                    {
                        timerOnDetection.StopCountdown();
                        timerOnLost.Trigger();
                    }

                    break;
            }
        }

        void CheckTriggers()
        {
            if (currentCheckPass <= 0)
            {
                if (timerOnDetection == null)
                {
                    // Echo($"WARNING: OnDetection Timer is null, trying to get compatible timer (with name {threatHighTriggerName})");
                    timerOnDetection = GridTerminalSystem.GetBlockWithName(threatHighTriggerName) as IMyTimerBlock;
                    if (timerOnDetection != null)
                        timerOnDetection.TriggerDelay = detectionTriggerDelayTime;
                    else
                        Echo($"ERROR: there is no timer for OnDetection. Please add Timer with name {threatHighTriggerName} and start this script again (Button RUN)");
                }

                if (timerOnDetection == null)
                {
                    // Echo($"WARNING: OnDetection Timer is null, trying to get compatible timer (with name {threatMidTriggerName})");
                    timerOnDetection = GridTerminalSystem.GetBlockWithName(threatMidTriggerName) as IMyTimerBlock;
                    if (timerOnDetection != null)
                        timerOnDetection.TriggerDelay = detectionTriggerDelayTime;
                    else
                        Echo($"ERROR: there is no timer for OnDetection. Please add Timer with name {threatMidTriggerName} and start this script again (Button RUN)");
                }

                if (timerOnDetection == null)
                {
                    // Echo($"WARNING: OnDetection Timer is null, trying to get compatible timer (with name {threatLowTriggerName})");
                    timerOnDetection = GridTerminalSystem.GetBlockWithName(threatLowTriggerName) as IMyTimerBlock;
                    if (timerOnDetection != null)
                        timerOnDetection.TriggerDelay = detectionTriggerDelayTime;
                    else
                        Echo($"ERROR: there is no timer for OnDetection. Please add Timer with name {threatLowTriggerName} and start this script again (Button RUN)");
                }

                if (timerOnLost == null)
                {
                    // Echo($"WARNING: OnLost Timer is null, trying to get compatible timer (with name {lostTriggerName})");
                    timerOnLost = GridTerminalSystem.GetBlockWithName(lostTriggerName) as IMyTimerBlock;
                    if (timerOnLost != null)
                        timerOnLost.TriggerDelay = lostTriggerDelayTime;
                    else
                        Echo($"ERROR: there is no timer for OnLost. Please add Timer with name {lostTriggerName} and start this script again (Button RUN)");
                }
            }
        }

        void SearchTurrets()
        {
            weapons.Clear();

            List<IMyLargeTurretBase> vanillaTurrets = new List<IMyLargeTurretBase>();
            List<IMyTerminalBlock> moddedTurrets = new List<IMyTerminalBlock>();
            List<IMyTurretControlBlock> customTurrets = new List<IMyTurretControlBlock>();

            List<MyDefinitionId> tempIds = new List<MyDefinitionId>();
            api.GetAllCoreTurrets(tempIds);
            List<string> defSubIds = new List<string>();
            tempIds.ForEach(x => defSubIds.Add(x.SubtypeName));

            if (searchInMultipleGrids == 0)
            {
                GridTerminalSystem.GetBlocksOfType(vanillaTurrets, b => b.CubeGrid == Me.CubeGrid);
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
                    moddedTurrets, b => b.CubeGrid == Me.CubeGrid &&
                        defSubIds.Contains(b.BlockDefinition.SubtypeName));
                GridTerminalSystem.GetBlocksOfType(customTurrets, b => b.CubeGrid == Me.CubeGrid);
            }
            else
            {
                GridTerminalSystem.GetBlocksOfType(vanillaTurrets);
                GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(
                    moddedTurrets, b => defSubIds.Contains(b.BlockDefinition.SubtypeName));
                GridTerminalSystem.GetBlocksOfType(customTurrets);
            }

            if (turretsTag != "")
            {
                vanillaTurrets = vanillaTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                vanillaTurrets.AddRange(SearchGrouppedTurrets().ConvertAll(x => (IMyLargeTurretBase)x));
                vanillaTurrets = vanillaTurrets.Distinct().ToList();

                moddedTurrets = moddedTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                moddedTurrets.AddRange(SearchGrouppedTurrets());
                moddedTurrets = moddedTurrets.Distinct().ToList();

                customTurrets = customTurrets.Where(t => t.CustomName.Contains(turretsTag)).ToList();
                customTurrets.AddRange(SearchGrouppedTurrets().ConvertAll(x => (IMyTurretControlBlock)x));
                customTurrets = customTurrets.Distinct().ToList();
            }

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
        }

        List<IMyTerminalBlock> SearchGrouppedTurrets()
        {
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

        void UpdateLCDs()
        {
            if (!isLCDenabled)
            {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("TURRET ALARM INFO PANEL\n\nV2 version: ");
            stringBuilder.Append(version);
            stringBuilder.Append("\n\nMeshMode: ");
            stringBuilder.Append((MeshMode)meshMode);
            stringBuilder.Append("\n\nCurrent Status: ");
            stringBuilder.Append(currentStatus.ToString().ToUpper());

            if (refreshEnabled)
            {
                stringBuilder.Append("\n\nRefresh in: ");
                stringBuilder.Append(currentCheckPass > 50 ? refreshPass : "progress");
            }
            else
            {
                stringBuilder.Append("\n\nRefresh is DISABLED");
            }

            string lcdText = stringBuilder.ToString();

            foreach (var lcd in lcds)
            {
                lcd.WriteText(lcdText);
            }
        }

        void ResetSettings()
        {
            currentSettings = defaultSettings;
            WriteSettings();
        }

        bool ReadSettings()
        {
            string[] dataLines = Me.CustomData.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int linesMatches = 0;

            foreach (var line in dataLines)
            {
                string[] readyToReadData = line.Split(new char[] { '=' }, StringSplitOptions.None);

                if (readyToReadData.Length < 2)
                {
                    continue;
                }

                string key = readyToReadData[0].Trim();

                if (currentSettings.ContainsKey(key))
                {
                    if (string.IsNullOrWhiteSpace(readyToReadData[1]))
                    {
                        currentSettings[key] = "";
                    }
                    else
                    {
                        currentSettings[key] = readyToReadData[1].Trim();
                    }
                    linesMatches++;
                }
            }

            return linesMatches == defaultSettings.Count;
        }

        void WriteSettings()
        {
            Me.CustomData = "";

            foreach (var setting in currentSettings)
            {
                Me.CustomData += $"{setting.Key}={setting.Value}\n";
            }
        }

        void ApplySettings()
        {
            threatHighTriggerName = currentSettings["highAlertTriggerName"];
            threatMidTriggerName = currentSettings["midAlertTriggerName"];
            threatLowTriggerName = currentSettings["lowAlertTriggerName"];
            lostTriggerName = currentSettings["allClearTriggerName"];

            lcdTag = currentSettings["lcdTag"];
            turretsTag = currentSettings["turretsTag"];

            detectionTriggerDelay = bool.Parse(currentSettings["detectionTriggerDelay"]);
            lostTriggerDelay = bool.Parse(currentSettings["lostTriggerDelay"]);

            detectionTriggerDelayTime = float.Parse(currentSettings["detectionTriggerDelayTime"]);
            lostTriggerDelayTime = float.Parse(currentSettings["lostTriggerDelayTime"]);

            refreshEnabled = bool.Parse(currentSettings["refreshEnabled"]);
            checkRate = int.Parse(currentSettings["checkRate"]);
            searchInMultipleGrids = int.Parse(currentSettings["searchInMultipleGrids"]);

            enhancedSearchWCTargets = bool.Parse(currentSettings["enhancedSearchWCTargets"]);

            meshMode = int.Parse(currentSettings["meshMode"]);
            broadcastTag = currentSettings["broadcastTag"];
            debugMode = bool.Parse(currentSettings["debugMode"]);

            lastSettingsRawData = Me.CustomData;
        }

        void SearchLCDs()
        {
            if (!isLCDenabled)
            {
                return;
            }

            List<IMyTerminalBlock> tmp_lcds = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(tmp_lcds, b => b.CubeGrid == Me.CubeGrid && ((b is IMyTextSurfaceProvider && (b as IMyTextSurfaceProvider).SurfaceCount > 0) || b is IMyTextSurface) && b.CustomName.StartsWith(lcdTag));

            lcds = new IMyTextSurface[tmp_lcds.Count];

            for (int i = tmp_lcds.Count; i-- > 0;)
            {
                if (tmp_lcds[i] is IMyTextSurfaceProvider)
                {
                    bool cust_si = false;
                    if (tmp_lcds[i].CustomName.Length > (lcdTag.Length + 2) && tmp_lcds[i].CustomName[lcdTag.Length] == '[' && tmp_lcds[i].CustomName[lcdTag.Length + 2] == ']')
                    {
                        int srf_idx = (int)tmp_lcds[i].CustomName[lcdTag.Length + 1] - 48;
                        if ((cust_si = srf_idx > 0 && srf_idx < 10 && (tmp_lcds[i] as IMyTextSurfaceProvider).SurfaceCount > srf_idx)) lcds[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(srf_idx);
                    }
                    if (!cust_si) lcds[i] = ((IMyTextSurfaceProvider)tmp_lcds[i]).GetSurface(0);
                }
                else lcds[i] = (IMyTextSurface)tmp_lcds[i];

                lcds[i].ContentType = (ContentType)1;
                lcds[i].Font = lcdFontFamily;
                lcds[i].FontSize = lcdFontSize;
                lcds[i].FontColor = lcdFontColor;
                lcds[i].Alignment = lcdFontAlignment;
                lcds[i].ContentType = ContentType.TEXT_AND_IMAGE;
            }
        }

        public class WcPbApi
        {
            Action<ICollection<MyDefinitionId>> getCoreWeapons;
            Action<ICollection<MyDefinitionId>> getCoreTurrets;
            Func<IMyTerminalBlock, int, MyDetectedEntityInfo> getWeaponTarget;
            Action<IMyTerminalBlock, IDictionary<MyDetectedEntityInfo, float>> getSortedThreats;

            public bool Activate(IMyTerminalBlock pbBlock)
            {
                var dict = pbBlock.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(pbBlock);
                if (dict == null) return false;
                return ApiAssign(dict);
            }

            public bool ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
            {
                if (delegates == null)
                    return false;

                AssignMethod(delegates, "GetWeaponTarget", ref getWeaponTarget);
                AssignMethod(delegates, "GetCoreWeapons", ref getCoreWeapons);
                AssignMethod(delegates, "GetCoreTurrets", ref getCoreTurrets);
                AssignMethod(delegates, "GetSortedThreats", ref getSortedThreats);

                return true;
            }

            private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field) where T : class
            {
                if (delegates == null)
                {
                    field = null;
                    return;
                }
                Delegate del;
                if (!delegates.TryGetValue(name, out del))
                    throw new Exception($"{GetType().Name} :: Couldn't find {name} delegate of type {typeof(T)}");
                field = del as T;
                if (field == null)
                    throw new Exception(
                        $"{GetType().Name} :: Delegate {name} is not type {typeof(T)}, instead it's: {del.GetType()}");
            }

            public void GetAllCoreWeapons(ICollection<MyDefinitionId> collection) => getCoreWeapons?.Invoke(collection);

            public void GetAllCoreTurrets(ICollection<MyDefinitionId> collection) => getCoreTurrets?.Invoke(collection);

            public MyDetectedEntityInfo? GetWeaponTarget(IMyTerminalBlock weapon, int weaponId = 0) =>
            getWeaponTarget?.Invoke(weapon, weaponId);

            public void GetSortedThreats(IMyTerminalBlock pBlock, IDictionary<MyDetectedEntityInfo, float> collection) =>
            getSortedThreats?.Invoke(pBlock, collection);
        }

        public enum AlarmStatus
        {
            idle,
            detected,
            lost
        }

        public enum MeshMode
        {
            Disabled,
            Tower,
            Base
        }

        interface IWeapon
        {
            bool HaveTargets();
        }

        class VanillaWeapon : IWeapon
        {
            IMyLargeTurretBase turret;

            public VanillaWeapon(IMyLargeTurretBase turretBase)
            {
                turret = turretBase;
            }

            public bool HaveTargets()
            {
                return turret.IsWorking ? turret.IsShooting : false;
            }
        }

        class ModdedWeapon : IWeapon
        {
            IMyTerminalBlock turret;
            public ModdedWeapon(IMyTerminalBlock turretBlock)
            {
                turret = turretBlock;
            }

            public bool HaveTargets()
            {
                MyDetectedEntityInfo? entity = api.GetWeaponTarget(turret, 0);

                return !entity.Value.IsEmpty();
            }
        }
    }
}
