// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// I just think it's ugly.
[assembly: SuppressMessage("Style", "IDE0017:Simplify object initialization", Justification = "<Pending>", Scope = "member", Target = "~M:KerbalCombatSystems.DrawTransform.SetupLine(UnityEngine.LineRenderer,UnityEngine.Color)")]
[assembly: SuppressMessage("Style", "IDE0017:Simplify object initialization", Justification = "<Pending>", Scope = "member", Target = "~M:KerbalCombatSystems.Debug2.Line.CreateLine(UnityEngine.Color,System.Single,System.Single)~UnityEngine.LineRenderer")]

// Not compatible with Unity Object null check.
[assembly: SuppressMessage("Style", "IDE0074:Use compound assignment", Justification = "<Pending>", Scope = "member", Target = "~M:KerbalCombatSystems.ModuleShipController.AddIncoming(KerbalCombatSystems.ModuleWeaponController)")]

