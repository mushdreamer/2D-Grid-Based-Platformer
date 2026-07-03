using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using Random = UnityEngine.Random;

public partial class LevelGenerator : MonoBehaviour
{
    [Header("Evolutionary MAP-Elites Settings")]
    public int gaPopulationSize = 20;
    public int gaMaxGenerations = 10;
    public float gaMutationRate = 0.4f;

    [Header("Advanced Mutation Settings")]
    public int maxRiskEmitters = 3;
    public float blockadeProbability = 0.3f;

    [System.Serializable]
    public struct GenerationTuning
    {
        [Range(0f, 1f)] public float riskTension;
        [Range(0f, 1f)] public float mechanicalComplexity;
        [Range(0f, 1f)] public float structuralExploration;
    }

    [Header("Generation Tuning (placeholder until StateEnumerationEvaluator)")]
    public GenerationTuning designerIntent = new GenerationTuning
    {
        riskTension = 0.5f,
        mechanicalComplexity = 0.5f,
        structuralExploration = 0.5f
    };

    private List<Vector2i> activeRiskEmitters = new List<Vector2i>();

    public void GenerateEvolutionaryMapElitesLibrary(Vector2i startTile, Vector2i endTile)
    {
        if (DEBUG_REPLAY_ONLY)
        {
            StartCoroutine(GenerateDebugReplayOnlyRoutine(startTile, endTile));
            return;
        }

        StartCoroutine(GenerateSegmentedEvolutionaryRoutine(startTile, endTile));
    }

    private IEnumerator GenerateDebugReplayOnlyRoutine(Vector2i startTile, Vector2i endTile)
    {
        lastGenerationBestIndividual = null;
        lastGenerationSucceeded = false;
        lastGenerationFailureReason = "";

        Initialize();
        ClearVisuals();
        failureStatistics.Clear();
        mapEliteZoneVisitCounts.Clear();

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        List<GenerationRouteStep> route = BuildSimpleRouteFromZones(zones, startTile, endTile);

        string failReason;
        Vector2 failPos;
        bool success = RunGuidedSimulation(
            startTile,
            endTile,
            route,
            out failReason,
            out failPos,
            false,
            map.survivalSpaceTiles != null ? new HashSet<Vector2i>(map.survivalSpaceTiles) : null,
            0.25f,
            1);

        LogDebugReplayOnlyResult(success, failReason, failPos);
        lastGenerationSucceeded = success;
        lastGenerationFailureReason = success ? "" : failReason;
        yield return null;
    }

