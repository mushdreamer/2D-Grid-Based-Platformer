using UnityEngine;
using System.Collections.Generic;

public static class LevelMetrics
{
    // 计算轨迹的线性度 (0.0 - 1.0)
    // 越接近 1，说明关卡越平坦直接；越接近 0，说明需要频繁上下翻飞或回头
    public static float CalculateLinearity(List<Vector3> trajectory, Vector2 start, Vector2 end)
    {
        if (trajectory == null || trajectory.Count < 2) return 1.0f;

        float actualPathLength = 0f;
        for (int i = 0; i < trajectory.Count - 1; i++)
        {
            actualPathLength += Vector3.Distance(trajectory[i], trajectory[i + 1]);
        }

        float displacement = Vector2.Distance(start, end);

        // 线性度 = 位移 / 实际路程
        // 如果实际路程远大于位移（比如螺旋升天），这个值会很小
        return Mathf.Clamp01(displacement / actualPathLength);
    }

    // 计算操作密度 (0.0 - 1.0)
    // 统计每秒钟按键状态改变的次数
    public static float CalculateInputDensity(List<ReplayFrame> replay)
    {
        if (replay == null || replay.Count == 0) return 0f;

        int changes = 0;
        bool[] lastInputs = new bool[(int)KeyInput.Count];

        foreach (var frame in replay)
        {
            for (int i = 0; i < frame.inputs.Length; i++)
            {
                if (frame.inputs[i] != lastInputs[i])
                {
                    changes++;
                }
                lastInputs[i] = frame.inputs[i];
            }
        }

        // 归一化：假设每帧都变是极值 (太夸张了)，我们设定一个"高难"阈值
        // 比如平均每 10 帧操作一次就算很忙了
        float maxChanges = replay.Count / 5.0f;
        return Mathf.Clamp01(changes / maxChanges);
    }
}