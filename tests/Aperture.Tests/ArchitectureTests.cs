using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Aperture.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ProductContainsOnlyFrameworkReferencesAndNoNativeImports()
    {
        using var stream = File.OpenRead(typeof(XboxConsole).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        Assert.Equal("Aperture", metadata.GetString(metadata.GetAssemblyDefinition().Name));
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            Assert.True(name.StartsWith("System.", StringComparison.Ordinal) || name is "System" or "netstandard" or "Microsoft.Win32.Registry", "Unexpected runtime dependency: " + name);
        }
        foreach (var handle in metadata.MethodDefinitions)
            Assert.False(metadata.GetMethodDefinition(handle).Attributes.HasFlag(MethodAttributes.PinvokeImpl), "Product contains a native import.");
    }
}
