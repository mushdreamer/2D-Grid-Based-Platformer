using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using Random = UnityEngine.Random;

public partial class LevelGenerator : MonoBehaviour
{
    [Header("Evolutionary MAP-Elites Settings (演化型网格设定)")]
    public int gaPopulationSize = 20;
    public int gaMaxGenerations = 10;
    public float gaMutationRate = 0.3f;

    public void GenerateEvolutionaryMapElitesLibrary(Vector2i startTile, Vector2i endTile)
    {
        StartCoroutine(GenerateSegmentedEvolutionaryRoutine(startTile, endTile));
    }

    private IEnumerator GenerateSegmentedEvolutionaryRoutine(Vector2i globalStart, Vector2i globalEnd)
    {
        Initialize();
        if (director != null) director.SetRunning(false);
        ClearVisuals();
        InitLog("多空间独立分段生成 MAP-Elites (GA驱动)", gaPopulationSize, gaMaxGenerations);
        failureStatistics.Clear();

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        if (zones.Count == 0)
        {
            Debug.LogError("未能识别到任何生存空间，生成终止。");
            yield break;
        }

        zones = zones.OrderBy(z => z.center.x).ToList();
        HashSet<Vector2i> originalGlobalSurvivalSpace = new HashSet<Vector2i>(map.survivalSpaceTiles);
        List<LevelIndividual> globalBestIndividuals = new List<LevelIndividual>();

        for (int zIndex = 0; zIndex < zones.Count; zIndex++)
        {
            SurvivalSpaceAnalyzer.SurvivalZone currentZone = zones[zIndex];
            Vector2i localStart = DetermineZoneEntry(currentZone, zIndex == 0 ? null : zones[zIndex - 1], globalStart);
            Vector2i localEnd = DetermineZoneExit(currentZone, zIndex == zones.Count - 1 ? null : zones[zIndex + 1], globalEnd);

            HashSet<Vector2i> localSafeTiles = new HashSet<Vector2i>(currentZone.tiles);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    localSafeTiles.Add(new Vector2i(localStart.x + dx, localStart.y + dy));
                    localSafeTiles.Add(new Vector2i(localEnd.x + dx, localEnd.y + dy));
                }
            }
            map.survivalSpaceTiles = localSafeTiles;

            BuildSurvivalGradient(localEnd);
            ShowSurvivalSpaceInGame();

            System.Array.Clear(eliteGrid, 0, eliteGrid.Length);

            List<LevelGenerationPlanner.GenerationStep> localRoute = new List<LevelGenerationPlanner.GenerationStep>();
            localRoute.Add(new LevelGenerationPlanner.GenerationStep
            {
                description = $"Local Zone {zIndex} Internal Navigation",
                startPoint = map.GetMapTilePosition(localStart.x, localStart.y),
                endPoint = map.GetMapTilePosition(localEnd.x, localEnd.y),
                associatedZone = currentZone
            });

