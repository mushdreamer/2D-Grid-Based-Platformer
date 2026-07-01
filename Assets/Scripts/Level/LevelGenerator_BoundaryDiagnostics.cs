using UnityEngine;
using System.Collections.Generic;
using System.Text;

public partial class LevelGenerator : MonoBehaviour
{
    private const int BoundaryProbeFrames = 60;
    private const int MaxBoundaryProbeStarts = 24;
    private const int MaxUnsafeSamples = 3;
    private const int BoundaryReplaySampleStride = 8;
    private Bot boundaryProbeAgent;

    private struct BoundaryProbeStart
    {
        public int frameIndex;
        public Vector2 position;
        public Vector2 speed;
        public Character.CharacterState currentState;
        public bool onGround;
        public List<Vector2i> outwardDirections;
    }

    private struct BoundaryProbeDiagnostics
    {
        public int boundaryProbeCount;
        public int outsideReachedCount;
        public int outsideTerminalCount;
        public int outsideReturnedAliveCount;
        public int outsideAliveAfterKCount;
        public int unsafeOutsideCount;
        public List<Vector2> sampleUnsafePositions;
    }

    private void LogBoundaryLethalityDiagnostics(LevelIndividual individual, Vector2i startTile, string label)
    {
        BoundaryProbeDiagnostics diagnostics = EvaluateBoundaryLethality(individual, startTile);
        Debug.Log($"[BoundaryLethality:{label}] boundaryProbeCount={diagnostics.boundaryProbeCount}, " +
            $"outsideReachedCount={diagnostics.outsideReachedCount}, " +
            $"outsideTerminalCount={diagnostics.outsideTerminalCount}, " +
            $"outsideReturnedAliveCount={diagnostics.outsideReturnedAliveCount}, " +
            $"outsideAliveAfterKCount={diagnostics.outsideAliveAfterKCount}, " +
            $"unsafeOutsideCount={diagnostics.unsafeOutsideCount}, " +
            $"sampleUnsafePositions={FormatUnsafePositions(diagnostics.sampleUnsafePositions)}");
    }

    private BoundaryProbeDiagnostics EvaluateBoundaryLethality(LevelIndividual individual, Vector2i startTile)
    {
        BoundaryProbeDiagnostics diagnostics = new BoundaryProbeDiagnostics
        {
            sampleUnsafePositions = new List<Vector2>()
        };

        if (individual == null || individual.replay == null || individual.replay.Count == 0 ||
            map == null || map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0)
        {
            return diagnostics;
        }

        EnsureBoundaryProbeAgent();
        List<BoundaryProbeStart> starts = CollectBoundaryProbeStarts(individual, startTile);

        foreach (BoundaryProbeStart start in starts)
        {
            foreach (Vector2i direction in start.outwardDirections)
            {
                diagnostics.boundaryProbeCount++;
                RunBoundaryProbe(start, direction, ref diagnostics);
            }
        }

        return diagnostics;
    }

    private void EnsureBoundaryProbeAgent()
    {
        if (boundaryProbeAgent != null) return;

        boundaryProbeAgent = Instantiate(characterPrefab, Vector3.zero, Quaternion.identity);
        boundaryProbeAgent.gameObject.SetActive(false);
        boundaryProbeAgent.name = "BoundaryProbeAgent";
        boundaryProbeAgent.mMap = map;
        boundaryProbeAgent.BotInit(new bool[(int)KeyInput.Count], new bool[(int)KeyInput.Count]);
    }

    private List<BoundaryProbeStart> CollectBoundaryProbeStarts(LevelIndividual individual, Vector2i startTile)
    {
        List<BoundaryProbeStart> starts = new List<BoundaryProbeStart>();
        Vector2 startWorldPos = map.GetMapTilePosition(startTile) + new Vector2(0, boundaryProbeAgent.mAABB.HalfSizeY + 1f);

        RestoreBoundaryProbeAgent(startWorldPos, Vector2.zero, Character.CharacterState.Stand, false);

        for (int frameIndex = 0; frameIndex < individual.replay.Count && starts.Count < MaxBoundaryProbeStarts; frameIndex++)
        {
            boundaryProbeAgent.SimulationUpdate(SIM_STEP, individual.replay[frameIndex].inputs);

            if (frameIndex % BoundaryReplaySampleStride != 0) continue;
            if (boundaryProbeAgent.mCurrentState == Character.CharacterState.Die) break;
            if (boundaryProbeAgent.mPosition.y < map.position.y) break;

            Vector2i currentTile = map.GetMapTileAtPoint(boundaryProbeAgent.mPosition);
            if (!IsInsideSurvivalSpaceNeighborhood(currentTile)) continue;

            List<Vector2i> outwardDirections = GetOutwardDirections(currentTile);
            if (outwardDirections.Count == 0) continue;

            starts.Add(new BoundaryProbeStart
            {
                frameIndex = frameIndex,
                position = boundaryProbeAgent.mPosition,
                speed = boundaryProbeAgent.mSpeed,
                currentState = boundaryProbeAgent.mCurrentState,
                onGround = boundaryProbeAgent.mOnGround,
                outwardDirections = outwardDirections
            });
        }

        return starts;
    }