    private IEnumerator GenerateSegmentedEvolutionaryRoutine(Vector2i globalStart, Vector2i globalEnd)
    {
        lastGenerationBestIndividual = null;
        lastGenerationSucceeded = false;
        lastGenerationFailureReason = "";

        Initialize();
        ClearVisuals();
        InitLog("多样性增强版演化管线 (张量场扭曲)", gaPopulationSize, gaMaxGenerations);
        failureStatistics.Clear();
        mapEliteZoneVisitCounts.Clear();

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        if (zones.Count == 0)
        {
            LogDeepDiagnostic("System", "致命错误：未能识别到任何生存空间。");
            lastGenerationFailureReason = "NoSurvivalZones";
            yield break;
        }

        zones = zones.OrderBy(z => z.center.x).ToList();
        HashSet<Vector2i> originalGlobalSurvivalSpace = new HashSet<Vector2i>(map.survivalSpaceTiles);
        List<LevelIndividual> globalBestIndividuals = new List<LevelIndividual>();

        for (int zIndex = 0; zIndex < zones.Count; zIndex++)
        {
            LogPhaseTransition($"区域 {zIndex} 地形演算");
            SurvivalSpaceAnalyzer.SurvivalZone currentZone = zones[zIndex];
            Vector2i localStart = DetermineZoneEntry(currentZone, zIndex == 0 ? null : zones[zIndex - 1], globalStart);
            Vector2i localEnd = DetermineZoneExit(currentZone, zIndex == zones.Count - 1 ? null : zones[zIndex + 1], globalEnd);

            activeRiskEmitters.Clear();
            System.Array.Clear(eliteGrid, 0, eliteGrid.Length);

            int initialCount = 0;
            int initialAttempts = 0;
            while (initialCount < gaPopulationSize && initialAttempts < maxTotalAttempts)
            {
                initialAttempts++;
                if (GenerateAndEvaluate(localStart, localEnd, currentZone, 0f)) initialCount++;
                yield return null;
            }

            for (int generation = 1; generation <= gaMaxGenerations; generation++)
            {
                float temperature = 1.0f - ((float)generation / gaMaxGenerations);
                List<LevelIndividual> currentElites = GetAllElitesFromGrid();
                if (currentElites.Count < 2) break;

                int offspringProduced = 0;
                int maxOffspringAttempts = gaPopulationSize;
                while (offspringProduced < gaPopulationSize && maxOffspringAttempts > 0)
                {
                    maxOffspringAttempts--;
                    LevelIndividual parentA = TournamentSelection(currentElites);
                    LevelIndividual parentB = TournamentSelection(currentElites);

                    LevelIndividual offspring = CrossoverAndMutateWithDiversity(parentA, parentB, localStart, localEnd, currentZone, temperature);

                    if (offspring != null)
                    {
                        CalculateFitness(offspring, currentZone);
                        if (TryPlaceIndividualInGrid(offspring)) offspringProduced++;
                    }
                }
                yield return null;
            }

            LevelIndividual bestInZone = GetAllElitesFromGrid().OrderByDescending(p => p.fitness).FirstOrDefault();
            if (bestInZone != null)
            {
                globalBestIndividuals.Add(bestInZone);
                LogStateEnumerationDiagnostics(bestInZone, $"Zone {zIndex} best");
                BakeLevelToMapDataOnly(bestInZone.trajectory, bestInZone.safePlatforms, localStart, localEnd);
                if (enableBoundaryTerminalization)
                    ApplyOutsideBoundaryBandTerminalization(bestInZone.trajectory, bestInZone.safePlatforms, localStart, localEnd);
                if (enableBoundaryDiagnostics)
                    LogBoundaryLethalityDiagnostics(bestInZone, localStart, $"Zone {zIndex} best");
            }
        }

        map.survivalSpaceTiles = originalGlobalSurvivalSpace;
        ClearSurvivalVisuals();
        ShowSurvivalSpaceInGame();

        lastGenerationBestIndividual = globalBestIndividuals.OrderByDescending(p => p.fitness).FirstOrDefault();
        lastGenerationSucceeded = globalBestIndividuals.Count == zones.Count;
        if (!lastGenerationSucceeded)
            lastGenerationFailureReason = $"IncompleteZones_{globalBestIndividuals.Count}_of_{zones.Count}_{GetMostCommonFailureReason()}";

        if (lastGenerationSucceeded) StitchAndLoadGlobalLevel(globalBestIndividuals, globalStart, globalEnd);
        LogFinish(maxTotalAttempts, globalBestIndividuals.Count);
    }

