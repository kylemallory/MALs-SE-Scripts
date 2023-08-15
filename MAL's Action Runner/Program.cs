using Sandbox.Game.EntityComponents;
using Sandbox.Game.Lights;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
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

        /**
         * MAL's Action Runner is a script that allows running a set of actions on the a of IMyTerminalBlocks, similar to a Timer Block,
         * but allowing you to run multiple command sets, based on the run parameter into the Action Runner.  Specifically, when you call
         * this PB with an argument of "Do_Thing_A", this script will load the INI formatting custom data section labeled "Do_Thing_A",
         * and will then parse each key-value pair under that section. Each key is used as a selection filter for blocks, and the value is
         * an action that will be performed on each block which matches the filter.  In example form:
         * 
         * CustomData:
         * 
         * [Do_Thing_A]
         * Light:* = turn_off               ; turns off ALL lights
         * Spotlights:Forward = turn_on     ; turns on ONLY spotlights which have "Forward" in the name 
         * Reactor:* = turn_on              ; turns on all reactors
         * Battery:* = recharge             ; turns all batteries into RECHARGE mode
         *
         * A more practical example:
         * 
         * [DEFENSES_READY]
         * "Ship Shield Emitter" = on       ; power up the shield emitter
         * "Ship Shield Controller" = on    ; power on the shield controller
         * "Ship Shield Controller" = down  ; but let's leave the shields down
         * 
         * [DEFENSES_UP]
         * "Ship Shield Controller" = up    ; raises shields
         * turrets:* = on                   ; turn on all weapons
         * turrets:* = ai_aim               ; configure all weapons for ai_aim mode
         * 
         * [DEFENSES_DOWN]
         * "Ship Shield Controller" = down  ; lower shields
         * turrets:* = off                  ; turn off all weapons
         * 
         * [DEFENSES_OFF]
         * "Ship Shield Emitter" = off      ; power down the shield emitter
         * "Ship Shield Controller" = off   ; power off the shield controller
         * turrets:* = off                  ; turn on all weapons
         * 
         * [MY_GROUP]
         * *thruster* = off                 ; turn off all blocks which have "thruster" anywhere in the name
         * *thruster = off                  ; turn off all blocks which END with "thruster"
         * thruster* = off                  ; turn off all blocks which BEGIN with "thruster"
         * Thruster 3 = off                 ; turn off "Thruster 3"
         * <thrusters> = on                 ; turn on all blocks which belong to the "thrusters" 
         * 
         * With the actions setup in Custom data, simply setup normal sensors, timer blocks, etc, to call the Action Runner's PB with an
         * argument that corresponds to the actions to run.
         * 
         **/

        MyIni ini = new MyIni();
        string config = "";
        string lcdPattern = "[AR-DEBUG]";

        IMyTextSurface debugDisplay;
        IMyTextSurface[] debugLCDs = new IMyTextSurface[0];
        bool showDetails = true;
        string lcdFontFamily = "Debug";
        float lcdFontSize = 0.5f;
        Color lcdFontColor = new Color(0, 200, 255);
        TextAlignment lcdFontAlignment = TextAlignment.LEFT;

        StringBuilder runDebug = new StringBuilder();



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

            config = Me.CustomData;

            MyIniParseResult result;
            if (!ini.TryParse(config, out result))
                throw new Exception(result.ToString());

            BuildTypesList();

            debugDisplay = Me.GetSurface(0);
            debugDisplay.ContentType = ContentType.TEXT_AND_IMAGE;

            Echo("We are built!");
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

            // NOTE: This script is intended to ONLY BE MANUALLY EXECUTED using a RUN parameter.
            Echo("We are running!");
            if (argument == "") return;
            if (updateSource == UpdateType.Once) return;

            SearchLCDs();
            runDebug.Clear();

            if (!ini.ContainsSection(argument))
            {
                runDebug.Append(
                    $"No target seciont for '{argument}' found in the CustomData.\n" +
                    $"Confirm that the spelling is correct (case-sensitive), and\n" +
                    $"that you have recompiled the script after making changes.\n");
                return;
            }

            List<MyIniKey> keys = new List<MyIniKey>();
            ini.GetKeys(argument, keys);
            if (keys.Count > 0)
            {
                runDebug.Append($"Running actions for '{argument}'\n");
                foreach (MyIniKey key in keys)
                {
                    string action = ini.Get(key).ToString();
                    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                    GetTerminalBlocks(key.Name, blocks);

                    foreach (IMyTerminalBlock block in blocks)
                    {
                        string[] actions = action.Split('\n');
                        foreach (string a in actions)
                        {
                            // Echo($"  -> {key.Name} => {a}.");
                            DoAction(block, a);
                        }

                        // Echo($"Found matching block of type {block.GetType()} with name {block.Name}\n");
                    }
                }
            }

            debugDisplay.WriteText(runDebug, false);
            foreach (IMyTextSurface lcd in debugLCDs)
                if (lcd != null)
                    lcd.WriteText(runDebug, false);
        }

        public void DoAction(IMyTerminalBlock block, string action)
        {
            if (action.Equals("<ListActions>")) // having <> for the action, lists all available actions for each block matching the requested name/type
            {
                runDebug.Append($"  -> {block.DisplayNameText}\n");
                List<ITerminalAction> actions = new List<ITerminalAction>();
                block.GetActions(actions);
                foreach (var a in actions)
                {
                    runDebug.Append($"    >> {a.Id} : {a.Name}\n");
                }
            }
            else if (action.Equals("<ListProperties>")) // having [] for the action, lists all available properties for each block matching the requested name/type
            {
                runDebug.Append($"  -> {block.DisplayNameText}\n");
                List<ITerminalProperty> props = new List<ITerminalProperty>();
                block.GetProperties(props);
                foreach (var prop in props)
                {
                    if (prop.TypeName.Equals("Color"))
                    {
                        runDebug.Append($"    >> {prop.Id} = {block.GetValueColor(prop.Id)} ({prop.TypeName})\n");
                    }
                    else if (prop.TypeName.Equals("Single"))
                    {
                        runDebug.Append($"    >> {prop.Id} = {block.GetValueFloat(prop.Id)} ({prop.TypeName})\n");
                    }
                    else if (prop.TypeName.Equals("Boolean"))
                    {
                        runDebug.Append($"    >> {prop.Id} = {block.GetValueBool(prop.Id)} ({prop.TypeName})\n");
                    }
                    else if (prop.TypeName.Equals("Int64"))
                    {
                        runDebug.Append($"    >> {prop.Id} = {block.GetValue<long>(prop.Id)} ({prop.TypeName})\n");
                    }
                    else if (prop.TypeName.Equals("StringBuilder"))
                    {
                        runDebug.Append($"    >> {prop.Id} = {block.GetValue<StringBuilder>(prop.Id)} ({prop.TypeName})\n");
                    }
                    else
                    {
                        runDebug.Append($"    >> {prop.Id} = <UNKNOWN> ({prop.TypeName})\n");
                    }
                }
            }
            else if (action.IndexOf(":") >= 0)  // having "name:value" for the action, will attempt to set a block property of "name" with value (must be a valid property name)
            {
                string propName = GetUntilOrEmpty(action);
                string propVal = action.Substring(action.IndexOf(":") + 1);
                ITerminalProperty prop = block.GetProperty(propName);
                if (prop.TypeName.Equals("Color"))
                {
                    Color value = new Color(1f, 1f, 1f);
                    if (propVal.StartsWith("#"))
                    {
                        string cStr = propVal.Substring(1);
                        if (cStr.Length == 6) cStr = "FF" + cStr;
                        uint cI = (uint)Convert.ToInt64(cStr, 16);
                        value = new Color(cI);
                    }
                    block.SetValue<Color>(propName, value);
                    runDebug.Append($"  -> {block.DisplayNameText} : {propName} = {value}(c)\n");
                }
                else if (prop.TypeName.Equals("Single"))
                {
                    float value = Convert.ToSingle(propVal);
                    block.SetValue<float>(propName, value);
                    runDebug.Append($"  -> {block.DisplayNameText} : {propName} = {value}(f)\n");
                }
                else if (prop.TypeName.Equals("Int64"))
                {
                    long value = Int64.Parse(propVal);
                    block.SetValue<long>(propName, value);
                    runDebug.Append($"  -> {block.DisplayNameText} : {propName} = {value}(l)\n");
                }
                else if (prop.TypeName.Equals("Boolean"))
                {
                    bool value = Boolean.Parse(propVal);
                    block.SetValue<bool>(propName, value);
                    runDebug.Append($"  -> {block.DisplayNameText} : {propName} = {value}(b)\n");
                }
                else if (prop.TypeName.Equals("StringBuilder"))
                {
                    var value = new StringBuilder(propVal);
                    block.SetValue<StringBuilder>(propName, value);
                    runDebug.Append($"  -> {block.DisplayNameText} : {propName} = {value}(s)\n");
                }

            }
            else
            {
                List<ITerminalAction> actions = new List<ITerminalAction>();
                if (block.HasAction(action))
                {
                    runDebug.Append($"  -> {block.DisplayNameText} : {action}\n");
                    block.ApplyAction(action);
                }
            }

        }

        public static string GetUntilOrEmpty(string text, string stopAt = ":")
        {
            if (!String.IsNullOrWhiteSpace(text))
            {
                int charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

                if (charLocation > 0)
                {
                    return text.Substring(0, charLocation);
                }
            }

            return String.Empty;
        }

        public int GetTerminalBlocks(string pattern, List<IMyTerminalBlock> blocks)
        {
            // pattern looks like this:
            // [ block_type ]:{ match_pattern }
            //
            // The "block_type" is optional and will always be followed by a ':' (colon).  If a colon exists in the pattern, it means a block_type is specified. Only a single type can be specified.
            // The "match_pattern" is required, and is used as a substring match for any block who's name contains that substring.  A '*' mean to match on all blocks (effectively matching an empty string).
            // If the match_pattern matches a group, then all blocks in that group are selected (even if their individual block names do not match the pattern).

            blocks.Clear();

            Type type;
            string name = "";
            if (pattern.Contains(":"))
            {
                type = getBlockTypeByName(GetUntilOrEmpty(pattern));
                name = pattern.Substring(pattern.IndexOf(":")+1);
                if (name != "*") {
                    Echo($"Getting blocks of type {type} and name {name}\n");
                    GridTerminalSystem.SearchBlocksOfName(name, blocks, block => block.GetType() == type);
                } else
                {
                    Echo($"Getting blocks of type {type}\n");
                    GridTerminalSystem.GetBlocksOfType(blocks, block => block.GetType() == type);
                }
            }
            else
            {
                name = pattern;
                GridTerminalSystem.SearchBlocksOfName(name, blocks);
            }

            return blocks.Count;
        }

        Dictionary<string, Type> types = new Dictionary<string, Type>();
        public void BuildTypesList()
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);
            foreach (var block in blocks)
            {
                string longName = block.BlockDefinition.TypeIdString;
                string shortName = longName.Substring(longName.IndexOf("_") + 1);

                if (types.ContainsKey(shortName))
                    continue;

                types.Add(shortName, block.GetType());
            }
        }

        public Type getBlockTypeByName(string typeName)
        {
            return types[typeName];
        }

        void SearchLCDs()
        {
            // first look for debug LCDs
            List<IMyTerminalBlock> tmp_lcds = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(tmp_lcds, b => b.CubeGrid == Me.CubeGrid && ((b is IMyTextSurfaceProvider && (b as IMyTextSurfaceProvider).SurfaceCount > 0) || b is IMyTextSurface) && b.CustomName.Contains(lcdPattern));
            debugLCDs = new IMyTextSurface[tmp_lcds.Count];

            for (int i = tmp_lcds.Count; i-- > 0;)
            {
                if (tmp_lcds[i] is IMyTextSurfaceProvider)
                {
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


    }
}
