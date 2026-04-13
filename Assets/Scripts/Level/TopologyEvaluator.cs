using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class TopologyEvaluator
{
    [System.Serializable]
    public struct DesignerIntent
    {
        [Range(0f, 1f)] public float riskTension;
        [Range(0f, 1f)] public float mechanicalComplexity;
        [Range(0f, 1f)] public float structuralExploration;
    }

    public static void EvaluateIndividual(LevelIndividual ind, SurvivalSpaceAnalyzer.SurvivalZone zone, RiskFieldSolver riskSolver, DesignerIntent intent)
    {
        if (ind.trajectory == null || ind.trajectory.Count < 2)
        {
            ind.fitness = 0f;
            return;
        }

        float kinematicScore = CalculateKinematicComplexity(ind.trajectory);
        float topologyScore = CalculateTopologyMatch(ind.trajectory, zone);
        float spatialScore = CalculateSpatialUtilization(ind.trajectory, zone.bounds);

        float targetDensity = Mathf.Lerp(0.3f, 0.95f, intent.mechanicalComplexity);
        float densityScore = 1.0f - Mathf.Abs(targetDensity - ind.inputDensity);

        float targetLinearity = Mathf.Lerp(0.9f, 0.1f, intent.structuralExploration);
        float linearityScore = 1.0f - Mathf.Abs(targetLinearity - ind.linearity);

        float pathRiskIntegral = CalculateRiskPathIntegral(ind.trajectory, riskSolver);
        float targetRiskExperience = Mathf.Lerp(0.05f, 0.45f, intent.riskTension);
        float riskVariance = Mathf.Lerp(0.1f, 0.02f, intent.riskTension);
        float riskMatchScore = Mathf.Exp(-Mathf.Pow(pathRiskIntegral - targetRiskExperience, 2) / riskVariance);

        float kinematicWeight = Mathf.Lerp(0.1f, 0.3f, intent.mechanicalComplexity);
        float topologyWeight = Mathf.Lerp(0.1f, 0.3f, intent.structuralExploration);
        float riskWeight = Mathf.Lerp(0.2f, 0.5f, intent.riskTension);
        float spatialWeight = 0.1f;

        float totalWeight = kinematicWeight + topologyWeight + riskWeight + spatialWeight + 0.2f;

        ind.fitness = (kinematicScore * kinematicWeight) +
                      (topologyScore * topologyWeight) +
                      (spatialScore * spatialWeight) +
                      (densityScore * 0.1f) +
                      (linearityScore * 0.1f) +
                      (riskMatchScore * riskWeight);

        ind.fitness /= totalWeight;
        ind.fitness += (ind.trajectory.Count * 0.0005f);
    }

    private static float CalculateRiskPathIntegral(List<Vector3> trajectory, RiskFieldSolver solver)
    {
        if (solver == null || trajectory.Count == 0) return 0f;

        float totalRisk = 0f;
        foreach (var point in trajectory)
        {
            totalRisk += solver.GetRiskAtContinuousPosition(point);
        }

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