    private bool GenerateAndEvaluate(Vector2i start, Vector2i end, SurvivalSpaceAnalyzer.SurvivalZone zone, float temperature)
    {
        // TODO Phase 3: route generation should be driven by StateEnumerationEvaluator constraints.
        List<GenerationRouteStep> route = BuildRouteWithOptionalEnumerationGuidance(start, end, zone);

        string failReason; Vector2 failPos;
        if (RunGuidedSimulation(start, end, route, out failReason, out failPos, false, new HashSet<Vector2i>(zone.tiles), temperature))
        {
            BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, start, end);
            if (enableBoundaryTerminalization)
                ApplyOutsideBoundaryBandTerminalization(ghostTrajectory, ghostSafePlatforms, start, end);
            if (VerifyLevelWithRealPhysics(start, end, out failReason, out failPos))
            {
                LevelIndividual ind = CreateIndividualFromGhost(start, end);
                if (enableBoundaryDiagnostics || enableBoundarySafetyPenalty)
                    EvaluateAndStoreBoundaryLethalityDiagnostics(ind, start);
                CalculateFitness(ind, zone);
                return TryPlaceIndividualInGrid(ind);
            }
            else
            {
                RecordFailure("GA_Verify_" + failReason);
            }
        }
        else
        {
            RecordFailure("GA_Sim_" + failReason);
        }
        return false;
    }

    // Risk-field initialization moved out of the active core path in Phase 2.

    private LevelIndividual CrossoverAndMutateWithDiversity(LevelIndividual pA, LevelIndividual pB, Vector2i start, Vector2i end, SurvivalSpaceAnalyzer.SurvivalZone zone, float temp)
    {
        if (Random.value < gaMutationRate && zone.tiles.Count > 0)
        {
            Vector2i randomEmitter = zone.tiles[Random.Range(0, zone.tiles.Count)];
            activeRiskEmitters.Add(randomEmitter);
            if (activeRiskEmitters.Count > maxRiskEmitters) activeRiskEmitters.RemoveAt(0);
        }

        if (Random.value < blockadeProbability && pA.path != null && pA.path.Count > 4)
        {
            Vector2i blockadeTile = pA.path[Random.Range(pA.path.Count / 4, pA.path.Count * 3 / 4)];
            map.SetTile(blockadeTile.x, blockadeTile.y, TileType.Block);
        }

        // TODO Phase 3: mutation should be evaluated by StateEnumerationEvaluator.
        List<GenerationRouteStep> route = BuildRouteWithOptionalEnumerationGuidance(start, end, zone);

        string reason; Vector2 fPos;
        if (RunGuidedSimulation(start, end, route, out reason, out fPos, false, new HashSet<Vector2i>(zone.tiles), temp))
        {
            BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, start, end);
            if (enableBoundaryTerminalization)
                ApplyOutsideBoundaryBandTerminalization(ghostTrajectory, ghostSafePlatforms, start, end);
            if (VerifyLevelWithRealPhysics(start, end, out reason, out fPos))
            {
                LevelIndividual ind = CreateIndividualFromGhost(start, end);
                if (enableBoundaryDiagnostics || enableBoundarySafetyPenalty)
                    EvaluateAndStoreBoundaryLethalityDiagnostics(ind, start);
                return ind;
            }
            else
            {
                RecordFailure("Mutate_Verify_" + reason);
            }
        }
        else
        {
            RecordFailure("Mutate_Sim_" + reason);
        }
        return null;
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
        if (zoneTiles == null || zoneTiles.Count == 0) return target;
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
        if (sourceTiles == null || sourceTiles.Count == 0) return new Vector2i(0, 0);
        if (targetTiles == null || targetTiles.Count == 0) return sourceTiles[0];
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
        if (enableBoundaryTerminalization)
            ApplyOutsideBoundaryBandTerminalization(globalTrajectory, globalSafePlatforms, globalStart, globalEnd);

        if (finishLinePrefab != null)
        {
            Vector2 endWorldPos = map.GetMapTilePosition(globalEnd);
            Instantiate(finishLinePrefab, new Vector3(endWorldPos.x, endWorldPos.y, -5f), Quaternion.identity);
        }

        foreach (var ind in zoneIndividuals)
        {
            map.ApplyGeneratedPath(ind.path, ind.replay, ind.trajectory, ind.safeColumns);
            // Experimental IWBTG/risk-field baking is disabled for the state-enumeration core path.
            // if (enableIWBTGBaking) BakeIWBTGLevel(ind);
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
        int uniqueStates = ind.stateCounts != null ? ind.stateCounts.Count : 0;
        int x = Mathf.Clamp(uniqueStates, 0, GRID_SIZE - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt((ind.trajectory != null ? ind.trajectory.Count : 0) / 100f), 0, GRID_SIZE - 1);

        if (eliteGrid[x, y] == null || ind.fitness > eliteGrid[x, y].fitness)
        {
            eliteGrid[x, y] = ind;
            UpdateMapEliteZoneVisitCounts(ind);
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
        ind.stateSequence = new List<Character.CharacterState>(ghostStateSequence);
        ind.stateCounts = new Dictionary<Character.CharacterState, int>(ghostStateCounts);
        ind.stateTransitionCounts = new Dictionary<string, int>(ghostStateTransitionCounts);
        ind.deathCount = ghostDeathCount;
        ind.outsidePlayAreaFrames = ghostOutsidePlayAreaFrames;
        ind.trapContactCount = ghostTrapContactCount;
        ind.goalReached = true;
        ind.guidedTargetCount = currentGuidedRouteTargetCount;
        PopulateSurvivalCoverageMetrics(ind);

        Vector2 startPos = map.GetMapTilePosition(startTile);
        Vector2 endPos = map.GetMapTilePosition(endTile);
        ind.linearity = LevelMetrics.CalculateLinearity(verifiedTrajectory, startPos, endPos);
        ind.inputDensity = LevelMetrics.CalculateInputDensity(ghostReplay);
        return ind;
    }

    private void CalculateFitness(LevelIndividual ind, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        StateEnumerationEvaluator.EvaluationResult result = EvaluateIndividualWithExperimentFlags(ind);
        ind.fitness = result.totalFitness;
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
}
