using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Script.VisualScripting.Units.Actions
{
    /// <summary>
    /// BT_FindNearestTargetUnit：在 Visual Scripting 中用来查找指定标签（tag）下最近的 GameObject。
    /// 说明（中文注释）：
    /// - 这是一个同步执行的 Unit，接收控制流触发后会立刻执行全局搜索（使用 GameObject.FindGameObjectsWithTag）并返回结果。
    /// - 控制流：in -> outSuccess / outFailure。
    /// - 值端口：
    ///     - 输入：self (GameObject) —— 通常连接为当前对象（This），用于计算距离基准；若为空则使用 Vector3.zero 作为基准。
    ///     - 输入：tag (string) —— 要搜索的标签（默认 "Player"）。
    ///     - 输出：result (GameObject) —— 搜索到的最近目标或 null。
    /// - 返回：如果找到最近目标则走 outSuccess，否则走 outFailure。
    /// 注意事项（性能、安全）：
    /// - 当前实现使用 GameObject.FindGameObjectsWithTag，它会进行全局分配与扫描，不适合大规模频繁调用场景。建议：
    ///     1) 对调用频率进行节流（例如每 0.15-0.5s 调用一次）；
    ///     2) 或改用 Physics2D.OverlapCircleNonAlloc / spatial index 来替代全局查找；
    ///     3) 若项目中敌人较多，优先使用中央管理器（TargetManager）或分帧调度以降低峰值开销。
    /// - 为了兼容不同版本的 Visual Scripting，此 Unit 采用在控制流里用 flow.SetValue 写入 ValueOutput 的方式，而不是在 ValueOutput 的 getter 里计算，避免执行顺序与依赖性问题。
    /// - 异常处理：内部捕获异常并记录（Debug.LogException），确保不会因为单次搜索抛异常影响整体流程。
    /// </summary>
    [UnitTitle("BT/Find Nearest Target")]
    [UnitCategory("BT/Action")]
    public class BT_FindNearestTargetUnit : Unit
    {
        // ----------------------------- 控制端口 ---------------------------------
        // 控制输入：触发本 Unit 执行搜索
        private ControlInput _in;
        // 控制输出：成功与失败分支
        private ControlOutput _outSuccess;
        private ControlOutput _outFailure;

        // ----------------------------- 值端口 ---------------------------------
        // 输入：用于计算距离基准的 GameObject（通常为当前实体）
        private ValueInput _self;
        // 输入：要搜索的标签（例如 "Player"）
        private ValueInput _tag;
        // 输出：搜索结果（最近的 GameObject 或 null）
        private ValueOutput _result;

        // 在 Definition 中声明端口与控制流关系
        protected override void Definition()
        {
            // 定义值输入：self（可为 null）与 tag（默认为 "Player"）
            _self = ValueInput<GameObject>("self", null);
            _tag = ValueInput("tag", "Player");

            // 定义控制输出
            _outSuccess = ControlOutput("outSuccess");
            _outFailure = ControlOutput("outFailure");

            // 定义值输出（注意：此处不在 getter 中计算，结果由 control flow 写入）
            _result = ValueOutput<GameObject>("result");

            // 控制输入：按触发顺序执行搜索逻辑，并把结果写入 _result
            _in = ControlInput("in", (flow) =>
            {
                // 从输入端口获取参数
                var selfGo = flow.GetValue<GameObject>(_self);
                var tag = flow.GetValue<string>(_tag);

                GameObject best = null;
                float bestSqr = float.MaxValue;

                // 参数校验：若 tag 为空，则直接返回失败
                if (string.IsNullOrEmpty(tag))
                {
                    flow.SetValue(_result, null);
                    return _outFailure;
                }

                try
                {
                    // 简单实现：全局按 tag 查找所有候选对象并比较平方距离
                    var candidates = GameObject.FindGameObjectsWithTag(tag);
                    if (candidates != null && candidates.Length > 0)
                    {
                        // selfGo 为空时使用世界原点作为基准（此行为可根据项目需要调整）
                        Vector3 selfPos = selfGo != null ? selfGo.transform.position : Vector3.zero;
                        foreach (var c in candidates)
                        {
                            if (c == null) continue;
                            float sqr = (c.transform.position - selfPos).sqrMagnitude;
                            if (sqr < bestSqr)
                            {
                                bestSqr = sqr;
                                best = c;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 捕获并记录异常，不抛出，保证图执行稳定性
                    Debug.LogException(ex);
                }

                // 把搜索结果写入 ValueOutput，供后续单元读取
                flow.SetValue(_result, best);

                // 根据是否找到目标决定控制流分支
                return best != null ? _outSuccess : _outFailure;
            });

            // 声明依赖关系与控制流连线关系，便于编辑器显示与验证
            Requirement(_self, _in);
            Requirement(_tag, _in);

            Succession(_in, _outSuccess);
            Succession(_in, _outFailure);
        }
    }
}
