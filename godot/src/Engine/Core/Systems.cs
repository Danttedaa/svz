// Temporary empty systems; each gets its own file when implemented.
namespace Svz
{
	public static partial class Particles { public static void Update(float dt) { } }
	public static class ImGui { public static void Frame() { } }
	public static class Primitives { public static UnityEngine.Mesh Get(UnityEngine.PrimitiveType t) => null; }
}
namespace UnityEngine
{
	public partial class Component { internal virtual void __OnActiveChanged() { } }
}
