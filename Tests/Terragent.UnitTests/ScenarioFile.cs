using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Terragent.UnitTests;

/// <summary>Hands the scenarios to the harness before any test asks for them.</summary>
// In game the cases come out of the .tmod package, and there is no package here, so the
// same file is copied next to the test assembly and read from disk. Both routes end in
// Scenarios.Read, so the two levels cannot disagree about what a case is.
//
// A module initializer because the case names are a MemberData source, which xUnit reads
// while collecting tests and therefore before any constructor of ours has run.
internal static class ScenarioFile
{
    [ModuleInitializer]
    internal static void Load() =>
        Scenarios.Load(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "scenarios.json")));
}
