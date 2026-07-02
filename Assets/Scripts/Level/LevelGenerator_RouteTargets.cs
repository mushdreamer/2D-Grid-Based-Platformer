using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public partial class LevelGenerator : MonoBehaviour
{
    private int currentGuidedRouteTargetCount;

    private List<GenerationRouteStep> BuildRouteWithOptionalEnumerationGuidance(Vector2i start, Vector2i end, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        currentGuidedRouteTargetCount = 0;

        List<GenerationRouteStep> route = new List<GenerationRouteStep>();
        if (enableEnumerationGuidedRouteTargets)
        {
            List<Vector2i> intermediateTargets = SelectEnumerationGuidedIntermediateTargets(start, end, zone);
            currentGuidedRouteTargetCount = intermediateTargets.Count;

            foreach (Vector2i target in intermediateTargets)
            {
                route.Add(new GenerationRouteStep
                {
                    endPoint = map.GetMapTilePosition(target.x, target.y),
                    associatedZone = zone
                });
            }

            Debug.Log($"[EnumerationGuidedRoute] targetCount={intermediateTargets.Count}, targets={FormatRouteTargets(intermediateTargets)}");
        }

        route.Add(new GenerationRouteStep
        {
            endPoint = map.GetMapTilePosition(end.x, end.y),
            associatedZone = zone
        });

        return route;
    }

    private List<Vector2i> SelectEnumerationGuidedIntermediateTargets(Vector2i start, Vector2i end, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        List<Vector2i> selected = new List<Vector2i>();
        if (zone == null || zone.tiles == null || zone.tiles.Count == 0 || map == null)
            return selected;

        Dictionary<Vector2i, int> eliteVisitCounts = CountEliteSurvivalVisits();
        List<ScoredRouteTarget> candidates = new List<ScoredRouteTarget>();

        foreach (Vector2i tile in zone.tiles)
        {
            if (!IsValidIntermediateRouteTarget(tile, start, end)) continue;

            float directDistance = DistanceFromLine(tile, start, end);
            if (directDistance < 2.5f) continue;

            int visits;
            eliteVisitCounts.TryGetValue(tile, out visits);

            float verticalDelta = Mathf.Abs(tile.y - start.y) + Mathf.Abs(tile.y - end.y);
            float horizontalDelta = Mathf.Abs(tile.x - start.x) + Mathf.Abs(tile.x - end.x);
            float eliteDistance = DistanceFromEliteTrajectories(tile);
            float underCoveredScore = 20f / (1f + visits);

            float score = underCoveredScore
                + directDistance * 2.0f
                + eliteDistance * 0.25f
                + verticalDelta * 0.35f
                + horizontalDelta * 0.05f;

            candidates.Add(new ScoredRouteTarget { tile = tile, score = score });
        }

        foreach (ScoredRouteTarget candidate in candidates.OrderByDescending(c => c.score))
        {
            if (selected.Count >= 2) break;
            if (selected.Any(t => Mathf.Abs(t.x - candidate.tile.x) + Mathf.Abs(t.y - candidate.tile.y) < 5)) continue;
            selected.Add(candidate.tile);
        }

        selected = selected.OrderBy(t => Vector2.Distance(new Vector2(start.x, start.y), new Vector2(t.x, t.y))).ToList();
        return selected;
    }

    private bool IsValidIntermediateRouteTarget(Vector2i tile, Vector2i start, Vector2i end)
    {
        if (tile.x < 0 || tile.x >= map.mWidth || tile.y < 0 || tile.y >= map.mHeight) return false;
        if (tile == start || tile == end) return false;
        if (Mathf.Abs(tile.x - start.x) + Mathf.Abs(tile.y - start.y) < 5) return false;
        if (Mathf.Abs(tile.x - end.x) + Mathf.Abs(tile.y - end.y) < 5) return false;
        if (map.survivalSpaceTiles != null && !map.survivalSpaceTiles.Contains(tile)) return false;
        return true;
    }

    private Dictionary<Vector2i, int> CountEliteSurvivalVisits()
    {
        Dictionary<Vector2i, int> visits = new Dictionary<Vector2i, int>();
        foreach (LevelIndividual elite in GetAllElitesFromGrid())
        {
            if (elite == null || elite.trajectory == null) continue;
            foreach (Vector3 point in elite.trajectory)
            {
                Vector2i tile = map.GetMapTileAtPoint(point);
                if (map.survivalSpaceTiles != null && !map.survivalSpaceTiles.Contains(tile)) continue;

                if (visits.ContainsKey(tile)) visits[tile]++;
                else visits[tile] = 1;
            }
        }
        return visits;
    }

    private float DistanceFromEliteTrajectories(Vector2i tile)
    {
        float minDistance = float.MaxValue;
        bool found = false;

        foreach (LevelIndividual elite in GetAllElitesFromGrid())
        {
            if (elite == null || elite.trajectory == null) continue;
            foreach (Vector3 point in elite.trajectory)
            {
                Vector2i trajectoryTile = map.GetMapTileAtPoint(point);
                float distance = Mathf.Abs(tile.x - trajectoryTile.x) + Mathf.Abs(tile.y - trajectoryTile.y);
                if (distance < minDistance) minDistance = distance;
                found = true;
            }
        }

        return found ? minDistance : 0f;
    }

    private float DistanceFromLine(Vector2i tile, Vector2i start, Vector2i end)
    {
        Vector2 p = new Vector2(tile.x, tile.y);
        Vector2 a = new Vector2(start.x, start.y);
        Vector2 b = new Vector2(end.x, end.y);
        Vector2 ab = b - a;
        float magnitude = ab.sqrMagnitude;
        if (magnitude < 0.001f) return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / magnitude);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(p, closest);
    }

    private string FormatRouteTargets(List<Vector2i> targets)
    {
        if (targets == null || targets.Count == 0) return "none";
        return string.Join(";", targets.Select(t => $"({t.x},{t.y})"));
    }

    private void PopulateSurvivalCoverageMetrics(LevelIndividual individual)
    {
        if (individual == null) return;

        int survivalTileCount = map != null && map.survivalSpaceTiles != null ? map.survivalSpaceTiles.Count : 0;
        HashSet<Vector2i> visited = new HashSet<Vector2i>();

        if (individual.trajectory != null && map != null && map.survivalSpaceTiles != null)
        {
            foreach (Vector3 point in individual.trajectory)
            {
                Vector2i tile = map.GetMapTileAtPoint(point);
                if (map.survivalSpaceTiles.Contains(tile))
                    visited.Add(tile);
            }
        }

        individual.survivalSpaceTileCount = survivalTileCount;
        individual.visitedSurvivalTileCount = visited.Count;
        individual.survivalCoverageRatio = survivalTileCount > 0 ? visited.Count / (float)survivalTileCount : 0f;
    }

    private struct ScoredRouteTarget
    {
        public Vector2i tile;
        public float score;
    }
}
