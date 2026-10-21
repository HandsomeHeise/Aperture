using System.Text.Json;
using Aperture.Inventory;

if (args.Length == 0) { Console.Error.WriteLine("Usage: Inventory <assembly.dll> [...]"); return 2; }
var inventories = args.Select(ApiInventory.Read).ToArray();
Console.WriteLine(JsonSerializer.Serialize(inventories, new JsonSerializerOptions { WriteIndented = true }));
return 0;
