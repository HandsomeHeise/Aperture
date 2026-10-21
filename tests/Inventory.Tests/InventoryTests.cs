using Aperture.Inventory;
using Xunit;

namespace Inventory.Tests;

public sealed class InventoryTests
{
    [Fact]
    public void ReadsSignaturesNestedEnumsAndEventsWithoutExecutingCode()
    {
        var data = ApiInventory.Read(typeof(InventoryFixture).Assembly.Location);
        var type = Assert.Single(data.Types, x => x.Name == "Inventory.Tests.InventoryFixture");
        Assert.Contains(type.Members, m => m.Name == "Call" && m.Signature.Contains("System.UInt64[]"));
        Assert.Contains(type.Members, m => m.Name == "Name" && m.Kind == "property");
        Assert.Contains(type.Members, m => m.Name == "Changed" && m.Kind == "event");
        Assert.DoesNotContain(type.Members, m => m.Name == "Hidden");
        var flags = Assert.Single(data.Types, x => x.Name == "Inventory.Tests.InventoryFixture+Mode");
        Assert.Contains(flags.Members, m => m.Name == "High" && m.Constant == "128");
        Assert.DoesNotContain(data.Types, t => t.Name.Contains("PrivateType"));
    }

    [Fact]
    public void InventoryIsDeterministicAndContainsSourceIdentity()
    {
        var path = typeof(InventoryFixture).Assembly.Location;
        var first = System.Text.Json.JsonSerializer.Serialize(ApiInventory.Read(path));
        Assert.Equal(first, System.Text.Json.JsonSerializer.Serialize(ApiInventory.Read(path)));
        var data = ApiInventory.Read(path);
        Assert.Equal("Inventory.Tests", data.Assembly);
        Assert.Equal(64, data.Sha256.Length);
        Assert.NotEmpty(data.Types);
    }
}

public class InventoryFixture
{
    static InventoryFixture() => throw new InvalidOperationException("Metadata inspection must not execute me.");
    public enum Mode { Low = 1, High = 128 }
    private class PrivateType;
    public string Name { get; set; } = "";
    public event Action? Changed { add { } remove { } }
    public ulong[] Call<T>(ref uint address, T argument) => [];
    private void Hidden() { }
}
