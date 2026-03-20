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
        StartCoroutine(GenerateEvolutionaryMapElitesRoutine(startTile, endTile));
    }

    private IEnumerator GenerateEvolutionaryMapElitesRoutine(Vector2i startTile, Vector2i endTile)
    {
        Initialize();
        if (director != null) director.SetRunning(false);
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        ClearVisuals();
        InitLog("正统演化型 MAP-Elites (GA驱动)", gaPopulationSize, gaMaxGenerations);

        if (startTile.x != -1 && endTile.x != -1) AutoConnectStartAndEndToSurvivalSpace(startTile, endTile);
        BuildSurvivalGradient(endTile);

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        LevelGenerationPlanner planner = new LevelGenerationPlanner();
        planner.PlanGlobalRoute(map, zones);

        Debug.Log($">>> [阶段 1] 初始种群生成：使用模拟器创建 {gaPopulationSize} 个初代个体并置入特征网格...");
        int initialCount = 0;
        int initialAttempts = 0;

        while (initialCount < gaPopulationSize && initialAttempts < maxTotalAttempts)
        {
            initialAttempts++;
            string failReason;
            Vector2 failPos;

            if (RunGuidedSimulation(startTile, endTile, planner.plannedRoute, out failReason, out failPos))
            {
                BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, startTile, endTile);
                if (VerifyLevelWithRealPhysics(startTile, endTile, out failReason, out failPos))
                {
                    LevelIndividual newInd = CreateIndividualFromGhost(startTile, endTile);
                    CalculateFitness(newInd);
                    TryPlaceIndividualInGrid(newInd);
                    initialCount++;
                    LogAttemptResult(initialAttempts, "初代个体生成成功", $"当前进度: {initialCount}/{gaPopulationSize}");
                }
            }
            yield return null;
        }

        Debug.Log($">>> [阶段 2] 初始网格构建完成，启动由遗传算法驱动的内部进化循环，总代数：{gaMaxGenerations}...");

        for (int generation = 1; generation <= gaMaxGenerations; generation++)
        {
            List<LevelIndividual> currentElites = GetAllElitesFromGrid();

            if (currentElites.Count < 2)
            {
                Debug.LogWarning("精英网格中可用于交叉的个体不足，进化提前终止。");
                break;
            }

            LogAttemptResult(generation, $"=== 进化第 {generation} 代 ===", $"当前网格中共有 {currentElites.Count} 个独立精英");

            int offspringProduced = 0;
            int maxOffspringAttempts = gaPopulationSize;

            while (offspringProduced < gaPopulationSize && maxOffspringAttempts > 0)
            {
                maxOffspringAttempts--;
                LevelIndividual parentA = TournamentSelection(currentElites);
                LevelIndividual parentB = TournamentSelection(currentElites);
                LevelIndividual offspring = CrossoverAndMutate(parentA, parentB, startTile, endTile);

                if (offspring != null)
                {
                    CalculateFitness(offspring);
                    bool placed = TryPlaceIndividualInGrid(offspring);
                    offspringProduced++;

                    if (placed)
                    {
                        yield return StartCoroutine(ShowSuccessVisualsRoutine(offspring.trajectory));
                    }
                }
                yield return null;
            }
        }

        List<LevelIndividual> finalElites = GetAllElitesFromGrid();
        LogFinish(gaMaxGenerations, finalElites.Count);

        LevelIndividual absoluteBest = finalElites.OrderByDescending(p => p.fitness).FirstOrDefault();
        if (absoluteBest != null)
        {
            Debug.Log($">>> 演化型 MAP-Elites 运行完毕！网格内共沉淀 {finalElites.Count} 个独特关卡，全局最高适应度: {absoluteBest.fitness}");
            LoadBestGAIndividual(absoluteBest, startTile, endTile);
        }
        else
        {
            Debug.LogError("生成失败，特征网格最终为空。");
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
            if (best == null || candidate.fitness > best.fitness)
            {
                best = candidate;
            }
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