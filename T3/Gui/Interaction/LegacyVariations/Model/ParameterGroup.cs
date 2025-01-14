﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core;
using T3.Core.Operator;
using T3.Gui.Interaction.LegacyVariations.Model;

namespace T3.Gui.Interaction.LegacyVariations
{
    public class ParameterGroup
    {
        public Guid Id = Guid.NewGuid();
        public string Title;
        public List<GroupParameter> Parameters = new List<GroupParameter>(16);
        
        public int Index { get; internal set; }

        // TODO: Do not serialize
        public LegacyPreset ActivePreset { get; set; }
        public List<LegacyPreset> BlendedPresets { get; set; } = new List<LegacyPreset>();

        public GroupParameter AddParameterToIndex(GroupParameter parameter, int index)
        {
            // Extend list
            while (Parameters.Count <= index)
            {
                Parameters.Add(null);
            }

            Parameters[index] = parameter;
            return parameter;
        }

        public void SetActivePreset(LegacyPreset preset)
        {
            if (ActivePreset != null)
                ActivePreset.State = LegacyPreset.States.InActive;
            
            StopBlending();

            ActivePreset = preset;
            if(preset !=null)
                preset.State = LegacyPreset.States.Active;
        }

        public void StopBlending()
        {
            foreach (var p in BlendedPresets)
            {
                p.State = LegacyPreset.States.InActive;
            }
            BlendedPresets.Clear();
        }
        
        public void ToJson(JsonTextWriter writer)
        {
            writer.WriteValue("Id", Id);
            writer.WriteObject("Title", Title);
            
            // TODO: Implement VariationHandling.FindPresetInGroup(group,preset);
            // if (ActivePreset != null)
            // {
            //     writer.WritePropertyName("ActivePresetAddress");
            //     
            // }
            
            writer.WritePropertyName("Parameters");
            writer.WriteStartArray();
            foreach (var param in Parameters)
            {
                GroupParameter.ToJson(param, writer);
            }
            writer.WriteEndArray();
        }

        public static ParameterGroup FromJson(JToken groupToken)
        {
            if (!groupToken.HasValues)
                return null;
            
            var newGroup = new ParameterGroup()
                               {
                                   Id = Guid.Parse(groupToken["Id"].Value<string>()),
                                   Title = groupToken.Value<string>("Title"),
                               };
            
            foreach (var parameterToken in (JArray)groupToken["Parameters"])
            {
                newGroup.Parameters.Add(!parameterToken.HasValues ? null : GroupParameter.FromJson(parameterToken));
            }

            return newGroup;
            //Guid parameterId = Guid.Parse(presetToken["ParameterId"].Value<string>());
        }

        
        public int FindNextFreeParameterIndex()
        {
            for (var i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i] == null)
                    return i;
            }

            return Parameters.Count;
        }
        
        public LegacyPreset BlendStartPreset;
        public LegacyPreset BlendTargetPreset;
        public bool IsTransitionActive => BlendTargetPreset != null;
        public float BlendTransitionProgress;
        public float BlendTransitionDuration { get; internal set; } = 0;
        
    }
}