            int initialCount = 0;
            int initialAttempts = 0;
            while (initialCount < gaPopulationSize && initialAttempts < maxTotalAttempts)
            {
                initialAttempts++;
                string failReason;
                Vector2 failPos;
                if (RunGuidedSimulation(localStart, localEnd, localRoute, out failReason, out failPos))
                {
                    BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, localStart, localEnd);
                    if (VerifyLevelWithRealPhysics(localStart, localEnd, out failReason, out failPos))
                    {
                        LevelIndividual newInd = CreateIndividualFromGhost(localStart, localEnd);
                        CalculateFitness(newInd);
                        TryPlaceIndividualInGrid(newInd);
                        initialCount++;
                    }
                    else RecordFailure(failReason);
                }
                else RecordFailure(failReason);
                yield return null;
            }

            for (int generation = 1; generation <= gaMaxGenerations; generation++)
            {
                List<LevelIndividual> currentElites = GetAllElitesFromGrid();
                if (currentElites.Count < 2) break;

                int offspringProduced = 0;
                int maxOffspringAttempts = gaPopulationSize;
                while (offspringProduced < gaPopulationSize && maxOffspringAttempts > 0)
                {
                    maxOffspringAttempts--;
                    LevelIndividual parentA = TournamentSelection(currentElites);
                    LevelIndividual parentB = TournamentSelection(currentElites);
                    LevelIndividual offspring = CrossoverAndMutate(parentA, parentB, localStart, localEnd);

                    if (offspring != null)
                    {
                        CalculateFitness(offspring);
                        if (TryPlaceIndividualInGrid(offspring)) offspringProduced++;
                    }
                    yield return null;
                }
            }

            LevelIndividual bestInZone = GetAllElitesFromGrid().OrderByDescending(p => p.fitness).FirstOrDefault();
            if (bestInZone != null)
            {
                globalBestIndividuals.Add(bestInZone);
                BakeLevelToMapDataOnly(bestInZone.trajectory, bestInZone.safePlatforms, localStart, localEnd);
            }
            else
            {
                Debug.LogError($"区域 {zIndex} 生成失败，未能收敛出合法地形拓扑。");
            }
        }

        map.survivalSpaceTiles = originalGlobalSurvivalSpace;
        ClearSurvivalVisuals();
        ShowSurvivalSpaceInGame();

        if (globalBestIndividuals.Count == zones.Count)
        {
            StitchAndLoadGlobalLevel(globalBestIndividuals, globalStart, globalEnd);
        }
    }

    private Vector2i DetermineZoneEntry(SurvivalSpaceAnalyzer.SurvivalZone current, SurvivalSpaceAnalyzer.SurvivalZone previous, Vector2i globalStart)
    {
        if (previous == null) return FindClosestTile(current.tiles, globalStart);
        return FindClosestTileToZone(current.tiles, previous.tiles);
    }

    private Vector2i DetermineZoneExit(SurvivalSpaceAnalyzer.SurvivalZone current, SurvivalSpaceAnalyzer.SurvivalZone next, Vector2i globalEnd)
    {
        if (next == null) return FindClosestTile(current.tiles, globalEnd);
        return FindClosestTileToZone(current.tiles, next.tiles);
    }

    private Vector2i FindClosestTile(List<Vector2i> zoneTiles, Vector2i target)
    {
        Vector2i best = zoneTiles[0];
        float minDist = float.MaxValue;
        foreach (var t in zoneTiles)
        {
            float dist = Vector2.Distance(new Vector2(t.x, t.y), new Vector2(target.x, target.y));
            if (dist < minDist) { minDist = dist; best = t; }
        }
        return best;
    }

    private Vector2i FindClosestTileToZone(List<Vector2i> sourceTiles, List<Vector2i> targetTiles)
    {
        Vector2i bestSource = sourceTiles[0];
        float minDist = float.MaxValue;
        foreach (var s in sourceTiles)
        {
            foreach (var t in targetTiles)
            {
                float dist = Vector2.Distance(new Vector2(s.x, s.y), new Vector2(t.x, t.y));
                if (dist < minDist) { minDist = dist; bestSource = s; }
            }
        }
        return bestSource;
    }

    private void StitchAndLoadGlobalLevel(List<LevelIndividual> zoneIndividuals, Vector2i globalStart, Vector2i globalEnd)
    {
        map.ClearMapToEmpty();
        HashSet<Vector2i> globalSafePlatforms = new HashSet<Vector2i>();
        List<Vector3> globalTrajectory = new List<Vector3>();

        foreach (var ind in zoneIndividuals)
        {
            foreach (var p in ind.safePlatforms) globalSafePlatforms.Add(p);
            globalTrajectory.AddRange(ind.trajectory);
        }

        BakeLevelToMapDataOnly(globalTrajectory, globalSafePlatforms, globalStart, globalEnd);

        if (finishLinePrefab != null)
        {
            Vector2 endWorldPos = map.GetMapTilePosition(globalEnd);
            Instantiate(finishLinePrefab, new Vector3(endWorldPos.x, endWorldPos.y, -5f), Quaternion.identity);
        }

        foreach (var ind in zoneIndividuals)
        {
            map.ApplyGeneratedPath(ind.path, ind.replay, ind.trajectory, ind.safeColumns);
            if (enableIWBTGBaking) BakeIWBTGLevel(ind);
        }
    }

    private List<LevelIndividual> GetAllElitesFromGrid()
    {
        List<LevelIndividual> elites = new List<LevelIndividual>();
        for (int i = 0; i < GRID_SIZE; i++)
        {
            for (int j = 0; j < GRID_SIZE; j++)
            {
                if (eliteGrid[i, j] != null) elites.Add(eliteGrid[i, j]);
            }
        }
        return elites;
    }

    private bool TryPlaceIndividualInGrid(LevelIndividual ind)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(ind.linearity * GRID_SIZE), 0, GRID_SIZE - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(ind.inputDensity * GRID_SIZE), 0, GRID_SIZE - 1);

        if (eliteGrid[x, y] == null || ind.fitness > eliteGrid[x, y].fitness)
        {
            eliteGrid[x, y] = ind;
            return true;
        }
        return false;
    }

    private LevelIndividual CreateIndividualFromGhost(Vector2i startTile, Vector2i endTile)
    {
        LevelIndividual ind = new LevelIndividual();
        ind.path = new List<Vector2i>(ghostPath);
        ind.replay = new List<ReplayFrame>(ghostReplay);
        ind.trajectory = new List<Vector3>(verifiedTrajectory);
        ind.safeColumns = new HashSet<int>(ghostSafeColumns);
        ind.safePlatforms = new HashSet<Vector2i>(ghostSafePlatforms);

        Vector2 startPos = map.GetMapTilePosition(startTile);
        Vector2 endPos = map.GetMapTilePosition(endTile);
        ind.linearity = LevelMetrics.CalculateLinearity(verifiedTrajectory, startPos, endPos);
        ind.inputDensity = LevelMetrics.CalculateInputDensity(ghostReplay);
        return ind;
    }

    private void CalculateFitness(LevelIndividual ind)
    {
        float targetDensity = 0.8f;
        float densityScore = 1.0f - Mathf.Abs(targetDensity - ind.inputDensity);
        float linearityScore = 1.0f - ind.linearity;
        ind.fitness = (densityScore * 0.6f) + (linearityScore * 0.4f) + (ind.trajectory.Count * 0.01f);
    }

    private LevelIndividual TournamentSelection(List<LevelIndividual> population)
    {
        int tournamentSize = 3;
        LevelIndividual best = null;
        for (int i = 0; i < tournamentSize; i++)
        {
            LevelIndividual candidate = population[Random.Range(0, population.Count)];
            if (best == null || candidate.fitness > best.fitness) best = candidate;
        }
        return best;
    }

    private LevelIndividual CrossoverAndMutate(LevelIndividual parentA, LevelIndividual parentB, Vector2i startTile, Vector2i endTile)
    {
        int midX = map.mWidth / 2;
        HashSet<Vector2i> childSafePlatforms = new HashSet<Vector2i>();

        foreach (var p in parentA.safePlatforms) if (p.x <= midX) childSafePlatforms.Add(p);
        foreach (var p in parentB.safePlatforms) if (p.x > midX) childSafePlatforms.Add(p);

        List<Vector3> mixedTrajectory = new List<Vector3>();
        mixedTrajectory.AddRange(parentA.trajectory.Where(pos => map.GetMapTileAtPoint(pos).x <= midX));
        mixedTrajectory.AddRange(parentB.trajectory.Where(pos => map.GetMapTileAtPoint(pos).x > midX));

        if (Random.value < gaMutationRate)
        {
            List<Vector2i> platformsList = childSafePlatforms.ToList();
            if (platformsList.Count > 0)
            {
                Vector2i randomPlatform = platformsList[Random.Range(0, platformsList.Count)];
                childSafePlatforms.Remove(randomPlatform);
                childSafePlatforms.Add(new Vector2i(randomPlatform.x, randomPlatform.y + Random.Range(-1, 2)));
            }
        }

        BakeLevelToMapDataOnly(mixedTrajectory, childSafePlatforms, startTile, endTile);

        string failReason;
        Vector2 failPos;
        if (VerifyLevelWithRealPhysics(startTile, endTile, out failReason, out failPos))
        {
            LevelIndividual child = CreateIndividualFromGhost(startTile, endTile);
            child.safePlatforms = childSafePlatforms;
            return child;
        }
        else RecordFailure(failReason);

        return null;
    }

    private void LoadBestGAIndividual(LevelIndividual bestInd, Vector2i startTile, Vector2i endTile)
    {
        BakeLevelToMapDataOnly(bestInd.trajectory, bestInd.safePlatforms, startTile, endTile);
        if (finishLinePrefab != null)
        {
            Vector2 endWorldPos = map.GetMapTilePosition(endTile);
            Instantiate(finishLinePrefab, new Vector3(endWorldPos.x, endWorldPos.y, -5f), Quaternion.identity);
        }
        map.ApplyGeneratedPath(bestInd.path, bestInd.replay, bestInd.trajectory, bestInd.safeColumns);
        if (enableIWBTGBaking) BakeIWBTGLevel(bestInd);
    }
}