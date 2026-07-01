using System.Collections.Generic;
using System.Text;

public static class StateEnumerationEvaluator
{
    public struct EvaluationResult
    {
        public float totalFitness;
        public float goalReachScore;
        public float playAreaScore;
        public float survivalScore;
        public float trapScore;
        public float stateCoverageScore;
        public float transitionDiversityScore;
        public float boundarySafetyScore;
        public float auxiliaryTieBreakerScore;
        public string diagnostic;
    }

    private static readonly Dictionary<Character.CharacterState, float> StateCoverageRewards = new Dictionary<Character.CharacterState, float>
    {
        { Character.CharacterState.Stand, EvaluationWeights.StandStateCoverage },
        { Character.CharacterState.Run, EvaluationWeights.RunStateCoverage },
        { Character.CharacterState.Jump, EvaluationWeights.JumpStateCoverage },
        { Character.CharacterState.GrabLedge, EvaluationWeights.GrabLedgeStateCoverage }
    };

    private static readonly Dictionary<string, float> TransitionDiversityRewards = new Dictionary<string, float>
    {
        { TransitionKey(Character.CharacterState.Run, Character.CharacterState.Jump), EvaluationWeights.RunToJumpTransition },
        { TransitionKey(Character.CharacterState.Jump, Character.CharacterState.Run), EvaluationWeights.JumpToRunTransition },
        { TransitionKey(Character.CharacterState.Jump, Character.CharacterState.Stand), EvaluationWeights.JumpToStandTransition },
        { TransitionKey(Character.CharacterState.Stand, Character.CharacterState.Run), EvaluationWeights.StandToRunTransition },
        { TransitionKey(Character.CharacterState.Run, Character.CharacterState.Stand), EvaluationWeights.RunToStandTransition }
    };

    private static class EvaluationWeights
    {
        public const float GoalReached = 1000f;
        public const float GoalNotReached = 0f;

        public const float NoOutsidePlayAreaFrames = 250f;
        public const float OutsidePlayAreaFramePenalty = -1f;

        public const float NoDeaths = 250f;
        public const float DeathPenalty = -500f;

        public const float NoTrapContacts = 100f;
        public const float TrapContactPenalty = -100f;

        public const float StandStateCoverage = 50f;
        public const float RunStateCoverage = 100f;
        public const float JumpStateCoverage = 150f;
        public const float GrabLedgeStateCoverage = 150f;

        public const float RunToJumpTransition = 200f;
        public const float JumpToRunTransition = 150f;
        public const float JumpToStandTransition = 150f;
        public const float StandToRunTransition = 100f;
        public const float RunToStandTransition = 100f;

        public const float UnsafeOutsidePenalty = -500f;

        public const float TrajectoryFrameTieBreaker = 0.01f;
        public const float ReplayFrameTieBreaker = 0.005f;
    }

    public static EvaluationResult EvaluateIndividual(LevelIndividual individual)
    {
        if (individual == null)
        {
            return new EvaluationResult
            {
                diagnostic = "StateEnumerationEvaluator: null individual"
            };
        }

        EvaluationResult result = new EvaluationResult
        {
            goalReachScore = ScoreGoalReach(individual),
            playAreaScore = ScorePlayArea(individual),
            survivalScore = ScoreSurvival(individual),
            trapScore = ScoreTraps(individual),
            stateCoverageScore = ScoreStateCoverage(individual),
            transitionDiversityScore = ScoreTransitionDiversity(individual),
            boundarySafetyScore = ScoreBoundarySafety(individual),
            auxiliaryTieBreakerScore = ScoreAuxiliaryTieBreaker(individual)
        };

        result.totalFitness = result.goalReachScore
            + result.playAreaScore
            + result.survivalScore
            + result.trapScore
            + result.stateCoverageScore
            + result.transitionDiversityScore
            + result.boundarySafetyScore
            + result.auxiliaryTieBreakerScore;

        result.diagnostic = BuildDiagnostic(individual, result);
        return result;
    }

    private static float ScoreGoalReach(LevelIndividual individual)
    {
        return individual.goalReached
            ? EvaluationWeights.GoalReached
            : EvaluationWeights.GoalNotReached;
    }

    private static float ScorePlayArea(LevelIndividual individual)
    {
        return individual.outsidePlayAreaFrames == 0
            ? EvaluationWeights.NoOutsidePlayAreaFrames
            : individual.outsidePlayAreaFrames * EvaluationWeights.OutsidePlayAreaFramePenalty;
    }

