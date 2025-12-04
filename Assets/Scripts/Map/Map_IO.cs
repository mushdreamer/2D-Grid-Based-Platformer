using UnityEngine;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;

public partial class Map
{
#if UNITY_EDITOR
    private void HandleEnterKeySave()
    {
        if (pythonScriptsRunning) { Debug.LogWarning("Python 脚本已在运行，请稍候..."); return; }

        string workingDirectory = @"C:\GitHub\sturgeon-pub";
        string levelFileName = "MyDrawnLevel.lvl";
        string fullSavePath = Path.Combine(workingDirectory, levelFileName);

        try
        {
            SaveLevelDirectly(fullSavePath);
            Debug.Log($"关卡已成功保存到: {fullSavePath}");
        }
        catch (Exception e) { Debug.LogError($"直接保存关卡失败: {e.Message}"); return; }

        pythonScriptsRunning = true;
        pythonScriptsFinished = false;
        Debug.Log("关卡已保存。正在后台启动 Python 脚本...");
        new Thread(new ThreadStart(RunPythonScripts)).Start();
    }

    private void SaveLevelDirectly(string path)
    {
        StringBuilder sb = new StringBuilder();
        for (int y = mHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile))
                {
                    sb.Append('R');
                }
                else
                {
                    sb.Append('X');
                }
            }
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void RunPythonScripts()
    {
        string workingDirectory = @"C:\GitHub\sturgeon-pub";
        string executable = "pipenv";
        string args1 = "run python input2tile.py --outfile work/mario.tile --textfile levels/vglc/mario-1-1-generic.lvl";
        string args2 = "run python tile2scheme.py --outfile work/mario.scheme --tilefile work/mario.tile --count-divs 1 1 --pattern ring";
        string args3 = "run python scheme2output.py --outfile work/my-level-output --schemefile work/mario.scheme --size 10 29 --pattern-hard --reach-junction \"{\" l 3 --reach-junction \"}\" r 3 --reach-connect \"--src { --dst } --move platform --sink-bottom --fwdbwd-layers 25\" --reach-print-internal --custom fwdbwd-nostuck hard --custom fwdbwd-grid MyDrawnLevel.lvl soft";

        try
        {
            Debug.Log("开始执行 Python 脚本 (后台线程)...");
            if (!RunProcess(executable, args1, workingDirectory)) { Debug.LogError("步骤 1 (input2tile) 失败。终止执行。"); return; }
            if (!RunProcess(executable, args2, workingDirectory)) { Debug.LogError("步骤 2 (tile2scheme) 失败。终止执行。"); return; }
            if (!RunProcess(executable, args3, workingDirectory)) { Debug.LogError("步骤 3 (scheme2output) 失败。"); return; }
            Debug.Log("所有 Python 脚本执行完毕。");
        }
        catch (Exception e) { Debug.LogError($"Python 脚本执行出错: {e.Message}\n{e.StackTrace}"); }
        finally { pythonScriptsFinished = true; pythonScriptsRunning = false; }
    }

    private bool RunProcess(string executable, string args, string workingDir)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        Debug.Log($"正在执行: {executable} {args} @ {workingDir}");
        using (Process process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) { Debug.Log($"执行成功: {executable} {args}\n输出:\n{output}"); return true; }
            else { Debug.LogError($"执行失败 (ExitCode {process.ExitCode}): {executable} {args}\n错误:\n{error}\n输出:\n{output}"); return false; }
        }
    }

    private void SaveLevelToFile()
    {
        string path = EditorUtility.SaveFilePanel("保存关卡文件", @"C:\GitHub\2D-Grid-Based-Platformer\Level", "NewLevel", "lvl");
        if (string.IsNullOrEmpty(path)) { Debug.Log("保存已取消。"); return; }

        StringBuilder sb = new StringBuilder();
        for (int y = mHeight - 1; y >= 0; y--)
        {
            for (int x = 0; x < mWidth; x++)
            {
                Vector2i currentTile = new Vector2i(x, y);
                if (currentTile == startTile || currentTile == endTile || playerSelectedPath.Contains(currentTile)) sb.Append('R');
                else sb.Append('X');
            }
            sb.AppendLine();
        }

        try { File.WriteAllText(path, sb.ToString()); Debug.Log($"关卡已成功保存到: {path}"); }
        catch (System.Exception e) { Debug.LogError($"保存关卡失败: {e.Message}"); }
    }

    private void LoadLevelFromFile()
    {
        string path = EditorUtility.OpenFilePanel("加载关卡文件", @"C:\GitHub\2D-Grid-Based-Platformer\Level", "lvl");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        string[] lines;
        try { lines = File.ReadAllLines(path); } catch (System.Exception e) { Debug.LogError(e.Message); return; }

        ResetToDrawingMode();
        for (int i = 0; i < lines.Length; i++)
        {
            int mapY = (mHeight - 1) - i;
            if (mapY < 0) break;
            string line = lines[i];
            for (int mapX = 0; mapX < line.Length; mapX++)
            {
                if (mapX >= mWidth) break;
                if (line[mapX] == 'R') playerSelectedPath.Add(new Vector2i(mapX, mapY));
            }
        }

        foreach (Vector2i pathTile in playerSelectedPath)
        {
            if (pathTile.x >= 0 && pathTile.x < mWidth && pathTile.y >= 0 && pathTile.y < mHeight)
            {
                SetVisual(pathTile.x, pathTile.y, new Color(0.5f, 1f, 0.5f, 0.5f));
            }
        }
        Debug.Log($"关卡已成功从 {path} 加载！");
    }
#endif

    private void LoadGeneratedLevel()
    {
        string generatedLevelPath = Path.Combine(@"C:\GitHub\sturgeon-pub", "work", "my-level-output.lvl");
        if (!File.Exists(generatedLevelPath)) { Debug.LogError($"加载失败: 未找到文件 {generatedLevelPath}"); return; }

        string[] lines;
        try { lines = File.ReadAllLines(generatedLevelPath); } catch (System.Exception e) { Debug.LogError(e.Message); return; }

        playerSelectedPath.Clear();
        List<string> levelGridLines = new List<string>();
        foreach (string line in lines)
        {
            if (line.StartsWith("META")) break;
            levelGridLines.Add(line);
        }

        int fileHeight = levelGridLines.Count;
        for (int i = 0; i < fileHeight; i++)
        {
            int mapY = (fileHeight - 1) - i;
            if (mapY < 0 || mapY >= mHeight) continue;
            string line = levelGridLines[i];
            for (int mapX = 0; mapX < line.Length; mapX++)
            {
                if (mapX >= mWidth) break;
                if (line[mapX] == '-') playerSelectedPath.Add(new Vector2i(mapX, mapY));
            }
        }
        Debug.Log($"已成功解析 {playerSelectedPath.Count} 个可通行格子。");
        StartTrialMode();
    }
}