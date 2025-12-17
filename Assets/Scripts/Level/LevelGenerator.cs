using UnityEngine;
using System.Collections.Generic;

// 保持 ReplayFrame 结构体不变
public struct ReplayFrame
{
    public bool[] inputs;
    public ReplayFrame(bool[] src)
    {
        inputs = new bool[src.Length];
        System.Array.Copy(src, inputs, src.Length);
    }
}

public class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;

    private Bot ghostAgent;
    private List<Vector2i> generatedPath = new List<Vector2i>();
    public List<ReplayFrame> generatedReplay = new List<ReplayFrame>();

    // 记录精确的轨迹坐标点
    private List<Vector3> trajectoryPoints = new List<Vector3>();

    // --- 修改 1: 记录安全落地列 (防止在落地点生成尖刺) ---
    private HashSet<int> safeLandingColumns = new HashSet<int>();

    // --- 修改 2: 物理步长锁定为 0.02f (Unity默认FixedUpdate频率) ---
    // 必须与 Project Settings -> Time -> Fixed Timestep 保持一致
    private const float SIM_STEP = 0.02f;

    private float currentVirtualFloorY;

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

    public void GenerateIWBTGLevel(Vector2i startTile, Vector2i endTile)
    {
        Initialize();
        generatedPath.Clear();
        generatedReplay.Clear();
        trajectoryPoints.Clear();
        safeLandingColumns.Clear(); // 清空安全列记录

        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mAABB.Center = startWorldPos + ghostAgent.mAABBOffset;
        ghostAgent.mOnGround = false;

        currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;

        SimulateFallToFloor();

        int safetyCounter = 0;

        // --- 修改 3: 增加进度检测，防止无限垂直堆叠 ---
        float lastXProgress = ghostAgent.mPosition.x;
        int stagnationCount = 0;

        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 2000)
        {
            safetyCounter++;
            float heightDiff = endWorldPos.y - currentVirtualFloorY;
            float bias = Mathf.Clamp(heightDiff / 100.0f, -0.5f, 0.5f);

            // 检测是否卡住不前
            if (ghostAgent.mPosition.x - lastXProgress < 1.0f)
                stagnationCount++;
            else
                stagnationCount = 0;

            lastXProgress = ghostAgent.mPosition.x;

            ActionType nextAction;

            // 如果卡住超过 3 次，强制大跳以打破循环
            if (stagnationCount > 3)
            {
                nextAction = ActionType.LongJumpRight;
                stagnationCount = 0; // 重置计数
            }
            else
            {
                nextAction = PickAction();
            }

            ExecuteAction(nextAction, bias);

            // 掉落保护：如果掉出地图下界，强制拉回当前虚拟地板上方
            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }

        // --- 修改 4: 将 safeLandingColumns 传递给 Map ---
        map.ApplyGeneratedPath(generatedPath, generatedReplay, trajectoryPoints, safeLandingColumns);
        Debug.Log($">>> 生成完成! 轨迹点数: {trajectoryPoints.Count}, 步数: {safetyCounter}");
    }

    void SimulateFallToFloor()
    {
        for (int i = 0; i < 60; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            ghostAgent.SimulationUpdate(SIM_STEP, inputs);
            generatedReplay.Add(new ReplayFrame(inputs));

            // 记录点
            trajectoryPoints.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -1f));

            if (CheckVirtualFloorCollision()) break;
        }
    }

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
            newFloor = Mathf.Max(mapBottom, Mathf.Min(newFloor, mapTop));

            currentVirtualFloorY = newFloor;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            inputs[(int)KeyInput.GoRight] = right;
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            // 每次物理更新后都要记录轨迹
            RecordTrajectory();
            generatedReplay.Add(new ReplayFrame(inputs));

            trajectoryPoints.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));

            // 如果这一帧撞地了，就不需要继续模拟剩下的帧了（特别是跳跃落地后）
            if (CheckVirtualFloorCollision())
            {
                // 可选：落地后可以额外增加几帧滑行，这里暂时直接截断
                // break; 
            }
        }
    }

    bool CheckVirtualFloorCollision()
    {
        // 简单的落地检测：速度向下 且 位置低于虚拟地板
        if (ghostAgent.mSpeed.y <= 0 && ghostAgent.mPosition.y <= currentVirtualFloorY)
        {
            ghostAgent.mPosition.y = currentVirtualFloorY;
            ghostAgent.mSpeed.y = 0;
            ghostAgent.mOnGround = true;

            // --- 修改 5: 记录落地的列坐标 ---
            int landingCol = Mathf.RoundToInt((ghostAgent.mPosition.x - map.position.x) / Map.cTileSize);
            safeLandingColumns.Add(landingCol);
            safeLandingColumns.Add(landingCol + 1); // 稍微放宽一点范围
            safeLandingColumns.Add(landingCol - 1);
            // -----------------------------

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
                    if (!generatedPath.Contains(pos)) generatedPath.Add(pos);
                }
            }
        }
    }
}