    private void RunBoundaryProbe(BoundaryProbeStart start, Vector2i direction, ref BoundaryProbeDiagnostics diagnostics)
    {
        RestoreBoundaryProbeAgent(start.position, start.speed, start.currentState, start.onGround);

        bool outsideReached = false;
        bool returnedAlive = false;
        bool terminal = false;
        Vector2 lastOutsidePosition = start.position;

        for (int i = 0; i < BoundaryProbeFrames; i++)
        {
            bool[] inputs = CreateOutwardProbeInputs(direction, i);
            boundaryProbeAgent.SimulationUpdate(SIM_STEP, inputs);

            Vector2i currentTile = map.GetMapTileAtPoint(boundaryProbeAgent.mPosition);
            bool inside = IsInsideSurvivalSpaceNeighborhood(currentTile);
            bool trapContact = map.GetTile(currentTile.x, currentTile.y) == TileType.Danger;
            bool fellInvalid = boundaryProbeAgent.mPosition.y < map.position.y;
            bool died = boundaryProbeAgent.mCurrentState == Character.CharacterState.Die;

            if (!inside)
            {
                outsideReached = true;
                lastOutsidePosition = boundaryProbeAgent.mPosition;
            }
            else if (outsideReached && !died && !trapContact && !fellInvalid)
            {
                returnedAlive = true;
                break;
            }

            if (outsideReached && (died || trapContact || fellInvalid))
            {
                terminal = true;
                break;
            }
        }

        if (!outsideReached) return;

        diagnostics.outsideReachedCount++;
        if (terminal)
        {
            diagnostics.outsideTerminalCount++;
        }
        else if (returnedAlive)
        {
            diagnostics.outsideReturnedAliveCount++;
            diagnostics.unsafeOutsideCount++;
            AddUnsafeSample(lastOutsidePosition, diagnostics.sampleUnsafePositions);
        }
        else
        {
            diagnostics.outsideAliveAfterKCount++;
            diagnostics.unsafeOutsideCount++;
            AddUnsafeSample(lastOutsidePosition, diagnostics.sampleUnsafePositions);
        }
    }

    private void RestoreBoundaryProbeAgent(Vector2 position, Vector2 speed, Character.CharacterState state, bool onGround)
    {
        boundaryProbeAgent.mPosition = position;
        boundaryProbeAgent.mSpeed = speed;
        boundaryProbeAgent.mCurrentState = state;
        boundaryProbeAgent.mOnGround = onGround;
        boundaryProbeAgent.mAABB.Center = boundaryProbeAgent.mPosition + boundaryProbeAgent.mAABBOffset;
    }

    private bool[] CreateOutwardProbeInputs(Vector2i direction, int frameIndex)
    {
        bool[] inputs = new bool[(int)KeyInput.Count];
        if (direction.x < 0) inputs[(int)KeyInput.GoLeft] = true;
        if (direction.x > 0) inputs[(int)KeyInput.GoRight] = true;
        if (direction.y > 0 && frameIndex < 12) inputs[(int)KeyInput.Jump] = true;
        if (direction.y < 0) inputs[(int)KeyInput.GoDown] = true;
        return inputs;
    }

    private bool IsInsideSurvivalSpaceNeighborhood(Vector2i tile)
    {
        if (map.survivalSpaceTiles == null || map.survivalSpaceTiles.Count == 0) return true;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (map.survivalSpaceTiles.Contains(new Vector2i(tile.x + dx, tile.y + dy)))
                    return true;
            }
        }

        return false;
    }

    private List<Vector2i> GetOutwardDirections(Vector2i tile)
    {
        List<Vector2i> directions = new List<Vector2i>();
        AddOutwardDirectionIfOutside(tile, new Vector2i(-1, 0), directions);
        AddOutwardDirectionIfOutside(tile, new Vector2i(1, 0), directions);
        AddOutwardDirectionIfOutside(tile, new Vector2i(0, 1), directions);
        AddOutwardDirectionIfOutside(tile, new Vector2i(0, -1), directions);
        return directions;
    }

    private void AddOutwardDirectionIfOutside(Vector2i tile, Vector2i direction, List<Vector2i> directions)
    {
        Vector2i neighbor = new Vector2i(tile.x + direction.x, tile.y + direction.y);
        if (!map.survivalSpaceTiles.Contains(neighbor)) directions.Add(direction);
    }

    private void AddUnsafeSample(Vector2 position, List<Vector2> samples)
    {
        if (samples.Count < MaxUnsafeSamples) samples.Add(position);
    }

    private string FormatUnsafePositions(List<Vector2> positions)
    {
        if (positions == null || positions.Count == 0) return "none";

        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < positions.Count; i++)
        {
            if (i > 0) builder.Append(";");
            builder.Append($"({positions[i].x:F1},{positions[i].y:F1})");
        }
        return builder.ToString();
    }
}
