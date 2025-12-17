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

// 关卡个体：包含地图数据和特征分数
public class LevelIndividual
{
    public List<Vector2i> path;
    public List<ReplayFrame> replay;
    public List<Vector3> trajectory;
    public HashSet<int> safeColumns;

    public float linearity;    // 特征维度 1
    public float inputDensity; // 特征维度 2
    public float fitness;      // 适应度 (比如关卡长度)
}

public class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;

    private Bot ghostAgent;
    private const float SIM_STEP = 0.02f;
    private float currentVirtualFloorY;

    // --- MAP-Elites Settings ---
    private const int GRID_SIZE = 10;
    private LevelIndividual[,] eliteGrid = new LevelIndividual[GRID_SIZE, GRID_SIZE];

    // 临时存储单次生成的数据
    private List<Vector2i> tempPath = new List<Vector2i>();
    private List<ReplayFrame> tempReplay = new List<ReplayFrame>();
    private List<Vector3> tempTrajectory = new List<Vector3>();
    private HashSet<int> tempSafeColumns = new HashSet<int>();

    enum ActionType { MoveRight, JumpRight, LongJumpRight }

    public void Initialize()
    {
        if (ghostAgent == null)
        {
            ghostAgent = Instantiate(characterPrefab, Vector3.zero, Quaternion.identity);
            ghostAgent.gameObject.SetActive(false);
            ghostAgent.name = "GhostAgent";
            ghostAgent.mMap = map;
            ghostAgent.BotInit(new bool[(int)KeyInput.Count], new bool[(int)KeyInput.Count]);
        }
    }

    // --- MAP-Elites 主入口 ---
    // startNode/endNode: 起终点
    // iterations: 演化次数，比如跑 100 次模拟
    public void GenerateMapElitesLibrary(Vector2i startTile, Vector2i endTile, int iterations)
    {
        Initialize();

        // 清空精英库
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);
        int validLevelsFound = 0;

        Debug.Log($">>> MAP-Elites 开始演化 ({iterations} 次迭代)...");

        for (int i = 0; i < iterations; i++)
        {
            // 1. 生成一个随机关卡
            if (RunSimulationAttempt(startTile, endTile))
            {
                // 2. 计算特征
                Vector2 startPos = map.GetMapTilePosition(startTile);
                Vector2 endPos = map.GetMapTilePosition(endTile);

                float lin = LevelMetrics.CalculateLinearity(tempTrajectory, startPos, endPos);
                float den = LevelMetrics.CalculateInputDensity(tempReplay);

                // 3. 计算适应度 (这里简单用路径点数量代表长度，越长越好)
                float fit = tempTrajectory.Count;

                // 4. 映射到网格坐标 (0-9)
                int x = Mathf.Clamp(Mathf.FloorToInt(lin * GRID_SIZE), 0, GRID_SIZE - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt(den * GRID_SIZE), 0, GRID_SIZE - 1);

                // 5. 优胜劣汰：如果该格子是空的，或者新关卡适应度更高，则保留
                if (eliteGrid[x, y] == null || fit > eliteGrid[x, y].fitness)
                {
                    LevelIndividual newInd = new LevelIndividual();
                    newInd.path = new List<Vector2i>(tempPath);
                    newInd.replay = new List<ReplayFrame>(tempReplay);
                    newInd.trajectory = new List<Vector3>(tempTrajectory);
                    newInd.safeColumns = new HashSet<int>(tempSafeColumns);
                    newInd.linearity = lin;
                    newInd.inputDensity = den;
                    newInd.fitness = fit;

                    eliteGrid[x, y] = newInd;
                    validLevelsFound++;
                }
            }
        }

        Debug.Log($">>> 演化结束。发现了 {validLevelsFound} 个独特的关卡变体。");

        // 默认加载一个“最平衡”的关卡 (位于网格中心)
        SelectAndLoadLevel(5, 5);
    }

    // 选择网格中特定风格的关卡加载
    // x: 线性度 (0=曲折, 9=直线)
    // y: 操作密度 (0=简单, 9=繁琐)
    public void SelectAndLoadLevel(int x, int y)
    {
        // 如果选中格子是空的，尝试找最近的邻居
        LevelIndividual target = eliteGrid[x, y];
        if (target == null)
        {
            // 简单遍历找非空
            foreach (var ind in eliteGrid)
            {
                if (ind != null) { target = ind; break; }
            }
        }

        if (target != null)
        {
            Debug.Log($"加载关卡风格 -> Linearity: {target.linearity:F2}, Density: {target.inputDensity:F2}");
            map.ApplyGeneratedPath(target.path, target.replay, target.trajectory, target.safeColumns);
        }
        else
        {
            Debug.LogError("MAP-Elites 库为空，生成失败！");
        }
    }

    // 单次模拟逻辑 (原 GenerateIWBTGLevel 的核心逻辑)
    // 返回 true 表示生成成功到达终点
    bool RunSimulationAttempt(Vector2i startTile, Vector2i endTile)
    {
        // 清理临时数据
        tempPath.Clear();
        tempReplay.Clear();
        tempTrajectory.Clear();
        tempSafeColumns.Clear();

        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        // 重置 Ghost
        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mOnGround = false;

        // 随机化初始虚拟地板，增加多样性
        currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;

        int safetyCounter = 0;
        float lastXProgress = ghostAgent.mPosition.x;
        int stagnationCount = 0;

        // 模拟循环
        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 2000)
        {
            safetyCounter++;
            float heightDiff = endWorldPos.y - currentVirtualFloorY;

            // 增加随机扰动，产生不同风格的关卡
            float noise = Random.Range(-0.2f, 0.2f);
            float bias = Mathf.Clamp(heightDiff / 100.0f + noise, -0.5f, 0.5f);

            if (ghostAgent.mPosition.x - lastXProgress < 1.0f) stagnationCount++;
            else stagnationCount = 0;
            lastXProgress = ghostAgent.mPosition.x;

            ActionType nextAction;
            if (stagnationCount > 3) { nextAction = ActionType.LongJumpRight; stagnationCount = 0; }
            else nextAction = PickAction();

            ExecuteAction(nextAction, bias);

            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }

        // 成功到达终点附近才算有效
        return (ghostAgent.mPosition.x >= endWorldPos.x);
    }

    // --- 原有辅助函数保持不变 (PickAction, ExecuteAction, CheckVirtualFloorCollision, RecordTrajectory) ---
    // 只是把它们向 generatedPath 等变量的写入 改为向 tempPath 等变量写入
    // 为了节省篇幅，这里简写，请务必把原文件里的这些函数复制过来，
    // 并将 generatedPath -> tempPath, generatedReplay -> tempReplay 等替换掉。

    ActionType PickAction()
    {
        float r = Random.value;
        if (r < 0.3f) return ActionType.MoveRight;
        if (r < 0.7f) return ActionType.JumpRight;
        return ActionType.LongJumpRight;
    }

    void ExecuteAction(ActionType action, float heightBias)
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
            float randomChange = Random.Range(-2.0f, 2.5f);
            randomChange += heightBias * 3.0f;
            int tileChange = Mathf.RoundToInt(randomChange);
            float changeAmount = tileChange * Map.cTileSize;
            float newFloor = currentVirtualFloorY + changeAmount;

            float mapBottom = map.position.y + Map.cTileSize * 2;
            float mapTop = map.position.y + (map.mHeight - 5) * Map.cTileSize;
            newFloor = Mathf.Max(mapBottom, Mathf.Min(newFloor, mapTop)); // 边界保护

            currentVirtualFloorY = newFloor;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            inputs[(int)KeyInput.GoRight] = right;
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            RecordTrajectory(); // 写入 tempPath
            tempReplay.Add(new ReplayFrame(inputs));
            tempTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));

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
            tempSafeColumns.Add(landingCol);
            tempSafeColumns.Add(landingCol + 1);
            tempSafeColumns.Add(landingCol - 1);
            return true;
        }
        return false;
    }

    void RecordTrajectory()
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
                    if (!tempPath.Contains(pos)) tempPath.Add(pos);
                }
            }
        }
    }
}