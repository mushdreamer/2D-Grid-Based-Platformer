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

    // --- 新增：记录精确的轨迹坐标点，用于画线 ---
    private List<Vector3> trajectoryPoints = new List<Vector3>();

    private const float SIM_STEP = 0.01666f;
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
        trajectoryPoints.Clear(); // 清空旧轨迹

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
        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 1000) // 稍微增加点安全步数
        {
            safetyCounter++;
            float heightDiff = endWorldPos.y - currentVirtualFloorY;
            float bias = Mathf.Clamp(heightDiff / 100.0f, -0.5f, 0.5f);

            ActionType nextAction = PickAction();
            ExecuteAction(nextAction, bias);

            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }

        // --- 修改：将 trajectoryPoints 也传给 Map ---
        map.ApplyGeneratedPath(generatedPath, generatedReplay, trajectoryPoints);
        Debug.Log($">>> 生成完成! 轨迹点数: {trajectoryPoints.Count}");
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
            CheckVirtualFloorCollision();
            RecordTrajectory();
            generatedReplay.Add(new ReplayFrame(inputs));

            // --- 新增：记录每一帧的精确位置 ---
            // Z轴设为 -8，保证画在所有东西的最前面
            trajectoryPoints.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));
        }
    }

    // CheckVirtualFloorCollision 和 RecordTrajectory 保持不变...
    bool CheckVirtualFloorCollision()
    {
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