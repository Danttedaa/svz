// The port has no network access at all: every WWW request completes immediately with an error.
namespace UnityEngine
{
	public sealed partial class WWW
	{
		private string __url = "";

		partial void __Init() { }
		private partial string responseHeadersString => "";
		public partial byte[] bytes => new byte[0];
		public partial int size => 0;
		public partial string error => "offline build: network disabled";
		public partial Texture2D texture => new Texture2D(2, 2);
		public partial bool isDone => true;
		public partial float progress => 1f;
		public partial float uploadProgress => 1f;
		public partial AudioClip oggVorbis => null;
		public partial string url => __url;
		public partial AssetBundle assetBundle => null;
		private ThreadPriority __prio;
		public partial ThreadPriority threadPriority { get => __prio; set => __prio = value; }
		private partial void DestroyWWW(bool cancel) { }
		public partial void InitWWW(string url, byte[] postData, string[] iHeaders) => __url = url ?? "";
		public partial AudioClip GetAudioClip(bool threeD, bool stream) => null;
		public partial void LoadImageIntoTexture(Texture2D tex) { }
		public static partial string GetURL(string url) => "";
		public partial void LoadUnityWeb() { }
	}
}
