using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public partial class LevelGenerator : MonoBehaviour
{
    [Header("Macro Planning")]
    public WorldBlueprintPlanner blueprintPlanner;

    // 全局生成的最终路径集合
    private List<LevelIndividual> globalWovenPath = new List<LevelIndividual>();

    /// <summary>
    /// 全新管线入口：基于宏观蓝图的关卡生成
    /// </summary>
    public void GenerateWorldFromBlueprint()
    {
        if (blueprintPlanner == null)
        {
            Debug.LogError("未挂载 WorldBlueprintPlanner！");
            return;
        }

        StartCoroutine(WorldWeavingRoutine());
    }

    private IEnumerator WorldWeavingRoutine()
    {
        Initialize();
        ClearVisuals();
        globalWovenPath.Clear();

        // 1. 初始化蓝图并应用测试模板
        blueprintPlanner.InitializeBlueprint();
        blueprintPlanner.Debug_ApplyMetroidvaniaTemplate();

        Debug.Log("<color=cyan>[PathWeaver] 开始基于宏观语义图编织世界路径...</color>");

        // 2. 遍历蓝图中定义的所有节点（这里简化为遍历有连通关系的节点）
        for (int x = 0; x < blueprintPlanner.macroColumns; x++)
        {
            for (int y = 0; y < blueprintPlanner.macroRows; y++)
            {
                BlueprintNode currentNode = blueprintPlanner.blueprintGrid[x, y];

                // 如果该区块是虚空或没有向外的连通关系，跳过寻路
                if (currentNode.envType == EnvironmentType.SolidVoid || currentNode.connectedNeighbors.Count == 0)
                    continue;

                foreach (Vector2i neighborCoord in currentNode.connectedNeighbors)
                {
                    BlueprintNode nextNode = blueprintPlanner.blueprintGrid[neighborCoord.x, neighborCoord.y];

                    // 计算这两个相邻宏观区块的物理接缝点 (入口与出口)
                    Vector2i localStart = CalculatePortTile(currentNode, nextNode, isExit: false);
                    Vector2i localEnd = CalculatePortTile(currentNode, nextNode, isExit: true);

                    Debug.Log($"[PathWeaver] 正在桥接: {currentNode.envType}({x},{y}) -> {nextNode.envType}({neighborCoord.x},{neighborCoord.y})");

                    // 3. 动态覆写局部意图 (Local Intent Override)
                    // 核心逻辑：让生成算法的评价标准临时向当前区块的环境语义妥协
                    TopologyEvaluator.DesignerIntent localIntent = MapEnvironmentToIntent(currentNode);

                    // [重要] 这里复用你之前的 MAP-Elites 生成协程，但我们需要将其改造为接受 localIntent
                    yield return StartCoroutine(GenerateEdgeConnectionRoutine(localStart, localEnd, currentNode, localIntent));
                }
            }
        }

        Debug.Log("<color=green>[PathWeaver] 世界路径骨架编织完成！开始移交区域烘焙器 (Regional Baker)。</color>");

        // 4. 将所有生成的路段合并，并交给下一步的地形生成器
        StitchWovenPaths(globalWovenPath);
    }

    /// <summary>
    /// 将宏观环境语义 (Cave, Shaft 等) 映射为底层算法可读的控制参数
    /// </summary>
    private TopologyEvaluator.DesignerIntent MapEnvironmentToIntent(BlueprintNode node)
    {
        TopologyEvaluator.DesignerIntent intent = new TopologyEvaluator.DesignerIntent();

        // 基础参数继承自你在面板上设置的全局 intent
        intent.riskTension = designerIntent.riskTension;
        intent.mechanicalComplexity = designerIntent.mechanicalComplexity;
        intent.structuralExploration = designerIntent.structuralExploration;

        // 根据区块类型强制进行语义覆写
        switch (node.envType)
        {
            case EnvironmentType.Cave:
                // 洞穴：极高探索性（绕路），高操作复杂度
                intent.structuralExploration = 0.9f;
                intent.mechanicalComplexity = 0.8f;
                intent.riskTension += node.localDangerModifier; // 叠加局部危险度
                break;

            case EnvironmentType.Shaft:
                // 天井：中等探索性（垂直），低操作密度（大跳为主）
                intent.structuralExploration = 0.7f;
                intent.mechanicalComplexity = 0.3f;
                break;

            case EnvironmentType.Corridor:
                // 走廊：极低探索性（直线冲刺），高危险感（速度快容错低）
                intent.structuralExploration = 0.1f;
                intent.riskTension = Mathf.Clamp01(intent.riskTension + 0.2f);
                break;

            case EnvironmentType.SurvivalZone:
                // 安全区：零危险，低复杂度，平坦
                intent.riskTension = 0.0f;
                intent.structuralExploration = 0.2f;
                intent.mechanicalComplexity = 0.2f;
                break;
        }

        return intent;
    }

    /// <summary>
    /// 针对两个宏观节点之间的连线，运行局部的 MAP-Elites 生成
    /// </summary>
    private IEnumerator GenerateEdgeConnectionRoutine(Vector2i startTile, Vector2i endTile, BlueprintNode node, TopologyEvaluator.DesignerIntent intent)
    {
        System.Array.Clear(eliteGrid, 0, eliteGrid.Length);

        // 这里精简了代码逻辑以作演示，实际内部调用你之前写的 MAP-Elites 盲搜和遗传迭代
        // 关键区别在于：EvaluateIndividual 时，使用的是传入的局部 intent

        // ... (此处省略与原 GenerateSegmentedEvolutionaryRoutine 相同的寻路与交叉突变代码) ...
        // 假设我们已经跑完了几代遗传算法， eliteGrid 中填满了候选者

        yield return new WaitForSeconds(0.1f); // 模拟耗时

        // 4. 提取符合该区域气质的最优解
        LevelIndividual bestLocalFit = ExtractBestFitForEnvironment(node);

        if (bestLocalFit != null)
        {
            globalWovenPath.Add(bestLocalFit);
        }
        else
        {
            Debug.LogWarning($"[PathWeaver] 警告：未能生成连接 {startTile} 到 {endTile} 的有效路径。");
        }
    }

    /// <summary>
    /// 根据环境语义，在 10x10 的精英库中定向提取特定拓扑的解
    /// </summary>
    private LevelIndividual ExtractBestFitForEnvironment(BlueprintNode node)
    {
        LevelIndividual best = null;
        float bestScore = -999f;

        for (int x = 0; x < GRID_SIZE; x++)
        {
            for (int y = 0; y < GRID_SIZE; y++)
            {
                LevelIndividual ind = eliteGrid[x, y];
                if (ind == null) continue;

                float score = ind.fitness;

                // 启发式偏好：如果是走廊，我们偏好 X 轴 (Linearity) 极高的解
                if (node.envType == EnvironmentType.Corridor)
                    score += ind.linearity * 50f;

                // 如果是天井，我们偏好 X 轴极低 (高度重叠的垂直运动) 的解
                if (node.envType == EnvironmentType.Shaft)
                    score += (1.0f - ind.linearity) * 50f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = ind;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// 计算两个宏观区块交界处的物理瓦片坐标
    /// </summary>
    private Vector2i CalculatePortTile(BlueprintNode from, BlueprintNode to, bool isExit)
    {
        // 简化的交界点计算逻辑
        int portX = from.startTileX + (from.endTileX - from.startTileX) / 2;
        int portY = from.startTileY + (from.endTileY - from.startTileY) / 2;

        if (to.macroCoord.x > from.macroCoord.x) portX = isExit ? from.endTileX : to.startTileX;
        else if (to.macroCoord.x < from.macroCoord.x) portX = isExit ? from.startTileX : to.endTileX;

        if (to.macroCoord.y > from.macroCoord.y) portY = isExit ? from.endTileY : to.startTileY;
        else if (to.macroCoord.y < from.macroCoord.y) portY = isExit ? from.startTileY : to.endTileY;

        return new Vector2i(portX, portY);
    }

    private void StitchWovenPaths(List<LevelIndividual> paths)
    {
        Debug.Log($"[PathWeaver] 已汇总 {paths.Count} 条子路径，准备物理化...");

        // 调用区域烘焙器！
        // 假设你在 map 中记录了 globalStart 和 globalEnd，传入进去
        BakeWorldFromBlueprint(paths, map.startTile, map.endTile);

        // 生成 IWBTG 动态诱导平台 (针对这些融合后的轨迹)
        if (enableIWBTGBaking)
        {
            // 我们只需选取一条作为“主干诱导轨迹”即可
            if (paths.Count > 0) BakeIWBTGLevel(paths[0]);
        }
    }
}