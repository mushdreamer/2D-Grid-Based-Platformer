using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System;
using Random = UnityEngine.Random;

public struct ReplayFrame
{
    public bool[] inputs;
    public ReplayFrame(bool[] src)
    {
        inputs = new bool[src.Length];
        System.Array.Copy(src, inputs, src.Length);
    }
}

public class LevelIndividual
{
    public List<Vector2i> path;
    public List<ReplayFrame> replay;
    public List<Vector3> trajectory;
    public HashSet<int> safeColumns;
    public HashSet<Vector2i> safePlatforms;
    public List<Character.CharacterState> stateSequence;
    public Dictionary<Character.CharacterState, int> stateCounts;
    public Dictionary<string, int> stateTransitionCounts;
    public int deathCount;
    public int outsidePlayAreaFrames;
    public int trapContactCount;
    public bool goalReached;
    public float linearity;
    public float inputDensity;
    public float fitness;
}

public class GhostSnapshot
{
    public Vector2 position;
    public Vector2 speed;
    public bool onGround;
    public float virtualFloorY;
    public List<Vector2i> path;
    public List<ReplayFrame> replay;
    public List<Vector3> trajectory;
    public HashSet<int> safeColumns;
    public GhostSnapshot(Bot agent, float vFloor, List<Vector2i> p, List<ReplayFrame> r, List<Vector3> t, HashSet<int> s)
    {
        position = agent.mPosition;
        speed = agent.mSpeed;
        onGround = agent.mOnGround;
        virtualFloorY = vFloor;
        path = new List<Vector2i>(p);
        replay = new List<ReplayFrame>(r);
        trajectory = new List<Vector3>(t);
        safeColumns = new HashSet<int>(s);
    }
}

