using UnityEngine;
using System.Collections.Generic;

public partial class Map : MonoBehaviour
{
    [Header("Heatmap Visuals")]
    public Sprite heatmapSprite;
    public Color safeColor = new Color(0.0f, 1.0f, 0.0f, 0.3f);
    public Color dangerColor = new Color(1.0f, 0.0f, 0.0f, 0.7f);

    private float[,] heatMapWeights;
    private float maxWeight = 1.0f;
    private GameObject[,] heatmapOverlays;
    private bool isHeatmapVisible = true;

    public void GenerateHeatmap(List<Vector2> trapPositions)
    {
        if (heatmapOverlays == null)
        {
            heatmapOverlays = new GameObject[mWidth, mHeight];
        }

        heatMapWeights = new float[mWidth, mHeight];
        maxWeight = 0.1f;

        foreach (Vector2 pos in trapPositions)
        {
            Vector2i tilePos = GetMapTileAtPoint(pos);
            int radius = 2;

            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    int nx = tilePos.x + x;
                    int ny = tilePos.y + y;

                    if (nx >= 0 && nx < mWidth && ny >= 0 && ny < mHeight)
                    {
                        float distance = Mathf.Sqrt(x * x + y * y);
                        float weightGain = 1.0f / (1.0f + distance);
                        heatMapWeights[nx, ny] += weightGain;

                        if (heatMapWeights[nx, ny] > maxWeight)
                        {
                            maxWeight = heatMapWeights[nx, ny];
                        }
                    }
                }
            }
        }

        ApplyHeatmapVisuals();
    }

    private void ApplyHeatmapVisuals()
    {
        if (heatmapSprite == null)
        {
            Debug.LogWarning(">>> 未分配 heatmapSprite，系统已自动生成动态纯色方块作为热力图贴图。");
            Texture2D whiteTex = Texture2D.whiteTexture;
            heatmapSprite = Sprite.Create(whiteTex, new Rect(0.0f, 0.0f, whiteTex.width, whiteTex.height), new Vector2(0.5f, 0.5f));
        }

        for (int x = 0; x < mWidth; x++)
        {
            for (int y = 0; y < mHeight; y++)
            {
                float normalizedWeight = heatMapWeights[x, y] / maxWeight;

                if (normalizedWeight > 0.05f)
                {
                    if (heatmapOverlays[x, y] == null)
                    {
                        GameObject overlay = new GameObject($"HeatmapTile_{x}_{y}");
                        overlay.transform.SetParent(this.transform);
                        Vector2 worldPos = GetMapTilePosition(x, y);
                        overlay.transform.position = new Vector3(worldPos.x, worldPos.y, -4.5f);

                        float scaleFactor = cTileSize / (heatmapSprite.bounds.size.x * heatmapSprite.pixelsPerUnit);
                        overlay.transform.localScale = new Vector3(scaleFactor, scaleFactor, 1f);

                        SpriteRenderer sr = overlay.AddComponent<SpriteRenderer>();
                        sr.sprite = heatmapSprite;
                        sr.sortingOrder = 100;
                        heatmapOverlays[x, y] = overlay;
                    }

                    Color lerpedColor = Color.Lerp(safeColor, dangerColor, normalizedWeight);
                    heatmapOverlays[x, y].GetComponent<SpriteRenderer>().color = lerpedColor;
                    heatmapOverlays[x, y].SetActive(isHeatmapVisible);
                }
            }
        }
    }

    public void ToggleHeatmap()
    {
        isHeatmapVisible = !isHeatmapVisible;
        if (heatmapOverlays != null)
        {
            for (int x = 0; x < mWidth; x++)
            {
                for (int y = 0; y < mHeight; y++)
                {
                    if (heatmapOverlays[x, y] != null)
                    {
                        heatmapOverlays[x, y].SetActive(isHeatmapVisible);
                    }
                }
            }
        }
        Debug.Log(isHeatmapVisible ? ">>> 热力图已显示 (按 H 键隐藏)" : ">>> 热力图已隐藏 (按 H 键显示)");
    }

    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            ToggleHeatmap();
        }
    }
}