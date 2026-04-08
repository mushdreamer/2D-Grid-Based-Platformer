using UnityEngine;
using System.IO;
using System;

public partial class LevelGenerator : MonoBehaviour
{
    private string logFilePath => Path.Combine(Application.dataPath, "../LevelGeneratorLog.txt");
    private StreamWriter logWriter;

    public void InitLog(string modeName, int targetLevels, int maxAttempts)
    {
        if (logWriter != null)
        {
            logWriter.Close();
        }

        logWriter = new StreamWriter(logFilePath, false);
        logWriter.AutoFlush = true;

        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 系统启动：{modeName}");
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 目标有效样本数: {targetLevels}，最大容忍尝试次数: {maxAttempts}");
        logWriter.WriteLine(new string('-', 50));
    }

    public void LogAttemptResult(int attempt, string status, string details)
    {
        if (logWriter == null) return;
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [尝试 {attempt}] {status} - {details}");
    }

    public void LogPhaseTransition(string phaseName)
    {
        if (logWriter == null) return;
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [阶段转换] >>> 开始执行: {phaseName}");
    }

    public void LogDeepDiagnostic(string module, string details)
    {
        if (logWriter == null) return;
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [诊断 - {module}] {details}");
    }

    public void LogFinish(int attempts, int validLevelsFound)
    {
        if (logWriter == null) return;
        logWriter.WriteLine(new string('-', 50));
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 生成工作全部结束！");
        logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 最终统计 -> 总尝试: {attempts} 次，成功生成: {validLevelsFound} 个关卡。");

        logWriter.Close();
        logWriter = null;
    }

    void OnDestroy()
    {
        if (logWriter != null)
        {
            logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [生命周期] LevelGenerator 实例被销毁，强制关闭日志流。");
            logWriter.Close();
            logWriter = null;
        }
    }
}