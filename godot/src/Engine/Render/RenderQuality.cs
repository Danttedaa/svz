using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Collections.Generic;
using Godot;

namespace Svz
{
    /// Only for render-bridge-owned nodes. Cache submitted state to avoid repeated
    /// managed/native calls. Do not use for properties other systems can modify.
    public static class RenderQuality
    {
        private sealed class State
        {
            public bool HasVisible, Visible, HasLayers, HasShadow, HasTransform, HasSize;
            public uint Layers;
            public GeometryInstance3D.ShadowCastingSetting Shadow;
            public Transform3D Transform;
            public Vector2I Size;
            public Dictionary<int, Vector3> BoneScales;
            public Dictionary<int, Transform3D> BonePoses;
            public int MeshRevision, MaterialRevision = -1;
            public ArrayMesh MaterialMesh;
            public Dictionary<int, Material> Materials;
            public Viewport.Msaa Aa = (Viewport.Msaa)(-1);
            public Viewport.AnisotropicFiltering Aniso = (Viewport.AnisotropicFiltering)(-1);
        }
        private static readonly ConditionalWeakTable<GodotObject, State> States = new();
        public static double LastFrameMs { get; private set; }
        public static double LastRenderMs { get; private set; }
        public static string AaStatus => AaActive ? "2x" : "off";
        public static long Submitted { get; private set; }
        public static long Skipped { get; private set; }
        private static long LastSubmitted, LastSkipped;
        private static bool BudgetReduced, AaActive;
        public static void ResetBudget() { BudgetReduced = false; AaActive = false; }
        public static bool ReduceCostBeforeFallback()
        {
            if (Original || BudgetReduced || !AaActive) return false;
            BudgetReduced = true;
            return true;
        }
        private static long RenderStart, FrameStart, RenderTicks, FrameTicks, RenderCount, FrameCount;
        public static void BeginRender() => RenderStart = Stopwatch.GetTimestamp();
        public static void EndRender() { RenderTicks += Stopwatch.GetTimestamp() - RenderStart; RenderCount++; }
        public static void BeginFrame() => FrameStart = Stopwatch.GetTimestamp();
        public static void EndFrame() { FrameTicks += Stopwatch.GetTimestamp() - FrameStart; FrameCount++; }
        public static string Diagnostics()
        {
            long writes = Submitted - LastSubmitted, skipped = Skipped - LastSkipped;
            LastSubmitted = Submitted; LastSkipped = Skipped;
            double renderMs = RenderCount == 0 ? 0 : 1000.0 * RenderTicks / Stopwatch.Frequency / RenderCount;
            double frameMs = FrameCount == 0 ? 0 : 1000.0 * FrameTicks / Stopwatch.Frequency / FrameCount;
            LastFrameMs = frameMs; LastRenderMs = renderMs;
            RenderTicks = FrameTicks = RenderCount = FrameCount = 0;
            return FormattableString.Invariant($"cpu-process={frameMs:0.00}ms cpu-render-sync={renderMs:0.00}ms render-writes={writes} unchanged-skipped={skipped}");
        }
        // Original graphics remain available without another touch gesture.
        private static readonly bool Original = Array.IndexOf(OS.GetCmdlineUserArgs(), "--graphics=original") >= 0;

