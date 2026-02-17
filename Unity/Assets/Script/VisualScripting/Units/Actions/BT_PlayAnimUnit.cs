using Unity.VisualScripting;
using UnityEngine;

namespace Script.VisualScripting.Units.Actions
{
    [UnitTitle("BT/Play Anim")]
    [UnitCategory("BT/Action")]
    public class BT_PlayAnimUnit : Unit
    {
        private ControlInput _in;
        private ControlOutput _out;

        private ValueInput _self;
        private ValueInput _triggerName;

        protected override void Definition()
        {
            _self = ValueInput<GameObject>("self", null);
            _triggerName = ValueInput("trigger", "Attack");

            _in = ControlInput("in", (flow) =>
            {
                var selfGo = flow.GetValue<GameObject>(_self);
                var trig = flow.GetValue<string>(_triggerName);
                if (selfGo != null && !string.IsNullOrEmpty(trig))
                {
                    var anim = selfGo.GetComponent<Animator>();
                    if (anim != null)
                    {
                        anim.SetTrigger(trig);
                    }
                }
                return _out;
            });

            _out = ControlOutput("out");
            Succession(_in, _out);
            Requirement(_self, _in);
        }
    }
}
