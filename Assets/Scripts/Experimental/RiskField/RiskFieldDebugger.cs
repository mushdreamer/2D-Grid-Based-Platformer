using UnityEngine;
using System.Collections;

[RequireComponent(typeof(RiskFieldSolver))]
public class RiskFieldDebugger : MonoBehaviour
{
    private RiskFieldSolver solver;
    public Vector2i testDangerSource = new Vector2i(10, 10);
    public bool showDebugGizmos = true;

    IEnumerator Start()
    {
        solver = GetComponent<RiskFieldSolver>();
        solver.targetMap = FindObjectOfType<Map>();

        yield return new WaitForEndOfFrame();

        solver.InitializeSolver();
        solver.SetDirichletBoundary(testDangerSource, 1.0f);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || solver == null || solver.targetMap == null || !Application.isPlaying) return;

        Vector3 tileSize = new Vector3(Map.cTileSize, Map.cTileSize, 1f);

        for (int x = 0; x < solver.targetMap.mWidth; x += 1)
        {
            for (int y = 0; y < solver.targetMap.mHeight; y += 1)
            {
                Vector2 worldPos = solver.targetMap.GetMapTilePosition(x, y);
                float risk = solver.GetRiskAtContinuousPosition(worldPos);

                if (risk > 0.005f)
                {
                    Color heatColor = Color.Lerp(Color.green, Color.red, risk);
                    heatColor.a = 0.5f;

                    Gizmos.color = heatColor;
                    Gizmos.DrawCube(new Vector3(worldPos.x, worldPos.y, -5f), tileSize * 0.9f);
                }
            }
        }
    }
}