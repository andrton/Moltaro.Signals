using System.Runtime.InteropServices;

namespace Moltaro.Signals.Tests.Dependencies;

public sealed class DependencyTests
{
    // An assembly of .NET is one of the shared framework this test runs on; a package or another project of the
    // repository is not there.
    [Fact]
    public void The_library_references_only_assemblies_of_dotnet()
    {
        string frameworkDirectory = RuntimeEnvironment.GetRuntimeDirectory();

        string[] others = typeof(ISignals).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !File.Exists(Path.Combine(frameworkDirectory, name + ".dll")))
            .ToArray();

        Assert.Empty(others);
    }
}
