// SvZ port: every ad, tracking, analytics, push-notification, wallet and in-app-purchase SDK of the
// original game (Burstly/AdMob/InMobi/Millennial/Greystripe, Amazon Ads, Tapjoy, PlayHaven, Flurry,
// WildTangent BrandBoost, Glu GWallet, Google/Amazon billing) was deleted. These classes only keep
// the API surface the game calls; none of them touch the network or collect anything.
using UnityEngine;

public static class AAds
{
	public static void Init() { }

	public static class Tapjoy
	{
		public static void Init(string handler, string appId, string secret) { }
		public static void ActionComplete(string id) { }
		public static void ClearPoints() { }
		public static void GetPoints() { }
		public static void Launch(bool showFA = true) { }
		public static void ShowFeaturedApp() { }
	}

	public static class PlayHaven
	{
		public static void Init(string handler, string token, string secret) { }
		public static void ReportResolution(string resolution) { }
		public static void Show(string placement) { }
	}

	public static class Amazon
	{
		public static void Init(string appKey) { }
	}
}

public static class AFlurry
{
	public static void SetDebug(bool debug) { }
	public static void StartSession(string id) { }
	public static void EndSession() { }
	public static void LogEvent(string id, params string[] kv) { }
}

public static class AJavaTools
{
	public static void LogFlurryOBB() { }
	public static void LogFlurryDataRestored() { }
	public static bool IsRooted() => false;
	public static bool IsTablet() => Screen.width >= 1600 || Screen.height >= 1600;
	public static string GetProperty(string key, string def) => def;
	public static string GetBuildTag() => "SvZ port (Godot, arm64, no ads)";
	public static void DataChanged() { }
}

public static class ANotificationManager
{
	public static void Init(bool enable, bool debug, string url, string sku) { }
	public static void ClearScheduledNotifications() { }
	public static void ClearActiveNotifications() { }
	public static void ScheduleNotificationSecFromNow(int seconds, string message, string data) { }
}

public static class AInAppPurchase
{
	// the store screen only lists packs when billing is available; packs are granted for free (PurchaseCurrencyDialog)
	public static bool BillingSupported { get; set; } = true;
	public static bool SubscriptionSupported { get; set; }
	public static string UserID { get; set; } = string.Empty;
	public static bool RequestedIAP { get; set; }
	public static void Init(string handler, string store, bool debug) { }
	public static void RequestPendingPurchases() { }
	public static void RequestPurchase(string sku, string type) { }
	public static void ConfirmPurchase(string token) { }
	public static void Destroy() { }
}

public static class GWallet
{
	public static bool IsDeviceSupported() => false;
	public static bool IsSubscriber() => false;
	public static void Subscribe(string sku, string userId) { }
}

public class GWSubscriptionRecommendation_Unity
{
	public string m_storeSkuCode = string.Empty;
	public string m_planName = string.Empty;
}

// Without a wallet the game keeps gems/coins in its own save file (Profile falls back when these fail).
public class GWalletHelper : MonoBehaviour
{
	public static GWSubscriptionRecommendation_Unity[] subscription_recommendations = new GWSubscriptionRecommendation_Unity[0];
	public static int ggncount;
	public static void SetupGWallet(string sku) { }
	public static void SetupPrestitial(string a, string b, string c, string d, string e, string f) { }
	public static void SetupGGN(string src, string img, string badge, string alt = "") { }
	public static bool IsSubscriptionRecommendationAvailable() => false;
	public static int GetBalance() => -1;
	public static bool AddCurrency(int amount, string type, string desc) => false;
	public static bool SubtractCurrency(int amount, string type, string desc) => false;
	public static bool AddSoftCurrency(int amount, string type, string desc) => false;
	public static bool SubtractSoftCurrency(int amount, string type, string desc) => false;
	public static void ShowAd(string location, int width, int height, int gravity) { }
	public static void HideAd() { }
	public static void GoVIP() { }
	public static void LaunchGGN() { }
	public static void LaunchGGNBenefits() { }
}

public class IAPListenerScript : MonoBehaviour
{
}
