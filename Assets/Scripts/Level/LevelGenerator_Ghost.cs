using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public partial class LevelGenerator : MonoBehaviour
{
    bool RunGuidedSimulation(Vector2i startTile, Vector2i endTile, List<LevelGenerationPlanner.GenerationStep> route, out string finalReason, out Vector2 failPos)
    {
        int microAttempts = 200;
        finalReason = "";
        failPos = Vector2.zero;

        if (route == null || route.Count == 0)
        {
            finalReason = "RouteEmpty_规划路线为空";
            return false;
        }

        for (int i = 0; i < microAttempts; i++)
        {
            ClearGhostData();
            map.ClearMapToEmpty();

            if (startTile.x != -1)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    map.SetTile(startTile.x + dx, startTile.y - 1, TileType.Block);
                    ghostSafePlatforms.Add(new Vector2i(startTile.x + dx, startTile.y - 1));
                }
            }

            Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, characterPrefab.mAABB.HalfSizeY + 1f);

            ghostAgent.mPosition = startWorldPos;
            ghostAgent.mSpeed = Vector2.zero;
            ghostAgent.mCurrentState = Character.CharacterState.Stand;
            ghostAgent.mOnGround = false;

            currentVirtualFloorY = map.GetMapTilePosition(startTile).y - Map.cTileSize * 5f;

            bool routeSuccess = true;
            foreach (var step in route)
            {
                if (!SimulateGuidedPath(step.endPoint, step.associatedZone, out finalReason, out failPos))
                {
                    routeSuccess = false;
                    break;
                }
            }

            if (routeSuccess) return true;
        }
        return false;
    }

    bool SimulateGuidedPath(Vector2 finalDest, SurvivalSpaceAnalyzer.SurvivalZone currentZone, out string reason, out Vector2 failPos)
    {
        int framesLimit = 1500;
        int currentFrames = 0;
        int stagnationCount = 0;
        Vector2 lastProgressPos = ghostAgent.mPosition;

        while (currentFrames < framesLimit)
        {
            if (Vector2.Distance(ghostAgent.mPosition, finalDest) < Map.cTileSize * 3)
            {
                reason = "Success"; failPos = ghostAgent.mPosition; return true;
            }
            if (ghostAgent.mPosition.y < map.position.y - 100f)
            {
                reason = "FallOut_跌出地图底线"; failPos = ghostAgent.mPosition; return false;
            }

            if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
            {
                Vector2i currentTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
                bool isInside = false;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (map.survivalSpaceTiles.Contains(new Vector2i(currentTile.x + dx, currentTile.y + dy)))
                        {
                            isInside = true;
                            break;
                        }
                    }
                    if (isInside) break;
                }
                if (!isInside)
                {
                    string dir = "未知方向";
                    if (ghostAgent.mSpeed.x > 5f) dir = "向右冲出";
                    else if (ghostAgent.mSpeed.x < -5f) dir = "向左冲出";
                    else if (ghostAgent.mSpeed.y > 5f) dir = "向上飞出";
                    else if (ghostAgent.mSpeed.y < -5f) dir = "向下坠落";

                    reason = $"Out_Of_Bounds_脱离生存空间 ({dir})";
                    failPos = ghostAgent.mPosition;
                    return false;
                }
            }

            if (Vector2.Distance(ghostAgent.mPosition, lastProgressPos) < 2.0f) stagnationCount++;
            else { stagnationCount = 0; lastProgressPos = ghostAgent.mPosition; }

            ActionType nextAction = PickAction(ghostAgent.mPosition, finalDest, stagnationCount, currentZone);
            if (stagnationCount > 8) stagnationCount = 0;

            currentFrames += ExecuteGhostAction(nextAction);
        }

        reason = "Timeout_耗尽1500帧陷入死循环";
        failPos = ghostAgent.mPosition;
        return false;
    }

    void ClearGhostData()
    {
        ghostPath.Clear();
        ghostPathSet.Clear();
        ghostReplay.Clear();
        ghostTrajectory.Clear();
        ghostSafeColumns.Clear();
        ghostSafePlatforms.Clear();
    }

    ActionType PickAction(Vector2 currentPos, Vector2 endPos, int stagnationCount, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        Vector2i curTile = map.GetMapTileAtPoint(currentPos);
        float weightRight = 0f, weightLeft = 0f, weightUp = 0f, weightDown = 0f;
        float safeRightCount = 0f, safeLeftCount = 0f, safeUpCount = 0f, safeDownCount = 0f;

        if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
        {
            int bestRight = int.MaxValue, bestLeft = int.MaxValue, bestUp = int.MaxValue, bestDown = int.MaxValue;
            int currentDist = int.MaxValue;
            int currentStroke = -1;

            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    Vector2i targetTile = new Vector2i(curTile.x + dx, curTile.y + dy);
                    if (survivalGradient.TryGetValue(targetTile, out int d))
                    {
                        if (d < currentDist) currentDist = d;
                        if (map.survivalSpaceStrokeOrder != null && map.survivalSpaceStrokeOrder.TryGetValue(targetTile, out int s) && s > currentStroke) currentStroke = s;
                    }
                }
            }

            int scanRadius = 3;
            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            {
                for (int dy = -scanRadius; dy <= scanRadius; dy++)
                {
                    Vector2i targetTile = new Vector2i(curTile.x + dx, curTile.y + dy);
                    if (map.survivalSpaceTiles.Contains(targetTile))
                    {
                        float baseWeight = 2f;
                        if (map.survivalSpaceStrokeOrder != null && map.survivalSpaceStrokeOrder.TryGetValue(targetTile, out int targetStroke))
                        {
                            if (targetStroke > currentStroke && targetStroke != -1) baseWeight += 50f;
                        }
                        if (dx > 0) { weightRight += baseWeight; safeRightCount++; }
                        if (dx < 0) { weightLeft += baseWeight; safeLeftCount++; }
                        if (dy > 0) { weightUp += baseWeight; safeUpCount++; }
                        if (dy < 0) { weightDown += baseWeight; safeDownCount++; }

                        if (survivalGradient.TryGetValue(targetTile, out int dist))
                        {
                            if (dx > 0 && dist < bestRight) bestRight = dist;
                            if (dx < 0 && dist < bestLeft) bestLeft = dist;
                            if (dy > 0 && dist < bestUp) bestUp = dist;
                            if (dy < 0 && dist < bestDown) bestDown = dist;
                        }
                    }
                }
            }

            if (safeRightCount == 0) weightRight = 0f;
            if (safeLeftCount == 0) weightLeft = 0f;

            if (currentDist == int.MaxValue)
            {
                float minScore = float.MaxValue;
                Vector2i bestRescueTile = curTile;
                foreach (var t in map.survivalSpaceTiles)
                {
                    float physicalDist = Mathf.Abs(t.x - curTile.x) + Mathf.Abs(t.y - curTile.y);
                    if (physicalDist < minScore) { minScore = physicalDist; bestRescueTile = t; }
                }

                if (bestRescueTile.y > curTile.y) return (bestRescueTile.x >= curTile.x) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
                else return (bestRescueTile.x >= curTile.x) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
            }
            else
            {
                if (bestRight < currentDist && safeRightCount > 0) weightRight += 30f;
                if (bestLeft < currentDist && safeLeftCount > 0) weightLeft += 30f;
                if (bestUp < currentDist) weightUp += 30f;
                if (bestDown < currentDist) weightDown += 30f;
            }
        }
        else
        {
            if (endPos.x > currentPos.x) weightRight += 5f; else weightLeft += 5f;
            if (endPos.y > currentPos.y) weightUp += 5f; else weightDown += 5f;
        }

        if (weightRight <= 0 && weightLeft <= 0 && weightUp <= 0 && weightDown <= 0)
        {
            return ActionType.Drop;
        }

        ActionType pickedAction = ActionType.Drop;

        if (stagnationCount > 8)
        {
            if (weightUp >= weightRight && weightUp >= weightLeft) pickedAction = (Random.value > 0.5f) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
            else if (weightRight >= weightLeft) pickedAction = ActionType.LongJumpRight;
            else pickedAction = ActionType.LongJumpLeft;
        }
        else
        {
            float totalWeight = weightRight + weightLeft + weightUp + weightDown;
            float r = Random.Range(0, totalWeight);

            if (r < weightRight) pickedAction = (Random.value > 0.4f) ? ActionType.MoveRight : ((Random.value > 0.5f) ? ActionType.JumpRight : ActionType.LongJumpRight);
            else
            {
                r -= weightRight;
                if (r < weightLeft) pickedAction = (Random.value > 0.4f) ? ActionType.MoveLeft : ((Random.value > 0.5f) ? ActionType.JumpLeft : ActionType.LongJumpLeft);
                else
                {
                    r -= weightLeft;
                    if (r < weightUp)
                    {
                        float upR = Random.value;
                        if (upR < 0.33f) pickedAction = (Random.value > 0.5f) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
                        else if (upR < 0.66f) pickedAction = (Random.value > 0.5f) ? ActionType.LongJumpRight : ActionType.LongJumpLeft;
                        else pickedAction = (Random.value > 0.5f) ? ActionType.JumpRight : ActionType.JumpLeft;
                    }
                    else pickedAction = ActionType.Drop;
                }
            }
        }

        SurvivalSpaceAnalyzer.ZoneGeometry geometry = zone != null ? zone.geometryType : SurvivalSpaceAnalyzer.ZoneGeometry.OrganicShape;

        if (geometry == SurvivalSpaceAnalyzer.ZoneGeometry.HorizontalCorridor)
        {
            if (pickedAction == ActionType.HighJumpRight) pickedAction = ActionType.LongJumpRight;
            if (pickedAction == ActionType.HighJumpLeft) pickedAction = ActionType.LongJumpLeft;
            if (pickedAction == ActionType.JumpRight) pickedAction = ActionType.MoveRight;
            if (pickedAction == ActionType.JumpLeft) pickedAction = ActionType.MoveLeft;
        }
        else if (geometry == SurvivalSpaceAnalyzer.ZoneGeometry.VerticalShaft)
        {
            if (pickedAction == ActionType.LongJumpRight) pickedAction = ActionType.HighJumpRight;
            if (pickedAction == ActionType.LongJumpLeft) pickedAction = ActionType.HighJumpLeft;
            if (pickedAction == ActionType.MoveRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.MoveLeft) pickedAction = ActionType.JumpLeft;
        }

        return pickedAction;
    }

    int ExecuteGhostAction(ActionType action)
    {
        int frames = 0;
        bool right = false, left = false, jump = false, drop = false;
        int jumpHoldFrames = 0;

        switch (action)
        {
            case ActionType.MoveRight: frames = 15; right = true; break;
            case ActionType.MoveLeft: frames = 15; left = true; break;
            case ActionType.JumpRight: frames = 25; right = true; jump = true; jumpHoldFrames = 10; break;
            case ActionType.JumpLeft: frames = 25; left = true; jump = true; jumpHoldFrames = 10; break;
            case ActionType.LongJumpRight: frames = 40; right = true; jump = true; jumpHoldFrames = 15; break;
            case ActionType.LongJumpLeft: frames = 40; left = true; jump = true; jumpHoldFrames = 15; break;
            case ActionType.HighJumpRight: frames = 45; right = true; jump = true; jumpHoldFrames = 20; break;
            case ActionType.HighJumpLeft: frames = 45; left = true; jump = true; jumpHoldFrames = 20; break;
            case ActionType.Drop: frames = 20; drop = true; break;
        }

        for (int i = 0; i < frames; i++)
        {
            bool[] inputs = new bool[(int)KeyInput.Count];
            if (!drop) { inputs[(int)KeyInput.GoRight] = right; inputs[(int)KeyInput.GoLeft] = left; }
            if (jump && i < jumpHoldFrames) inputs[(int)KeyInput.Jump] = true;

            EnsureVirtualFloorRealtime();

            // 记录进入物理更新前的合法状态，用于绝对防穿透钳制
            Vector2 oldPos = ghostAgent.mPosition;

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            // 绝对物理空气墙：一旦物理引擎把特工推出安全区，立刻执行时光倒流
            if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
            {
                Vector2i currentTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
                bool isInside = false;

                // 给一个 3x3 的宽容判定区，防止踩在平台边缘被误判
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (map.survivalSpaceTiles.Contains(new Vector2i(currentTile.x + dx, currentTile.y + dy)))
                        {
                            isInside = true;
                            break;
                        }
                    }
                    if (isInside) break;
                }

                if (!isInside)
                {
                    // 强行把特工拉回越界前的位置，形成不可穿透的屏障
                    ghostAgent.mPosition = oldPos;
                    Vector2i oldTile = map.GetMapTileAtPoint(oldPos);

                    // 精准消除越界方向的动能，同时保留合法的滑动动能
                    if (ghostAgent.mSpeed.x > 0 && currentTile.x > oldTile.x) ghostAgent.mSpeed.x = 0f;
                    if (ghostAgent.mSpeed.x < 0 && currentTile.x < oldTile.x) ghostAgent.mSpeed.x = 0f;

                    // 向上跳出界没收垂直动能，变成撞天花板直接下落
                    if (ghostAgent.mSpeed.y > 0 && currentTile.y > oldTile.y) ghostAgent.mSpeed.y = 0f;
                }
            }

            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));
        }
        return frames;
    }

    void EnsureVirtualFloorRealtime()
    {
        if (ghostAgent.mSpeed.y > 0.1f) return;

        Vector2i centerTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
        bool isBottomEdge = false;

        if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
        {
            // 探地雷达：检测脚底下两格内是否存在安全区，如果没有，说明特工正踩在安全区的绝对底线上
            if (!map.survivalSpaceTiles.Contains(new Vector2i(centerTile.x, centerTile.y - 1)) &&
                !map.survivalSpaceTiles.Contains(new Vector2i(centerTile.x, centerTile.y - 2)))
            {
                isBottomEdge = true;
            }
        }

        // 动态收网：一旦到达底线边缘，不等掉下去就强行铺路
        if (isBottomEdge || ghostAgent.mPosition.y <= currentVirtualFloorY)
        {
            float feetY = ghostAgent.mPosition.y - ghostAgent.mAABB.HalfSizeY;
            Vector2i feetTile = map.GetMapTileAtPoint(new Vector2(ghostAgent.mPosition.x, feetY));

            int blockY = feetTile.y - 1;
            float targetFeetY = map.GetMapTilePosition(feetTile.x, blockY).y + (Map.cTileSize / 2.0f);

            float leftEdge = ghostAgent.mPosition.x - ghostAgent.mAABB.HalfSizeX - Map.cTileSize * 1.5f;
            float rightEdge = ghostAgent.mPosition.x + ghostAgent.mAABB.HalfSizeX + Map.cTileSize * 1.5f;

            int minX = map.GetMapTileAtPoint(new Vector2(leftEdge, ghostAgent.mPosition.y)).x;
            int maxX = map.GetMapTileAtPoint(new Vector2(rightEdge, ghostAgent.mPosition.y)).x;

            for (int bx = minX; bx <= maxX; bx++)
            {
                if (bx >= 0 && bx < map.mWidth && blockY >= 0 && blockY < map.mHeight)
                {
                    map.SetTile(bx, blockY, TileType.Block);
                    ghostSafePlatforms.Add(new Vector2i(bx, blockY));
                    ghostSafeColumns.Add(bx);
                }
            }

            if (map.survivalSpaceTiles != null) currentVirtualFloorY = targetFeetY - Map.cTileSize * 5f;
        }
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