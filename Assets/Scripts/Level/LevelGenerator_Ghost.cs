using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public partial class LevelGenerator : MonoBehaviour
{
    public struct GenerationRouteStep
    {
        public Vector2 endPoint;
        public SurvivalSpaceAnalyzer.SurvivalZone associatedZone;
    }

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
        public int stateSequenceCount;
        public Dictionary<Character.CharacterState, int> stateCounts;
        public Dictionary<string, int> stateTransitionCounts;
        public int deathCount;
        public int outsidePlayAreaFrames;
        public int trapContactCount;

        public GhostCheckpoint(
            Bot agent,
            float vFloor,
            int rCount,
            int tCount,
            int pCount,
            int sCount,
            Dictionary<Character.CharacterState, int> stateCountsSnapshot,
            Dictionary<string, int> stateTransitionCountsSnapshot,
            int deathCountSnapshot,
            int outsidePlayAreaFramesSnapshot,
            int trapContactCountSnapshot)
        {
            position = agent.mPosition;
            speed = agent.mSpeed;
            currentState = agent.mCurrentState;
            onGround = agent.mOnGround;
            virtualFloorY = vFloor;
            replayCount = rCount;
            trajectoryCount = tCount;
            pathCount = pCount;
            stateSequenceCount = sCount;
            stateCounts = new Dictionary<Character.CharacterState, int>(stateCountsSnapshot);
            stateTransitionCounts = new Dictionary<string, int>(stateTransitionCountsSnapshot);
            deathCount = deathCountSnapshot;
            outsidePlayAreaFrames = outsidePlayAreaFramesSnapshot;
            trapContactCount = trapContactCountSnapshot;
        }
    }

    bool RunGuidedSimulation(Vector2i startTile, Vector2i endTile, List<GenerationRouteStep> route, out string finalReason, out Vector2 failPos, bool injectBaseline = false, HashSet<Vector2i> localSafeTiles = null, float temperature = 0f)
    {
        int microAttempts = 5;
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
                if (!SimulateGuidedPath(step.endPoint, step.associatedZone, out finalReason, out failPos, temperature))
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

    bool SimulateGuidedPath(Vector2 finalDest, SurvivalSpaceAnalyzer.SurvivalZone currentZone, out string reason, out Vector2 failPos, float temperature)
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

            GhostCheckpoint cp = new GhostCheckpoint(
                ghostAgent,
                currentVirtualFloorY,
                ghostReplay.Count,
                ghostTrajectory.Count,
                ghostPath.Count,
                ghostStateSequence.Count,
                ghostStateCounts,
                ghostStateTransitionCounts,
                ghostDeathCount,
                ghostOutsidePlayAreaFrames,
                ghostTrapContactCount);

            int maxRetries = 12;
            bool stepSuccess = false;
            int framesTaken = 0;

            for (int r = 0; r < maxRetries; r++)
            {
                ActionType nextAction = PickAnnealedAction(ghostAgent.mPosition, finalDest, stagnationCount, currentZone, temperature);

                bool actionFailed = false;
                framesTaken = ExecuteGhostAction(nextAction, out actionFailed);

                if (ghostAgent.mPosition.y < map.position.y - 100f) actionFailed = true;

                if (!actionFailed)
                {
                    CommitGhostActionVisit(nextAction);
                    stepSuccess = true;
                    break;
                }
                else
                {
                    ghostAgent.mPosition = cp.position;
                    ghostAgent.mSpeed = cp.speed;
                    ghostAgent.mCurrentState = cp.currentState;
                    ghostAgent.mOnGround = cp.onGround;
                    currentVirtualFloorY = cp.virtualFloorY;

                    if (ghostReplay.Count > cp.replayCount) ghostReplay.RemoveRange(cp.replayCount, ghostReplay.Count - cp.replayCount);
                    if (ghostTrajectory.Count > cp.trajectoryCount) ghostTrajectory.RemoveRange(cp.trajectoryCount, ghostTrajectory.Count - cp.trajectoryCount);
                    if (ghostPath.Count > cp.pathCount) ghostPath.RemoveRange(cp.pathCount, ghostPath.Count - cp.pathCount);
                    if (ghostStateSequence.Count > cp.stateSequenceCount) ghostStateSequence.RemoveRange(cp.stateSequenceCount, ghostStateSequence.Count - cp.stateSequenceCount);
                    ghostStateCounts = new Dictionary<Character.CharacterState, int>(cp.stateCounts);
                    ghostStateTransitionCounts = new Dictionary<string, int>(cp.stateTransitionCounts);
                    ghostDeathCount = cp.deathCount;
                    ghostOutsidePlayAreaFrames = cp.outsidePlayAreaFrames;
                    ghostTrapContactCount = cp.trapContactCount;
                }
            }

            if (!stepSuccess)
            {
                reason = "All_Retries_Failed_At_Step";
                failPos = ghostAgent.mPosition;

                // 陷入死胡同时，利用温度注入位置微扰以脱困
                if (temperature > 0.2f)
                {
                    ghostAgent.mPosition += new Vector2(Random.Range(-2f, 2f), 0);
                }
                return false;
            }

            currentFrames += framesTaken;

            if (Vector2.Distance(ghostAgent.mPosition, lastProgressPos) < 2.0f) stagnationCount++;
            else { stagnationCount = 0; lastProgressPos = ghostAgent.mPosition; }
        }

        reason = "Timeout_耗尽2500帧陷入死循环";
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
        ghostStateSequence.Clear();
        ghostStateCounts.Clear();
        ghostStateTransitionCounts.Clear();
        ghostDeathCount = 0;
        ghostOutsidePlayAreaFrames = 0;
        ghostTrapContactCount = 0;
        ghostActionHistory.Clear();
        ghostVisitCounts.Clear();
    }

    ActionType PickAnnealedAction(Vector2 currentPos, Vector2 endPos, int stagnationCount, SurvivalSpaceAnalyzer.SurvivalZone zone, float temp)
    {
        ActionType[] candidates = new ActionType[]
        {
            ActionType.MoveRight,
            ActionType.MoveLeft,
            ActionType.JumpRight,
            ActionType.JumpLeft,
            ActionType.LongJumpRight,
            ActionType.LongJumpLeft,
            ActionType.HighJumpRight,
            ActionType.HighJumpLeft,
            ActionType.Drop
        };

        float bestScore = float.NegativeInfinity;
        List<ActionType> bestActions = new List<ActionType>();
        List<float> scores = new List<float>();

        foreach (ActionType action in candidates)
        {
            float score = EvaluateExplorationAction(action, currentPos, endPos, stagnationCount, zone);
            scores.Add(score);

            if (score > bestScore + 0.001f)
            {
                bestScore = score;
                bestActions.Clear();
                bestActions.Add(action);
            }
            else if (Mathf.Abs(score - bestScore) <= 0.001f)
            {
                bestActions.Add(action);
            }
        }

        if (temp > 0.01f)
        {
            float softness = Mathf.Lerp(0.35f, 1.25f, Mathf.Clamp01(temp));
            float totalWeight = 0f;
            float[] weights = new float[scores.Count];
            for (int i = 0; i < scores.Count; i++)
            {
                weights[i] = Mathf.Exp((scores[i] - bestScore) * softness) + Random.Range(0f, 0.08f * temp);
                totalWeight += weights[i];
            }

            float roll = Random.Range(0f, totalWeight);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (roll <= weights[i]) return ApplyMechanicalComplexity(candidates[i]);
                roll -= weights[i];
            }
        }

        return ApplyMechanicalComplexity(bestActions[Random.Range(0, bestActions.Count)]);
    }

    float EvaluateExplorationAction(ActionType action, Vector2 currentPos, Vector2 endPos, int stagnationCount, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        Vector2i currentTile = map.GetMapTileAtPoint(currentPos);
        Vector2i predictedTile = PredictActionTile(currentTile, action);

        int directVisits = 0;
        ghostVisitCounts.TryGetValue(predictedTile, out directVisits);

        float unvisitedReward = directVisits == 0 ? 18f : -4f * directVisits;
        float lowVisitReward = 0f;
        int sampledNeighbors = 0;
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                Vector2i sample = new Vector2i(predictedTile.x + dx, predictedTile.y + dy);
                int visits = 0;
                ghostVisitCounts.TryGetValue(sample, out visits);
                lowVisitReward += Mathf.Clamp(3f - visits, -2f, 3f);
                sampledNeighbors++;
            }
        }
        lowVisitReward = sampledNeighbors > 0 ? lowVisitReward / sampledNeighbors : 0f;

        float coverageReward = ScoreSurvivalCoverageTarget(predictedTile, zone);
        float directionDiversityReward = ScoreDirectionDiversity(action);
        float oscillationPenalty = IsOppositeOfLastAction(action) ? -16f : 0f;
        float boundaryPenalty = IsPredictedInsideSurvival(predictedTile) ? 0f : -35f;
        float regionOverusePenalty = -2.5f * CountRecentNearbyVisits(predictedTile, 3);

        float currentGoalDistance = Vector2.Distance(currentPos, endPos);
        Vector2 predictedWorld = map.GetMapTilePosition(predictedTile.x, predictedTile.y);
        float progressReward = Mathf.Clamp((currentGoalDistance - Vector2.Distance(predictedWorld, endPos)) / Map.cTileSize, -2f, 2f) * 2.5f;

        float stagnationReward = stagnationCount > 3 ? unvisitedReward * 0.75f + directionDiversityReward : 0f;
        float intentExplorationScale = Mathf.Lerp(0.85f, 1.35f, designerIntent.structuralExploration);

        return ((unvisitedReward + lowVisitReward + coverageReward + directionDiversityReward + stagnationReward) * intentExplorationScale)
            + oscillationPenalty
            + boundaryPenalty
            + regionOverusePenalty
            + progressReward;
    }

    Vector2i PredictActionTile(Vector2i currentTile, ActionType action)
    {
        int dx = 0;
        int dy = 0;
        switch (action)
        {
            case ActionType.MoveRight: dx = 2; break;
            case ActionType.MoveLeft: dx = -2; break;
            case ActionType.JumpRight: dx = 3; dy = 2; break;
            case ActionType.JumpLeft: dx = -3; dy = 2; break;
            case ActionType.LongJumpRight: dx = 5; dy = 2; break;
            case ActionType.LongJumpLeft: dx = -5; dy = 2; break;
            case ActionType.HighJumpRight: dx = 2; dy = 4; break;
            case ActionType.HighJumpLeft: dx = -2; dy = 4; break;
            case ActionType.Drop: dy = -3; break;
        }
        return new Vector2i(currentTile.x + dx, currentTile.y + dy);
    }

    bool IsPredictedInsideSurvival(Vector2i tile)
    {
        if (map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0) return true;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (map.survivalSpaceTiles.Contains(new Vector2i(tile.x + dx, tile.y + dy))) return true;
        return false;
    }

    float ScoreSurvivalCoverageTarget(Vector2i tile, SurvivalSpaceAnalyzer.SurvivalZone zone)
    {
        if (zone == null || zone.tiles == null || zone.tiles.Count == 0) return 0f;
        float nearestUnvisited = float.MaxValue;
        foreach (Vector2i survivalTile in zone.tiles)
        {
            if (ghostPathSet.Contains(survivalTile)) continue;
            float d = Mathf.Abs(survivalTile.x - tile.x) + Mathf.Abs(survivalTile.y - tile.y);
            if (d < nearestUnvisited) nearestUnvisited = d;
        }
        if (nearestUnvisited == float.MaxValue) return 0f;
        return Mathf.Clamp(10f - nearestUnvisited, -4f, 10f);
    }

    float ScoreDirectionDiversity(ActionType action)
    {
        int recentWindow = Mathf.Min(6, ghostActionHistory.Count);
        if (recentWindow == 0) return 6f;
        int sameDirectionCount = 0;
        int actionDirection = GetActionDirection(action);
        for (int i = ghostActionHistory.Count - recentWindow; i < ghostActionHistory.Count; i++)
        {
            if (GetActionDirection(ghostActionHistory[i]) == actionDirection) sameDirectionCount++;
        }
        return 8f - sameDirectionCount * 4f;
    }

    bool IsOppositeOfLastAction(ActionType action)
    {
        if (ghostActionHistory.Count == 0) return false;
        int currentDirection = GetActionDirection(action);
        int previousDirection = GetActionDirection(ghostActionHistory[ghostActionHistory.Count - 1]);
        return currentDirection != 0 && previousDirection != 0 && currentDirection == -previousDirection;
    }

    int GetActionDirection(ActionType action)
    {
        switch (action)
        {
            case ActionType.MoveRight:
            case ActionType.JumpRight:
            case ActionType.LongJumpRight:
            case ActionType.HighJumpRight:
                return 1;
            case ActionType.MoveLeft:
            case ActionType.JumpLeft:
            case ActionType.LongJumpLeft:
            case ActionType.HighJumpLeft:
                return -1;
            default:
                return 0;
        }
    }

    int CountRecentNearbyVisits(Vector2i tile, int radius)
    {
        int count = 0;
        foreach (Vector2i visitedTile in ghostPath)
        {
            if (Mathf.Abs(visitedTile.x - tile.x) <= radius && Mathf.Abs(visitedTile.y - tile.y) <= radius) count++;
        }
        return count;
    }

    ActionType ApplyMechanicalComplexity(ActionType pickedAction)
    {
        if (designerIntent.mechanicalComplexity > 0.7f)
        {
            if (pickedAction == ActionType.MoveRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.MoveLeft) pickedAction = ActionType.JumpLeft;
            if (pickedAction == ActionType.JumpRight && Random.value < 0.8f) pickedAction = ActionType.LongJumpRight;
            if (pickedAction == ActionType.JumpLeft && Random.value < 0.8f) pickedAction = ActionType.LongJumpLeft;
        }
        else if (designerIntent.mechanicalComplexity < 0.3f)
        {
            if (pickedAction == ActionType.JumpRight && Random.value < 0.6f) pickedAction = ActionType.MoveRight;
            if (pickedAction == ActionType.JumpLeft && Random.value < 0.6f) pickedAction = ActionType.MoveLeft;
            if (pickedAction == ActionType.LongJumpRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.LongJumpLeft) pickedAction = ActionType.JumpLeft;
            if (pickedAction == ActionType.HighJumpRight) pickedAction = ActionType.JumpRight;
            if (pickedAction == ActionType.HighJumpLeft) pickedAction = ActionType.JumpLeft;
        }
        return pickedAction;
    }

    void CommitGhostActionVisit(ActionType action)
    {
        ghostActionHistory.Add(action);
        Vector2i currentTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
        if (ghostVisitCounts.ContainsKey(currentTile)) ghostVisitCounts[currentTile]++;
        else ghostVisitCounts[currentTile] = 1;
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

            bool outsidePlayArea = false;
            Vector2i currentTile = map.GetMapTileAtPoint(ghostAgent.mPosition);
            if (map.survivalSpaceTiles != null && map.survivalSpaceTiles.Count > 0)
            {
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

                outsidePlayArea = !isInside;
                if (outsidePlayArea)
                {
                    ghostOutsidePlayAreaFrames++;
                    actionFailed = true;
                }
            }

            bool touchedTrap = map.GetTile(currentTile.x, currentTile.y) == TileType.Danger;
            if (touchedTrap) ghostTrapContactCount++;

            if (ghostAgent.mCurrentState == Character.CharacterState.Die)
            {
                ghostDeathCount++;
                actionFailed = true;
            }

            RecordGhostState(ghostAgent.mCurrentState);
            RecordGhostTrajectory();
            ghostReplay.Add(new ReplayFrame(inputs));
            ghostTrajectory.Add(new Vector3(ghostAgent.mPosition.x, ghostAgent.mPosition.y, -8f));

            if (actionFailed) break;
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

    void RecordGhostState(Character.CharacterState state)
    {
        if (ghostStateSequence.Count > 0)
        {
            Character.CharacterState previous = ghostStateSequence[ghostStateSequence.Count - 1];
            if (previous != state)
            {
                string transition = previous + "->" + state;
                if (ghostStateTransitionCounts.ContainsKey(transition)) ghostStateTransitionCounts[transition]++;
                else ghostStateTransitionCounts[transition] = 1;
            }
        }

        ghostStateSequence.Add(state);
        if (ghostStateCounts.ContainsKey(state)) ghostStateCounts[state]++;
        else ghostStateCounts[state] = 1;
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