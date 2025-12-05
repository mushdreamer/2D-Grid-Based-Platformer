using UnityEngine;
using System.Collections.Generic;

// 定义录像帧结构
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
    // --- 新增：录像数据 ---
    public List<ReplayFrame> generatedReplay = new List<ReplayFrame>();

    private const float SIM_STEP = 0.01666f; // 60 FPS 固定步长

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
        generatedReplay.Clear(); // 清空旧录像

        // 1. 初始化位置
        // 注意：这里我们让 Ghost 稍微悬空一点点，利用重力自然落地，以消除初始误差
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, Map.cTileSize * 2);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mAABB.Center = startWorldPos + ghostAgent.mAABBOffset;
        ghostAgent.mOnGround = false;

        // 初始地板对齐网格
        currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize / 2.0f + ghostAgent.mAABB.HalfSizeY;

        Debug.Log(">>> 开始智能生成路径...");

        // 先让 Ghost 自然下落几帧以吸附到地板
        SimulateFallToFloor();

        int safetyCounter = 0;
        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 500)
        {
            safetyCounter++;
            float heightDiff = endWorldPos.y - currentVirtualFloorY;
            float bias = Mathf.Clamp(heightDiff / 100.0f, -0.5f, 0.5f);

            ActionType nextAction = PickAction();
            ExecuteAction(nextAction, bias);

            // 强制拉回逻辑 (同样要对齐网格)
            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }

        // 传递录像数据给 Map
        map.ApplyGeneratedPath(generatedPath, generatedReplay);
        Debug.Log($">>> 生成完成! 录像帧数: {generatedReplay.Count}");
    }

    // 辅助：让 Agent 自然掉落直到碰到 VirtualFloor
    void SimulateFallToFloor()
    {
        for (int i = 0; i < 60; i++) // 最多模拟 60 帧下落
        {
            bool[] inputs = new bool[(int)KeyInput.Count]; // 无输入
            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            // 记录这一帧（哪怕是发呆也要记录，保证时间轴对齐）
            generatedReplay.Add(new ReplayFrame(inputs));

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

        // --- 关键修正：地板高度必须是 Tile 的整数倍 ---
        if (jump)
        {
            float randomChange = Random.Range(-2.0f, 2.5f);
            randomChange += heightBias * 3.0f;

            // 强制 RoundToInt，保证高度变化是整数个格子
            int tileChange = Mathf.RoundToInt(randomChange);
            float changeAmount = tileChange * Map.cTileSize;

            float newFloor = currentVirtualFloorY + changeAmount;

            // 边界限制
            float mapBottom = map.position.y + Map.cTileSize * 2;
            float mapTop = map.position.y + (map.mHeight - 5) * Map.cTileSize;

            // 再次对齐确保万无一失
            newFloor = Mathf.Max(mapBottom, Mathf.Min(newFloor, mapTop));

            // 确保 newFloor 也是网格对齐的
            // (这里假设 map.position.y 也是对齐的，通常是 0)
            currentVirtualFloorY = newFloor;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            inputs[(int)KeyInput.GoRight] = right;
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            CheckVirtualFloorCollision();
            RecordTrajectory();

            // --- 录制当前帧 ---
            generatedReplay.Add(new ReplayFrame(inputs));
        }
    }

    bool CheckVirtualFloorCollision()
    {
        // 只有下落时才检测碰撞
        if (ghostAgent.mSpeed.y <= 0 && ghostAgent.mPosition.y <= currentVirtualFloorY)
        {
            ghostAgent.mPosition.y = currentVirtualFloorY;
            ghostAgent.mSpeed.y = 0;
            ghostAgent.mOnGround = true;
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