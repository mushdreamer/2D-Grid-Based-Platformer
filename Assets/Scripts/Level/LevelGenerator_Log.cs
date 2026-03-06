using UnityEngine;
using System.IO;
using System;

// 负责将日志写入本地文件的分部类
public partial class LevelGenerator : MonoBehaviour
{
    private string logFilePath => Path.Combine(Application.dataPath, "../LevelGeneratorLog.txt");

    public void InitLog(string modeName, int iterations, int maxAttempts)
    {
        File.WriteAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 系统启动：{modeName}\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 目标有效样本数: {iterations}，安全熔断最大尝试次数: {maxAttempts}\n");
        File.AppendAllText(logFilePath, new string('-', 50) + "\n");
    }

    public void LogSuccess(int validLevelsFound, int iterations, int attempts)
    {
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] [成功入库] 找到有效关卡！当前进度: {validLevelsFound} / {iterations} (已耗费尝试次数: {attempts})\n");
    }

    public void LogStatus(int attempts, int validLevelsFound, int failTimeout, int failFall, int verifyFall, int verifyDie, int verifyTimeout)
    {
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] [状态播报] 当前总尝试次数: {attempts}，已入库: {validLevelsFound}\n");
        File.AppendAllText(logFilePath, $"   -> 最近100次失败原因：鬼魂卡死({failTimeout}) | 鬼魂坠崖({failFall}) | 验证坠落({verifyFall}) | 验证死亡({verifyDie}) | 验证卡墙({verifyTimeout})\n");
    }

    public void LogFinish(int attempts, int validLevelsFound)
    {
        File.AppendAllText(logFilePath, new string('-', 50) + "\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 生成工作全部结束！\n");
        File.AppendAllText(logFilePath, $"[{DateTime.Now:HH:mm:ss}] 最终统计 -> 总尝试: {attempts} 次，成功生成: {validLevelsFound} 个关卡。\n");
    }
}