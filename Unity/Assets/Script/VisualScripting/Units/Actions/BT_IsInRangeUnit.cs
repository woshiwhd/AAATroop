using Unity.VisualScripting;
using UnityEngine;

namespace Script.VisualScripting.Units.Actions
{
    [UnitTitle("BT/Is In Range")]
    [UnitCategory("BT/Condition")]
    public class BT_IsInRangeUnit : Unit
    {
        private ControlInput _in;
        private ControlOutput _outSuccess;
        private ControlOutput _outFailure;

        private ValueInput _self;
        private ValueInput _target;
        private ValueInput _range;

        protected override void Definition()
        {
            _self = ValueInput<GameObject>("self", null);
            _target = ValueInput<GameObject>("target", null);
            _range = ValueInput<float>("range", 1.5f);

            _in = ControlInput("in", (flow) =>
            {
                var selfGo = flow.GetValue<GameObject>(_self);
                var targetGo = flow.GetValue<GameObject>(_target);
                var r = flow.GetValue<float>(_range);

                if (selfGo == null || targetGo == null) return _outFailure;

                float sqr = (targetGo.transform.position - selfGo.transform.position).sqrMagnitude;
                return sqr <= r * r ? _outSuccess : _outFailure;
            });

            _outSuccess = ControlOutput("outSuccess");
            _outFailure = ControlOutput("outFailure");

            Succession(_in, _outSuccess);
            Succession(_in, _outFailure);
            Requirement(_self, _in);
            Requirement(_target, _in);
        }
    }
}

