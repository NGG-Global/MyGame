using UnityEngine;

namespace OpenSkies.Missions
{
    /// <summary>
    /// Mission tuning in one asset, so it survives scene rebuilds (the scene
    /// builder creates it once and never overwrites it). Without an asset, the
    /// defaults in code are used. In its own file: Unity saves a ScriptableObject
    /// asset with its script only when the class has a script file of the same name.
    /// </summary>
    [CreateAssetMenu(menuName = "Open Skies/Mission Configuration", fileName = "MissionConfiguration")]
    public sealed class MissionConfiguration : ScriptableObject
    {
        public TutorialSettings tutorial = new TutorialSettings();
        public LandingChallengeSettings landingChallenge = new LandingChallengeSettings();
        public LandingScoringSettings landingScoring = new LandingScoringSettings();
        public FreeFlightSettings freeFlight = new FreeFlightSettings();
    }
}
