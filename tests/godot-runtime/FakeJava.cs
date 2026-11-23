using System.Collections.Generic;
using Godot;

// Stand-ins for the Java objects FramePacing.SendFrameRate walks on Android. Method names and shapes
// mirror Godot 4.7.2's template (GodotActivity.getGodot, Godot.getRenderView, GodotRenderView.getView)
// and android.view (Window.getDecorView, ViewGroup.getChildCount/getChildAt, SurfaceView.getHolder,
// SurfaceHolder.getSurface, Surface.setFrameRate). Like JavaObject, each answers has_java_method;
// `Hidden` simulates a method that does not exist on the device.
public partial class FakeJava : RefCounted
{
	public readonly HashSet<string> Hidden = new HashSet<string>();
	public bool has_java_method(string method) => !Hidden.Contains(method) && HasMethod(method);
}

public partial class FakeSurface : FakeJava
{
	public readonly List<(float hz, int compat, int strategy)> Calls = new List<(float, int, int)>();
	// API 31+ overload. (Godot's Call does not apply C# default arguments, so the API 30 overload is a separate fake.)
	public void setFrameRate(float hz, int compatibility, int strategy) => Calls.Add((hz, compatibility, strategy));
}

/// Android 11 (API 30) Surface: only the 2-argument overload exists.
public partial class FakeSurfaceApi30 : FakeJava
{
	public readonly List<(float hz, int compat)> Calls = new List<(float, int)>();
	public void setFrameRate(float hz, int compatibility) => Calls.Add((hz, compatibility));
}

public partial class FakeHolder : FakeJava
{
	public FakeSurface Surface = new FakeSurface();
	public FakeSurfaceApi30 SurfaceApi30;   // set to simulate Android 11
	public GodotObject getSurface() => SurfaceApi30 != null ? SurfaceApi30 : Surface;
}

public partial class FakeSurfaceView : FakeJava
{
	public FakeHolder Holder = new FakeHolder();
	public GodotObject getHolder() => Holder;
}

public partial class FakeViewGroup : FakeJava
{
	public readonly List<GodotObject> Children = new List<GodotObject>();
	public int getChildCount() => Children.Count;
	public GodotObject getChildAt(int i) => Children[i];
}

public partial class FakeWindow : FakeJava
{
	public FakeViewGroup Decor = new FakeViewGroup();
	public GodotObject getDecorView() => Decor;
}

public partial class FakeRenderView : FakeJava
{
	public FakeSurfaceView View;
	public GodotObject getView() => View;
}

public partial class FakeGodot : FakeJava
{
	public FakeRenderView RenderView = new FakeRenderView();
	public GodotObject getRenderView() => RenderView;
}

public partial class FakeActivity : FakeJava
{
	public FakeGodot Godot = new FakeGodot();
	public FakeWindow Window = new FakeWindow();
	public GodotObject getGodot() => Godot;
	public GodotObject getWindow() => Window;

	/// Godot's layout: DecorView > FrameLayout > SurfaceView, with the same SurfaceView behind getRenderView().
	public static FakeActivity Standard(out FakeSurfaceView surfaceView)
	{
		var a = new FakeActivity();
		surfaceView = new FakeSurfaceView();
		a.Godot.RenderView.View = surfaceView;
		var content = new FakeViewGroup();
		content.Children.Add(new FakeViewGroup());   // an empty sibling layout
		content.Children.Add(surfaceView);
		a.Window.Decor.Children.Add(content);
		return a;
	}
}
