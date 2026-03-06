using UnityEngine;
using System.IO;
using System;

public partial class LevelGenerator : MonoBehaviour
{
    private string logFilePath => Path.Combine(Application.dataPath, "../LevelGeneratorLog.txt");

    public void InitLog(string modeName, int targetLevels, int maxAttempts)
    {
        File.WriteAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 系统启动：{modeName}\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 目标有效样本数: {targetLevels}，最大容忍尝试次数: {maxAttempts}\n");
        File.AppendAllText(logFilePath, new string('-', 50) + "\n");
    }

    public void LogAttemptResult(int attempt, string status, string details)
    {
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] [尝试 {attempt}] {status} - {details}\n");
    }

    public void LogFinish(int attempts, int validLevelsFound)
    {
        File.AppendAllText(logFilePath, new string('-', 50) + "\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 生成工作全部结束！\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 最终统计 -> 总尝试: {attempts} 次，成功生成: {validLevelsFound} 个关卡。\n");
    }
}