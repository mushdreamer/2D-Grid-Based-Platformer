using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

public partial class LevelGenerator : MonoBehaviour
{
    private struct ExperimentCondition
    {
        public string name;
        public bool stateEnumerationFitness;
        public bool boundaryDiagnostics;
        public bool boundarySafetyPenalty;
        public bool boundaryTerminalization;
    }

    private class ExperimentRecord
    {
        public string timestamp;
        public string conditionName;
        public int seed;
        public bool success;
        public string failureReason;
        public float generationTimeSeconds;
        public float totalFitness;
        public bool goalReached;
        public int deaths;
        public int trapContacts;
        public int outsidePlayAreaFrames;
        public float stateCoverageScore;
        public float transitionDiversityScore;
        public string detectedUsefulStates;
        public string detectedUsefulTransitions;
        public int boundaryProbeCount;
        public int outsideReachedCount;
        public int outsideTerminalCount;
        public int outsideAliveAfterKCount;
        public int unsafeOutsideCount;
        public float boundarySafetyScore;
        public int trajectoryLength;
        public int uniqueStateCount;
        public int replayLength;
    }

    private LevelIndividual lastGenerationBestIndividual;
    private bool lastGenerationSucceeded;
    private string lastGenerationFailureReason = "";
    private bool ablationExperimentRunning;

    [ContextMenu("Run Ablation Experiment")]
    public void RunAblationExperimentFromInspector()
    {
        RunAblationExperiment(ablationRunsPerCondition);
    }

    public void RunAblationExperiment(int runsPerCondition)
    {
        if (ablationExperimentRunning)
        {
            Debug.LogWarning("[AblationExperiment] Experiment is already running.");
            return;
        }

        StartCoroutine(RunAblationExperimentRoutine(Mathf.Max(1, runsPerCondition)));
    }

    private IEnumerator RunAblationExperimentRoutine(int runsPerCondition)
    {
        ablationExperimentRunning = true;

        bool originalStateEnumerationFitness = enableStateEnumerationFitness;
        bool originalBoundaryDiagnostics = enableBoundaryDiagnostics;
        bool originalBoundarySafetyPenalty = enableBoundarySafetyPenalty;
        bool originalBoundaryTerminalization = enableBoundaryTerminalization;
        bool originalExperimentLogging = enableExperimentLogging;

        List<ExperimentRecord> records = new List<ExperimentRecord>();
        ExperimentCondition[] conditions = GetAblationConditions();
        int baseSeed = DateTime.Now.GetHashCode();

        Debug.Log($"[AblationExperiment] Starting {conditions.Length} conditions x {runsPerCondition} runs.");

        for (int c = 0; c < conditions.Length; c++)
        {
            ApplyExperimentCondition(conditions[c]);

            for (int run = 0; run < runsPerCondition; run++)
            {
                int seed = baseSeed + (c * 10000) + run;
                UnityEngine.Random.InitState(seed);

                float startTime = Time.realtimeSinceStartup;
                Vector2i startTile = map.startTile.x == -1 ? new Vector2i(2, 5) : map.startTile;
                Vector2i endTile = map.endTile.x == -1 ? new Vector2i(map.mWidth - 5, 5) : map.endTile;

                if (!map.IsInitializedForTileEditing)
                {
                    Debug.LogError("[AblationExperiment] Map is not initialized for tile editing. Start the experiment after Map.Start has created tile storage and sprites.");
                    RestoreExperimentFlags(
                        originalStateEnumerationFitness,
                        originalBoundaryDiagnostics,
                        originalBoundarySafetyPenalty,
                        originalBoundaryTerminalization,
                        originalExperimentLogging);
                    ablationExperimentRunning = false;
                    yield break;
                }

                map.ClearMapToEmpty();
                yield return StartCoroutine(GenerateSegmentedEvolutionaryRoutine(startTile, endTile));

                float elapsed = Time.realtimeSinceStartup - startTime;
                records.Add(CreateExperimentRecord(conditions[c].name, seed, elapsed));
                yield return null;
            }
        }

        RestoreExperimentFlags(
            originalStateEnumerationFitness,
            originalBoundaryDiagnostics,
            originalBoundarySafetyPenalty,
            originalBoundaryTerminalization,
            originalExperimentLogging);

        string csvPath = ExportExperimentCsv(records);
        PrintAblationSummary(records, csvPath);

        ablationExperimentRunning = false;
    }

