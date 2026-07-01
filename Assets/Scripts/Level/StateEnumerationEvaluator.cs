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
        public float auxiliaryTieBreakerScore;
        public string diagnostic;
    }

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
            stateCoverageScore = 0f,
            transitionDiversityScore = 0f,
            auxiliaryTieBreakerScore = ScoreAuxiliaryTieBreaker(individual)
        };

        result.totalFitness = result.goalReachScore
            + result.playAreaScore
            + result.survivalScore
            + result.trapScore
            + result.stateCoverageScore
            + result.transitionDiversityScore
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
        builder.Append("StateEnumerationEvaluator StepA");
        builder.Append($" total={result.totalFitness:F2}");
        builder.Append($" goal={result.goalReachScore:F2}");
        builder.Append($" playArea={result.playAreaScore:F2}");
        builder.Append($" survival={result.survivalScore:F2}");
        builder.Append($" trap={result.trapScore:F2}");
        builder.Append($" stateCoverage={result.stateCoverageScore:F2}");
        builder.Append($" transitionDiversity={result.transitionDiversityScore:F2}");
        builder.Append($" tieBreaker={result.auxiliaryTieBreakerScore:F2}");
        builder.Append($" goalReached={individual.goalReached}");
        builder.Append($" deaths={individual.deathCount}");
        builder.Append($" outsidePlayAreaFrames={individual.outsidePlayAreaFrames}");
        builder.Append($" trapContacts={individual.trapContactCount}");
        return builder.ToString();
    }
}
