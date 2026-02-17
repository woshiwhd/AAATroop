using Unity.VisualScripting;
using UnityEngine;

namespace Script.VisualScripting.Units.Actions
{
    [UnitTitle("BT/Move To")]
    [UnitCategory("BT/Action")]
    public class BT_MoveToUnit : Unit
    {
        private ControlInput _in;
        private ControlOutput _out;

        private ValueInput _self;
        private ValueInput _targetPos;
        private ValueInput _speed;

        protected override void Definition()
        {
            _self = ValueInput<GameObject>("self", null);
            _targetPos = ValueInput("targetPos", Vector3.zero);
            _speed = ValueInput("speed", 2f);

            _in = ControlInput("in", (flow) =>
            {
                var selfGo = flow.GetValue<GameObject>(_self);
                var target = flow.GetValue<Vector3>(_targetPos);
                var spd = flow.GetValue<float>(_speed);

                if (selfGo != null)
                {
                    Vector3 dir3 = target - selfGo.transform.position;
                    if (dir3.sqrMagnitude > 0.001f)
                    {
                        Vector3 dirNorm = dir3.normalized;
                        var rb = selfGo.GetComponent<Rigidbody2D>();
                        if (rb != null)
                        {
                            rb.velocity = new Vector2(dirNorm.x, dirNorm.y) * spd;
                        }
                        else
                        {
                            selfGo.transform.position = Vector3.MoveTowards(selfGo.transform.position, target, spd * Time.deltaTime);
                        }
                    }
                }

                return _out;
            });

            _out = ControlOutput("out");
            Succession(_in, _out);
            Requirement(_self, _in);
            Requirement(_targetPos, _in);
        }
    }
}
