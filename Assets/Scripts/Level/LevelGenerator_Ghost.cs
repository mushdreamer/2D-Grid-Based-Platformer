using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public partial class LevelGenerator : MonoBehaviour
{
    // [ÐÂÔö] ÇáÁ¿¼¶×´Ì¬¿ìÕÕ£¬ÓÃÓÚ¶¯×÷Ê§°ÜÊ±µÄË²¼ä»Øµµ
    public struct GhostCheckpoint
    {
        public Vector2 position;
        public Vector2 speed;
        public Character.CharacterState currentState;
        public bool onGround;
        public float virtualFloorY;
        public int replayCount;
        public int trajectoryCount;
        public int pathCount;

        public GhostCheckpoint(Bot agent, float vFloor, int rCount, int tCount, int pCount)
        {
            position = agent.mPosition;
            speed = agent.mSpeed;
            currentState = agent.mCurrentState;
            onGround = agent.mOnGround;
            virtualFloorY = vFloor;
            replayCount = rCount;
            trajectoryCount = tCount;
            pathCount = pCount;
        }
    }

    bool RunGuidedSimulation(Vector2i startTile, Vector2i endTile, List<LevelGenerationPlanner.GenerationStep> route, out string finalReason, out Vector2 failPos, bool injectBaseline = false, HashSet<Vector2i> localSafeTiles = null)
    {
        int microAttempts = 5; // ÒýÈë»ØËÝºó£¬ÕûÌåºê¹ÛÖØÊÔ´ÎÊý¿ÉÒÔ´ó·ù½µµÍ
        finalReason = "";
        failPos = Vector2.zero;

        if (route == null || route.Count == 0)
        {
            finalReason = "RouteEmpty_¹æ»®Â·ÏßÎª¿Õ";
            return false;
        }

        for (int i = 0; i < microAttempts; i++)
        {
            ClearGhostData();
            map.ClearMapToEmpty();

            if (injectBaseline && localSafeTiles != null)
            {
                GenerateKinematicBaseline(startTile, endTile, localSafeTiles);
            }
            else if (startTile.x != -1)
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

    void GenerateKinematicBaseline(Vector2i startTile, Vector2i endTile, HashSet<Vector2i> safeTiles)
    {
        int curX = startTile.x;
        int curY = startTile.y - 1;
        int dirX = endTile.x > startTile.x ? 1 : -1;

        map.SetTile(curX, curY, TileType.Block);
        ghostSafePlatforms.Add(new Vector2i(curX, curY));

        int failsafe = 0;
        while (Mathf.Abs(endTile.x - curX) > 2 && failsafe < 100)
        {
            failsafe++;
            int nextX = curX + dirX * Random.Range(2, 5);
            int nextY = curY;
            if (endTile.y > curY) nextY += Random.Range(0, 3);
            else if (endTile.y < curY) nextY -= Random.Range(0, 3);

            if (safeTiles.Contains(new Vector2i(nextX, nextY + 1)))
            {
                curX = nextX;
                curY = nextY;
                map.SetTile(curX, curY, TileType.Block);
                ghostSafePlatforms.Add(new Vector2i(curX, curY));
            }
            else
            {
                curX += dirX * 1;
            }
        }

        map.SetTile(endTile.x, endTile.y - 1, TileType.Block);
        ghostSafePlatforms.Add(new Vector2i(endTile.x, endTile.y - 1));

        for (int dx = -1; dx <= 1; dx++)
        {
            map.SetTile(startTile.x + dx, startTile.y - 1, TileType.Block);
            ghostSafePlatforms.Add(new Vector2i(startTile.x + dx, startTile.y - 1));
            map.SetTile(endTile.x + dx, endTile.y - 1, TileType.Block);
            ghostSafePlatforms.Add(new Vector2i(endTile.x + dx, endTile.y - 1));
        }
    }

    bool SimulateGuidedPath(Vector2 finalDest, SurvivalSpaceAnalyzer.SurvivalZone currentZone, out string reason, out Vector2 failPos)
    {
        int framesLimit = 2500;
        int currentFrames = 0;
        int stagnationCount = 0;
        Vector2 lastProgressPos = ghostAgent.mPosition;

        while (currentFrames < framesLimit)
        {
            if (Vector2.Distance(ghostAgent.mPosition, finalDest) < Map.cTileSize * 3)
            {
                reason = "Success"; failPos = ghostAgent.mPosition; return true;
            }

            // [ºËÐÄ»úÖÆ] ¼ÇÂ¼¶¯×÷Ç°µÄÎïÀí×´Ì¬Óë¹ì¼£Ë÷Òý¿ìÕÕ
            GhostCheckpoint cp = new GhostCheckpoint(ghostAgent, currentVirtualFloorY, ghostReplay.Count, ghostTrajectory.Count, ghostPath.Count);

            int maxRetries = 12; // ÔÊÐíÔÚÍ¬Ò»µã½øÐÐ¶à´ï12´ÎµÄ²»Í¬¶¯×÷ÊÔ´í
            bool stepSuccess = false;
            int framesTaken = 0;

            for (int r = 0; r < maxRetries; r++)
            {
                ActionType nextAction = PickAction(ghostAgent.mPosition, finalDest, stagnationCount, currentZone);

                bool actionFailed = false;
                framesTaken = ExecuteGhostAction(nextAction, out actionFailed);

                // ÑÏ¿ÁµÄËÀÏß¼ì²â
                if (ghostAgent.mPosition.y < map.position.y - 100f) actionFailed = true;

                if (!actionFailed)
                {
                    stepSuccess = true;
                    break;
                }
                else
                {
                    // [»ØËÝ´¥·¢] µ±Ç°¶¯×÷µ¼ÖÂÔ½½ç»ò×¹Âä£¬Ö´ÐÐ O(1) ×´Ì¬»Ø¹ö²¢»»¸ö¶¯×÷ÔÙÊÔ
                    ghostAgent.mPosition = cp.position;
                    ghostAgent.mSpeed = cp.speed;
                    ghostAgent.mCurrentState = cp.currentState;
                    ghostAgent.mOnGround = cp.onGround;
                    currentVirtualFloorY = cp.virtualFloorY;

                    // ½Ø¶ÏÊ§°ÜµÄÂ¼ÏñÓë¹ì¼££¬±£Ö¤¼ÇÂ¼µÄ¾ø¶Ô´¿½àÐÔ
                    if (ghostReplay.Count > cp.replayCount) ghostReplay.RemoveRange(cp.replayCount, ghostReplay.Count - cp.replayCount);
                    if (ghostTrajectory.Count > cp.trajectoryCount) ghostTrajectory.RemoveRange(cp.trajectoryCount, ghostTrajectory.Count - cp.trajectoryCount);
                    if (ghostPath.Count > cp.pathCount) ghostPath.RemoveRange(cp.pathCount, ghostPath.Count - cp.pathCount);
                }
            }

            if (!stepSuccess)
            {
                reason = "All_Retries_Failed_At_Step";
                failPos = ghostAgent.mPosition;
                return false;
            }

            currentFrames += framesTaken;

            if (Vector2.Distance(ghostAgent.mPosition, lastProgressPos) < 2.0f) stagnationCount++;
            else { stagnationCount = 0; lastProgressPos = ghostAgent.mPosition; }
        }

        reason = "Timeout_ºÄ¾¡2500Ö¡ÏÝÈëËÀÑ­»·";
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
        float weightRight = 0f, weightLeft = 0f, weightUp = 0f, weightDown = 0f;

        // --- 1. 基础方向权重 ---
        if (endPos.x > currentPos.x) weightRight += 5f; else weightLeft += 5f;
        if (endPos.y > currentPos.y) weightUp += 5f; else weightDown += 5f;

        // --- [意图注入 1] 结构探索性 (Structural Exploration) ---
        // 探索性越高，幽灵越喜欢“绕远路”和“向上飞”；探索性越低，幽灵越喜欢无脑直线冲锋。
        if (designerIntent.structuralExploration > 0.6f)
        {
            weightUp += 15f * designerIntent.structuralExploration; // 强拉垂直权重
            if (Random.value < 0.3f) // 30%概率反向走，制造回字形结构
            {
                float temp = weightRight; weightRight = weightLeft; weightLeft = temp;
            }
        }
        else if (designerIntent.structuralExploration < 0.4f)
        {
            if (endPos.x > currentPos.x) weightRight += 20f; else weightLeft += 20f;
            if (Mathf.Abs(endPos.y - currentPos.y) < Map.cTileSize * 2) { weightUp = 0; weightDown = 0; } // 压平轨迹
        }

        // --- [意图注入 2] 风险紧张感 (Risk Tension) ---
        if (riskFieldSolver != null)
        {
            float delta = Map.cTileSize;
            float currentRisk = riskFieldSolver.GetRiskAtContinuousPosition(currentPos);
            float riskRight = riskFieldSolver.GetRiskAtContinuousPosition(currentPos + Vector2.right * delta);
            float riskLeft = riskFieldSolver.GetRiskAtContinuousPosition(currentPos + Vector2.left * delta);
            float riskUp = riskFieldSolver.GetRiskAtContinuousPosition(currentPos + Vector2.up * delta);
            float riskDown = riskFieldSolver.GetRiskAtContinuousPosition(currentPos + Vector2.down * delta);

            if (designerIntent.riskTension > 0.7f)
            {
                // [刀尖起舞] 紧张感极高时，幽灵会产生“向死而生”的倾向，主动靠近(但不跨入)致死区
                if (riskRight > 0.4f && riskRight < 0.9f) weightRight += 15f;
                if (riskUp > 0.4f && riskUp < 0.9f) weightUp += 15f;
            }
            else
            {
                // 正常的风险逃逸逻辑...
                Vector2 escapeVector = -new Vector2(riskRight - riskLeft, riskUp - riskDown).normalized;
                if (escapeVector.x > 0) weightRight += escapeVector.x * 50f * currentRisk;
                // ... (保留你原有的逃逸逻辑)
            }

            // 绝对死亡墙依然不可跨越
            if (riskRight > 0.9f) weightRight = 0f;
            if (riskLeft > 0.9f) weightLeft = 0f;
            if (riskUp > 0.9f) weightUp = 0f;
        }

        if (weightRight <= 0 && weightLeft <= 0 && weightUp <= 0 && weightDown <= 0) return ActionType.Drop;

        ActionType pickedAction = ActionType.Drop;
        float totalWeight = weightRight + weightLeft + weightUp + weightDown;
        float r = Random.Range(0, totalWeight);

        // 标准概率抽取
        if (r < weightRight) pickedAction = (Random.value > 0.4f) ? ActionType.MoveRight : ((Random.value > 0.5f) ? ActionType.JumpRight : ActionType.LongJumpRight);
        else
        {
            r -= weightRight;
            if (r < weightLeft) pickedAction = (Random.value > 0.4f) ? ActionType.MoveLeft : ((Random.value > 0.5f) ? ActionType.JumpLeft : ActionType.LongJumpLeft);
            else
            {
                r -= weightLeft;
                if (r < weightUp) pickedAction = (Random.value > 0.5f) ? ActionType.HighJumpRight : ActionType.HighJumpLeft;
                else pickedAction = ActionType.Drop;
            }
        }

        // --- [意图注入 3] 机械复杂度 (Mechanical Complexity) ---
        // 强制升格或降格幽灵的动作操作量
        if (designerIntent.mechanicalComplexity > 0.7f)
        {
            // 强制炫技：把走变成跳，跳变成大跳
            if (pickedAction == ActionType.MoveRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.MoveLeft) pickedAction = ActionType.JumpLeft;
            if (pickedAction == ActionType.JumpRight && Random.value < 0.8f) pickedAction = ActionType.LongJumpRight;
            if (pickedAction == ActionType.JumpLeft && Random.value < 0.8f) pickedAction = ActionType.LongJumpLeft;
        }
        else if (designerIntent.mechanicalComplexity < 0.3f)
        {
            // 降低操作：尽量只保留基本移动
            if (pickedAction == ActionType.JumpRight && Random.value < 0.6f) pickedAction = ActionType.MoveRight;
            if (pickedAction == ActionType.JumpLeft && Random.value < 0.6f) pickedAction = ActionType.MoveLeft;
            if (pickedAction == ActionType.LongJumpRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.HighJumpRight) pickedAction = ActionType.JumpRight;
        }

        return pickedAction;
    }

    int ExecuteGhostAction(ActionType action, out bool actionFailed)
    {
        int frames = 0;
        actionFailed = false;
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

            ghostAgent.SimulationUpdate(SIM_STEP, inputs);

            // [ÐÞ¸Ä] ³¹µ×·ÏÆú¿ÕÆøÇ½ºÍÊ±¹âµ¹Á÷£¬Ô½½çÖ±½ÓÅÐ¶¨¶¯×÷Ê§°Ü
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
                    actionFailed = true;
                }
            }

            if (ghostAgent.mCurrentState == Character.CharacterState.Die)
            {
                actionFailed = true;
            }

            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));

            if (actionFailed) break; // ÈôÖÐÍ¾Ê§°Ü£¬Á¢¿Ì´ò¶Ï¶¯×÷£¬½»»¹¸øÍâ²ã½øÐÐ»ØËÝ
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
            if (!map.survivalSpaceTiles.Contains(new Vector2i(centerTile.x, centerTile.y - 1)) &&
                !map.survivalSpaceTiles.Contains(new Vector2i(centerTile.x, centerTile.y - 2)))
            {
                isBottomEdge = true;
            }
        }

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