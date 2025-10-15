using System.Collections.Generic;
using UnityEngine;

public class LevelGenerator : MonoBehaviour
{
    public LineRenderer pathRenderer; // ���ڻ���·����LineRenderer
    public Map map;                   // ��Map�ű�������

    private List<Vector2i> pathPoints = new List<Vector2i>(); // �洢·������б�
    private Camera mainCamera;

    void Start()
    {
        // ��ȡ��Ҫ���������
        if (map == null) map = GetComponent<Map>();
        mainCamera = Camera.main;

        // ��ʼ��LineRenderer
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = 0;
        }
    }

    void Update()
    {
        // ������������
        if (Input.GetMouseButtonDown(0))
        {
            // ����Ļ����ת��Ϊ�������� (����Map.cs�Ĺ���!)
            Vector2 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2i gridPos = map.GetMapTileAtPoint(mouseWorldPos);

            // ����µ�·���㲢������ʾ
            AddPathPoint(gridPos);
        }
    }

    void AddPathPoint(Vector2i gridPos)
    {
        // ��ֹ�ظ����ͬһ����
        if (pathPoints.Count > 0 && pathPoints[pathPoints.Count - 1] == gridPos)
        {
            return;
        }

        pathPoints.Add(gridPos);
        Debug.Log("���·����: " + gridPos.x + ", " + gridPos.y);

        // ����LineRenderer������·�� (����LineRenderer!)
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = pathPoints.Count;
            for (int i = 0; i < pathPoints.Count; i++)
            {
                // ����������ת��Ϊ����������л���
                Vector3 worldPos = map.GetMapTilePosition(pathPoints[i]);
                pathRenderer.SetPosition(i, new Vector3(worldPos.x, worldPos.y, -5)); // Z=-5ȷ��������ǰ��
            }
        }
    }
}