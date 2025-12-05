using UnityEngine;
using System.Collections.Generic;

public class LevelGenerator : MonoBehaviour
{
    public Map map;
    public Bot characterPrefab;

    private Bot ghostAgent;
    private List<Vector2i> generatedPath = new List<Vector2i>();
    private const float SIM_STEP = 0.016f;

    private float currentVirtualFloorY;

    // 去掉了 Wait，防止它原地发呆掉坑里
    enum ActionType { MoveRight, JumpRight, LongJumpRight }

    public void Initialize()
    {
        if (ghostAgent == null)
        {
            ghostAgent = Instantiate(characterPrefab, Vector3.zero, Quaternion.identity);
            ghostAgent.gameObject.SetActive(false);
            ghostAgent.name = "GhostAgent";
            ghostAgent.mMap = map;

            bool[] inputs = new bool[(int)KeyInput.Count];
            bool[] prevInputs = new bool[(int)KeyInput.Count];
            ghostAgent.BotInit(inputs, prevInputs);
        }
    }

    public void GenerateIWBTGLevel(Vector2i startTile, Vector2i endTile)
    {
        Initialize();
        generatedPath.Clear();

        // 1. 初始化位置
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, ghostAgent.mAABB.HalfSizeY + 0.1f);
        Vector2 endWorldPos = map.GetMapTilePosition(endTile);

        ghostAgent.mPosition = startWorldPos;
        ghostAgent.mSpeed = Vector2.zero;
        ghostAgent.mCurrentState = Character.CharacterState.Stand;
        ghostAgent.mAABB.Center = startWorldPos + ghostAgent.mAABBOffset;
        ghostAgent.mOnGround = true;

        currentVirtualFloorY = startWorldPos.y;

        Debug.Log(">>> 开始智能生成路径...");
        RecordTrajectory();

        int safetyCounter = 0;
        // 2. 智能循环：只要没到达终点右侧，就一直生成
        while (ghostAgent.mPosition.x < endWorldPos.x && safetyCounter < 500)
        {
            safetyCounter++;

            // 决策：根据当前高度和终点高度的差值，决定地板走势
            // 如果比终点低，就大概率往上跳；如果比终点高，就允许往下跳
            float heightDiff = endWorldPos.y - currentVirtualFloorY;
            float bias = Mathf.Clamp(heightDiff / 100.0f, -0.5f, 0.5f); // 归一化偏差

            ActionType nextAction = PickAction();
            ExecuteAction(nextAction, bias);

            // 防掉落保险：如果掉出地图太远，强制拉回来 (防止生成无底洞)
            if (ghostAgent.mPosition.y < map.position.y)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY + Map.cTileSize * 2;
                ghostAgent.mSpeed.y = 0;
            }
        }

        map.ApplyGeneratedPath(generatedPath);
        Debug.Log($">>> 生成完成! 步数: {safetyCounter}, 轨迹点: {generatedPath.Count}");
    }

    ActionType PickAction()
    {
        float r = Random.value;
        if (r < 0.4f) return ActionType.MoveRight;
        if (r < 0.8f) return ActionType.JumpRight;
        return ActionType.LongJumpRight;
    }

    void ExecuteAction(ActionType action, float heightBias)
    {
        int frames = 0;
        bool jump = false;
        bool right = true; // 永远向右，保证通关

        switch (action)
        {
            case ActionType.MoveRight: frames = 15; break;
            case ActionType.JumpRight: frames = 25; jump = true; break;
            case ActionType.LongJumpRight: frames = 45; jump = true; break;
        }

        // 智能地板生成
        if (jump)
        {
            // 基础随机范围：-2格 到 +2格
            float randomChange = Random.Range(-2.0f, 2.5f);

            // 加上高度偏差引导 (如果终点在上面，heightBias是正的，地板就会倾向于变高)
            randomChange += heightBias * 3.0f;

            float changeAmount = Mathf.Round(randomChange) * Map.cTileSize;
            float newFloor = currentVirtualFloorY + changeAmount;

            // 限制地板不能超出地图上下界
            float mapBottom = map.position.y + Map.cTileSize * 2;
            float mapTop = map.position.y + (map.mHeight - 5) * Map.cTileSize;
            newFloor = Mathf.Clamp(newFloor, mapBottom, mapTop);

            currentVirtualFloorY = newFloor;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            inputs[(int)KeyInput.GoRight] = right;
            if (jump && i < 15) inputs[(int)KeyInput.Jump] = true;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            // 虚拟地板吸附逻辑
            if (ghostAgent.mSpeed.y < 0 && ghostAgent.mPosition.y <= currentVirtualFloorY)
            {
                ghostAgent.mPosition.y = currentVirtualFloorY;
                ghostAgent.mSpeed.y = 0;
                ghostAgent.mOnGround = true;
            }

            RecordTrajectory();
        }
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