    private ExperimentCondition[] GetAblationConditions()
    {
        return new ExperimentCondition[]
        {
            new ExperimentCondition
            {
                name = "Baseline",
                stateEnumerationFitness = false,
                boundaryDiagnostics = false,
                boundarySafetyPenalty = false,
                boundaryTerminalization = false
            },
            new ExperimentCondition
            {
                name = "StateEnumerationOnly",
                stateEnumerationFitness = true,
                boundaryDiagnostics = false,
                boundarySafetyPenalty = false,
                boundaryTerminalization = false
            },
            new ExperimentCondition
            {
                name = "BoundaryPenaltyOnly",
                stateEnumerationFitness = false,
                boundaryDiagnostics = true,
                boundarySafetyPenalty = true,
                boundaryTerminalization = false
            },
            new ExperimentCondition
            {
                name = "BoundaryTerminalizationOnly",
                stateEnumerationFitness = false,
                boundaryDiagnostics = true,
                boundarySafetyPenalty = false,
                boundaryTerminalization = true
            },
            new ExperimentCondition
            {
                name = "FullSystem",
                stateEnumerationFitness = true,
                boundaryDiagnostics = true,
                boundarySafetyPenalty = true,
                boundaryTerminalization = true
            }
        };
    }

    private void ApplyExperimentCondition(ExperimentCondition condition)
    {
        enableStateEnumerationFitness = condition.stateEnumerationFitness;
        enableBoundaryDiagnostics = condition.boundaryDiagnostics;
        enableBoundarySafetyPenalty = condition.boundarySafetyPenalty;
        enableBoundaryTerminalization = condition.boundaryTerminalization;
        enableExperimentLogging = true;
    }

    private void RestoreExperimentFlags(
        bool stateEnumerationFitness,
        bool boundaryDiagnostics,
        bool boundarySafetyPenalty,
        bool boundaryTerminalization,
        bool experimentLogging)
    {
        enableStateEnumerationFitness = stateEnumerationFitness;
        enableBoundaryDiagnostics = boundaryDiagnostics;
        enableBoundarySafetyPenalty = boundarySafetyPenalty;
        enableBoundaryTerminalization = boundaryTerminalization;
        enableExperimentLogging = experimentLogging;
    }

    private ExperimentRecord CreateExperimentRecord(string conditionName, int seed, float generationTimeSeconds)
    {
        LevelIndividual individual = lastGenerationBestIndividual;
        StateEnumerationEvaluator.EvaluationResult evaluation = individual != null
            ? EvaluateIndividualWithExperimentFlags(individual)
            : new StateEnumerationEvaluator.EvaluationResult();

        return new ExperimentRecord
        {
            timestamp = DateTime.Now.ToString("o"),
            conditionName = conditionName,
            seed = seed,
            success = lastGenerationSucceeded && individual != null,
            failureReason = lastGenerationSucceeded ? "" : lastGenerationFailureReason,
            generationTimeSeconds = generationTimeSeconds,
            totalFitness = individual != null ? evaluation.totalFitness : 0f,
            goalReached = individual != null && individual.goalReached,
            deaths = individual != null ? individual.deathCount : 0,
            trapContacts = individual != null ? individual.trapContactCount : 0,
            outsidePlayAreaFrames = individual != null ? individual.outsidePlayAreaFrames : 0,
            stateCoverageScore = individual != null ? evaluation.stateCoverageScore : 0f,
            transitionDiversityScore = individual != null ? evaluation.transitionDiversityScore : 0f,
            detectedUsefulStates = individual != null ? StateEnumerationEvaluator.GetDetectedUsefulStates(individual) : "none",
            detectedUsefulTransitions = individual != null ? StateEnumerationEvaluator.GetDetectedUsefulTransitions(individual) : "none",
            boundaryProbeCount = individual != null ? individual.boundaryProbeCount : 0,
            outsideReachedCount = individual != null ? individual.outsideReachedCount : 0,
            outsideTerminalCount = individual != null ? individual.outsideTerminalCount : 0,
            outsideAliveAfterKCount = individual != null ? individual.outsideAliveAfterKCount : 0,
            unsafeOutsideCount = individual != null ? individual.unsafeOutsideCount : 0,
            boundarySafetyScore = individual != null ? evaluation.boundarySafetyScore : 0f,
            trajectoryLength = individual != null && individual.trajectory != null ? individual.trajectory.Count : 0,
            uniqueStateCount = individual != null && individual.stateCounts != null ? individual.stateCounts.Count : 0,
            replayLength = individual != null && individual.replay != null ? individual.replay.Count : 0
        };
    }

