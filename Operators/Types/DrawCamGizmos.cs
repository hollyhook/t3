using T3.Core;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;

namespace T3.Operators.Types.Id_cdf5dd6a_73dc_4779_a366_df19b69071a6
{
    public class DrawCamGizmos : Instance<DrawCamGizmos>
    {
        [Output(Guid = "6cee53fc-92df-4a9e-b519-da857bdf9419")]
        public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "f322ca22-8200-449c-b09b-618cddf488d3")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> Visibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();


    }
}

