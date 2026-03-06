using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

// 注意这里加了 partial 关键字，以便和视觉、日志文件合并
public partial class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;
    public AdversarialDirector director;

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

    enum ActionType { MoveRight, MoveLeft, JumpRight, JumpLeft, LongJumpRight, LongJumpLeft, Drop }

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

        Vector2i[] directions = new Vector2i[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };

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

    public void GenerateMapElitesLibrary(Vector2i startTile, Vector2i endTile, int iterations)
    {
        StartCoroutine(GenerateMapElitesRoutine(startTile, endTile, iterations));
    }

    private IEnumerator GenerateMapElitesRoutine(Vector2i startTile, Vector2i endTile, int iterations)
    {
        Initialize();
        if (director != null) director.SetRunning(false);
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;
        int attempts = 0;
        int maxAttempts = iterations * 150;

        int failTimeoutCount = 0;
        int failFallCount = 0;
        int failVerifyFallCount = 0;
        int failVerifyDieCount = 0;
        int failVerifyTimeoutCount = 0;

        // 调用日志文件的初始化接口
        InitLog("分离可视化与日志模块版本", iterations, maxAttempts);

        BuildSurvivalGradient(endTile);
        Debug.Log($">>> 开始生成全向引力关卡 (目标: {iterations} 个样本, 最大尝试: {maxAttempts} 次)...");

        while (validLevelsFound < iterations && attempts < maxAttempts)
        {
            attempts++;
            string failReason = "";

            if (RunGuidedSimulation(startTile, endTile, out failReason))
            {
                BakeLevelToMapDataOnly(ghostTrajectory, ghostSafePlatforms, startTile, endTile);

                // 调用可视化文件的接口展示成功前的一瞥
                yield return StartCoroutine(ShowSearchVisualsRoutine(ghostTrajectory));

                if (VerifyLevelWithRealPhysics(startTile, endTile, out failReason))
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

                        // 调用日志接口记录成功
                        LogSuccess(validLevelsFound, iterations, attempts);
                        Debug.Log($"进度: {validLevelsFound}/{iterations} (尝试 {attempts} 次)");

                        // 调用可视化文件的接口展示最终通过路线
                        yield return StartCoroutine(ShowSuccessVisualsRoutine(verifiedTrajectory));
                    }
                }
                else
                {
                    if (failReason == "VerifyFall") failVerifyFallCount++;
                    else if (failReason == "VerifyDie") failVerifyDieCount++;
                    else if (failReason == "VerifyTimeout") failVerifyTimeoutCount++;
                }

                map.ClearMapToEmpty();
                ClearVisuals();
            }
            else
            {
                if (failReason == "Timeout") failTimeoutCount++;
                else if (failReason == "FallOut") failFallCount++;

                if (attempts % 15 == 0)
                {
                    // 调用可视化文件展示失败挣扎线路
                    yield return StartCoroutine(ShowSearchVisualsRoutine(ghostTrajectory));
                }
            }

            if (attempts % 100 == 0)
            {
                // 调用日志接口播报阶段性状态
                LogStatus(attempts, validLevelsFound, failTimeoutCount, failFallCount, failVerifyFallCount, failVerifyDieCount, failVerifyTimeoutCount);

                failTimeoutCount = 0;
                failFallCount = 0;
                failVerifyFallCount = 0;
                failVerifyDieCount = 0;
                failVerifyTimeoutCount = 0;
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
            Debug.Log($"加载关卡 -> Linearity: {target.linearity:F2}, Density: {target.inputDensity:F2}");
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

            if (enableIWBTGBaking)
            {
                BakeIWBTGLevel(target);
            }
        }
        else
        {
            Debug.LogError("未能生成任何有效关卡");
        }
    }

    bool RunGuidedSimulation(Vector2i startTile, Vector2i endTile, out string finalReason)
    {
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);
        int microAttempts = 200;
        finalReason = "";

        for (int i = 0; i < microAttempts; i++)
        {
            ClearGhostData();
            ghostAgent.mPosition = startWorldPos;
            ghostAgent.mSpeed = Vector2.zero;
            ghostAgent.mCurrentState = Character.CharacterState.Stand;
            ghostAgent.mOnGround = false;
            currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;

            if (SimulateGuidedPath(endWorldPos, out finalReason))
            {
                return true;
            }
        }
        return false;
    }

    bool SimulateGuidedPath(Vector2 finalDest, out string reason)
    {
        int framesLimit = 1500;
        int currentFrames = 0;
        int stagnationCount = 0;
        Vector2 lastProgressPos = ghostAgent.mPosition;

        while (currentFrames < framesLimit)
        {
            if (Vector2.Distance(ghostAgent.mPosition, finalDest) < Map.cTileSize * 3)
            {
                reason = "Success";
                return true;
            }
            if (ghostAgent.mPosition.y < map.position.y - 100f)
            {
                reason = "FallOut";
                return false;
            }

            if (Vector2.Distance(ghostAgent.mPosition, lastProgressPos) < 2.0f)
            {
                stagnationCount++;
            }
            else
            {
                stagnationCount = 0;
                lastProgressPos = ghostAgent.mPosition;
            }

            ActionType nextAction;
            if (stagnationCount > 8)
            {
                nextAction = (Random.value > 0.5f) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
                stagnationCount = 0;
            }
            else
            {
                nextAction = PickAction(ghostAgent.mPosition, finalDest);
            }

            currentFrames += ExecuteGhostAction(nextAction);
        }

        reason = "Timeout";
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

    bool VerifyLevelWithRealPhysics(Vector2i startTile, Vector2i endTile, out string reason)
    {
        verifiedTrajectory.Clear();
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
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
                reason = "VerifyFall";
                return false;
            }
            if (validatorAgent.mCurrentState == Character.CharacterState.Die)
            {
                reason = "VerifyDie";
                return false;
            }
            if (Vector2.Distance(validatorAgent.mPosition, endWorldPos) < Map.cTileSize * 2)
            {
                reason = "Success";
                return true;
            }
            frameIndex++;
        }

        reason = "VerifyTimeout";
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

    ActionType PickAction(Vector2 currentPos, Vector2 endPos)
    {
        Vector2i curTile = map.GetMapTileAtPoint(currentPos);
        float weightRight = 1f;
        float weightLeft = 1f;
        float weightUp = 1f;
        float weightDown = 1f;

        if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
        {
            int bestRight = int.MaxValue, bestLeft = int.MaxValue, bestUp = int.MaxValue, bestDown = int.MaxValue;
            int scanRadius = 3;

            int currentStroke = -1;
            if (map.survivalSpaceStrokeOrder != null) map.survivalSpaceStrokeOrder.TryGetValue(curTile, out currentStroke);

            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            {
                for (int dy = -scanRadius; dy <= scanRadius; dy++)
                {
                    Vector2i targetTile = new Vector2i(curTile.x + dx, curTile.y + dy);

                    if (map.survivalSpaceTiles.Contains(targetTile))
                    {
                        float baseWeight = 2f;
                        int targetStroke = -1;
                        if (map.survivalSpaceStrokeOrder != null) map.survivalSpaceStrokeOrder.TryGetValue(targetTile, out targetStroke);

                        if (targetStroke > currentStroke && targetStroke != -1)
                        {
                            baseWeight += 50f;
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

            int currentDist = int.MaxValue;
            if (survivalGradient.TryGetValue(curTile, out int cDist)) currentDist = cDist;

            if (bestRight < currentDist) weightRight += 20f;
            if (bestLeft < currentDist) weightLeft += 20f;
            if (bestUp < currentDist) weightUp += 20f;
            if (bestDown < currentDist) weightDown += 20f;
        }
        else
        {
            if (endPos.x > currentPos.x) weightRight += 5f;
            else weightLeft += 5f;
            if (endPos.y > currentPos.y) weightUp += 5f;
            else weightDown += 5f;
        }

        float totalWeight = weightRight + weightLeft + weightUp + weightDown;
        float r = Random.Range(0, totalWeight);

        if (r < weightRight) return (Random.value > 0.4f) ? ActionType.MoveRight : ActionType.JumpRight;
        r -= weightRight;
        if (r < weightLeft) return (Random.value > 0.4f) ? ActionType.MoveLeft : ActionType.JumpLeft;
        r -= weightLeft;
        if (r < weightUp) return (Random.value > 0.5f) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
        return ActionType.Drop;
    }

    int ExecuteGhostAction(ActionType action)
    {
        int frames = 0;
        bool right = true;
        bool left = false;
        bool jump = false;
        bool drop = false;

        switch (action)
        {
            case ActionType.MoveRight: frames = 15; right = true; break;
            case ActionType.MoveLeft: frames = 15; left = true; right = false; break;
            case ActionType.JumpRight: frames = 25; right = true; jump = true; break;
            case ActionType.JumpLeft: frames = 25; left = true; right = false; jump = true; break;
            case ActionType.LongJumpRight: frames = 45; right = true; jump = true; break;
            case ActionType.LongJumpLeft: frames = 45; left = true; right = false; jump = true; break;
            case ActionType.Drop: frames = 20; drop = true; right = false; left = false; break;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            if (!drop)
            {
                inputs[(int)KeyInput.GoRight] = right;
                inputs[(int)KeyInput.GoLeft] = left;
            }
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);
            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));

            if (CheckVirtualFloorCollision()) { }
        }
        return frames;
    }

    bool CheckVirtualFloorCollision()
    {
        if (ghostAgent.mSpeed.y <= 0)
        {
            Vector2i curTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
            Vector2i tileBelow = new Vector2i(curTile.x, curTile.y - 1);

            bool shouldLand = false;
            float landY = currentVirtualFloorY;

            if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
            {
                if (map.survivalSpaceTiles.Contains(curTile))
                {
                    shouldLand = true;
                    landY = map.GetMapTilePosition(curTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;
                }
                else if (map.survivalSpaceTiles.Contains(tileBelow))
                {
                    shouldLand = true;
                    landY = map.GetMapTilePosition(tileBelow).y + Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;
                }
            }

            if (!shouldLand && ghostAgent.mPosition.y <= currentVirtualFloorY)
            {
                shouldLand = true;
                landY = currentVirtualFloorY;
            }

            if (shouldLand && ghostAgent.mPosition.y <= landY + 2f)
            {
                ghostAgent.mPosition.y = landY;
                ghostAgent.mSpeed.y = 0;
                ghostAgent.mOnGround = true;

                int landingColX = Mathf.RoundToInt((ghostAgent.mPosition.x - map.position.x) / Map.cTileSize);
                int landingColY = Mathf.RoundToInt((landY - ghostAgent.mAABB.HalfSizeY - map.position.y) / Map.cTileSize);

                ghostSafeColumns.Add(landingColX);
                ghostSafeColumns.Add(landingColX + 1);
                ghostSafeColumns.Add(landingColX - 1);

                ghostSafePlatforms.Add(new Vector2i(landingColX, landingColY));
                ghostSafePlatforms.Add(new Vector2i(landingColX + 1, landingColY));
                ghostSafePlatforms.Add(new Vector2i(landingColX - 1, landingColY));

                return true;
            }
        }
        return false;
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
            int x = Mathf.RoundToInt((point.x - map.position.x) / Map.cTileSize);
            int y = Mathf.RoundToInt((point.y - map.position.y) / Map.cTileSize);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -1; dy <= 3; dy++)
                    airMask.Add(new Vector2i(x + dx, y + dy));
        }

        if (map.survivalSpaceTiles != null)
        {
            foreach (Vector2i safeTile in map.survivalSpaceTiles)
            {
                airMask.Add(safeTile);
            }
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

                    float spawnProbability = 0.9f;

                    if (Random.value < spawnProbability)
                    {
                        if (topBlock) SpawnSpike(x, y, true);
                        else if (bottomBlock) SpawnSpike(x, y, false);
                    }
                }
            }
        }

        if (start.x != -1) FillColumn(start.x, 0, start.y - 1, TileType.Block);
        if (end.x != -1) FillColumn(end.x, 0, end.y - 1, TileType.Block);
    }

    public void BakeIWBTGLevel(LevelIndividual goldenLevel)
    {
        List<Vector2> trapPositions = new List<Vector2>();

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

                    if (minDist > hardcoreDeviationTolerance * Map.cTileSize)
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