public partial class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;

    [Header("Generation Limits (生成规模限制)")]
    public int targetValidLevels = 5;
    public int maxTotalAttempts = 250;

    [Header("Generation Settings")]
    [Range(0f, 1f)] public float blockDensity = 0.45f;
    [Range(0.01f, 0.5f)] public float noiseScale = 0.15f;

    public GameObject finishLinePrefab;

    [Header("IWBTG Hardcore Baking")]
    public bool enableIWBTGBaking = true;
    public float hardcoreDeviationTolerance = 1.5f;

    private Bot ghostAgent;
    private Bot validatorAgent;

    private const float SIM_STEP = 0.02f;
    private float currentVirtualFloorY;

    private const int GRID_SIZE = 10;
    private LevelIndividual[,] eliteGrid = new LevelIndividual[GRID_SIZE, GRID_SIZE];

    private List<Vector2i> ghostPath = new List<Vector2i>();
    private HashSet<Vector2i> ghostPathSet = new HashSet<Vector2i>();
    private List<ReplayFrame> ghostReplay = new List<ReplayFrame>();
    private List<Vector3> ghostTrajectory = new List<Vector3>();
    private HashSet<int> ghostSafeColumns = new HashSet<int>();
    private HashSet<Vector2i> ghostSafePlatforms = new HashSet<Vector2i>();
    private List<Character.CharacterState> ghostStateSequence = new List<Character.CharacterState>();
    private Dictionary<Character.CharacterState, int> ghostStateCounts = new Dictionary<Character.CharacterState, int>();
    private Dictionary<string, int> ghostStateTransitionCounts = new Dictionary<string, int>();
    private int ghostDeathCount = 0;
    private int ghostOutsidePlayAreaFrames = 0;
    private int ghostTrapContactCount = 0;

    private List<Vector3> verifiedTrajectory = new List<Vector3>();
    private Dictionary<Vector2i, int> survivalGradient = new Dictionary<Vector2i, int>();

    private Dictionary<string, int> failureStatistics = new Dictionary<string, int>();
    private List<GameObject> survivalVisuals = new List<GameObject>();

    enum ActionType { MoveRight, MoveLeft, JumpRight, JumpLeft, LongJumpRight, LongJumpLeft, HighJumpRight, HighJumpLeft, Drop }

    public void Initialize()
    {
        if (ghostAgent == null)
        {
            ghostAgent = Instantiate(characterPrefab, Vector3.zero, Quaternion.identity);
            ghostAgent.gameObject.SetActive(false);
            ghostAgent.name = "GhostGenerator";
            ghostAgent.mMap = map;
            ghostAgent.BotInit(new bool[(int)KeyInput.Count], new bool[(int)KeyInput.Count]);
        }

        if (validatorAgent == null)
        {
            validatorAgent = Instantiate(characterPrefab, Vector3.zero, Quaternion.identity);
            validatorAgent.gameObject.SetActive(false);
            validatorAgent.name = "PathValidator";
            validatorAgent.mMap = map;
            validatorAgent.BotInit(new bool[(int)KeyInput.Count], new bool[(int)KeyInput.Count]);
        }
    }

    private void RecordFailure(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;
        if (failureStatistics.ContainsKey(reason)) failureStatistics[reason]++;
        else failureStatistics[reason] = 1;
    }

    private void PrintFailureStats(string phaseName)
    {
        if (failureStatistics.Count == 0) return;
        Debug.Log($"=== {phaseName} 失败原因诊断报告 ===");
        foreach (var kvp in failureStatistics.OrderByDescending(k => k.Value))
        {
            Debug.Log($"死因: {kvp.Key} | 累计次数: {kvp.Value}");
        }
        Debug.Log("======================================");
        failureStatistics.Clear();
    }

    private void ShowSurvivalSpaceInGame()
    {
        ClearSurvivalVisuals();
        if (map.survivalSpaceTiles == null) return;
        foreach (var tile in map.survivalSpaceTiles)
        {
            Vector2 pos = map.GetMapTilePosition(tile.x, tile.y);
            GameObject go = new GameObject("SurvivalVis");
            go.transform.position = new Vector3(pos.x, pos.y, -3f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            if (map.tilePrefab != null) sr.sprite = map.tilePrefab.sprite;
            sr.color = new Color(0f, 1f, 0f, 0.25f);
            survivalVisuals.Add(go);
        }
    }

    private void ClearSurvivalVisuals()
    {
        foreach (var go in survivalVisuals) { if (go != null) Destroy(go); }
        survivalVisuals.Clear();
    }

    private void AutoConnectAllSurvivalZones(Vector2i startTile, Vector2i endTile)
    {
        if (map.survivalSpaceTiles == null) map.survivalSpaceTiles = new HashSet<Vector2i>();

        List<SurvivalSpaceAnalyzer.SurvivalZone> islands = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);

        if (islands.Count > 0)
        {
            islands = islands.OrderBy(z => z.center.x).ToList();

            Vector2i firstZoneEntry = islands.First().tiles.OrderBy(t => t.x).First();
            DrawSurvivalLine(startTile, firstZoneEntry);

            for (int i = 0; i < islands.Count - 1; i++)
            {
                Vector2i currentZoneExit = islands[i].tiles.OrderByDescending(t => t.x).First();
                Vector2i nextZoneEntry = islands[i + 1].tiles.OrderBy(t => t.x).First();
                DrawSurvivalLine(currentZoneExit, nextZoneEntry);
            }

            Vector2i lastZoneExit = islands.Last().tiles.OrderByDescending(t => t.x).First();
            DrawSurvivalLine(lastZoneExit, endTile);
        }
        else
        {
            DrawSurvivalLine(startTile, endTile);
        }
    }

    private void DrawSurvivalLine(Vector2i from, Vector2i to)
    {
        int dx = Mathf.Abs(to.x - from.x), dy = Mathf.Abs(to.y - from.y);
        int sx = from.x < to.x ? 1 : -1, sy = from.y < to.y ? 1 : -1;
        int err = dx - dy;
        int x = from.x, y = from.y;

        while (true)
        {
            for (int ix = -2; ix <= 2; ix++)
                for (int iy = -2; iy <= 2; iy++)
                    map.survivalSpaceTiles.Add(new Vector2i(x + ix, y + iy));

            if (x == to.x && y == to.y) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }

    private void BuildSurvivalGradient(Vector2i endTile)
    {
        survivalGradient.Clear();
        if (map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0) return;

        Queue<Vector2i> queue = new Queue<Vector2i>();
        HashSet<Vector2i> visited = new HashSet<Vector2i>();

        Vector2i bestStart = endTile;
        float minDist = float.MaxValue;
        foreach (var tile in map.survivalSpaceTiles)
        {
            float d = Vector2.Distance(new Vector2(tile.x, tile.y), new Vector2(endTile.x, endTile.y));
            if (d < minDist)
            {
                minDist = d;
                bestStart = tile;
            }
        }

        queue.Enqueue(bestStart);
        visited.Add(bestStart);
        survivalGradient[bestStart] = 0;

        Vector2i[] directions = new Vector2i[] {
            new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1),
            new Vector2i(1, 1), new Vector2i(-1, 1), new Vector2i(1, -1), new Vector2i(-1, -1)
        };

        while (queue.Count > 0)
        {
            Vector2i current = queue.Dequeue();
            int currentDist = survivalGradient[current];

            foreach (var dir in directions)
            {
                Vector2i neighbor = new Vector2i(current.x + dir.x, current.y + dir.y);
                if (map.survivalSpaceTiles.Contains(neighbor) && !visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    survivalGradient[neighbor] = currentDist + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    public void GenerateMapElitesLibrary(Vector2i startTile, Vector2i endTile)
    {
        StartCoroutine(GenerateMapElitesRoutine(startTile, endTile));
    }

    private IEnumerator GenerateMapElitesRoutine(Vector2i startTile, Vector2i endTile)
    {
        Initialize();
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;
        int attempts = 0;

        ClearVisuals();
        InitLog("全息可视化与逐条记录版", targetValidLevels, maxTotalAttempts);

        if (startTile.x != -1 && endTile.x != -1)
        {
            AutoConnectAllSurvivalZones(startTile, endTile);
        }
        BuildSurvivalGradient(endTile);
        ShowSurvivalSpaceInGame();
        failureStatistics.Clear();

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        List<GenerationRouteStep> plannedRoute = BuildSimpleRouteFromZones(zones, startTile, endTile);

        Debug.Log($">>> 开始生成全向引力关卡，完全读取面板参数控制规模...");

        while (validLevelsFound < targetValidLevels && attempts < maxTotalAttempts)
        {
            attempts++;
            string failReason = "";
            Vector2 failPos = Vector2.zero;

            if (RunGuidedSimulation(startTile, endTile, plannedRoute, out failReason, out failPos))
            {
                BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, startTile, endTile);

                if (VerifyLevelWithRealPhysics(startTile, endTile, out failReason, out failPos))
                {
                    Vector2 startPos = map.GetMapTilePosition(startTile);
                    Vector2 endPos = map.GetMapTilePosition(endTile);
                    float lin = LevelMetrics.CalculateLinearity(verifiedTrajectory, startPos, endPos);
                    float den = LevelMetrics.CalculateInputDensity(ghostReplay);
                    float fit = verifiedTrajectory.Count;
                    int uniqueStates = ghostStateCounts != null ? ghostStateCounts.Count : 0;
                    int x = Mathf.Clamp(uniqueStates, 0, GRID_SIZE - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(verifiedTrajectory.Count / 100f), 0, GRID_SIZE - 1);

                    if (eliteGrid[x, y] == null || fit > eliteGrid[x, y].fitness)
                    {
                        LevelIndividual newInd = new LevelIndividual();
                        newInd.path = new List<Vector2i>(ghostPath);
                        newInd.replay = new List<ReplayFrame>(ghostReplay);
                        newInd.trajectory = new List<Vector3>(verifiedTrajectory);
                        newInd.safeColumns = new HashSet<int>(ghostSafeColumns);
                        newInd.safePlatforms = new HashSet<Vector2i>(ghostSafePlatforms);
                        newInd.stateSequence = new List<Character.CharacterState>(ghostStateSequence);
                        newInd.stateCounts = new Dictionary<Character.CharacterState, int>(ghostStateCounts);
                        newInd.stateTransitionCounts = new Dictionary<string, int>(ghostStateTransitionCounts);
                        newInd.deathCount = ghostDeathCount;
                        newInd.outsidePlayAreaFrames = ghostOutsidePlayAreaFrames;
                        newInd.trapContactCount = ghostTrapContactCount;
                        newInd.goalReached = true;
                        newInd.linearity = lin;
                        newInd.inputDensity = den;
                        newInd.fitness = fit;
                        eliteGrid[x, y] = newInd;
                        validLevelsFound++;

                        LogAttemptResult(attempts, "成功入库", $"当前进度: {validLevelsFound} / {targetValidLevels}");
                        yield return StartCoroutine(ShowSuccessVisualsRoutine(verifiedTrajectory));
                    }
                    else
                    {
                        LogAttemptResult(attempts, "成功但淘汰", "因网格已有更优解被丢弃");
                        yield return StartCoroutine(ShowSuccessVisualsRoutine(verifiedTrajectory));
                    }
                }
                else
                {
                    RecordFailure(failReason);
                    LogAttemptResult(attempts, "验证失败", failReason);
                    yield return StartCoroutine(ShowSearchVisualsRoutine(verifiedTrajectory));
                }

                map.ClearMapToEmpty();
                ClearVisuals();
            }
            else
            {
                RecordFailure(failReason);
                LogAttemptResult(attempts, "鬼魂卡死", failReason);
                yield return StartCoroutine(ShowSearchVisualsRoutine(ghostTrajectory));
            }
        }

        PrintFailureStats("旧版 MAP-Elites 全局生成");
        ClearSurvivalVisuals();
        ClearVisuals();
        LogFinish(attempts, validLevelsFound);
        Debug.Log($">>> 生成完毕! 总尝试: {attempts} 次，成功: {validLevelsFound} 个。");

        SelectAndLoadLevel(5, 5);
    }

    public void SelectAndLoadLevel(int x, int y)
    {
        LevelIndividual target = eliteGrid[x, y];
        if (target == null)
        {
            float minDist = float.MaxValue;
            for (int i = 0; i < GRID_SIZE; i++)
            {
                for (int j = 0; j < GRID_SIZE; j++)
                {
                    if (eliteGrid[i, j] != null)
                    {
                        float d = Mathf.Pow(i - x, 2) + Mathf.Pow(j - y, 2);
                        if (d < minDist)
                        {
                            minDist = d;
                            target = eliteGrid[i, j];
                        }
                    }
                }
            }
        }

        if (target != null)
        {
            if (target.path != null && target.path.Count > 0)
            {
                Vector2i start = target.path[0];
                Vector2i end = target.path[target.path.Count - 1];
                BakeLevelToMapDataOnly(target.trajectory, target.safePlatforms, start, end);

                if (finishLinePrefab != null)
                {
                    Vector2 endWorldPos = map.GetMapTilePosition(end);
                    Instantiate(finishLinePrefab, new Vector3(endWorldPos.x, endWorldPos.y, -5f), Quaternion.identity);
                }
            }
            map.ApplyGeneratedPath(target.path, target.replay, target.trajectory, target.safeColumns);
            LogStateEnumerationDiagnostics(target, "Selected");

            // Phase 2 scope cleanup: IWBTG risk-field baking is experimental and no longer part of the core G-key path.
            // if (enableIWBTGBaking) BakeIWBTGLevel(target);
        }
    }

    bool VerifyLevelWithRealPhysics(Vector2i startTile, Vector2i endTile, out string reason, out Vector2 failPos)
    {
        verifiedTrajectory.Clear();

        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, validatorAgent.mAABB.HalfSizeY + 1f);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        validatorAgent.mPosition = startWorldPos;
        validatorAgent.mSpeed = Vector2.zero;
        validatorAgent.mCurrentState = Character.CharacterState.Stand;
        validatorAgent.mOnGround = false;
        validatorAgent.mAABB.Center = validatorAgent.mPosition + validatorAgent.mAABBOffset;

        int frameIndex = 0;
        int maxFrames = ghostReplay.Count + 120;

        while (frameIndex < ghostReplay.Count && frameIndex < maxFrames)
        {
            bool[] currentInputs = ghostReplay[frameIndex].inputs;
            validatorAgent.SimulationUpdate(SIM_STEP, currentInputs);
            verifiedTrajectory.Add(new Vector3(validatorAgent.mPosition.x, validatorAgent.mPosition.y, -8f));

            if (validatorAgent.mPosition.y < map.position.y)
            {
                reason = $"VerifyFall_坠落深渊"; failPos = validatorAgent.mPosition; return false;
            }
            if (validatorAgent.mCurrentState == Character.CharacterState.Die)
            {
                reason = $"VerifyDie_撞击致死"; failPos = validatorAgent.mPosition; return false;
            }

            if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
            {
                Vector2i currentTile = map.GetMapTileAtPoint(validatorAgent.mPosition);
                bool isInside = false;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (map.survivalSpaceTiles.Contains(new Vector2i(currentTile.x + dx, currentTile.y + dy)))
                        {
                            isInside = true;
                            break;
                        }
                    }
                    if (isInside) break;
                }
                if (!isInside)
                {
                    reason = $"VerifyOutOfBounds_物理验证阶段脱离生存空间";
                    failPos = validatorAgent.mPosition;
                    return false;
                }
            }

            if (Vector2.Distance(validatorAgent.mPosition, endWorldPos) < Map.cTileSize * 3)
            {
                reason = "Success"; failPos = validatorAgent.mPosition; return true;
            }
            frameIndex++;
        }

        reason = "VerifyTimeout_动作播完未达终点";
        failPos = validatorAgent.mPosition;
        return false;
    }

    void FillColumn(int x, int yStart, int yEnd, TileType type)
    {
        for (int y = yStart; y <= yEnd; y++) map.SetTile(x, y, type);
    }

    void SpawnSpike(int x, int y, bool flipped)
    {
        if (map.GetTile(x, y) == TileType.Empty) map.SetTile(x, y, TileType.Danger);
    }

    void BakeLevelToMapDataOnly(List<Vector3> trajectory, HashSet<Vector2i> safePlatforms, Vector2i start, Vector2i end)
    {
        map.ClearMapToEmpty();
        HashSet<Vector2i> airMask = new HashSet<Vector2i>();

        foreach (var point in trajectory)
        {
            Vector2i t = map.GetMapTileAtPoint(point);
            // [意图注入 4] 机械复杂度越高，给玩家留下的腾空容错空间越小（气道变窄）
            int padding = designerIntent.mechanicalComplexity > 0.7f ? 2 : 4;
            for (int dx = -padding; dx <= padding; dx++)
                for (int dy = -padding; dy <= padding; dy++)
                    airMask.Add(new Vector2i(t.x + dx, t.y + dy));
        }

        if (map.survivalSpaceTiles != null)
        {
            foreach (Vector2i safeTile in map.survivalSpaceTiles) airMask.Add(safeTile);
        }

        float seed = Random.Range(0f, 100f);

        // [意图注入 5] 地貌拓扑动态映射
        // 探索性低(0.0) -> 大块厚实平地；探索性高(1.0) -> 极度破碎的零星碎块
        float dynamicBlockDensity = Mathf.Lerp(0.85f, 0.20f, designerIntent.structuralExploration);
        float dynamicNoiseScale = Mathf.Lerp(0.02f, 0.35f, designerIntent.structuralExploration);

        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i currentPos = new Vector2i(x, y);

                if (safePlatforms != null && safePlatforms.Contains(currentPos)) { map.SetTile(x, y, TileType.Block); continue; }
                if (airMask.Contains(currentPos)) { map.SetTile(x, y, TileType.Empty); continue; }
                if (y < 2) { map.SetTile(x, y, TileType.Block); continue; }

                float noiseVal = Mathf.PerlinNoise(x * dynamicNoiseScale + seed, y * dynamicNoiseScale + seed);
                float heightAtten = 1.0f - ((float)y / map.mHeight) * 0.5f;

                if (noiseVal * heightAtten > (1.0f - dynamicBlockDensity)) map.SetTile(x, y, TileType.Block);
                else map.SetTile(x, y, TileType.Empty);
            }
        }

        // 静态刺生成逻辑
        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                Vector2i cur = new Vector2i(x, y);
                bool isSafeZone = map.survivalSpaceTiles != null && (map.survivalSpaceTiles.Contains(cur) || map.survivalSpaceTiles.Contains(new Vector2i(x, y + 1)) || map.survivalSpaceTiles.Contains(new Vector2i(x, y - 1)));
                if (isSafeZone) continue;

                if (map.GetTile(x, y) == TileType.Empty && !airMask.Contains(new Vector2i(x, y)))
                {
                    bool topBlock = map.GetTile(x, y + 1) == TileType.Block;
                    bool bottomBlock = map.GetTile(x, y - 1) == TileType.Block;
                    // [意图注入 6] 紧张感越高，背景里的视觉干扰刺越多
                    float spikeProb = Mathf.Lerp(0.95f, 0.4f, designerIntent.riskTension);
                    if (Random.value > spikeProb)
                    {
                        if (topBlock) SpawnSpike(x, y, true);
                        else if (bottomBlock) SpawnSpike(x, y, false);
                    }
                }
            }
        }

        if (start.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(start.x + dx, 0, start.y - 1, TileType.Block);
        if (end.x != -1) for (int dx = -2; dx <= 2; dx++) FillColumn(end.x + dx, 0, end.y - 1, TileType.Block);
    }


    private List<GenerationRouteStep> BuildSimpleRouteFromZones(List<SurvivalSpaceAnalyzer.SurvivalZone> zones, Vector2i startTile, Vector2i endTile)
    {
        List<GenerationRouteStep> route = new List<GenerationRouteStep>();
        if (zones != null && zones.Count > 0)
        {
            foreach (var zone in zones.OrderBy(z => z.center.x))
            {
                var sortedTiles = zone.tiles.OrderBy(t => t.x).ToList();
                if (sortedTiles.Count == 0) continue;
                Vector2 zoneExit = map.GetMapTilePosition(sortedTiles[sortedTiles.Count - 1]);
                route.Add(new GenerationRouteStep { endPoint = zoneExit, associatedZone = zone });
            }
        }
        route.Add(new GenerationRouteStep { endPoint = map.GetMapTilePosition(endTile), associatedZone = zones != null && zones.Count > 0 ? zones[zones.Count - 1] : null });
        return route;
    }

    private void LogStateEnumerationDiagnostics(LevelIndividual ind, string label)
    {
        if (ind == null) return;
        StateEnumerationEvaluator.EvaluationResult result = StateEnumerationEvaluator.EvaluateIndividual(ind);
        Debug.Log($"[StateEnumeration:{label}] {result.diagnostic}, states={FormatStateCounts(ind.stateCounts)}, transitions={FormatTransitionCounts(ind.stateTransitionCounts)}");
    }

    private string FormatStateCounts(Dictionary<Character.CharacterState, int> counts)
    {
        if (counts == null || counts.Count == 0) return "none";
        return string.Join(", ", counts.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
    }

    private string FormatTransitionCounts(Dictionary<string, int> counts)
    {
        if (counts == null || counts.Count == 0) return "none";
        return string.Join(", ", counts.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
    }
    public void BakeIWBTGLevel(LevelIndividual goldenLevel)
    {
        // Experimental IWBTG/risk-field baking has been moved out of the active core path for Phase 2.
        // TODO Phase 3+: replace this with trap/play-area constraints driven by StateEnumerationEvaluator if needed.
        Debug.Log("IWBTG risk-field baking is disabled in the state-enumeration core path.");
    }
}
