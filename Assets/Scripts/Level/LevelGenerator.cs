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

public class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;
    public AdversarialDirector director;

    [Header("Generation Style")]
    [Range(0f, 1f)] public float blockDensity = 0.45f; // 砖块密度：0=全空，1=全满。建议 0.4-0.5
    [Range(0.01f, 0.5f)] public float noiseScale = 0.15f; // 噪声缩放：越小地形越平缓巨大，越大越破碎

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

        Debug.Log($">>> MAP-Elites 开始演化 (目标: {iterations})...");

        while (validLevelsFound < iterations && attempts < iterations * 20)
        {
            attempts++;

            if (RunGhostSimulation(startTile, endTile))
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
                    }
                }

                map.ClearMapToEmpty();
            }
        }

        Debug.Log($">>> 演化结束。尝试 {attempts} 次，发现了 {validLevelsFound} 个有效关卡。");
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
            }
            map.ApplyGeneratedPath(target.path, target.replay, target.trajectory, target.safeColumns);
        }
    }

    // ==========================================
    // Phase 1: Ghost Simulation
    // ==========================================
    bool RunGhostSimulation(Vector2i startTile, Vector2i endTile)
    {
        ghostPath.Clear();
        ghostPathSet.Clear();
        ghostReplay.Clear();
        ghostTrajectory.Clear();
        ghostSafeColumns.Clear();

        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mOnGround = false;

        currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;

        int safetyCounter = 0;
        float lastXProgress = ghostAgent.mPosition.x;
        int stagnationCount = 0;

        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 2000)
        {
            safetyCounter++;
            float heightDiff = endWorldPos.y - currentVirtualFloorY;
            float noise = Random.Range(-0.2f, 0.2f);
            float bias = Mathf.Clamp(heightDiff / 100.0f + noise, -0.5f, 0.5f);

            if (ghostAgent.mPosition.x - lastXProgress < 1.0f) stagnationCount++;
            else stagnationCount = 0;
            lastXProgress = ghostAgent.mPosition.x;

            ActionType nextAction;
            if (stagnationCount > 3) { nextAction = ActionType.LongJumpRight; stagnationCount = 0; }
            else nextAction = PickAction();

            ExecuteGhostAction(nextAction, bias);

            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }
        return (ghostAgent.mPosition.x >= endWorldPos.x);
    }

    // ==========================================
    // Phase 2: Verification
    // ==========================================
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
        int maxFrames = ghostReplay.Count + 60;

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

    // =================================================================
    // [核心修改] 伪开放结构生成 (Noise Filling + Path Masking)
    // =================================================================
    void BakeLevelToMapDataOnly(List<Vector3> trajectory, HashSet<int> safeCols, Vector2i start, Vector2i end)
    {
        map.ClearMapToEmpty();

        // 1. 构建安全区掩码 (Safety Mask)
        // 凡是轨迹经过的地方，绝对不能生成砖块 (挖空)
        // 凡是需要落脚的地方，绝对要生成砖块 (平台)
        HashSet<Vector2i> airMask = new HashSet<Vector2i>();
        Dictionary<int, int> platformMask = new Dictionary<int, int>();

        int padding = 2; // 头部空间预留
        float seed = Random.Range(0f, 100f);

        // 分析轨迹，构建 Mask
        foreach (var point in trajectory)
        {
            int x = Mathf.RoundToInt((point.x - map.position.x) / Map.cTileSize);
            int y = Mathf.RoundToInt((point.y - map.position.y) / Map.cTileSize);

            // 标记空气区域（轨迹周围）
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= padding; dy++)
                {
                    airMask.Add(new Vector2i(x + dx, y + dy));
                }
            }

            // 标记落脚平台
            if (safeCols.Contains(x))
            {
                // 注意：我们这里不填满柱子，只记录落脚点，柱子由下面的噪声生成来决定是否连接地面
                if (!platformMask.ContainsKey(x) || y < platformMask[x])
                {
                    platformMask[x] = y - 1; // 脚下一格是平台
                }
            }
        }

        // 2. 遍历全图，使用柏林噪声生成 "虚假地形"
        for (int x = 0; x < map.mWidth; x++)
        {
            for (int y = 0; y < map.mHeight; y++)
            {
                Vector2i currentPos = new Vector2i(x, y);

                // [优先级 1] 强制空气 (挖空路径)
                if (airMask.Contains(currentPos))
                {
                    map.SetTile(x, y, TileType.Empty);
                    continue;
                }

                // [优先级 2] 强制平台 (落脚点)
                if (platformMask.ContainsKey(x) && platformMask[x] == y)
                {
                    map.SetTile(x, y, TileType.Block);
                    continue;
                }

                // [优先级 3] 强制地面 (防止玩家掉出世界，保留最底部的基座)
                if (y < 2)
                {
                    map.SetTile(x, y, TileType.Block);
                    continue;
                }

                // [优先级 4] 伪随机地形生成 (Openness Control)
                // 使用 Perlin Noise 生成自然的斑块、浮岛和结构
                float noiseVal = Mathf.PerlinNoise(x * noiseScale + seed, y * noiseScale + seed);

                // 增加高度衰减：越高的地方，生成砖块的概率越低 (让顶部更开阔)
                float heightAtten = 1.0f - ((float)y / map.mHeight) * 0.5f;

                // 最终判定
                if (noiseVal * heightAtten > (1.0f - blockDensity))
                {
                    map.SetTile(x, y, TileType.Block);
                }
                else
                {
                    map.SetTile(x, y, TileType.Empty);
                }
            }
        }

        // 3. 装饰性刺生成 (Spike Coating)
        // 遍历所有空气格子，如果它紧邻一个 Block，有机率生成刺
        for (int x = 1; x < map.mWidth - 1; x++)
        {
            for (int y = 1; y < map.mHeight - 1; y++)
            {
                // 只在空气处生成刺，且不能在安全路径上
                if (map.GetTile(x, y) == TileType.Empty && !airMask.Contains(new Vector2i(x, y)))
                {
                    bool topBlock = map.GetTile(x, y + 1) == TileType.Block;
                    bool bottomBlock = map.GetTile(x, y - 1) == TileType.Block;

                    // 避免刺生成得太密，增加随机性
                    if (Random.value < 0.15f)
                    {
                        if (topBlock) SpawnSpike(x, y, true); // 倒刺
                        else if (bottomBlock) SpawnSpike(x, y, false); // 地刺
                    }
                }
            }
        }

        // 4. 起点终点加固
        if (start.x != -1) FillColumn(start.x, 0, start.y - 1, TileType.Block);
        if (end.x != -1) FillColumn(end.x, 0, end.y - 1, TileType.Block);
    }

    void FillColumn(int x, int yStart, int yEnd, TileType type)
    {
        for (int y = yStart; y <= yEnd; y++)
        {
            map.SetTile(x, y, type);
        }
    }

    void SpawnSpike(int x, int y, bool flipped)
    {
        if (map.GetTile(x, y) == TileType.Empty)
        {
            map.SetTile(x, y, TileType.Danger);
        }
    }

    ActionType PickAction()
    {
        float r = Random.value;
        if (r < 0.2f) return ActionType.MoveRight;
        if (r < 0.5f) return ActionType.JumpRight;
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
}