    private static float ScoreSurvival(LevelIndividual individual)
    {
        return individual.deathCount == 0
            ? EvaluationWeights.NoDeaths
            : individual.deathCount * EvaluationWeights.DeathPenalty;
    }

    private static float ScoreTraps(LevelIndividual individual)
    {
        return individual.trapContactCount == 0
            ? EvaluationWeights.NoTrapContacts
            : individual.trapContactCount * EvaluationWeights.TrapContactPenalty;
    }

    private static float ScoreStateCoverage(LevelIndividual individual)
    {
        if (individual.stateCounts == null || individual.stateCounts.Count == 0)
            return 0f;

        float score = 0f;
        foreach (KeyValuePair<Character.CharacterState, float> reward in StateCoverageRewards)
        {
            int observedCount;
            if (individual.stateCounts.TryGetValue(reward.Key, out observedCount) && observedCount > 0)
            {
                score += reward.Value;
            }
        }

        return score;
    }

    private static float ScoreTransitionDiversity(LevelIndividual individual)
    {
        if (individual.stateTransitionCounts == null || individual.stateTransitionCounts.Count == 0)
            return 0f;

        float score = 0f;
        foreach (KeyValuePair<string, float> reward in TransitionDiversityRewards)
        {
            int observedCount;
            if (individual.stateTransitionCounts.TryGetValue(reward.Key, out observedCount) && observedCount > 0)
            {
                score += reward.Value;
            }
        }

        return score;
    }

    private static float ScoreBoundarySafety(LevelIndividual individual)
    {
        return individual.unsafeOutsideCount * EvaluationWeights.UnsafeOutsidePenalty;
    }

    private static float ScoreAuxiliaryTieBreaker(LevelIndividual individual)
    {
        int trajectoryCount = individual.trajectory != null ? individual.trajectory.Count : 0;
        int replayCount = individual.replay != null ? individual.replay.Count : 0;

        return (trajectoryCount * EvaluationWeights.TrajectoryFrameTieBreaker)
            + (replayCount * EvaluationWeights.ReplayFrameTieBreaker);
    }

    private static string BuildDiagnostic(LevelIndividual individual, EvaluationResult result)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("StateEnumerationEvaluator StepC");
        builder.Append($" total={result.totalFitness:F2}");
        builder.Append($" goal={result.goalReachScore:F2}");
        builder.Append($" playArea={result.playAreaScore:F2}");
        builder.Append($" survival={result.survivalScore:F2}");
        builder.Append($" trap={result.trapScore:F2}");
        builder.Append($" stateCoverage={result.stateCoverageScore:F2}");
        builder.Append($" detectedUsefulStates={FormatDetectedUsefulStates(individual)}");
        builder.Append($" transitionDiversity={result.transitionDiversityScore:F2}");
        builder.Append($" detectedUsefulTransitions={FormatDetectedUsefulTransitions(individual)}");
        builder.Append($" boundarySafety={result.boundarySafetyScore:F2}");
        builder.Append($" unsafeOutsideCount={individual.unsafeOutsideCount}");
        builder.Append($" tieBreaker={result.auxiliaryTieBreakerScore:F2}");
        builder.Append($" goalReached={individual.goalReached}");
        builder.Append($" deaths={individual.deathCount}");
        builder.Append($" outsidePlayAreaFrames={individual.outsidePlayAreaFrames}");
        builder.Append($" trapContacts={individual.trapContactCount}");
        return builder.ToString();
    }

    private static string TransitionKey(Character.CharacterState from, Character.CharacterState to)
    {
        return from + "->" + to;
    }

    private static string FormatDetectedUsefulStates(LevelIndividual individual)
    {
        if (individual.stateCounts == null || individual.stateCounts.Count == 0)
            return "none";

        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<Character.CharacterState, float> reward in StateCoverageRewards)
        {
            int observedCount;
            if (individual.stateCounts.TryGetValue(reward.Key, out observedCount) && observedCount > 0)
            {
                if (builder.Length > 0) builder.Append(",");
                builder.Append(reward.Key);
            }
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }

    private static string FormatDetectedUsefulTransitions(LevelIndividual individual)
    {
        if (individual.stateTransitionCounts == null || individual.stateTransitionCounts.Count == 0)
            return "none";

        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<string, float> reward in TransitionDiversityRewards)
        {
            int observedCount;
            if (individual.stateTransitionCounts.TryGetValue(reward.Key, out observedCount) && observedCount > 0)
            {
                if (builder.Length > 0) builder.Append(",");
                builder.Append(reward.Key);
            }
        }

        return builder.Length > 0 ? builder.ToString() : "none";
    }
}
