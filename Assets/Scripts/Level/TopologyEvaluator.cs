using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class TopologyEvaluator
{
    public static void EvaluateIndividual(LevelIndividual ind, SurvivalSpaceAnalyzer.SurvivalZone zone, RiskFieldSolver riskSolver)
    {
        if (ind.trajectory == null || ind.trajectory.Count < 2)
        {
            ind.fitness = 0f;
            return;
        }

        float kinematicScore = CalculateKinematicComplexity(ind.trajectory);
        float topologyScore = CalculateTopologyMatch(ind.trajectory, zone);
        float spatialScore = CalculateSpatialUtilization(ind.trajectory, zone.bounds);

        float targetDensity = 0.8f;
        float densityScore = 1.0f - Mathf.Abs(targetDensity - ind.inputDensity);
        float linearityScore = 1.0f - ind.linearity;

        // [核心重构] 计算路径的期望死亡率积分
        float pathRiskIntegral = CalculateRiskPathIntegral(ind.trajectory, riskSolver);

        // 假设我们期望关卡的理想生存压力 (期望死亡率) 为 0.15 (即15%概率死亡)
        float targetRiskExperience = 0.15f;
        // 采用高斯核衰减函数：实际积分越偏离目标体验，得分越低
        float riskMatchScore = Mathf.Exp(-Mathf.Pow(pathRiskIntegral - targetRiskExperience, 2) / 0.05f);

        // 将风险积分拟合度作为最高权重的评价维度并入适应度函数
        ind.fitness = (kinematicScore * 0.1f) +
                      (topologyScore * 0.2f) +
                      (spatialScore * 0.1f) +
                      (densityScore * 0.1f) +
                      (linearityScore * 0.1f) +
                      (riskMatchScore * 0.4f) + // 40% 的权重交由物理场控制
                      (ind.trajectory.Count * 0.001f);
    }

    // [新增] 沿物理轨迹进行离散化的连续场路径积分
    private static float CalculateRiskPathIntegral(List<Vector3> trajectory, RiskFieldSolver solver)
    {
        if (solver == null || trajectory.Count == 0) return 0f;

        float totalRisk = 0f;
        foreach (var point in trajectory)
        {
            totalRisk += solver.GetRiskAtContinuousPosition(point);
        }

        // 返回归一化的平均期望死亡率
        return totalRisk / trajectory.Count;
    }

    private static float CalculateKinematicComplexity(List<Vector3> trajectory)
    {
        int parabolicArcs = 0;
        bool inAir = false;
        float previousVerticalVelocity = 0f;
        float apexCount = 0f;

        for (int i = 1; i < trajectory.Count; i++)
        {
            float currentVerticalVelocity = trajectory[i].y - trajectory[i - 1].y;

            if (Mathf.Abs(currentVerticalVelocity) > 0.05f)
            {
                if (!inAir && currentVerticalVelocity > 0)
                {
                    inAir = true;
                    parabolicArcs++;
                }

                if (inAir && previousVerticalVelocity > 0 && currentVerticalVelocity <= 0)
                {
                    apexCount++;
                }
            }
            else if (inAir && Mathf.Abs(currentVerticalVelocity) <= 0.05f)
            {
                inAir = false;
            }

            previousVerticalVelocity = currentVerticalVelocity;
        }

        float expectedArcs = trajectory.Count / 40f;
        float complexity = (parabolicArcs + apexCount * 1.5f) / (expectedArcs + 1f);
        return Mathf.Clamp01(complexity);
    }

    private static float CalculateTopologyMatch(List<Vector3> trajectory, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        if (zone == null) return 0.5f;

        Vector3 start = trajectory.First();
        Vector3 end = trajectory.Last();
        float deltaX = Mathf.Abs(end.x - start.x);
        float deltaY = Mathf.Abs(end.y - start.y);

        switch (zone.geometryType)
        {
            case SurvivalSpaceAnalyzer.ZoneGeometry.HorizontalCorridor:
                return Mathf.Clamp01(deltaX / (deltaY + 0.1f)) * 0.5f + 0.5f;
            case SurvivalSpaceAnalyzer.ZoneGeometry.VerticalShaft:
                return Mathf.Clamp01(deltaY / (deltaX + 0.1f)) * 0.5f + 0.5f;
            case SurvivalSpaceAnalyzer.ZoneGeometry.SquareRoom:
                float optimalDiagonal = Mathf.Sqrt(zone.bounds.width * zone.bounds.width + zone.bounds.height * zone.bounds.height);
                float actualDisplacement = Vector3.Distance(start, end);
                return Mathf.Clamp01(actualDisplacement / (optimalDiagonal + 0.1f));
            case SurvivalSpaceAnalyzer.ZoneGeometry.OrganicShape:
                float pathLength = 0f;
                for (int i = 1; i < trajectory.Count; i++) pathLength += Vector3.Distance(trajectory[i - 1], trajectory[i]);
                return Mathf.Clamp01((deltaX + deltaY) / (pathLength + 0.1f));
            default:
                return 0.5f;
        }
    }

    private static float CalculateSpatialUtilization(List<Vector3> trajectory, Rect bounds)
    {
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var p in trajectory)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float trajWidth = maxX - minX;
        float trajHeight = maxY - minY;

        float widthRatio = Mathf.Clamp01(trajWidth / (bounds.width + 0.1f));
        float heightRatio = Mathf.Clamp01(trajHeight / (bounds.height + 0.1f));

        return (widthRatio + heightRatio) / 2f;
    }
}