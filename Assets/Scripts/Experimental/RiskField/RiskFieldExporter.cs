using UnityEngine;
using System.IO;
using System;

[RequireComponent(typeof(RiskFieldSolver))]
public class RiskFieldExporter : MonoBehaviour
{
    public string folderName = "SavedHeatmaps";

    public void ExportRiskMap(float[] riskData, int width, int height, string fileName)
    {
        if (riskData == null || riskData.Length != width * height)
        {
            Debug.LogError("Risk data mismatch or null.");
            return;
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float risk = riskData[y * width + x];
                // 生存率 = 1 - 死亡率
                // 颜色映射：0.0 (绿) -> 1.0 (红)
                Color color = Color.Lerp(Color.green, Color.red, risk);
                texture.SetPixel(x, y, color);
            }
        }

        texture.Apply();
        byte[] bytes = texture.EncodeToPNG();

        string dirPath = Path.Combine(Application.dataPath, folderName);
        if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

        string fullPath = Path.Combine(dirPath, $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(fullPath, bytes);

        Debug.Log($">>> 风险场热力图已导出至: {fullPath}");
        Destroy(texture);
    }
}