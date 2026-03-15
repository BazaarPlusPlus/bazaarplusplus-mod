// BepInEx macOS 26 + Apple Silicon Preloader Patcher
//
// Patches BepInEx.Preloader.dll to make it work on macOS 26.
//
// Background:
//   BepInEx's RuntimeFixes call MonoMod.RuntimeDetour.DetourHelper.GetIdentifiable() which
//   returns null on macOS 26's Mono runtime, causing NullReferenceException in the preloader
//   before any plugins can load. These three Apply() methods are patched to no-ops since they
//   only affect console output formatting and debug stack traces — not mod functionality.
//
// Usage:
//   dotnet run -- <path-to-BepInEx.Preloader.dll>
//   dotnet run   (uses default The Bazaar path)
//
// Re-run this after every BepInEx update.

using System;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

var dllPath = args.Length > 0
    ? args[0]
    : Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library/Application Support/Steam/steamapps/common/The Bazaar/BepInEx/core/BepInEx.Preloader.dll"
    );

if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"File not found: {dllPath}");
    Console.Error.WriteLine("Usage: dotnet run -- <path-to-BepInEx.Preloader.dll>");
    Environment.Exit(1);
}

var backupPath = dllPath + ".bak";
var tmpPath = dllPath + ".patched";

File.Copy(dllPath, backupPath, overwrite: true);
Console.WriteLine($"Backed up to {backupPath}");

var module = ModuleDefMD.Load(dllPath);

// These three RuntimeFixes crash on macOS 26 due to GetIdentifiable() returning null.
// Patching them to no-ops allows BepInEx to complete preloader initialization.
var targetTypes = new[]
{
    "BepInEx.Preloader.RuntimeFixes.ConsoleSetOutFix",
    "BepInEx.Preloader.RuntimeFixes.HarmonyInteropFix",
    "BepInEx.Preloader.RuntimeFixes.UnityPatches",
};

var patched = 0;
foreach (var typeName in targetTypes)
{
    var type = module.Types.FirstOrDefault(t => t.FullName == typeName);
    if (type == null) { Console.WriteLine($"SKIP (not found): {typeName}"); continue; }

    var method = type.Methods.FirstOrDefault(m => m.Name == "Apply");
    if (method == null) { Console.WriteLine($"SKIP (no Apply): {typeName}"); continue; }

    Console.WriteLine($"Patching {method.FullName} ({method.Body.Instructions.Count} instructions -> ret)");
    method.Body.Instructions.Clear();
    method.Body.Variables.Clear();
    method.Body.ExceptionHandlers.Clear();
    method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
    patched++;
}

module.Write(tmpPath);
module.Dispose();

File.Move(tmpPath, dllPath, overwrite: true);
Console.WriteLine($"\nDone. {patched}/{targetTypes.Length} methods patched.");
