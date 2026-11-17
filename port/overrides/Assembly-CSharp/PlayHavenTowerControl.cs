// PlayHaven (ads / cross-promotion) removed; kept as an inert singleton for the game's calls.
public class PlayHavenTowerControl : Singleton<PlayHavenTowerControl>
{
	public bool inIAPMode { get; set; }

	public void InvokeContent(string contentType) { }

	public void ClearHistory()
	{
		inIAPMode = false;
	}
}
