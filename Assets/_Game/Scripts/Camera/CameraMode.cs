namespace OpenSkies.CameraSystems
{
    public enum CameraMode
    {
        Chase = 0,
        Cockpit = 1,
        /// <summary>The entry screen's framing of the parked aircraft, posed each frame by the session; never saved as the player's view.</summary>
        Showcase = 2,
    }
}