        public static void Visible(Node3D node, bool value)
        {
            var s = States.GetOrCreateValue(node);
            if (s.HasVisible && s.Visible == value) { Skipped++; return; }
            node.Visible = value; s.Visible = value; s.HasVisible = true; Submitted++;
        }
        public static void Layers(VisualInstance3D node, uint value)
        {
            var s = States.GetOrCreateValue(node);
            if (s.HasLayers && s.Layers == value) { Skipped++; return; }
            node.Layers = value; s.Layers = value; s.HasLayers = true; Submitted++;
        }
        public static void Shadow(GeometryInstance3D node, GeometryInstance3D.ShadowCastingSetting value)
        {
            var s = States.GetOrCreateValue(node);
            if (s.HasShadow && s.Shadow == value) { Skipped++; return; }
            node.CastShadow = value; s.Shadow = value; s.HasShadow = true; Submitted++;
        }
        public static void Transform(Node3D node, Transform3D value)
        {
            var s = States.GetOrCreateValue(node);
            // Exact comparison: small animation and camera changes must survive.
            if (s.HasTransform && s.Transform == value) { Skipped++; return; }
            node.GlobalTransform = value; s.Transform = value; s.HasTransform = true; Submitted++;
        }
        public static void BoneScale(Skeleton3D skeleton, int bone, Vector3 value)
        {
            var s = States.GetOrCreateValue(skeleton);
            s.BoneScales ??= new Dictionary<int, Vector3>();
            if (s.BoneScales.TryGetValue(bone, out var previous) && previous == value)
            { Skipped++; return; }
            skeleton.SetBonePoseScale(bone, value); s.BoneScales[bone] = value; Submitted++;
        }
        public static void BonePose(Skeleton3D skeleton, int bone, ref Transform3D value)
        {
            var s = States.GetOrCreateValue(skeleton);
            s.BonePoses ??= new Dictionary<int, Transform3D>();
            if (s.BonePoses.TryGetValue(bone, out var previous) && previous == value)
            { Skipped++; return; }
            // Godot decomposes the pose in one native call instead of separate
            // position/scale/rotation interop calls and managed decomposition.
            if (WellConditioned(value.Basis)) skeleton.SetBonePose(bone, value);
            else
            {
                // Collapsed or nearly coplanar bones (hidden/dismembered parts, flattened bodies):
                // native orthonormalization fails is_rotation, logs an error with a C# backtrace
                // and substitutes identity. Keep the original port's managed decomposition here.
                skeleton.SetBonePosePosition(bone, value.Origin);
                skeleton.SetBonePoseScale(bone, value.Basis.Scale);
                skeleton.SetBonePoseRotation(bone, value.Basis.Orthonormalized().GetRotationQuaternion());
                Degenerate++;
            }
            s.BonePoses[bone] = value; Submitted++;
        }
        public static long Degenerate { get; private set; }
        /// True when single-precision Gram-Schmidt reliably yields a rotation: comparable axis
        /// lengths and a volume close to that of orthogonal axes. Ordinary animated bones
        /// (rotation, moderate nonuniform scale, reflection, mild shear) pass.
        public static bool WellConditioned(in Basis b)
        {
            // Raw row fields: no property getters or temporary vectors on the per-bone path.
            Vector3 r0 = b.Row0, r1 = b.Row1, r2 = b.Row2;
            float x = r0.X * r0.X + r1.X * r1.X + r2.X * r2.X;
            float y = r0.Y * r0.Y + r1.Y * r1.Y + r2.Y * r2.Y;
            float z = r0.Z * r0.Z + r1.Z * r1.Z + r2.Z * r2.Z;
            float min = Math.Min(x, Math.Min(y, z)), max = Math.Max(x, Math.Max(y, z));
            if (!(min > 1e-12f) || !float.IsFinite(max) || min < 1e-4f * max) return false;
            float det = r0.X * (r1.Y * r2.Z - r1.Z * r2.Y) - r0.Y * (r1.X * r2.Z - r1.Z * r2.X) + r0.Z * (r1.X * r2.Y - r1.Y * r2.X);
            return (double)det * det >= 0.01 * ((double)x * y * z);
        }
        public static void ClearMeshSurfaces(ArrayMesh mesh)
        {
            mesh.ClearSurfaces(); States.GetOrCreateValue(mesh).MeshRevision++;
        }
        public static void ApplyMaterial(MeshInstance3D node, ArrayMesh mesh, int surface, Material material)
        {
            var state = States.GetOrCreateValue(node);
            int revision = States.GetOrCreateValue(mesh).MeshRevision;
            state.Materials ??= new Dictionary<int, Material>();
            if (state.MaterialMesh != mesh || state.MaterialRevision != revision)
            {
                state.Materials.Clear(); state.MaterialMesh = mesh; state.MaterialRevision = revision;
            }
            if (state.Materials.TryGetValue(surface, out var previous) && previous == material) { Skipped++; return; }
            node.SetSurfaceOverrideMaterial(surface, material); state.Materials[surface] = material; Submitted++;
        }
        public static Viewport.Msaa Antialiasing(bool original, int effectiveFps) =>
            original || effectiveFps < 120 ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;

        public static void Size(SubViewport viewport, Vector2I value)
        {
            var s = States.GetOrCreateValue(viewport);
            var aniso = BudgetReduced || FramePacing.EffectiveFps < 120
                ? Viewport.AnisotropicFiltering.Anisotropy2X : Viewport.AnisotropicFiltering.Anisotropy4X;
            if (s.Aniso != aniso) { viewport.AnisotropicFilteringLevel = aniso; s.Aniso = aniso; }
            if (!s.HasSize || s.Size != value)
            { viewport.Size = value; s.Size = value; s.HasSize = true; Submitted++; }
            else Skipped++;
            // Only 3D camera output is smoothed; GUI/text lives outside these
            // viewports. Release MSAA cost when the existing load monitor falls back.
            var aa = Antialiasing(Original || BudgetReduced, FramePacing.EffectiveFps);
            if (s.Aa != aa) { viewport.Msaa3D = aa; s.Aa = aa; Submitted++; }
            AaActive = aa != Viewport.Msaa.Disabled;
        }
    }
}
