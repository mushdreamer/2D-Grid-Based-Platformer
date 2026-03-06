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
    public AdversarialDirector director;

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

    private List<Vector3> verifiedTrajectory = new List<Vector3>();
    private Dictionary<Vector2i, int> survivalGradient = new Dictionary<Vector2i, int>();

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

    private void AutoConnectStartAndEndToSurvivalSpace(Vector2i startTile, Vector2i endTile)
    {
        if (map.survivalSpaceTiles == null) map.survivalSpaceTiles = new HashSet<Vector2i>();

        if (map.survivalSpaceTiles.Count > 0)
        {
            Vector2i nearestToStart = startTile;
            float minDist = float.MaxValue;
            foreach (var t in map.survivalSpaceTiles)
            {
                float d = Vector2.Distance(new Vector2(startTile.x, startTile.y), new Vector2(t.x, t.y));
                if (d < minDist) { minDist = d; nearestToStart = t; }
            }
            DrawSurvivalLine(startTile, nearestToStart);

            Vector2i nearestToEnd = endTile;
            minDist = float.MaxValue;
            foreach (var t in map.survivalSpaceTiles)
            {
                float d = Vector2.Distance(new Vector2(endTile.x, endTile.y), new Vector2(t.x, t.y));
                if (d < minDist) { minDist = d; nearestToEnd = t; }
            }
            DrawSurvivalLine(endTile, nearestToEnd);
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
        if (director != null) director.SetRunning(false);
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;
        int attempts = 0;

        ClearVisuals();
        InitLog("全息可视化与逐条记录版", targetValidLevels, maxTotalAttempts);

        if (startTile.x != -1 && endTile.x != -1)
        {
            AutoConnectStartAndEndToSurvivalSpace(startTile, endTile);
        }
        BuildSurvivalGradient(endTile);

        List<SurvivalSpaceAnalyzer.SurvivalZone> zones = SurvivalSpaceAnalyzer.GetIdentifiedZones(map);
        LevelGenerationPlanner planner = new LevelGenerationPlanner();
        planner.PlanGlobalRoute(map, zones);

        Debug.Log($">>> 开始生成全向引力关卡，完全读取面板参数控制规模...");

        while (validLevelsFound < targetValidLevels && attempts < maxTotalAttempts)
        {
            attempts++;
            string failReason = "";
            Vector2 failPos = Vector2.zero;

            if (RunGuidedSimulation(startTile, endTile, planner.plannedRoute, out failReason, out failPos))
            {
                BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, startTile, endTile);

                if (VerifyLevelWithRealPhysics(startTile, endTile, out failReason, out failPos))
                {
                    Vector2 startPos = map.GetMapTilePosition(startTile);
                    Vector2 endPos = map.GetMapTilePosition(endTile);
                    float lin = LevelMetrics.CalculateLinearity(verifiedTrajectory, startPos, endPos);
                    float den = LevelMetrics.CalculateInputDensity(ghostReplay);
                    float fit = verifiedTrajectory.Count;
                    int x = Mathf.Clamp(Mathf.FloorToInt(lin * GRID_SIZE), 0, GRID_SIZE - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(den * GRID_SIZE), 0, GRID_SIZE - 1);

                    if (eliteGrid[x, y] == null || fit > eliteGrid[x, y].fitness)
                    {
                        LevelIndividual newInd = new LevelIndividual();
                        newInd.path = new List<Vector2i>(ghostPath);
                        newInd.replay = new List<ReplayFrame>(ghostReplay);
                        newInd.trajectory = new List<Vector3>(verifiedTrajectory);
                        newInd.safeColumns = new HashSet<int>(ghostSafeColumns);
                        newInd.safePlatforms = new HashSet<Vector2i>(ghostSafePlatforms);
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
                    LogAttemptResult(attempts, "验证失败", failReason);
                    yield return StartCoroutine(ShowSearchVisualsRoutine(verifiedTrajectory));
                }

                map.ClearMapToEmpty();
                ClearVisuals();
            }
            else
            {
                LogAttemptResult(attempts, "鬼魂卡死", failReason);
                yield return StartCoroutine(ShowSearchVisualsRoutine(ghostTrajectory));
            }
        }

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

            if (enableIWBTGBaking) BakeIWBTGLevel(target);
        }
    }

    bool RunGuidedSimulation(Vector2i startTile, Vector2i endTile, List<LevelGenerationPlanner.GenerationStep> route, out string finalReason, out Vector2 failPos)
    {
        int microAttempts = 200;
        finalReason = "";
        failPos = Vector2.zero;

        if (route == null || route.Count == 0)
        {
            finalReason = "RouteEmpty_规划路线为空";
            return false;
        }

        for (int i = 0; i < microAttempts; i++)
        {
            ClearGhostData();
            map.ClearMapToEmpty();

            if (startTile.x != -1)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    map.SetTile(startTile.x + dx, startTile.y - 1, TileType.Block);
                    ghostSafePlatforms.Add(new Vector2i(startTile.x + dx, startTile.y - 1));
                }
            }

            Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, characterPrefab.mAABB.HalfSizeY + 1f);

            ghostAgent.mPosition = startWorldPos;
            ghostAgent.mSpeed = Vector2.zero;
            ghostAgent.mCurrentState = Character.CharacterState.Stand;
            ghostAgent.mOnGround = false;

            currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize * 5f;

            bool routeSuccess = true;
            foreach (var step in route)
            {
                if (!SimulateGuidedPath(step.endPoint, step.associatedZone, out finalReason, out failPos))
                {
                    routeSuccess = false;
                    break;
                }
            }

            if (routeSuccess) return true;
        }
        return false;
    }

    bool SimulateGuidedPath(Vector2 finalDest, SurvivalSpaceAnalyzer.SurvivalZone currentZone, out string reason, out Vector2 failPos)
    {
        int framesLimit = 1500;
        int currentFrames = 0;
        int stagnationCount = 0;
        Vector2 lastProgressPos = ghostAgent.mPosition;

        while (currentFrames < framesLimit)
        {
            if (Vector2.Distance(ghostAgent.mPosition, finalDest) < Map.cTileSize * 3)
            {
                reason = "Success"; failPos = ghostAgent.mPosition; return true;
            }
            if (ghostAgent.mPosition.y < map.position.y - 100f)
            {
                reason = "FallOut_跌出边界"; failPos = ghostAgent.mPosition; return false;
            }

            if (Vector2.Distance(ghostAgent.mPosition, lastProgressPos) < 2.0f) stagnationCount++;
            else { stagnationCount = 0; lastProgressPos = ghostAgent.mPosition; }

            ActionType nextAction = PickAction(ghostAgent.mPosition, finalDest, stagnationCount, currentZone);
            if (stagnationCount > 8) stagnationCount = 0;

            currentFrames += ExecuteGhostAction(nextAction);
        }

        reason = "Timeout_耗尽1500帧陷入死循环";
        failPos = ghostAgent.mPosition;
        return false;
    }

    void ClearGhostData()
    {
        ghostPath.Clear();
        ghostPathSet.Clear();
        ghostReplay.Clear();
        ghostTrajectory.Clear();
        ghostSafeColumns.Clear();
        ghostSafePlatforms.Clear();
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
                reason = $"VerifyFall_第{frameIndex}帧坠落深渊"; failPos = validatorAgent.mPosition; return false;
            }
            if (validatorAgent.mCurrentState == Character.CharacterState.Die)
            {
                reason = $"VerifyDie_第{frameIndex}帧撞击致死"; failPos = validatorAgent.mPosition; return false;
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

    ActionType PickAction(Vector2 currentPos, Vector2 endPos, int stagnationCount, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        Vector2i curTile = map.GetMapTileAtPoint(currentPos);
        float weightRight = 1f, weightLeft = 1f, weightUp = 1f, weightDown = 1f;

        if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
        {
            int bestRight = int.MaxValue, bestLeft = int.MaxValue, bestUp = int.MaxValue, bestDown = int.MaxValue;
            int currentDist = int.MaxValue;
            int currentStroke = -1;

            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    Vector2i targetTile = new Vector2i(curTile.x + dx, curTile.y + dy);
                    if (survivalGradient.TryGetValue(targetTile, out int d))
                    {
                        if (d < currentDist) currentDist = d;
                        if (map.survivalSpaceStrokeOrder != null && map.survivalSpaceStrokeOrder.TryGetValue(targetTile, out int s) && s > currentStroke) currentStroke = s;
                    }
                }
            }

            int scanRadius = 3;
            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            {
                for (int dy = -scanRadius; dy <= scanRadius; dy++)
                {
                    Vector2i targetTile = new Vector2i(curTile.x + dx, curTile.y + dy);
                    if (map.survivalSpaceTiles.Contains(targetTile))
                    {
                        float baseWeight = 2f;
                        if (map.survivalSpaceStrokeOrder != null && map.survivalSpaceStrokeOrder.TryGetValue(targetTile, out int targetStroke))
                        {
                            if (targetStroke > currentStroke && targetStroke != -1) baseWeight += 50f;
                        }
                        if (dx > 0) weightRight += baseWeight;
                        if (dx < 0) weightLeft += baseWeight;
                        if (dy > 0) weightUp += baseWeight;
                        if (dy < 0) weightDown += baseWeight;
                    }
                    if (survivalGradient.TryGetValue(targetTile, out int dist))
                    {
                        if (dx > 0 && dist < bestRight) bestRight = dist;
                        if (dx < 0 && dist < bestLeft) bestLeft = dist;
                        if (dy > 0 && dist < bestUp) bestUp = dist;
                        if (dy < 0 && dist < bestDown) bestDown = dist;
                    }
                }
            }

            if (currentDist == int.MaxValue)
            {
                float minScore = float.MaxValue;
                Vector2i bestRescueTile = curTile;
                foreach (var t in map.survivalSpaceTiles)
                {
                    float physicalDist = Mathf.Abs(t.x - curTile.x) + Mathf.Abs(t.y - curTile.y);
                    if (physicalDist < minScore) { minScore = physicalDist; bestRescueTile = t; }
                }

                if (bestRescueTile.y > curTile.y) return (bestRescueTile.x >= curTile.x) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
                else return (bestRescueTile.x >= curTile.x) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
            }
            else
            {
                if (bestRight < currentDist) weightRight += 30f;
                if (bestLeft < currentDist) weightLeft += 30f;
                if (bestUp < currentDist) weightUp += 30f;
                if (bestDown < currentDist) weightDown += 30f;
            }
        }
        else
        {
            if (endPos.x > currentPos.x) weightRight += 5f; else weightLeft += 5f;
            if (endPos.y > currentPos.y) weightUp += 5f; else weightDown += 5f;
        }

        ActionType pickedAction = ActionType.Drop;

        if (stagnationCount > 8)
        {
            if (weightUp >= weightRight && weightUp >= weightLeft) pickedAction = (Random.value > 0.5f) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
            else if (weightRight >= weightLeft) pickedAction = ActionType.LongJumpRight;
            else pickedAction = ActionType.LongJumpLeft;
        }
        else
        {
            float totalWeight = weightRight + weightLeft + weightUp + weightDown;
            float r = Random.Range(0, totalWeight);

            if (r < weightRight) pickedAction = (Random.value > 0.4f) ? ActionType.MoveRight : ((Random.value > 0.5f) ? ActionType.JumpRight : ActionType.LongJumpRight);
            else
            {
                r -= weightRight;
                if (r < weightLeft) pickedAction = (Random.value > 0.4f) ? ActionType.MoveLeft : ((Random.value > 0.5f) ? ActionType.JumpLeft : ActionType.LongJumpLeft);
                else
                {
                    r -= weightLeft;
                    if (r < weightUp)
                    {
                        float upR = Random.value;
                        if (upR < 0.33f) pickedAction = (Random.value > 0.5f) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
                        else if (upR < 0.66f) pickedAction = (Random.value > 0.5f) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
                        else pickedAction = (Random.value > 0.5f) ? ActionType.JumpRight : ActionType.JumpLeft;
                    }
                    else pickedAction = ActionType.Drop;
                }
            }
        }

        SurvivalSpaceAnalyzer.ZoneGeometry geometry = zone != null ? zone.geometryType : SurvivalSpaceAnalyzer.ZoneGeometry.OrganicShape;

        if (geometry == SurvivalSpaceAnalyzer.ZoneGeometry.HorizontalCorridor)
        {
            if (pickedAction == ActionType.HighJumpRight) pickedAction = ActionType.LongJumpRight;
            if (pickedAction == ActionType.HighJumpLeft) pickedAction = ActionType.LongJumpLeft;
            if (pickedAction == ActionType.JumpRight) pickedAction = ActionType.MoveRight;
            if (pickedAction == ActionType.JumpLeft) pickedAction = ActionType.MoveLeft;
        }
        else if (geometry == SurvivalSpaceAnalyzer.ZoneGeometry.VerticalShaft)
        {
            if (pickedAction == ActionType.LongJumpRight) pickedAction = ActionType.HighJumpRight;
            if (pickedAction == ActionType.LongJumpLeft) pickedAction = ActionType.HighJumpLeft;
            if (pickedAction == ActionType.MoveRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.MoveLeft) pickedAction = ActionType.JumpLeft;
        }

        return pickedAction;
    }

    int ExecuteGhostAction(ActionType action)
    {
        int frames = 0;
        bool right = false, left = false, jump = false, drop = false;
        int jumpHoldFrames = 0;

        switch (action)
        {
            case ActionType.MoveRight: frames = 15; right = true; break;
            case ActionType.MoveLeft: frames = 15; left = true; break;
            case ActionType.JumpRight: frames = 25; right = true; jump = true; jumpHoldFrames = 10; break;
            case ActionType.JumpLeft: frames = 25; left = true; jump = true; jumpHoldFrames = 10; break;
            case ActionType.LongJumpRight: frames = 40; right = true; jump = true; jumpHoldFrames = 15; break;
            case ActionType.LongJumpLeft: frames = 40; left = true; jump = true; jumpHoldFrames = 15; break;
            case ActionType.HighJumpRight: frames = 45; right = true; jump = true; jumpHoldFrames = 20; break;
            case ActionType.HighJumpLeft: frames = 45; left = true; jump = true; jumpHoldFrames = 20; break;
            case ActionType.Drop: frames = 20; drop = true; break;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            if (!drop) { inputs[(int)KeyInput.GoRight] = right; inputs[(int)KeyInput.GoLeft] = left; }
            if (jump && i < jumpHoldFrames) inputs[(int)KeyInput.Jump] = true;

            EnsureVirtualFloorRealtime();

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);
            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));
        }
        return frames;
    }

    void EnsureVirtualFloorRealtime()
    {
        if (ghostAgent.mSpeed.y > 0.1f) return;

        Vector2i centerTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
        bool inSafeZone = false;

        if (map.survivalSpaceTiles != null)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (map.survivalSpaceTiles.Contains(new Vector2i(centerTile.x + dx, centerTile.y + dy)))
                    {
                        inSafeZone = true;
                        break;
                    }
                }
                if (inSafeZone) break;
            }
        }

        if (inSafeZone || ghostAgent.mPosition.y <= currentVirtualFloorY)
        {
            float feetY = ghostAgent.mPosition.y - ghostAgent.mAABB.HalfSizeY;
            Vector2i feetTile = map.GetMapTileAtPoint(new Vector2(ghostAgent.mPosition.x, feetY));

            int blockY = feetTile.y - 1;
            float targetFeetY = map.GetMapTilePosition(feetTile.x, blockY).y + (Map.cTileSize / 2.0f);

            if (feetY <= targetFeetY + Map.cTileSize)
            {
                ghostAgent.mPosition.y = targetFeetY + ghostAgent.mAABB.HalfSizeY;
                ghostAgent.mSpeed.y = 0;
                ghostAgent.mOnGround = true;

                float leftEdge = ghostAgent.mPosition.x - ghostAgent.mAABB.HalfSizeX - Map.cTileSize * 1.5f;
                float rightEdge = ghostAgent.mPosition.x + ghostAgent.mAABB.HalfSizeX + Map.cTileSize * 1.5f;

                int minX = map.GetMapTileAtPoint(new Vector2(leftEdge, ghostAgent.mPosition.y)).x;
                int maxX = map.GetMapTileAtPoint(new Vector2(rightEdge, ghostAgent.mPosition.y)).x;

                for (int bx = minX; bx <= maxX; bx++)
                {
                    if (bx >= 0 && bx < map.mWidth && blockY >= 0 && blockY < map.mHeight)
                    {
                        map.SetTile(bx, blockY, TileType.Block);
                        ghostSafePlatforms.Add(new Vector2i(bx, blockY));
                        ghostSafeColumns.Add(bx);
                    }
                }

                if (inSafeZone) currentVirtualFloorY = targetFeetY - Map.cTileSize * 5f;
            }
        }
    }

    void RecordGhostTrajectory()
    {
        AABB box = ghostAgent.mAABB;
        float padding = 6.0f;
        Vector2 min = box.Center - box.HalfSize - Vector2.one * padding;
        Vector2 max = box.Center + box.HalfSize + Vector2.one * padding;
        Vector2i bl = map.GetMapTileAtPoint(min);
        Vector2i tr = map.GetMapTileAtPoint(max);
        for (int x = bl.x; x <= tr.x; x++)
        {
            for (int y = bl.y; y <= tr.y; y++)
            {
                if (x >= 0 && x < map.mWidth && y >= 0 && y < map.mHeight)
                {
                    Vector2i pos = new Vector2i(x, y);
                    if (!ghostPathSet.Contains(pos))
                    {
                        ghostPathSet.Add(pos);
                        ghostPath.Add(pos);
                    }
                }
            }
        }
    }

    void BakeLevelToMapDataOnly(List<Vector3> trajectory, HashSet<Vector2i> safePlatforms, Vector2i start, Vector2i end)
    {
        map.ClearMapToEmpty();
        HashSet<Vector2i> airMask = new HashSet<Vector2i>();

        foreach (var point in trajectory)
        {
            Vector2i t = map.GetMapTileAtPoint(point);
            for (int dx = -4; dx <= 4; dx++)
                for (int dy = -4; dy <= 4; dy++)
                    airMask.Add(new Vector2i(t.x + dx, t.y + dy));
        }

        if (map.survivalSpaceTiles != null)
        {
            foreach (Vector2i safeTile in map.survivalSpaceTiles) airMask.Add(safeTile);
        }

        float seed = Random.Range(0f, 100f);

        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i currentPos = new Vector2i(x, y);

                if (safePlatforms != null && safePlatforms.Contains(currentPos)) { map.SetTile(x, y, TileType.Block); continue; }
                if (airMask.Contains(currentPos)) { map.SetTile(x, y, TileType.Empty); continue; }
                if (y < 2) { map.SetTile(x, y, TileType.Block); continue; }

                float noiseVal = Mathf.PerlinNoise(x * noiseScale + seed, y * noiseScale + seed);
                float heightAtten = 1.0f - ((float)y / map.mHeight) * 0.5f;

                if (noiseVal * heightAtten > (1.0f - blockDensity)) map.SetTile(x, y, TileType.Block);
                else map.SetTile(x, y, TileType.Empty);
            }
        }

        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                Vector2i cur = new Vector2i(x, y);
                Vector2i up = new Vector2i(x, y + 1);
                Vector2i down = new Vector2i(x, y - 1);

                bool isSafeZone = false;
                if (map.survivalSpaceTiles != null)
                {
                    isSafeZone = map.survivalSpaceTiles.Contains(cur) ||
                                 map.survivalSpaceTiles.Contains(up) ||
                                 map.survivalSpaceTiles.Contains(down);
                }

                if (isSafeZone) continue;

                if (map.GetTile(x, y) == TileType.Empty && !airMask.Contains(new Vector2i(x, y)))
                {
                    bool topBlock = map.GetTile(x, y + 1) == TileType.Block;
                    bool bottomBlock = map.GetTile(x, y - 1) == TileType.Block;

                    if (Random.value < 0.9f)
                    {
                        if (topBlock) SpawnSpike(x, y, true);
                        else if (bottomBlock) SpawnSpike(x, y, false);
                    }
                }
            }
        }

        if (start.x != -1)
        {
            for (int dx = -2; dx <= 2; dx++) FillColumn(start.x + dx, 0, start.y - 1, TileType.Block);
        }
        if (end.x != -1)
        {
            for (int dx = -2; dx <= 2; dx++) FillColumn(end.x + dx, 0, end.y - 1, TileType.Block);
        }
    }

    public void BakeIWBTGLevel(LevelIndividual goldenLevel)
    {
        List<Vector2> trapPositions = new List<Vector2>();

        float safeDistance = Mathf.Max(hardcoreDeviationTolerance, 3.5f) * Map.cTileSize;

        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                if (map.GetTile(x, y) == TileType.Empty)
                {
                    if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Contains(new Vector2i(x, y)))
                        continue;

                    Vector2 worldPos = map.GetMapTilePosition(x, y);
                    float minDist = float.MaxValue;

                    foreach (var pos in goldenLevel.trajectory)
                    {
                        float dist = Vector2.Distance(worldPos, (Vector2)pos);
                        if (dist < minDist) minDist = dist;
                    }

                    if (minDist > safeDistance)
                    {
                        trapPositions.Add(worldPos);
                        map.SetTile(x, y, TileType.Danger);

                        if (map.spikePrefab != null)
                        {
                            Instantiate(map.spikePrefab, new Vector3(worldPos.x, worldPos.y, -5f), Quaternion.identity);
                        }
                    }
                }
            }
        }

        map.GenerateHeatmap(trapPositions);
        Debug.Log($">>> IWBTG 烘焙完成！全图共固化 {trapPositions.Count} 个永久陷阱，已锁定唯一生存路线。");
    }
}