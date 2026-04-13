using UnityEngine;
using System.Collections.Generic;

// 1. 定义空间语义语法 (Spatial Grammar)
public enum EnvironmentType
{
    SolidVoid,       // 虚空/实心墙 (绝对致死，不可通行的填充区)
    SurvivalZone,    // 生存空间 (设计师划定的绝对安全区)
    Cave,            // 洞穴 (高密度、破碎、低容错的狭窄空间)
    Shaft,           // 天井 (强调垂直跳跃，缺乏横向平台)
    Corridor,        // 走廊 (强调水平冲刺，大跨度跳跃)
    Arena            // 竞技场 (开阔空间，通常伴随密集陷阱或首领机制)
}

// 2. 宏观蓝图节点 (Blueprint Node)
[System.Serializable]
public class BlueprintNode
{
    public Vector2i macroCoord;          // 节点在宏观网格 (如 4x4) 中的坐标
    public EnvironmentType envType;      // 当前区块的环境类型

    [Range(0f, 1f)]
    public float localDangerModifier;    // 局部危险度乘区 (例如同样是洞穴，通往终点的安全些，隐藏洞穴极其危险)

    public bool isMainPath;              // 是否属于通关必经的“主干道”
    public bool isExplorationBranch;     // 是否属于“探索支路” (用于引导玩家去角落)

    // 拓扑连通性：记录该区块与周围哪些区块在逻辑上是打通的
    public List<Vector2i> connectedNeighbors = new List<Vector2i>();

    // 物理映射：记录该宏观区块对应地图上真实的瓦片范围 (Bounding Box)
    public int startTileX, startTileY;
    public int endTileX, endTileY;

    public BlueprintNode(int x, int y)
    {
        macroCoord = new Vector2i(x, y);
        envType = EnvironmentType.SolidVoid; // 默认全是致死虚空
        localDangerModifier = 0f;
        isMainPath = false;
        isExplorationBranch = false;
    }
}

// 3. 蓝图规划器核心组件
public class WorldBlueprintPlanner : MonoBehaviour
{
    [Header("Macro Grid Settings (宏观网格划分)")]
    public int macroColumns = 4;  // 横向分为4个大区
    public int macroRows = 4;     // 纵向分为4个大区

    [HideInInspector]
    public BlueprintNode[,] blueprintGrid;

    public Map map; // 引用底层地图

    /// <summary>
    /// 初始化宏观蓝图网格，并计算每个区块对应的真实瓦片坐标
    /// </summary>
    public void InitializeBlueprint()
    {
        if (map == null) return;

        blueprintGrid = new BlueprintNode[macroColumns, macroRows];

        int tilesPerCol = map.mWidth / macroColumns;
        int tilesPerRow = map.mHeight / macroRows;

        for (int x = 0; x < macroColumns; x++)
        {
            for (int y = 0; y < macroRows; y++)
            {
/*                BlueprintNode node = new BlueprintNode(x, y);

                // 映射真实瓦片范围
                node.startTileX = x * tilesPerCol;
                node.endTileX = (x == macroColumns - 1) ? map.mWidth - 1 : (x + 1) * tilesPerCol - 1;

                node.startTileY = y * tilesPerRow;
                node.endTileY = (y == macroRows - 1) ? map.mHeight - 1 : (y + 1) * tilesPerRow - 1;

                blueprintGrid[x, y] = node;*/

                blueprintGrid[x, y].envType = EnvironmentType.SolidVoid;
                blueprintGrid[x, y].connectedNeighbors.Clear();
            }
        }

        Debug.Log($"[WorldBlueprint] 宏观蓝图初始化完成: {macroColumns}x{macroRows} 区域。");
    }

    /// <summary>
    /// 查询真实瓦片坐标属于哪一个宏观语义区块
    /// </summary>
    public BlueprintNode GetNodeAtTile(Vector2i tileCoord)
    {
        if (blueprintGrid == null) return null;

        for (int x = 0; x < macroColumns; x++)
        {
            for (int y = 0; y < macroRows; y++)
            {
                BlueprintNode node = blueprintGrid[x, y];
                if (tileCoord.x >= node.startTileX && tileCoord.x <= node.endTileX &&
                    tileCoord.y >= node.startTileY && tileCoord.y <= node.endTileY)
                {
                    return node;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 测试用：手动构建一个“银河恶魔城”式的环境布局
    /// (后续这个过程将由算法基于设计师的 Survival Space 自动推导)
    /// </summary>
    public void Debug_ApplyMetroidvaniaTemplate()
    {
        // 假设 0,0 是左下角，3,3 是右上角
        // 1. 初始化全为虚空 SolidVoid

        // 2. 设定起跑走廊 (左下)
        blueprintGrid[0, 0].envType = EnvironmentType.Corridor;
        blueprintGrid[1, 0].envType = EnvironmentType.Corridor;

        // 3. 设定主天井 (中部向上)
        blueprintGrid[1, 1].envType = EnvironmentType.Shaft;
        blueprintGrid[1, 2].envType = EnvironmentType.Shaft;

        // 4. 设定终点走廊 (右上)
        blueprintGrid[2, 2].envType = EnvironmentType.Corridor;
        blueprintGrid[3, 2].envType = EnvironmentType.SurvivalZone; // 终点安全区

        // 5. 设定你提到的“左上角高难度洞穴” (支线)
        blueprintGrid[0, 3].envType = EnvironmentType.Cave;
        blueprintGrid[0, 3].localDangerModifier = 1.0f; // 极致危险
        blueprintGrid[0, 3].isExplorationBranch = true;

        // 6. 建立逻辑连通图 (用于指引寻路算法)
        blueprintGrid[0, 0].connectedNeighbors.Add(new Vector2i(1, 0));
        blueprintGrid[1, 0].connectedNeighbors.Add(new Vector2i(1, 1));
        blueprintGrid[1, 1].connectedNeighbors.Add(new Vector2i(1, 2));
        blueprintGrid[1, 2].connectedNeighbors.Add(new Vector2i(2, 2));
        blueprintGrid[2, 2].connectedNeighbors.Add(new Vector2i(3, 2));

        // 支线连通：从主天井(1,2)可以进入左上角的洞穴(0,3)
        blueprintGrid[1, 2].connectedNeighbors.Add(new Vector2i(0, 3));
    }
}