    private StateEnumerationEvaluator.EvaluationResult EvaluateIndividualWithExperimentFlags(LevelIndividual individual)
    {
        StateEnumerationEvaluator.EvaluationResult result = StateEnumerationEvaluator.EvaluateIndividual(individual);

        if (!enableStateEnumerationFitness)
            result.totalFitness -= result.stateCoverageScore + result.transitionDiversityScore;

        if (!enableBoundarySafetyPenalty)
            result.totalFitness -= result.boundarySafetyScore;

        return result;
    }

    private string GetMostCommonFailureReason()
    {
        if (failureStatistics == null || failureStatistics.Count == 0)
            return "Unknown";

        return failureStatistics.OrderByDescending(kvp => kvp.Value).First().Key;
    }

    private string ExportExperimentCsv(List<ExperimentRecord> records)
    {
        string fileName = $"AblationExperiment_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(Application.persistentDataPath, fileName);

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("timestamp,conditionName,seed,success,failureReason,generationTimeSeconds,totalFitness,goalReached,deaths,trapContacts,outsidePlayAreaFrames,stateCoverageScore,transitionDiversityScore,detectedUsefulStates,detectedUsefulTransitions,boundaryProbeCount,outsideReachedCount,outsideTerminalCount,outsideAliveAfterKCount,unsafeOutsideCount,boundarySafetyScore,trajectoryLength,uniqueStateCount,replayLength");

        foreach (ExperimentRecord record in records)
        {
            builder.AppendLine(string.Join(",", new string[]
            {
                Csv(record.timestamp),
                Csv(record.conditionName),
                record.seed.ToString(CultureInfo.InvariantCulture),
                record.success.ToString(),
                Csv(record.failureReason),
                record.generationTimeSeconds.ToString("F3", CultureInfo.InvariantCulture),
                record.totalFitness.ToString("F3", CultureInfo.InvariantCulture),
                record.goalReached.ToString(),
                record.deaths.ToString(CultureInfo.InvariantCulture),
                record.trapContacts.ToString(CultureInfo.InvariantCulture),
                record.outsidePlayAreaFrames.ToString(CultureInfo.InvariantCulture),
                record.stateCoverageScore.ToString("F3", CultureInfo.InvariantCulture),
                record.transitionDiversityScore.ToString("F3", CultureInfo.InvariantCulture),
                Csv(record.detectedUsefulStates),
                Csv(record.detectedUsefulTransitions),
                record.boundaryProbeCount.ToString(CultureInfo.InvariantCulture),
                record.outsideReachedCount.ToString(CultureInfo.InvariantCulture),
                record.outsideTerminalCount.ToString(CultureInfo.InvariantCulture),
                record.outsideAliveAfterKCount.ToString(CultureInfo.InvariantCulture),
                record.unsafeOutsideCount.ToString(CultureInfo.InvariantCulture),
                record.boundarySafetyScore.ToString("F3", CultureInfo.InvariantCulture),
                record.trajectoryLength.ToString(CultureInfo.InvariantCulture),
                record.uniqueStateCount.ToString(CultureInfo.InvariantCulture),
                record.replayLength.ToString(CultureInfo.InvariantCulture)
            }));
        }

        File.WriteAllText(path, builder.ToString());
        return path;
    }

    private string Csv(string value)
    {
        if (value == null) value = "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void PrintAblationSummary(List<ExperimentRecord> records, string csvPath)
    {
        Debug.Log($"[AblationExperiment] CSV exported: {csvPath}");

        foreach (IGrouping<string, ExperimentRecord> group in records.GroupBy(r => r.conditionName))
        {
            int count = group.Count();
            float successRate = count > 0 ? group.Count(r => r.success) / (float)count : 0f;
            float avgFitness = count > 0 ? group.Average(r => r.totalFitness) : 0f;
            float avgStateCoverage = count > 0 ? group.Average(r => r.stateCoverageScore) : 0f;
            float avgTransitionDiversity = count > 0 ? group.Average(r => r.transitionDiversityScore) : 0f;
            float avgUnsafeOutside = count > 0 ? group.Average(r => r.unsafeOutsideCount) : 0f;
            float avgGenerationTime = count > 0 ? group.Average(r => r.generationTimeSeconds) : 0f;

            Debug.Log($"[AblationExperiment:{group.Key}] " +
                $"successRate={successRate:P1}, " +
                $"avgFitness={avgFitness:F2}, " +
                $"avgStateCoverage={avgStateCoverage:F2}, " +
                $"avgTransitionDiversity={avgTransitionDiversity:F2}, " +
                $"avgUnsafeOutsideCount={avgUnsafeOutside:F2}, " +
                $"avgGenerationTime={avgGenerationTime:F2}s");
        }
    }
}
