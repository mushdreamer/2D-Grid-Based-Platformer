using System.Collections.Generic;
using UnityEngine;

public class LevelGenerator : MonoBehaviour
{
    public LineRenderer pathRenderer; // 用于绘制路径的LineRenderer
    public Map map;                   // 对Map脚本的引用

    private List<Vector2i> pathPoints = new List<Vector2i>(); // 存储路径点的列表
    private Camera mainCamera;

    void Start()
    {
        // 获取必要的组件引用
        if (map == null) map = GetComponent<Map>();
        mainCamera = Camera.main;

        // 初始化LineRenderer
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = 0;
        }
    }

    void Update()
    {
        // 检测鼠标左键点击
        if (Input.GetMouseButtonDown(0))
        {
            // 将屏幕坐标转换为网格坐标 (复用Map.cs的功能!)
            Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2i gridPos = map.GetMapTileAtPoint(mouseWorldPos);

            // 添加新的路径点并更新显示
            AddPathPoint(gridPos);
        }
    }

    void AddPathPoint(Vector2i gridPos)
    {
        // 防止重复添加同一个点
        if (pathPoints.Count > 0 && pathPoints[pathPoints.Count - 1] == gridPos)
        {
            return;
        }

        pathPoints.Add(gridPos);
        Debug.Log("添加路径点: " + gridPos.x + ", " + gridPos.y);

        // 更新LineRenderer来绘制路径 (复用LineRenderer!)
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = pathPoints.Count;
            for (int i = 0; i < pathPoints.Count; i++)
            {
                // 将网格坐标转换为世界坐标进行绘制
                Vector3 worldPos = map.GetMapTilePosition(pathPoints[i]);
                pathRenderer.SetPosition(i, new Vector3(worldPos.x, worldPos.y, -5)); // Z=-5确保它在最前面
            }
        }
    }
}