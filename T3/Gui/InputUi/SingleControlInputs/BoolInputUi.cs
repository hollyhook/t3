﻿using ImGuiNET;

namespace T3.Gui.InputUi.SingleControlInputs
{
    public class BoolInputUi : SingleControlInputUi<bool>
    {
        protected override bool DrawSingleEditControl(string name, ref bool value)
        {
            return ImGui.Checkbox("##boolParam", ref value);
        }

        protected override void DrawReadOnlyControl(string name, ref bool value)
        {
            ImGui.Text(value.ToString());
        }
    }
}