using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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

public class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;
    public AdversarialDirector director;

    [Header("Generation Settings")]
    [Range(2, 10)] public int generationSegments = 4;
    [Range(0f, 1f)] public float blockDensity = 0.45f;
    [Range(0.01f, 0.5f)] public float noiseScale = 0.15f;

    // 终点预制体，请在 Inspector 中拖入包含 Collectible (ItemType.Finish) 的 Prefab
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

    private List<Vector3> verifiedTrajectory = new List<Vector3>();

    enum ActionType { MoveRight, JumpRight, LongJumpRight }

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

    public void GenerateMapElitesLibrary(Vector2i startTile, Vector2i endTile, int iterations)
    {
        Initialize();
        if (director != null) director.SetRunning(false);
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;
        int attempts = 0;
        int maxAttempts = iterations * 100;

        Debug.Log($">>> 开始生成 (目标: {iterations} 个样本, 最大尝试: {maxAttempts} 次)...");

        while (validLevelsFound < iterations && attempts < maxAttempts)
        {
            attempts++;
            if (RunSegmentedSimulation(startTile, endTile))
            {
                BakeLevelToMapDataOnly(ghostTrajectory, ghostSafeColumns, startTile, endTile);
                if (VerifyLevelWithRealPhysics(startTile, endTile))
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
                        newInd.linearity = lin;
                        newInd.inputDensity = den;
                        newInd.fitness = fit;
                        eliteGrid[x, y] = newInd;
                        validLevelsFound++;
                        if (validLevelsFound % 5 == 0) Debug.Log($"进度: {validLevelsFound}/{iterations} (尝试 {attempts} 次)");
                    }
                }
                map.ClearMapToEmpty();
            }
        }
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
                BakeLevelToMapDataOnly(target.trajectory, target.safeColumns, start, end);

                // --- 物理层面安放终点 ---
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

    bool RunSegmentedSimulation(Vector2i startTile, Vector2i endTile)
    {
        ClearGhostData();
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);
        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mOnGround = false;
        currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;
        float totalDistance = endWorldPos.x - startWorldPos.x;
        float segmentLength = totalDistance / generationSegments;
        GhostSnapshot currentSnapshot = new GhostSnapshot(ghostAgent, currentVirtualFloorY, ghostPath, ghostReplay, ghostTrajectory, ghostSafeColumns);
        for (int i = 1; i <= generationSegments; i++)
        {
            float targetX = startWorldPos.x + segmentLength * i;
            if (i == generationSegments) targetX = endWorldPos.x;
            bool segmentSuccess = false;
            int segmentAttempts = 0;
            while (!segmentSuccess && segmentAttempts < 200)
            {
                segmentAttempts++;
                RestoreSnapshot(currentSnapshot);
                if (SimulateSegment(targetX, endWorldPos))
                {
                    segmentSuccess = true;
                    currentSnapshot = new GhostSnapshot(ghostAgent, currentVirtualFloorY, ghostPath, ghostReplay, ghostTrajectory, ghostSafeColumns);
                }
            }
            if (!segmentSuccess) return false;
        }
        return true;
    }

    void RestoreSnapshot(GhostSnapshot snap)
    {
        ghostAgent.mPosition = snap.position;
        ghostAgent.mSpeed = snap.speed;
        ghostAgent.mOnGround = snap.onGround;
        currentVirtualFloorY = snap.virtualFloorY;
        ghostPath = new List<Vector2i>(snap.path);
        ghostPathSet = new HashSet<Vector2i>(snap.path);
        ghostReplay = new List<ReplayFrame>(snap.replay);
        ghostTrajectory = new List<Vector3>(snap.trajectory);
        ghostSafeColumns = new HashSet<int>(snap.safeColumns);
    }

    bool SimulateSegment(float targetX, Vector2 finalDest)
    {
        int safetyCounter = 0;
        float lastXProgress = ghostAgent.mPosition.x;
        int stagnationCount = 0;
        while (ghostAgent.mPosition.x < targetX && safetyCounter < 300)
        {
            safetyCounter++;
            if (ghostAgent.mPosition.x - lastXProgress < 1.0f) stagnationCount++; else stagnationCount = 0;
            lastXProgress = ghostAgent.mPosition.x;
            ActionType nextAction;
            if (stagnationCount > 3)
            {
                nextAction = ActionType.LongJumpRight;
                stagnationCount = 0;
            }
            else nextAction = PickAction(ghostAgent.mPosition, finalDest);
            float heightDiff = finalDest.y - currentVirtualFloorY;
            float bias = Mathf.Clamp(heightDiff / 100.0f + Random.Range(-0.2f, 0.4f), -0.5f, 0.6f);
            ExecuteGhostAction(nextAction, bias);
            if (ghostAgent.mPosition.y < map.position.y) return false;
        }
        return (ghostAgent.mPosition.x >= targetX);
    }

    void ClearGhostData()
    {
        ghostPath.Clear();
        ghostPathSet.Clear();
        ghostReplay.Clear();
        ghostTrajectory.Clear();
        ghostSafeColumns.Clear();
    }

    bool VerifyLevelWithRealPhysics(Vector2i startTile, Vector2i endTile)
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
            if (validatorAgent.mPosition.y < map.position.y) return false;
            if (validatorAgent.mCurrentState == Character.CharacterState.Die) return false;
            if (Vector2.Distance(validatorAgent.mPosition, endWorldPos) < Map.cTileSize * 2) return true;
            frameIndex++;
        }
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
        float distToGoal = endPos.x - currentPos.x;
        float forwardBias = (distToGoal > 100f) ? 0.5f : 0.3f;
        float r = Random.value;
        if (r < forwardBias) return ActionType.MoveRight;
        if (Random.value < 0.6f) return ActionType.JumpRight;
        return ActionType.LongJumpRight;
    }

    void ExecuteGhostAction(ActionType action, float heightBias)
    {
        int frames = 0;
        bool jump = false;
        bool right = true;
        switch (action)
        {
            case ActionType.MoveRight: frames = 15; break;
            case ActionType.JumpRight: frames = 25; jump = true; break;
            case ActionType.LongJumpRight: frames = 45; jump = true; break;
        }
        if (jump)
        {
            float heightChangeTiles = 0;
            float r = Random.value;
            if (r < 0.4f) heightChangeTiles = Random.Range(1.0f, 4.0f);
            else if (r < 0.7f) heightChangeTiles = Random.Range(-6.0f, -2.0f);
            else heightChangeTiles = 0;
            heightChangeTiles += heightBias * 5.0f;
            float changeAmount = heightChangeTiles * Map.cTileSize;
            float newFloor = currentVirtualFloorY + changeAmount;
            float mapBottom = map.position.y + Map.cTileSize * 2;
            float mapTop = map.position.y + (map.mHeight - 8) * Map.cTileSize;
            newFloor = Mathf.Max(mapBottom, Mathf.Min(newFloor, mapTop));
            currentVirtualFloorY = newFloor;
        }
        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            inputs[(int)KeyInput.GoRight] = right;
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;
            ghostAgent.SimulationUpdate(SIM_STEP, inputs);
            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));
            if (CheckVirtualFloorCollision()) { }
        }
    }

    bool CheckVirtualFloorCollision()
    {
        if (ghostAgent.mSpeed.y <= 0 && ghostAgent.mPosition.y <= currentVirtualFloorY)
        {
            ghostAgent.mPosition.y = currentVirtualFloorY;
            ghostAgent.mSpeed.y = 0;
            ghostAgent.mOnGround = true;
            int landingCol = Mathf.RoundToInt((ghostAgent.mPosition.x - map.position.x) / Map.cTileSize);
            ghostSafeColumns.Add(landingCol);
            ghostSafeColumns.Add(landingCol + 1);
            ghostSafeColumns.Add(landingCol - 1);
            return true;
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

    void BakeLevelToMapDataOnly(List<Vector3> trajectory, HashSet<int> safeCols, Vector2i start, Vector2i end)
    {
        map.ClearMapToEmpty();

        HashSet<Vector2i> airMask = new HashSet<Vector2i>();
        Dictionary<int, int> platformMask = new Dictionary<int, int>();

        int padding = 2;
        float seed = Random.Range(0f, 100f);

        // 1. 幽灵轨迹遮罩
        foreach (var point in trajectory)
        {
            int x = Mathf.RoundToInt((point.x - map.position.x) / Map.cTileSize);
            int y = Mathf.RoundToInt((point.y - map.position.y) / Map.cTileSize);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = 0; dy <= padding; dy++)
                    airMask.Add(new Vector2i(x + dx, y + dy));

            if (safeCols.Contains(x))
            {
                if (!platformMask.ContainsKey(x) || y < platformMask[x])
                    platformMask[x] = y - 1;
            }
        }

        // --- 核心修复：强制整合设计师的生存空间 ---
        if (map.survivalSpaceTiles != null)
        {
            foreach (Vector2i safeTile in map.survivalSpaceTiles)
            {
                // 设计师画过的地方，强制设为空气通道
                airMask.Add(safeTile);

                // 托底逻辑：检查当前生存空间下方的一格。
                // 如果下方不是生存空间，说明这是地板边缘，强制要求在此生成一块实体砖块！
                Vector2i tileBelow = new Vector2i(safeTile.x, safeTile.y - 1);
                if (!map.survivalSpaceTiles.Contains(tileBelow))
                {
                    // 覆写 platformMask，确保这里长出垫脚石
                    if (!platformMask.ContainsKey(tileBelow.x) || tileBelow.y > platformMask[tileBelow.x])
                    {
                        platformMask[tileBelow.x] = tileBelow.y;
                    }
                }
            }
        }
        // ----------------------------------------

        // 2. 地形填充与冲突裁决
        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i currentPos = new Vector2i(x, y);

                // 如果位于遮罩内（AI 轨迹或设计师的生存通道），必须是空的
                if (airMask.Contains(currentPos)) { map.SetTile(x, y, TileType.Empty); continue; }

                // 如果是托底区域，必须是实体砖块
                if (platformMask.ContainsKey(x) && platformMask[x] >= y) { map.SetTile(x, y, TileType.Block); continue; }

                // 底层基座
                if (y < 2) { map.SetTile(x, y, TileType.Block); continue; }

                // 其他无关区域，交给柏林噪声随机生成
                float noiseVal = Mathf.PerlinNoise(x * noiseScale + seed, y * noiseScale + seed);
                float heightAtten = 1.0f - ((float)y / map.mHeight) * 0.5f;

                if (noiseVal * heightAtten > (1.0f - blockDensity)) map.SetTile(x, y, TileType.Block);
                else map.SetTile(x, y, TileType.Empty);
            }
        }

        // 3. 装饰性静态尖刺生成
        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                Vector2i cur = new Vector2i(x, y);
                Vector2i up = new Vector2i(x, y + 1);
                Vector2i down = new Vector2i(x, y - 1);

                // 设计师规划的生存区附近，绝对不长天然尖刺
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

        // 起点与终点的保护立柱
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
                    // 确保不会把陷阱放进生存空间里
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