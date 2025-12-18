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
    public AdversarialDirector director; // [新增] 引用导演

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

        // [关键修复 1] 生成期间，强制关闭对抗导演，防止陷阱干扰验证
        if (director != null) director.SetRunning(false);

        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;
        int attempts = 0;

        Debug.Log($">>> MAP-Elites 开始演化 (目标有效样本: {iterations})...");

        // 这里的逻辑改为：直到找到足够数量的“真正可通关”关卡，或者尝试次数过多
        while (validLevelsFound < iterations && attempts < iterations * 10) // 增加尝试次数上限
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

        Debug.Log($">>> 演化结束。尝试 {attempts} 次，发现了 {validLevelsFound} 个经物理验证的可通关关卡。");

        // [关键修复 2] 生成结束后，重新启用导演 (如果是在 TrialPlay 模式下)
        // 注意：具体的启用时机通常在 Map.cs 的 StartTrialMode 里，这里只需确保不永久关闭即可
        // 但为了安全，我们保持关闭，直到 Map.cs 显式开启它

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
            map.ApplyGeneratedPath(target.path, target.replay, target.trajectory, target.safeColumns);
        }
        else
        {
            Debug.LogError("MAP-Elites 库为空，生成失败！");
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

            // [调试日志] 验证失败原因
            if (validatorAgent.mPosition.y < map.position.y)
            {
                // Debug.Log("验证失败: 掉出地图"); // 调试用
                return false;
            }

            // 注意：因为我们现在只生成 Block 或 OneWay，没有 Danger，所以不用检测 TileType.Danger

            // 成功条件
            if (Vector2.Distance(validatorAgent.mPosition, endWorldPos) < Map.cTileSize * 2)
            {
                return true;
            }

            frameIndex++;
        }

        // Debug.Log("验证失败: 超时未到达终点");
        return false;
    }

    void BakeLevelToMapDataOnly(List<Vector3> trajectory, HashSet<int> safeCols, Vector2i start, Vector2i end)
    {
        map.ClearMapToEmpty();

        Dictionary<int, int> columnFloorY = new Dictionary<int, int>();
        foreach (var point in trajectory)
        {
            int x = Mathf.RoundToInt((point.x - map.position.x) / Map.cTileSize);
            int y = Mathf.RoundToInt((point.y - map.position.y) / Map.cTileSize);
            if (!columnFloorY.ContainsKey(x)) columnFloorY[x] = y;
            else if (y < columnFloorY[x]) columnFloorY[x] = y;
        }

        foreach (int x in safeCols)
        {
            if (columnFloorY.ContainsKey(x))
            {
                int footY = columnFloorY[x];
                int width = Random.Range(2, 5);
                int halfW = width / 2;
                for (int px = x - halfW; px <= x + halfW; px++)
                {
                    // [关键修复 3] 将生成的平台改为 OneWay
                    // 这样 Validator 跳起来时不会撞到头顶的砖块而掉下去
                    // 只有起点和终点才用实心块 Block
                    map.SetTile(px, footY - 1, TileType.OneWay);
                }
            }
        }

        if (start.x != -1)
        {
            for (int px = start.x - 1; px <= start.x + 1; px++) map.SetTile(px, start.y - 1, TileType.Block);
        }
        if (end.x != -1)
        {
            for (int px = end.x - 1; px <= end.x + 1; px++) map.SetTile(px, end.y - 1, TileType.Block);
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